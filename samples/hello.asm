; Classic CP/M "hello world" -- assembles to a .com file runnable via
;   dotnet run --project src/Z80Emulator.Cli -- asm samples/hello.asm samples/hello.com
;   dotnet run --project src/Z80Emulator.Cli -- cpm samples/hello.com

        org     0100h

bdos    equ     5
c_write equ     9

start:  ld      de,message
        ld      c,c_write
        call    bdos
        jp      0

message:
        db      'Hello from the Z80 assembler!',13,10,'$'

        end     start
