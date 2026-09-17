namespace Z80Emulator.Assembler;

public sealed partial class Encoder
{
    private List<byte> EncodeRotateShift(int y, IReadOnlyList<Operand> ops, int address, int line)
    {
        if (ops.Count is < 1 or > 2) throw new AssemblyException(line, "expected one operand (two for the indexed shadow-register form)");
        Operand target = ops[0];

        if (!target.IsIndirect)
        {
            if (ops.Count == 2) throw new AssemblyException(line, "the shadow-register operand only applies to (IX+d)/(IY+d) targets");
            Reg8? r = Registers.TryParseReg8(target.Text);
            if (r is not { } reg || Registers.PrefixFor(reg) != null)
                throw new AssemblyException(line, $"invalid rotate/shift operand '{target.Text}'");
            return new List<byte> { 0xCB, (byte)((y << 3) | Registers.Code(reg)) };
        }

        if (IsReg(target.Inner, "HL"))
        {
            if (ops.Count == 2) throw new AssemblyException(line, "the shadow-register operand only applies to (IX+d)/(IY+d) targets");
            return new List<byte> { 0xCB, (byte)((y << 3) | 6) };
        }

        if (TryParseIndexedIndirect(target, out var baseReg, out string dispText))
        {
            byte disp = EvalDisplacement(dispText, address, line);
            int code = 6;
            if (ops.Count == 2)
            {
                Reg8? shadow = ops[1].IsIndirect ? null : Registers.TryParseReg8(ops[1].Text);
                if (shadow is not { } sr || Registers.PrefixFor(sr) != null)
                    throw new AssemblyException(line, $"invalid shadow-register operand '{ops[1].Text}'");
                code = Registers.Code(sr);
            }
            return new List<byte> { IndexPrefixOf(baseReg)!.Value, 0xCB, disp, (byte)((y << 3) | code) };
        }

        throw new AssemblyException(line, $"invalid rotate/shift operand ({target.Inner})");
    }

    private List<byte> EncodeBitSetRes(byte groupBase, IReadOnlyList<Operand> ops, int address, int line, bool allowShadow)
    {
        int minOps = 2, maxOps = allowShadow ? 3 : 2;
        if (ops.Count < minOps || ops.Count > maxOps)
            throw new AssemblyException(line, $"expected {minOps}{(allowShadow ? $" to {maxOps}" : "")} operands");

        int bit = Eval(ops[0].Text, address, line);
        if (_validate && (bit < 0 || bit > 7))
            throw new AssemblyException(line, $"bit number {bit} out of range (0..7)");

        Operand target = ops[1];

        if (!target.IsIndirect)
        {
            if (ops.Count == 3) throw new AssemblyException(line, "the shadow-register operand only applies to (IX+d)/(IY+d) targets");
            Reg8? r = Registers.TryParseReg8(target.Text);
            if (r is not { } reg || Registers.PrefixFor(reg) != null)
                throw new AssemblyException(line, $"invalid operand '{target.Text}'");
            return new List<byte> { 0xCB, (byte)(groupBase | (bit << 3) | Registers.Code(reg)) };
        }

        if (IsReg(target.Inner, "HL"))
        {
            if (ops.Count == 3) throw new AssemblyException(line, "the shadow-register operand only applies to (IX+d)/(IY+d) targets");
            return new List<byte> { 0xCB, (byte)(groupBase | (bit << 3) | 6) };
        }

        if (TryParseIndexedIndirect(target, out var baseReg, out string dispText))
        {
            byte disp = EvalDisplacement(dispText, address, line);
            int code = 6;
            if (ops.Count == 3)
            {
                if (!allowShadow) throw new AssemblyException(line, "BIT has no destination, so it has no shadow-register operand");
                Reg8? shadow = ops[2].IsIndirect ? null : Registers.TryParseReg8(ops[2].Text);
                if (shadow is not { } sr || Registers.PrefixFor(sr) != null)
                    throw new AssemblyException(line, $"invalid shadow-register operand '{ops[2].Text}'");
                code = Registers.Code(sr);
            }
            return new List<byte> { IndexPrefixOf(baseReg)!.Value, 0xCB, disp, (byte)(groupBase | (bit << 3) | code) };
        }

        throw new AssemblyException(line, $"invalid operand ({target.Inner})");
    }
}
