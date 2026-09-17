using Z80Emulator.Assembler;
using Z80Emulator.Core;
using Xunit;

namespace Z80Emulator.Tests;

public class AssemblerTests
{
    private static byte[] Asm(string source) => Assembler.Assembler.Assemble(source).Bytes;

    [Theory]
    [InlineData("NOP", new byte[] { 0x00 })]
    [InlineData("HALT", new byte[] { 0x76 })]
    [InlineData("RET", new byte[] { 0xC9 })]
    [InlineData("EXX", new byte[] { 0xD9 })]
    [InlineData("DI", new byte[] { 0xF3 })]
    [InlineData("EI", new byte[] { 0xFB })]
    [InlineData("NEG", new byte[] { 0xED, 0x44 })]
    [InlineData("LDIR", new byte[] { 0xED, 0xB0 })]
    [InlineData("CPDR", new byte[] { 0xED, 0xB9 })]
    [InlineData("RLD", new byte[] { 0xED, 0x6F })]
    [InlineData("RETI", new byte[] { 0xED, 0x4D })]
    public void NoOperandMnemonics_EncodeToFixedBytes(string src, byte[] expected) => Assert.Equal(expected, Asm(src));

    [Theory]
    [InlineData("LD B,C", new byte[] { 0x41 })]
    [InlineData("LD A,5", new byte[] { 0x3E, 0x05 })]
    [InlineData("LD HL,1234h", new byte[] { 0x21, 0x34, 0x12 })]
    [InlineData("LD (HL),A", new byte[] { 0x77 })]
    [InlineData("LD A,(HL)", new byte[] { 0x7E })]
    [InlineData("LD (BC),A", new byte[] { 0x02 })]
    [InlineData("LD A,(DE)", new byte[] { 0x1A })]
    [InlineData("LD SP,HL", new byte[] { 0xF9 })]
    [InlineData("LD (1234h),A", new byte[] { 0x32, 0x34, 0x12 })]
    [InlineData("LD A,(1234h)", new byte[] { 0x3A, 0x34, 0x12 })]
    [InlineData("LD (1234h),HL", new byte[] { 0x22, 0x34, 0x12 })]
    [InlineData("LD BC,(1234h)", new byte[] { 0xED, 0x4B, 0x34, 0x12 })]
    [InlineData("LD (1234h),DE", new byte[] { 0xED, 0x53, 0x34, 0x12 })]
    [InlineData("LD A,I", new byte[] { 0xED, 0x57 })]
    [InlineData("LD R,A", new byte[] { 0xED, 0x4F })]
    public void Ld_Forms(string src, byte[] expected) => Assert.Equal(expected, Asm(src));

    [Theory]
    [InlineData("LD B,(IX+5)", new byte[] { 0xDD, 0x46, 0x05 })]
    [InlineData("LD (IY-1),C", new byte[] { 0xFD, 0x71, 0xFF })]
    [InlineData("LD IXH,7", new byte[] { 0xDD, 0x26, 0x07 })]
    [InlineData("LD (IX+2),20h", new byte[] { 0xDD, 0x36, 0x02, 0x20 })]
    [InlineData("LD IX,1234h", new byte[] { 0xDD, 0x21, 0x34, 0x12 })]
    [InlineData("LD IX,(1234h)", new byte[] { 0xDD, 0x2A, 0x34, 0x12 })]
    public void Ld_IndexedForms(string src, byte[] expected) => Assert.Equal(expected, Asm(src));

    [Theory]
    [InlineData("ADD A,B", new byte[] { 0x80 })]
    [InlineData("ADC A,10h", new byte[] { 0xCE, 0x10 })]
    [InlineData("SUB B", new byte[] { 0x90 })]
    [InlineData("SUB A,B", new byte[] { 0x90 })]
    [InlineData("AND (HL)", new byte[] { 0xA6 })]
    [InlineData("XOR A", new byte[] { 0xAF })]
    [InlineData("OR C", new byte[] { 0xB1 })]
    [InlineData("CP 10", new byte[] { 0xFE, 0x0A })]
    [InlineData("ADD A,(IX+2)", new byte[] { 0xDD, 0x86, 0x02 })]
    [InlineData("ADD HL,BC", new byte[] { 0x09 })]
    [InlineData("ADC HL,DE", new byte[] { 0xED, 0x5A })]
    [InlineData("SBC HL,SP", new byte[] { 0xED, 0x72 })]
    [InlineData("ADD IX,BC", new byte[] { 0xDD, 0x09 })]
    [InlineData("ADD IX,IX", new byte[] { 0xDD, 0x29 })]
    public void Alu_Forms(string src, byte[] expected) => Assert.Equal(expected, Asm(src));

