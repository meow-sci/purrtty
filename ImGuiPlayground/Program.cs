using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace ImGuiPlayground;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is ["--help"])
        {
            Console.WriteLine("Usage: dotnet run --project ImGuiPlayground [-- --smoke-test | --capture <file.png>]");
            return 0;
        }

        if (args.Length != 0 && args is not ["--smoke-test"] && args is not ["--capture", _])
        {
            Console.Error.WriteLine("Unknown arguments. Use --help for usage.");
            return 2;
        }

        try
        {
            if (args is ["--capture", var path])
                PlaygroundHost.Capture(DrawHelloWorld).SavePng(path);
            else
                PlaygroundHost.Run(DrawHelloWorld, smokeTest: args.Length != 0);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ImGuiPlayground: {exception}");
            return 1;
        }
    }

    // This is ordinary KSA-compatible BRUTAL UI code. Replace this callback to iterate.
    private static void DrawHelloWorld()
    {
        ImGui.SetNextWindowPos(new float2(40, 40), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new float2(360, 160), ImGuiCond.FirstUseEver);
        bool visible = ImGui.Begin("ImGui Playground"u8);
        try
        {
            if (visible)
                ImGui.Text("Hello world!"u8);
        }
        finally
        {
            ImGui.End();
        }
    }
}
