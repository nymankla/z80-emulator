namespace Z80Emulator.Assembler;

/// <summary>A source-level assembly error, reported with the 1-based line number it came from.</summary>
public sealed class AssemblyException : Exception
{
    public int LineNumber { get; }

    public AssemblyException(int lineNumber, string message)
        : base($"line {lineNumber}: {message}")
    {
        LineNumber = lineNumber;
    }
}
