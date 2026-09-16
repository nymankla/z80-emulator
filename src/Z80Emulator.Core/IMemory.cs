namespace Z80Emulator.Core;

/// <summary>16-bit addressable byte memory as seen by the CPU's address/data bus.</summary>
public interface IMemory
{
    byte ReadByte(ushort address);
    void WriteByte(ushort address, byte value);
}
