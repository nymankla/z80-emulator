# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

A Zilog Z80 CPU core interpreter in C# (`src/Z80Emulator.Core`): the full documented
instruction set, both register sets, IX/IY (including the undocumented IXH/IXL/IYH/IYL
half-registers and the DD CB/FD CB "shadow register" quirk), all three interrupt modes,
and NMI. It is a pure instruction-set interpreter with no peripherals, host machine, or
clock modeled — callers drive it entirely through the `IMemory`/`IIoDevice` interfaces.
`src/Z80Emulator.Assembler` is a matching two-pass assembler for the same instruction set,
able to produce CP/M `.com` images the CLI's `cpm` mode can run.

## Commands

Build the whole solution:

```bash
dotnet build Z80Emulator.slnx
```

Run the test suite (xUnit), or a single test:

```bash
dotnet test
dotnet test --filter "FullyQualifiedName~CpuTests.Ldir_CopiesBlockAndTerminatesWhenBcReachesZero"
```

Run a flat binary or a CP/M `.com` program through the CLI, or assemble source into a `.com`:

```bash
dotnet run --project src/Z80Emulator.Cli -- <binary-file> [load-address-hex] [max-steps]
dotnet run --project src/Z80Emulator.Cli -- cpm <com-file> [max-t-states]
dotnet run --project src/Z80Emulator.Cli -- asm <source.asm> <output.com>
```

Run the ZEXDOC/ZEXALL instruction exercisers (see below) — build in Release first, since
each is ~5.76 billion instructions and takes about a minute in Debug vs. seconds in Release:

```bash
dotnet run -c Release --project src/Z80Emulator.Cli -- cpm tools/zexall/zexdoc.com
dotnet run -c Release --project src/Z80Emulator.Cli -- cpm tools/zexall/zexall.com
```

## Architecture

**`src/Z80Emulator.Core` is the entire CPU.** `Cpu` is a single `partial class` split
across files by concern, not by feature — there's no per-instruction-group class hierarchy:

- `Cpu.cs` — registers, the fetch/execute loop (`Step`), interrupt/NMI handling, and the
  DD/FD/CB/ED prefix-detection logic that decides which decoder handles an opcode.
- `Cpu.Registers.cs` — resolves the 3-bit register/register-pair encodings used throughout
  the other opcode tables (`ReadR8`/`WriteR8`, `GetRp`/`SetRp`, etc.), including how a
  DD/FD prefix redirects H/L or (HL) to IX/IY/(IX+d).
- `Cpu.Alu.cs` — arithmetic/logic/rotate/shift primitives and flag computation (including
  the undocumented X/Y bits and the BIT instruction's MEMPTR-sourced flags).
- `Cpu.Main.cs`, `Cpu.Cb.cs`, `Cpu.Ed.cs` — the three opcode tables (unprefixed, CB-prefixed,
  ED-prefixed), each decoded via the standard x/y/z bit-field scheme
  (see http://www.z80.info/decoding.htm) rather than a flat 256-case switch.

**The DD/FD prefix is not a separate opcode table.** `Cpu.Main.cs`'s unprefixed decoder
takes an `IndexMode` parameter (`None`/`IX`/`IY`); a DD/FD prefix just re-invokes the same
decoder with that mode set, and `Cpu.Registers.cs` redirects H/L/(HL) accordingly. The one
subtlety baked into that redirection: when an `LD r,r'` pairs a real register with a
`(IX+d)`/`(IY+d)` memory operand (e.g. `LD H,(IX+d)`), the *other* operand's H/L is the real
H/L, not IXH/IXL — only a pure register-to-register move redirects both sides. Getting this
wrong (along with the BIT/MEMPTR flag source) is exactly what ZEXALL caught during
development; see git history for both fixes.

**`src/Z80Emulator.Assembler` mirrors the core's own decoding scheme in reverse.** `Encoder`
is a `partial class` split the same way `Cpu` is (`Encoder.Ld.cs`, `Encoder.Alu.cs`,
`Encoder.Cb.cs`, `Encoder.ControlFlow.cs`), each mnemonic group built from the same
register/opcode-field building blocks the core's decoder uses, just run backwards.
`Assembler.Assemble` is a two-pass driver: pass 1 walks the source with a *lenient* symbol
resolver (an undefined forward reference reads as 0) purely to compute every instruction's
byte length and fix every label's address — this works because a Z80 instruction's length
never depends on an operand's value, only its shape (register vs. immediate vs. indexed);
pass 2 re-walks it with the now-complete table and a *strict* resolver to emit real bytes.
`EQU` is the one thing that must resolve immediately (no forward references), since letting
it differ between the two passes would desync their address bookkeeping for everything after
it. `SourceLine`/`Lexer` do line-level parsing (labels, directives, comma-splitting operands
while respecting quotes); `ExpressionEvaluator` is a small recursive-descent evaluator for
operand/directive expressions (numbers in several bases, `'char'` literals, `$` for the
current address, labels, `+ - * / ~`).

**`src/Z80Emulator.Cli`** is a thin runner over the core (and the assembler), each mode
implemented as local functions in `Program.cs`:
- Default mode loads a flat binary at an address and runs it to `HALT`.
- `cpm` mode loads a CP/M `.com` image at `0x0100` and services BDOS `CALL 5` by trapping
  `PC == 0x0005` in the run loop (no BIOS/CCP, no disk — see `HandleBdosCall`). Only
  console-I/O functions are implemented (1, 2, 6, 9, 10, 11); the disk/file functions
  (15–34) report failure (`A = 0xFF`) rather than no-op, so programs that probe for a
  filesystem fail predictably instead of hanging or misreading uninitialized state;
  everything else is an unimplemented no-op. This exists specifically to host classic
  CP/M-hosted test tools.
- `asm` mode runs `Z80Emulator.Assembler` over a source file and writes the resulting bytes
  straight to the output path — no headers, since a `.com` file is just raw bytes loaded at
  `0x0100`; `ORG` only affects label/address resolution, not what gets written.

**`tools/zexall/`** bundles Frank Cringle's ZEXDOC/ZEXALL Z80 instruction exercisers
(prebuilt `.com` binaries + source, from https://github.com/agn453/ZEXALL) as test input for
the `cpm` CLI mode — this is the standard correctness benchmark for Z80 cores, checking CRCs
of register/flag state after each instruction group. It is GPLv2-licensed and bundled only
as data to execute under the interpreter; it is not linked into `Z80Emulator.Core` or `Cli`.

**`tests/Z80Emulator.Tests`** is a faster, narrower xUnit suite: `CpuTests` covers ALU flags,
branching, indexed addressing, CB/ED-prefixed instructions, block instructions, and
interrupts; `AssemblerTests` covers the encoder's instruction forms (largely as exact
expected-byte-sequence cases per mnemonic group), directives, forward/backward label
references, error cases, and one end-to-end assemble-then-run check against `Cpu` itself.
Useful for quick iteration, but ZEXDOC/ZEXALL remains the higher-confidence correctness
check for the CPU core, since it covers documented *and* undocumented flag behavior
exhaustively.
