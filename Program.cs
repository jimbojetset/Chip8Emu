using System.Reflection;
using System.Runtime.InteropServices;

namespace Chip8Emu
{
    internal static class Program
    {
        private static SDL2Window? _window;
        private static Chip8? _chip8;
        private static byte[]? _videoBuffer;
        private static bool _needsRender = true;

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
            string romPath = "test.ROM";
            bool shiftQuirk = false;
            bool jumpQuirk = false;
            bool vfReset = false;
            bool memoryQuirk = false;
            bool clippingQuirk = false;
            bool displayWaitQuirk = false;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--"))
                {
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

            Console.WriteLine($"Loading ROM: {romPath}");
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

                // Set up display callback
                _chip8.OnDisplayUpdate = () => _needsRender = true;

                // Start emulator in background thread
                var chip8Thread = new Thread(() => _chip8.Start(romPath))
                {
                    IsBackground = true
                };
                chip8Thread.Start();

                // Wait for emulator to start
                while (!_chip8.Running && chip8Thread.IsAlive)
                {
                    Thread.Sleep(10);
                }

                // Main loop - keep window open until user closes it
                Console.WriteLine("Entering main loop...");
                while (_window.IsRunning)
                {
                    _window.ProcessEvents(_chip8);

                    if (_needsRender)
                    {
                        _videoBuffer = _chip8.GetVideoBuffer();
                        _window.Render(_videoBuffer);
                        _needsRender = false;
                    }

                    // Cap at ~60 FPS
                    Thread.Sleep(16);
                }

                _chip8.Stop();
                Sound.Cleanup();
            }
        }
    }
}