using System.Runtime.InteropServices;
using Chip8Emu.CORE;
using SDL2;
using Silk.NET.OpenGL;
using static SDL2.SDL;

namespace Chip8Emu
{
    public class SDL2Window : IDisposable
    {
        private const string BaseWindowTitle = "Chip8 Emulator";
        private const int CHIP8_WIDTH = 64;
        private const int CHIP8_HEIGHT = 32;
        private const int SCALE = 15;
        private const int WINDOW_WIDTH = CHIP8_WIDTH * SCALE;
        private const int WINDOW_HEIGHT = CHIP8_HEIGHT * SCALE;

        private IntPtr _window;
        private IntPtr _glContext;
        private GL? _gl;
        private uint _chip8Texture;
        private uint _quadVao;
        private uint _quadVbo;
        private uint _quadShader;
        private int _texUniformLoc;
        private bool _disposed = false;
        private string? _lastWindowTitle;
        private readonly float[] _textureData = new float[CHIP8_WIDTH * CHIP8_HEIGHT * 3];
        private readonly byte[] _lastVideoBuffer = new byte[CHIP8_WIDTH * CHIP8_HEIGHT];
        private bool _textureInitialized = false;

        private ImGuiController? _imguiController;

        public bool IsRunning { get; private set; } = true;
        public GL? GL => _gl;
        public IntPtr WindowHandle => _window;
        public int Width => WINDOW_WIDTH;
        public int Height => WINDOW_HEIGHT;

        // Color configuration (normalized 0-1)
        private readonly float[] _onColor = { 50f / 255f, 205f / 255f, 50f / 255f }; // LimeGreen
        private readonly float[] _offColor = { 0f, 0f, 0f }; // Black

        public SDL2Window()
        {
            if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_AUDIO) < 0)
            {
                throw new Exception($"SDL could not initialize! SDL_Error: {SDL_GetError()}");
            }

