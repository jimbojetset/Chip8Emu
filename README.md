# Chip8Emu

A cross-platform Chip-8 emulator built with C# .NET 9 and SDL2.

## Description

Chip-8 is an interpreted programming language developed in the mid-1970s for 8-bit microcomputers. This emulator faithfully recreates the Chip-8 virtual machine, allowing you to run classic games and programs from that era.

## Features

- Full Chip-8 instruction set implementation
- SDL2-based graphics rendering with OpenGL (64x32 display scaled to 960x480)
- SDL2 audio for beep/sound timer
- **ImGui settings window** for live quirk adjustment, ROM selection (with descriptions), and keyboard mapping
- Configurable quirk modes for compatibility with different ROMs
- **Embedded demo ROM** — runs without any external files
- Cross-platform support (macOS, Linux, Windows)

## Requirements

### Build Requirements
- .NET 9.0 SDK or later
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
# Run with embedded demo ROM (no files needed)
dotnet run

# Run a specific ROM
dotnet run -- path/to/rom.ch8

# Run with quirk options
dotnet run -- ROMS/BREAKOUT.ch8 --s 1 --v 1
```

### Settings Window

Press **F1** to open the settings window, where you can:
- Browse and load ROMs from the `ROMS/` folder — titles and descriptions are read from `ROMS/roms.json`
- Hover over a ROM title to see its description in a tooltip
- View the keyboard mapping in the **Keyboard** tab
- Toggle quirk settings in real-time
- Apply preset configurations (COSMAC VIP, SUPER-CHIP)

When you click **Load ROM** (or double-click a title), the settings window auto-collapses.

Startup behavior:
- No command-line switches: settings window starts expanded
- With command-line switches (or a ROM path): settings window starts hidden

### ROM Catalogue (`roms.json`)

The `ROMS/roms.json` file drives the ROM list in the settings window. Each entry supports the following fields:

```json
{
    "title": "SPACE INVADERS",
    "file": "Space Invaders [David Winter].ch8",
    "description": "Classic Space Invaders. Shoot with 5, move with 4 and 6.",
    "quirks": { "ShiftQuirk": true }
}
```

| Field | Purpose |
|-------|-------|
| `title` | Display name shown in the ROM list |
| `file` | Filename inside the `ROMS/` folder to load |
| `description` | Tooltip text shown on hover |
| `quirks` | Optional — quirks to enable when this ROM is loaded |

The `quirks` object supports any combination of the following boolean fields:

| Field | Quirk |
|-------|-------|
| `ShiftQuirk` | Shift VX directly (CHIP-48/SCHIP style) |
| `JumpQuirk` | BNNN uses VX offset instead of V0 |
| `VFReset` | Reset VF after logic ops |
| `MemoryQuirk` | FX55/FX65 do not modify the I register |
| `ClippingQuirk` | Clip sprites at screen edges instead of wrapping |
| `DisplayWaitQuirk` | Wait for VBlank after each sprite draw |

When a ROM is loaded from the catalogue, any quirks defined in its entry are **OR'd onto** the current quirk state — existing enabled quirks are never disabled, only additional ones are switched on.

If `roms.json` is missing or cannot be parsed the emulator falls back to scanning the `ROMS/` folder directly.

## Keyboard Layout

The original Chip-8 had a 16-key hexadecimal keypad. This emulator maps it to your keyboard as follows:

You can also see this mapping live in the settings window under the **Keyboard** tab.

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
- `F1` - Toggle settings window
- `ESC` - Quit the emulator

## ROM Compatibility

Some ROMs may appear to run incorrectly — you might see graphical glitches, flickering sprites, games running too fast, or unexpected behavior. This is usually not a bug in the emulator but rather a result of **CHIP-8 quirks**.

### What are quirks?

CHIP-8 was never a standardized system. Over the decades, many different interpreters were written for various platforms (COSMAC VIP, HP-48 calculators, CHIP-48, SUPER-CHIP, etc.), and each implemented certain instructions slightly differently. Programs written for one interpreter may behave incorrectly on another due to these subtle differences.

Common issues caused by quirks include:
- **Graphical corruption or missing sprites** — Often caused by shift or VF reset quirk differences
- **Games running too fast** — The display wait quirk (`--d 1`) can help slow things down
- **Sprites wrapping incorrectly** — The clipping quirk (`--c 1`) controls edge behavior
- **Incorrect score/collision detection** — Usually related to VF reset or memory quirks

### What should I do?

If a ROM doesn't work correctly, try enabling different quirk combinations using the command-line switches below, or toggle them live in the **Quirks** tab of the settings window. Many classic games were written for SUPER-CHIP or CHIP-48 and require quirks like `--s 1` (shift) and `--v 1` (VF reset). You can also run the `QUIRKS` test ROM to see which quirk settings your ROM might need.

ROMs listed in `roms.json` with a `quirks` field will have those quirks applied automatically when loaded from the settings window.

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

The `ROMS/` directory includes test ROMs and classic games. Titles, filenames, and descriptions for all included ROMs are listed in `ROMS/roms.json` and shown in the settings window ROM list.

## Technical Details

- **Display:** 64x32 monochrome pixels (scaled 15x to 960x480)
- **Memory:** 4KB RAM
- **Registers:** 16 general-purpose 8-bit registers (V0-VF)
- **Stack:** 16 levels
- **Timers:** Delay timer and sound timer (both 60Hz)
- **Clock Speed:** ~500-1000 instructions per second (configurable via FrameSize)
- **UI:** ImGui-based settings overlay with Dear ImGui

## License

This project is provided as-is for educational purposes.

## Acknowledgments

- Chip-8 technical reference by Cowgod
- Test ROMs from the Chip-8 community
- SDL2 C# bindings by ppy
- Dear ImGui and ImGui.NET for the settings UI
- Silk.NET for OpenGL bindings
