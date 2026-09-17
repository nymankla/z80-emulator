# Z80 Emulator

A Zilog Z80 CPU core interpreter in C#. It implements the full documented
instruction set, both register sets, IX/IY (including the undocumented
IXH/IXL/IYH/IYL half-registers and the DD CB / FD CB "shadow register" quirk),
all three interrupt modes, and NMI — driven purely through the `IMemory` and
`IIoDevice` interfaces you provide. No peripherals, host machine, or clock are
modeled; this is the CPU core only.

## Layout

- `src/Z80Emulator.Core` — the CPU core. Start at [`Cpu.cs`](src/Z80Emulator.Core/Cpu.cs).
- `src/Z80Emulator.Assembler` — a two-pass Z80 assembler (see below). Start at
  [`Assembler.cs`](src/Z80Emulator.Assembler/Assembler.cs).
- `src/Z80Emulator.Cli` — a minimal runner: assembles, or loads a flat binary/CP/M `.com`
  file, and executes it.
- `tests/Z80Emulator.Tests` — xUnit tests covering the ALU, flags, branching, indexed
  addressing, CB/ED-prefixed instructions, block instructions, interrupts, and the
  assembler's instruction encodings and directives.

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

## Assembling a CP/M program

```bash
dotnet run --project src/Z80Emulator.Cli -- asm samples/hello.asm samples/hello.com
dotnet run --project src/Z80Emulator.Cli -- cpm samples/hello.com
```

The assembler covers the full documented instruction set — main, CB-, ED-, and
DD/FD-prefixed (including the DD CB/FD CB "shadow register" form and the
IXH/IXL/IYH/IYL half-registers) — plus `ORG`, `EQU`, `DB`/`DEFB` (numbers and
`'strings'`/`"strings"`), `DW`/`DEFW`, `DS`/`DEFS`, and `END`. Labels may be
referenced before they're defined (forward references); `EQU` may not — its
value must already be known at the point it's defined. Numbers accept
`123`, `0x7B`, `7Bh`, `01111011b`, and `'A'`/`173o`; `$` means "this line's
address". There's no macro support.

```csharp
using Z80Emulator.Assembler;

AssembleResult result = Assembler.Assemble(sourceText); // throws AssemblyException on error
byte[] bytes = result.Bytes;                            // ready to write out or load into IMemory
```

## Running the sample

```bash
dotnet run --project src/Z80Emulator.Cli -- samples/sum-1-to-10.bin
```

This loads a hand-assembled program (`LD B,10 / LD A,0 / loop: ADD A,B / DEC B / JR NZ,loop / HALT`)
and dumps the registers afterward; `A` ends up holding `0x37` (55).

There are also two CP/M samples exercising the console-I/O BDOS stub (see below):
`samples/readchar-echo.com` (functions 1/11) and `samples/readline-echo.com`
(function 10, including backspace editing and max-length truncation):

```bash
echo hello | dotnet run --project src/Z80Emulator.Cli -- cpm samples/readline-echo.com
```

## Tests

```bash
dotnet test
```

### ZEXDOC / ZEXALL

The CLI has a `cpm` mode — a minimal CP/M BDOS stub covering console I/O
(functions 1, 2, 6, 9, 10, 11 — read/write a character, direct I/O, print a
`$`-terminated string, buffered line input, and input status; everything
else, notably disk/file I/O, is an unimplemented no-op) — specifically so it
can host Frank Cringle's classic Z80 instruction exerciser, the standard
correctness benchmark for Z80 cores:

```bash
dotnet run -c Release --project src/Z80Emulator.Cli -- cpm tools/zexall/zexdoc.com   # documented flags
dotnet run -c Release --project src/Z80Emulator.Cli -- cpm tools/zexall/zexall.com   # + undocumented X/Y flags
```

Both pass all 65 test cases (`tools/zexall/`, from
[agn453/ZEXALL](https://github.com/agn453/ZEXALL), GPLv2, bundled here only as
test input — not linked into `Z80Emulator.Core`/`Cli`). Each run executes
~5.76 billion instructions and takes roughly a minute.

## Known limitations

This targets a correct, well-organized *interpreter*, not cycle-accurate hardware emulation:

- ZEXDOC/ZEXALL don't exercise the block I/O instructions (`INI`/`IND`/`OUTI`/`OUTD`
  and their repeating forms) at all, since CP/M doesn't define port behavior for
  them. Their `S`/`Z` flags are correct; the remaining undocumented flag bits are
  approximated rather than verified.
- The one-instruction interrupt-acceptance delay after `EI` is not modeled
  (an interrupt raised immediately after `EI` is accepted before the next
  instruction executes, rather than after it).
- No refresh-driven wait states, contended memory, or bus timing — this is a
  pure instruction-set interpreter, meant to sit behind whatever host/memory
  map you build around it.
