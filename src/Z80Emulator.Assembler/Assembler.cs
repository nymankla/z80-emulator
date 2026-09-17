namespace Z80Emulator.Assembler;

public sealed class AssembleResult
{
    public byte[] Bytes { get; init; } = Array.Empty<byte>();
    public int OriginAddress { get; init; }
    public IReadOnlyDictionary<string, int> Symbols { get; init; } = new Dictionary<string, int>();
}

/// <summary>Resolves a name against a symbol table; in "lenient" mode an undefined name
/// reads as 0 instead of failing, which is what lets pass 1 measure instruction lengths
/// (and so assign every label's address) before any forward reference is actually known.</summary>
internal sealed class SymbolResolver : ISymbolResolver
{
    private readonly Dictionary<string, int> _symbols;
    private readonly bool _strict;

    public SymbolResolver(Dictionary<string, int> symbols, bool strict)
    {
        _symbols = symbols;
        _strict = strict;
    }

    public bool TryResolve(string name, out int value)
    {
        if (_symbols.TryGetValue(name, out value)) return true;
        if (_strict) return false;
        value = 0;
        return true;
    }
}

/// <summary>
/// Two-pass Z80 assembler. Pass 1 walks the source with a lenient symbol resolver (any
/// forward reference reads as 0) purely to compute every instruction's byte length and
/// so fix every label's address; pass 2 walks it again with the now-complete symbol table
/// and a strict resolver, producing the real bytes. This works because on the Z80 an
/// instruction's length never depends on an operand's *value* — only its *shape*
/// (register vs. immediate vs. indexed-memory) — so both passes always agree on lengths.
///
/// EQU is the one exception: it must resolve immediately (forward-referencing EQU is not
/// supported) so that pass 1 and pass 2 can never disagree about a constant's value, which
/// would desynchronize their address bookkeeping for everything after it.
/// </summary>
public static class Assembler
{
    public static AssembleResult Assemble(string source)
    {
        var lines = Lexer.Parse(source);
        var symbols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        RunPass(lines, symbols, new SymbolResolver(symbols, strict: false), emit: null);

        var output = new List<byte>();
        int origin = RunPass(lines, symbols, new SymbolResolver(symbols, strict: true), emit: output);

        return new AssembleResult { Bytes = output.ToArray(), OriginAddress = origin, Symbols = symbols };
    }

    private static int RunPass(List<SourceLine> lines, Dictionary<string, int> symbols, ISymbolResolver resolver, List<byte>? emit)
    {
        bool isFirstPass = emit == null;
        var strictResolver = new SymbolResolver(symbols, strict: true);
        var encoder = new Encoder(resolver, validate: !isFirstPass);

        int address = 0;
        int origin = 0;
        bool originSet = false;

        foreach (var line in lines)
        {
            if (line.Label != null && line.Kind != "EQU" && isFirstPass)
            {
                if (!symbols.TryAdd(line.Label, address))
                    throw new AssemblyException(line.LineNumber, $"duplicate label '{line.Label}'");
            }

            switch (line.Kind)
            {
                case "EQU":
                    if (line.Label == null) throw new AssemblyException(line.LineNumber, "EQU requires a label");
                    if (isFirstPass)
                    {
                        int value = ExpressionEvaluator.Evaluate(line.OperandTexts[0], line.LineNumber, address, strictResolver);
                        symbols[line.Label] = value;
                    }
                    break;

                case "ORG":
                    address = ExpressionEvaluator.Evaluate(line.OperandTexts[0], line.LineNumber, address, resolver);
                    if (!originSet) { origin = address; originSet = true; }
                    break;

                case "END":
                    return origin;

                case "DB":
                    address += EmitDb(line, address, resolver, emit);
                    break;

                case "DW":
                    address += EmitDw(line, address, resolver, emit);
                    break;

                case "DS":
                    address += EmitDs(line, address, resolver, emit);
                    break;

                default:
                    if (line.Mnemonic.Length > 0)
                    {
                        var operands = line.OperandTexts.Select(t => new Operand(t)).ToList();
                        List<byte> bytes = encoder.Encode(line.Mnemonic, operands, address, line.LineNumber);
                        emit?.AddRange(bytes);
                        address += bytes.Count;
                    }
                    break;
            }
        }

        return origin;
    }

    private static int EmitDb(SourceLine line, int address, ISymbolResolver resolver, List<byte>? emit)
    {
        if (line.OperandTexts.Count == 0) throw new AssemblyException(line.LineNumber, "DB requires at least one value");

        int length = 0;
        foreach (string text in line.OperandTexts)
        {
            // A quoted string ('...' or "...") is a run of bytes, one per character.
            bool isQuotedString = text.Length >= 2 && ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\''));
            if (isQuotedString)
            {
                string content = text.Substring(1, text.Length - 2);
                length += content.Length;
                if (emit != null) foreach (char ch in content) emit.Add((byte)ch);
            }
            else
            {
                length += 1;
                if (emit != null) emit.Add((byte)ExpressionEvaluator.Evaluate(text, line.LineNumber, address + length - 1, resolver));
            }
        }
        return length;
    }

    private static int EmitDw(SourceLine line, int address, ISymbolResolver resolver, List<byte>? emit)
    {
        if (line.OperandTexts.Count == 0) throw new AssemblyException(line.LineNumber, "DW requires at least one value");

        int length = 0;
        foreach (string text in line.OperandTexts)
        {
            if (emit != null)
            {
                int value = ExpressionEvaluator.Evaluate(text, line.LineNumber, address + length, resolver);
                emit.Add((byte)value);
                emit.Add((byte)(value >> 8));
            }
            length += 2;
        }
        return length;
    }

    private static int EmitDs(SourceLine line, int address, ISymbolResolver resolver, List<byte>? emit)
    {
        if (line.OperandTexts.Count is 0 or > 2) throw new AssemblyException(line.LineNumber, "DS expects 'count' or 'count,fill'");

        int count = ExpressionEvaluator.Evaluate(line.OperandTexts[0], line.LineNumber, address, resolver);
        if (emit != null && count < 0) throw new AssemblyException(line.LineNumber, $"DS count {count} cannot be negative");
        if (count < 0) count = 0;

        if (emit != null)
        {
            byte fill = line.OperandTexts.Count > 1 ? (byte)ExpressionEvaluator.Evaluate(line.OperandTexts[1], line.LineNumber, address, resolver) : (byte)0;
            for (int i = 0; i < count; i++) emit.Add(fill);
        }
        return count;
    }
}