            // Request OpenGL 3.3 Core Profile
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_CONTEXT_MAJOR_VERSION, 3);
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_CONTEXT_MINOR_VERSION, 3);
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_CONTEXT_PROFILE_MASK, (int)SDL_GLprofile.SDL_GL_CONTEXT_PROFILE_CORE);
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_DOUBLEBUFFER, 1);
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_DEPTH_SIZE, 24);

            _window = SDL_CreateWindow(
                BaseWindowTitle,
                SDL_WINDOWPOS_CENTERED,
                SDL_WINDOWPOS_CENTERED,
                WINDOW_WIDTH,
                WINDOW_HEIGHT,
                SDL_WindowFlags.SDL_WINDOW_SHOWN | SDL_WindowFlags.SDL_WINDOW_OPENGL
            );

            if (_window == IntPtr.Zero)
            {
                throw new Exception($"Window could not be created! SDL_Error: {SDL_GetError()}");
            }

            _glContext = SDL_GL_CreateContext(_window);
            if (_glContext == IntPtr.Zero)
            {
                throw new Exception($"OpenGL context could not be created! SDL_Error: {SDL_GetError()}");
            }

            SDL_GL_MakeCurrent(_window, _glContext);
            SDL_GL_SetSwapInterval(1); // Enable VSync

            // Initialize OpenGL bindings
            _gl = GL.GetApi(SDL_GL_GetProcAddress);

            // Create CHIP-8 display texture
            CreateChip8Texture();

            // Create quad for rendering CHIP-8 display
            CreateQuadResources();

            // Initialize ImGui
            _imguiController = new ImGuiController(_gl, _window, WINDOW_WIDTH, WINDOW_HEIGHT);
        }

        private unsafe void CreateChip8Texture()
        {
            _chip8Texture = _gl!.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _chip8Texture);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgb, CHIP8_WIDTH, CHIP8_HEIGHT, 0, PixelFormat.Rgb, PixelType.Float, null);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }

        private unsafe void CreateQuadResources()
        {
            // Simple fullscreen quad vertices (position + texcoord)
            float[] quadVertices = {
                // positions   // texcoords
                -1f,  1f,      0f, 0f,  // top-left
                -1f, -1f,      0f, 1f,  // bottom-left
                 1f, -1f,      1f, 1f,  // bottom-right

                -1f,  1f,      0f, 0f,  // top-left
                 1f, -1f,      1f, 1f,  // bottom-right
                 1f,  1f,      1f, 0f   // top-right
            };

            // Create VAO and VBO
            _quadVao = _gl!.GenVertexArray();
            _quadVbo = _gl.GenBuffer();

            _gl.BindVertexArray(_quadVao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _quadVbo);

            fixed (float* v = quadVertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quadVertices.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
            }

            // Position attribute
            _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
            _gl.EnableVertexAttribArray(0);

            // Texcoord attribute
            _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
            _gl.EnableVertexAttribArray(1);

            _gl.BindVertexArray(0);

            // Create simple shader
            string vertexShaderSrc = @"
                #version 330 core
                layout (location = 0) in vec2 aPos;
                layout (location = 1) in vec2 aTexCoord;
                out vec2 TexCoord;
                void main() {
                    gl_Position = vec4(aPos, 0.0, 1.0);
                    TexCoord = aTexCoord;
                }
            ";

            string fragmentShaderSrc = @"
                #version 330 core
                in vec2 TexCoord;
                out vec4 FragColor;
                uniform sampler2D uTexture;
                void main() {
                    FragColor = texture(uTexture, TexCoord);
                }
            ";

            uint vs = CompileShader(ShaderType.VertexShader, vertexShaderSrc);
            uint fs = CompileShader(ShaderType.FragmentShader, fragmentShaderSrc);

            _quadShader = _gl.CreateProgram();
            _gl.AttachShader(_quadShader, vs);
            _gl.AttachShader(_quadShader, fs);
            _gl.LinkProgram(_quadShader);

            _gl.DeleteShader(vs);
            _gl.DeleteShader(fs);

            _texUniformLoc = _gl.GetUniformLocation(_quadShader, "uTexture");
        }

        private uint CompileShader(ShaderType type, string source)
        {
            uint shader = _gl!.CreateShader(type);
            _gl.ShaderSource(shader, source);
            _gl.CompileShader(shader);

            _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int success);
            if (success == 0)
            {
                string info = _gl.GetShaderInfoLog(shader);
                throw new Exception($"Shader compilation failed: {info}");
            }

            return shader;
        }

        public bool ProcessEvents(Chip8 chip8, int waitTimeoutMs = 0)
        {
            bool sawEvent = false;

            // Optional blocking wait to avoid a tight poll loop when idle.
            if (waitTimeoutMs > 0 && SDL_WaitEventTimeout(out SDL_Event waitedEvent, waitTimeoutMs) != 0)
            {
                sawEvent = true;
                HandleEvent(waitedEvent, chip8);
            }

            while (SDL_PollEvent(out SDL_Event e) != 0)
            {
                sawEvent = true;
                HandleEvent(e, chip8);
            }

            return sawEvent;
        }

        private void HandleEvent(SDL_Event e, Chip8 chip8)
        {
            // Forward events to ImGui
            _imguiController?.ProcessEvent(e);

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

        public void BeginFrame(float deltaTime)
        {
            _imguiController?.NewFrame(deltaTime);
        }

        public unsafe void Render(byte[] videoBuffer)
        {
            var gl = _gl;
            if (gl == null)
                return;

            bool textureChanged = !_textureInitialized;
            if (!textureChanged)
            {
                for (int i = 0; i < _lastVideoBuffer.Length; i++)
                {
                    if (_lastVideoBuffer[i] != videoBuffer[i])
                    {
                        textureChanged = true;
                        break;
                    }
                }
            }

            if (textureChanged)
            {
                // Convert CHIP-8 video buffer to RGB float texture data only when content changes.
                for (int y = 0; y < CHIP8_HEIGHT; y++)
                {
                    for (int x = 0; x < CHIP8_WIDTH; x++)
                    {
                        int bufIndex = y * CHIP8_WIDTH + x;
                        int texIndex = (y * CHIP8_WIDTH + x) * 3;
                        bool isOn = videoBuffer[bufIndex] != 0;
                        float[] color = isOn ? _onColor : _offColor;
                        _textureData[texIndex + 0] = color[0];
                        _textureData[texIndex + 1] = color[1];
                        _textureData[texIndex + 2] = color[2];
                    }
                }

                System.Buffer.BlockCopy(videoBuffer, 0, _lastVideoBuffer, 0, _lastVideoBuffer.Length);
                _textureInitialized = true;

                // Upload texture data
                gl.BindTexture(TextureTarget.Texture2D, _chip8Texture);
                fixed (float* data = _textureData)
                {
                    gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, CHIP8_WIDTH, CHIP8_HEIGHT, PixelFormat.Rgb, PixelType.Float, data);
                }
            }

            // Clear screen
            gl.ClearColor(0f, 0f, 0f, 1f);
            gl.Clear(ClearBufferMask.ColorBufferBit);

            // Draw CHIP-8 display
            gl.UseProgram(_quadShader);
            gl.Uniform1(_texUniformLoc, 0);
            gl.ActiveTexture(TextureUnit.Texture0);
            gl.BindTexture(TextureTarget.Texture2D, _chip8Texture);
            gl.BindVertexArray(_quadVao);
            gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
            gl.BindVertexArray(0);

            // Render ImGui on top
            _imguiController?.Render();

            // Swap buffers
            SDL_GL_SwapWindow(_window);
        }

        public uint GetChip8TextureId()
        {
            return _chip8Texture;
        }

        public void SetRomTitle(string? romTitle)
        {
            string title = string.IsNullOrWhiteSpace(romTitle)
                ? BaseWindowTitle
                : $"{BaseWindowTitle} - {romTitle}";

            if (string.Equals(_lastWindowTitle, title, StringComparison.Ordinal))
                return;

            SDL_SetWindowTitle(_window, title);
            _lastWindowTitle = title;
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
                if (disposing)
                {
                    _imguiController?.Dispose();
                }

                if (_gl != null)
                {
                    _gl.DeleteVertexArray(_quadVao);
                    _gl.DeleteBuffer(_quadVbo);
                    _gl.DeleteTexture(_chip8Texture);
                    _gl.DeleteProgram(_quadShader);
                }

                if (_glContext != IntPtr.Zero)
                {
                    SDL_GL_DeleteContext(_glContext);
                    _glContext = IntPtr.Zero;
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
