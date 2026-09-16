using Z80Emulator.Core;
using Xunit;

namespace Z80Emulator.Tests;

public class CpuTests
{
    private static (Cpu cpu, PlainMemory mem) NewCpu(params byte[] program)
    {
        var mem = new PlainMemory();
        mem.Load(0, program);
        var cpu = new Cpu(mem);
        return (cpu, mem);
    }

    private static void Run(Cpu cpu, int steps)
    {
        for (int i = 0; i < steps; i++) cpu.Step();
    }

    [Fact]
    public void Nop_AdvancesProgramCounterAndTakesFourCycles()
    {
        var (cpu, _) = NewCpu(0x00);
        int cycles = cpu.Step();
        Assert.Equal(4, cycles);
        Assert.Equal(1, cpu.PC);
    }

    [Fact]
    public void LdBcImmediate_LoadsBothHalves()
    {
        var (cpu, _) = NewCpu(0x01, 0x34, 0x12); // LD BC,0x1234
        cpu.Step();
        Assert.Equal(0x1234, cpu.BC);
        Assert.Equal(0x12, cpu.B);
        Assert.Equal(0x34, cpu.C);
    }

    [Fact]
    public void AddA_SetsCarryAndHalfCarryAndZero()
    {
        var (cpu, _) = NewCpu(0x3E, 0xFF, 0xC6, 0x01); // LD A,0xFF ; ADD A,1
        Run(cpu, 2);
        Assert.Equal(0, cpu.A);
        Assert.True((cpu.F & Flags.Zero) != 0);
        Assert.True((cpu.F & Flags.Carry) != 0);
        Assert.True((cpu.F & Flags.HalfCarry) != 0);
    }

    [Fact]
    public void AddA_SetsOverflowOnSignedWraparound()
    {
        var (cpu, _) = NewCpu(0x3E, 0x7F, 0xC6, 0x01); // LD A,0x7F ; ADD A,1 -> 0x80, overflow
        Run(cpu, 2);
        Assert.Equal(0x80, cpu.A);
        Assert.True((cpu.F & Flags.ParityOverflow) != 0);
        Assert.True((cpu.F & Flags.Sign) != 0);
    }

    [Fact]
    public void IncDec_TrackHalfCarryAndOverflowButPreserveCarry()
    {
        var (cpu, _) = NewCpu(0x37, 0x3E, 0x0F, 0x3C); // SCF ; LD A,0x0F ; INC A
        Run(cpu, 3);
        Assert.Equal(0x10, cpu.A);
        Assert.True((cpu.F & Flags.HalfCarry) != 0);
        Assert.True((cpu.F & Flags.Carry) != 0, "INC must not clear a carry set by a previous instruction");
    }

    [Fact]
    public void SubtractionSetsAddSubtractFlag()
    {
        var (cpu, _) = NewCpu(0x3E, 0x05, 0xD6, 0x03); // LD A,5 ; SUB 3
        Run(cpu, 2);
        Assert.Equal(2, cpu.A);
        Assert.True((cpu.F & Flags.AddSubtract) != 0);
        Assert.False((cpu.F & Flags.Carry) != 0);
    }

    [Fact]
    public void Cp_ComparesWithoutModifyingAccumulator()
    {
        var (cpu, _) = NewCpu(0x3E, 0x05, 0xFE, 0x05); // LD A,5 ; CP 5
        Run(cpu, 2);
        Assert.Equal(5, cpu.A);
        Assert.True((cpu.F & Flags.Zero) != 0);
    }

    [Fact]
    public void And_SetsParityAndClearsCarry()
    {
        var (cpu, _) = NewCpu(0x3E, 0xFF, 0x37, 0xE6, 0x0F); // LD A,0xFF ; SCF ; AND 0x0F
        Run(cpu, 3);
        Assert.Equal(0x0F, cpu.A);
        Assert.False((cpu.F & Flags.Carry) != 0);
        Assert.True((cpu.F & Flags.ParityOverflow) != 0); // 0x0F has even parity (4 bits set)
    }

