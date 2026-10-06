using Brutal.GlfwApi;

namespace ImGuiPlayground;

// OpenGL entry points belong to the current GLFW context. No generated C++ shim,
// platform-specific GL imports, or graphics NuGet package is needed.
internal sealed unsafe class OpenGl
{
    internal readonly delegate* unmanaged<int, uint*, void> GenBuffers = (delegate* unmanaged<int, uint*, void>)Load("glGenBuffers"u8);
    internal readonly delegate* unmanaged<int, uint*, void> DeleteBuffers = (delegate* unmanaged<int, uint*, void>)Load("glDeleteBuffers"u8);
    internal readonly delegate* unmanaged<uint, uint, void> BindBuffer = (delegate* unmanaged<uint, uint, void>)Load("glBindBuffer"u8);
    internal readonly delegate* unmanaged<uint, nint, void*, uint, void> BufferData = (delegate* unmanaged<uint, nint, void*, uint, void>)Load("glBufferData"u8);
    internal readonly delegate* unmanaged<int, uint*, void> GenVertexArrays = (delegate* unmanaged<int, uint*, void>)Load("glGenVertexArrays"u8);
    internal readonly delegate* unmanaged<int, uint*, void> DeleteVertexArrays = (delegate* unmanaged<int, uint*, void>)Load("glDeleteVertexArrays"u8);
    internal readonly delegate* unmanaged<uint, void> BindVertexArray = (delegate* unmanaged<uint, void>)Load("glBindVertexArray"u8);
    internal readonly delegate* unmanaged<uint, void> EnableVertexAttribArray = (delegate* unmanaged<uint, void>)Load("glEnableVertexAttribArray"u8);
    internal readonly delegate* unmanaged<uint, int, uint, byte, int, void*, void> VertexAttribPointer = (delegate* unmanaged<uint, int, uint, byte, int, void*, void>)Load("glVertexAttribPointer"u8);
    internal readonly delegate* unmanaged<uint, uint> CreateShader = (delegate* unmanaged<uint, uint>)Load("glCreateShader"u8);
    internal readonly delegate* unmanaged<uint, int, byte**, int*, void> ShaderSource = (delegate* unmanaged<uint, int, byte**, int*, void>)Load("glShaderSource"u8);
    internal readonly delegate* unmanaged<uint, void> CompileShader = (delegate* unmanaged<uint, void>)Load("glCompileShader"u8);
    internal readonly delegate* unmanaged<uint, uint, int*, void> GetShaderiv = (delegate* unmanaged<uint, uint, int*, void>)Load("glGetShaderiv"u8);
    internal readonly delegate* unmanaged<uint, int, int*, byte*, void> GetShaderInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)Load("glGetShaderInfoLog"u8);
    internal readonly delegate* unmanaged<uint, void> DeleteShader = (delegate* unmanaged<uint, void>)Load("glDeleteShader"u8);
    internal readonly delegate* unmanaged<uint> CreateProgram = (delegate* unmanaged<uint>)Load("glCreateProgram"u8);
    internal readonly delegate* unmanaged<uint, uint, void> AttachShader = (delegate* unmanaged<uint, uint, void>)Load("glAttachShader"u8);
    internal readonly delegate* unmanaged<uint, uint, byte*, void> BindAttribLocation = (delegate* unmanaged<uint, uint, byte*, void>)Load("glBindAttribLocation"u8);
    internal readonly delegate* unmanaged<uint, void> LinkProgram = (delegate* unmanaged<uint, void>)Load("glLinkProgram"u8);
    internal readonly delegate* unmanaged<uint, uint, int*, void> GetProgramiv = (delegate* unmanaged<uint, uint, int*, void>)Load("glGetProgramiv"u8);
    internal readonly delegate* unmanaged<uint, int, int*, byte*, void> GetProgramInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)Load("glGetProgramInfoLog"u8);
    internal readonly delegate* unmanaged<uint, void> DeleteProgram = (delegate* unmanaged<uint, void>)Load("glDeleteProgram"u8);
    internal readonly delegate* unmanaged<uint, void> UseProgram = (delegate* unmanaged<uint, void>)Load("glUseProgram"u8);
    internal readonly delegate* unmanaged<uint, byte*, int> GetUniformLocation = (delegate* unmanaged<uint, byte*, int>)Load("glGetUniformLocation"u8);
    internal readonly delegate* unmanaged<int, int, void> Uniform1i = (delegate* unmanaged<int, int, void>)Load("glUniform1i"u8);
    internal readonly delegate* unmanaged<int, int, byte, float*, void> UniformMatrix4fv = (delegate* unmanaged<int, int, byte, float*, void>)Load("glUniformMatrix4fv"u8);
    internal readonly delegate* unmanaged<int, uint*, void> GenTextures = (delegate* unmanaged<int, uint*, void>)Load("glGenTextures"u8);
    internal readonly delegate* unmanaged<int, uint*, void> DeleteTextures = (delegate* unmanaged<int, uint*, void>)Load("glDeleteTextures"u8);
    internal readonly delegate* unmanaged<uint, void> ActiveTexture = (delegate* unmanaged<uint, void>)Load("glActiveTexture"u8);
    internal readonly delegate* unmanaged<uint, uint, void> BindTexture = (delegate* unmanaged<uint, uint, void>)Load("glBindTexture"u8);
    internal readonly delegate* unmanaged<uint, uint, int, void> TexParameteri = (delegate* unmanaged<uint, uint, int, void>)Load("glTexParameteri"u8);
    internal readonly delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void> TexImage2D = (delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void>)Load("glTexImage2D"u8);
    internal readonly delegate* unmanaged<uint, int, void> PixelStorei = (delegate* unmanaged<uint, int, void>)Load("glPixelStorei"u8);
    internal readonly delegate* unmanaged<uint, void> Enable = (delegate* unmanaged<uint, void>)Load("glEnable"u8);
    internal readonly delegate* unmanaged<uint, void> Disable = (delegate* unmanaged<uint, void>)Load("glDisable"u8);
    internal readonly delegate* unmanaged<uint, void> BlendEquation = (delegate* unmanaged<uint, void>)Load("glBlendEquation"u8);
    internal readonly delegate* unmanaged<uint, uint, uint, uint, void> BlendFuncSeparate = (delegate* unmanaged<uint, uint, uint, uint, void>)Load("glBlendFuncSeparate"u8);
    internal readonly delegate* unmanaged<int, int, int, int, void> Viewport = (delegate* unmanaged<int, int, int, int, void>)Load("glViewport"u8);
    internal readonly delegate* unmanaged<int, int, int, int, void> Scissor = (delegate* unmanaged<int, int, int, int, void>)Load("glScissor"u8);
    internal readonly delegate* unmanaged<float, float, float, float, void> ClearColor = (delegate* unmanaged<float, float, float, float, void>)Load("glClearColor"u8);
    internal readonly delegate* unmanaged<uint, void> Clear = (delegate* unmanaged<uint, void>)Load("glClear"u8);
    internal readonly delegate* unmanaged<uint, int, uint, void*, int, void> DrawElementsBaseVertex = (delegate* unmanaged<uint, int, uint, void*, int, void>)Load("glDrawElementsBaseVertex"u8);
    internal readonly delegate* unmanaged<uint, void> ReadBuffer = (delegate* unmanaged<uint, void>)Load("glReadBuffer"u8);
    internal readonly delegate* unmanaged<int, int, int, int, uint, uint, void*, void> ReadPixels = (delegate* unmanaged<int, int, int, int, uint, uint, void*, void>)Load("glReadPixels"u8);
    internal readonly delegate* unmanaged<uint, int*, void> GetIntegerv = (delegate* unmanaged<uint, int*, void>)Load("glGetIntegerv"u8);
    internal readonly delegate* unmanaged<void> Finish = (delegate* unmanaged<void>)Load("glFinish"u8);
    internal readonly delegate* unmanaged<uint> GetError = (delegate* unmanaged<uint>)Load("glGetError"u8);

    internal void CheckError()
    {
        uint error = GetError();
        if (error != 0)
            throw new InvalidOperationException($"OpenGL error 0x{error:X}.");
    }

    private static nint Load(ReadOnlySpan<byte> name)
    {
        byte[] terminated = new byte[name.Length + 1];
        name.CopyTo(terminated);
        nint pointer = Glfw.GetProcAddress(terminated);
        return pointer != 0 ? pointer : throw new NotSupportedException($"OpenGL 3.2 entry point unavailable: {System.Text.Encoding.UTF8.GetString(name)}");
    }
}
