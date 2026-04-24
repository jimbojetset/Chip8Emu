using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Chip8Emu.CORE
{
    public class Chip8
    {
        private bool running = false;
        public bool Running
        {
            get { return running; }
            set { running = value; }
        }

        private bool shiftQuirk = false;
        public bool ShiftQuirk
        {
            get { return shiftQuirk; }
            set { shiftQuirk = value; }
        }

        private bool jumpQuirk = false;
        public bool JumpQuirk
        {
            get { return jumpQuirk; }
            set { jumpQuirk = value; }
        }

        private bool vFResetQuirk = true;
        public bool VFReset
        {
            get { return vFResetQuirk; }
            set { vFResetQuirk = value; }
        }

        private bool memoryQuirk = false;
        public bool MemoryQuirk
        {
            get { return memoryQuirk; }
            set { memoryQuirk = value; }
        }

        private bool clippingQuirk = false;
        public bool ClippingQuirk
        {
            get { return clippingQuirk; }
            set { clippingQuirk = value; }
        }

        private bool displayWaitQuirk = true;
        public bool DisplayWaitQuirk
        {
            get { return displayWaitQuirk; }
            set { displayWaitQuirk = value; }
        }

        private bool keyReleaseWaitQuirk = true;
        public bool KeyReleaseWaitQuirk
        {
            get { return keyReleaseWaitQuirk; }
            set { keyReleaseWaitQuirk = value; }
        }

        private bool waitingForVBlank = false;

        private int frameSize = 10;
        public int FrameSize
        {
            get { return frameSize; }
            set { frameSize = value; }
        }

        private int cpuHz = 100000; //cycles per frame 
        public int CpuHz
        {
            get { return cpuHz; }
            set { cpuHz = value; }
        }

        private const int VIDEO_WIDTH = 64;
        public static int VideoWidth
        {
            get { return VIDEO_WIDTH; }
        }

        private const int VIDEO_HEIGHT = 32;
        public static int VideoHeight
        {
            get { return VIDEO_HEIGHT; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class FIXED_BYTE_ARRAY
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 320)]
            public byte[]? @byte;
        }

        private FIXED_BYTE_ARRAY video;
        public FIXED_BYTE_ARRAY Video
        {
            get { return video; }
            set { video = value; }
        }

        public uint KeyDown
        {
            set
            {
                if (value < keypad.@byte!.Length)
                    keypad.@byte![value] = 1;
            }
        }

        public uint KeyUp
        {
            set
            {
                if (value < keypad.@byte!.Length)
                    keypad.@byte![value] = 0;
            }
        }

        private uint I;
        private uint PC;
        private byte SP = 0;// stack pointer
        private byte ST = 0;// sound timer
        private byte DT = 0;// delay timer
        private readonly FIXED_BYTE_ARRAY registers = new() { @byte = new byte[16] };
        private readonly FIXED_BYTE_ARRAY memory = new() { @byte = new byte[4096] };
        private readonly FIXED_BYTE_ARRAY keypad = new() { @byte = new byte[16] };
        private const uint START_ADDRESS = 0x200;
        private const int FONTSET_SIZE = 80;
        private bool playingSound;
        private readonly uint[] STACK = new uint[16];
        private readonly Random _random = new();
        private readonly Stopwatch _frameStopwatch = new();

        // FX0A key wait state (original VIP waits for press AND release)
        private bool _waitingForKeyRelease = false;
        private int _pressedKey = -1;

        // Callback for display updates (called by main loop)
        public Action? OnDisplayUpdate { get; set; }

        // Get the raw video buffer for rendering
        public byte[] GetVideoBuffer() => video.@byte!;

        // Font base address (configurable: 0x000 or 0x050 are common)
        private int fontBaseAddress = 0x000;
        public int FontBaseAddress
        {
            get { return fontBaseAddress; }
            set { fontBaseAddress = value & 0x0FFF; } // constrain to 12-bit address space
        }


        private readonly byte[] FONTS = new byte[]
        {
            0xF0, 0x90, 0x90, 0x90, 0xF0, // 0
            0x20, 0x60, 0x20, 0x20, 0x70, // 1
            0xF0, 0x10, 0xF0, 0x80, 0xF0, // 2
            0xF0, 0x10, 0xF0, 0x10, 0xF0, // 3
            0x90, 0x90, 0xF0, 0x10, 0x10, // 4
            0xF0, 0x80, 0xF0, 0x10, 0xF0, // 5
            0xF0, 0x80, 0xF0, 0x90, 0xF0, // 6
            0xF0, 0x10, 0x20, 0x40, 0x40, // 7
            0xF0, 0x90, 0xF0, 0x90, 0xF0, // 8
            0xF0, 0x90, 0xF0, 0x10, 0xF0, // 9
            0xF0, 0x90, 0xF0, 0x90, 0x90, // A
            0xE0, 0x90, 0xE0, 0x90, 0xE0, // B
            0xF0, 0x80, 0x80, 0x80, 0xF0, // C
            0xE0, 0x90, 0x90, 0x90, 0xE0, // D
            0xF0, 0x80, 0xF0, 0x80, 0xF0, // E
            0xF0, 0x80, 0xF0, 0x80, 0x80  // F
        };

        public Chip8()
        {
            video = new FIXED_BYTE_ARRAY { @byte = new byte[VIDEO_WIDTH * VIDEO_HEIGHT] };
            Reset();
        }

        /// <summary>
        /// Reset the emulator to initial state (call before loading a new ROM)
        /// </summary>
        public void Reset()
        {
            // Reset program counter
            PC = START_ADDRESS;
            I = 0;
            SP = 0;
            ST = 0;
            DT = 0;
            playingSound = false;
            waitingForVBlank = false;
            _waitingForKeyRelease = false;
            _pressedKey = -1;

            // Clear registers
            Array.Clear(registers.@byte!, 0, registers.@byte!.Length);

            // Clear memory and reload fonts at configurable base address
            Array.Clear(memory.@byte!, 0, memory.@byte!.Length);
            int baseAddr = Math.Min(fontBaseAddress, memory.@byte!.Length - FONTSET_SIZE);
            for (uint i = 0; i < FONTSET_SIZE; i++)
                memory.@byte![baseAddr + (int)i] = FONTS[i];

            // Clear video buffer
            Array.Clear(video.@byte!, 0, video.@byte!.Length);
            OnDisplayUpdate?.Invoke();

            // Clear stack
            Array.Clear(STACK, 0, STACK.Length);

            // Clear keypad
            Array.Clear(keypad.@byte!, 0, keypad.@byte!.Length);
        }

        private bool LoadROM(string filePath)
        {
            try
            {
                using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
                BinaryReader br = new(fs);
                long progSize = new FileInfo(filePath).Length;
                byte[] rom = br.ReadBytes((int)progSize);
                return LoadROMFromBytes(rom);
            }
            catch
            {
                return false;
            }
        }

        private bool LoadROMFromBytes(byte[] rom)
        {
            try
            {
                if (rom.Length <= memory.@byte!.Length - START_ADDRESS)
                {
                    for (int i = 0; i < rom.Length; i++)
                        memory.@byte![START_ADDRESS + i] = rom[i];
                    return true;
                }
                else
                {
                    throw new Exception("Memory Overflow");
                }
            }
            catch
            {
                return false;
            }
        }

        public void Start(string filePathROM)
        {
            // Reset to clean state before loading new ROM
            Reset();

            if (!LoadROM(filePathROM))
                throw new Exception("Failed to load ROM");

            RunEmulator();
        }

        public void Start(byte[] romData)
        {
            // Reset to clean state before loading new ROM
            Reset();

            if (!LoadROMFromBytes(romData))
                throw new Exception("Failed to load ROM from embedded data");

            RunEmulator();
        }

        private void RunEmulator()
        {
            running = true;
            const double frameMs = 1000.0 / 60.0; // target 60Hz
            double cycleRemainder = 0.0;
            _frameStopwatch.Restart();

            // Main run loop
            while (running)
            {
                double frameStart = _frameStopwatch.Elapsed.TotalMilliseconds;

                // Clear VBlank wait at start of each frame
                waitingForVBlank = false;

                // Run a fixed number of CPU cycles per frame instead of filling the entire frame budget.
                double cyclesPerFrame = CpuHz / 60.0;
                cycleRemainder += cyclesPerFrame;
                int cyclesThisFrame = (int)cycleRemainder;
                cycleRemainder -= cyclesThisFrame;

                for (int cycles = 0; cycles < cyclesThisFrame; cycles++)
                {
                    // Display wait quirk: stop executing until next frame after draw
                    if (displayWaitQuirk && waitingForVBlank)
                        break;

                    if (PC >= memory.@byte!.Length - 1)
                        throw new Exception($"Program counter out of range: 0x{PC:X}");

                    uint opcode = (uint)memory.@byte![PC] << 8 | memory.@byte![PC + 1];

                    // Execute current instruction
                    ExecuteOpcode(opcode);
                }

                UpdateTimers();

                // High-resolution wait for the remainder of the frame
                double frameElapsed = _frameStopwatch.Elapsed.TotalMilliseconds - frameStart;
                double remaining = frameMs - frameElapsed;
                while (remaining > 0)
                {
                    // Prefer sleep/yield over spin waiting to reduce host CPU usage.
                    if (remaining > 2)
                        Thread.Sleep((int)(remaining - 1));
                    else
                        Thread.Yield();

                    frameElapsed = _frameStopwatch.Elapsed.TotalMilliseconds - frameStart;
                    remaining = frameMs - frameElapsed;
                }
            }
            running = false;
        }

        public void Stop()
        {
            running = false;
        }

        public void ExecuteOpcode(uint opcode)
        {
            PC += 2;
            CallOpcode(opcode);
        }

        private void CallOpcode(uint opcode)
        {
            string opHex = opcode.ToString("X4");
            if (opHex == "00E0") { OP_00E0(); return; }
            if (opHex == "00EE") { OP_00EE(); return; }
            if (opHex[0] == '0') { OP_0nnn(); return; }
            if (opHex[0] == '1') { OP_1nnn(opcode); return; }
            if (opHex[0] == '2') { OP_2nnn(opcode); return; }
            if (opHex[0] == '3') { OP_3xnn(opcode); return; }
            if (opHex[0] == '4') { OP_4xnn(opcode); return; }
            if (opHex[0] == '6') { OP_6xnn(opcode); return; }
            if (opHex[0] == '7') { OP_7xnn(opcode); return; }
            if (opHex[0] == 'A') { OP_Annn(opcode); return; }
            if (opHex[0] == 'B') { OP_Bnnn(opcode); return; }
            if (opHex[0] == 'C') { OP_Cxnn(opcode); return; }
            if (opHex[0] == 'D') { OP_Dxyn(opcode); return; }
            if (opHex[0] == '5' && opHex[3] == '0') { OP_5xy0(opcode); return; }
            if (opHex[0] == '8' && opHex[3] == '0') { OP_8xy0(opcode); return; }
            if (opHex[0] == '8' && opHex[3] == '1') { OP_8xy1(opcode); return; }
            if (opHex[0] == '8' && opHex[3] == '2') { OP_8xy2(opcode); return; }
            if (opHex[0] == '8' && opHex[3] == '3') { OP_8xy3(opcode); return; }
            if (opHex[0] == '8' && opHex[3] == '4') { OP_8xy4(opcode); return; }
            if (opHex[0] == '8' && opHex[3] == '5') { OP_8xy5(opcode); return; }
            if (opHex[0] == '8' && opHex[3] == '6') { OP_8xy6(opcode); return; }
            if (opHex[0] == '8' && opHex[3] == '7') { OP_8xy7(opcode); return; }
            if (opHex[0] == '8' && opHex[3] == 'E') { OP_8xyE(opcode); return; }
            if (opHex[0] == '9' && opHex[3] == '0') { OP_9xy0(opcode); return; }
            if (opHex[0] == 'E' && opHex[2] == '9' && opHex[3] == 'E') { OP_Ex9E(opcode); return; }
            if (opHex[0] == 'E' && opHex[2] == 'A' && opHex[3] == '1') { OP_ExA1(opcode); return; }
            if (opHex[0] == 'F' && opHex[2] == '0' && opHex[3] == '7') { OP_Fx07(opcode); return; }
            if (opHex[0] == 'F' && opHex[2] == '0' && opHex[3] == 'A') { OP_Fx0A(opcode); return; }
            if (opHex[0] == 'F' && opHex[2] == '1' && opHex[3] == '5') { OP_Fx15(opcode); return; }
            if (opHex[0] == 'F' && opHex[2] == '1' && opHex[3] == '8') { OP_Fx18(opcode); return; }
            if (opHex[0] == 'F' && opHex[2] == '1' && opHex[3] == 'E') { OP_Fx1E(opcode); return; }
            if (opHex[0] == 'F' && opHex[2] == '2' && opHex[3] == '9') { OP_Fx29(opcode); return; }
            if (opHex[0] == 'F' && opHex[2] == '3' && opHex[3] == '3') { OP_Fx33(opcode); return; }
            if (opHex[0] == 'F' && opHex[2] == '5' && opHex[3] == '5') { OP_Fx55(opcode); return; }
            if (opHex[0] == 'F' && opHex[2] == '6' && opHex[3] == '5') { OP_Fx65(opcode); return; }
            throw new Exception("Invalid Opcode - " + opHex + " at PC: " + (PC - 2).ToString("X"));
        }

        private void UpdateTimers()
        {
            if (DT > 0)
                DT--;
            if (ST > 0)
            {
                if (!playingSound)
                {
                    playingSound = true;
                    Sound.StartTone(500);
                }
                ST--;
            }
            else if (playingSound)
            {
                playingSound = false;
                Sound.StopTone();
            }
        }

        private void OP_00E0()
        {
            video = new FIXED_BYTE_ARRAY { @byte = new byte[VIDEO_WIDTH * VIDEO_HEIGHT] };
            OnDisplayUpdate?.Invoke();
        }

        private void OP_00EE()
        {
            if (SP == 0)
                throw new Exception("Stack underflow on RET");
            SP--;
            PC = STACK[SP];
            STACK[SP] = 0x00;
        }

        private static void OP_0nnn()
        {
            //NOP
        }

        private void OP_1nnn(uint opcode)
        {
            uint address = opcode & 0x0FFF;
            PC = address;
        }

        private void OP_2nnn(uint opcode)
        {
            if (SP >= STACK.Length)
                throw new Exception("Stack overflow on CALL");

            uint address = opcode & 0x0FFF;
            STACK[SP] = PC;
            SP++;
            PC = address;
        }

        private void OP_3xnn(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint b = opcode & 0x00FF;
            if (registers.@byte![Vx] == b)
                PC += 2;
        }

        private void OP_4xnn(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint b = opcode & 0x00FF;
            if (registers.@byte![Vx] != b)
                PC += 2;
        }

        private void OP_5xy0(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint Vy = (opcode & 0x00F0) >> 4;
            if (registers.@byte![Vx] == registers.@byte![Vy])
                PC += 2;
        }

        private void OP_6xnn(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint b = opcode & 0x00FF;
            registers.@byte![Vx] = (byte)b;
        }

        private void OP_7xnn(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint b = opcode & 0x00FF;
            registers.@byte![Vx] = (byte)(registers.@byte![Vx] + (byte)b & 0xFF);
        }

        private void OP_8xy0(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint Vy = (opcode & 0x00F0) >> 4;
            registers.@byte![Vx] = registers.@byte![Vy];
        }

        private void OP_8xy1(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint Vy = (opcode & 0x00F0) >> 4;
            registers.@byte![Vx] |= registers.@byte![Vy];
            if (vFResetQuirk)
                registers.@byte![15] = 0;
        }

        private void OP_8xy2(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint Vy = (opcode & 0x00F0) >> 4;
            registers.@byte![Vx] &= registers.@byte![Vy];
            if (vFResetQuirk)
                registers.@byte![15] = 0;
        }

        private void OP_8xy3(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint Vy = (opcode & 0x00F0) >> 4;
            registers.@byte![Vx] ^= registers.@byte![Vy];
            if (vFResetQuirk)
                registers.@byte![15] = 0;
        }

        private void OP_8xy4(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint Vy = (opcode & 0x00F0) >> 4;
            uint carry = 0;
            if (registers.@byte![Vy] > 0xFF - registers.@byte![Vx])
                carry = 1;
            registers.@byte![Vx] += registers.@byte![Vy];
            registers.@byte![15] = (byte)carry;
        }

        private void OP_8xy5(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint Vy = (opcode & 0x00F0) >> 4;
            uint carry = 0;
            if (registers.@byte![Vx] >= registers.@byte![Vy])
                carry = 1;
            registers.@byte![Vx] -= registers.@byte![Vy];
            registers.@byte![15] = (byte)carry;
        }

        private void OP_8xy6(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint Vy = (opcode & 0x00F0) >> 4;
            byte source = shiftQuirk ? registers.@byte![Vx] : registers.@byte![Vy];
            registers.@byte![Vx] = (byte)(source >> 1);
            registers.@byte![15] = (byte)(source & 0x1);
        }

        private void OP_8xy7(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint Vy = (opcode & 0x00F0) >> 4;
            uint carry = 0;
            if (registers.@byte![Vy] >= registers.@byte![Vx])
                carry = 1;
            registers.@byte![Vx] = (byte)(registers.@byte![Vy] - registers.@byte![Vx]);
            registers.@byte![15] = (byte)carry;
        }

        private void OP_8xyE(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint Vy = (opcode & 0x00F0) >> 4;
            byte source = shiftQuirk ? registers.@byte![Vx] : registers.@byte![Vy];
            registers.@byte![Vx] = (byte)(source << 1);
            registers.@byte![15] = (byte)((source & 0x80) >> 7);
        }

        private void OP_9xy0(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint Vy = (opcode & 0x00F0) >> 4;
            if (registers.@byte![Vx] != registers.@byte![Vy])
                PC += 2;
        }

        private void OP_Annn(uint opcode)
        {
            uint address = opcode & 0x0FFF;
            I = (ushort)address;
        }

        private void OP_Bnnn(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint address = opcode & 0x0FFF;
            if (!jumpQuirk)
                PC = address + registers.@byte![0];
            else
                PC = address + registers.@byte![Vx];
        }

        private void OP_Cxnn(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint b = opcode & 0x00FF;
            int r = _random.Next(0, 256);
            registers.@byte![Vx] = (byte)(r & b);
        }

        private void OP_Dxyn(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint Vy = (opcode & 0x00F0) >> 4;
            uint height = opcode & 0x000F;
            uint xPos = (uint)registers.@byte![Vx] % VIDEO_WIDTH;
            uint yPos = (uint)registers.@byte![Vy] % VIDEO_HEIGHT;
            registers.@byte![15] = 0;
            for (uint row = 0; row < height; row++)
            {
                // Clipping: stop drawing rows if we go past screen edge
                if (clippingQuirk && yPos + row >= VIDEO_HEIGHT)
                    break;

                // Safe sprite byte fetch with wrapping to avoid out-of-range
                int addr = (int)((I + row) % (uint)memory.@byte!.Length);
                uint spriteByte = memory.@byte![addr];

                for (uint col = 0; col < 8; col++)
                {
                    // Clipping: skip pixels past screen edge
                    if (clippingQuirk && xPos + col >= VIDEO_WIDTH)
                        continue;

                    uint vp = clippingQuirk
                        ? (yPos + row) * VIDEO_WIDTH + xPos + col
                        : ((yPos + row) % VIDEO_HEIGHT) * VIDEO_WIDTH + ((xPos + col) % VIDEO_WIDTH);

                    if ((spriteByte & (0x80 >> (int)col)) != 0)
                    {
                        if (video.@byte![vp] == 1)
                            registers.@byte![15] = 1;
                        video.@byte![vp] ^= 1;
                    }
                }
            }
            // Display wait quirk: signal to wait for next VBlank
            waitingForVBlank = true;
            OnDisplayUpdate?.Invoke();
        }

        private void OP_Ex9E(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint key = registers.@byte![Vx];
            if (key >= keypad.@byte!.Length)
                throw new Exception($"Invalid keypad index in SKP: 0x{key:X}");

            if (keypad.@byte![key] == 1)
                PC += 2;
        }

        private void OP_ExA1(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint key = registers.@byte![Vx];
            if (key >= keypad.@byte!.Length)
                throw new Exception($"Invalid keypad index in SKNP: 0x{key:X}");

            if (keypad.@byte![key] == 0)
                PC += 2;
        }

        private void OP_Fx07(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            registers.@byte![Vx] = DT;
        }

        private void OP_Fx0A(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;

            if (keyReleaseWaitQuirk)
            {
                // Original COSMAC VIP behavior: wait for key press AND release
                if (_waitingForKeyRelease)
                {
                    if (keypad.@byte![_pressedKey] == 0)
                    {
                        registers.@byte![Vx] = (byte)_pressedKey;
                        _waitingForKeyRelease = false;
                        _pressedKey = -1;
                    }
                    else
                    {
                        PC -= 2;
                    }
                }
                else
                {
                    for (int i = 0; i < 16; i++)
                    {
                        if (keypad.@byte![i] != 0)
                        {
                            _pressedKey = i;
                            _waitingForKeyRelease = true;
                            break;
                        }
                    }
                    PC -= 2;
                }
            }
            else
            {
                bool keyPressed = false;
                for (int i = 0; i < 16; i++)
                {
                    if (keypad.@byte![i] != 0)
                    {
                        registers.@byte![Vx] = (byte)i;
                        keyPressed = true;
                        break;
                    }
                }

                if (!keyPressed)
                    PC -= 2;
            }
        }

        private void OP_Fx15(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            DT = registers.@byte![Vx];
        }

        private void OP_Fx18(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            ST = registers.@byte![Vx];
        }

        private void OP_Fx1E(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            I = I + registers.@byte![Vx] & 0xFFFF;
        }

        private void OP_Fx29(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint digit = registers.@byte![Vx];
            // Point I to the font sprite for the digit at the configurable font base
            I = (uint)((fontBaseAddress + (int)digit * 5) & 0xFFFF);
        }

        private void OP_Fx33(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            uint value = registers.@byte![Vx];
            uint h = value / 100;
            uint t = (value - h * 100) / 10;
            uint u = value - h * 100 - t * 10;
            memory.@byte![I] = (byte)h;
            memory.@byte![I + 1] = (byte)t;
            memory.@byte![I + 2] = (byte)u;
        }

        private void OP_Fx55(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            for (uint i = 0; i <= Vx; i++)
                memory.@byte![I + i] = registers.@byte![i];
            // Original COSMAC VIP increments I; memoryQuirk disables this for modern ROMs
            if (!memoryQuirk)
                I = I + Vx + 1 & 0xFFFF;
        }

        private void OP_Fx65(uint opcode)
        {
            uint Vx = (opcode & 0x0F00) >> 8;
            for (uint i = 0; i <= Vx; i++)
                registers.@byte![i] = memory.@byte![I + i];
            // Original COSMAC VIP increments I; memoryQuirk disables this for modern ROMs
            if (!memoryQuirk)
                I = I + Vx + 1 & 0xFFFF;
        }
    }
}
