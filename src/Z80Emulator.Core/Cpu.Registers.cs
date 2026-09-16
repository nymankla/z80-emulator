namespace Z80Emulator.Core;

public sealed partial class Cpu
{
    /// <summary>
    /// Resolves an (HL)/(IX+d)/(IY+d) memory operand for the current instruction,
    /// fetching and consuming the displacement byte from the instruction stream
    /// when <paramref name="mode"/> is indexed. Must be called at most once per
    /// instruction (the displacement byte is only present once in the stream).
    /// </summary>
    private ushort ResolveHlAddress(IndexMode mode)
    {
        return mode switch
        {
            IndexMode.None => HL,
            IndexMode.IX => (ushort)(IX + FetchSignedByte()),
            IndexMode.IY => (ushort)(IY + FetchSignedByte()),
            _ => HL
        };
    }

    /// <summary>Same as <see cref="ResolveHlAddress"/> but for the DD CB/FD CB form, where the
    /// displacement has already been fetched ahead of the opcode byte.</summary>
    private ushort IndexedAddress(IndexMode mode, sbyte displacement) => mode switch
    {
        IndexMode.IX => (ushort)(IX + displacement),
        IndexMode.IY => (ushort)(IY + displacement),
        _ => HL
    };

    private ushort GetHlLike(IndexMode mode) => mode switch
    {
        IndexMode.IX => IX,
        IndexMode.IY => IY,
        _ => HL
    };

    private void SetHlLike(IndexMode mode, ushort value)
    {
        switch (mode)
        {
            case IndexMode.IX: IX = value; break;
            case IndexMode.IY: IY = value; break;
            default: HL = value; break;
        }
    }

    /// <summary>
    /// Reads one of the eight 3-bit-encoded 8-bit register operands (B,C,D,E,H,L,(HL),A).
    /// Under a DD/FD prefix, H and L are redirected to the undocumented IXH/IXL/IYH/IYL
    /// half-registers, and (HL) becomes (IX+d)/(IY+d).
    /// </summary>
    private byte ReadR8(int code, IndexMode mode)
    {
        switch (code)
        {
            case 0: return B;
            case 1: return C;
            case 2: return D;
            case 3: return E;
            case 4: return mode == IndexMode.None ? H : (byte)(GetHlLike(mode) >> 8);
            case 5: return mode == IndexMode.None ? L : (byte)GetHlLike(mode);
            case 6: return _memory.ReadByte(ResolveHlAddress(mode));
            case 7: return A;
            default: throw new ArgumentOutOfRangeException(nameof(code));
        }
    }

    private void WriteR8(int code, IndexMode mode, byte value)
    {
        switch (code)
        {
            case 0: B = value; break;
            case 1: C = value; break;
            case 2: D = value; break;
            case 3: E = value; break;
            case 4:
                if (mode == IndexMode.None) H = value;
                else SetHlLike(mode, (ushort)((GetHlLike(mode) & 0x00FF) | (value << 8)));
                break;
            case 5:
                if (mode == IndexMode.None) L = value;
                else SetHlLike(mode, (ushort)((GetHlLike(mode) & 0xFF00) | value));
                break;
            case 6: _memory.WriteByte(ResolveHlAddress(mode), value); break;
            case 7: A = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(code));
        }
    }

    /// <summary>The "dd"/"ss" register-pair table: BC, DE, HL(or IX/IY), SP.</summary>
    private ushort GetRp(int code, IndexMode mode) => code switch
    {
        0 => BC,
        1 => DE,
        2 => GetHlLike(mode),
        3 => SP,
        _ => throw new ArgumentOutOfRangeException(nameof(code))
    };

    private void SetRp(int code, IndexMode mode, ushort value)
    {
        switch (code)
        {
            case 0: BC = value; break;
            case 1: DE = value; break;
            case 2: SetHlLike(mode, value); break;
            case 3: SP = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(code));
        }
    }

    /// <summary>The "qq" register-pair table used by PUSH/POP: BC, DE, HL(or IX/IY), AF.</summary>
    private ushort GetQq(int code, IndexMode mode) => code switch
    {
        0 => BC,
        1 => DE,
        2 => GetHlLike(mode),
        3 => AF,
        _ => throw new ArgumentOutOfRangeException(nameof(code))
    };

    private void SetQq(int code, IndexMode mode, ushort value)
    {
        switch (code)
        {
            case 0: BC = value; break;
            case 1: DE = value; break;
            case 2: SetHlLike(mode, value); break;
            case 3: AF = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(code));
        }
    }
}
