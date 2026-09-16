namespace Z80Emulator.Core;

/// <summary>
/// DD/FD prefixes redirect every HL/(HL) reference in the base opcode table to
/// IX/(IX+d) or IY/(IY+d) instead. The base table is shared; only the register
/// resolution changes, which is what this enum threads through the decoder.
/// </summary>
internal enum IndexMode
{
    None,
    IX,
    IY
}
