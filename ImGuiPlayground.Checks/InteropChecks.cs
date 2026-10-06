using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using ImGuiPlayground;

internal static unsafe class InteropChecks
{
    private static delegate* unmanaged<uint, void> s_disableAttribute;

    internal static void AddVertexResetFixture(ImDrawListPtr draw)
    {
        s_disableAttribute = (delegate* unmanaged<uint, void>)Glfw.GetProcAddress("glDisableVertexAttribArray\0"u8);
        if (s_disableAttribute == null) throw new Exception("Missing GL test entry point.");
        draw.AddCallback(&ChangeVertexInput, default);
        draw.AddCallback((delegate* unmanaged[Cdecl]<ImDrawList*, ImDrawCmd*, void>)(nint)(-8), default);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ChangeVertexInput(ImDrawList* list, ImDrawCmd* command) => s_disableAttribute(0);

    internal static void CheckInput()
    {
        if (GlfwInput.MapKey(GlfwInput.TranslatePrintableKey(GlfwKey.Q, "a")) != ImGuiKey.A ||
            GlfwInput.MapKey(GlfwInput.TranslatePrintableKey(GlfwKey.Y, "z")) != ImGuiKey.Z ||
            GlfwInput.TranslatePrintableKey(GlfwKey.Kp1, "1") != GlfwKey.Kp1 ||
            GlfwInput.TranslatePrintableKey(GlfwKey.Enter, null) != GlfwKey.Enter)
            throw new Exception("Keyboard layout translation failed.");

        string filename = OperatingSystem.IsWindows() ? "glfw3.dll" : OperatingSystem.IsMacOS() ? "libglfw.dylib" : "libglfw.so";
        nint library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, filename));
        try
        {
            var setCharacter = (delegate* unmanaged[Cdecl]<nint, nint, nint>)NativeLibrary.GetExport(library, "glfwSetCharCallback");
            int frame = 0;
            PlaygroundHost.Capture(() =>
            {
                ++frame;
                if (frame == 1)
                {
                    // Exercise the actually installed native callback, not just a helper.
                    nint window = Glfw.GetCurrentContext();
                    nint callback = setCharacter(window, 0);
                    setCharacter(window, callback);
                    if (callback == 0) throw new Exception("Character callback was not installed.");
                    ((delegate* unmanaged[Cdecl]<nint, uint, void>)callback)(window, 0x1F600);
                    ImGui.GetIO().AddInputCharacter(0x1F600);
                }
                else if (frame == 2)
                {
                    var queue = ImGui.GetIO().InputQueueCharacters;
                    // The available older macOS native library uses ImWchar16 despite the
                    // binding's uint vector declaration. Two characters guarantee at least
                    // four valid bytes: inspect that word first, never read a uint span from
                    // a ushort buffer. Such builds correctly replace unsupported codepoints.
                    if (queue.Count != 2) throw new Exception("Expected two character events.");
                    uint first = *queue.DataRaw;
                    bool wchar16Replacement = first == 0xFFFDFFFD;
                    bool wchar32Preserved = first == 0x1F600 && queue.DataRaw[1] == 0x1F600;
                    if (!wchar16Replacement && !wchar32Preserved)
                        throw new Exception($"Character callback differs from direct BRUTAL input (first word 0x{first:X}).");
                }
                ImGui.GetBackgroundDrawList().AddRectFilled(new float2(0, 0), new float2(50, 50), ImColor8.White);
            }, 200, 150);
        }
        finally { NativeLibrary.Free(library); }
    }
}
