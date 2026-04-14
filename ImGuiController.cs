using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImGuiNET;
using SDL2;
using Silk.NET.OpenGL;
using static SDL2.SDL;

namespace Chip8Emu
{
    /// <summary>
    /// Handles ImGui initialization, input processing, and rendering with SDL2 + OpenGL
    /// </summary>
    public class ImGuiController : IDisposable
    {
        private readonly GL _gl;
        private readonly IntPtr _window;

        private uint _vao;
        private uint _vbo;
        private uint _ebo;
        private uint _fontTexture;
        private uint _shaderProgram;

        private int _attribLocationTex;
        private int _attribLocationProjMtx;
        private uint _attribLocationVtxPos;
        private uint _attribLocationVtxUV;
        private uint _attribLocationVtxColor;

        private int _windowWidth;
        private int _windowHeight;

        private readonly List<char> _inputChars = new();
        private bool _disposed = false;

        public ImGuiController(GL gl, IntPtr window, int width, int height)
        {
            _gl = gl;
            _window = window;
            _windowWidth = width;
            _windowHeight = height;

            // Create ImGui context
            IntPtr context = ImGui.CreateContext();
            ImGui.SetCurrentContext(context);

            var io = ImGui.GetIO();
            io.DisplaySize = new Vector2(width, height);
            io.DisplayFramebufferScale = Vector2.One;
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;

            // Set up key mappings
            SetupKeyMappings(io);

            // Create device objects (shaders, buffers, fonts)
            CreateDeviceObjects();
        }

        private void SetupKeyMappings(ImGuiIOPtr io)
        {
            // ImGui.NET uses ImGuiKey enum directly now
        }

        private unsafe void CreateDeviceObjects()
        {
            // Create shaders
            string vertexShaderSource = @"
                #version 330 core
                layout (location = 0) in vec2 Position;
                layout (location = 1) in vec2 UV;
                layout (location = 2) in vec4 Color;
                uniform mat4 ProjMtx;
                out vec2 Frag_UV;
                out vec4 Frag_Color;
                void main()
                {
                    Frag_UV = UV;
                    Frag_Color = Color;
                    gl_Position = ProjMtx * vec4(Position.xy, 0, 1);
                }
            ";

            string fragmentShaderSource = @"
                #version 330 core
                in vec2 Frag_UV;
                in vec4 Frag_Color;
                uniform sampler2D Texture;
                layout (location = 0) out vec4 Out_Color;
                void main()
                {
                    Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
                }
            ";

            uint vertexShader = CompileShader(ShaderType.VertexShader, vertexShaderSource);
            uint fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentShaderSource);

            _shaderProgram = _gl.CreateProgram();
            _gl.AttachShader(_shaderProgram, vertexShader);
            _gl.AttachShader(_shaderProgram, fragmentShader);
            _gl.LinkProgram(_shaderProgram);

            _gl.DeleteShader(vertexShader);
            _gl.DeleteShader(fragmentShader);

            _attribLocationTex = _gl.GetUniformLocation(_shaderProgram, "Texture");
            _attribLocationProjMtx = _gl.GetUniformLocation(_shaderProgram, "ProjMtx");
            _attribLocationVtxPos = (uint)_gl.GetAttribLocation(_shaderProgram, "Position");
            _attribLocationVtxUV = (uint)_gl.GetAttribLocation(_shaderProgram, "UV");
            _attribLocationVtxColor = (uint)_gl.GetAttribLocation(_shaderProgram, "Color");

            // Create buffers
            _vao = _gl.GenVertexArray();
            _vbo = _gl.GenBuffer();
            _ebo = _gl.GenBuffer();

            _gl.BindVertexArray(_vao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);

            // Vertex attributes
            _gl.EnableVertexAttribArray(_attribLocationVtxPos);
            _gl.EnableVertexAttribArray(_attribLocationVtxUV);
            _gl.EnableVertexAttribArray(_attribLocationVtxColor);

