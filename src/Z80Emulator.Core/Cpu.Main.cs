namespace Z80Emulator.Core;

public sealed partial class Cpu
{
    private bool EvaluateCondition(int cc) => cc switch
    {
        0 => (F & Flags.Zero) == 0,          // NZ
        1 => (F & Flags.Zero) != 0,          // Z
        2 => (F & Flags.Carry) == 0,         // NC
        3 => (F & Flags.Carry) != 0,         // C
        4 => (F & Flags.ParityOverflow) == 0,// PO
        5 => (F & Flags.ParityOverflow) != 0,// PE
        6 => (F & Flags.Sign) == 0,          // P
        7 => (F & Flags.Sign) != 0,          // M
        _ => false
    };

    private byte ApplyAlu(int op, byte a, byte b) => op switch
    {
        0 => Add8(a, b, false),
        1 => Add8(a, b, true),
        2 => Sub8(a, b, false),
        3 => Sub8(a, b, true),
        4 => And8(a, b),
        5 => Xor8(a, b),
        6 => Or8(a, b),
        7 => CpAndReturnA(a, b),
        _ => a
    };

    private byte CpAndReturnA(byte a, byte b)
    {
        Cp8(a, b);
        return a; // CP never stores its result.
    }

    /// <summary>
    /// Dispatches one unprefixed opcode using the standard x/y/z/p/q decomposition
    /// (see z80.info's "Decoding Z80 opcodes"). Called for the plain instruction
    /// stream and, with mode set, for the DD/FD-redirected form of the same table.
    /// </summary>
    private int Execute(byte opcode, IndexMode mode)
    {
        int x = opcode >> 6;
        int y = (opcode >> 3) & 7;
        int z = opcode & 7;
        int p = y >> 1;
        int q = y & 1;

        switch (x)
        {
            case 0: return ExecuteX0(z, y, p, q, mode);
            case 1: return ExecuteX1(z, y, mode);
            case 2: return ExecuteX2(z, y, mode);
            default: return ExecuteX3(z, y, p, q, mode);
        }
    }

    private int ExecuteX0(int z, int y, int p, int q, IndexMode mode)
    {
        switch (z)
        {
            case 0:
                switch (y)
                {
                    case 0: return 4; // NOP
                    case 1: ExAfAf(); return 4;
                    case 2: return Djnz();
                    case 3: JumpRelative(FetchSignedByte()); return 12;
                    default:
                        sbyte offset = FetchSignedByte();
                        if (EvaluateCondition(y - 4)) { JumpRelative(offset); return 12; }
                        return 7;
                }
            case 1:
                if (q == 0) { SetRp(p, mode, FetchWord()); return 10; }
                else { SetHlLike(mode, Add16(GetHlLike(mode), GetRp(p, mode))); return 11; }
            case 2:
                return LoadIndirectAccumulatorOrHl(p, q, mode);
            case 3:
                if (q == 0) SetRp(p, mode, (ushort)(GetRp(p, mode) + 1));
                else SetRp(p, mode, (ushort)(GetRp(p, mode) - 1));
                return 6;
            case 4:
                return IncR(y, mode);
            case 5:
                return DecR(y, mode);
            case 6:
                return LoadRImmediate(y, mode);
            default: // z == 7
                return y switch
                {
                    0 => Rlca(),
                    1 => Rrca(),
                    2 => Rla(),
                    3 => Rra(),
                    4 => DaaOp(),
                    5 => Cpl(),
                    6 => Scf(),
                    _ => Ccf()
                };
        }
    }

    private void ExAfAf()
    {
        (A, A_) = (A_, A);
        (F, F_) = (F_, F);
    }

    private int Djnz()
    {
        sbyte offset = FetchSignedByte();
        B = (byte)(B - 1);
        if (B != 0) { JumpRelative(offset); return 13; }
        return 8;
    }

    private void JumpRelative(sbyte offset) => PC = (ushort)(PC + offset);

