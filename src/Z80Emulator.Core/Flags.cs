namespace Z80Emulator.Core;

/// <summary>Bit masks for the F (flags) register.</summary>
public static class Flags
{
    public const byte Carry = 0x01;      // C
    public const byte AddSubtract = 0x02; // N
    public const byte ParityOverflow = 0x04; // P/V
    public const byte X = 0x08;          // undocumented, bit 3 of result
    public const byte HalfCarry = 0x10;  // H
    public const byte Y = 0x20;          // undocumented, bit 5 of result
    public const byte Zero = 0x40;       // Z
    public const byte Sign = 0x80;       // S

    private static readonly bool[] ParityTable = BuildParityTable();

    private static bool[] BuildParityTable()
    {
        var table = new bool[256];
        for (int i = 0; i < 256; i++)
        {
            int bits = i;
            int count = 0;
            while (bits != 0)
            {
                count += bits & 1;
                bits >>= 1;
            }
            table[i] = (count % 2) == 0;
        }
        return table;
    }

    /// <summary>True if the byte has an even number of set bits.</summary>
    public static bool IsEvenParity(byte value) => ParityTable[value];

    /// <summary>The undocumented X/Y flag bits, copied from bits 3 and 5 of the given value.</summary>
    public static byte XyBitsOf(byte value) => (byte)(value & (X | Y));
}
