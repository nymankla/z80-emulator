namespace Z80Emulator.Core;

/// <summary>Simple flat 64KB RAM, useful for tests and for running standalone binaries/ROMs.</summary>
public sealed class PlainMemory : IMemory
{
    private readonly byte[] _bytes = new byte[0x10000];

    public byte ReadByte(ushort address) => _bytes[address];

    public void WriteByte(ushort address, byte value) => _bytes[address] = value;

    public void Load(ushort baseAddress, ReadOnlySpan<byte> data)
    {
        data.CopyTo(_bytes.AsSpan(baseAddress));
    }

    public ReadOnlySpan<byte> Raw => _bytes;
}
