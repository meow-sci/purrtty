using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

internal static class PackagingChecks
{
    // These checks build/publish foreign RIDs but do NOT execute foreign binaries.
    // Locate the sibling source project, not an enclosing repository or a fixed cwd.
    // No desktop, Python, or native compiler required.
    internal static void Run(string? projectPath = null)
    {
        string project = projectPath is null ? FindProject() : Path.GetFullPath(projectPath);
        if (!File.Exists(project)) throw new FileNotFoundException("Playground project not found.", project);
        string gameDir = Dotnet("msbuild", project, "-nologo", "-getProperty:_PlaygroundKsaFolder").Trim();
        if (gameDir.Length == 0)
            throw new InvalidOperationException("Packaging checks start new builds: set KSA_DLL_DIR in the environment. MSBuild -p overrides on dotnet run are not inherited by child processes.");
        var temporary = Directory.CreateTempSubdirectory("imgui-packaging-");
        try
        {
            string missing = Path.Combine(temporary.FullName, "no-external-native-runtime");
            foreach (string rid in new[] { "osx-arm64", "linux-x64", "win-x64" })
            {
                string output = Path.Combine(temporary.FullName, rid);
                string[] publish = ["publish", project, "--nologo", "-v", "quiet", "-c", "Release",
                    "-r", rid, "--self-contained", "false", "-o", output,
                    "-p:KSA_DLL_DIR=" + gameDir,
                    "-p:KSA_NATIVE_DIR=" + missing, "-p:KSA_GLFW_DIR=" + missing];
                Dotnet(publish);
                using var manifest = JsonDocument.Parse(Dotnet("msbuild", project, "-nologo",
                    "-p:RuntimeIdentifier=" + rid, "-getItem:_PlaygroundNative"));
                var assets = manifest.RootElement.GetProperty("Items").GetProperty("_PlaygroundNative");
                if (assets.GetArrayLength() != 2) throw new Exception($"Expected exactly two assets for {rid}.");
                CheckOutput(output, rid, assets);

                // A newer timestamp must not preserve an obsolete native DLL. This caught
                // the old PreserveNewest behavior when switching from local GLFW to NuGet.
                foreach (var asset in assets.EnumerateArray())
                {
                    string path = Path.Combine(output, asset.GetProperty("TargetPath").GetString()!);
                    File.WriteAllText(path, "obsolete native runtime");
                    File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
                }
                Dotnet(publish);
                CheckOutput(output, rid, assets);
            }

            ExpectFailure("Unsupported playground runtime", "msbuild", project, "-nologo",
                "-t:ValidatePlaygroundNative", "-p:RuntimeIdentifier=linux-arm64");

            // Corrupt the expected pin through a temporary MSBuild hook, never a repo file
            // or NuGet cache asset, to prove the hash verification is actually enforced.
            string hook = Path.Combine(temporary.FullName, "wrong-hash.targets");
            File.WriteAllText(hook, """
                <Project>
                    <Target Name="CorruptExpectedNativeHash" BeforeTargets="ValidatePlaygroundNative">
                        <ItemGroup>
                            <_PlaygroundNative Update="@(_PlaygroundNative)">
                                <Sha256>0000000000000000000000000000000000000000000000000000000000000000</Sha256>
                            </_PlaygroundNative>
                        </ItemGroup>
                    </Target>
                </Project>
                """);
            ExpectFailure("MSB3952", "msbuild", project, "-nologo", "-t:ValidatePlaygroundNative",
                "-p:RuntimeIdentifier=osx-arm64", "-p:CustomBeforeMicrosoftCommonTargets=" + hook);
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

    private static void CheckOutput(string output, string rid, JsonElement assets)
    {
        string[] expected = rid switch
        {
            "osx-arm64" => ["libimgui.dylib", "libglfw.dylib"],
            "linux-x64" => ["libimgui.so", "libglfw.so"],
            "win-x64" => ["imgui.dll", "glfw3.dll"],
            _ => throw new ArgumentOutOfRangeException(nameof(rid))
        };
        string[] nativeNames = ["libimgui.dylib", "libimgui.so", "imgui.dll", "libglfw.dylib", "libglfw.so",
            "glfw3.dll", "libglfw.3.dylib", "libglfw.so.3"];
        var staged = Directory.GetFiles(output, "*", SearchOption.AllDirectories)
            .Where(path => nativeNames.Contains(Path.GetFileName(path), StringComparer.Ordinal)).ToArray();
        if (staged.Length != 2 || staged.Any(path => Path.GetDirectoryName(path) != output || !expected.Contains(Path.GetFileName(path))))
            throw new Exception($"Wrong native files staged for {rid}: {string.Join(", ", staged)}");
        foreach (var asset in assets.EnumerateArray())
        {
            string filename = asset.GetProperty("TargetPath").GetString()!;
            byte[] binary = File.ReadAllBytes(Path.Combine(output, filename));
            string hash = Convert.ToHexString(SHA256.HashData(binary));
            if (!hash.Equals(asset.GetProperty("Sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new Exception($"Published {rid}/{filename} does not match its SHA-256 pin.");
            CheckArchitecture(binary, rid);
        }
        foreach (string filename in new[] { "Brutal.ImGui.dll", "Brutal.Glfw.dll", "fonts/JetBrainsMono-Regular.ttf", "LICENSE", "THIRD-PARTY-NOTICES.md" })
            if (!File.Exists(Path.Combine(output, filename))) throw new Exception($"Missing {filename} in {rid} output.");
    }

    private static void CheckArchitecture(ReadOnlySpan<byte> binary, string rid)
    {
        bool correct;
        if (rid == "osx-arm64")
            correct = BinaryPrimitives.ReadUInt32LittleEndian(binary) == 0xFEEDFACF &&
                BinaryPrimitives.ReadUInt32LittleEndian(binary[4..]) == 0x0100000C;
        else if (rid == "linux-x64")
            correct = binary[..6].SequenceEqual(new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1 }) &&
                BinaryPrimitives.ReadUInt16LittleEndian(binary[18..]) == 62;
        else
        {
            int pe = BinaryPrimitives.ReadInt32LittleEndian(binary[0x3C..]);
            correct = binary[..2].SequenceEqual("MZ"u8) && binary.Slice(pe, 4).SequenceEqual("PE\0\0"u8) &&
                BinaryPrimitives.ReadUInt16LittleEndian(binary[(pe + 4)..]) == 0x8664;
        }
        if (!correct) throw new Exception($"Native binary architecture does not match {rid}.");
    }

    private static string Dotnet(params string[] arguments)
    {
        var result = Execute(arguments);
        if (result.Code != 0) throw new Exception($"dotnet {string.Join(' ', arguments)} failed:\n{result.Output}{result.Error}");
        return result.Output;
    }

    private static void ExpectFailure(string message, params string[] arguments)
    {
        var result = Execute(arguments);
        if (result.Code == 0 || !(result.Output + result.Error).Contains(message, StringComparison.OrdinalIgnoreCase))
            throw new Exception($"Expected failure containing '{message}', got {result.Code}:\n{result.Output}{result.Error}");
    }

    private static (int Code, string Output, string Error) Execute(string[] arguments)
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
            throw new TimeoutException("Packaging check exceeded its three-minute command deadline.");
        }
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
