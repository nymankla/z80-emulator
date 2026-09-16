namespace Z80Emulator.Core;

public sealed partial class Cpu
{
    private byte ApplyRotateOrShift(int y, byte value) => y switch
    {
        0 => Rlc8(value),
        1 => Rrc8(value),
        2 => Rl8(value),
        3 => Rr8(value),
        4 => Sla8(value),
        5 => Sra8(value),
        6 => Sll8(value), // undocumented
        _ => Srl8(value)
    };

    /// <summary>
    /// Executes one CB-prefixed opcode. For the plain form (<paramref name="mode"/> is
    /// <see cref="IndexMode.None"/>) the opcode's own z-field selects the 8-bit operand.
    /// For the DD CB d / FD CB d form the operand is always memory at (IX/IY+d); the
    /// z-field instead selects which register also receives a copy of a written-back
    /// result (the well-known undocumented "shadow register" behaviour), and does
    /// nothing for BIT, which never writes back.
    /// </summary>
    private int ExecuteCb(IndexMode mode, sbyte displacement)
    {
        byte opcode = FetchByte();
        BumpR();
        int x = opcode >> 6;
        int y = (opcode >> 3) & 7;
        int z = opcode & 7;

        if (mode == IndexMode.None)
        {
            if (x == 1) // BIT y, r[z]
            {
                Bit(y, ReadR8(z, IndexMode.None));
                return z == 6 ? 8 : 4;
            }

            byte value = ReadR8(z, IndexMode.None);
            byte result = x switch
            {
                0 => ApplyRotateOrShift(y, value),
                2 => (byte)(value & ~(1 << y)),
                _ => (byte)(value | (1 << y))
            };
            WriteR8(z, IndexMode.None, result);
            return z == 6 ? 11 : 4;
        }
        else
        {
            ushort addr = IndexedAddress(mode, displacement);
            byte value = _memory.ReadByte(addr);

            if (x == 1) // BIT y, (IX/IY+d)
            {
                Bit(y, value);
                return 12;
            }

            byte result = x switch
            {
                0 => ApplyRotateOrShift(y, value),
                2 => (byte)(value & ~(1 << y)),
                _ => (byte)(value | (1 << y))
            };
            _memory.WriteByte(addr, result);
            if (z != 6) WriteR8(z, IndexMode.None, result); // undocumented shadow-register copy
            return 15;
        }
    }
}
