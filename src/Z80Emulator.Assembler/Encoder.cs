namespace Z80Emulator.Assembler;

/// <summary>
/// Encodes one parsed instruction line into bytes. Mirrors the CPU core's own decoding
/// scheme in reverse: instead of one giant table, each mnemonic group has its own method,
/// built from the same register/condition/opcode-field building blocks the core's decoder
/// (Cpu.Main.cs / Cpu.Cb.cs / Cpu.Ed.cs) uses.
///
/// <paramref name="validate"/> (constructor arg) controls whether value-dependent checks
/// (relative jump range, RST/IM/bit-number range) throw or are silently skipped. Pass 1 of
/// the assembler calls this with validate:false and a lenient symbol resolver (forward
/// references read as 0) purely to measure instruction lengths; pass 2 calls it with
/// validate:true and every symbol fully resolved, to produce the real bytes.
/// </summary>
public sealed partial class Encoder
{
    private readonly ISymbolResolver _symbols;
    private readonly bool _validate;

    public Encoder(ISymbolResolver symbols, bool validate)
    {
        _symbols = symbols;
        _validate = validate;
    }

    public List<byte> Encode(string mnemonic, IReadOnlyList<Operand> ops, int address, int line)
    {
        switch (mnemonic)
        {
            case "LD": return EncodeLd(Require2(ops, line), address, line);

            case "ADD": case "ADC": case "SBC": return EncodeAddAdcSbc(mnemonic, ops, address, line);
            case "SUB": case "AND": case "XOR": case "OR": case "CP": return EncodeAluSingle(mnemonic, ops, address, line);

            case "INC": return EncodeIncDec(true, Require1(ops, line), address, line);
            case "DEC": return EncodeIncDec(false, Require1(ops, line), address, line);

            case "RLC": return EncodeRotateShift(0, ops, address, line);
            case "RRC": return EncodeRotateShift(1, ops, address, line);
            case "RL": return EncodeRotateShift(2, ops, address, line);
            case "RR": return EncodeRotateShift(3, ops, address, line);
            case "SLA": return EncodeRotateShift(4, ops, address, line);
            case "SRA": return EncodeRotateShift(5, ops, address, line);
            case "SLL": case "SLI": return EncodeRotateShift(6, ops, address, line);
            case "SRL": return EncodeRotateShift(7, ops, address, line);

            case "BIT": return EncodeBitSetRes(0x40, ops, address, line, allowShadow: false);
            case "RES": return EncodeBitSetRes(0x80, ops, address, line, allowShadow: true);
            case "SET": return EncodeBitSetRes(0xC0, ops, address, line, allowShadow: true);

            case "EX": return EncodeEx(Require2(ops, line), address, line);
            case "PUSH": return EncodeStack(0xC5, Require1(ops, line), line);
            case "POP": return EncodeStack(0xC1, Require1(ops, line), line);

            case "JP": return EncodeJp(ops, address, line);
            case "JR": return EncodeJr(ops, address, line);
            case "DJNZ": return EncodeRelative(0x10, Require1(ops, line), address, line);
            case "CALL": return EncodeCallOrJp(ops, address, line, isCall: true);
            case "RET": return EncodeRet(ops, line);
            case "RST": return EncodeRst(Require1(ops, line), address, line);

            case "IM": return EncodeIm(Require1(ops, line), address, line);
            case "IN": return EncodeIn(Require2(ops, line), address, line);
            case "OUT": return EncodeOut(Require2(ops, line), address, line);

            default:
                if (FixedOpcodes.TryGetValue(mnemonic, out var fixedBytes))
                {
                    RequireCount(ops, 0, line);
                    return new List<byte>(fixedBytes);
                }
                throw new AssemblyException(line, $"unknown mnemonic '{mnemonic}'");
        }
    }

    private int Eval(string text, int address, int line) => ExpressionEvaluator.Evaluate(text, line, address, _symbols);

    private static Operand Require1(IReadOnlyList<Operand> ops, int line)
    {
        if (ops.Count != 1) throw new AssemblyException(line, "expected exactly one operand");
        return ops[0];
    }

    private static (Operand, Operand) Require2(IReadOnlyList<Operand> ops, int line)
    {
        if (ops.Count != 2) throw new AssemblyException(line, "expected exactly two operands");
        return (ops[0], ops[1]);
    }

    private static void RequireCount(IReadOnlyList<Operand> ops, int count, int line)
    {
        if (ops.Count != count) throw new AssemblyException(line, $"expected {count} operand(s), got {ops.Count}");
    }

