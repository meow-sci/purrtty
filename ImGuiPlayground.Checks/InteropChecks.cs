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
        CheckDpiCoordinates();
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
            var setCursor = (delegate* unmanaged[Cdecl]<nint, nint, nint>)NativeLibrary.GetExport(library, "glfwSetCursorPosCallback");
            var getScale = (delegate* unmanaged[Cdecl]<nint, float*, float*, void>)NativeLibrary.GetExport(library, "glfwGetWindowContentScale");
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
                    // Exercise BRUTAL's installed cursor callback and the input adapter.
                    // GLFW cursor units need conversion only on Windows, not Retina macOS.
                    float xScale = 1, yScale = 1;
                    if (OperatingSystem.IsWindows()) getScale(window, &xScale, &yScale);
                    nint cursorCallback = setCursor(window, 0);
                    setCursor(window, cursorCallback);
                    if (cursorCallback == 0) throw new Exception("Cursor callback was not installed.");
                    ((delegate* unmanaged[Cdecl]<nint, double, double, void>)cursorCallback)(window, 80d * xScale, 40d * yScale);
                }
                else if (frame == 2)
                {
                    Equal(ImGui.GetIO().MousePos, new float2(80, 40), "Native cursor callback logical position");
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

    private static void CheckDpiCoordinates()
    {
        // Pure policy/mapping checks run on every OS; these do not simulate Windows' WM_DPICHANGED.
        // Returning to 100% also guards against accidentally accumulating scale across frames.
        foreach (float dpi in new[] { 1f, 1.25f, 1.5f, 2f, 2.5f, 3f, 1f })
        {
            var scale = GlfwInput.GetCoordinateScale(true, new float2(dpi, dpi));
            var size = new int2((int)(800 * dpi), (int)(500 * dpi));
            var metrics = GlfwInput.GetDisplayMetrics(size, size, scale);
            Equal(metrics.DisplaySize, new float2(800, 500), $"Windows {dpi} display size");
            Equal(metrics.FramebufferScale, new float2(dpi, dpi), $"Windows {dpi} framebuffer scale");
            Equal(GlfwInput.ToLogicalPosition(new double2(125 * dpi, 75 * dpi), scale),
                new float2(125, 75), $"Windows {dpi} cursor");
            Equal(GlfwInput.ToLogicalPosition(new double2(-10 * dpi, 510 * dpi), scale),
                new float2(-10, 510), $"Windows {dpi} drag outside window");
        }

        var retinaScale = GlfwInput.GetCoordinateScale(false, new float2(2, 2));
        var retina = GlfwInput.GetDisplayMetrics(new int2(800, 500), new int2(1600, 1000), retinaScale);
        Equal(retina.DisplaySize, new float2(800, 500), "Retina logical size must not be halved");
        Equal(retina.FramebufferScale, new float2(2, 2), "Retina scale must not be doubled");
        Equal(GlfwInput.ToLogicalPosition(new double2(125, 75), retinaScale), new float2(125, 75), "Retina cursor");
        var unscaled = GlfwInput.GetDisplayMetrics(new int2(800, 500), new int2(800, 500), retinaScale);
        Equal(unscaled.FramebufferScale, new float2(1, 1), "Other platforms keep existing framebuffer ratio");

        var asymmetric = GlfwInput.GetCoordinateScale(true, new float2(2.5f, 2));
        var separateAxes = GlfwInput.GetDisplayMetrics(new int2(2000, 1000), new int2(2000, 1000), asymmetric);
        Equal(separateAxes.DisplaySize, new float2(800, 500), "Independent DPI axes");
        Equal(separateAxes.FramebufferScale, asymmetric, "Independent framebuffer axes");
        Equal(GlfwInput.ToLogicalPosition(new double2(250, 100), asymmetric), new float2(100, 50), "Independent cursor axes");
        Equal(GlfwInput.GetCoordinateScale(true, new float2(0, float.NaN)), new float2(1, 1), "Invalid DPI fallback");
        Equal(GlfwInput.GetCoordinateScale(true, new float2(float.PositiveInfinity, -1)), new float2(1, 1), "Nonfinite/negative DPI fallback");
    }

    private static void Equal(float2 actual, float2 expected, string reason)
    {
        if (!float.IsFinite(actual.X) || !float.IsFinite(actual.Y) ||
            Math.Abs(actual.X - expected.X) > 0.001f || Math.Abs(actual.Y - expected.Y) > 0.001f)
            throw new Exception($"{reason}: expected {expected}, got {actual}.");
    }
}
