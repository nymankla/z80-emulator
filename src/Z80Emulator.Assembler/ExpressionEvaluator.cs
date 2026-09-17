namespace Z80Emulator.Assembler;

/// <summary>Resolves an identifier used in an expression to a numeric value.</summary>
public interface ISymbolResolver
{
    bool TryResolve(string name, out int value);
}

/// <summary>
/// A small recursive-descent evaluator for the arithmetic expressions that appear as
/// instruction operands and directive arguments: decimal/hex/binary/character literals,
/// the current-address symbol <c>$</c>, label references, unary +/-, and + - * / with the
/// usual precedence and parentheses.
/// </summary>
public static class ExpressionEvaluator
{
    public static int Evaluate(string text, int lineNumber, int currentAddress, ISymbolResolver symbols)
    {
        var cursor = new Cursor(text, lineNumber, currentAddress, symbols);
        int value = ParseExpr(cursor);
        cursor.SkipWhitespace();
        if (!cursor.AtEnd)
            throw new AssemblyException(lineNumber, $"unexpected character '{cursor.Peek()}' in expression '{text}'");
        return value;
    }

    private static int ParseExpr(Cursor c)
    {
        int value = ParseTerm(c);
        while (true)
        {
            c.SkipWhitespace();
            if (c.TryConsume('+')) value += ParseTerm(c);
            else if (c.TryConsume('-')) value -= ParseTerm(c);
            else return value;
        }
    }

    private static int ParseTerm(Cursor c)
    {
        int value = ParseFactor(c);
        while (true)
        {
            c.SkipWhitespace();
            if (c.TryConsume('*')) value *= ParseFactor(c);
            else if (c.TryConsume('/'))
            {
                int divisor = ParseFactor(c);
                if (divisor == 0) throw new AssemblyException(c.LineNumber, "division by zero");
                value /= divisor;
            }
            else return value;
        }
    }

    private static int ParseFactor(Cursor c)
    {
        c.SkipWhitespace();
        if (c.TryConsume('-')) return -ParseFactor(c);
        if (c.TryConsume('+')) return ParseFactor(c);
        if (c.TryConsume('~')) return ~ParseFactor(c);

        if (c.TryConsume('('))
        {
            int value = ParseExpr(c);
            c.SkipWhitespace();
            if (!c.TryConsume(')')) throw new AssemblyException(c.LineNumber, "missing ')' in expression");
            return value;
        }

        if (c.TryConsume('$'))
            return c.CurrentAddress;

        if (c.Peek() == '\'')
            return ParseCharLiteral(c);

        if (char.IsDigit(c.Peek()))
            return ParseNumber(c);

        if (IsIdentifierStart(c.Peek()))
            return ParseIdentifierOrSymbol(c);

        throw new AssemblyException(c.LineNumber, $"unexpected character '{c.Peek()}' in expression");
    }

    private static int ParseCharLiteral(Cursor c)
    {
        c.Advance(); // opening quote
        if (c.AtEnd) throw new AssemblyException(c.LineNumber, "unterminated character literal");
        char ch = c.Peek();
        c.Advance();
        if (c.AtEnd || c.Peek() != '\'')
            throw new AssemblyException(c.LineNumber, "character literal must contain exactly one character, e.g. 'A'");
        c.Advance(); // closing quote
        return ch;
    }

    private static int ParseNumber(Cursor c)
    {
        int start = c.Position;
        while (!c.AtEnd && (char.IsLetterOrDigit(c.Peek()))) c.Advance();
        string token = c.Text.Substring(start, c.Position - start);

        try
        {
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt32(token.Substring(2), 16);

            if (token.EndsWith("h", StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt32(token.Substring(0, token.Length - 1), 16);

            if (token.EndsWith("b", StringComparison.OrdinalIgnoreCase) && token.Take(token.Length - 1).All(ch => ch is '0' or '1'))
                return Convert.ToInt32(token.Substring(0, token.Length - 1), 2);

            if (token.EndsWith("o", StringComparison.OrdinalIgnoreCase) || token.EndsWith("q", StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt32(token.Substring(0, token.Length - 1), 8);

            if (!token.All(char.IsDigit))
                throw new FormatException();

            return Convert.ToInt32(token, 10);
        }
        catch (FormatException)
        {
            throw new AssemblyException(c.LineNumber, $"invalid numeric literal '{token}'");
        }
    }

    private static int ParseIdentifierOrSymbol(Cursor c)
    {
        int start = c.Position;
        while (!c.AtEnd && IsIdentifierPart(c.Peek())) c.Advance();
        string name = c.Text.Substring(start, c.Position - start);

        if (!c.Symbols.TryResolve(name, out int value))
            throw new AssemblyException(c.LineNumber, $"undefined symbol '{name}'");
        return value;
    }

    private static bool IsIdentifierStart(char ch) => char.IsLetter(ch) || ch == '_' || ch == '.';
    private static bool IsIdentifierPart(char ch) => char.IsLetterOrDigit(ch) || ch == '_' || ch == '.';

    private sealed class Cursor
    {
        public readonly string Text;
        public readonly int LineNumber;
        public readonly int CurrentAddress;
        public readonly ISymbolResolver Symbols;
        public int Position;

        public Cursor(string text, int lineNumber, int currentAddress, ISymbolResolver symbols)
        {
            Text = text;
            LineNumber = lineNumber;
            CurrentAddress = currentAddress;
            Symbols = symbols;
        }

        public bool AtEnd => Position >= Text.Length;
        public char Peek() => AtEnd ? '\0' : Text[Position];
        public void Advance() => Position++;

        public void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(Peek())) Advance();
        }

        public bool TryConsume(char ch)
        {
            SkipWhitespace();
            if (AtEnd || Peek() != ch) return false;
            Advance();
            return true;
        }
    }
}
