using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Abstractions;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using RenderCore;
using System.IO;
using caTTY.Display.Rendering;

namespace caTTY.TestApp.Rendering;

/// <summary>
///     Standalone ImGui context for the terminal test application.
///     Based on the KSA ImGui framework and playground implementation.
/// </summary>
public static class StandaloneImGui
{
    private static GlfwWindow? window;
    private static Renderer? renderer;
    private static RenderPassState? rstate;
    private static Action? OnDrawUi;
    private static DateTime _lastFrameTime = DateTime.Now;

    /// <summary>
    ///     Run the ImGui application loop with the specified UI drawing callback.
    /// </summary>
    /// <param name="onDrawUi">Callback to draw the UI each frame, receives delta time in seconds</param>
    public static void Run(Action<float> onDrawUi)
    {
        OnDrawUi = () => onDrawUi(GetDeltaTime());
        Init();

        Console.WriteLine("BRUTAL ImGui context initialized successfully");
        Console.WriteLine("Terminal window should now be visible");
        Console.WriteLine("Press Ctrl+C in the terminal or close the window to exit");

        while (!window!.ShouldClose)
        {
            OnFrame();
        }

        Console.WriteLine("Application shutting down...");
    }

    /// <summary>
    ///     Run the ImGui application loop with the specified UI drawing callback (legacy overload).
    /// </summary>
    /// <param name="onDrawUi">Callback to draw the UI each frame</param>
    public static void Run(Action onDrawUi)
    {
        Run(_ => onDrawUi());
    }

    /// <summary>
    ///     Initializes the BRUTAL ImGui context with GLFW window and Vulkan renderer.
    /// </summary>
    private static void Init()
    {
        // Initialize GLFW
        Glfw.Init();

        // Configure GLFW window hints
        Glfw.WindowHint(GlfwWindowHint.ClientApi, 0);
        Glfw.WindowHint(GlfwWindowHint.AutoIconify, 0);
        Glfw.WindowHint(GlfwWindowHint.FocusOnShow, 1);

        // Create window with appropriate size for terminal
        window = Glfw.CreateWindow(new GlfwWindow.CreateInfo
        {
            Title = "caTTY Terminal Emulator - BRUTAL ImGui Test",
            Size = new int2(1400, 900) // Good size for 80x24 terminal
        });

        // Initialize Vulkan renderer
        renderer = new Renderer(window, VkFormat.D32SFloat, VkPresentModeKHR.FifoKHR, VulkanHelpers.Api.VERSION_1_3);

        // Set up render pass state
        rstate = new RenderPassState
        {
            Pass = renderer.MainRenderPass,
            SampleCount = VkSampleCountFlags._1Bit,
            ClearValues =
            [
                new VkClearColorValue { Float32 = Color.Black.AsFloat4 },
                new VkClearDepthStencilValue { Depth = 0 }
            ]
        };

        // Initialize ImGui context
        ImGui.CreateContext();

        ImGuiIOPtr io = ImGui.GetIO();
        io.ConfigDpiScaleFonts = true;
        io.ConfigDpiScaleViewports = true;
        io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;
        io.IniFilename = null;
        io.IniSavingRate = 0;

        // Initialize ImGui backend
        ImGuiBackend.Initialize(window, renderer);

        // Initialize font manager (requires KSA console window)
        KSA.Program.ConsoleWindow = new ConsoleWindow();
        FontManager.Initialize(renderer.Device);

        // Load .iamttf fonts explicitly (similar to GameMod pattern)
        CaTTYFontManager.LoadFonts();
    }

    /// <summary>
    ///     Processes a single frame of the application loop.
    /// </summary>
    private static void OnFrame()
    {
        // Poll GLFW events
        Glfw.PollEvents();

        // Start ImGui frame
        ImGuiBackend.NewFrame();
        ImGui.NewFrame();
        ImGuiHelper.StartFrame();

        // Draw the UI
        OnDrawUi!();

        // Render ImGui
        ImGui.Render();

        // Acquire next frame from renderer
        (FrameResult result, AcquiredFrame frame) = renderer!.TryAcquireNextFrame();
        if (result != FrameResult.Success)
        {
            RebuildRenderer();
            (result, frame) = renderer!.TryAcquireNextFrame();
        }

        if (result != FrameResult.Success)
        {
            throw new InvalidOperationException($"Failed to acquire frame: {result}");
        }

        (FrameResources resources, CommandBuffer commandBuffer) = frame;
        var begin = new VkRenderPassBeginInfo
        {
            RenderPass = renderer!.MainRenderPass,
            Framebuffer = resources.Framebuffer,
            RenderArea = new VkRect2D(renderer.Extent),
            ClearValues = rstate!.ClearValues!.Ptr,
            ClearValueCount = 2
        };

        // Record and submit command buffer
        commandBuffer.Reset();
        commandBuffer.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
        commandBuffer.BeginRenderPass(in begin, VkSubpassContents.Inline);
        ImGuiBackend.Vulkan.RenderDrawData(commandBuffer);
        commandBuffer.EndRenderPass();
        commandBuffer.End();

        // Submit frame
        FrameResult frameResult = renderer.TrySubmitFrame();
        ImGui.UpdatePlatformWindows();
        ImGui.RenderPlatformWindowsDefault();

        if (frameResult != FrameResult.Success)
        {
            RebuildRenderer();
        }
    }

    /// <summary>
    ///     Rebuilds the renderer when needed (e.g., window resize).
    /// </summary>
    private static void RebuildRenderer()
    {
        renderer!.Rebuild(VkPresentModeKHR.FifoKHR);
        renderer!.Device.WaitIdle();
        rstate!.Pass = renderer!.MainRenderPass;
    }

    /// <summary>
    ///     Calculates delta time since last frame.
    /// </summary>
    /// <returns>Delta time in seconds</returns>
    private static float GetDeltaTime()
    {
        DateTime currentTime = DateTime.Now;
        float deltaTime = (float)(currentTime - _lastFrameTime).TotalSeconds;
        _lastFrameTime = currentTime;
        return deltaTime;
    }

}
