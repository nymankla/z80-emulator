namespace Z80Emulator.Core;

public sealed partial class Cpu
{
    private void SetSZXY(byte result)
    {
        F = (byte)(F & (Flags.Carry | Flags.AddSubtract | Flags.ParityOverflow | Flags.HalfCarry));
        if (result == 0) F |= Flags.Zero;
        if ((result & 0x80) != 0) F |= Flags.Sign;
        F |= Flags.XyBitsOf(result);
    }

    private byte Add8(byte a, byte b, bool withCarry)
    {
        int carryIn = withCarry && (F & Flags.Carry) != 0 ? 1 : 0;
        int result = a + b + carryIn;
        byte r = (byte)result;

        byte flags = 0;
        if (((a & 0x0F) + (b & 0x0F) + carryIn) > 0x0F) flags |= Flags.HalfCarry;
        if (result > 0xFF) flags |= Flags.Carry;
        // Overflow: operands share a sign, result differs.
        if (((a ^ r) & (b ^ r) & 0x80) != 0) flags |= Flags.ParityOverflow;
        if (r == 0) flags |= Flags.Zero;
        if ((r & 0x80) != 0) flags |= Flags.Sign;
        flags |= Flags.XyBitsOf(r);

        F = flags;
        return r;
    }

    private byte Sub8(byte a, byte b, bool withCarry, bool storeResult = true)
    {
        int carryIn = withCarry && (F & Flags.Carry) != 0 ? 1 : 0;
        int result = a - b - carryIn;
        byte r = (byte)result;

        byte flags = Flags.AddSubtract;
        if (((a & 0x0F) - (b & 0x0F) - carryIn) < 0) flags |= Flags.HalfCarry;
        if (result < 0) flags |= Flags.Carry;
        if (((a ^ b) & (a ^ r) & 0x80) != 0) flags |= Flags.ParityOverflow;
        if (r == 0) flags |= Flags.Zero;
        if ((r & 0x80) != 0) flags |= Flags.Sign;
        flags |= Flags.XyBitsOf(r);

        F = flags;
        return r;
    }

    private byte And8(byte a, byte b)
    {
        byte r = (byte)(a & b);
        SetSZXY(r);
        F = (byte)((F & ~(Flags.Carry | Flags.AddSubtract | Flags.ParityOverflow | Flags.HalfCarry)) | Flags.HalfCarry);
        if (Flags.IsEvenParity(r)) F |= Flags.ParityOverflow;
        return r;
    }

    private byte Or8(byte a, byte b)
    {
        byte r = (byte)(a | b);
        SetSZXY(r);
        F = (byte)(F & ~(Flags.Carry | Flags.AddSubtract | Flags.ParityOverflow | Flags.HalfCarry));
        if (Flags.IsEvenParity(r)) F |= Flags.ParityOverflow;
        return r;
    }

    private byte Xor8(byte a, byte b)
    {
        byte r = (byte)(a ^ b);
        SetSZXY(r);
        F = (byte)(F & ~(Flags.Carry | Flags.AddSubtract | Flags.ParityOverflow | Flags.HalfCarry));
        if (Flags.IsEvenParity(r)) F |= Flags.ParityOverflow;
        return r;
    }

    private void Cp8(byte a, byte b)
    {
        // Same flags as SUB, but the result is discarded (A is unmodified).
        byte carrySave = 0;
        Sub8(a, b, false);
        // Undocumented X/Y flags for CP come from the operand, not the result.
        F = (byte)((F & ~(Flags.X | Flags.Y)) | Flags.XyBitsOf(b));
        _ = carrySave;
    }

    private byte Inc8(byte value)
    {
        byte r = (byte)(value + 1);
        byte carry = (byte)(F & Flags.Carry);
        SetSZXY(r);
        F = (byte)((F & ~(Flags.AddSubtract | Flags.HalfCarry | Flags.ParityOverflow)) | carry);
        if ((value & 0x0F) == 0x0F) F |= Flags.HalfCarry;
        if (value == 0x7F) F |= Flags.ParityOverflow;
        return r;
    }

