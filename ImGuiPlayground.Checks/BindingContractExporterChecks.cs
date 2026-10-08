using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace ImGuiPlayground.Checks;

internal static class BindingContractExporterChecks
{
    internal static unsafe void Run(string? selectedDirectory = null)
    {
        var exporter = new BindingContractExporter();
        JsonObject record = exporter.DescribeType(typeof(ExtendedLayout));
        Require(record["layout"]!["declaredSizeBytes"]!.GetValue<int>() == 4, "Declared size lost.");
        Require(record["measurement"]!["sizeBytes"]!.GetValue<int>() == 7, "Unsafe size must include field beyond declared extent.");
        Require(record["measurement"]!["arrayStrideBytes"]!.GetValue<long>() == 7, "Actual array stride lost.");
        Require(record["overlaps"]!.AsArray().Count == 1, "Overlapping fields must not be dropped.");
        JsonNode tail = record["fields"]!.AsArray().Single(field => field!["name"]!.GetValue<string>() == nameof(ExtendedLayout.Tail))!;
        Require(tail["runtimeOffset"]!["offsetBytes"]!.GetValue<long>() == 3, "Raw address offset differs.");
        Require(tail["storageWidth"]!["sizeBytes"]!.GetValue<int>() == 4, "Raw field width differs.");

        JsonObject boolRecord = exporter.DescribeType(typeof(RawBoolean));
        Require(boolRecord["measurement"]!["sizeBytes"]!.GetValue<int>() == 2, "Raw bool must not use Marshal.SizeOf.");
        Require(boolRecord["fields"]![1]!["runtimeOffset"]!["offsetBytes"]!.GetValue<long>() == 1, "Raw bool offset is wrong.");
        Require(boolRecord["marshalerMeasurement"]!["sizeBytes"]!.GetValue<int>() == 8, "Marshaler size must be distinct from raw bool storage.");
        Require(boolRecord["fields"]![1]!["marshalerOffset"]!["offsetBytes"]!.GetValue<long>() == 4, "Marshaler offset lost.");
        JsonObject generic = exporter.DescribeType(typeof(Container<int>));
        Require(generic["genericArguments"]![0]!.GetValue<string>() == BindingContractExporter.Id(typeof(int)), "Closed generic arguments lost.");
        Require(generic["fields"]!.AsArray().Count == 2, "Private container storage lost.");
        Require(exporter.DescribeType(typeof(Container<>))["measurement"]!["status"]!.GetValue<string>() == "unavailable", "Open generics must not get invented sizes.");
        Require(exporter.DescribeType(typeof(Span<int>))["measurement"]!["status"]!.GetValue<string>() == "unavailable", "Byref-like unavailable measurement must be explicit.");
        JsonObject fixedArray = exporter.DescribeType(typeof(FixedArray));
        Require(fixedArray["fields"]![0]!["fixedBuffer"]!["length"]!.GetValue<int>() == 7, "Fixed buffer length lost.");
        JsonObject inline = exporter.DescribeType(typeof(Inline));
        Require(inline["inlineArray"]!["length"]!.GetValue<int>() == 3, "Inline array length lost.");
        Require(inline["measurement"]!["sizeBytes"]!.GetValue<int>() == 6, "Inline array actual size differs.");
        JsonObject enumeration = exporter.DescribeType(typeof(WideEnum));
        Require(enumeration["enumValues"]![0]!["value"]!.GetValue<string>() == "18446744073709551615", "Enum precision/sign lost.");
        JsonObject callback = exporter.DescribeType(typeof(Callback));
        Require(callback["invoke"]!["return"]!["type"]!.GetValue<string>().EndsWith("::System.Int32", StringComparison.Ordinal), "Delegate return lost.");
        Require(callback["unmanagedFunctionPointer"] is not null, "Callback convention lost.");
        JsonObject functionPointer = exporter.DescribeType(typeof(delegate* unmanaged[Cdecl]<byte*, int>));
        Require(functionPointer["signature"]!["unmanaged"]!.GetValue<bool>(), "Function pointer unmanaged convention lost.");
        JsonObject modifiedPointer = exporter.SignatureShape(typeof(FunctionPointers).GetField(nameof(FunctionPointers.Callback))!.GetModifiedFieldType());
        Require(modifiedPointer["callingConventions"]!.AsArray().Single()!.GetValue<string>().EndsWith("CallConvCdecl", StringComparison.Ordinal), "Function pointer convention marker lost.");
        Require(exporter.DescribeType(typeof(int**))["pointerDepth"]!.GetValue<int>() == 2, "Pointer depth lost.");
        Require(exporter.DescribeType(typeof(int[,]))["rank"]!.GetValue<int>() == 2, "Array rank lost.");
        JsonObject refs = exporter.DescribeType(typeof(RefWrapper));
        Require(refs["refReturnProperties"]!.AsArray().Count == 1, "Ref-return property not exported.");
        Require(refs["refReturnProperties"]![0]!["rawStorageLink"]!["status"]!.GetValue<string>() == "unresolved", "Storage link must not be invented.");
        JsonObject signature = exporter.DescribeSignature(typeof(BindingContractExporterChecks).GetMethod(nameof(MarshallingFixture), BindingFlags.NonPublic | BindingFlags.Static)!);
        Require(signature["parameters"]![0]!["isOut"]!.GetValue<bool>(), "Out metadata lost.");
        Require(signature["return"]!["attributes"]!.AsArray().Count > 0, "Return marshalling lost.");
        Require(signature["parameters"]![1]!["hasDefaultValue"]!.GetValue<bool>(), "Default value metadata lost.");

        BindingContractExporter.ValidateImportMetadata("imgui", "Entry", true, "imgui", "Entry");
        Expect<InvalidDataException>(() => BindingContractExporter.ValidateImportMetadata("imgui", "Entry", true, "wrong", "Entry"), "Contradictory");
        Expect<InvalidDataException>(() => BindingContractExporter.ValidateImportMetadata("imgui", "Entry", true, "imgui", "wrong"), "Contradictory");
        Expect<InvalidDataException>(() => BindingContractExporter.ValidateImportMetadata("", "Entry", true, null, null), "empty");
        Expect<InvalidDataException>(() => BindingContractExporter.ValidateImportKeys(new[] { "imgui\0Entry", "imgui\0Entry" }), "duplicate");
        Expect<PlatformNotSupportedException>(() => BindingContractExporter.ValidateHost(Architecture.X86, 4, true, "win"), "64-bit");
        Expect<PlatformNotSupportedException>(() => BindingContractExporter.ValidateHost(Architecture.X64, 8, false, "linux"), "little-endian");
        Require(BindingContractExporter.ValidateHost(Architecture.X64, 8, true, "linux") == "linux-x64", "Supported host rejected.");

        string scratch = Path.Combine(Path.GetTempPath(), "binding-export-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        string output = Path.Combine(scratch, "result.json");
        string input = Path.Combine(scratch, "selected");
        Directory.CreateDirectory(input);
        try
        {
            CheckOutputBoundary(scratch);
            File.WriteAllText(output, "sentinel");
            Expect<FileNotFoundException>(() => BindingContractExporter.Run(input, output), "Brutal.ImGui.dll");
            Require(File.ReadAllText(output) == "sentinel", "Failed export replaced existing output.");
            Expect<ArgumentException>(() => BindingContractExporter.Run(input, Path.Combine(input, "unsafe.json")), "external assembly directory");
            Expect<ArgumentException>(() => BindingContractExporter.Run(input, Path.Combine(scratch, "wrong.txt")), ".json");
            Expect<DirectoryNotFoundException>(() => BindingContractExporter.Run(Path.Combine(scratch, "missing"), output), "does not exist");
            File.WriteAllText(Path.Combine(input, "Brutal.ImGui.dll"), "not an assembly");
            Expect<BadImageFormatException>(() => BindingContractExporter.Run(input, output), null);
            File.Copy(typeof(BindingContractExporterChecks).Assembly.Location, Path.Combine(input, "Brutal.ImGui.dll"), overwrite: true);
            Expect<InvalidDataException>(() => BindingContractExporter.Run(input, output), "not Brutal.ImGui");
            Require(File.ReadAllText(output) == "sentinel", "Malformed input replaced existing output.");
            if (selectedDirectory is not null) CheckSelectedAssembly(selectedDirectory, scratch, input, output);
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    private static void CheckOutputBoundary(string scratch)
    {
        string parent = Path.Combine(scratch, "boundary");
        string physical = Path.Combine(parent, "physical");
        string protectedDirectory = Path.Combine(physical, "selected");
        Directory.CreateDirectory(protectedDirectory);
        string sentinel = Path.Combine(protectedDirectory, "sentinel.json");
        File.WriteAllText(sentinel, "protected");
        void Reject(string input, string output, string message)
        {
            // No DLL is needed: the path gate must reject before metadata loading or writes.
            Expect<ArgumentException>(() => BindingContractExporter.Run(input, output), message);
            Require(File.ReadAllText(sentinel) == "protected", "Protected output was replaced.");
            Require(Directory.GetFileSystemEntries(protectedDirectory).SequenceEqual(new[] { sentinel }),
                "Path rejection created output or a temporary file in the protected directory.");
        }
        Reject(protectedDirectory + Path.DirectorySeparatorChar, sentinel, "external assembly directory");
        Reject(protectedDirectory + Path.DirectorySeparatorChar + Path.DirectorySeparatorChar, sentinel, "external assembly directory");
        string alias = Path.Combine(parent, "selected-link");
        if (!TryDirectoryLink(alias, protectedDirectory)) return; // Windows may require symlink privileges.
        Reject(protectedDirectory, Path.Combine(alias, "sentinel.json"), "external assembly directory");
        Reject(alias, sentinel, "external assembly directory");
        string ancestor = Path.Combine(parent, "ancestor-link");
        if (!TryDirectoryLink(ancestor, physical)) return;
        Reject(protectedDirectory, Path.Combine(ancestor, "selected", "new", "result.json"), "external assembly directory");
        Reject(Path.Combine(ancestor, "selected"), sentinel, "external assembly directory");
        string dangling = Path.Combine(parent, "dangling-link");
        if (!TryDirectoryLink(dangling, Path.Combine(parent, "missing"))) return;
        Reject(protectedDirectory, Path.Combine(dangling, "result.json"), "redirect");
        Require(!Directory.Exists(Path.Combine(parent, "missing")), "Rejected dangling redirect created its target.");
        string leaf = Path.Combine(parent, "leaf.json");
        File.CreateSymbolicLink(leaf, sentinel);
        Reject(protectedDirectory, leaf, "regular file");
    }

    private static bool TryDirectoryLink(string path, string target)
    {
        try { Directory.CreateSymbolicLink(path, target); return true; }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows()) { return false; }
        catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) == 1314) { return false; }
    }

