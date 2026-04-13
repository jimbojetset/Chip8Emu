# Chip8Emu

A cross-platform Chip-8 emulator built with C# .NET 8 and SDL2.

## Description

Chip-8 is an interpreted programming language developed in the mid-1970s for 8-bit microcomputers. This emulator faithfully recreates the Chip-8 virtual machine, allowing you to run classic games and programs from that era.

## Features

- Full Chip-8 instruction set implementation
- SDL2-based graphics rendering (64x32 display scaled to 640x320)
- SDL2 audio for beep/sound timer
- Configurable quirk modes for compatibility with different ROMs
- Cross-platform support (macOS, Linux, Windows)

## Requirements

### Build Requirements
- .NET 8.0 SDK or later
- SDL2 library installed on your system

### Installing SDL2

**macOS (Homebrew):**
```bash
brew install sdl2
```

**Linux (Debian/Ubuntu):**
```bash
sudo apt install libsdl2-dev
```

**Windows:**
Download SDL2 from https://libsdl.org and place `SDL2.dll` in your application directory.

## Building

```bash
cd Chip8Emu
dotnet build
```

## Running

```bash
# Run with default test ROM
dotnet run

# Run a specific ROM
dotnet run -- path/to/rom.ch8

# Run with quirk options
dotnet run -- ROMS/BREAKOUT.ch8 --s 1 --v 1
```

## Keyboard Layout

The original Chip-8 had a 16-key hexadecimal keypad. This emulator maps it to your keyboard as follows:

```
Chip-8 Keypad       Keyboard Mapping
+-+-+-+-+           +-+-+-+-+
|1|2|3|C|           |1|2|3|4|
+-+-+-+-+           +-+-+-+-+
|4|5|6|D|           |Q|W|E|R|
+-+-+-+-+     →     +-+-+-+-+
|7|8|9|E|           |A|S|D|F|
+-+-+-+-+           +-+-+-+-+
|A|0|B|F|           |Z|X|C|V|
+-+-+-+-+           +-+-+-+-+
```

| Chip-8 Key | Keyboard Key |
|------------|--------------|
| 0          | X            |
| 1          | 1            |
| 2          | 2            |
| 3          | 3            |
| 4          | Q            |
| 5          | W            |
| 6          | E            |
| 7          | A            |
| 8          | S            |
| 9          | D            |
| A          | Z            |
| B          | C            |
| C          | 4            |
| D          | R            |
| E          | F            |
| F          | V            |

**Additional Controls:**
- `ESC` - Quit the emulator

## Quirk Options

Different Chip-8 programs were written for different interpreters with subtle behavioral differences. Use these switches to enable compatibility quirks:

| Switch | Name | Description |
|--------|------|-------------|
| `--s 1` | Shift | 8XY6/8XYE instructions shift VX directly instead of shifting VY and storing in VX |
| `--j 1` | Jump | BNNN instruction uses VX instead of V0 for the jump offset |
| `--v 1` | VF Reset | VF is reset to 0 after OR, AND, and XOR operations (8XY1, 8XY2, 8XY3) |
| `--m 1` | Memory | FX55/FX65 instructions don't modify the I register |
| `--c 1` | Clipping | Sprites are clipped at screen edges instead of wrapping around |
| `--d 1` | Display Wait | Emulator waits for VBlank after each sprite draw (limits to 60 sprites/second) |

### Examples

```bash
# Original COSMAC VIP behavior (most test ROMs)
dotnet run -- ROMS/3-corax+.ch8

# Modern interpreter behavior
dotnet run -- ROMS/BREAKOUT.ch8 --m 1

# Full quirk mode for maximum compatibility
dotnet run -- ROMS/game.ch8 --s 1 --v 1 --c 1 --d 1
```

## Included Test ROMs

The `ROMS/` directory includes several test and game ROMs:

| ROM | Description |
|-----|-------------|
| `1-chip8-logo.ch8` | Displays the Chip-8 logo |
| `2-ibm-logo.ch8` | Displays the IBM logo |
| `3-corax+.ch8` | Comprehensive opcode test suite |
| `4-flags.ch8` | Tests flag behavior |
| `5-quirks.ch8` | Tests quirk modes |
| `6-keypad.ch8` | Tests keypad input |
| `7-beep.ch8` | Tests sound timer |
| `BREAKOUT.ch8` | Classic Breakout game |
| `INVADERS.ch8` | Space Invaders clone |
| `TETRIS.ch8` | Tetris game |
| `TANK.ch8` | Tank battle game |

## Technical Details

- **Display:** 64x32 monochrome pixels (scaled 10x to 640x320)
- **Memory:** 4KB RAM
- **Registers:** 16 general-purpose 8-bit registers (V0-VF)
- **Stack:** 16 levels
- **Timers:** Delay timer and sound timer (both 60Hz)
- **Clock Speed:** ~500-1000 instructions per second (configurable via FrameSize)

## License

This project is provided as-is for educational purposes.

## Acknowledgments

- Chip-8 technical reference by Cowgod
- Test ROMs from the Chip-8 community
- SDL2 C# bindings by ppy
