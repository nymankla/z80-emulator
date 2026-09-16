using Z80Emulator.Core;

if (args.Length < 1)
{
    Console.WriteLine("Usage: Z80Emulator.Cli <binary-file> [load-address-hex] [max-steps]");
    Console.WriteLine();
    Console.WriteLine("Loads a flat binary image into memory and runs it starting at the load");
    Console.WriteLine("address (default 0x0000), stopping on HALT or after max-steps (default 1,000,000).");
    return 1;
}

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
