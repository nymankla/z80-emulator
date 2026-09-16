namespace Z80Emulator.Core;

/// <summary>
/// The Z80's separate I/O address space, addressed by IN/OUT instructions.
/// This core models no peripherals: callers may supply their own device map,
/// or use <see cref="NullIoDevice"/> when only the CPU core is needed.
/// </summary>
public interface IIoDevice
{
    byte ReadPort(ushort port);
    void WritePort(ushort port, byte value);
}

/// <summary>Default I/O device: reads return 0xFF (open bus), writes are ignored.</summary>
public sealed class NullIoDevice : IIoDevice
{
    public static readonly NullIoDevice Instance = new();

    public byte ReadPort(ushort port) => 0xFF;
    public void WritePort(ushort port, byte value) { }
}
