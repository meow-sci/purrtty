using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

internal static class PackagingChecks
{
    private const string PackageVersion = "0.1.0-local.4";
    private const string PackageSha256 = "1c2c4a35ba6c3004a6bbb34c1ff7bc7d53946a95b00eb5b880f305222377bd20";

    // Consumer output checks only. Native export/ABI/startup-negative tests belong to the producer.
    // Child builds inherit explicit KSA_DLL_DIR and optional isolated NUGET_PACKAGES.
    internal static void Run(string? projectPath = null)
    {
        string project = projectPath is null ? FindProject() : Path.GetFullPath(projectPath);
        if (!File.Exists(project)) throw new FileNotFoundException("Playground project not found.", project);
        var temporary = Directory.CreateTempSubdirectory("imgui-consumer-packaging-");
        try
        {
            string portable = Path.Combine(temporary.FullName, "portable");
            Dotnet("publish", project, "--nologo", "-v", "quiet", "-c", "Release", "-o", portable);
            string assetsPath = Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json");
            using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
            string imguiPackage = PackageDirectory(assets.RootElement, "XBrutal.ImGuiStandalone/" + PackageVersion);
            string glfwPackage = PackageDirectory(assets.RootElement, "Ultz.Native.GLFW/3.4.0");
            string archive = Path.Combine(imguiPackage, $"xbrutal.imguistandalone.{PackageVersion}.nupkg");
            if (Hash(archive) != PackageSha256) throw new Exception("Restored standalone package differs from the approved archive.");
            CheckOutput(portable, imguiPackage, glfwPackage, rid: null);
            foreach (string rid in new[] { "osx-arm64", "linux-x64", "win-x64" })
            {
                string output = Path.Combine(temporary.FullName, rid);
                Dotnet("publish", project, "--nologo", "-v", "quiet", "-c", "Release", "-r", rid,
                    "--self-contained", "false", "-o", output);
                CheckOutput(output, imguiPackage, glfwPackage, rid);
            }
        }
        finally { temporary.Delete(recursive: true); }
    }

    private static string FindProject()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string project = Path.Combine(directory.FullName, "ImGuiPlayground", "ImGuiPlayground.csproj");
            if (File.Exists(project)) return project;
        }
        throw new FileNotFoundException("Cannot locate the playground source tree. Pass --packaging /path/to/ImGuiPlayground.csproj.");
    }

    private static string PackageDirectory(JsonElement assets, string identity)
    {
        string path = assets.GetProperty("libraries").GetProperty(identity).GetProperty("path").GetString()!;
        foreach (var folder in assets.GetProperty("packageFolders").EnumerateObject())
        {
            string candidate = Path.Combine(folder.Name, path);
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException($"Resolved package not found: {identity}.");
    }

    private static void CheckOutput(string output, string imguiPackage, string glfwPackage, string? rid)
    {
        string[] nativeNames = ["libimgui.dylib", "libimgui.so", "imgui.dll", "libglfw.dylib", "libglfw.so",
            "glfw3.dll", "libglfw.3.dylib", "libglfw.so.3"];
        if (rid is null && Directory.GetFiles(output).Any(path => nativeNames.Contains(Path.GetFileName(path))))
            throw new Exception("Portable output contains obsolete flat native files.");
        foreach (string target in rid is null ? new[] { "osx-arm64", "linux-x64", "win-x64" } : new[] { rid })
        {
            (string imgui, string glfw, string alias) = target switch
            {
                "osx-arm64" => ("libimgui.dylib", "libglfw.3.dylib", "libglfw.dylib"),
                "linux-x64" => ("libimgui.so", "libglfw.so.3", "libglfw.so"),
                "win-x64" => ("imgui.dll", "glfw3.dll", "glfw3.dll"),
                _ => throw new ArgumentOutOfRangeException(nameof(rid))
            };
            string layout = rid is null ? Path.Combine("runtimes", target, "native") : "";
            SameFile(Path.Combine(output, layout, imgui), Path.Combine(imguiPackage, "runtimes", target, "native", imgui));
            string glfwSource = Path.Combine(glfwPackage, "runtimes", target, "native", glfw);
            SameFile(Path.Combine(output, layout, glfw), glfwSource);
            SameFile(Path.Combine(output, layout, alias), glfwSource);
        }
        SameFile(Path.Combine(output, "XBrutal.ImGuiStandalone.dll"), Path.Combine(imguiPackage, "lib", "net10.0", "XBrutal.ImGuiStandalone.dll"));
        SameFile(Path.Combine(output, "fonts", "JetBrainsMono-Regular.ttf"), Path.Combine(imguiPackage, "contentFiles", "any", "net10.0", "fonts", "JetBrainsMono-Regular.ttf"));
        foreach (string filename in new[] { "Brutal.ImGui.dll", "Brutal.Glfw.dll", "LICENSE", "THIRD-PARTY-NOTICES.md" })
            if (!File.Exists(Path.Combine(output, filename))) throw new Exception($"Missing {filename} in consumer output.");
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static void SameFile(string output, string source)
    {
        if (Hash(output) != Hash(source)) throw new Exception($"Consumer output differs from resolved package asset: {output}.");
    }

    private static void Dotnet(params string[] arguments)
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new Exception("Could not start dotnet.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(180_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("Consumer packaging command exceeded its three-minute deadline.");
        }
        if (process.ExitCode != 0)
            throw new Exception($"dotnet {string.Join(' ', arguments)} failed:\n{output.GetAwaiter().GetResult()}{error.GetAwaiter().GetResult()}");
    }
}
