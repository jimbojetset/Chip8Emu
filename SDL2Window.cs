using SDL2;
using static SDL2.SDL;

namespace Chip8Emu
{
    public class SDL2Window : IDisposable
    {
        private const int CHIP8_WIDTH = 64;
        private const int CHIP8_HEIGHT = 32;
        private const int SCALE = 10;
        private const int WINDOW_WIDTH = CHIP8_WIDTH * SCALE;
        private const int WINDOW_HEIGHT = CHIP8_HEIGHT * SCALE;

        private IntPtr _window;
        private IntPtr _renderer;
        private IntPtr _texture;
        private bool _disposed = false;

        public bool IsRunning { get; private set; } = true;

        // Color configuration
        private readonly SDL_Color _onColor = new() { r = 50, g = 205, b = 50, a = 255 }; // LimeGreen
        private readonly SDL_Color _offColor = new() { r = 0, g = 0, b = 0, a = 255 }; // Black

        public SDL2Window()
        {
            if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_AUDIO) < 0)
            {
                throw new Exception($"SDL could not initialize! SDL_Error: {SDL_GetError()}");
            }

            _window = SDL_CreateWindow(
                "Chip8 Emulator",
                SDL_WINDOWPOS_CENTERED,
                SDL_WINDOWPOS_CENTERED,
                WINDOW_WIDTH,
                WINDOW_HEIGHT,
                SDL_WindowFlags.SDL_WINDOW_SHOWN
            );

            if (_window == IntPtr.Zero)
            {
                throw new Exception($"Window could not be created! SDL_Error: {SDL_GetError()}");
            }

            _renderer = SDL_CreateRenderer(_window, -1, SDL_RendererFlags.SDL_RENDERER_ACCELERATED);
            if (_renderer == IntPtr.Zero)
            {
                throw new Exception($"Renderer could not be created! SDL_Error: {SDL_GetError()}");
            }

            _texture = SDL_CreateTexture(
                _renderer,
                SDL_PIXELFORMAT_RGBA8888,
                (int)SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING,
                CHIP8_WIDTH,
                CHIP8_HEIGHT
            );

            if (_texture == IntPtr.Zero)
            {
                throw new Exception($"Texture could not be created! SDL_Error: {SDL_GetError()}");
            }
        }

        public void ProcessEvents(Chip8 chip8)
        {
            while (SDL_PollEvent(out SDL_Event e) != 0)
            {
                switch (e.type)
                {
                    case SDL_EventType.SDL_QUIT:
                        IsRunning = false;
                        chip8.Stop();
                        break;

                    case SDL_EventType.SDL_KEYDOWN:
                        HandleKeyDown(e.key.keysym.sym, chip8);
                        break;

                    case SDL_EventType.SDL_KEYUP:
                        HandleKeyUp(e.key.keysym.sym, chip8);
                        break;
                }
            }
        }

        private void HandleKeyDown(SDL_Keycode key, Chip8 chip8)
        {
            uint? chipKey = MapKey(key);
            if (chipKey.HasValue)
            {
                chip8.KeyDown = chipKey.Value;
            }

            // ESC to quit
            if (key == SDL_Keycode.SDLK_ESCAPE)
            {
                IsRunning = false;
                chip8.Stop();
            }
        }

        private void HandleKeyUp(SDL_Keycode key, Chip8 chip8)
        {
            uint? chipKey = MapKey(key);
            if (chipKey.HasValue)
            {
                chip8.KeyUp = chipKey.Value;
            }
        }

        private static uint? MapKey(SDL_Keycode key)
        {
            return key switch
            {
                SDL_Keycode.SDLK_1 => 1,
                SDL_Keycode.SDLK_2 => 2,
                SDL_Keycode.SDLK_3 => 3,
                SDL_Keycode.SDLK_4 => 12,
                SDL_Keycode.SDLK_q => 4,
                SDL_Keycode.SDLK_w => 5,
                SDL_Keycode.SDLK_e => 6,
                SDL_Keycode.SDLK_r => 13,
                SDL_Keycode.SDLK_a => 7,
                SDL_Keycode.SDLK_s => 8,
                SDL_Keycode.SDLK_d => 9,
                SDL_Keycode.SDLK_f => 14,
                SDL_Keycode.SDLK_z => 10,
                SDL_Keycode.SDLK_x => 0,
                SDL_Keycode.SDLK_c => 11,
                SDL_Keycode.SDLK_v => 15,
                _ => null
            };
        }

        public unsafe void Render(byte[] videoBuffer)
        {
            // Lock texture for pixel access
            if (SDL_LockTexture(_texture, IntPtr.Zero, out IntPtr pixels, out int pitch) != 0)
            {
                Console.WriteLine($"Failed to lock texture: {SDL_GetError()}");
                return;
            }

            uint* pixelPtr = (uint*)pixels;

            for (int y = 0; y < CHIP8_HEIGHT; y++)
            {
                for (int x = 0; x < CHIP8_WIDTH; x++)
                {
                    int index = y * CHIP8_WIDTH + x;
                    bool isOn = videoBuffer[index] != 0;

                    // RGBA8888 format
                    SDL_Color color = isOn ? _onColor : _offColor;
                    pixelPtr[y * (pitch / 4) + x] =
                        ((uint)color.r << 24) |
                        ((uint)color.g << 16) |
                        ((uint)color.b << 8) |
                        color.a;
                }
            }

            SDL_UnlockTexture(_texture);

            // Clear and render
            SDL_RenderClear(_renderer);
            SDL_RenderCopy(_renderer, _texture, IntPtr.Zero, IntPtr.Zero);
            SDL_RenderPresent(_renderer);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_texture != IntPtr.Zero)
                {
                    SDL_DestroyTexture(_texture);
                    _texture = IntPtr.Zero;
                }

                if (_renderer != IntPtr.Zero)
                {
                    SDL_DestroyRenderer(_renderer);
                    _renderer = IntPtr.Zero;
                }

                if (_window != IntPtr.Zero)
                {
                    SDL_DestroyWindow(_window);
                    _window = IntPtr.Zero;
                }

                SDL_Quit();
                _disposed = true;
            }
        }

        ~SDL2Window()
        {
            Dispose(false);
        }
    }
}
