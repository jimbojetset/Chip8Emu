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
        private static AudioDeviceSelector? _audioSelector;
        private static string _pendingRomPath = "";
        private static readonly object _romLoadLock = new();
        private static bool _useEmbeddedRom = false;
        private static volatile bool _redrawRequested = true;

        // Embedded test ROM - displays "CHIP8 EMULATOR" logo
        private static readonly byte[] EmbeddedRom = {
            0x61, 0x02, 0x60, 0x0C, 0xA2, 0x54, 0xD0, 0x1F, 0x60, 0x14, 0xA2, 0x63, 0xD0, 0x1F,
            0x60, 0x1C, 0xA2, 0x72, 0xD0, 0x1F, 0x60, 0x24, 0xA2, 0x81, 0xD0, 0x1F, 0x60, 0x2C, 
            0xA2, 0x90, 0xD0, 0x1F, 0x61, 0x16, 0x60, 0x08, 0xA2, 0x9F, 0xD0, 0x15, 0x60, 0x0E, 
            0xA2, 0xA4, 0xD0, 0x15, 0x60, 0x14, 0xA2, 0xA9, 0xD0, 0x15, 0x60, 0x1A, 0xA2, 0xAE, 
            0xD0, 0x15, 0x60, 0x20, 0xA2, 0xB3, 0xD0, 0x15, 0x60, 0x26, 0xA2, 0xB8, 0xD0, 0x15, 
            0x60, 0x2C, 0xA2, 0xBD, 0xD0, 0x15, 0x60, 0x32, 0xA2, 0xC2, 0xD0, 0x15, 0x12, 0x52, 
            0x7C, 0xFE, 0xC6, 0xC0, 0xC0, 0xC0, 0xC0, 0xC0, 0xC0, 0xC0, 0xC0, 0xC6, 0xC6, 0xFE, 
            0x7C, 0xC6, 0xC6, 0xC6, 0xC6, 0xC6, 0xC6, 0xC6, 0xFE, 0xC6, 0xC6, 0xC6, 0xC6, 0xC6, 
            0xC6, 0xC6, 0xFE, 0xFE, 0x38, 0x38, 0x38, 0x38, 0x38, 0x38, 0x38, 0x38, 0x38, 0x38, 
            0x38, 0xFE, 0xFE, 0xFC, 0xFE, 0xC6, 0xC6, 0xC6, 0xC6, 0xFE, 0xFC, 0xC0, 0xC0, 0xC0, 
            0xC0, 0xC0, 0xC0, 0xC0, 0x7C, 0xFE, 0xC6, 0xC6, 0xC6, 0x7C, 0x7C, 0x7C, 0xC6, 0xC6, 
            0xC6, 0xC6, 0xC6, 0xFE, 0x7C, 0xF8, 0x80, 0xF0, 0x80, 0xF8, 0x88, 0xD8, 0xA8, 0x88, 
            0x88, 0x88, 0x88, 0x88, 0x88, 0x70, 0x80, 0x80, 0x80, 0x80, 0xF8, 0x70, 0x88, 0xF8, 
            0x88, 0x88, 0xF8, 0x20, 0x20, 0x20, 0x20, 0x70, 0x88, 0x88, 0x88, 0x70, 0xF0, 0x88, 
            0xF0, 0xA0, 0x90
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

                // Enumerate audio devices. If only one (or none) is available, open it
                // immediately. Otherwise defer initialization until the user picks one
                // through the modal popup drawn below.
                var audioDevices = Sound.EnumerateDevices();
                _audioSelector = new AudioDeviceSelector(audioDevices);
                if (_audioSelector.IsCompleted)
                {
                    Sound.Initialize(_audioSelector.SelectedDeviceName);
                }
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

                    bool shouldRender = _redrawRequested || sawEvent || _settingsWindow?.IsVisible == true;
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

                        // Draw the audio device selection modal (only renders while pending).
                        if (_audioSelector != null && !_audioSelector.IsCompleted)
                        {
                            _audioSelector.Draw();
                            if (_audioSelector.IsCompleted)
                            {
                                Sound.Initialize(_audioSelector.SelectedDeviceName);
                            }
                            _redrawRequested = true;
                        }

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