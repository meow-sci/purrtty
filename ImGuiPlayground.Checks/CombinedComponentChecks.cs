using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal.ImGuiApi;
using ImGuiPlayground.Checks;

// Explicit milestone-only dispatch. No renderer/host startup and no native work in ordinary builds.
internal static unsafe class CombinedComponentChecks
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DlInfo { public nint FileName, Base, SymbolName, SymbolAddress; }
    [DllImport("/usr/lib/libSystem.B.dylib", CallingConvention = CallingConvention.Cdecl)]
    private static extern int dladdr(nint address, out DlInfo info);
    [DllImport("/usr/lib/libSystem.B.dylib", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint _dyld_image_count();
    [DllImport("/usr/lib/libSystem.B.dylib", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint _dyld_get_image_name(uint index);
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("Combined component: " + message);
    }
    private static string[] ImGuiImages() => Enumerable.Range(0, checked((int)_dyld_image_count()))
        .Select(i => Marshal.PtrToStringUTF8(_dyld_get_image_name((uint)i))!)
        .Where(p => Path.GetFileName(p).Contains("imgui", StringComparison.OrdinalIgnoreCase)).ToArray();

    internal static void Run(string mode, string receiptPath, string reportPath)
    {
        Require(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && RuntimeInformation.ProcessArchitecture == Architecture.Arm64, "macOS arm64 execution only");
        Require(mode is "profile" or "manual" or "accessor" or "composition", "unknown process mode");
        using var receipt = JsonDocument.Parse(File.ReadAllBytes(receiptPath));
        var root = receipt.RootElement;
        Require(root.GetProperty("schema").GetString() == "purr.combined.stage.v1", "wrong stage receipt");
        var identities = new List<object>();
        string stage = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        foreach (var pin in root.GetProperty("assemblies").EnumerateObject())
        {
            Assembly loaded = Assembly.Load(pin.Name);
            string expected = pin.Value.GetProperty("sha256").GetString()!;
            string built = pin.Value.GetProperty("built").GetString()!;
            string selected = pin.Value.GetProperty("selected").GetString()!;
            string staged = Path.Combine(stage, pin.Name + ".dll");
            Require(Hash(selected) == expected && Hash(built) == expected && Hash(staged) == expected && Hash(loaded.Location) == expected, "selected/built/staged/loaded contributor: " + pin.Name);
            Require(Path.GetFullPath(loaded.Location) == staged, "loaded contributor outside disposable stage: " + pin.Name);
            if (pin.Value.TryGetProperty("moduleMvid", out var mvid)) Require(loaded.ManifestModule.ModuleVersionId.ToString() == mvid.GetString(), "MVID: " + pin.Name);
            identities.Add(new { name = pin.Name, expected, selected, built, staged, loaded = loaded.Location, mvid = loaded.ManifestModule.ModuleVersionId });
        }
        foreach (var file in root.GetProperty("files").EnumerateObject())
            Require(Hash(Path.Combine(stage, file.Name)) == file.Value.GetString(), "staged evidence hash: " + file.Name);
        string native = Path.Combine(stage, "libimgui.dylib");
        string sourceNative = root.GetProperty("sourceNative").GetString()!;
        Require(Hash(sourceNative) == Hash(native), "source/staged native mismatch");
        // Measure actual process CLR storage before any library load or raw native access.
        // The same-source exhaustive native comparison is a required hashed build input.
        var layouts = CompareManagedLayouts(Path.Combine(stage, "binding-contract.json"));
        Require(ImGuiImages().Length == 0, "preexisting ImGui image invalidates single-owner/fresh-process premise");
        nint handle = NativeLibrary.Load(native);
        // Keep this image and all explicit Interop references alive until process exit.
        NativeLibrary.SetDllImportResolver(typeof(ImGui).Assembly, (name, _, _) => name == "imgui" ? handle : 0);
        var exportedImages = new SortedDictionary<string, string>();
        using var exports = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(stage, "exports.json")));
        foreach (var row in exports.RootElement.EnumerateArray())
        {
            string name = row.GetProperty("name").GetString()!;
            nint address = NativeLibrary.GetExport(handle, name);
            Require(dladdr(address, out DlInfo info) != 0, "dladdr failed: " + name);
            string image = Marshal.PtrToStringUTF8(info.FileName)!;
            Require(Path.GetFullPath(image) == native && Hash(image) == Hash(sourceNative), "export from wrong loaded image: " + name);
            exportedImages.Add(name, image);
        }
        object observations = mode switch
        {
            "accessor" => FullAccessorChecks.Run(handle, Path.Combine(stage, "accessors.json"), root.GetProperty("selectedDirectory").GetString()!, root.GetProperty("fixtureFile").GetString()!),
            "manual" => FullManualAbiChecks.Run(handle),
            "profile" => new
            {
                profileShapes = FullProfileAbiChecks.Run(handle),
                actualEnumImports = FullEnumAbiChecks.Run(handle),
                typedEnumAndBackingCalls = FullEnumAbiChecks.RunMatrix(handle, File.ReadAllText(Path.Combine(stage, "matrix.json"))),
                matrixNegatives = FullEnumAbiChecks.RunMatrixNegatives(handle, File.ReadAllText(Path.Combine(stage, "matrix.json")))
            },
            _ => Composition(handle)
        };
        Require(ImGuiImages() is [var actual] && Path.GetFullPath(actual) == native, "more than one loaded ImGui image");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new
        {
            status = "passed", mode, processId = Environment.ProcessId, nativeSource = sourceNative,
            sourceSha256 = Hash(sourceNative), stagedNative = native, stagedSha256 = Hash(native),
            loadedImages = ImGuiImages(), exportedImages, loadedAssemblies = identities,
            managedLayouts = layouts, observations, fullQualification = false,
            freshAccessorPremise = mode == "accessor" ? "fresh process; no ImGui calls/allocations/context/objects before FullAccessorChecks audit begin; only loader/export-address inspection" : "not the accessor audit process"
        }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }) + "\n");
    }

    private static object CompareManagedLayouts(string contractPath)
    {
        var contract = JsonNode.Parse(File.ReadAllBytes(contractPath))!;
        int types = 0, fields = 0;
        foreach (var row in contract["types"]!.AsArray())
        {
            if (row!["kind"]!.GetValue<string>() is not ("value" or "scalar" or "enum") || row["measurement"]!["status"]!.GetValue<string>() != "measured") continue;
            Type type = Type.GetType(row["assemblyQualifiedName"]!.GetValue<string>(), throwOnError: true)!;
            Require(JsonNode.DeepEquals(BindingContractExporter.Measurement(type), row["measurement"]), "actual CLR layout differs: " + type);
            ++types;
            if (row["fields"] is not JsonArray entries) continue;
            foreach (var field in entries)
            {
                FieldInfo actual = type.GetField(field!["name"]!.GetValue<string>(), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)!;
                Require(actual is not null && JsonNode.DeepEquals(BindingContractExporter.FieldOffset(actual), field["runtimeOffset"]), "actual CLR field offset differs: " + type + "." + field["name"]);
                ++fields;
            }
        }
        Require(types > 300 && fields > 1000, "layout measurement inventory unexpectedly small");
        return new { types, fields, knownRawAliasesRemainUnsafe = true, nativeEvidence = "same-source classified exhaustive layout gate required by combined build; no conflict waivers added" };
    }

    private static object Composition(nint handle)
    {
        var current = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(handle, "GetCurrentContext");
        var getAllocator = (delegate* unmanaged[Cdecl]<nint*, nint*, nint*, void>)NativeLibrary.GetExport(handle, "GetAllocatorFunctions");
        var manualCreate = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(handle, "purr_manual_fixture_create");
        var manualDestroy = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(handle, "purr_manual_fixture_destroy");
        var profileCreate = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(handle, "profile_fixture_create");
        var profileDestroy = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(handle, "profile_fixture_destroy");
        var accessorCreate = (delegate* unmanaged[Cdecl]<int, nint>)NativeLibrary.GetExport(handle, "purr_fixture_field_create");
        var accessorDestroy = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(handle, "purr_fixture_field_destroy");
        var begin = (delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(handle, "purr_fixture_audit_begin");
        var end = (delegate* unmanaged[Cdecl]<ulong*, int>)NativeLibrary.GetExport(handle, "purr_fixture_audit_end");
        nint alloc = 0, free = 0, user = 0;
        getAllocator(&alloc, &free, &user);
        Require(current() == 0 && begin() == 0, "composition allocator interval setup");
        ulong* audit = stackalloc ulong[7];
        nint context = 0, profile = 0, accessor = 0;
        nint observedContext = 0, observedAccessorContext = 0;
        try
        {
            nint auditingAlloc = 0, auditingFree = 0, auditingUser = 0;
            getAllocator(&auditingAlloc, &auditingFree, &auditingUser);
            Require(auditingAlloc != alloc && auditingFree != free, "audit and wrapper allocator globals differ");
            context = manualCreate();
            Require(context != 0 && current() == context, "manual and wrapper context globals differ");
            observedContext = current();
            profile = profileCreate(context);
            Require(profile != 0 && current() == context, "profile context ownership differs");
            accessor = accessorCreate(0);
            Require(accessor != 0 && current() != 0 && current() != context, "accessor fixture did not set shared context");
            observedAccessorContext = current();
            accessorDestroy(accessor); accessor = 0;
            Require(current() == context, "accessor did not restore shared context");
        }
        finally
        {
            if (accessor != 0) accessorDestroy(accessor);
            if (profile != 0) profileDestroy(profile);
            if (context != 0) manualDestroy(context);
            int status = end(audit);
            nint restoredAlloc = 0, restoredFree = 0, restoredUser = 0;
            getAllocator(&restoredAlloc, &restoredFree, &restoredUser);
            Require(status == 0 && audit[4] == 0 && audit[5] == 0 && audit[6] == 1 && audit[0] > 0 && audit[0] == audit[1], "combined allocation interval failed");
            Require(restoredAlloc == alloc && restoredFree == free && restoredUser == user, "exact allocator callbacks/user-data restoration");
        }
        Require(current() == 0, "composition context not destroyed");
        return new { observedContext = (long)observedContext, observedAccessorContext = (long)observedAccessorContext, allocations = audit[0], frees = audit[1], outstanding = audit[4], restored = audit[6], conclusion = "shared actual contexts and callback-observed allocator; not CRT/STB/OS/global leak freedom" };
    }
}
