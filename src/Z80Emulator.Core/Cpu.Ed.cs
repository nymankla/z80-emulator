namespace Z80Emulator.Core;

public sealed partial class Cpu
{
    private static readonly byte[] ImTable = { 0, 0, 1, 2, 0, 0, 1, 2 };

    private int ExecuteEd()
    {
        byte opcode = FetchByte();
        BumpR();
        int x = opcode >> 6;
        int y = (opcode >> 3) & 7;
        int z = opcode & 7;
        int p = y >> 1;
        int q = y & 1;

        if (x == 1) return ExecuteEdX1(z, y, p, q);
        if (x == 2) return ExecuteEdBlock(z, y);
        return 8; // Undefined ED opcode: behaves as an 8 T-state NOP.
    }

    private int ExecuteEdX1(int z, int y, int p, int q)
    {
        switch (z)
        {
            case 0:
                {
                    byte value = _io.ReadPort(BC);
                    if (y != 6) WriteR8(y, IndexMode.None, value);
                    F = (byte)(F & Flags.Carry);
                    if (value == 0) F |= Flags.Zero;
                    if ((value & 0x80) != 0) F |= Flags.Sign;
                    if (Flags.IsEvenParity(value)) F |= Flags.ParityOverflow;
                    F |= Flags.XyBitsOf(value);
                    return 12;
                }
            case 1:
                _io.WritePort(BC, y == 6 ? (byte)0 : ReadR8(y, IndexMode.None));
                return 12;
            case 2:
                HL = q == 0 ? Sbc16(HL, GetRp(p, IndexMode.None)) : Adc16(HL, GetRp(p, IndexMode.None));
                return 15;
            case 3:
                if (q == 0) WriteWord(FetchWord(), GetRp(p, IndexMode.None));
                else SetRp(p, IndexMode.None, ReadWord(FetchWord()));
                return 20;
            case 4:
                { byte result = Sub8(0, A, false); A = result; return 8; } // NEG
            case 5:
                if (y == 1) { PC = PopWord(); } // RETI
                else { IFF1 = IFF2; PC = PopWord(); } // RETN
                return 14;
            case 6:
                IM = ImTable[y];
                return 8;
            default: // z == 7
                return ExecuteEdMisc(y);
        }
    }

    private int ExecuteEdMisc(int y)
    {
        switch (y)
        {
            case 0: I = A; return 9;
            case 1: R = A; return 9;
            case 2:
                A = I;
                F = (byte)(F & Flags.Carry);
                if (A == 0) F |= Flags.Zero;
                if ((A & 0x80) != 0) F |= Flags.Sign;
                if (IFF2) F |= Flags.ParityOverflow;
                F |= Flags.XyBitsOf(A);
                return 9;
            case 3:
                A = R;
                F = (byte)(F & Flags.Carry);
                if (A == 0) F |= Flags.Zero;
                if ((A & 0x80) != 0) F |= Flags.Sign;
                if (IFF2) F |= Flags.ParityOverflow;
                F |= Flags.XyBitsOf(A);
                return 9;
            case 4: Rrd(); return 18;
            case 5: Rld(); return 18;
            default: return 8; // undocumented ED NOPs
        }
    }

    private void Rrd()
    {
        byte hl = _memory.ReadByte(HL);
        byte newA = (byte)((A & 0xF0) | (hl & 0x0F));
        byte newHl = (byte)(((A & 0x0F) << 4) | (hl >> 4));
        _memory.WriteByte(HL, newHl);
        A = newA;
        byte carry = (byte)(F & Flags.Carry);
        SetSZXY(A);
        F = (byte)((F & ~(Flags.AddSubtract | Flags.HalfCarry | Flags.ParityOverflow | Flags.Carry)) | carry);
        if (Flags.IsEvenParity(A)) F |= Flags.ParityOverflow;
    }