    private static void CheckSelectedAssembly(string selected, string scratch, string missingDirectory, string output)
    {
        BindingContractExporter.Run(selected, output);
        string second = Path.Combine(scratch, "second.json");
        BindingContractExporter.Run(selected, second);
        byte[] firstBytes = File.ReadAllBytes(output);
        Require(firstBytes.AsSpan().SequenceEqual(File.ReadAllBytes(second)), "Two exports differ.");
        string safeAlias = Path.Combine(scratch, "safe-output-alias");
        string safeTarget = Path.Combine(scratch, "safe-output-target");
        Directory.CreateDirectory(safeTarget);
        if (TryDirectoryLink(safeAlias, safeTarget))
        {
            BindingContractExporter.Run(selected, Path.Combine(safeAlias, "contract.json"));
            Require(firstBytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(safeTarget, "contract.json"))),
                "Allowed physical output through a directory alias changed the contract.");
        }
        JsonObject contract = JsonNode.Parse(firstBytes)!.AsObject();
        Require(contract["schema"]!.GetValue<string>() == BindingContractExporter.Schema, "Wrong schema.");
        Require(contract["schemaVersion"]!.GetValue<int>() == BindingContractExporter.SchemaVersion, "Wrong schema version.");
        JsonArray types = contract["types"]!.AsArray();
        var ids = types.Select(type => type!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        Require(ids.Count == types.Count, "Duplicate type identities.");
        Require(contract["counts"]!["types"]!.GetValue<int>() == types.Count, "Type count differs.");
        Require(contract["counts"]!["imports"]!.GetValue<int>() == contract["imports"]!.AsArray().Count, "Import count differs.");
        void CheckReferences(JsonNode? node)
        {
            if (node is JsonObject obj) foreach (var member in obj) CheckReferences(member.Value);
            else if (node is JsonArray array) foreach (JsonNode? child in array) CheckReferences(child);
            else if (node is JsonValue value && value.TryGetValue<string>(out string? text) && text.Contains("::", StringComparison.Ordinal))
                Require(ids.Contains(text), "Dangling structural type reference: " + text);
        }
        CheckReferences(contract);
        Require(contract["dynamicExports"]!.AsArray().Count == 6, "Dynamic bridge declarations missing.");
        JsonNode identity = contract["assemblies"]!.AsArray().Single(assembly => assembly!["name"]!.GetValue<string>() == "Brutal.ImGui")!;
        using (var stream = File.OpenRead(Path.Combine(selected, "Brutal.ImGui.dll")))
            Require(identity["sha256"]!.GetValue<string>() == Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream)), "Selected assembly hash differs.");
        string text = System.Text.Encoding.UTF8.GetString(firstBytes);
        Require(!text.Contains(Path.GetFullPath(selected), StringComparison.Ordinal) && !text.Contains(scratch, StringComparison.Ordinal), "Machine paths leaked into contract.");

        File.Copy(Path.Combine(selected, "Brutal.ImGui.dll"), Path.Combine(missingDirectory, "Brutal.ImGui.dll"), overwrite: true);
        try { BindingContractExporter.Run(missingDirectory, output); throw new InvalidOperationException("Missing metadata dependency silently resolved."); }
        catch (ReflectionTypeLoadException error)
        {
            Require(error.LoaderExceptions.Any(exception => exception is FileNotFoundException missing &&
                (missing.FileName?.StartsWith("Brutal.Core.", StringComparison.Ordinal) ?? false)), "Missing dependency rejected at wrong gate.");
        }
        Require(firstBytes.AsSpan().SequenceEqual(File.ReadAllBytes(output)), "Missing dependency replaced output.");
        // A present file with the wrong assembly identity must not fall back to stale bin copies.
        File.Copy(Path.Combine(selected, "Brutal.Core.Numerics.dll"), Path.Combine(missingDirectory, "Brutal.Core.Common.dll"));
        bool IsIdentityError(Exception error) => error is InvalidDataException && error.Message.Contains("Selected dependency identity differs", StringComparison.Ordinal) ||
            error.InnerException is { } inner && IsIdentityError(inner) ||
            error is ReflectionTypeLoadException loader && loader.LoaderExceptions.OfType<Exception>().Any(IsIdentityError);
        try { BindingContractExporter.Run(missingDirectory, output); throw new InvalidOperationException("Wrong dependency identity accepted."); }
        catch (Exception error) when (IsIdentityError(error)) { }
        Require(firstBytes.AsSpan().SequenceEqual(File.ReadAllBytes(output)), "Wrong dependency identity replaced output.");
    }

    private static void Expect<T>(Action action, string? message) where T : Exception
    {
        try { action(); }
        catch (T error) when (message is null || error.Message.Contains(message, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Expected exact failure gate " + typeof(T).Name + ": " + message);
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [StructLayout(LayoutKind.Explicit, Size = 4)]
    private struct ExtendedLayout
    {
        [FieldOffset(0)] public uint First;
        [FieldOffset(3)] public uint Tail;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct RawBoolean { public bool Value; public byte Tail; }
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct Container<T> where T : unmanaged { private int count; private T* data; }
    private unsafe struct FixedArray { public fixed ushort Elements[7]; }
    [InlineArray(3)]
    private struct Inline { private ushort element; }
    private enum WideEnum : ulong { Maximum = ulong.MaxValue }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int Callback(byte* input);
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct FunctionPointers { public delegate* unmanaged[Cdecl]<byte*, int> Callback; }
    private unsafe struct RefWrapper { public ref int Value => ref *(int*)1; }
    [return: MarshalAs(UnmanagedType.I1)]
    private static bool MarshallingFixture([Out] out int value, int count = 7) { value = count; return true; }
}
