namespace Z80Emulator.Assembler;

public sealed partial class Encoder
{
    private List<byte> EncodeEx((Operand a, Operand b) pair, int address, int line)
    {
        var (a, b) = pair;

        if (!a.IsIndirect && !b.IsIndirect)
        {
            if (IsReg(a.Text, "DE") && IsReg(b.Text, "HL")) return new List<byte> { 0xEB };
            if (IsReg(a.Text, "AF") && IsReg(b.Text, "AF'")) return new List<byte> { 0x08 };
        }

        if (a.IsIndirect && IsReg(a.Inner, "SP") && !b.IsIndirect)
        {
            if (IsReg(b.Text, "HL")) return new List<byte> { 0xE3 };
            if (IsReg(b.Text, "IX")) return new List<byte> { 0xDD, 0xE3 };
            if (IsReg(b.Text, "IY")) return new List<byte> { 0xFD, 0xE3 };
        }

        throw new AssemblyException(line, $"invalid EX operands '{a.Text}', '{b.Text}' (expected DE,HL / AF,AF' / (SP),HL / (SP),IX / (SP),IY)");
    }

    private List<byte> EncodeStack(byte baseOpcode, Operand op, int line)
    {
        Reg16? reg16 = op.IsIndirect ? null : Registers.TryParseReg16(op.Text);
        if (reg16 is not { } r || r == Reg16.SP)
            throw new AssemblyException(line, $"invalid operand '{op.Text}' (expected BC, DE, HL, AF, IX, or IY)");

        var bytes = new List<byte>();
        byte? prefix = IndexPrefixOf(r);
        if (prefix != null) bytes.Add(prefix.Value);
        bytes.Add((byte)(baseOpcode | (QqCode(r) << 4)));
        return bytes;
    }

    private List<byte> EncodeJp(IReadOnlyList<Operand> ops, int address, int line)
    {
        if (ops.Count == 1 && ops[0].IsIndirect)
        {
            Operand op = ops[0];
            if (IsReg(op.Inner, "HL")) return new List<byte> { 0xE9 };
            if (IsReg(op.Inner, "IX")) return new List<byte> { 0xDD, 0xE9 };
            if (IsReg(op.Inner, "IY")) return new List<byte> { 0xFD, 0xE9 };
            throw new AssemblyException(line, $"invalid JP target ({op.Inner})");
        }
        return EncodeCallOrJp(ops, address, line, isCall: false);
    }

    private List<byte> EncodeCallOrJp(IReadOnlyList<Operand> ops, int address, int line, bool isCall)
    {
        byte unconditional = isCall ? (byte)0xCD : (byte)0xC3;
        byte conditionalBase = isCall ? (byte)0xC4 : (byte)0xC2;

        if (ops.Count == 1)
        {
            int nn = Eval(ops[0].Text, address, line);
            return new List<byte> { unconditional, (byte)nn, (byte)(nn >> 8) };
        }
        if (ops.Count == 2)
        {
            Condition? cc = ops[0].IsIndirect ? null : Registers.TryParseCondition(ops[0].Text);
            if (cc is not { } c) throw new AssemblyException(line, $"invalid condition '{ops[0].Text}'");
            int nn = Eval(ops[1].Text, address, line);
            return new List<byte> { (byte)(conditionalBase | (Registers.Code(c) << 3)), (byte)nn, (byte)(nn >> 8) };
        }
        throw new AssemblyException(line, $"{(isCall ? "CALL" : "JP")} expects 'nn' or 'cc,nn'");
    }

    private List<byte> EncodeJr(IReadOnlyList<Operand> ops, int address, int line)
    {
        if (ops.Count == 1)
            return EncodeRelative(0x18, ops[0], address, line);

        if (ops.Count == 2)
        {
            Condition? cc = ops[0].IsIndirect ? null : Registers.TryParseCondition(ops[0].Text);
            if (cc is not { } c || Registers.Code(c) > 3)
                throw new AssemblyException(line, "JR only supports the NZ, Z, NC, C conditions");
            return EncodeRelative((byte)(0x20 | (Registers.Code(c) << 3)), ops[1], address, line);
        }

        throw new AssemblyException(line, "JR expects 'e' or 'cc,e'");
    }

