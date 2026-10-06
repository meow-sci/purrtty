using System.Runtime.InteropServices;
using System.Text;
using Brutal.ImGuiApi;

namespace ImGuiPlayground;

// Dedicated context: this renderer owns GL state, rather than saving/restoring a game's state.
internal sealed unsafe class OpenGlRenderer : IDisposable
{
    private const uint ArrayBuffer = 0x8892, ElementBuffer = 0x8893, StreamDraw = 0x88E0;
    private const uint Texture2D = 0x0DE1, Rgba = 0x1908, UnsignedByte = 0x1401;
    private const uint Blend = 0x0BE2, ScissorTest = 0x0C11;
    private readonly OpenGl _gl = new();
    private readonly HashSet<uint> _textures = [];
    private uint _program, _vertices, _indices, _vertexArray;
    private int _projection, _sampler;
    private bool _disposed;

    internal OpenGlRenderer()
    {
        try
        {
            CreateDeviceObjects();
            var io = ImGui.GetIO();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset | ImGuiBackendFlags.RendererHasTextures;
            int maxTextureSize;
            _gl.GetIntegerv(0x0D33, &maxTextureSize); // GL_MAX_TEXTURE_SIZE
            var platform = ImGui.GetPlatformIO();
            platform.Renderer_TextureMaxWidth = platform.Renderer_TextureMaxHeight = maxTextureSize;
            _gl.CheckError();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void Render(ImDrawDataPtr data, int width, int height)
    {
        UpdateTextures(data);
        SetupState(data, width, height);
        _gl.Disable(ScissorTest);
        _gl.ClearColor(0.08f, 0.09f, 0.12f, 1);
        _gl.Clear(0x4000); // GL_COLOR_BUFFER_BIT
        _gl.Enable(ScissorTest);

        foreach (var list in data.CmdLists.Span)
        {
            _gl.BufferData(ArrayBuffer, checked(list.VtxBuffer.Count * sizeof(ImDrawVert)), list.VtxBuffer.DataRaw, StreamDraw);
            _gl.BufferData(ElementBuffer, checked(list.IdxBuffer.Count * sizeof(ushort)), list.IdxBuffer.DataRaw, StreamDraw);
            for (int i = 0; i < list.CmdBuffer.Count; ++i)
            {
                ImDrawCmd* command = list.CmdBuffer.DataRaw + i;
                if (command->UserCallback != 0)
                {
                    if (command->UserCallback == -8) // ImDrawCallback_ResetRenderState
                        SetupState(data, width, height);
                    else
                        ((delegate* unmanaged<ImDrawList*, ImDrawCmd*, void>)command->UserCallback)(list.Ptr, command);
                    continue;
                }

                var clip = command->ClipRect;
                int left = Math.Clamp((int)((clip.X - data.DisplayPos.X) * data.FramebufferScale.X), 0, width);
                int top = Math.Clamp((int)((clip.Y - data.DisplayPos.Y) * data.FramebufferScale.Y), 0, height);
                int right = Math.Clamp((int)((clip.Z - data.DisplayPos.X) * data.FramebufferScale.X), 0, width);
                int bottom = Math.Clamp((int)((clip.W - data.DisplayPos.Y) * data.FramebufferScale.Y), 0, height);
                if (right <= left || bottom <= top) continue;
                _gl.Scissor(left, height - bottom, right - left, bottom - top);
                _gl.BindTexture(Texture2D, checked((uint)command->GetTexID().Value));
                _gl.DrawElementsBaseVertex(0x0004, checked((int)command->ElemCount), 0x1403,
                    (void*)(command->IdxOffset * sizeof(ushort)), checked((int)command->VtxOffset)); // TRIANGLES, UNSIGNED_SHORT
            }
        }
        _gl.CheckError();
    }

    internal void Finish()
    {
        _gl.Finish();
        _gl.CheckError();
    }

    internal CapturedFrame Capture(int width, int height)
    {
        int stride = checked(width * 4);
        byte[] pixels = new byte[checked(stride * height)];
        _gl.BindBuffer(0x88EB, 0); // GL_PIXEL_PACK_BUFFER: read into managed memory, not a buffer offset.
        _gl.PixelStorei(0x0D05, 1); // GL_PACK_ALIGNMENT
        _gl.PixelStorei(0x0D02, 0); // GL_PACK_ROW_LENGTH
        _gl.PixelStorei(0x0D03, 0); // GL_PACK_SKIP_ROWS
        _gl.PixelStorei(0x0D04, 0); // GL_PACK_SKIP_PIXELS
        _gl.ReadBuffer(0x0405); // GL_BACK, before swapping; excludes OS title bar/other windows.
        fixed (byte* pointer = pixels)
            _gl.ReadPixels(0, 0, width, height, Rgba, UnsignedByte, pointer);
        _gl.CheckError(); // ReadPixels synchronizes readback with the rendering commands.
        byte[] row = new byte[stride];
        for (int y = 0; y < height / 2; ++y)
        {
            Span<byte> top = pixels.AsSpan(y * stride, stride);
            Span<byte> bottom = pixels.AsSpan((height - 1 - y) * stride, stride);
            top.CopyTo(row);
            bottom.CopyTo(top);
            row.CopyTo(bottom);
        }
        return new CapturedFrame(width, height, pixels);
    }

    private void SetupState(ImDrawDataPtr data, int width, int height)
    {
        _gl.Enable(Blend);
        _gl.BlendEquation(0x8006); // FUNC_ADD
        _gl.BlendFuncSeparate(0x0302, 0x0303, 1, 0x0303); // SRC_ALPHA, ONE_MINUS_SRC_ALPHA, ONE
        _gl.Disable(0x0B44); // CULL_FACE
        _gl.Disable(0x0B71); // DEPTH_TEST
        _gl.Disable(0x0B90); // STENCIL_TEST
        _gl.Enable(ScissorTest);
        _gl.Viewport(0, 0, width, height);
        _gl.UseProgram(_program);
        _gl.ActiveTexture(0x84C0); // TEXTURE0
        _gl.Uniform1i(_sampler, 0);
        float left = data.DisplayPos.X, right = left + data.DisplaySize.X;
        float top = data.DisplayPos.Y, bottom = top + data.DisplaySize.Y;
        float* projection = stackalloc float[16]
        {
            2 / (right - left), 0, 0, 0,
            0, 2 / (top - bottom), 0, 0,
            0, 0, -1, 0,
            (right + left) / (left - right), (top + bottom) / (bottom - top), 0, 1
        };
        _gl.UniformMatrix4fv(_projection, 1, 0, projection);
        _gl.BindVertexArray(_vertexArray);
        _gl.BindBuffer(ArrayBuffer, _vertices);
        _gl.BindBuffer(ElementBuffer, _indices);
        for (uint i = 0; i < 3; ++i) _gl.EnableVertexAttribArray(i);
        _gl.VertexAttribPointer(0, 2, 0x1406, 0, sizeof(ImDrawVert), (void*)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.pos)));
        _gl.VertexAttribPointer(1, 2, 0x1406, 0, sizeof(ImDrawVert), (void*)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.uv)));
        _gl.VertexAttribPointer(2, 4, UnsignedByte, 1, sizeof(ImDrawVert), (void*)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.col)));
    }

    private void UpdateTextures(ImDrawDataPtr data)
    {
        ImVector<ImTextureDataPtr>* textures = data.Textures;
        if (textures == null) return;
        foreach (var texture in textures->Span)
        {
            if (texture.Status is ImTextureStatus.OK or ImTextureStatus.Destroyed) continue;
            uint id = checked((uint)texture.TexID.Value);
            if (texture.Status == ImTextureStatus.WantDestroy)
            {
                if (texture.UnusedFrames <= 0) continue;
                if (_textures.Remove(id)) _gl.DeleteTextures(1, &id);
                texture.SetTexID(ImTextureID.Invalid);
                texture.SetStatus(ImTextureStatus.Destroyed);
                continue;
            }
            if (texture.Format != ImTextureFormat.RGBA32)
                throw new NotSupportedException("The playground renderer requires RGBA32 ImGui textures.");
            if (texture.Status == ImTextureStatus.WantCreate)
            {
                _gl.GenTextures(1, &id);
                _textures.Add(id);
                texture.SetTexID(new ImTextureID((nint)id));
            }
            _gl.BindTexture(Texture2D, id);
            _gl.TexParameteri(Texture2D, 0x2801, 0x2601); // MIN_FILTER, LINEAR
            _gl.TexParameteri(Texture2D, 0x2800, 0x2601); // MAG_FILTER, LINEAR
            _gl.TexParameteri(Texture2D, 0x2802, 0x812F); // WRAP_S, CLAMP_TO_EDGE
            _gl.TexParameteri(Texture2D, 0x2803, 0x812F); // WRAP_T, CLAMP_TO_EDGE
            _gl.PixelStorei(0x0CF5, 1); // UNPACK_ALIGNMENT
            _gl.PixelStorei(0x0CF2, 0); // UNPACK_ROW_LENGTH
            // Full upload on atlas updates keeps the first managed backend simple/correct.
            _gl.TexImage2D(Texture2D, 0, (int)Rgba, texture.Width, texture.Height, 0, Rgba, UnsignedByte, texture.Pixels.Pointer);
            _gl.CheckError();
            texture.SetStatus(ImTextureStatus.OK);
        }
    }

    private void CreateDeviceObjects()
    {
        uint vertex = 0, fragment = 0;
        try
        {
            vertex = CompileShader(0x8B31, """
                #version 150
                uniform mat4 Projection;
                in vec2 Position;
                in vec2 UV;
                in vec4 Color;
                out vec2 FragUV;
                out vec4 FragColor;
                void main() { FragUV=UV; FragColor=Color; gl_Position=Projection*vec4(Position,0,1); }
                """u8);
            fragment = CompileShader(0x8B30, """
                #version 150
                uniform sampler2D Texture;
                in vec2 FragUV;
                in vec4 FragColor;
                out vec4 OutColor;
                void main() { OutColor=FragColor*texture(Texture,FragUV); }
                """u8);
            _program = _gl.CreateProgram();
            _gl.AttachShader(_program, vertex);
            _gl.AttachShader(_program, fragment);
            fixed (byte* position = "Position"u8, uv = "UV"u8, color = "Color"u8)
            {
                _gl.BindAttribLocation(_program, 0, position);
                _gl.BindAttribLocation(_program, 1, uv);
                _gl.BindAttribLocation(_program, 2, color);
            }
            _gl.LinkProgram(_program);
            int linked;
            _gl.GetProgramiv(_program, 0x8B82, &linked); // LINK_STATUS
            if (linked == 0) throw new InvalidOperationException("OpenGL program link failed: " + GetLog(_program, false));
            fixed (byte* projection = "Projection"u8, sampler = "Texture"u8)
            {
                _projection = _gl.GetUniformLocation(_program, projection);
                _sampler = _gl.GetUniformLocation(_program, sampler);
            }
            uint* buffers = stackalloc uint[2];
            _gl.GenBuffers(2, buffers);
            _vertices = buffers[0]; _indices = buffers[1];
            uint array;
            _gl.GenVertexArrays(1, &array);
            _vertexArray = array;
            // Vertex attributes are set by SetupState on every frame/reset callback.
        }
        finally
        {
            if (vertex != 0) _gl.DeleteShader(vertex);
            if (fragment != 0) _gl.DeleteShader(fragment);
        }
    }

    private uint CompileShader(uint type, ReadOnlySpan<byte> source)
    {
        uint shader = _gl.CreateShader(type);
        fixed (byte* text = source)
        {
            byte* pointer = text;
            int length = source.Length;
            _gl.ShaderSource(shader, 1, &pointer, &length);
        }
        _gl.CompileShader(shader);
        int compiled;
        _gl.GetShaderiv(shader, 0x8B81, &compiled); // COMPILE_STATUS
        if (compiled != 0) return shader;
        string log = GetLog(shader, true);
        _gl.DeleteShader(shader);
        throw new InvalidOperationException("OpenGL shader compilation failed: " + log);
    }

    private string GetLog(uint handle, bool shader)
    {
        byte* buffer = stackalloc byte[4096];
        int length;
        if (shader) _gl.GetShaderInfoLog(handle, 4096, &length, buffer);
        else _gl.GetProgramInfoLog(handle, 4096, &length, buffer);
        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(buffer, Math.Clamp(length, 0, 4096)));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gl.Finish();
        foreach (var texture in ImGui.GetPlatformIO().Textures.Span)
        {
            if (_textures.Contains(checked((uint)texture.TexID.Value)))
            {
                texture.SetTexID(ImTextureID.Invalid);
                texture.SetStatus(ImTextureStatus.Destroyed);
            }
        }
        foreach (uint texture in _textures) { uint id = texture; _gl.DeleteTextures(1, &id); }
        _textures.Clear();
        uint* buffers = stackalloc uint[2] { _vertices, _indices };
        _gl.DeleteBuffers(2, buffers);
        uint array = _vertexArray;
        _gl.DeleteVertexArrays(1, &array);
        if (_program != 0) _gl.DeleteProgram(_program);
        ImGui.GetIO().BackendFlags &= ~(ImGuiBackendFlags.RendererHasTextures | ImGuiBackendFlags.RendererHasVtxOffset);
    }
}