            int stride = Unsafe.SizeOf<ImDrawVert>();
            _gl.VertexAttribPointer(_attribLocationVtxPos, 2, VertexAttribPointerType.Float, false, (uint)stride, (void*)0);
            _gl.VertexAttribPointer(_attribLocationVtxUV, 2, VertexAttribPointerType.Float, false, (uint)stride, (void*)8);
            _gl.VertexAttribPointer(_attribLocationVtxColor, 4, VertexAttribPointerType.UnsignedByte, true, (uint)stride, (void*)16);

            _gl.BindVertexArray(0);

            // Create font texture
            CreateFontTexture();
        }

        private uint CompileShader(ShaderType type, string source)
        {
            uint shader = _gl.CreateShader(type);
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

        private unsafe void CreateFontTexture()
        {
            var io = ImGui.GetIO();

            // Build font atlas
            io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);

            _fontTexture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _fontTexture);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, (void*)pixels);

            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            io.Fonts.SetTexID((IntPtr)_fontTexture);
            io.Fonts.ClearTexData();
        }

        public void ProcessEvent(SDL_Event e)
        {
            var io = ImGui.GetIO();

            switch (e.type)
            {
                case SDL_EventType.SDL_MOUSEWHEEL:
                    io.MouseWheel += e.wheel.y;
                    io.MouseWheelH += e.wheel.x;
                    break;

                case SDL_EventType.SDL_MOUSEBUTTONDOWN:
                case SDL_EventType.SDL_MOUSEBUTTONUP:
                    int button = e.button.button switch
                    {
                        (byte)SDL_BUTTON_LEFT => 0,
                        (byte)SDL_BUTTON_RIGHT => 1,
                        (byte)SDL_BUTTON_MIDDLE => 2,
                        (byte)SDL_BUTTON_X1 => 3,
                        (byte)SDL_BUTTON_X2 => 4,
                        _ => -1
                    };
                    if (button >= 0 && button < 5)
                    {
                        io.AddMouseButtonEvent(button, e.type == SDL_EventType.SDL_MOUSEBUTTONDOWN);
                    }
                    break;

                case SDL_EventType.SDL_MOUSEMOTION:
                    io.AddMousePosEvent(e.motion.x, e.motion.y);
                    break;

                case SDL_EventType.SDL_TEXTINPUT:
                    unsafe
                    {
                        string text = Marshal.PtrToStringUTF8((IntPtr)e.text.text) ?? "";
                        foreach (char c in text)
                        {
                            io.AddInputCharacter(c);
                        }
                    }
                    break;

                case SDL_EventType.SDL_KEYDOWN:
                case SDL_EventType.SDL_KEYUP:
                    bool isDown = e.type == SDL_EventType.SDL_KEYDOWN;
                    UpdateModifiers(io);
                    ImGuiKey key = TranslateKey(e.key.keysym.sym);
                    if (key != ImGuiKey.None)
                    {
                        io.AddKeyEvent(key, isDown);
                    }
                    break;
            }
        }

        private void UpdateModifiers(ImGuiIOPtr io)
        {
            SDL_Keymod mod = SDL_GetModState();
            io.AddKeyEvent(ImGuiKey.ModCtrl, (mod & SDL_Keymod.KMOD_CTRL) != 0);
            io.AddKeyEvent(ImGuiKey.ModShift, (mod & SDL_Keymod.KMOD_SHIFT) != 0);
            io.AddKeyEvent(ImGuiKey.ModAlt, (mod & SDL_Keymod.KMOD_ALT) != 0);
            io.AddKeyEvent(ImGuiKey.ModSuper, (mod & SDL_Keymod.KMOD_GUI) != 0);
        }

        private ImGuiKey TranslateKey(SDL_Keycode key)
        {
            return key switch
            {
                SDL_Keycode.SDLK_TAB => ImGuiKey.Tab,
                SDL_Keycode.SDLK_LEFT => ImGuiKey.LeftArrow,
                SDL_Keycode.SDLK_RIGHT => ImGuiKey.RightArrow,
                SDL_Keycode.SDLK_UP => ImGuiKey.UpArrow,
                SDL_Keycode.SDLK_DOWN => ImGuiKey.DownArrow,
                SDL_Keycode.SDLK_PAGEUP => ImGuiKey.PageUp,
                SDL_Keycode.SDLK_PAGEDOWN => ImGuiKey.PageDown,
                SDL_Keycode.SDLK_HOME => ImGuiKey.Home,
                SDL_Keycode.SDLK_END => ImGuiKey.End,
                SDL_Keycode.SDLK_INSERT => ImGuiKey.Insert,
                SDL_Keycode.SDLK_DELETE => ImGuiKey.Delete,
                SDL_Keycode.SDLK_BACKSPACE => ImGuiKey.Backspace,
                SDL_Keycode.SDLK_SPACE => ImGuiKey.Space,
                SDL_Keycode.SDLK_RETURN => ImGuiKey.Enter,
                SDL_Keycode.SDLK_ESCAPE => ImGuiKey.Escape,
                SDL_Keycode.SDLK_F1 => ImGuiKey.F1,
                SDL_Keycode.SDLK_F2 => ImGuiKey.F2,
                SDL_Keycode.SDLK_F3 => ImGuiKey.F3,
                SDL_Keycode.SDLK_F4 => ImGuiKey.F4,
                SDL_Keycode.SDLK_F5 => ImGuiKey.F5,
                SDL_Keycode.SDLK_F6 => ImGuiKey.F6,
                SDL_Keycode.SDLK_F7 => ImGuiKey.F7,
                SDL_Keycode.SDLK_F8 => ImGuiKey.F8,
                SDL_Keycode.SDLK_F9 => ImGuiKey.F9,
                SDL_Keycode.SDLK_F10 => ImGuiKey.F10,
                SDL_Keycode.SDLK_F11 => ImGuiKey.F11,
                SDL_Keycode.SDLK_F12 => ImGuiKey.F12,
                SDL_Keycode.SDLK_a => ImGuiKey.A,
                SDL_Keycode.SDLK_c => ImGuiKey.C,
                SDL_Keycode.SDLK_v => ImGuiKey.V,
                SDL_Keycode.SDLK_x => ImGuiKey.X,
                SDL_Keycode.SDLK_y => ImGuiKey.Y,
                SDL_Keycode.SDLK_z => ImGuiKey.Z,
                _ => ImGuiKey.None
            };
        }

        public void NewFrame(float deltaTime)
        {
            var io = ImGui.GetIO();
            io.DisplaySize = new Vector2(_windowWidth, _windowHeight);
            io.DeltaTime = deltaTime > 0 ? deltaTime : 1f / 60f;

            ImGui.NewFrame();
        }

        public void WindowResized(int width, int height)
        {
            _windowWidth = width;
            _windowHeight = height;
        }

        public unsafe void Render()
        {
            ImGui.Render();
            var drawData = ImGui.GetDrawData();

            if (drawData.CmdListsCount == 0)
                return;

            // Backup GL state
            _gl.GetInteger(GetPName.CurrentProgram, out int lastProgram);
            _gl.GetInteger(GetPName.TextureBinding2D, out int lastTexture);
            _gl.GetInteger(GetPName.ArrayBufferBinding, out int lastArrayBuffer);
            _gl.GetInteger(GetPName.VertexArrayBinding, out int lastVertexArray);
            bool lastEnableBlend = _gl.IsEnabled(EnableCap.Blend);
            bool lastEnableCullFace = _gl.IsEnabled(EnableCap.CullFace);
            bool lastEnableDepthTest = _gl.IsEnabled(EnableCap.DepthTest);
            bool lastEnableScissorTest = _gl.IsEnabled(EnableCap.ScissorTest);

            // Setup render state
            _gl.Enable(EnableCap.Blend);
            _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
            _gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
            _gl.Disable(EnableCap.CullFace);
            _gl.Disable(EnableCap.DepthTest);
            _gl.Enable(EnableCap.ScissorTest);

            // Setup orthographic projection matrix
            float L = drawData.DisplayPos.X;
            float R = drawData.DisplayPos.X + drawData.DisplaySize.X;
            float T = drawData.DisplayPos.Y;
            float B = drawData.DisplayPos.Y + drawData.DisplaySize.Y;

            Span<float> orthoProjection = stackalloc float[16]
            {
                2.0f/(R-L),     0.0f,           0.0f,   0.0f,
                0.0f,           2.0f/(T-B),     0.0f,   0.0f,
                0.0f,           0.0f,          -1.0f,   0.0f,
                (R+L)/(L-R),    (T+B)/(B-T),    0.0f,   1.0f
            };

            _gl.UseProgram(_shaderProgram);
            _gl.Uniform1(_attribLocationTex, 0);
            _gl.UniformMatrix4(_attribLocationProjMtx, 1, false, orthoProjection);

            _gl.BindVertexArray(_vao);

            Vector2 clipOff = drawData.DisplayPos;
            Vector2 clipScale = drawData.FramebufferScale;

            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                var cmdList = drawData.CmdLists[n];

                // Upload vertex/index buffers
                _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(cmdList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>()), (void*)cmdList.VtxBuffer.Data, BufferUsageARB.StreamDraw);

                _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(cmdList.IdxBuffer.Size * sizeof(ushort)), (void*)cmdList.IdxBuffer.Data, BufferUsageARB.StreamDraw);

                for (int cmd_i = 0; cmd_i < cmdList.CmdBuffer.Size; cmd_i++)
                {
                    var pcmd = cmdList.CmdBuffer[cmd_i];

                    if (pcmd.UserCallback != IntPtr.Zero)
                    {
                        // User callback - not implemented
                    }
                    else
                    {
                        Vector4 clipRect;
                        clipRect.X = (pcmd.ClipRect.X - clipOff.X) * clipScale.X;
                        clipRect.Y = (pcmd.ClipRect.Y - clipOff.Y) * clipScale.Y;
                        clipRect.Z = (pcmd.ClipRect.Z - clipOff.X) * clipScale.X;
                        clipRect.W = (pcmd.ClipRect.W - clipOff.Y) * clipScale.Y;

                        if (clipRect.X < _windowWidth && clipRect.Y < _windowHeight && clipRect.Z >= 0 && clipRect.W >= 0)
                        {
                            _gl.Scissor((int)clipRect.X, (int)(_windowHeight - clipRect.W), (uint)(clipRect.Z - clipRect.X), (uint)(clipRect.W - clipRect.Y));
                            _gl.BindTexture(TextureTarget.Texture2D, (uint)pcmd.TextureId);
                            _gl.DrawElementsBaseVertex(PrimitiveType.Triangles, pcmd.ElemCount, DrawElementsType.UnsignedShort, (void*)(pcmd.IdxOffset * sizeof(ushort)), (int)pcmd.VtxOffset);
                        }
                    }
                }
            }

            // Restore GL state
            _gl.UseProgram((uint)lastProgram);
            _gl.BindTexture(TextureTarget.Texture2D, (uint)lastTexture);
            _gl.BindVertexArray((uint)lastVertexArray);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, (uint)lastArrayBuffer);

            if (lastEnableBlend) _gl.Enable(EnableCap.Blend); else _gl.Disable(EnableCap.Blend);
            if (lastEnableCullFace) _gl.Enable(EnableCap.CullFace); else _gl.Disable(EnableCap.CullFace);
            if (lastEnableDepthTest) _gl.Enable(EnableCap.DepthTest); else _gl.Disable(EnableCap.DepthTest);
            if (lastEnableScissorTest) _gl.Enable(EnableCap.ScissorTest); else _gl.Disable(EnableCap.ScissorTest);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _gl.DeleteVertexArray(_vao);
                _gl.DeleteBuffer(_vbo);
                _gl.DeleteBuffer(_ebo);
                _gl.DeleteTexture(_fontTexture);
                _gl.DeleteProgram(_shaderProgram);

                ImGui.DestroyContext();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
