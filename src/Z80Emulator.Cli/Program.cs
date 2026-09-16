using Z80Emulator.Core;

if (args.Length < 1)
{
    PrintUsage();
    return 1;
}

if (args[0].Equals("cpm", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2) { PrintUsage(); return 1; }
    long budget = args.Length > 2 ? long.Parse(args[2]) : 50_000_000_000L;
    return RunCpm(args[1], budget);
}

return RunRaw(args);

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  Z80Emulator.Cli <binary-file> [load-address-hex] [max-steps]");
    Console.WriteLine("      Loads a flat binary at the given address (default 0x0000) and runs");
    Console.WriteLine("      it until HALT or max-steps (default 1,000,000).");
    Console.WriteLine();
    Console.WriteLine("  Z80Emulator.Cli cpm <com-file> [max-t-states]");
    Console.WriteLine("      Loads a CP/M .com program at 0x0100 and runs it under a minimal BDOS");
    Console.WriteLine("      stub (console I/O only: functions 1, 2, 6, 9, 10, 11), for CP/M-hosted");
    Console.WriteLine("      test tools such as ZEXDOC/ZEXALL. Stops when the program warm-boots");
    Console.WriteLine("      (jumps to 0x0000) or after max-t-states (default 50,000,000,000).");
}

static int RunRaw(string[] args)
{
    string path = args[0];
    ushort loadAddress = args.Length > 1 ? Convert.ToUInt16(args[1], 16) : (ushort)0;
    long maxSteps = args.Length > 2 ? long.Parse(args[2]) : 1_000_000;

    byte[] program = File.ReadAllBytes(path);

    var memory = new PlainMemory();
    memory.Load(loadAddress, program);

    var cpu = new Cpu(memory) { PC = loadAddress };

    long steps = 0;
    long totalCycles = 0;
    while (!cpu.Halted && steps < maxSteps)
    {
        totalCycles += cpu.Step();
        steps++;
    }

    Console.WriteLine($"Stopped after {steps:N0} instructions ({totalCycles:N0} T-states).");
    Console.WriteLine(cpu.Halted ? "Reason: HALT" : "Reason: step limit reached");
    Console.WriteLine();
    Console.WriteLine($"AF={cpu.AF:X4}  BC={cpu.BC:X4}  DE={cpu.DE:X4}  HL={cpu.HL:X4}");
    Console.WriteLine($"IX={cpu.IX:X4}  IY={cpu.IY:X4}  SP={cpu.SP:X4}  PC={cpu.PC:X4}");
    Console.WriteLine($"I={cpu.I:X2}  R={cpu.R:X2}  IM={cpu.IM}  IFF1={cpu.IFF1}  IFF2={cpu.IFF2}");

    return 0;
}

/// <summary>
/// Runs a CP/M .com image with just enough BDOS emulated (console I/O) to host
/// classic CP/M test tools like ZEXDOC/ZEXALL. PC==0x0000 (warm boot) ends the run;
/// PC==0x0005 (the BDOS entry point) is intercepted, the requested function performed
/// against this process's console, and control returned via the return address CALL 5
/// already pushed on the guest stack.
/// </summary>
static int RunCpm(string path, long maxTStates)
{
    byte[] program = File.ReadAllBytes(path);

    var memory = new PlainMemory();
    memory.Load(0x0100, program);

    var cpu = new Cpu(memory) { PC = 0x0100, SP = 0xFFFE };

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    long steps = 0;
    long tStates = 0;

    while (tStates < maxTStates)
    {
        if (cpu.PC == 0x0000) break; // warm boot: program finished

        if (cpu.PC == 0x0005)
        {
            HandleBdosCall(cpu, memory);
            continue;
        }

        tStates += cpu.Step();
        steps++;
    }

    stopwatch.Stop();
    Console.WriteLine();
    Console.WriteLine(cpu.PC == 0x0000
        ? $"-- Program exited normally after {steps:N0} instructions ({tStates:N0} T-states, {stopwatch.Elapsed}) --"
        : $"-- Stopped: T-state budget exhausted after {steps:N0} instructions ({tStates:N0} T-states, {stopwatch.Elapsed}) --");

    return 0;
}