    [Fact]
    public void JumpRelative_Backward_Loops()
    {
        // LD B,3 ; loop: DEC B ; JR NZ,loop ; HALT
        var (cpu, _) = NewCpu(0x06, 0x03, 0x05, 0x20, 0xFD, 0x76);
        Run(cpu, 1); // LD B,3
        Assert.Equal(3, cpu.B);
        Run(cpu, 6); // three (DEC;JR) pairs
        Assert.Equal(0, cpu.B);
    }

    [Fact]
    public void Djnz_DecrementsBAndBranchesUntilZero()
    {
        // LD B,5 ; loop: NOP ; DJNZ loop
        var (cpu, _) = NewCpu(0x06, 0x05, 0x00, 0x10, 0xFD);
        Run(cpu, 1);
        for (int i = 0; i < 5; i++) Run(cpu, 2); // NOP + DJNZ per iteration
        Assert.Equal(0, cpu.B);
        Assert.Equal(5, cpu.PC); // fell through past DJNZ
    }

    [Fact]
    public void CallAndRet_RoundTripTheStackAndProgramCounter()
    {
        // 0000: CALL 0005 ; 0003: HALT ; 0005: RET
        var (cpu, _) = NewCpu(0xCD, 0x05, 0x00, 0x76, 0x00, 0xC9);
        cpu.SP = 0x2000;
        cpu.Step(); // CALL
        Assert.Equal(5, cpu.PC);
        Assert.Equal(0x2000 - 2, cpu.SP);
        cpu.Step(); // RET
        Assert.Equal(3, cpu.PC);
        Assert.Equal(0x2000, cpu.SP);
    }

    [Fact]
    public void PushPop_RoundTripsAllRegisterPairs()
    {
        var (cpu, _) = NewCpu(0xC5, 0xC1); // PUSH BC ; POP BC
        cpu.SP = 0x2000;
        cpu.BC = 0xBEEF;
        cpu.Step();
        cpu.BC = 0;
        cpu.Step();
        Assert.Equal(0xBEEF, cpu.BC);
        Assert.Equal(0x2000, cpu.SP);
    }

    [Fact]
    public void ExDeHl_SwapsRegistersEvenUnderIndexPrefix()
    {
        // Real hardware: EX DE,HL always swaps the true DE/HL, never IX/IY.
        var (cpu, _) = NewCpu(0xDD, 0xEB);
        cpu.DE = 0x1111;
        cpu.HL = 0x2222;
        cpu.IX = 0x3333;
        cpu.Step();
        Assert.Equal(0x2222, cpu.DE);
        Assert.Equal(0x1111, cpu.HL);
        Assert.Equal(0x3333, cpu.IX); // untouched
    }

    [Fact]
    public void CbBit_TestsBitWithoutModifyingOperand()
    {
        // LD A,0x40 ; BIT 6,A
        var (cpu, _) = NewCpu(0x3E, 0x40, 0xCB, 0x77);
        Run(cpu, 2);
        Assert.Equal(0x40, cpu.A);
        Assert.False((cpu.F & Flags.Zero) != 0);
    }

    [Fact]
    public void CbSetAndRes_ModifyIndividualBits()
    {
        // LD A,0 ; SET 3,A ; RES 3,A (RES undoes the SET)
        var (cpu, _) = NewCpu(0x3E, 0x00, 0xCB, 0xDF, 0xCB, 0x9F);
        Run(cpu, 2);
        Assert.Equal(0x08, cpu.A);
        Run(cpu, 1);
        Assert.Equal(0x00, cpu.A);
    }

    [Fact]
    public void IndexedLoad_ReadsMemoryAtDisplacedAddress()
    {
        // LD IX,0x0010 ; LD (IX+2),0x42 ; LD A,(IX+2)
        var (cpu, mem) = NewCpu(0xDD, 0x21, 0x10, 0x00, 0xDD, 0x36, 0x02, 0x42, 0xDD, 0x7E, 0x02);
        Run(cpu, 3);
        Assert.Equal(0x42, cpu.A);
        Assert.Equal(0x42, mem.Raw[0x12]);
    }

