using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Brutal.ImGuiApi;
using ImGuiPlayground;
using ImGuiPlayground.Checks;

// Only the opt-in runner invokes this. Every unsafe child establishes its own
// guard using its actual loaded CLR contract and freshly executed target probes.
internal static unsafe class FullQualificationChecks
{
    private static Session? rendererSession;
    private sealed record RendererAssets(string GlfwPath, string GlfwSha256, string ManagedGlfwSha256, string FontSha256);
    private sealed record Session(string Mode, string Output, string Native, string NativeHash, nint Handle, object Identities, JsonNode Guard, string[] Exports, RendererAssets? RendererAssets);
    [StructLayout(LayoutKind.Sequential)]
    private struct DlInfo { public nint FileName, Base, SymbolName, SymbolAddress; }
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dladdr")]
    private static extern int MacAddress(nint address, out DlInfo info);
    [DllImport("libdl.so.2", EntryPoint = "dladdr")]
    private static extern int LinuxAddress(nint address, out DlInfo info);
    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern uint _dyld_image_count();
    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern nint _dyld_get_image_name(uint index);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetModuleHandleExW(uint flags, nint address, out nint module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetModuleFileNameW(nint module, char* filename, uint size);

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidDataException("Full qualification: " + message);
    }
    private static string Hash(string file) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
    private static JsonNode Read(string file)
    {
        Require(new FileInfo(file).Length <= 128 * 1024 * 1024, "JSON bound");
        using var document = JsonDocument.Parse(File.ReadAllBytes(file));
        void Unique(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    Require(keys.Add(property.Name), "duplicate JSON key: " + property.Name);
                    Unique(property.Value);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray()) Unique(item);
        }
        Unique(document.RootElement);
        return JsonNode.Parse(document.RootElement.GetRawText())!;
    }
    private static void Execute(string executable, IEnumerable<string> arguments, string? log = null)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("Could not start guard");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(180_000))
        {
            process.Kill(entireProcessTree: true); process.WaitForExit();
            throw new TimeoutException("Qualification guard exceeded bounded process timeout");
        }
        string output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        if (log is not null) File.WriteAllText(log, output);
        Require(process.ExitCode == 0, "guard process failed: " + output);
    }
    private static string[] Images(string filename)
    {
        if (OperatingSystem.IsMacOS())
            return Enumerable.Range(0, checked((int)_dyld_image_count()))
                .Select(i => Marshal.PtrToStringUTF8(_dyld_get_image_name((uint)i))!)
                .Where(p => Path.GetFileName(p).Equals(filename, StringComparison.OrdinalIgnoreCase)).ToArray();
        using var current = Process.GetCurrentProcess();
        return current.Modules.Cast<ProcessModule>().Select(m => m.FileName)
            .Where(p => Path.GetFileName(p).Equals(filename, StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();
    }
    private static string AddressImage(nint address)
    {
        if (OperatingSystem.IsWindows())
        {
            Require(GetModuleHandleExW(6, address, out nint module), "module from export address");
            char* path = stackalloc char[32768];
            uint count = GetModuleFileNameW(module, path, 32768);
            Require(count > 0 && count < 32768, "module path bounds");
            return new string(path, 0, (int)count);
        }
        DlInfo info;
        int result = OperatingSystem.IsMacOS() ? MacAddress(address, out info) : LinuxAddress(address, out info);
        Require(result != 0, "dladdr export attribution");
        return Marshal.PtrToStringUTF8(info.FileName)!;
    }

    internal static bool Run(string mode, string bundle, string anchor, string python, string receiptPath, string output)
    {
        Require(mode is "accessor" or "profile" or "manual" or "composition" or "renderer" or "capture", "unknown mode");
        // Stdlib-only bootstrap checks physical leaves/ancestors/shared files before
        // reading any controlled input, importing a project module or making output.
        const string bootstrap = """
import os,sys,stat,json
from pathlib import Path
def physical(raw):
 p=Path(raw)
 if '..' in p.parts: raise ValueError('full preflight: traversal')
 p=p.absolute()
 for x in reversed((p,*p.parents)):
  s=x.lstat()
  if stat.S_ISLNK(s.st_mode) or getattr(s,'st_file_attributes',0)&getattr(stat,'FILE_ATTRIBUTE_REPARSE_POINT',0): raise ValueError('full preflight: redirect')
  if not (stat.S_ISDIR(s.st_mode) or x==p and stat.S_ISREG(s.st_mode) and s.st_nlink==1): raise ValueError('full preflight: shared/nonregular')
 return p
def tree(p):
 p=physical(p)
 for r,ds,fs in os.walk(p,followlinks=False):
  for n in ds+fs: physical(Path(r)/n)
 return p
def unique(pairs):
 d={}
 for k,v in pairs:
  if k in d: raise ValueError('full preflight: duplicate key')
  d[k]=v
 return d
bundle=tree(sys.argv[1]); receipt=physical(sys.argv[2]); out=Path(sys.argv[3]).absolute()
r=json.loads(receipt.read_text(),object_pairs_hook=unique)
protected=[bundle,receipt]+[tree(r[k]) for k in ('stageDirectory','builtDirectory','selectedDirectory')]
if '..' in Path(sys.argv[3]).parts or out.exists() or out.is_symlink(): raise ValueError('full preflight: fresh output required')
for p in protected:
 if out==p or p in out.parents or out in p.parents: raise ValueError('full preflight: output overlaps input')
for p in reversed((out.parent,*out.parent.parents)):
 if p.exists() or p.is_symlink(): physical(p)
""";
        Execute(python, ["-c", bootstrap, bundle, receiptPath, output]);
        bundle = Path.GetFullPath(bundle); output = Path.GetFullPath(output);
        Require(anchor.Length == 64 && anchor.All(c => char.IsAsciiHexDigit(c)) && Hash(Path.Combine(bundle, "bundle.json")) == anchor, "independent manifest anchor mismatch");
        JsonNode manifest = Read(Path.Combine(bundle, "bundle.json"));
        JsonNode receipt = Read(receiptPath);
        Require(receipt["schema"]!.GetValue<string>() == "purr.full-stage.v1" && receipt["bundleManifestSha256"]!.GetValue<string>() == anchor, "stage receipt schema/anchor");
        string stage = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        Require(stage == receipt["stageDirectory"]!.GetValue<string>(), "wrong actual process stage");
        string kind = mode is "renderer" or "capture" ? "production" : "fixture";
        Require(kind == receipt["kind"]!.GetValue<string>(), "wrong production/fixture selection");
        string rid = OperatingSystem.IsMacOS() ? "osx-arm64" : OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        Require(RuntimeInformation.ProcessArchitecture == (OperatingSystem.IsMacOS() ? Architecture.Arm64 : Architecture.X64) && receipt["rid"]!.GetValue<string>() == rid, "actual process target");
        foreach (var file in manifest["files"]!.AsObject())
        {
            string name = file.Key;
            Require(!Path.IsPathRooted(name) && !name.Split('/').Any(p => p is ".." or "" or ".") && !name.Contains('\\') && !name.Contains(':'), "bundle path");
            string path = Path.Combine(bundle, name);
            Require(new FileInfo(path).Length == file.Value!["bytes"]!.GetValue<long>() && Hash(path) == file.Value["sha256"]!.GetValue<string>(), "bundle file hash: " + name);
        }
        var identities = new List<object>();
        string selected = receipt["selectedDirectory"]!.GetValue<string>();
        string built = receipt["builtDirectory"]!.GetValue<string>();
        var selectedIdentities = new JsonArray();
        foreach (var pin in manifest["managedPins"]!.AsObject())
        {
            string name = pin.Key, expected = pin.Value!["sha256"]!.GetValue<string>();
            Assembly loaded = Assembly.Load(name);
            string filename = name + ".dll";
            Require(Hash(Path.Combine(selected, filename)) == expected && Hash(Path.Combine(built, filename)) == expected && Hash(Path.Combine(stage, filename)) == expected && Hash(loaded.Location) == expected, "selected/built/staged/loaded contributor: " + name);
            Require(Path.GetFullPath(loaded.Location) == Path.Combine(stage, filename) && loaded.ManifestModule.ModuleVersionId.ToString() == pin.Value["moduleMvid"]!.GetValue<string>(), "actual contributor location/MVID: " + name);
            identities.Add(new { name, expected, selected, built, staged = Path.Combine(stage, filename), loaded = loaded.Location, mvid = loaded.ManifestModule.ModuleVersionId });
            selectedIdentities.Add(new JsonObject { ["name"] = name, ["sha256"] = expected, ["moduleMvid"] = loaded.ManifestModule.ModuleVersionId.ToString(), ["resolutionSource"] = "selected-directory" });
        }
        foreach (string name in new[] { "ImGuiPlayground", "ImGuiPlayground.Checks" })
        {
            Assembly loaded = Assembly.Load(name);
            string file = name + ".dll", expected = receipt["managedHashes"]![file]!.GetValue<string>();
            Require(Hash(loaded.Location) == expected && Hash(Path.Combine(built, file)) == expected && Path.GetFullPath(loaded.Location) == Path.Combine(stage, file), "actual built Host/Checks boundary: " + name);
        }
        Directory.CreateDirectory(output);
        string contractPath = Path.Combine(output, "actual-contract.json");
        JsonObject contract = new BindingContractExporter().Build(typeof(ImGui).Assembly);
        contract["assemblies"] = selectedIdentities;
        File.WriteAllText(contractPath, contract.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        string guardOutput = Path.Combine(Path.GetDirectoryName(output)!, "guard-" + Path.GetFileName(output));
        string runner = Path.Combine(bundle, "source", "ImGuiPlayground", "native", "full", "qualification", "run.py");
        Execute(python, [runner, "guard", "--bundle", bundle, "--anchor", anchor, "--selection", selected, "--stage", stage, "--kind", kind, "--contract", contractPath, "--output", guardOutput], Path.Combine(output, "guard.log"));
        JsonNode guard = Read(Path.Combine(guardOutput, "guard.json"));
        string filenameNative = OperatingSystem.IsWindows() ? "imgui.dll" : OperatingSystem.IsMacOS() ? "libimgui.dylib" : "libimgui.so";
        string native = Path.Combine(stage, filenameNative), nativeHash = Hash(native);
        Require(guard["status"]!.GetValue<string>() == "passed" && guard["actualContractSha256"]!.GetValue<string>() == Hash(contractPath) && guard["nativeArtifactSha256"]!.GetValue<string>() == nativeHash && guard["bundleManifestSha256"]!.GetValue<string>() == anchor, "guard report is not this process/artifact");
        Require(nativeHash == receipt["nativeSha256"]!.GetValue<string>() && Hash(receipt["nativeSource"]!.GetValue<string>()) == nativeHash, "source/stage native mismatch");
        Require(Images(filenameNative).Length == 0, "preexisting native image invalidates ownership/audit premise");
        RendererAssets? rendererAssets = null;
        if (kind == "production")
        {
            var runtimeManifest = XDocument.Load(Path.Combine(bundle, "source/ImGuiPlayground/runtimes/NativeLibraries.props"));
            var glfw = runtimeManifest.Descendants("ItemGroup").Single(g => ((string?)g.Attribute("Condition"))?.Contains("'" + rid + "'", StringComparison.Ordinal) == true)
                .Elements("_PlaygroundNative").Single(e => ((string?)e.Attribute("Include"))?.Contains("GLFW", StringComparison.Ordinal) == true);
            string glfwName = glfw.Element("TargetPath")!.Value;
            string glfwPath = Path.Combine(stage, glfwName);
            string glfwHash = glfw.Element("Sha256")!.Value;
            var glfwAssembly = Assembly.Load("Brutal.Glfw");
            string managedGlfwHash = Hash(Path.Combine(selected, "Brutal.Glfw.dll"));
            Require(Path.GetFullPath(glfwAssembly.Location) == Path.Combine(stage, "Brutal.Glfw.dll") && Hash(glfwAssembly.Location) == managedGlfwHash && Hash(Path.Combine(built, "Brutal.Glfw.dll")) == managedGlfwHash, "selected/built/staged/loaded GLFW managed dependency");
            Require(Hash(glfwPath) == glfwHash && Hash(Path.Combine(built, glfwName)) == glfwHash, "ordinary pinned GLFW stage/source identity");
            string font = "fonts/JetBrainsMono-Regular.ttf";
            string fontHash = Hash(Path.Combine(bundle, "source/ImGuiPlayground", font));
            Require(Hash(Path.Combine(stage, font)) == fontHash && Hash(Path.Combine(built, font)) == fontHash, "ordinary pinned font stage/source identity");
            rendererAssets = new(glfwPath, glfwHash, managedGlfwHash, fontHash);
        }
        File.WriteAllText(Path.Combine(output, "native-open.marker"), "identity/layout guard completed before the sole owner loads\n");
        nint handle = PlaygroundHost.PrepareNativeLibraryForChecks();
        var exports = Read(Path.Combine(bundle, "evidence", "exports.json"))!.AsArray();
        var names = new List<string>();
        foreach (var row in exports)
        {
            string name = row!["name"]!.GetValue<string>();
            if (kind == "production" && row["surface"]!.GetValue<string>() == "fixture")
            {
                Require(!NativeLibrary.TryGetExport(handle, name, out _), "actual fixture leakage: " + name);
                continue;
            }
            nint address = NativeLibrary.GetExport(handle, name);
            Require(Path.GetFullPath(AddressImage(address)) == native, "export belongs to different actual image: " + name);
            names.Add(name);
        }
        var session = new Session(mode, output, native, nativeHash, handle, identities, guard, names.ToArray(), rendererAssets);
        object observations;
        switch (mode)
        {
            case "accessor":
                string fixtureFile = Path.Combine(output, "fixture-file.bin");
                File.WriteAllText(fixtureFile, "full qualification native file-size fixture\n");
                observations = FullAccessorChecks.Run(handle, Path.Combine(bundle, "evidence", "accessors.json"), selected, fixtureFile);
                break;
            case "profile":
                string matrix = File.ReadAllText(Path.Combine(bundle, "evidence", "matrix.json"));
                observations = new { shapes = FullProfileAbiChecks.Run(handle), actualEnums = FullEnumAbiChecks.Run(handle), transports = FullEnumAbiChecks.RunMatrix(handle, matrix), negatives = FullEnumAbiChecks.RunMatrixNegatives(handle, matrix) };
                break;
            case "manual": observations = FullManualAbiChecks.Run(handle); break;
            case "composition":
                MethodInfo method = typeof(CombinedComponentChecks).GetMethod("Composition", BindingFlags.NonPublic | BindingFlags.Static)!;
                try { observations = method.Invoke(null, [handle])!; }
                catch (TargetInvocationException error) when (error.InnerException is not null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
                break;
            case "renderer": rendererSession = session; return true;
            default:
                MethodInfo entry = typeof(PlaygroundHost).Assembly.EntryPoint!;
                Require(entry.DeclaringType!.FullName == "ImGuiPlayground.Program" && entry.Name == "Main" && entry.ReturnType == typeof(int), "exact ordinary capture entrypoint");
                string png = Path.Combine(output, "hello-world.png");
                Require(entry.Invoke(null, [new[] { "--capture", png }]) is int result && result == 0, "ordinary synchronous capture failed");
                byte[] bytes = File.ReadAllBytes(png);
                Require(bytes.Length > 32 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "capture PNG missing/malformed");
                observations = new { entrypoint = entry.ToString(), arguments = new[] { "--capture", png }, pngSha256 = Hash(png), bytes = bytes.Length };
                break;
        }
        Finish(session, observations);
        return false;
    }
    internal static void CompleteRenderer()
    {
        if (rendererSession is not { } session) return;
        Finish(session, new { actualExistingChecksPassed = true, checks = new[] { "empty subsequent frame", "exception identity/recovery", "repeat lifecycle", "draw/reset callbacks", "16-bit vertex rollover", "fonts/textures", "scissor/orientation/pixels", "keyboard/full-width Unicode", "PNG/CRC/exact pixels" } });
        rendererSession = null;
    }
    private static void Finish(Session session, object observations)
    {
        string[] images = Images(Path.GetFileName(session.Native));
        Require(images is [var image] && Path.GetFullPath(image) == session.Native && Hash(image) == session.NativeHash, "actual image identity after execution");
        if (session.RendererAssets is { } assets)
        {
            string[] loaded = Images(Path.GetFileName(assets.GlfwPath));
            Require(loaded.Length == 1 && Path.GetFullPath(loaded[0]) == assets.GlfwPath && Hash(loaded[0]) == assets.GlfwSha256, "actual loaded GLFW image attribution");
        }
        File.WriteAllText(Path.Combine(session.Output, "report.json"), JsonSerializer.Serialize(new
        {
            schema = "purr.full-process.v1", status = "passed", mode = session.Mode, processId = Environment.ProcessId,
            nativeSha256 = session.NativeHash, loadedImages = images, actualAttributedExports = session.Exports,
            selectedBuiltStagedLoadedContributors = session.Identities, guard = session.Guard, rendererAssets = session.RendererAssets, observations,
            nativeOwner = "existing PlaygroundHost cached owner/resolver; process lifetime", allEndpointSemanticsClaimed = false
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }) + "\n");
    }
}
