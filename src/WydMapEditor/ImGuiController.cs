using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace WydMapEditor;

public sealed class ImGuiController : IDisposable
{
    private const int ImGuiKeyCount = 512;
    private bool _frameBegun;
    private int _vertexArray;
    private int _vertexBuffer;
    private int _indexBuffer;
    private int _vertexBufferSize;
    private int _indexBufferSize;
    private int _fontTexture;
    private int _shader;
    private int _attribLocationTex;
    private int _attribLocationProjMtx;
    private int _attribLocationVtxPos;
    private int _attribLocationVtxUV;
    private int _attribLocationVtxColor;
    private int _windowWidth;
    private int _windowHeight;
    private System.Numerics.Vector2 _scaleFactor = System.Numerics.Vector2.One;

    private readonly GameWindow _window;
    private IntPtr _iniPathPtr = IntPtr.Zero;

    public ImGuiController(GameWindow window, int width, int height)
    {
        _window = window;
        _windowWidth = width;
        _windowHeight = height;

        ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        string imguiDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WydTools");
        Directory.CreateDirectory(imguiDir);
        string iniPath = Path.Combine(imguiDir, "imgui.ini");
        _iniPathPtr = Marshal.StringToHGlobalAnsi(iniPath);
        unsafe { io.NativePtr->IniFilename = (byte*)_iniPathPtr; }

        // Tenta carregar fonte do sistema para visual mais moderno
        string[] sysFonts = {
            @"C:\Windows\Fonts\segoeui.ttf",
            @"C:\Windows\Fonts\calibri.ttf",
            @"C:\Windows\Fonts\tahoma.ttf",
        };
        bool loaded = false;
        foreach (var fp in sysFonts)
        {
            if (!File.Exists(fp)) continue;
            io.Fonts.AddFontFromFileTTF(fp, 14.0f);
            loaded = true;
            break;
        }
        if (!loaded) io.Fonts.AddFontDefault();

        CreateDeviceResources();
        SetKeyMappings();
        SetPerFrameImGuiData(1f / 60f);
        ImGui.NewFrame();
        _frameBegun = true;
    }

    public void Dispose()
    {
        GL.DeleteBuffer(_vertexBuffer);
        GL.DeleteBuffer(_indexBuffer);
        GL.DeleteVertexArray(_vertexArray);
        GL.DeleteTexture(_fontTexture);
        GL.DeleteProgram(_shader);
        if (_iniPathPtr != IntPtr.Zero) { Marshal.FreeHGlobal(_iniPathPtr); _iniPathPtr = IntPtr.Zero; }
    }

    public void WindowResized(int width, int height)
    {
        _windowWidth = width;
        _windowHeight = height;
    }

    public void Update(GameWindow wnd, float deltaSeconds)
    {
        if (_frameBegun)
            ImGui.Render();

        SetPerFrameImGuiData(deltaSeconds);
        UpdateImGuiInput(wnd);

        _frameBegun = true;
        ImGui.NewFrame();
    }

    public void Render()
    {
        if (_frameBegun)
        {
            _frameBegun = false;
            ImGui.Render();
            RenderImDrawData(ImGui.GetDrawData());
        }
    }

    private void CreateDeviceResources()
    {
        _vertexBufferSize = 10000;
        _indexBufferSize = 2000;

        _vertexArray = GL.GenVertexArray();
        GL.BindVertexArray(_vertexArray);

        _vertexBuffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, _vertexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        _indexBuffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
        GL.BufferData(BufferTarget.ElementArrayBuffer, _indexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        const string vertexSource = @"#version 330 core
uniform mat4 projection_matrix;
layout (location = 0) in vec2 in_position;
layout (location = 1) in vec2 in_texCoord;
layout (location = 2) in vec4 in_color;
out vec2 frag_UV;
out vec4 frag_Color;
void main()
{
    frag_UV = in_texCoord;
    frag_Color = in_color;
    gl_Position = projection_matrix * vec4(in_position.xy, 0, 1);
}";

        const string fragmentSource = @"#version 330 core
uniform sampler2D in_fontTexture;
in vec2 frag_UV;
in vec4 frag_Color;
out vec4 out_Color;
void main()
{
    out_Color = frag_Color * texture(in_fontTexture, frag_UV.st);
}";

        _shader = CreateProgram(vertexSource, fragmentSource);
        _attribLocationTex = GL.GetUniformLocation(_shader, "in_fontTexture");
        _attribLocationProjMtx = GL.GetUniformLocation(_shader, "projection_matrix");

        _attribLocationVtxPos = 0;
        _attribLocationVtxUV = 1;
        _attribLocationVtxColor = 2;

        GL.EnableVertexAttribArray(_attribLocationVtxPos);
        GL.EnableVertexAttribArray(_attribLocationVtxUV);
        GL.EnableVertexAttribArray(_attribLocationVtxColor);

        int stride = System.Runtime.InteropServices.Marshal.SizeOf<ImDrawVert>();
        GL.VertexAttribPointer(_attribLocationVtxPos, 2, VertexAttribPointerType.Float, false, stride, 0);
        GL.VertexAttribPointer(_attribLocationVtxUV, 2, VertexAttribPointerType.Float, false, stride, 8);
        GL.VertexAttribPointer(_attribLocationVtxColor, 4, VertexAttribPointerType.UnsignedByte, true, stride, 16);

        CreateFontTexture();

        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);
    }

