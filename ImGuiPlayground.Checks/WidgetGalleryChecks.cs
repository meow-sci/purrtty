using Brutal.ImGuiApi;
using Brutal.Numerics;
using ImGuiPlayground;

// Focused scene and mouse checks on the real host/native widgets, not a UI automation layer.
internal static class WidgetGalleryChecks
{
    internal static void Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        CapturedFrame? previous = null;
        foreach (MockupScene scene in Enum.GetValues<MockupScene>())
        {
            var gallery = new WidgetGallery(scene);
            CapturedFrame image = Capture(() =>
            {
                gallery.Draw();
                if (scene == MockupScene.Demo) CheckDemoWindowBounds();
            }, outputDirectory, scene.ToString().ToLowerInvariant());
            if (scene == MockupScene.Demo) CheckDemoContent(image);
            if (scene == MockupScene.Parts) Require(gallery.SawPartTree, "The selected-part tree did not expand.");
            if (previous is not null) Require(!previous.Rgba.Span.SequenceEqual(image.Rgba.Span), "Different scenes rendered identical pixels.");
            previous = image;
        }

        var parts = new WidgetGallery(MockupScene.Parts);
        parts.RequestReload();
        Capture(parts.Draw, outputDirectory, "parts-modal");
        Require(parts.SawReloadModal, "The reload confirmation modal was not rendered.");

        var tuning = new WidgetGallery(MockupScene.Tuning);
        tuning.RequestColorPicker();
        Capture(tuning.Draw, outputDirectory, "tuning-picker");
        Require(tuning.SawColorPicker, "The full color picker popup was not rendered.");

        var camera = new WidgetGallery();
        Click(camera, () => camera.PlayButtonCenter, outputDirectory, "camera-play");
        Require(camera.IsPlaying, "Clicking Play did not change playback state.");
        // The label precedes the button: frame 3 processes release after submitting PAUSED.
        // Reuse the clicked fake model in a fresh capture to retain the settled PLAYING view.
        CheckPlayingLabel(Capture(camera.Draw, outputDirectory, "camera-play"));
        camera = new WidgetGallery();
        Click(camera, () => camera.LoopCheckboxCenter, outputDirectory, "camera-loop");
        Require(!camera.LoopEnabled, "Clicking Loop did not toggle the checkbox state.");

        var galleryDemo = new WidgetGallery();
        galleryDemo.RequestDemo(); // Same render path used by the real checkbox/menu.
        CheckDemoContent(Capture(() =>
        {
            galleryDemo.Draw();
            CheckDemoWindowBounds();
        }, outputDirectory, "demo-gallery"));
    }

    private static void CheckDemoWindowBounds()
    {
        // SetWindowSize updates the created window's next layout; validate subsequent frames.
        if (ImGui.GetFrameCount() == 1) return;
        // Append a balanced public Begin/End to query the actual native demo window, not structs.
        ImGui.Begin("Dear ImGui Demo"u8);
        try
        {
            float2 position = ImGui.GetWindowPos(), size = ImGui.GetWindowSize();
            float2 viewport = ImGui.GetIO().DisplaySize;
            Require(position.X >= 0 && position.Y >= 0 && position.X <= 16 && position.Y <= 16 &&
                size.X >= viewport.X - 32 && size.Y >= viewport.Y - 32 &&
                position.X + size.X <= viewport.X && position.Y + size.Y <= viewport.Y,
                $"Native demo bounds are outside the viewport: position {position}, size {size}, viewport {viewport}.");
        }
        finally { ImGui.End(); }
    }

    private static void CheckDemoContent(CapturedFrame image)
    {
        int brightPixels = 0;
        ReadOnlySpan<byte> pixels = image.Rgba.Span;
        // The old x=650 logical default leaves this left/central body region blank.
        for (int y = image.Height * 60 / 500; y < image.Height * 430 / 500; ++y)
        for (int x = image.Width * 20 / 800; x < image.Width * 620 / 800; ++x)
        {
            int i = (y * image.Width + x) * 4;
            if (pixels[i] > 180 && pixels[i + 1] > 180 && pixels[i + 2] > 180) ++brightPixels;
        }
        Require(brightPixels > 500, "Expected visible demo content in the left/central body region.");
    }

    private static void CheckPlayingLabel(CapturedFrame image)
    {
        int greenPixels = 0;
        ReadOnlySpan<byte> pixels = image.Rgba.Span;
        // PLAYING is green; PAUSED is cyan. Restrict the assertion to the status label row.
        for (int y = image.Height * 140 / 500; y < image.Height * 164 / 500; ++y)
        for (int x = image.Width * 16 / 800; x < image.Width * 460 / 800; ++x)
        {
            int i = (y * image.Width + x) * 4;
            if (pixels[i + 1] > 160 && pixels[i + 1] > pixels[i] + 40 && pixels[i + 1] > pixels[i + 2] + 20) ++greenPixels;
        }
        Require(greenPixels > 100, "The settled capture did not show the green PLAYING status label.");
    }

    private static void Click(WidgetGallery gallery, Func<float2> target, string directory, string name)
    {
        int frame = 0;
        Capture(() =>
        {
            gallery.Draw();
            // Queue events for the next NewFrame. Press on frame 2, release on frame 3.
            var io = ImGui.GetIO();
            if (++frame == 1)
            {
                float2 point = target();
                io.AddMousePosEvent(point.X, point.Y);
                io.AddMouseButtonEvent(0, true);
            }
            else if (frame == 2) io.AddMouseButtonEvent(0, false);
        }, directory, name);
        Require(frame == 3, "Interaction did not render three frames.");
    }

    private static CapturedFrame Capture(Action draw, string directory, string name)
    {
        // Host requires nonempty vertex/index geometry on each frame and checks GL errors.
        CapturedFrame image = PlaygroundHost.Capture(draw);
        Require(image.Rgba.Length == checked(image.Width * image.Height * 4), "Invalid capture size.");
        int brightPixels = 0, coloredPixels = 0;
        ReadOnlySpan<byte> pixels = image.Rgba.Span;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 180 && pixels[i + 1] > 180 && pixels[i + 2] > 180) ++brightPixels;
            if (Math.Abs(pixels[i] - pixels[i + 2]) > 30) ++coloredPixels;
        }
        Require(brightPixels > 500 && coloredPixels > 500, $"{name}: expected text and colored widget pixels.");
        image.SavePng(Path.Combine(directory, name + ".png"));
        return image;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