    [Fact]
    public void UndocumentedIxHalfRegisters_AreIndependentOfHl()
    {
        // LD IX,0xABCD ; LD B,IXH ; LD C,IXL
        var (cpu, _) = NewCpu(0xDD, 0x21, 0xCD, 0xAB, 0xDD, 0x44, 0xDD, 0x4D);
        Run(cpu, 3);
        Assert.Equal(0xAB, cpu.B);
        Assert.Equal(0xCD, cpu.C);
        Assert.Equal(0, cpu.H);
        Assert.Equal(0, cpu.L);
    }

    [Fact]
    public void LoadHFromIndexedMemory_UsesRealHNotIxh()
    {
        // LD IX,0x0010 ; LD (IX+2),0x42 ; LD H,(IX+2) -- must set real H, not IXH.
        var (cpu, _) = NewCpu(0xDD, 0x21, 0x10, 0x00, 0xDD, 0x36, 0x02, 0x42, 0xDD, 0x66, 0x02);
        Run(cpu, 3);
        Assert.Equal(0x42, cpu.H);
        Assert.Equal(0x00, cpu.IX >> 8);
    }

    [Fact]
    public void StoreIndexedMemoryFromL_ReadsRealLNotIyl()
    {
        // LD IY,0x0010 ; LD L,0x99 ; LD (IY+2),L -- must read real L, not IYL.
        var (cpu, mem) = NewCpu(0xFD, 0x21, 0x10, 0x00, 0x2E, 0x99, 0xFD, 0x75, 0x02);
        Run(cpu, 3);
        Assert.Equal(0x99, mem.Raw[0x12]);
    }

    [Fact]
    public void BitOnIndexedMemory_TakesUndocumentedFlagsFromAddressHighByte()
    {
        // LD IX,0x2000 ; LD (IX+1),0 ; BIT 0,(IX+1) -- effective address is 0x2001, so
        // X/Y flags must come from its high byte (0x20 -> Y set, X clear), not from the
        // tested value (0, which would clear both).
        var (cpu, _) = NewCpu(0xDD, 0x21, 0x00, 0x20, 0xDD, 0x36, 0x01, 0x00, 0xDD, 0xCB, 0x01, 0x46);
        Run(cpu, 3);
        Assert.True((cpu.F & Flags.Zero) != 0);
        Assert.True((cpu.F & Flags.Y) != 0);  // bit 5 of 0x20 is set
        Assert.False((cpu.F & Flags.X) != 0); // bit 3 of 0x20 is clear
    }

    [Fact]
    public void DdCb_RotateWritesBackToMemoryAndShadowRegister()
    {
        // LD IX,0x0020 ; LD (IX+0),0x01 ; RLC (IX+0) ; result also copied into B (undocumented)
        var (cpu, mem) = NewCpu(0xDD, 0x21, 0x20, 0x00, 0xDD, 0x36, 0x00, 0x01, 0xDD, 0xCB, 0x00, 0x00);
        Run(cpu, 3);
        Assert.Equal(0x02, mem.Raw[0x20]);
        Assert.Equal(0x02, cpu.B);
    }

    [Fact]
    public void Ldir_CopiesBlockAndTerminatesWhenBcReachesZero()
    {
        var (cpu, mem) = NewCpu(0xED, 0xB0); // LDIR
        cpu.HL = 0x1000;
        cpu.DE = 0x2000;
        cpu.BC = 3;
        for (ushort i = 0; i < 3; i++) mem.WriteByte((ushort)(0x1000 + i), (byte)(0xA0 + i));

        int cycles;
        do { cycles = cpu.Step(); } while (cpu.BC != 0);

        Assert.Equal(16, cycles); // final iteration, BC now 0
        Assert.Equal(0xA0, mem.Raw[0x2000]);
        Assert.Equal(0xA1, mem.Raw[0x2001]);
        Assert.Equal(0xA2, mem.Raw[0x2002]);
        Assert.Equal(0x1003, cpu.HL);
        Assert.Equal(0x2003, cpu.DE);
    }