    private static int CreateProgram(string vertexCode, string fragmentCode)
    {
        int vertex = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertex, vertexCode);
        GL.CompileShader(vertex);
        GL.GetShader(vertex, ShaderParameter.CompileStatus, out var vStatus);
        if (vStatus == 0)
            throw new Exception(GL.GetShaderInfoLog(vertex));

        int frag = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(frag, fragmentCode);
        GL.CompileShader(frag);
        GL.GetShader(frag, ShaderParameter.CompileStatus, out var fStatus);
        if (fStatus == 0)
            throw new Exception(GL.GetShaderInfoLog(frag));

        int program = GL.CreateProgram();
        GL.AttachShader(program, vertex);
        GL.AttachShader(program, frag);
        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out var lStatus);
        if (lStatus == 0)
            throw new Exception(GL.GetProgramInfoLog(program));

        GL.DetachShader(program, vertex);
        GL.DetachShader(program, frag);
        GL.DeleteShader(vertex);
        GL.DeleteShader(frag);

        return program;
    }

    private void CreateFontTexture()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out _);

        _fontTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _fontTexture);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

        io.Fonts.SetTexID((IntPtr)_fontTexture);
        io.Fonts.ClearTexData();
    }

    private void SetPerFrameImGuiData(float deltaSeconds)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new System.Numerics.Vector2(_windowWidth / _scaleFactor.X, _windowHeight / _scaleFactor.Y);
        io.DisplayFramebufferScale = _scaleFactor;
        io.DeltaTime = deltaSeconds;
    }

    private void UpdateImGuiInput(GameWindow wnd)
    {
        var io = ImGui.GetIO();

        var mouse = wnd.MouseState;
        var keyboard = wnd.KeyboardState;

        io.MouseDown[0] = mouse.IsButtonDown(MouseButton.Left);
        io.MouseDown[1] = mouse.IsButtonDown(MouseButton.Right);
        io.MouseDown[2] = mouse.IsButtonDown(MouseButton.Middle);
        io.MousePos = new System.Numerics.Vector2(mouse.X, mouse.Y);
        io.MouseWheel = mouse.ScrollDelta.Y;
        io.MouseWheelH = mouse.ScrollDelta.X;

        foreach (OpenTK.Windowing.GraphicsLibraryFramework.Keys key in Enum.GetValues(typeof(OpenTK.Windowing.GraphicsLibraryFramework.Keys)))
        {
            if (key == OpenTK.Windowing.GraphicsLibraryFramework.Keys.Unknown)
                continue;
            int idx = (int)key;
            if (idx >= 0 && idx < ImGuiKeyCount)
                io.KeysDown[idx] = keyboard.IsKeyDown(key);
        }

        io.KeyCtrl = keyboard.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.LeftControl) || keyboard.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.RightControl);
        io.KeyAlt = keyboard.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.LeftAlt) || keyboard.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.RightAlt);
        io.KeyShift = keyboard.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.LeftShift) || keyboard.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.RightShift);
        io.KeySuper = keyboard.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.LeftSuper) || keyboard.IsKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.RightSuper);

        wnd.TextInput -= OnTextInput;
        wnd.TextInput += OnTextInput;
    }

    private void OnTextInput(TextInputEventArgs e)
    {
        if (e.AsString.Length > 0)
            ImGui.GetIO().AddInputCharactersUTF8(e.AsString);
    }

    private void SetKeyMappings()
    {
        var io = ImGui.GetIO();
        io.KeyMap[(int)ImGuiKey.Tab] = (int)OpenTK.Windowing.GraphicsLibraryFramework.Keys.Tab;
        io.KeyMap[(int)ImGuiKey.LeftArrow] = (int)OpenTK.Windowing.GraphicsLibraryFramework.Keys.Left;
        io.KeyMap[(int)ImGuiKey.RightArrow] = (int)OpenTK.Windowing.GraphicsLibraryFramework.Keys.Right;
        io.KeyMap[(int)ImGuiKey.UpArrow] = (int)OpenTK.Windowing.GraphicsLibraryFramework.Keys.Up;
        io.KeyMap[(int)ImGuiKey.DownArrow] = (int)OpenTK.Windowing.GraphicsLibraryFramework.Keys.Down;
        io.KeyMap[(int)ImGuiKey.PageUp] = (int)OpenTK.Windowing.GraphicsLibraryFramework.Keys.PageUp;
        io.KeyMap[(int)ImGuiKey.PageDown] = (int)OpenTK.Windowing.GraphicsLibraryFramework.Keys.PageDown;
        io.KeyMap[(int)ImGuiKey.Home] = (int)OpenTK.Windowing.GraphicsLibraryFramework.Keys.Home;
        io.KeyMap[(int)ImGuiKey.End] = (int)OpenTK.Windowing.GraphicsLibraryFramework.Keys.End;
        io.KeyMap[(int)ImGuiKey.Delete] = (int)OpenTK.Windowing.GraphicsLibraryFramework.Keys.Delete;
        io.KeyMap[(int)ImGuiKey.Backspace] = (int)OpenTK.Windowing.GraphicsLibraryFramework.Keys.Backspace;
        io.KeyMap[(int)ImGuiKey.Enter] = (int)OpenTK.Windowing.GraphicsLibraryFramework.Keys.Enter;
        io.KeyMap[(int)ImGuiKey.Escape] = (int)OpenTK.Windowing.GraphicsLibraryFramework.Keys.Escape;
        io.KeyMap[(int)ImGuiKey.A] = (int)OpenTK.Windowing.GraphicsLibraryFramework.Keys.A;
        io.KeyMap[(int)ImGuiKey.C] = (int)OpenTK.Windowing.GraphicsLibraryFramework.Keys.C;
        io.KeyMap[(int)ImGuiKey.V] = (int)OpenTK.Windowing.GraphicsLibraryFramework.Keys.V;
        io.KeyMap[(int)ImGuiKey.X] = (int)OpenTK.Windowing.GraphicsLibraryFramework.Keys.X;
        io.KeyMap[(int)ImGuiKey.Y] = (int)OpenTK.Windowing.GraphicsLibraryFramework.Keys.Y;
        io.KeyMap[(int)ImGuiKey.Z] = (int)OpenTK.Windowing.GraphicsLibraryFramework.Keys.Z;
    }

    private void RenderImDrawData(ImDrawDataPtr drawData)
    {
        int fbWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
        int fbHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
        if (fbWidth <= 0 || fbHeight <= 0)
            return;

        drawData.ScaleClipRects(drawData.FramebufferScale);

        GL.Enable(EnableCap.Blend);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.DepthTest);
        GL.Enable(EnableCap.ScissorTest);
        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);

        GL.Viewport(0, 0, fbWidth, fbHeight);

        var io = ImGui.GetIO();
        Matrix4 proj = Matrix4.CreateOrthographicOffCenter(0.0f, io.DisplaySize.X, io.DisplaySize.Y, 0.0f, -1.0f, 1.0f);

        GL.UseProgram(_shader);
        GL.Uniform1(_attribLocationTex, 0);
        GL.UniformMatrix4(_attribLocationProjMtx, false, ref proj);
        GL.BindVertexArray(_vertexArray);

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[n];

            int vertexSize = cmdList.VtxBuffer.Size * System.Runtime.InteropServices.Marshal.SizeOf<ImDrawVert>();
            if (vertexSize > _vertexBufferSize)
            {
                while (vertexSize > _vertexBufferSize)
                    _vertexBufferSize *= 2;
                GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
                GL.BufferData(BufferTarget.ArrayBuffer, _vertexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            }

            int indexSize = cmdList.IdxBuffer.Size * sizeof(ushort);
            if (indexSize > _indexBufferSize)
            {
                while (indexSize > _indexBufferSize)
                    _indexBufferSize *= 2;
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
                GL.BufferData(BufferTarget.ElementArrayBuffer, _indexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            }

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vertexSize, cmdList.VtxBuffer.Data);

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
            GL.BufferSubData(BufferTarget.ElementArrayBuffer, IntPtr.Zero, indexSize, cmdList.IdxBuffer.Data);

            int idxOffset = 0;
            for (int cmdi = 0; cmdi < cmdList.CmdBuffer.Size; cmdi++)
            {
                ImDrawCmdPtr pcmd = cmdList.CmdBuffer[cmdi];
                if (pcmd.UserCallback != IntPtr.Zero)
                {
                    idxOffset += (int)pcmd.ElemCount;
                    continue;
                }

                GL.BindTexture(TextureTarget.Texture2D, (int)pcmd.TextureId);
                GL.Scissor(
                    (int)pcmd.ClipRect.X,
                    fbHeight - (int)pcmd.ClipRect.W,
                    (int)(pcmd.ClipRect.Z - pcmd.ClipRect.X),
                    (int)(pcmd.ClipRect.W - pcmd.ClipRect.Y));

                if ((io.BackendFlags & ImGuiBackendFlags.RendererHasVtxOffset) != 0)
                {
                    GL.DrawElementsBaseVertex(PrimitiveType.Triangles, (int)pcmd.ElemCount, DrawElementsType.UnsignedShort, (IntPtr)(idxOffset * sizeof(ushort)), (int)pcmd.VtxOffset);
                }
                else
                {
                    GL.DrawElements(PrimitiveType.Triangles, (int)pcmd.ElemCount, DrawElementsType.UnsignedShort, idxOffset * sizeof(ushort));
                }
                idxOffset += (int)pcmd.ElemCount;
            }
        }

        GL.Disable(EnableCap.ScissorTest);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }
}