static void HandleBdosCall(Cpu cpu, IMemory memory)
{
    switch (cpu.C)
    {
        case 1: // C_READ: blocking console character read, echoed
            cpu.A = ConsoleReadChar(echo: true);
            break;
        case 2: // C_WRITE: print the character in E
            Console.Write((char)cpu.E);
            break;
        case 6: // C_RAWIO: E=0xFF polls for input (no echo, 0 if none ready); else outputs E
            if (cpu.E == 0xFF) cpu.A = ConsoleCharAvailable() ? ConsoleReadChar(echo: false) : (byte)0;
            else Console.Write((char)cpu.E);
            break;
        case 9: // C_WRITESTR: print the '$'-terminated string at DE
            {
                ushort addr = cpu.DE;
                while (memory.ReadByte(addr) != (byte)'$')
                {
                    Console.Write((char)memory.ReadByte(addr));
                    addr++;
                }
                break;
            }
        case 10: // C_READSTR: buffered line read into (DE): [0]=max len, [1]=count out, [2..]=chars
            {
                ushort bufAddr = cpu.DE;
                byte maxLen = memory.ReadByte(bufAddr);
                int count = 0;
                while (true)
                {
                    byte ch = ConsoleReadChar(echo: false);
                    if (ch == 0x0D) { Console.Write("\r\n"); break; }
                    if ((ch == 0x08 || ch == 0x7F) && count > 0)
                    {
                        count--;
                        Console.Write("\b \b");
                        continue;
                    }
                    if (count < maxLen)
                    {
                        memory.WriteByte((ushort)(bufAddr + 2 + count), ch);
                        count++;
                        Console.Write((char)ch);
                    }
                }
                memory.WriteByte((ushort)(bufAddr + 1), (byte)count);
                break;
            }
        case 11: // C_STAT: 0xFF if a character is waiting, else 0x00
            cpu.A = ConsoleCharAvailable() ? (byte)0xFF : (byte)0x00;
            break;
        default:
            // Every other BDOS function is unused by ZEXDOC/ZEXALL; ignore it.
            break;
    }

    ushort returnAddress = memory.ReadByte(cpu.SP);
    returnAddress |= (ushort)(memory.ReadByte((ushort)(cpu.SP + 1)) << 8);
    cpu.SP += 2;
    cpu.PC = returnAddress;
}

/// <summary>True if a character is available to read without blocking, on either an
/// interactive console or redirected/piped input.</summary>
static bool ConsoleCharAvailable()
{
    if (!Console.IsInputRedirected)
    {
        try { return Console.KeyAvailable; }
        catch (InvalidOperationException) { /* not actually a console; fall through */ }
    }
    return Console.In.Peek() != -1;
}

/// <summary>Reads one character, blocking, from either an interactive console (via
/// ConsoleKey, so Enter maps to CR) or redirected/piped input. 0x1A (Ctrl-Z, the
/// classic CP/M end-of-file marker) is returned once the input stream is exhausted.</summary>
static byte ConsoleReadChar(bool echo)
{
    if (!Console.IsInputRedirected)
    {
        try
        {
            var key = Console.ReadKey(intercept: true);
            byte pressed = key.Key == ConsoleKey.Enter ? (byte)0x0D : (byte)key.KeyChar;
            if (echo) Console.Write(pressed == 0x0D ? "\r\n" : ((char)pressed).ToString());
            return pressed;
        }
        catch (InvalidOperationException) { /* not actually a console; fall through */ }
    }

    int read = Console.In.Read();
    byte ch = read == -1 ? (byte)0x1A : (byte)read;
    if (echo) Console.Write((char)ch);
    return ch;
}