    private void Rld()
    {
        byte hl = _memory.ReadByte(HL);
        byte newA = (byte)((A & 0xF0) | (hl >> 4));
        byte newHl = (byte)(((hl & 0x0F) << 4) | (A & 0x0F));
        _memory.WriteByte(HL, newHl);
        A = newA;
        byte carry = (byte)(F & Flags.Carry);
        SetSZXY(A);
        F = (byte)((F & ~(Flags.AddSubtract | Flags.HalfCarry | Flags.ParityOverflow | Flags.Carry)) | carry);
        if (Flags.IsEvenParity(A)) F |= Flags.ParityOverflow;
    }

    private int ExecuteEdBlock(int z, int y)
    {
        if (y < 4 || z > 3) return 8; // undefined ED opcode

        bool increment = y == 4 || y == 6;
        bool repeat = y == 6 || y == 7;

        return z switch
        {
            0 => BlockLoad(increment, repeat),
            1 => BlockCompare(increment, repeat),
            2 => BlockIn(increment, repeat),
            _ => BlockOut(increment, repeat)
        };
    }

    private void StepHlDe(bool increment)
    {
        if (increment) { HL++; DE++; } else { HL--; DE--; }
    }

    private int BlockLoad(bool increment, bool repeat)
    {
        byte value = _memory.ReadByte(HL);
        _memory.WriteByte(DE, value);
        StepHlDe(increment);
        BC--;

        byte n = (byte)(value + A);
        F = (byte)(F & (Flags.Sign | Flags.Zero | Flags.Carry));
        if ((n & 0x02) != 0) F |= Flags.Y;
        if ((n & 0x08) != 0) F |= Flags.X;
        if (BC != 0) F |= Flags.ParityOverflow;

        if (repeat && BC != 0) { PC -= 2; return 21; }
        return 16;
    }

    private int BlockCompare(bool increment, bool repeat)
    {
        byte value = _memory.ReadByte(HL);
        int result = A - value;
        bool halfCarry = (A & 0x0F) < (value & 0x0F);
        if (increment) HL++; else HL--;
        BC--;

        byte flags = (byte)(F & Flags.Carry);
        flags |= Flags.AddSubtract;
        if (halfCarry) flags |= Flags.HalfCarry;
        if ((byte)result == 0) flags |= Flags.Zero;
        if ((result & 0x80) != 0) flags |= Flags.Sign;
        if (BC != 0) flags |= Flags.ParityOverflow;
        int n = result - (halfCarry ? 1 : 0);
        if ((n & 0x02) != 0) flags |= Flags.Y;
        if ((n & 0x08) != 0) flags |= Flags.X;
        F = flags;

        bool zero = (F & Flags.Zero) != 0;
        if (repeat && BC != 0 && !zero) { PC -= 2; return 21; }
        return 16;
    }

    private int BlockIn(bool increment, bool repeat)
    {
        byte value = _io.ReadPort(BC);
        _memory.WriteByte(HL, value);
        if (increment) HL++; else HL--;
        B = (byte)(B - 1);

        F = (byte)(F & Flags.Carry);
        if (B == 0) F |= Flags.Zero;
        if ((B & 0x80) != 0) F |= Flags.Sign;
        if ((value & 0x80) != 0) F |= Flags.AddSubtract;
        F |= Flags.XyBitsOf(B);

        if (repeat && B != 0) { PC -= 2; return 21; }
        return 16;
    }

    private int BlockOut(bool increment, bool repeat)
    {
        byte value = _memory.ReadByte(HL);
        _io.WritePort(BC, value);
        if (increment) HL++; else HL--;
        B = (byte)(B - 1);

        F = (byte)(F & Flags.Carry);
        if (B == 0) F |= Flags.Zero;
        if ((B & 0x80) != 0) F |= Flags.Sign;
        if ((value & 0x80) != 0) F |= Flags.AddSubtract;
        F |= Flags.XyBitsOf(B);

        if (repeat && B != 0) { PC -= 2; return 21; }
        return 16;
    }
}