    [Fact]
    public void Cpir_StopsAsSoonAsItFindsTheValue()
    {
        var (cpu, mem) = NewCpu(0xED, 0xB1); // CPIR
        cpu.HL = 0x1000;
        cpu.BC = 5;
        cpu.A = 0x99;
        mem.WriteByte(0x1000, 0x11);
        mem.WriteByte(0x1001, 0x22);
        mem.WriteByte(0x1002, 0x99);
        mem.WriteByte(0x1003, 0x33);

        cpu.Step(); cpu.Step(); cpu.Step();
        Assert.Equal(0x1003, cpu.HL);
        Assert.Equal(2, cpu.BC);
        Assert.True((cpu.F & Flags.Zero) != 0);
    }

    [Fact]
    public void Daa_CorrectsAfterBcdAddition()
    {
        // LD A,0x15 ; LD B,0x27 ; ADD A,B ; DAA  (15 + 27 = 42 in BCD)
        var (cpu, _) = NewCpu(0x3E, 0x15, 0x06, 0x27, 0x80, 0x27);
        Run(cpu, 4);
        Assert.Equal(0x42, cpu.A);
    }

    [Fact]
    public void Neg_NegatesAccumulator()
    {
        var (cpu, _) = NewCpu(0x3E, 0x01, 0xED, 0x44); // LD A,1 ; NEG
        Run(cpu, 2);
        Assert.Equal(0xFF, cpu.A);
        Assert.True((cpu.F & Flags.Carry) != 0);
        Assert.True((cpu.F & Flags.AddSubtract) != 0);
    }

    [Fact]
    public void Halt_StopsAdvancingUntilInterrupted()
    {
        var (cpu, _) = NewCpu(0x76); // HALT
        cpu.Step();
        Assert.True(cpu.Halted);
        int pcBefore = cpu.PC;
        cpu.Step();
        cpu.Step();
        Assert.Equal(pcBefore, cpu.PC);
        Assert.True(cpu.Halted);
    }

    [Fact]
    public void MaskableInterrupt_Mode1_VectorsToHex0038()
    {
        var (cpu, _) = NewCpu(0xFB, 0x00, 0x00, 0x00); // EI ; NOP ; NOP ; NOP
        cpu.SP = 0x2000;
        cpu.IM = 1;
        cpu.Step(); // EI
        cpu.Step(); // NOP (IFF1 now true; interrupts accepted starting next instruction)
        cpu.RaiseInterrupt();
        cpu.Step();
        Assert.Equal(0x0038, cpu.PC);
        Assert.False(cpu.IFF1);
    }

    [Fact]
    public void NonMaskableInterrupt_VectorsToHex0066AndPreservesIff1InIff2()
    {
        var (cpu, _) = NewCpu(0x00);
        cpu.SP = 0x2000;
        cpu.IFF1 = true;
        cpu.RaiseNmi();
        cpu.Step();
        Assert.Equal(0x0066, cpu.PC);
        Assert.False(cpu.IFF1);
        Assert.True(cpu.IFF2);
    }

    [Fact]
    public void SumOneToTen_ProducesFiftyFive()
    {
        // LD B,10 ; LD A,0 ; loop: ADD A,B ; DEC B ; JR NZ,loop ; HALT
        var (cpu, _) = NewCpu(0x06, 0x0A, 0x3E, 0x00, 0x80, 0x05, 0x20, 0xFC, 0x76);
        for (int i = 0; i < 100 && !cpu.Halted; i++) cpu.Step();
        Assert.Equal(55, cpu.A);
        Assert.True(cpu.Halted);
    }
}