    private int LoadIndirectAccumulatorOrHl(int p, int q, IndexMode mode)
    {
        if (q == 0)
        {
            switch (p)
            {
                case 0: _memory.WriteByte(BC, A); return 7;
                case 1: _memory.WriteByte(DE, A); return 7;
                case 2: WriteWord(FetchWord(), GetHlLike(mode)); return 16;
                default: _memory.WriteByte(FetchWord(), A); return 13;
            }
        }
        switch (p)
        {
            case 0: A = _memory.ReadByte(BC); return 7;
            case 1: A = _memory.ReadByte(DE); return 7;
            case 2: SetHlLike(mode, ReadWord(FetchWord())); return 16;
            default: A = _memory.ReadByte(FetchWord()); return 13;
        }
    }

    private int IncR(int y, IndexMode mode)
    {
        if (y == 6)
        {
            ushort addr = ResolveHlAddress(mode);
            byte v = Inc8(_memory.ReadByte(addr));
            _memory.WriteByte(addr, v);
            return mode == IndexMode.None ? 11 : 23;
        }
        WriteR8(y, mode, Inc8(ReadR8(y, mode)));
        return 4;
    }

    private int DecR(int y, IndexMode mode)
    {
        if (y == 6)
        {
            ushort addr = ResolveHlAddress(mode);
            byte v = Dec8(_memory.ReadByte(addr));
            _memory.WriteByte(addr, v);
            return mode == IndexMode.None ? 11 : 23;
        }
        WriteR8(y, mode, Dec8(ReadR8(y, mode)));
        return 4;
    }

    private int LoadRImmediate(int y, IndexMode mode)
    {
        if (y == 6)
        {
            ushort addr = ResolveHlAddress(mode);
            byte n = FetchByte();
            _memory.WriteByte(addr, n);
            return mode == IndexMode.None ? 10 : 19;
        }
        WriteR8(y, mode, FetchByte());
        return mode == IndexMode.None ? 7 : 11;
    }

    private int Rlca() { RestoreSzpAfterRotate(() => A = Rlc8(A)); return 4; }
    private int Rrca() { RestoreSzpAfterRotate(() => A = Rrc8(A)); return 4; }
    private int Rla() { RestoreSzpAfterRotate(() => A = Rl8(A)); return 4; }
    private int Rra() { RestoreSzpAfterRotate(() => A = Rr8(A)); return 4; }

    /// <summary>
    /// RLCA/RRCA/RLA/RRA only affect C/N/H (and the undocumented X/Y, taken from A);
    /// S/Z/P are preserved from before the rotate, unlike the CB-prefixed RLC/RRC/RL/RR.
    /// </summary>
    private void RestoreSzpAfterRotate(Action rotate)
    {
        byte preserved = (byte)(F & (Flags.Sign | Flags.Zero | Flags.ParityOverflow));
        rotate();
        F = (byte)((F & ~(Flags.Sign | Flags.Zero | Flags.ParityOverflow)) | preserved);
    }

    private int DaaOp() { Daa(); return 4; }

    private int Cpl()
    {
        A = (byte)~A;
        F = (byte)(F & (Flags.Sign | Flags.Zero | Flags.ParityOverflow | Flags.Carry));
        F |= Flags.AddSubtract | Flags.HalfCarry;
        F |= Flags.XyBitsOf(A);
        return 4;
    }

    private int Scf()
    {
        F = (byte)(F & (Flags.Sign | Flags.Zero | Flags.ParityOverflow));
        F |= Flags.Carry;
        F |= Flags.XyBitsOf(A);
        return 4;
    }

    private int Ccf()
    {
        bool oldCarry = (F & Flags.Carry) != 0;
        F = (byte)(F & (Flags.Sign | Flags.Zero | Flags.ParityOverflow));
        if (oldCarry) F |= Flags.HalfCarry; else F |= Flags.Carry;
        F |= Flags.XyBitsOf(A);
        return 4;
    }