    private byte Dec8(byte value)
    {
        byte r = (byte)(value - 1);
        byte carry = (byte)(F & Flags.Carry);
        SetSZXY(r);
        F = (byte)((F & ~(Flags.AddSubtract | Flags.HalfCarry | Flags.ParityOverflow)) | carry | Flags.AddSubtract);
        if ((value & 0x0F) == 0x00) F |= Flags.HalfCarry;
        if (value == 0x80) F |= Flags.ParityOverflow;
        return r;
    }

    private ushort Add16(ushort a, ushort b)
    {
        int result = a + b;
        byte flags = (byte)(F & (Flags.Sign | Flags.Zero | Flags.ParityOverflow));
        if (((a & 0x0FFF) + (b & 0x0FFF)) > 0x0FFF) flags |= Flags.HalfCarry;
        if (result > 0xFFFF) flags |= Flags.Carry;
        flags |= Flags.XyBitsOf((byte)(result >> 8));
        F = flags;
        return (ushort)result;
    }

    private ushort Adc16(ushort a, ushort b)
    {
        int carryIn = (F & Flags.Carry) != 0 ? 1 : 0;
        int result = a + b + carryIn;
        ushort r = (ushort)result;

        byte flags = 0;
        if (((a & 0x0FFF) + (b & 0x0FFF) + carryIn) > 0x0FFF) flags |= Flags.HalfCarry;
        if (result > 0xFFFF) flags |= Flags.Carry;
        if (((a ^ r) & (b ^ r) & 0x8000) != 0) flags |= Flags.ParityOverflow;
        if (r == 0) flags |= Flags.Zero;
        if ((r & 0x8000) != 0) flags |= Flags.Sign;
        flags |= Flags.XyBitsOf((byte)(r >> 8));

        F = flags;
        return r;
    }

    private ushort Sbc16(ushort a, ushort b)
    {
        int carryIn = (F & Flags.Carry) != 0 ? 1 : 0;
        int result = a - b - carryIn;
        ushort r = (ushort)result;

        byte flags = Flags.AddSubtract;
        if (((a & 0x0FFF) - (b & 0x0FFF) - carryIn) < 0) flags |= Flags.HalfCarry;
        if (result < 0) flags |= Flags.Carry;
        if (((a ^ b) & (a ^ r) & 0x8000) != 0) flags |= Flags.ParityOverflow;
        if (r == 0) flags |= Flags.Zero;
        if ((r & 0x8000) != 0) flags |= Flags.Sign;
        flags |= Flags.XyBitsOf((byte)(r >> 8));

        F = flags;
        return r;
    }

    private void Daa()
    {
        int a = A;
        bool carry = (F & Flags.Carry) != 0;
        bool halfCarry = (F & Flags.HalfCarry) != 0;
        bool subtract = (F & Flags.AddSubtract) != 0;

        int correction = 0;
        if (halfCarry || (a & 0x0F) > 9) correction |= 0x06;
        if (carry || a > 0x99) { correction |= 0x60; carry = true; }

        int result = subtract ? a - correction : a + correction;

        byte newHalfCarry;
        if (subtract)
            newHalfCarry = (byte)(halfCarry && (a & 0x0F) < 6 ? Flags.HalfCarry : 0);
        else
            newHalfCarry = (byte)((a & 0x0F) + (correction & 0x0F) > 0x0F ? Flags.HalfCarry : 0);

        A = (byte)result;
        SetSZXY(A);
        F = (byte)(F & ~(Flags.Carry | Flags.HalfCarry | Flags.ParityOverflow));
        if (carry) F |= Flags.Carry;
        F |= newHalfCarry;
        if (Flags.IsEvenParity(A)) F |= Flags.ParityOverflow;
        // AddSubtract flag is left untouched by DAA.
    }

    private byte Rlc8(byte v)
    {
        byte carry = (byte)((v & 0x80) >> 7);
        byte r = (byte)((v << 1) | carry);
        SetSZXY(r);
        F = (byte)(F & ~(Flags.Carry | Flags.AddSubtract | Flags.HalfCarry | Flags.ParityOverflow));
        if (carry != 0) F |= Flags.Carry;
        if (Flags.IsEvenParity(r)) F |= Flags.ParityOverflow;
        return r;
    }

