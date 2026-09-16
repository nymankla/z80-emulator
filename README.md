# Z80 Emulator

A Zilog Z80 CPU core interpreter in C#. It implements the full documented
instruction set, both register sets, IX/IY (including the undocumented
IXH/IXL/IYH/IYL half-registers and the DD CB / FD CB "shadow register" quirk),
all three interrupt modes, and NMI — driven purely through the `IMemory` and
`IIoDevice` interfaces you provide. No peripherals, host machine, or clock are
modeled; this is the CPU core only.

## Layout

- `src/Z80Emulator.Core` — the CPU core. Start at [`Cpu.cs`](src/Z80Emulator.Core/Cpu.cs).
- `src/Z80Emulator.Cli` — a minimal runner: loads a flat binary at an address and executes it.
- `tests/Z80Emulator.Tests` — xUnit tests covering the ALU, flags, branching, indexed
  addressing, CB/ED-prefixed instructions, block instructions, and interrupts.

## Using the core

```csharp
using Z80Emulator.Core;

var memory = new PlainMemory();       // flat 64KB RAM, or implement IMemory yourself
memory.Load(0x0000, myProgramBytes);

var cpu = new Cpu(memory);            // pass an IIoDevice too if you need IN/OUT
while (!cpu.Halted)
    cpu.Step();                       // executes one instruction, returns T-states elapsed
```

`Step()` also services a pending `RaiseInterrupt()` / `RaiseNmi()` at the start
of the next instruction boundary, per IM0/IM1/IM2 semantics.

## Running the sample

```bash
dotnet run --project src/Z80Emulator.Cli -- samples/sum-1-to-10.bin
```

This loads a hand-assembled program (`LD B,10 / LD A,0 / loop: ADD A,B / DEC B / JR NZ,loop / HALT`)
and dumps the registers afterward; `A` ends up holding `0x37` (55).

## Tests

```bash
dotnet test
```

## Known limitations

This targets a correct, well-organized *interpreter*, not cycle-accurate hardware emulation:

- T-state counts are accurate for every documented instruction, but the few
  genuinely obscure undocumented flag bits on the block I/O instructions
  (`INI`/`IND`/`OUTI`/`OUTD` and their repeating forms) are simplified —
  `S`/`Z` are correct, the rest are approximated.
- The one-instruction interrupt-acceptance delay after `EI` is not modeled
  (an interrupt raised immediately after `EI` is accepted before the next
  instruction executes, rather than after it).
- No refresh-driven wait states, contended memory, or bus timing — this is a
  pure instruction-set interpreter, meant to sit behind whatever host/memory
  map you build around it.
