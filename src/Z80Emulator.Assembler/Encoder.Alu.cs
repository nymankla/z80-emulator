namespace Z80Emulator.Assembler;

public sealed partial class Encoder
{
    // ADD/ADC/SUB/SBC/AND/XOR/OR/CP in y-field order, matching Cpu.Main.cs's ApplyAlu.
    private static readonly Dictionary<string, int> AluIndex = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ADD"] = 0, ["ADC"] = 1, ["SUB"] = 2, ["SBC"] = 3, ["AND"] = 4, ["XOR"] = 5, ["OR"] = 6, ["CP"] = 7
    };

    private List<byte> EncodeAddAdcSbc(string mnemonic, IReadOnlyList<Operand> ops, int address, int line)
    {
        var (dst, src) = Require2(ops, line);

        if (!dst.IsIndirect)
        {
            Reg16? dstReg16 = Registers.TryParseReg16(dst.Text);
            if (dstReg16 is Reg16.HL or Reg16.IX or Reg16.IY)
            {
                Reg16 dr = dstReg16.Value;
                Reg16? srcReg16 = src.IsIndirect ? null : Registers.TryParseReg16(src.Text);
                if (srcReg16 is not { } sr || (sr != Reg16.BC && sr != Reg16.DE && sr != Reg16.SP && sr != dr))
                    throw new AssemblyException(line, $"{mnemonic} {dst.Text},{src.Text}: source must be BC, DE, SP, or {dst.Text} itself");

                int p = RpCode(sr == dr ? dr : sr);
                byte? prefix = IndexPrefixOf(dr);
                var bytes = new List<byte>();
                if (prefix != null) bytes.Add(prefix.Value);

                switch (mnemonic.ToUpperInvariant())
                {
                    case "ADD": bytes.Add((byte)(0x09 | (p << 4))); break;
                    case "ADC": bytes.Add(0xED); bytes.Add((byte)(0x4A | (p << 4))); break;
                    default: bytes.Add(0xED); bytes.Add((byte)(0x42 | (p << 4))); break; // SBC
                }
                return bytes;
            }

            if (!IsReg(dst.Text, "A"))
                throw new AssemblyException(line, $"{mnemonic} requires A or HL/IX/IY as the first operand");
        }
        else
        {
            throw new AssemblyException(line, $"{mnemonic} requires A or HL/IX/IY as the first operand");
        }

        return EncodeAlu8(AluIndex[mnemonic], src, address, line);
    }

    private List<byte> EncodeAluSingle(string mnemonic, IReadOnlyList<Operand> ops, int address, int line)
    {
        Operand src = ops.Count switch
        {
            1 => ops[0],
            2 when !ops[0].IsIndirect && IsReg(ops[0].Text, "A") => ops[1],
            2 => throw new AssemblyException(line, $"{mnemonic} with two operands requires A as the first"),
            _ => throw new AssemblyException(line, $"{mnemonic} expects one operand (or 'A,' plus one operand)")
        };
        return EncodeAlu8(AluIndex[mnemonic], src, address, line);
    }

    private List<byte> EncodeAlu8(int aluIndex, Operand src, int address, int line)
    {
        if (!src.IsIndirect)
        {
            Reg8? reg8 = Registers.TryParseReg8(src.Text);
            if (reg8 is { } r)
            {
                var bytes = new List<byte>();
                byte? prefix = Registers.PrefixFor(r);
                if (prefix != null) bytes.Add(prefix.Value);
                bytes.Add((byte)(0x80 | (aluIndex << 3) | Registers.Code(r)));
                return bytes;
            }
            return new List<byte> { (byte)(0xC6 | (aluIndex << 3)), (byte)Eval(src.Text, address, line) };
        }

        if (IsReg(src.Inner, "HL"))
            return new List<byte> { (byte)(0x80 | (aluIndex << 3) | 6) };

        if (TryParseIndexedIndirect(src, out var baseReg, out string dispText))
        {
            byte disp = EvalDisplacement(dispText, address, line);
            return new List<byte> { IndexPrefixOf(baseReg)!.Value, (byte)(0x80 | (aluIndex << 3) | 6), disp };
        }

        throw new AssemblyException(line, $"invalid ALU operand ({src.Inner})");
    }

    private List<byte> EncodeIncDec(bool isInc, Operand op, int address, int line)
    {
        byte baseOpcode8 = isInc ? (byte)0x04 : (byte)0x05;
        byte baseOpcode16 = isInc ? (byte)0x03 : (byte)0x0B;
        byte baseOpcodeMem = isInc ? (byte)0x34 : (byte)0x35;

        if (!op.IsIndirect)
        {
            Reg8? reg8 = Registers.TryParseReg8(op.Text);
            if (reg8 is { } r)
            {
                var bytes = new List<byte>();
                byte? prefix = Registers.PrefixFor(r);
                if (prefix != null) bytes.Add(prefix.Value);
                bytes.Add((byte)(baseOpcode8 | (Registers.Code(r) << 3)));
                return bytes;
            }

            Reg16? reg16 = Registers.TryParseReg16(op.Text);
            if (reg16 is { } rp && rp != Reg16.AF)
            {
                var bytes = new List<byte>();
                byte? prefix = IndexPrefixOf(rp);
                if (prefix != null) bytes.Add(prefix.Value);
                bytes.Add((byte)(baseOpcode16 | (RpCode(rp) << 4)));
                return bytes;
            }

            throw new AssemblyException(line, $"invalid operand '{op.Text}' for {(isInc ? "INC" : "DEC")}");
        }

        if (IsReg(op.Inner, "HL"))
            return new List<byte> { baseOpcodeMem };

        if (TryParseIndexedIndirect(op, out var baseReg, out string dispText))
        {
            byte disp = EvalDisplacement(dispText, address, line);
            return new List<byte> { IndexPrefixOf(baseReg)!.Value, baseOpcodeMem, disp };
        }

        throw new AssemblyException(line, $"invalid operand ({op.Inner}) for {(isInc ? "INC" : "DEC")}");
    }
}