    private int ExecuteX1(int z, int y, IndexMode mode)
    {
        if (y == 6 && z == 6)
        {
            Halted = true;
            PC--; // Re-executes the (implicit) NOP at the same address each cycle while halted.
            return 4;
        }

        // When one side of the move is the (HL)/(IX+d)/(IY+d) memory operand (register
        // code 6), the OTHER side's H/L is the real H/L register, not IXH/IXL/IYH/IYL —
        // e.g. DD 66 d (LD H,(IX+d)) loads the real H. Only a pure register-to-register
        // move (neither side is code 6) redirects H/L to the indexed half-registers.
        IndexMode srcMode = y == 6 ? IndexMode.None : mode;
        IndexMode dstMode = z == 6 ? IndexMode.None : mode;

        byte value = ReadR8(z, srcMode);
        WriteR8(y, dstMode, value);
        if (z == 6 || y == 6) return mode == IndexMode.None ? 7 : 19;
        return 4;
    }

    private int ExecuteX2(int z, int y, IndexMode mode)
    {
        byte operand = ReadR8(z, mode);
        A = ApplyAlu(y, A, operand);
        if (z == 6) return mode == IndexMode.None ? 7 : 19;
        return 4;
    }

    private int ExecuteX3(int z, int y, int p, int q, IndexMode mode)
    {
        switch (z)
        {
            case 0:
                if (EvaluateCondition(y)) { PC = PopWord(); return 11; }
                return 5;
            case 1:
                if (q == 0) { SetQq(p, mode, PopWord()); return mode == IndexMode.None || p != 2 ? 10 : 14; }
                switch (p)
                {
                    case 0: PC = PopWord(); return 10;
                    case 1: ExExx(); return 4;
                    case 2: PC = GetHlLike(mode); return 4;
                    default: SP = GetHlLike(mode); return 6;
                }
            case 2:
                {
                    ushort target = FetchWord();
                    if (EvaluateCondition(y)) PC = target;
                    return 10;
                }
            case 3:
                return ExecuteX3Z3(y, mode);
            case 4:
                {
                    ushort target = FetchWord();
                    if (EvaluateCondition(y)) { PushWord(PC); PC = target; return 17; }
                    return 10;
                }
            case 5:
                if (q == 0) { PushWord(GetQq(p, mode)); return mode == IndexMode.None || p != 2 ? 11 : 15; }
                switch (p)
                {
                    case 0: { ushort target = FetchWord(); PushWord(PC); PC = target; return 17; }
                    default: return 0; // p=1..3 are DD/ED/FD prefixes, handled by the caller before reaching here.
                }
            case 6:
                A = ApplyAlu(y, A, FetchByte());
                return 7;
            default: // z == 7
                PushWord(PC);
                PC = (ushort)(y * 8);
                return 11;
        }
    }

    private void ExExx()
    {
        (B, B_) = (B_, B); (C, C_) = (C_, C);
        (D, D_) = (D_, D); (E, E_) = (E_, E);
        (H, H_) = (H_, H); (L, L_) = (L_, L);
    }

    private int ExecuteX3Z3(int y, IndexMode mode)
    {
        switch (y)
        {
            case 0: { ushort target = FetchWord(); PC = target; return 10; }
            case 1: return 0; // CB prefix, handled by the caller.
            case 2: _io.WritePort(FetchByte(), A); return 11;
            case 3: A = _io.ReadPort(FetchByte()); return 11;
            case 4:
                {
                    ushort addr = SP;
                    ushort hl = GetHlLike(mode);
                    ushort mem = ReadWord(addr);
                    WriteWord(addr, hl);
                    SetHlLike(mode, mem);
                    return mode == IndexMode.None ? 19 : 23;
                }
            case 5: (D, H) = (H, D); (E, L) = (L, E); return 4;
            case 6: IFF1 = IFF2 = false; return 4;
            default: IFF1 = IFF2 = true; return 4;
        }
    }
}
