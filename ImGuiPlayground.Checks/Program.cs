using Brutal.ImGuiApi;
using ImGuiPlayground;
using XBrutal.ImGuiStandalone;

// Console checks run on Main: macOS GLFW cannot run on NUnit worker threads.
// Only consumer integration lives here; reusable host/input/ABI checks live in the producer.
try
{
    if (args is ["--help"])
    {
        Console.WriteLine("Usage: ImGuiPlayground.Checks [--widgets [capture-directory] | --packaging [path/to/ImGuiPlayground.csproj]]");
        return 0;
    }
    if (args is ["--widgets"] or ["--widgets", _])
    {
        WidgetGalleryChecks.Run(args.Length == 2 ? args[1] : Path.Combine(AppContext.BaseDirectory, "widget-captures"));
        return 0;
    }
    if (args is ["--packaging"] or ["--packaging", _])
    {
        PackagingChecks.Run(args.Length == 2 ? args[1] : null);
        return 0;
    }
    if (args.Length != 0) throw new ArgumentException("Unknown arguments. Use --help for usage.");

    var gallery = new WidgetGallery();
    int frames = 0;
    CapturedFrame image = StandaloneHost.Capture(() =>
    {
        ++frames;
        gallery.Draw(); // The application still uses the original BRUTAL API directly.
        if (!ImGui.GetFont().GetDebugName().ToString().Contains("JetBrainsMono-Regular", StringComparison.Ordinal))
            throw new InvalidOperationException("The package-delivered font is not active.");
    });
    if (frames != 3 || image.Width <= 0 || image.Height <= 0 || image.Rgba.Length != checked(image.Width * image.Height * 4))
        throw new InvalidOperationException("The application did not receive a complete package capture.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}
