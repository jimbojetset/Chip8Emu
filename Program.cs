using Chip8Emu.CORE;
using ImGuiNET;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;

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
        private static volatile bool _redrawRequested = true;

        // Embedded test ROM - displays "CHIP8" logo
        private static readonly byte[] EmbeddedRom = {
            0x00,0xE0,0x60,0x00,0x61,0x00,0x62,0x08,0xA2,0x20,0x40,0x40,0x22,0x1A,0x41,0x20,0x12,0x10,0xD0,0x18,
            0xF2,0x1E,0x70,0x08,0x12,0x0A,0x60,0x00,0x71,0x08,0x00,0xEE,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x7F,0x40,0x5F,0x50,0x57,0x54,0x54,0x00,0xFC,0x04,0xF4,
            0x14,0xD4,0x54,0x54,0x00,0x3F,0x20,0x2F,0x28,0x2B,0x2A,0x2A,0x00,0xFE,0x02,0xFA,0x0A,0xEA,0x2A,0x2A,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x54,0x54,0x54,0x54,0x54,0x54,0x74,0x00,
            0x54,0x54,0x54,0x54,0x74,0x00,0x00,0x00,0x2A,0x2A,0x2A,0x2A,0x2A,0x2A,0x3B,0x00,0x2A,0x2A,0x2A,0x2A,
            0x2A,0x2A,0xEE,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x74,0x54,0x54,0x54,
            0x54,0x54,0x54,0x54,0x00,0x00,0x74,0x54,0x54,0x54,0x54,0x54,0x3B,0x2A,0x2A,0x2A,0x2A,0x2A,0x2A,0x2A,
            0xEE,0x2A,0x2A,0x2A,0x2A,0x2A,0x2A,0x2A,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x54,0x54,0x57,0x50,0x5F,0x40,0x7F,0x00,0x54,0x54,0xD4,0x14,0xF4,0x04,0xFC,0x00,0x2A,0x2A,0x2B,0x28,
            0x2F,0x20,0x3F,0x00,0x2A,0x2A,0xEA,0x0A,0xFA,0x02,0xFE,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00
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
            bool keyReleaseWaitQuirk = true;

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
                            case "k": keyReleaseWaitQuirk = value; break;
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
            Console.WriteLine($"Quirks: shift={shiftQuirk}, jump={jumpQuirk}, vfReset={vfReset}, memory={memoryQuirk}, clipping={clippingQuirk}, dispWait={displayWaitQuirk}, keyWaitRelease={keyReleaseWaitQuirk}");

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
                    KeyReleaseWaitQuirk = keyReleaseWaitQuirk,
                    FrameSize = 4
                };
                _chip8.OnDisplayUpdate = () => _redrawRequested = true;

                // Create settings window with ROM load callback
                bool startSettingsCollapsed = hasCommandLineSwitches ? UiLayoutDefaults.SettingsWindowStartsCollapsed : false;
                _settingsWindow = new SettingsWindow(_chip8, LoadRom, startSettingsCollapsed, () => _redrawRequested = true);
                if (romPath != null)
                {
                    _settingsWindow.IsVisible = false;
                }
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
                var frameStopwatch = Stopwatch.StartNew();
                double lastFrameSeconds = frameStopwatch.Elapsed.TotalSeconds;
                const double targetFrameSeconds = 1.0 / 60.0;

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
                        _redrawRequested = true;
                    }

                    // Block briefly for events to avoid high CPU when idle.
                    int waitTimeoutMs = _settingsWindow?.IsVisible == true ? 1 : 8;
                    bool sawEvent = _window.ProcessEvents(_chip8, waitTimeoutMs);

                    bool shouldRender = _redrawRequested || sawEvent;
                    if (shouldRender)
                    {
                        // Consume the current frame request up-front so requests raised during Draw()
                        // persist and schedule the next frame immediately.
                        _redrawRequested = false;

                        // Calculate delta time from monotonic clock only when drawing a new frame.
                        double currentFrameSeconds = frameStopwatch.Elapsed.TotalSeconds;
                        float deltaTime = (float)(currentFrameSeconds - lastFrameSeconds);
                        lastFrameSeconds = currentFrameSeconds;

                        // Begin ImGui frame
                        _window.BeginFrame(deltaTime);

                        // Toggle settings window with F1
                        if (ImGui.IsKeyPressed(ImGuiKey.F1, false) && _settingsWindow != null)
                        {
                            _settingsWindow.IsVisible = !_settingsWindow.IsVisible;
                            _redrawRequested = true;
                        }

                        // Draw settings window
                        _settingsWindow?.Draw();

                        // Show current ROM title in the window title
                        _window.SetRomTitle(_settingsWindow?.CurrentRomTitle);

                        _videoBuffer = _chip8.GetVideoBuffer();
                        _window.Render(_videoBuffer);

                        // Keep a frame cap fallback when VSync doesn't block (common on some platforms/drivers).
                        double frameElapsed = frameStopwatch.Elapsed.TotalSeconds - currentFrameSeconds;
                        double frameRemaining = targetFrameSeconds - frameElapsed;
                        while (frameRemaining > 0)
                        {
                            if (frameRemaining > 0.002)
                            {
                                Thread.Sleep((int)((frameRemaining - 0.001) * 1000));
                            }
                            else
                            {
                                Thread.Yield();
                            }

                            frameElapsed = frameStopwatch.Elapsed.TotalSeconds - currentFrameSeconds;
                            frameRemaining = targetFrameSeconds - frameElapsed;
                        }
                    }
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