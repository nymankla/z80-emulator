namespace Z80Emulator.Assembler;

/// <summary>One parsed line of source: an optional label, and either a directive
/// (ORG/EQU/DB/DW/DS/END) or a CPU instruction (or neither, for a label-only line).</summary>
public sealed class SourceLine
{
    public int LineNumber { get; init; }
    public string? Label { get; init; }

    /// <summary>"ORG"/"EQU"/"DB"/"DW"/"DS"/"END" for a directive, or "" for a CPU instruction
    /// (or a label-only line, when <see cref="Mnemonic"/> is also empty).</summary>
    public string Kind { get; init; } = "";

    public string Mnemonic { get; init; } = "";
    public List<string> OperandTexts { get; init; } = new();
}

public static class Lexer
{
    private static readonly HashSet<string> DirectiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ORG", "EQU", "DB", "DEFB", "DW", "DEFW", "DS", "DEFS", "END"
    };

    public static List<SourceLine> Parse(string source)
    {
        var result = new List<SourceLine>();
        string[] rawLines = source.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        for (int i = 0; i < rawLines.Length; i++)
        {
            int lineNumber = i + 1;
            string line = StripComment(rawLines[i]).Trim();
            if (line.Length == 0) continue;

            string? label = null;

            if (TrySplitLabel(line, out string labelName, out string rest))
            {
                label = labelName;
                line = rest.Trim();
                if (line.Length == 0)
                {
                    result.Add(new SourceLine { LineNumber = lineNumber, Label = label });
                    continue;
                }
            }

            (string mnemonic, string operandText) = SplitFirstWord(line);

            // `label EQU value` is conventionally written without a colon.
            if (label == null && operandText.Length > 0)
            {
                (string secondWord, string afterSecond) = SplitFirstWord(operandText);
                if (string.Equals(secondWord, "EQU", StringComparison.OrdinalIgnoreCase))
                {
                    label = mnemonic;
                    mnemonic = "EQU";
                    operandText = afterSecond;
                }
            }

            string kind = DirectiveNames.Contains(mnemonic) ? NormalizeDirective(mnemonic) : "";
            var operands = kind == "DB" ? SplitDbOperands(operandText) : SplitOperands(operandText);

            result.Add(new SourceLine
            {
                LineNumber = lineNumber,
                Label = label,
                Kind = kind,
                Mnemonic = kind.Length == 0 ? mnemonic.ToUpperInvariant() : "",
                OperandTexts = operands
            });
        }

        return result;
    }

    private static string NormalizeDirective(string mnemonic) => mnemonic.ToUpperInvariant() switch
    {
        "DEFB" => "DB",
        "DEFW" => "DW",
        "DEFS" => "DS",
        var other => other
    };

    /// <summary>
    /// True at a 'X' character-literal (exactly 3 chars: quote, one char, quote). Single
    /// quotes are otherwise treated as ordinary characters (not a string-quoting toggle),
    /// since the "AF'" register name also uses one — a trailing apostrophe with no match.
    /// </summary>
    private static bool IsCharLiteralAt(string text, int i) =>
        text[i] == '\'' && i + 2 < text.Length && text[i + 2] == '\'';

    private static string StripComment(string line)
    {
        bool inDouble = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"') inDouble = !inDouble;
            else if (!inDouble && IsCharLiteralAt(line, i)) i += 2;
            else if (ch == ';' && !inDouble) return line.Substring(0, i);
        }
        return line;
    }

    private static bool TrySplitLabel(string line, out string label, out string rest)
    {
        label = "";
        rest = line;
        int i = 0;
        if (i >= line.Length || !(char.IsLetter(line[i]) || line[i] is '_' or '.')) return false;
        int start = i;
        while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] is '_' or '.')) i++;
        int end = i;
        while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
        if (i >= line.Length || line[i] != ':') return false;
        label = line.Substring(start, end - start);
        rest = line.Substring(i + 1);
        return true;
    }

    private static (string mnemonic, string rest) SplitFirstWord(string text)
    {
        text = text.TrimStart();
        int i = 0;
        while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
        return (text.Substring(0, i), text.Substring(i));
    }

    private static List<string> SplitOperands(string text)
    {
        var parts = new List<string>();
        text = text.Trim();
        if (text.Length == 0) return parts;

        int start = 0;
        bool inDouble = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '"') inDouble = !inDouble;
            else if (!inDouble && IsCharLiteralAt(text, i)) i += 2;
            else if (ch == ',' && !inDouble)
            {
                parts.Add(text.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }
        parts.Add(text.Substring(start).Trim());
        return parts;
    }

    /// <summary>Same as <see cref="SplitOperands"/>, but a "double-quoted" piece is kept as
    /// its own operand text verbatim (including the quotes) so DB can expand it to bytes.</summary>
    private static List<string> SplitDbOperands(string text) => SplitOperands(text);
}
