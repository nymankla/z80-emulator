namespace Z80Emulator.Assembler;

public sealed partial class Encoder
{
    private List<byte> EncodeLd((Operand dst, Operand src) pair, int address, int line)
    {
        var (dst, src) = pair;

        // LD A,I / LD A,R / LD I,A / LD R,A
        if (!dst.IsIndirect && IsReg(dst.Text, "A") && !src.IsIndirect)
        {
            if (IsReg(src.Text, "I")) return new List<byte> { 0xED, 0x57 };
            if (IsReg(src.Text, "R")) return new List<byte> { 0xED, 0x5F };
        }
        if (!dst.IsIndirect && !src.IsIndirect && IsReg(src.Text, "A"))
        {
            if (IsReg(dst.Text, "I")) return new List<byte> { 0xED, 0x47 };
            if (IsReg(dst.Text, "R")) return new List<byte> { 0xED, 0x4F };
        }

        Reg8? dstReg8 = dst.IsIndirect ? null : Registers.TryParseReg8(dst.Text);
        Reg8? srcReg8 = src.IsIndirect ? null : Registers.TryParseReg8(src.Text);
        Reg16? dstReg16 = dst.IsIndirect ? null : Registers.TryParseReg16(dst.Text);
        Reg16? srcReg16 = src.IsIndirect ? null : Registers.TryParseReg16(src.Text);

        // LD r,r' / LD r,n  (dst is a plain 8-bit register, real or IXH/IXL/IYH/IYL)
        if (dstReg8 is { } d8)
        {
            if (srcReg8 is { } s8)
            {
                byte? dstPrefix = Registers.PrefixFor(d8);
                byte? srcPrefix = Registers.PrefixFor(s8);
                if (dstPrefix != null && srcPrefix != null && dstPrefix != srcPrefix)
                    throw new AssemblyException(line, "cannot combine an IX half-register with an IY half-register in one LD");
                if ((dstPrefix != null && (s8 is Reg8.H or Reg8.L)) || (srcPrefix != null && (d8 is Reg8.H or Reg8.L)))
                    throw new AssemblyException(line, "cannot combine an indexed half-register (IXH/IXL/IYH/IYL) with the real H/L in one LD");

                var bytes = new List<byte>();
                byte? prefix = dstPrefix ?? srcPrefix;
                if (prefix != null) bytes.Add(prefix.Value);
                bytes.Add((byte)(0x40 | (Registers.Code(d8) << 3) | Registers.Code(s8)));
                return bytes;
            }

            if (src.IsIndirect)
            {
                if (Registers.PrefixFor(d8) != null)
                    throw new AssemblyException(line, "an indexed half-register cannot be loaded from memory");

                if (IsReg(src.Inner, "HL"))
                    return new List<byte> { (byte)(0x40 | (Registers.Code(d8) << 3) | 6) };

                if (TryParseIndexedIndirect(src, out var srcBase, out string srcDisp))
                {
                    byte disp = EvalDisplacement(srcDisp, address, line);
                    return new List<byte> { IndexPrefixOf(srcBase)!.Value, (byte)(0x40 | (Registers.Code(d8) << 3) | 6), disp };
                }

                if (d8 != Reg8.A)
                    throw new AssemblyException(line, $"only A can be loaded from ({src.Inner})");

                if (IsReg(src.Inner, "BC")) return new List<byte> { 0x0A };
                if (IsReg(src.Inner, "DE")) return new List<byte> { 0x1A };

                int nn = Eval(src.Inner, address, line);
                return new List<byte> { 0x3A, (byte)nn, (byte)(nn >> 8) };
            }

            // LD r,n
            var immBytes = new List<byte>();
            byte? immPrefix = Registers.PrefixFor(d8);
            if (immPrefix != null) immBytes.Add(immPrefix.Value);
            immBytes.Add((byte)(0x06 | (Registers.Code(d8) << 3)));
            immBytes.Add((byte)Eval(src.Text, address, line));
            return immBytes;
        }

        // LD (HL),r / LD (HL),n / LD (IX+d),r / LD (IX+d),n
        if (dst.IsIndirect && IsReg(dst.Inner, "HL"))
        {
            if (srcReg8 is { } s8b)
            {
                if (Registers.PrefixFor(s8b) != null) throw new AssemblyException(line, "cannot store an indexed half-register to (HL)");
                return new List<byte> { (byte)(0x70 | Registers.Code(s8b)) };
            }
            return new List<byte> { 0x36, (byte)Eval(src.Text, address, line) };
        }
        if (TryParseIndexedIndirect(dst, out var dstBase, out string dstDisp))
        {
            byte disp = EvalDisplacement(dstDisp, address, line);
            byte prefix = IndexPrefixOf(dstBase)!.Value;
            if (srcReg8 is { } s8c)
            {
                if (Registers.PrefixFor(s8c) != null) throw new AssemblyException(line, "cannot store an indexed half-register to indexed memory");
                return new List<byte> { prefix, (byte)(0x70 | Registers.Code(s8c)), disp };
            }
            return new List<byte> { prefix, 0x36, disp, (byte)Eval(src.Text, address, line) };
        }

        // LD (BC),A / LD (DE),A
        if (dst.IsIndirect && IsReg(dst.Inner, "BC")) { RequireA(src, line); return new List<byte> { 0x02 }; }
        if (dst.IsIndirect && IsReg(dst.Inner, "DE")) { RequireA(src, line); return new List<byte> { 0x12 }; }

        // LD SP,HL / LD SP,IX / LD SP,IY
        if (dstReg16 == Reg16.SP && srcReg16 is Reg16.HL or Reg16.IX or Reg16.IY)
        {
            var bytes = new List<byte>();
            byte? prefix = IndexPrefixOf(srcReg16.Value);
            if (prefix != null) bytes.Add(prefix.Value);
            bytes.Add(0xF9);
            return bytes;
        }

        // LD rp,nn / LD rp,(nn)
        if (dstReg16 is { } drp && drp != Reg16.AF)
        {
            var bytes = new List<byte>();
            byte? prefix = IndexPrefixOf(drp);

            if (src.IsIndirect && !TryParseIndexedIndirect(src, out _, out _) && !IsReg(src.Inner, "HL") && !IsReg(src.Inner, "BC") && !IsReg(src.Inner, "DE"))
            {
                // LD rp,(nn)
                int nn = Eval(src.Inner, address, line);
                if (drp is Reg16.HL or Reg16.IX or Reg16.IY)
                {
                    if (prefix != null) bytes.Add(prefix.Value);
                    bytes.Add(0x2A);
                }
                else
                {
                    bytes.Add(0xED);
                    bytes.Add((byte)(0x4B | (RpCode(drp) << 4)));
                }
                bytes.Add((byte)nn);
                bytes.Add((byte)(nn >> 8));
                return bytes;
            }

            // LD rp,nn
            if (prefix != null) bytes.Add(prefix.Value);
            bytes.Add((byte)(0x01 | (RpCode(drp) << 4)));
            int imm = Eval(src.Text, address, line);
            bytes.Add((byte)imm);
            bytes.Add((byte)(imm >> 8));
            return bytes;
        }

        // LD (nn),A / LD (nn),HL / LD (nn),IX / LD (nn),IY / LD (nn),rp
        if (dst.IsIndirect && !TryParseIndexedIndirect(dst, out _, out _) && !IsReg(dst.Inner, "HL") && !IsReg(dst.Inner, "BC") && !IsReg(dst.Inner, "DE"))
        {
            int nn = Eval(dst.Inner, address, line);
            var bytes = new List<byte>();
            if (!src.IsIndirect && IsReg(src.Text, "A"))
            {
                bytes.Add(0x32);
            }
            else if (srcReg16 is Reg16.HL or Reg16.IX or Reg16.IY)
            {
                byte? prefix = IndexPrefixOf(srcReg16.Value);
                if (prefix != null) bytes.Add(prefix.Value);
                bytes.Add(0x22);
            }
            else if (srcReg16 is { } srp)
            {
                bytes.Add(0xED);
                bytes.Add((byte)(0x43 | (RpCode(srp) << 4)));
            }
            else
            {
                throw new AssemblyException(line, $"invalid source for LD ({dst.Inner}),...");
            }
            bytes.Add((byte)nn);
            bytes.Add((byte)(nn >> 8));
            return bytes;
        }

        throw new AssemblyException(line, $"invalid LD operands '{dst.Text}', '{src.Text}'");
    }

    private static void RequireA(Operand src, int line)
    {
        if (src.IsIndirect || !IsReg(src.Text, "A"))
            throw new AssemblyException(line, "only A can be stored to (BC)/(DE)");
    }
}
