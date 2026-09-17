namespace Z80Emulator.Assembler;

/// <summary>
/// One parsed operand. Deliberately lightweight: it only splits off an indirect
/// "(...)" wrapper up front, and leaves everything else (which register, which
/// expression) to be classified on demand by the mnemonic-specific encoders in
/// <see cref="Encoder"/> — different instructions accept different operand shapes,
/// so there's no single enum that fits every mnemonic's grammar.
/// </summary>
public sealed class Operand
{
    /// <summary>The full trimmed operand text, e.g. "(IX+5)" or "A" or "mylabel+1".</summary>
    public string Text { get; }

    /// <summary>True if the operand is parenthesized, e.g. "(HL)", "(1234h)", "(IX+2)".</summary>
    public bool IsIndirect { get; }

    /// <summary>The text inside the parentheses when <see cref="IsIndirect"/>, else same as <see cref="Text"/>.</summary>
    public string Inner { get; }

    public Operand(string text)
    {
        Text = text.Trim();
        if (Text.Length >= 2 && Text[0] == '(' && Text[^1] == ')')
        {
            IsIndirect = true;
            Inner = Text.Substring(1, Text.Length - 2).Trim();
        }
        else
        {
            IsIndirect = false;
            Inner = Text;
        }
    }

    public override string ToString() => Text;
}

public enum Reg8 { B, C, D, E, H, L, A, IXH, IXL, IYH, IYL }
public enum Reg16 { BC, DE, HL, SP, AF, IX, IY }
public enum Condition { NZ, Z, NC, C, PO, PE, P, M }

public static class Registers
{
    /// <summary>The standard 3-bit register-table code for B,C,D,E,H,L,A (H/L here mean the
    /// real registers; IXH/IXL/IYH/IYL share the same codes as H/L but need a DD/FD prefix,
    /// which the caller adds separately).</summary>
    public static int Code(Reg8 r) => r switch
    {
        Reg8.B => 0,
        Reg8.C => 1,
        Reg8.D => 2,
        Reg8.E => 3,
        Reg8.H or Reg8.IXH or Reg8.IYH => 4,
        Reg8.L or Reg8.IXL or Reg8.IYL => 5,
        Reg8.A => 7,
        _ => throw new ArgumentOutOfRangeException(nameof(r))
    };

    /// <summary>DD for an IX half-register, FD for an IY half-register, else no prefix needed.</summary>
    public static byte? PrefixFor(Reg8 r) => r switch
    {
        Reg8.IXH or Reg8.IXL => 0xDD,
        Reg8.IYH or Reg8.IYL => 0xFD,
        _ => null
    };

    public static Reg8? TryParseReg8(string s) => s.Trim().ToUpperInvariant() switch
    {
        "A" => Reg8.A,
        "B" => Reg8.B,
        "C" => Reg8.C,
        "D" => Reg8.D,
        "E" => Reg8.E,
        "H" => Reg8.H,
        "L" => Reg8.L,
        "IXH" or "HX" => Reg8.IXH,
        "IXL" or "LX" => Reg8.IXL,
        "IYH" or "HY" => Reg8.IYH,
        "IYL" or "LY" => Reg8.IYL,
        _ => null
    };

    public static Reg16? TryParseReg16(string s) => s.Trim().ToUpperInvariant() switch
    {
        "BC" => Reg16.BC,
        "DE" => Reg16.DE,
        "HL" => Reg16.HL,
        "SP" => Reg16.SP,
        "AF" => Reg16.AF,
        "IX" => Reg16.IX,
        "IY" => Reg16.IY,
        _ => null
    };

    public static Condition? TryParseCondition(string s) => s.Trim().ToUpperInvariant() switch
    {
        "NZ" => Condition.NZ,
        "Z" => Condition.Z,
        "NC" => Condition.NC,
        "C" => Condition.C,
        "PO" => Condition.PO,
        "PE" => Condition.PE,
        "P" => Condition.P,
        "M" => Condition.M,
        _ => null
    };

    public static int Code(Condition cc) => (int)cc;
}