    [Theory]
    [InlineData("INC B", new byte[] { 0x04 })]
    [InlineData("DEC (HL)", new byte[] { 0x35 })]
    [InlineData("INC IX", new byte[] { 0xDD, 0x23 })]
    [InlineData("DEC IXH", new byte[] { 0xDD, 0x25 })]
    [InlineData("INC (IY+3)", new byte[] { 0xFD, 0x34, 0x03 })]
    public void IncDec_Forms(string src, byte[] expected) => Assert.Equal(expected, Asm(src));

    [Theory]
    [InlineData("RLC B", new byte[] { 0xCB, 0x00 })]
    [InlineData("BIT 7,A", new byte[] { 0xCB, 0x7F })]
    [InlineData("SET 3,(HL)", new byte[] { 0xCB, 0xDE })]
    [InlineData("RES 0,(IX+1)", new byte[] { 0xDD, 0xCB, 0x01, 0x86 })]
    [InlineData("RLC (IY+2),B", new byte[] { 0xFD, 0xCB, 0x02, 0x00 })]
    [InlineData("SRL (HL)", new byte[] { 0xCB, 0x3E })]
    public void CbGroup_Forms(string src, byte[] expected) => Assert.Equal(expected, Asm(src));

    [Theory]
    [InlineData("JP 1234h", new byte[] { 0xC3, 0x34, 0x12 })]
    [InlineData("JP NZ,1234h", new byte[] { 0xC2, 0x34, 0x12 })]
    [InlineData("JP (HL)", new byte[] { 0xE9 })]
    [InlineData("JP (IX)", new byte[] { 0xDD, 0xE9 })]
    [InlineData("CALL 1234h", new byte[] { 0xCD, 0x34, 0x12 })]
    [InlineData("CALL C,1234h", new byte[] { 0xDC, 0x34, 0x12 })]
    [InlineData("RET Z", new byte[] { 0xC8 })]
    [InlineData("RST 38h", new byte[] { 0xFF })]
    [InlineData("RST 0", new byte[] { 0xC7 })]
    [InlineData("PUSH BC", new byte[] { 0xC5 })]
    [InlineData("POP AF", new byte[] { 0xF1 })]
    [InlineData("PUSH IX", new byte[] { 0xDD, 0xE5 })]
    [InlineData("EX DE,HL", new byte[] { 0xEB })]
    [InlineData("EX AF,AF'", new byte[] { 0x08 })]
    [InlineData("EX (SP),HL", new byte[] { 0xE3 })]
    [InlineData("EX (SP),IX", new byte[] { 0xDD, 0xE3 })]
    [InlineData("IN A,(10h)", new byte[] { 0xDB, 0x10 })]
    [InlineData("IN B,(C)", new byte[] { 0xED, 0x40 })]
    [InlineData("OUT (10h),A", new byte[] { 0xD3, 0x10 })]
    [InlineData("OUT (C),C", new byte[] { 0xED, 0x49 })]
    [InlineData("IM 1", new byte[] { 0xED, 0x56 })]
    [InlineData("IM 2", new byte[] { 0xED, 0x5E })]
    public void ControlFlowAndMisc_Forms(string src, byte[] expected) => Assert.Equal(expected, Asm(src));

    [Fact]
    public void Org_SetsTheStartingAddressForLabelsAndOutput()
    {
        var result = Assembler.Assembler.Assemble("org 0100h\nstart: nop\n");
        Assert.Equal(0x0100, result.OriginAddress);
        Assert.Equal(0x0100, result.Symbols["start"]);
        Assert.Equal(new byte[] { 0x00 }, result.Bytes);
    }