    private static bool IsReg(string text, string name) => text.Trim().Equals(name, StringComparison.OrdinalIgnoreCase);

    /// <summary>The "rp" register-pair table used by 16-bit INC/DEC, ADD HL,rp, and LD rp,nn/(nn):
    /// BC=0, DE=1, HL(or IX/IY)=2, SP=3.</summary>
    private static int RpCode(Reg16 r) => r switch
    {
        Reg16.BC => 0,
        Reg16.DE => 1,
        Reg16.HL or Reg16.IX or Reg16.IY => 2,
        Reg16.SP => 3,
        _ => throw new InvalidOperationException($"{r} has no rp code")
    };

    /// <summary>The "qq" register-pair table used by PUSH/POP: BC=0, DE=1, HL(or IX/IY)=2, AF=3.</summary>
    private static int QqCode(Reg16 r) => r switch
    {
        Reg16.BC => 0,
        Reg16.DE => 1,
        Reg16.HL or Reg16.IX or Reg16.IY => 2,
        Reg16.AF => 3,
        _ => throw new InvalidOperationException($"{r} has no qq code")
    };

    private static byte? IndexPrefixOf(Reg16 r) => r switch { Reg16.IX => 0xDD, Reg16.IY => 0xFD, _ => (byte?)null };

    /// <summary>Recognizes "(IX+d)"/"(IY+d)"/"(IX)"/"(IY)"; the displacement text still needs evaluating.</summary>
    private static bool TryParseIndexedIndirect(Operand op, out Reg16 baseReg, out string dispText)
    {
        baseReg = default;
        dispText = "0";
        if (!op.IsIndirect) return false;
        string upper = op.Inner.ToUpperInvariant();
        if (upper.StartsWith("IX")) baseReg = Reg16.IX;
        else if (upper.StartsWith("IY")) baseReg = Reg16.IY;
        else return false;

        string rest = op.Inner.Substring(2).Trim();
        if (rest.Length > 0) dispText = rest;
        return true;
    }

    private byte EvalDisplacement(string text, int address, int line)
    {
        int value = Eval(text, address, line);
        if (_validate && (value < -128 || value > 127))
            throw new AssemblyException(line, $"displacement {value} out of range (-128..127)");
        return unchecked((byte)value);
    }

    private static readonly Dictionary<string, byte[]> FixedOpcodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NOP"] = new byte[] { 0x00 },
        ["HALT"] = new byte[] { 0x76 },
        ["DI"] = new byte[] { 0xF3 },
        ["EI"] = new byte[] { 0xFB },
        ["EXX"] = new byte[] { 0xD9 },
        ["DAA"] = new byte[] { 0x27 },
        ["CPL"] = new byte[] { 0x2F },
        ["CCF"] = new byte[] { 0x3F },
        ["SCF"] = new byte[] { 0x37 },
        ["RLCA"] = new byte[] { 0x07 },
        ["RRCA"] = new byte[] { 0x0F },
        ["RLA"] = new byte[] { 0x17 },
        ["RRA"] = new byte[] { 0x1F },
        ["NEG"] = new byte[] { 0xED, 0x44 },
        ["RETN"] = new byte[] { 0xED, 0x45 },
        ["RETI"] = new byte[] { 0xED, 0x4D },
        ["RRD"] = new byte[] { 0xED, 0x67 },
        ["RLD"] = new byte[] { 0xED, 0x6F },
        ["LDI"] = new byte[] { 0xED, 0xA0 },
        ["CPI"] = new byte[] { 0xED, 0xA1 },
        ["INI"] = new byte[] { 0xED, 0xA2 },
        ["OUTI"] = new byte[] { 0xED, 0xA3 },
        ["LDD"] = new byte[] { 0xED, 0xA8 },
        ["CPD"] = new byte[] { 0xED, 0xA9 },
        ["IND"] = new byte[] { 0xED, 0xAA },
        ["OUTD"] = new byte[] { 0xED, 0xAB },
        ["LDIR"] = new byte[] { 0xED, 0xB0 },
        ["CPIR"] = new byte[] { 0xED, 0xB1 },
        ["INIR"] = new byte[] { 0xED, 0xB2 },
        ["OTIR"] = new byte[] { 0xED, 0xB3 },
        ["LDDR"] = new byte[] { 0xED, 0xB8 },
        ["CPDR"] = new byte[] { 0xED, 0xB9 },
        ["INDR"] = new byte[] { 0xED, 0xBA },
        ["OTDR"] = new byte[] { 0xED, 0xBB },
    };
}
