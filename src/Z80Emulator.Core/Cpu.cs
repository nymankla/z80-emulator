namespace Z80Emulator.Core;

/// <summary>
/// A Zilog Z80 CPU core: the documented instruction set, registers and interrupt
/// modes, interpreted instruction-by-instruction against caller-supplied memory
/// and I/O. No peripherals, clock, or host machine are modeled here.
/// </summary>
public sealed partial class Cpu
{
    private readonly IMemory _memory;
    private readonly IIoDevice _io;

    // Main register set.
    public byte A, F, B, C, D, E, H, L;

    // Shadow (alternate) register set, swapped in by EXX / EX AF,AF'.
    public byte A_, F_, B_, C_, D_, E_, H_, L_;

    public ushort IX, IY, SP, PC;

    /// <summary>Interrupt vector base register.</summary>
    public byte I;

    /// <summary>Memory refresh register; increments once per opcode fetch.</summary>
    public byte R;

    public bool IFF1, IFF2;

    /// <summary>Interrupt mode: 0, 1, or 2.</summary>
    public byte IM;

    public bool Halted { get; private set; }

    private bool _nmiPending;
    private bool _intPending;
    private byte _intDataBusValue;

    public Cpu(IMemory memory, IIoDevice? io = null)
    {
        _memory = memory;
        _io = io ?? NullIoDevice.Instance;
        Reset();
    }

    public void Reset()
    {
        A = F = B = C = D = E = H = L = 0;
        A_ = F_ = B_ = C_ = D_ = E_ = H_ = L_ = 0;
        IX = IY = 0;
        SP = 0xFFFF;
        PC = 0;
        I = 0;
        R = 0;
        IFF1 = IFF2 = false;
        IM = 0;
        Halted = false;
        _nmiPending = false;
        _intPending = false;
    }

    public ushort AF { get => (ushort)((A << 8) | F); set { A = (byte)(value >> 8); F = (byte)value; } }
    public ushort BC { get => (ushort)((B << 8) | C); set { B = (byte)(value >> 8); C = (byte)value; } }
    public ushort DE { get => (ushort)((D << 8) | E); set { D = (byte)(value >> 8); E = (byte)value; } }
    public ushort HL { get => (ushort)((H << 8) | L); set { H = (byte)(value >> 8); L = (byte)value; } }

    /// <summary>Raises a non-maskable interrupt, taken at the start of the next <see cref="Step"/>.</summary>
    public void RaiseNmi() => _nmiPending = true;

    /// <summary>
    /// Raises a maskable interrupt. <paramref name="dataBusValue"/> is only used in
    /// interrupt mode 0, where it is interpreted as the instruction opcode placed on
    /// the data bus by the interrupting device (a single-byte RST is typical).
    /// </summary>
    public void RaiseInterrupt(byte dataBusValue = 0xFF)
    {
        _intPending = true;
        _intDataBusValue = dataBusValue;
    }

    /// <summary>Executes exactly one instruction (or one halted no-op cycle, or a pending interrupt). Returns T-states elapsed.</summary>
    public int Step()
    {
        if (_nmiPending)
        {
            _nmiPending = false;
            Halted = false;
            IFF2 = IFF1;
            IFF1 = false;
            PushWord(PC);
            PC = 0x0066;
            return 11;
        }

        if (_intPending && IFF1)
        {
            _intPending = false;
            Halted = false;
            IFF1 = IFF2 = false;
            return HandleMaskableInterrupt();
        }
        _intPending = false;

        if (Halted)
        {
            BumpR();
            return 4;
        }

        return ExecuteOne();
    }

    private int HandleMaskableInterrupt()
    {
        switch (IM)
        {
            case 0:
                // Simplified IM 0: treat the supplied data-bus byte as a one-byte
                // instruction (in practice almost always an RST xx).
                return Execute(_intDataBusValue, IndexMode.None) + 2;
            case 1:
                PushWord(PC);
                PC = 0x0038;
                return 13;
            default: // IM 2
                PushWord(PC);
                ushort vector = (ushort)((I << 8) | _intDataBusValue);
                PC = ReadWord(vector);
                return 19;
        }
    }

    private byte FetchByte()
    {
        byte value = _memory.ReadByte(PC);
        PC++;
        return value;
    }

    private sbyte FetchSignedByte() => (sbyte)FetchByte();

    private ushort FetchWord()
    {
        byte lo = FetchByte();
        byte hi = FetchByte();
        return (ushort)((hi << 8) | lo);
    }

    private ushort ReadWord(ushort address)
    {
        byte lo = _memory.ReadByte(address);
        byte hi = _memory.ReadByte((ushort)(address + 1));
        return (ushort)((hi << 8) | lo);
    }

    private void WriteWord(ushort address, ushort value)
    {
        _memory.WriteByte(address, (byte)value);
        _memory.WriteByte((ushort)(address + 1), (byte)(value >> 8));
    }

    private void PushWord(ushort value)
    {
        SP--;
        _memory.WriteByte(SP, (byte)(value >> 8));
        SP--;
        _memory.WriteByte(SP, (byte)value);
    }

    private ushort PopWord()
    {
        byte lo = _memory.ReadByte(SP);
        SP++;
        byte hi = _memory.ReadByte(SP);
        SP++;
        return (ushort)((hi << 8) | lo);
    }

    private void BumpR() => R = (byte)((R & 0x80) | ((R + 1) & 0x7F));

    private int ExecuteOne()
    {
        byte opcode = FetchByte();
        BumpR();

        switch (opcode)
        {
            case 0xDD:
                return ExecutePrefixed(IndexMode.IX);
            case 0xFD:
                return ExecutePrefixed(IndexMode.IY);
            case 0xCB:
                return 4 + ExecuteCb(IndexMode.None, 0);
            case 0xED:
                BumpR();
                return ExecuteEd();
            default:
                return Execute(opcode, IndexMode.None);
        }
    }

    /// <summary>Handles a DD/FD prefix, including runs of repeated prefixes and the DDCB/FDCB form.</summary>
    private int ExecutePrefixed(IndexMode mode)
    {
        byte opcode = FetchByte();
        while (opcode == 0xDD || opcode == 0xFD)
        {
            // A repeated prefix just overrides the index register and costs another 4 T-states.
            mode = opcode == 0xDD ? IndexMode.IX : IndexMode.IY;
            BumpR();
            opcode = FetchByte();
        }

        if (opcode == 0xCB)
        {
            sbyte displacement = FetchSignedByte();
            return 8 + ExecuteCb(mode, displacement);
        }

        if (opcode == 0xED)
        {
            // DD ED / FD ED behaves as a plain ED-prefixed instruction (the index prefix is ignored).
            BumpR();
            return 4 + ExecuteEd();
        }

        BumpR();
        return 4 + Execute(opcode, mode);
    }
}
