using System.Reflection;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace Chip8Emu
{
    internal static class Program
    {
        private static SDL2Window? _window;
        private static Chip8? _chip8;
        private static byte[]? _videoBuffer;
        private static SettingsWindow? _settingsWindow;
        private static string _pendingRomPath = "";
        private static readonly object _romLoadLock = new();
        private static bool _useEmbeddedRom = false;

        // Embedded test ROM - displays "CHIP8" logo
        private static readonly byte[] EmbeddedRom = {
            0x00, 0xe0, 0x61, 0x01, 0x60, 0x08, 0xa2, 0x50, 0xd0, 0x1f, 0x60, 0x10,
            0xa2, 0x5f, 0xd0, 0x1f, 0x60, 0x18, 0xa2, 0x6e, 0xd0, 0x1f, 0x60, 0x20,
            0xa2, 0x7d, 0xd0, 0x1f, 0x60, 0x28, 0xa2, 0x8c, 0xd0, 0x1f, 0x60, 0x30,
            0xa2, 0x9b, 0xd0, 0x1f, 0x61, 0x10, 0x60, 0x08, 0xa2, 0xaa, 0xd0, 0x1f,
            0x60, 0x10, 0xa2, 0xb9, 0xd0, 0x1f, 0x60, 0x18, 0xa2, 0xc8, 0xd0, 0x1f,
            0x60, 0x20, 0xa2, 0xd7, 0xd0, 0x1f, 0x60, 0x28, 0xa2, 0xe6, 0xd0, 0x1f,
            0x60, 0x30, 0xa2, 0xf5, 0xd0, 0x1f, 0x12, 0x4e, 0x0f, 0x02, 0x02, 0x02,
            0x02, 0x02, 0x00, 0x00, 0x1f, 0x3f, 0x71, 0xe0, 0xe5, 0xe0, 0xe8, 0xa0,
            0x0d, 0x2a, 0x28, 0x28, 0x28, 0x00, 0x00, 0x18, 0xb8, 0xb8, 0x38, 0x38,
            0x3f, 0xbf, 0x00, 0x19, 0xa5, 0xbd, 0xa1, 0x9d, 0x00, 0x00, 0x0c, 0x1d,
            0x1d, 0x01, 0x0d, 0x1d, 0x9d, 0x01, 0xc7, 0x29, 0x29, 0x29, 0x27, 0x00,
            0x00, 0xf8, 0xfc, 0xce, 0xc6, 0xc6, 0xc6, 0xc6, 0x00, 0x49, 0x4a, 0x49,
            0x48, 0x3b, 0x00, 0x00, 0x00, 0x01, 0x03, 0x03, 0x03, 0x01, 0xf0, 0x30,
            0x90, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00, 0xfe, 0xc7, 0x83, 0x83, 0x83,
            0xc6, 0xfc, 0xe7, 0xe0, 0xe0, 0xe0, 0xe0, 0x71, 0x3f, 0x1f, 0x00, 0x00,
            0x07, 0x02, 0x02, 0x02, 0x02, 0x39, 0x38, 0x38, 0x38, 0x38, 0xb8, 0xb8,
            0x38, 0x00, 0x00, 0x31, 0x4a, 0x79, 0x40, 0x3b, 0xdd, 0xdd, 0xdd, 0xdd,
            0xdd, 0xdd, 0xdd, 0xdd, 0x00, 0x00, 0xa0, 0x38, 0x20, 0xa0, 0x18, 0xce,
            0xfc, 0xf8, 0xc0, 0xd4, 0xdc, 0xc4, 0xc5, 0x00, 0x00, 0x30, 0x44, 0x24,
            0x14, 0x63, 0xf1, 0x03, 0x07, 0x07, 0x77, 0x17, 0x63, 0x71, 0x00, 0x00,
            0x28, 0x8e, 0xa8, 0xa8, 0xa6, 0xce, 0x87, 0x03, 0x03, 0x03, 0x87, 0xfe,
            0xfc, 0x00, 0x00, 0x60, 0x90, 0xf0, 0x80, 0x70
        };

        static Program()
        {
            NativeLibrary.SetDllImportResolver(typeof(SDL2.SDL).Assembly, ResolveSdl2);
        }

        private static IntPtr ResolveSdl2(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName != "SDL2")
                return IntPtr.Zero;

            // Try common SDL2 library locations
            string[] candidates = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? new[]
                {
                    "/opt/homebrew/lib/libSDL2.dylib",      // Apple Silicon Homebrew
                    "/usr/local/lib/libSDL2.dylib",         // Intel Homebrew
                    "/opt/local/lib/libSDL2.dylib",         // MacPorts
                    "libSDL2.dylib"
                }
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? new[] { "libSDL2-2.0.so.0", "libSDL2.so" }
                : new[] { "SDL2.dll" };

            foreach (var candidate in candidates)
            {
                if (NativeLibrary.TryLoad(candidate, out IntPtr handle))
                    return handle;
            }

            return IntPtr.Zero;
        }

        private static void Main(string[] args)
        {
            // Parse command line arguments
            string? romPath = null;
            bool hasCommandLineSwitches = false;
            bool shiftQuirk = false;
            bool jumpQuirk = false;
            bool vfReset = true;
            bool memoryQuirk = false;
            bool clippingQuirk = false;
            bool displayWaitQuirk = true;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--"))
                {
                    hasCommandLineSwitches = true;
                    string option = args[i][2..];
                    if (i + 1 < args.Length && (args[i + 1] == "0" || args[i + 1] == "1"))
                    {
                        bool value = args[i + 1] == "1";
                        switch (option)
                        {
                            case "s": shiftQuirk = value; break;
                            case "j": jumpQuirk = value; break;
                            case "v": vfReset = value; break;
                            case "m": memoryQuirk = value; break;
                            case "c": clippingQuirk = value; break;
                            case "d": displayWaitQuirk = value; break;
                        }
                        i++; // Skip the value
                    }
                }
                else if (!args[i].StartsWith("-"))
                {
                    romPath = args[i];
                }
            }

            // Use embedded ROM if no path specified
            _useEmbeddedRom = romPath == null;
            if (_useEmbeddedRom)
            {
                Console.WriteLine("Loading embedded ROM (CHIP8 logo)");
            }
            else
            {
                Console.WriteLine($"Loading ROM: {romPath}");
            }
            Console.WriteLine($"Quirks: shift={shiftQuirk}, jump={jumpQuirk}, vfReset={vfReset}, memory={memoryQuirk}, clipping={clippingQuirk}, dispWait={displayWaitQuirk}");

            using (_window = new SDL2Window())
            {
                Console.WriteLine("SDL2 window created successfully");
                Sound.Initialize();
                _chip8 = new Chip8
                {
                    ShiftQuirk = shiftQuirk,
                    VFReset = vfReset,
                    JumpQuirk = jumpQuirk,
                    MemoryQuirk = memoryQuirk,
                    ClippingQuirk = clippingQuirk,
                    DisplayWaitQuirk = displayWaitQuirk,
                    FrameSize = 4
                };

                // Create settings window with ROM load callback
                bool startSettingsCollapsed = hasCommandLineSwitches ? UiLayoutDefaults.SettingsWindowStartsCollapsed : false;
                _settingsWindow = new SettingsWindow(_chip8, LoadRom, startSettingsCollapsed);
                if (!_useEmbeddedRom && romPath != null)
                {
                    _settingsWindow.SetCurrentRom(romPath);
                }
                else
                {
                    _settingsWindow.SetCurrentRom("[Embedded CHIP8 Logo]");
                }

                // Start emulator in background thread
                StartEmulator(romPath);

                // Main loop - keep window open until user closes it
                Console.WriteLine("Entering main loop...");
                var lastFrameTime = DateTime.Now;

                while (_window.IsRunning)
                {
                    // Check for pending ROM load
                    string? romToLoad = null;
                    lock (_romLoadLock)
                    {
                        if (!string.IsNullOrEmpty(_pendingRomPath))
                        {
                            romToLoad = _pendingRomPath;
                            _pendingRomPath = "";
                        }
                    }

                    if (romToLoad != null)
                    {
                        _chip8.Stop();
                        Sound.StopSound();
                        Thread.Sleep(50); // Give emulator thread time to stop
                        StartEmulator(romToLoad);
                        _settingsWindow?.SetCurrentRom(romToLoad);
                    }

                    // Calculate delta time
                    var currentTime = DateTime.Now;
                    float deltaTime = (float)(currentTime - lastFrameTime).TotalSeconds;
                    lastFrameTime = currentTime;

                    _window.ProcessEvents(_chip8);

                    // Begin ImGui frame
                    _window.BeginFrame(deltaTime);

                    // Toggle settings window with F1
                    if (ImGui.IsKeyPressed(ImGuiKey.F1) && _settingsWindow != null)
                    {
                        _settingsWindow.IsVisible = !_settingsWindow.IsVisible;
                    }

                    // Draw settings window
                    _settingsWindow?.Draw();

                    // Update window title with running status
                    _window.UpdateTitle(_chip8.Running);

                    // Always render (ImGui needs continuous updates)
                    _videoBuffer = _chip8.GetVideoBuffer();
                    _window.Render(_videoBuffer);

                    // VSync handles frame timing now
                }

                _chip8.Stop();
                Sound.Cleanup();
            }
        }

        private static void StartEmulator(string? romPath)
        {
            Thread chip8Thread;
            if (romPath == null || _useEmbeddedRom)
            {
                // Use embedded ROM
                chip8Thread = new Thread(() => _chip8!.Start(EmbeddedRom))
                {
                    IsBackground = true
                };
                _useEmbeddedRom = false; // Only use embedded on first start
            }
            else
            {
                chip8Thread = new Thread(() => _chip8!.Start(romPath))
                {
                    IsBackground = true
                };
            }
            chip8Thread.Start();

            // Wait for emulator to start
            while (!_chip8!.Running && chip8Thread.IsAlive)
            {
                Thread.Sleep(10);
            }

            Console.WriteLine($"Started emulator with ROM: {romPath ?? "[Embedded]"}");
        }

        private static void LoadRom(string romPath)
        {
            lock (_romLoadLock)
            {
                _pendingRomPath = romPath;
            }
            Console.WriteLine($"Queued ROM for loading: {romPath}");
        }
    }
}