    [Fact]
    public void Equ_DefinesAReusableConstant()
    {
        var result = Assembler.Assembler.Assemble("count equ 10\n ld a,count\n");
        Assert.Equal(10, result.Symbols["count"]);
        Assert.Equal(new byte[] { 0x3E, 0x0A }, result.Bytes);
    }

    [Fact]
    public void Db_MixesStringsAndNumbers()
    {
        var result = Assembler.Assembler.Assemble("db 'AB',13,10,'$'");
        Assert.Equal(new byte[] { (byte)'A', (byte)'B', 13, 10, (byte)'$' }, result.Bytes);
    }

    [Fact]
    public void Dw_EmitsLittleEndianWords()
    {
        var result = Assembler.Assembler.Assemble("dw 1234h,5678h");
        Assert.Equal(new byte[] { 0x34, 0x12, 0x78, 0x56 }, result.Bytes);
    }

    [Fact]
    public void Ds_ReservesZeroFilledSpaceByDefault()
    {
        var result = Assembler.Assembler.Assemble("ds 4\ndb 99");
        Assert.Equal(new byte[] { 0, 0, 0, 0, 99 }, result.Bytes);
    }

    [Fact]
    public void Ds_HonorsExplicitFillValue()
    {
        var result = Assembler.Assembler.Assemble("ds 3,0FFh");
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF }, result.Bytes);
    }

    [Fact]
    public void ForwardLabelReference_ResolvesToCorrectRelativeDisplacement()
    {
        // JR skip ; NOP ; skip: HALT   -- skip is 1 byte past the JR, so displacement=1.
        var result = Assembler.Assembler.Assemble("org 0\n jr skip\n nop\nskip: halt\n");
        Assert.Equal(new byte[] { 0x18, 0x01, 0x00, 0x76 }, result.Bytes);
    }

    [Fact]
    public void BackwardLabelReference_ResolvesToCorrectRelativeDisplacement()
    {
        // loop: DEC B ; JR NZ,loop -- loop (addr 0) minus (JR's own address 1 + 2) = -3.
        var result = Assembler.Assembler.Assemble("org 0\nloop: dec b\n jr nz,loop\n");
        Assert.Equal(new byte[] { 0x05, 0x20, 0xFD }, result.Bytes);
    }

    [Fact]
    public void UnknownMnemonic_Throws()
    {
        var ex = Assert.Throws<AssemblyException>(() => Asm("FROB A,B"));
        Assert.Contains("unknown mnemonic", ex.Message);
    }

    [Fact]
    public void DuplicateLabel_Throws()
    {
        Assert.Throws<AssemblyException>(() => Assembler.Assembler.Assemble("x: nop\nx: nop\n"));
    }

    [Fact]
    public void UndefinedSymbol_Throws()
    {
        var ex = Assert.Throws<AssemblyException>(() => Asm("LD A,nosuchlabel"));
        Assert.Contains("undefined symbol", ex.Message);
    }

    [Fact]
    public void OutOfRangeRelativeJump_Throws()
    {
        string src = "org 0\n jr far\n" + string.Concat(System.Linq.Enumerable.Repeat("nop\n", 200)) + "far: nop\n";
        Assert.Throws<AssemblyException>(() => Asm(src));
    }

    [Fact]
    public void MixingIndexedHalfRegisterWithRealHl_Throws()
    {
        Assert.Throws<AssemblyException>(() => Asm("LD IXH,H"));
    }

    [Fact]
    public void EndToEnd_AssembledSumProgram_RunsCorrectlyOnTheCpu()
    {
        string src = """
            org 0
                ld b,10
                ld a,0
            loop:
                add a,b
                dec b
                jr nz,loop
                halt
            """;

        byte[] program = Asm(src);

        var memory = new PlainMemory();
        memory.Load(0, program);
        var cpu = new Cpu(memory);
        for (int i = 0; i < 100 && !cpu.Halted; i++) cpu.Step();

        Assert.True(cpu.Halted);
        Assert.Equal(55, cpu.A);
    }
}