    private List<byte> EncodeRelative(byte opcode, Operand target, int address, int line)
    {
        int targetAddress = Eval(target.Text, address, line);
        int displacement = targetAddress - (address + 2);
        if (_validate && (displacement < -128 || displacement > 127))
            throw new AssemblyException(line, $"relative jump to {target.Text} (displacement {displacement}) is out of range (-128..127)");
        return new List<byte> { opcode, unchecked((byte)displacement) };
    }

    private List<byte> EncodeRet(IReadOnlyList<Operand> ops, int line)
    {
        if (ops.Count == 0) return new List<byte> { 0xC9 };
        if (ops.Count == 1)
        {
            Condition? cc = ops[0].IsIndirect ? null : Registers.TryParseCondition(ops[0].Text);
            if (cc is not { } c) throw new AssemblyException(line, $"invalid condition '{ops[0].Text}'");
            return new List<byte> { (byte)(0xC0 | (Registers.Code(c) << 3)) };
        }
        throw new AssemblyException(line, "RET expects no operand or a condition");
    }

    private List<byte> EncodeRst(Operand op, int address, int line)
    {
        int n = Eval(op.Text, address, line);
        if (_validate && (n < 0 || n > 56 || n % 8 != 0))
            throw new AssemblyException(line, $"RST target {n} must be one of 0,8,16,24,32,40,48,56");
        return new List<byte> { (byte)(0xC7 | (n & 0x38)) };
    }

    private List<byte> EncodeIm(Operand op, int address, int line)
    {
        int n = Eval(op.Text, address, line);
        return n switch
        {
            0 => new List<byte> { 0xED, 0x46 },
            1 => new List<byte> { 0xED, 0x56 },
            2 => new List<byte> { 0xED, 0x5E },
            _ when !_validate => new List<byte> { 0xED, 0x46 },
            _ => throw new AssemblyException(line, $"IM operand must be 0, 1, or 2 (got {n})")
        };
    }

    private List<byte> EncodeIn((Operand dst, Operand src) pair, int address, int line)
    {
        var (dst, src) = pair;
        if (!src.IsIndirect) throw new AssemblyException(line, "IN expects 'A,(n)' or 'r,(C)'");

        if (IsReg(src.Inner, "C"))
        {
            if (!dst.IsIndirect && IsReg(dst.Text, "F")) return new List<byte> { 0xED, 0x70 }; // undocumented IN F,(C)
            Reg8? r = dst.IsIndirect ? null : Registers.TryParseReg8(dst.Text);
            if (r is { } reg && Registers.PrefixFor(reg) == null)
                return new List<byte> { 0xED, (byte)(0x40 | (Registers.Code(reg) << 3)) };
            throw new AssemblyException(line, $"invalid IN destination '{dst.Text}'");
        }

        if (!IsReg(dst.Text, "A")) throw new AssemblyException(line, "IN n,(...) form only supports A as the destination");
        return new List<byte> { 0xDB, (byte)Eval(src.Inner, address, line) };
    }

    private List<byte> EncodeOut((Operand dst, Operand src) pair, int address, int line)
    {
        var (dst, src) = pair;
        if (!dst.IsIndirect) throw new AssemblyException(line, "OUT expects '(n),A' or '(C),r'");

        if (IsReg(dst.Inner, "C"))
        {
            Reg8? r = src.IsIndirect ? null : Registers.TryParseReg8(src.Text);
            if (r is { } reg && Registers.PrefixFor(reg) == null)
                return new List<byte> { 0xED, (byte)(0x41 | (Registers.Code(reg) << 3)) };
            if (!src.IsIndirect && src.Text.Trim() == "0") return new List<byte> { 0xED, 0x71 }; // undocumented OUT (C),0
            throw new AssemblyException(line, $"invalid OUT source '{src.Text}'");
        }

        if (!IsReg(src.Text, "A")) throw new AssemblyException(line, "OUT (...),n form only supports A as the source");
        return new List<byte> { 0xD3, (byte)Eval(dst.Inner, address, line) };
    }
}
