namespace ImGuiPlayground;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is ["--help"])
        {
            Console.WriteLine("Usage: dotnet run --project ImGuiPlayground -- [--mockup <camera|parts|tuning|demo>] [--smoke-test | --capture <file.png>]");
            return 0;
        }

        MockupScene scene = MockupScene.Camera;
        bool selected = false, smokeTest = false;
        string? capturePath = null;
        for (int i = 0; i < args.Length; ++i)
        {
            if (args[i] == "--mockup" && !selected && i + 1 < args.Length)
            {
                string name = args[++i];
                if (name is not ("camera" or "parts" or "tuning" or "demo")) return InvalidArguments();
                scene = name switch { "parts" => MockupScene.Parts, "tuning" => MockupScene.Tuning, "demo" => MockupScene.Demo, _ => MockupScene.Camera };
                selected = true;
            }
            else if (args[i] == "--smoke-test" && !smokeTest && capturePath is null) smokeTest = true;
            else if (args[i] == "--capture" && !smokeTest && capturePath is null && i + 1 < args.Length) capturePath = args[++i];
            else return InvalidArguments();
        }

        try
        {
            var gallery = new WidgetGallery(scene);
            if (capturePath is not null)
                PlaygroundHost.Capture(gallery.Draw).SavePng(capturePath);
            else
                PlaygroundHost.Run(gallery.Draw, smokeTest);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ImGuiPlayground: {exception}");
            return 1;
        }
    }

    private static int InvalidArguments()
    {
        Console.Error.WriteLine("Unknown arguments. Use --help for usage.");
        return 2;
    }
}
