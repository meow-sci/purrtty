using System.Diagnostics;
using System.Runtime.InteropServices;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace ImGuiPlayground;

/// <summary>Standalone main-thread BRUTAL ImGui host, using GLFW and OpenGL on macOS, Linux and Windows.</summary>
public static class PlaygroundHost
{
    private static nint s_imgui;
    private static int s_running;

    /// <summary>Draws until the window closes. Smoke mode verifies three nonempty frames and exits.</summary>
    public static void Run(Action drawUi, bool smokeTest = false) => RunCore(drawUi, smokeTest, false, 800, 500);

    /// <summary>
    /// Renders three frames in an invisible window and reads its framebuffer, without desktop screenshots.
    /// Still requires a graphical session/OpenGL driver (Linux CI can use Xvfb + Mesa).
    /// </summary>
    public static CapturedFrame Capture(Action drawUi, int width = 800, int height = 500) =>
        RunCore(drawUi, true, true, width, height)!;

    private static unsafe CapturedFrame? RunCore(Action drawUi, bool smokeTest, bool capture, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(drawUi);
        if (!Environment.Is64BitProcess || !(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsWindows()))
            throw new PlatformNotSupportedException("The playground requires a 64-bit macOS, Linux, or Windows process.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (OperatingSystem.IsMacOS() && pthread_main_np() == 0)
            throw new InvalidOperationException("GLFW must run synchronously on the macOS main thread.");
        if (Interlocked.CompareExchange(ref s_running, 1, 0) != 0)
            throw new InvalidOperationException("Only one playground can run at a time.");

        bool glfwInitialized = false;
        GlfwWindow? window = null;
        ImGuiContextPtr context = default;
        try
        {
            EnsureNativeLibrary();
            if (ImGui.GetVersion().ToString() != Constants.IMGUI_VERSION ||
                !ImGui.DebugCheckVersionAndDataLayout(Constants.IMGUI_VERSION,
                    (nuint)sizeof(ImGuiIO), (nuint)sizeof(ImGuiStyle), (nuint)sizeof(float2),
                    (nuint)sizeof(float4), (nuint)sizeof(ImDrawVert), sizeof(ushort)))
                throw new InvalidOperationException("BRUTAL/native ImGui ABI mismatch. Use matching KSA managed and native libraries.");
            if (!ImGui.GetCurrentContext().IsNull())
                throw new InvalidOperationException("The playground must own its ImGui context; another host is already active.");

            glfwInitialized = Glfw.Init();
            if (!glfwInitialized) throw new InvalidOperationException("GLFW initialization failed. A graphical session is required.");
            Glfw.DefaultWindowHints();
            Glfw.WindowHint(GlfwWindowHint.ContextVersionMajor, 3);
            Glfw.WindowHint(GlfwWindowHint.ContextVersionMinor, 2);
            Glfw.WindowHint(GlfwWindowHint.OpenGlProfile, Glfw.Constants.GLFW_OPENGL_CORE_PROFILE);
            Glfw.WindowHint(GlfwWindowHint.OpenGlForwardCompat, 1);
            Glfw.WindowHint(GlfwWindowHint.Visible, capture ? 0 : 1);
            window = Glfw.CreateWindow(new GlfwWindow.CreateInfo { Title = "ImGui Playground", Size = new int2(width, height) });
            window.MakeContextCurrent();
            Glfw.SwapInterval(smokeTest ? 0 : 1);

            context = ImGui.CreateContext();
            if (context.IsNull()) throw new InvalidOperationException("ImGui context creation failed.");
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
            ImGui.StyleColorsDark();
            ImGui.GetStyle().FontSizeBase = 18;
            string fontPath = Path.Combine(AppContext.BaseDirectory, "fonts", "JetBrainsMono-Regular.ttf");
            if (!File.Exists(fontPath)) throw new FileNotFoundException("The bundled KSA default font is missing.", fontPath);
            io.FontDefault = io.Fonts.AddFontFromFileTTF(fontPath, 18);
            if (io.FontDefault.IsNull()) throw new InvalidOperationException($"Could not load font: {fontPath}");
            using var input = new GlfwInput(window);
            using var renderer = new OpenGlRenderer();
            CapturedFrame? image = null;
            int frames = 0;
            var deadline = Stopwatch.StartNew();
            while (!window.ShouldClose)
            {
                if (smokeTest && deadline.Elapsed > TimeSpan.FromSeconds(15))
                    throw new TimeoutException("Timed out waiting for three renderable frames.");
                Glfw.PollEvents();
                if (window.ShouldClose) break;
                var pixels = window.FramebufferSize;
                if (pixels.X <= 0 || pixels.Y <= 0 || window.Width <= 0 || window.Height <= 0 || window.IsIconified)
                {
                    Glfw.WaitEventsTimeout(0.1);
                    continue;
                }
                input.NewFrame();
                ImString.SharedStorage.Reset();
                ImGui.NewFrame();
                drawUi();
                input.ThrowIfCallbackFailed();
                ImGui.Render();
                var data = ImGui.GetDrawData();
                if (smokeTest && (data.TotalVtxCount <= 0 || data.TotalIdxCount <= 0))
                    throw new InvalidOperationException("Smoke test encountered a frame without UI geometry.");
                renderer.Render(data, pixels.X, pixels.Y);
                if (smokeTest) renderer.Finish();
                ++frames;
                if (capture && frames == 3) image = renderer.Capture(pixels.X, pixels.Y);
                window.SwapBuffers();
                if (smokeTest && frames == 3) break;
            }
            if (smokeTest && frames != 3)
                throw new InvalidOperationException("Window closed before three frames were rendered.");
            return image;
        }
        finally
        {
            // using declarations dispose renderer/input first, while both contexts are alive.
            try
            {
                if (!context.IsNull()) ImGui.DestroyContext(context);
            }
            finally
            {
                try { window?.Dispose(); }
                finally
                {
                    try { if (glfwInitialized) Glfw.Terminate(); }
                    finally { Volatile.Write(ref s_running, 0); }
                }
            }
        }
    }

    private static void EnsureNativeLibrary()
    {
        if (s_imgui != 0) return;
        string filename = OperatingSystem.IsWindows() ? "imgui.dll" :
            OperatingSystem.IsMacOS() ? "libimgui.dylib" : "libimgui.so";
        nint library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, filename));
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(ImGui).Assembly,
                (name, _, _) => name == "imgui" ? library : 0);
        }
        catch
        {
            NativeLibrary.Free(library);
            throw;
        }
        // Cached P/Invoke addresses must remain valid for the process lifetime.
        s_imgui = library;
        // Brutal.Glfw installs its OWN resolver, loading the staged GLFW library.
    }

    [DllImport("/usr/lib/libSystem.B.dylib", CallingConvention = CallingConvention.Cdecl)]
    private static extern int pthread_main_np();
}