    private byte Rrc8(byte v)
    {
        byte carry = (byte)(v & 0x01);
        byte r = (byte)((v >> 1) | (carry << 7));
        SetSZXY(r);
        F = (byte)(F & ~(Flags.Carry | Flags.AddSubtract | Flags.HalfCarry | Flags.ParityOverflow));
        if (carry != 0) F |= Flags.Carry;
        if (Flags.IsEvenParity(r)) F |= Flags.ParityOverflow;
        return r;
    }

    private byte Rl8(byte v)
    {
        byte carryIn = (byte)(F & Flags.Carry);
        byte carryOut = (byte)((v & 0x80) >> 7);
        byte r = (byte)((v << 1) | carryIn);
        SetSZXY(r);
        F = (byte)(F & ~(Flags.Carry | Flags.AddSubtract | Flags.HalfCarry | Flags.ParityOverflow));
        if (carryOut != 0) F |= Flags.Carry;
        if (Flags.IsEvenParity(r)) F |= Flags.ParityOverflow;
        return r;
    }

    private byte Rr8(byte v)
    {
        byte carryIn = (byte)((F & Flags.Carry) != 0 ? 0x80 : 0);
        byte carryOut = (byte)(v & 0x01);
        byte r = (byte)((v >> 1) | carryIn);
        SetSZXY(r);
        F = (byte)(F & ~(Flags.Carry | Flags.AddSubtract | Flags.HalfCarry | Flags.ParityOverflow));
        if (carryOut != 0) F |= Flags.Carry;
        if (Flags.IsEvenParity(r)) F |= Flags.ParityOverflow;
        return r;
    }

    private byte Sla8(byte v)
    {
        byte carryOut = (byte)((v & 0x80) >> 7);
        byte r = (byte)(v << 1);
        SetSZXY(r);
        F = (byte)(F & ~(Flags.Carry | Flags.AddSubtract | Flags.HalfCarry | Flags.ParityOverflow));
        if (carryOut != 0) F |= Flags.Carry;
        if (Flags.IsEvenParity(r)) F |= Flags.ParityOverflow;
        return r;
    }

    private byte Sra8(byte v)
    {
        byte carryOut = (byte)(v & 0x01);
        byte r = (byte)((v >> 1) | (v & 0x80));
        SetSZXY(r);
        F = (byte)(F & ~(Flags.Carry | Flags.AddSubtract | Flags.HalfCarry | Flags.ParityOverflow));
        if (carryOut != 0) F |= Flags.Carry;
        if (Flags.IsEvenParity(r)) F |= Flags.ParityOverflow;
        return r;
    }

    /// <summary>Undocumented SLL/SL1: shifts left, feeding a 1 into bit 0.</summary>
    private byte Sll8(byte v)
    {
        byte carryOut = (byte)((v & 0x80) >> 7);
        byte r = (byte)((v << 1) | 0x01);
        SetSZXY(r);
        F = (byte)(F & ~(Flags.Carry | Flags.AddSubtract | Flags.HalfCarry | Flags.ParityOverflow));
        if (carryOut != 0) F |= Flags.Carry;
        if (Flags.IsEvenParity(r)) F |= Flags.ParityOverflow;
        return r;
    }

    private byte Srl8(byte v)
    {
        byte carryOut = (byte)(v & 0x01);
        byte r = (byte)(v >> 1);
        SetSZXY(r);
        F = (byte)(F & ~(Flags.Carry | Flags.AddSubtract | Flags.HalfCarry | Flags.ParityOverflow));
        if (carryOut != 0) F |= Flags.Carry;
        if (Flags.IsEvenParity(r)) F |= Flags.ParityOverflow;
        return r;
    }

    private void Bit(int bit, byte value)
    {
        bool set = (value & (1 << bit)) != 0;
        F = (byte)(F & Flags.Carry);
        F |= Flags.HalfCarry;
        if (!set) F |= Flags.Zero | Flags.ParityOverflow;
        if (bit == 7 && set) F |= Flags.Sign;
        // Undocumented X/Y come from the tested value for (HL)-form BIT.
        F |= Flags.XyBitsOf(value);
    }
}
