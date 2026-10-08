using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Brutal;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;
using Brutal.Numerics;

internal static unsafe class PrototypeAbiChecks
{
    private const string SourceCommit = "031a18c417158427217bc5890e0ec0cb7e7b4b63";
    private const string ProbeSchema = "playground_imgui.native-abi";
    private const string ExportSchema = "playground_imgui.brutal-subset";
    private const int ManagedImportCount = 1146;
    private const int MaximumManifestBytes = 1_048_576;
    private const int MaximumJsonBytes = 4_194_304;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    // Independent consumer contract, NOT derived from whichever fields the producer supplies.
    // Keep in sync with raw accesses in PlaygroundHost, GlfwInput, OpenGlRenderer and checks.
    // Extra font records retain the original probe's public-type checks, not bitfield parity.
    private static readonly Dictionary<string, string[]> RequiredProbeFields = new(StringComparer.Ordinal)
    {
        ["ImVec2"] = ["x", "y"],
        ["ImVec4"] = ["x", "y", "z", "w"],
        ["PlaygroundImVec2"] = ["x", "y"],
        ["PlaygroundImVec4"] = ["x", "y", "z", "w"],
        ["PlaygroundFloatRect"] = ["Min", "Max"],
        ["ImRect"] = ["Min", "Max"],
        ["ImVector<ImWchar>"] = ["Size", "Capacity", "Data"],
        ["ImVector<ImDrawVert>"] = ["Size", "Capacity", "Data"],
        ["ImVector<ImDrawIdx>"] = ["Size", "Capacity", "Data"],
        ["ImVector<ImDrawCmd>"] = ["Size", "Capacity", "Data"],
        ["ImVector<ImDrawList*>"] = ["Size", "Capacity", "Data"],
        ["ImVector<ImTextureData*>"] = ["Size", "Capacity", "Data"],
        ["ImTextureRef"] = ["_TexData", "_TexID"],
        ["ImDrawVert"] = ["pos", "uv", "col"],
        ["ImDrawList"] = ["CmdBuffer", "IdxBuffer", "VtxBuffer"],
        ["ImDrawCmd"] = ["ClipRect", "TexRef", "VtxOffset", "IdxOffset", "ElemCount", "UserCallback", "UserCallbackData"],
        ["ImDrawData"] = ["TotalIdxCount", "TotalVtxCount", "CmdLists", "DisplayPos", "DisplaySize", "FramebufferScale", "Textures"],
        ["ImTextureData"] = ["Status", "TexID", "Format", "Width", "Height", "Pixels", "UnusedFrames"],
        ["ImGuiIO"] = ["ConfigFlags", "BackendFlags", "DisplaySize", "DisplayFramebufferScale", "DeltaTime", "Fonts", "FontDefault", "InputQueueCharacters", "IniFilename", "LogFilename", "ConfigMacOSXBehaviors"],
        ["ImGuiStyle"] = ["FontSizeBase"],
        ["ImGuiPlatformIO"] = ["Platform_GetClipboardTextFn", "Platform_SetClipboardTextFn", "Renderer_TextureMaxWidth", "Renderer_TextureMaxHeight", "Textures"],
        ["ImFontConfig"] = ["Name", "FontData", "FontDataSize", "FontDataOwnedByAtlas", "EllipsisChar", "GlyphRanges", "GlyphOffset", "FontLoaderFlags", "FontLoader", "FontLoaderData"],
        ["ImFontBaked"] = ["IndexAdvanceX", "FallbackAdvanceX", "Size", "RasterizerDensity", "IndexLookup", "Glyphs", "FallbackGlyphIndex", "Ascent", "LastUsedFrame", "BakedId", "ContainerFont", "FontLoaderDatas"],
        ["ImFont"] = ["LastBaked", "ContainerAtlas", "Flags", "CurrentRasterizerDensity", "FontId", "LegacySize", "Sources", "EllipsisChar", "FallbackChar", "Used8kPagesMap", "EllipsisAutoBake", "RemapPairs"],
        ["ImFontAtlas"] = ["Flags", "TexDesiredFormat", "TexGlyphPadding", "UserData", "TexRef", "TexData", "TexList", "TexUvScale", "Fonts", "Sources", "Builder", "FontLoader", "RefCount", "OwnerContext"]
    };
    private static readonly string[] RequiredProbeTypes = RequiredProbeFields.Keys.ToArray();
    private static readonly HashSet<string> RiskyNativeTypes =
    [
        "ImRect", "ImFontGlyph", "ImFontBaked", "ImGuiBoxSelectState", "ImGuiDockNode",
        "ImGuiStackLevelInfo", "ImGuiContext"
    ];
    private static readonly Dictionary<string, string[]> ExpectedBitfields = new(StringComparer.Ordinal)
    {
        ["ImFontGlyph"] = ["Colored", "Visible", "SourceIdx", "Codepoint"],
        ["ImFontBaked"] = ["MetricsTotalSurface", "WantDestroy", "LoadNoFallback", "LoadNoRenderOnLayout"],
        ["ImGuiBoxSelectState"] = ["KeyMods"],
        ["ImGuiDockNode"] = ["AuthorityForPos", "AuthorityForSize", "AuthorityForViewport", "IsVisible", "IsFocused", "IsBgDrawnThisFrame", "HasCloseButton", "HasWindowMenuButton", "HasCentralNodeChild", "WantCloseAll", "WantLockSizeOnce", "WantMouseMove", "WantHiddenTabBarUpdate", "WantHiddenTabBarToggle"],
        ["ImGuiStackLevelInfo"] = ["DataType"]
    };

    private static nint s_callbackViewport;
    private static int s_callbackIndex;
    private static JsonObject? CurrentReport;
    private static string? s_reportPath;
    private static nint s_activeLibrary;

    internal static void Run(string libraryPath, string metadataDirectory, string reportPath, string targetRid, string expectedManagedHash)
    {
        var report = new JsonObject
        {
            ["schema"] = "playground_imgui.managed-abi-report",
            ["schemaVersion"] = 1,
            ["status"] = "running",
            ["fullBindingCompatible"] = false,
            ["compatibilityClaim"] = "bounded native prototype; not full Brutal.ImGui compatibility",
            ["negativeChecks"] = new JsonArray(),
            ["exercises"] = new JsonArray(),
            ["failures"] = new JsonArray()
        };
        nint library = 0;
        bool contextCreated = false;
        CurrentReport = report;
        s_reportPath = reportPath;
        ImGuiContextPtr context = default;
        try
        {
            targetRid = RequireTargetRid(targetRid);
            var metadata = LoadMetadata(metadataDirectory, libraryPath, targetRid, expectedManagedHash);
            var managedAssembly = typeof(ImGui).Assembly;
            if (!string.Equals(managedAssembly.GetName().Name, "Brutal.ImGui", StringComparison.Ordinal))
                throw new InvalidOperationException("The loaded managed assembly identity is not Brutal.ImGui.");
            string managedPath = Path.GetFullPath(managedAssembly.Location);
            string managedHash = Sha256(managedPath);
            RequireMatchingIdentity("staged Brutal.ImGui.dll", managedHash, metadata.ExpectedManagedHash);
            report["managedAssembly"] = new JsonObject
            {
                ["name"] = managedAssembly.GetName().Name,
                ["path"] = managedPath,
                ["sha256"] = managedHash,
                ["identityCheck"] = "matches exact staged Brutal.ImGui.dll hash passed by runner"
            };
            report["nativeArtifact"] = new JsonObject
            {
                ["path"] = Path.GetFullPath(libraryPath),
                ["filename"] = Path.GetFileName(libraryPath),
                ["sha256"] = metadata.ArtifactHash,
                ["targetRid"] = targetRid,
                ["sourceCommit"] = SourceCommit,
                ["configSha256"] = metadata.ConfigHash,
                ["exportManifestSha256"] = metadata.ExportsHash,
                ["sourceLockSha256"] = metadata.LockHash
            };

            using var exportsDocument = ParseStrictJson(metadata.ExportsText, "curated export manifest", MaximumJsonBytes);
            var imports = ReadManagedImports(managedAssembly);
            if (imports.Count != ManagedImportCount)
                throw new InvalidOperationException($"Managed binding import inventory identity mismatch: reflected {imports.Count}, expected {ManagedImportCount}. Refusing to infer compatibility from a different Brutal.ImGui assembly.");
            var exports = ParseExportManifest(exportsDocument.RootElement);
            var importNames = imports.Select(item => item.ExportName).ToHashSet(StringComparer.Ordinal);
            string[] unexplained = exports.ManagedImportNames.Where(name => !importNames.Contains(name)).ToArray();
            if (unexplained.Length != 0)
                throw new InvalidOperationException("Curated managed-import list does not match the reflected assembly: " + string.Join(", ", unexplained));
            report["bindingInventory"] = new JsonObject
            {
                ["managedImports"] = imports.Count,
                ["distinctManagedImportSymbols"] = importNames.Count,
                ["curatedNativeSubsetImports"] = exports.ManagedImports.Length,
                ["dynamicBridgeSymbols"] = exports.DataExports.Length + exports.DynamicGetters.Length,
                ["importMethods"] = new JsonArray(imports.Select(item => (JsonNode?)new JsonObject
                {
                    ["declaringType"] = item.DeclaringType,
                    ["method"] = item.MethodName,
                    ["symbol"] = item.ExportName
                }).ToArray())
            };

            library = NativeLibrary.Load(Path.GetFullPath(libraryPath));
            s_activeLibrary = library;
            var symbolPresence = CheckExports(library, exports);
            report["exportInventory"] = BuildExportReport(library, exports, imports, symbolPresence);
            RequireAllExports(exports.AllSymbols, name => symbolPresence[name]);
            var manifestFunction = (delegate* unmanaged[Cdecl]<byte*>)NativeLibrary.GetExport(library, "playground_imgui_probe_manifest_utf8");
            var manifestPointer = manifestFunction();
            string manifestText = ReadNativeUtf8(manifestPointer, "ABI manifest");
            report["nativeManifestSha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifestText))).ToLowerInvariant();
            metadata = metadata with { NativeManifestText = manifestText };
            JsonDocument manifestDoc = ParseStrictJson(manifestText, "native ABI manifest", MaximumManifestBytes);
            using (manifestDoc)
            {
                var probe = ValidateProbe(manifestDoc.RootElement, targetRid);
                CheckProbeIdentityFunctions(library, targetRid);
                report["nativeConfiguration"] = new JsonObject
                {
                    ["version"] = probe.Version,
                    ["sourceCommit"] = probe.SourceCommit,
                    ["targetRid"] = probe.TargetRid,
                    ["IMGUI_USE_WCHAR32"] = probe.Wchar32,
                    ["IMGUI_DISABLE_OBSOLETE_FUNCTIONS"] = probe.DisableObsolete,
                    ["ImDrawIdxBytes"] = probe.DrawIndexBytes,
                    ["ImWcharBytes"] = probe.WcharBytes,
                    ["ImTextureIDBytes"] = probe.TextureIdBytes,
                    ["sizeTBytes"] = probe.SizeTBytes
                };

                var comparison = CompareLayouts(probe);
                report["layoutComparison"] = comparison.Report;
                RequireSafeHostLayoutGate(comparison);
                report["safeHostPublicSubsetGate"] = new JsonObject
                {
                    ["passed"] = true,
                    ["scope"] = "independent required host/checks field inventory, including draw-list and concrete vector headers; all supplied fields on required types also compared",
                    ["requiredFields"] = new JsonObject(RequiredProbeFields.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, StringArray(pair.Value)))),
                    ["notFullParity"] = true,
                    ["requiredTypes"] = StringArray(RequiredProbeTypes)
                };

                RunNegativeChecks(metadata, targetRid, managedHash, exports, symbolPresence, comparison);
                InstallResolver(managedAssembly, library);
                CheckManagedVersionAndExistingLayoutGuard();
                ExerciseManagedAbi(library, ref context, ref contextCreated, report);
            }
            report["status"] = "passed";
            report["partialCompatibility"] = new JsonObject
            {
                ["safeHostPublicSubset"] = true,
                ["fullBinding"] = false,
                ["missingManagedImportCount"] = CountMissingImports(library, imports),
                ["fullParityProven"] = false,
                ["internalRawLayoutsAndBitfields"] = "reported but not proven",
                ["foreignRuntimeExecution"] = "not implied by a cross-build"
            };
        }
        catch (Exception error)
        {
            report["status"] = "failed";
            ((JsonArray)report["failures"]!).Add(error.ToString());
            throw;
        }
        finally
        {
            Exception? teardownFailure = null;
            if (contextCreated)
            {
                try { ImGui.DestroyContext(context); }
                catch (Exception error)
                {
                    report["status"] = "failed";
                    ((JsonArray)report["failures"]!).Add("Context teardown failed: " + error);
                    teardownFailure = error;
                }
            }
            if (library != 0) NativeLibrary.Free(library);
            s_activeLibrary = 0;
            WriteReport(reportPath, report);
            CurrentReport = null;
            s_reportPath = null;
            if (teardownFailure is not null)
                throw new InvalidOperationException("Managed ABI probe context teardown failed.", teardownFailure);
        }
    }

    private static Metadata LoadMetadata(string metadataDirectory, string libraryPath, string rid, string expectedManagedHash)
    {
        string directory = Path.GetFullPath(metadataDirectory);
        if (!Directory.Exists(directory) || new DirectoryInfo(directory).LinkTarget is not null)
            throw new InvalidOperationException("Prototype metadata directory must be an existing non-symlink directory.");
        string exportsPath = SafeMetadataFile(directory, "exports.json");
        string lockPath = SafeMetadataFile(directory, "source.lock.json");
        string buildPath = SafeMetadataFile(directory, "build.json");
        string configPath = SafeMetadataFile(directory, "playground_imgui_config.h");
        string exportsText = ReadBounded(exportsPath, MaximumJsonBytes);
        string lockText = ReadBounded(lockPath, MaximumJsonBytes);
        string buildText = ReadBounded(buildPath, MaximumJsonBytes);
        string exportsHash = HashText(exportsText);
        string lockHash = HashText(lockText);
        var exportsDoc = ParseStrictJson(exportsText, "curated export manifest", MaximumJsonBytes);
        var lockDoc = ParseStrictJson(lockText, "native source lock", MaximumJsonBytes);
        var buildDoc = ParseStrictJson(buildText, "native build record", MaximumJsonBytes);
        using (exportsDoc)
        using (lockDoc)
        using (buildDoc)
        {
            JsonElement exports = exportsDoc.RootElement;
            JsonElement sourceLock = lockDoc.RootElement;
            JsonElement build = buildDoc.RootElement;
            RequireObject(exports, "export manifest");
            RequireObject(sourceLock, "source lock");
            RequireObject(build, "native build record");
            if (String(exports, "schema", "export manifest") != ExportSchema || Integer(exports, "schemaVersion", "export manifest") != 1)
                throw new InvalidOperationException("Unsupported curated export manifest schema/version.");
            if (String(sourceLock, "schema", "source lock") != "playground_imgui.native-source-lock" || Integer(sourceLock, "schemaVersion", "source lock") != 1)
                throw new InvalidOperationException("Unsupported native source-lock schema/version.");
            var upstream = Property(sourceLock, "upstream", "source lock");
            var configuration = Property(sourceLock, "configuration", "source lock");
            if (String(upstream, "sourceCommit", "source lock upstream") != SourceCommit ||
                String(upstream, "tag", "source lock upstream") != "v1.92.2-docking" ||
                String(upstream, "tagObject", "source lock upstream") != "04c3466d23a72abee3696dcba698b0e02fee6057")
                throw new InvalidOperationException("Native source lock does not pin the authorized vanilla source tag/commit identity.");
            if (String(configuration, "header", "source lock configuration") != "include/playground_imgui_config.h")
                throw new InvalidOperationException("Native source lock config-header path changed unexpectedly.");
            string expectedConfigHash = String(configuration, "sha256", "source lock configuration");
            string actualConfigHash = Sha256(configPath);
            if (!FixedHashEquals(expectedConfigHash, actualConfigHash))
                throw new InvalidOperationException("Native config header hash differs from the source-lock pin.");
            string expectedSourceArchiveHash = String(upstream, "archiveSha256", "source lock upstream");
            string expectedFilename = rid switch
            {
                "osx-arm64" => "libimgui.dylib",
                "linux-x64" => "libimgui.so",
                "win-x64" => "imgui.dll",
                _ => throw new InvalidOperationException($"Unsupported prototype target RID '{rid}'.")
            };
            var toolchain = Property(sourceLock, "toolchain", "source lock");
            if (String(toolchain, "zigVersion", "source lock toolchain") != "0.17.0" ||
                String(build, "zigVersion", "native build record") != "0.17.0")
                throw new InvalidOperationException("Native artifact does not record the pinned Zig 0.17.0 prototype toolchain.");
            var targets = Property(sourceLock, "targets", "source lock");
            var target = Property(targets, rid, "source lock targets");
            if (String(target, "file", $"source lock target {rid}") != expectedFilename ||
                String(build, "zigTarget", "native build record") != String(target, "zigTarget", $"source lock target {rid}"))
                throw new InvalidOperationException("Native target triple/file differs from the frozen source lock.");
            if (String(build, "schema", "native build record") != "playground_imgui.native-build-record" || Integer(build, "schemaVersion", "native build record") != 1)
                throw new InvalidOperationException("Unsupported native build-record schema/version.");
            if (String(build, "rid", "native build record") != rid || String(build, "artifact", "native build record") != expectedFilename)
                throw new InvalidOperationException("Native artifact/build-record target identity mismatch.");
            if (String(build, "sourceCommit", "native build record") != SourceCommit || String(build, "sourceArchiveSha256", "native build record") != expectedSourceArchiveHash || String(build, "configSha256", "native build record") != expectedConfigHash)
                throw new InvalidOperationException("Native build record does not match the authorized source/config lock.");
            if (!Path.GetFileName(libraryPath).Equals(expectedFilename, StringComparison.Ordinal))
                throw new InvalidOperationException($"Expected {expectedFilename} for {rid}; refusing an ambiguous native artifact name.");
            if (!File.Exists(libraryPath) || new FileInfo(libraryPath).LinkTarget is not null)
                throw new InvalidOperationException("Native artifact must be a regular non-symlink file in the disposable stage.");
            string artifactHash = Sha256(libraryPath);
            string expectedArtifactHash = String(build, "sha256", "native build record");
            RequireMatchingIdentity("native artifact", artifactHash, expectedArtifactHash);
            long bytes = new FileInfo(libraryPath).Length;
            if (Integer64(build, "bytes", "native build record") != bytes)
                throw new InvalidOperationException("Native artifact length differs from its frozen build record.");
            if (!IsHash(expectedManagedHash))
                throw new InvalidOperationException("Expected staged Brutal.ImGui assembly SHA-256 is malformed.");
            return new Metadata(exportsText, exportsHash, lockHash, artifactHash, expectedConfigHash, actualConfigHash, expectedManagedHash);
        }
    }

    private static string SafeMetadataFile(string directory, string filename)
    {
        string path = Path.Combine(directory, filename);
        if (!File.Exists(path) || new FileInfo(path).LinkTarget is not null)
            throw new InvalidOperationException($"Required prototype metadata file is missing or a symlink: {filename}.");
        return path;
    }

    private static string RequireTargetRid(string rid)
    {
        string hostRid = (OperatingSystem.IsMacOS(), OperatingSystem.IsLinux(), OperatingSystem.IsWindows(), RuntimeInformation.ProcessArchitecture) switch
        {
            (true, _, _, Architecture.Arm64) => "osx-arm64",
            (_, true, _, Architecture.X64) => "linux-x64",
            (_, _, true, Architecture.X64) => "win-x64",
            _ => throw new PlatformNotSupportedException("The managed ABI prototype runs only on native macOS arm64, Linux x64, or Windows x64 hosts.")
        };
        if (!string.Equals(hostRid, rid, StringComparison.Ordinal))
            throw new InvalidOperationException($"Refusing target '{rid}' on host '{hostRid}': foreign native artifacts are not executed. Run this explicit verifier on the target OS/architecture.");
        return rid;
    }

    private static ExportSet ParseExportManifest(JsonElement root)
    {
        RequireObject(root, "curated export manifest");
        var upstream = Property(root, "upstream", "curated export manifest");
        if (String(upstream, "sourceCommit", "curated export manifest upstream") != SourceCommit)
            throw new InvalidOperationException("Curated export manifest source commit does not match the authorized source lock.");
        var managedImports = ReadExportEntries(root, "managedImports", "managed-import");
        var dataExports = ReadExportEntries(root, "dataExports", "data-export");
        var extraFunctions = ReadExportEntries(root, "extraFunctions", "helper-function");
        var all = managedImports.Concat(dataExports).Concat(extraFunctions).ToArray();
        if (all.Length != 72 || all.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != all.Length)
            throw new InvalidOperationException($"Curated export manifest must describe 72 unique symbols; found {all.Length}.");
        if (managedImports.Length != 54 || dataExports.Length != 3 || extraFunctions.Length != 15)
            throw new InvalidOperationException("Curated native subset inventory count changed; review the protocol before use.");
        var uncoveredElement = Property(root, "intentionallyNotCovered", "curated export manifest");
        if (uncoveredElement.ValueKind != JsonValueKind.Array || uncoveredElement.GetArrayLength() == 0)
            throw new InvalidOperationException("Curated export manifest must state its unsupported surface.");
        var uncovered = uncoveredElement.EnumerateArray().Select(item => new UnsupportedEntry(
            String(item, "name", "intentionally-not-covered entry"),
            String(item, "reason", "intentionally-not-covered entry"))).ToArray();
        return new ExportSet(managedImports, dataExports, extraFunctions, all, uncovered);
    }

    private static ExportDescription[] ReadExportEntries(JsonElement root, string propertyName, string category)
    {
        var array = Property(root, propertyName, "curated export manifest");
        if (array.ValueKind != JsonValueKind.Array) throw new InvalidOperationException($"Export manifest '{propertyName}' must be an array.");
        var result = new List<ExportDescription>();
        foreach (var item in array.EnumerateArray())
        {
            string name = String(item, "name", propertyName + " entry");
            string signature = category == "data-export"
                ? "data: " + String(item, "type", propertyName + " entry")
                : String(item, "signature", propertyName + " entry");
            string purpose = category switch
            {
                "managed-import" => String(item, "covers", propertyName + " entry"),
                "data-export" => String(item, "purpose", propertyName + " entry"),
                _ => item.TryGetProperty("purpose", out var detail) && detail.ValueKind == JsonValueKind.String
                    ? detail.GetString()!
                    : "additional prototype adapter/probe helper"
            };
            result.Add(new ExportDescription(name, category, signature, purpose));
        }
        if (result.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw new InvalidOperationException($"Export manifest '{propertyName}' contains duplicate names.");
        return result.ToArray();
    }

    private static List<ManagedImport> ReadManagedImports(Assembly assembly)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException error)
        {
            throw new InvalidOperationException("Could not reflect the complete Brutal.ImGui assembly type set; import inventory is incomplete.", error);
        }
        var imports = new List<ManagedImport>();
        foreach (var type in types)
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var dll = method.GetCustomAttribute<DllImportAttribute>();
            var library = method.GetCustomAttribute<LibraryImportAttribute>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (dll?.Value == "imgui") names.Add(dll.EntryPoint ?? method.Name);
            if (library?.LibraryName == "imgui") names.Add(library.EntryPoint ?? method.Name);
            if (names.Count != 0)
            {
                string signature = $"{FormatManagedType(method.ReturnType)}({string.Join(", ", method.GetParameters().Select(FormatManagedParameter))})";
                foreach (string name in names)
                    imports.Add(new ManagedImport(type.FullName ?? type.Name, method.Name, name, signature));
            }
        }
        return imports;
    }

    private static string FormatManagedParameter(ParameterInfo parameter)
    {
        string prefix = parameter.IsOut ? "out " : parameter.IsIn ? "in " : string.Empty;
        return prefix + FormatManagedType(parameter.ParameterType);
    }

    private static string FormatManagedType(Type type)
    {
        if (type.IsByRef) return FormatManagedType(type.GetElementType()!) + "&";
        if (type.IsPointer) return FormatManagedType(type.GetElementType()!) + "*";
        if (type.IsArray) return FormatManagedType(type.GetElementType()!) + "[]";
        if (type.IsGenericType)
        {
            string name = type.GetGenericTypeDefinition().FullName ?? type.Name;
            int tick = name.IndexOf('`');
            if (tick >= 0) name = name[..tick];
            return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FormatManagedType))}>";
        }
        return type.FullName ?? type.Name;
    }

    private static Dictionary<string, bool> CheckExports(nint library, ExportSet exports)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (string name in exports.AllSymbols)
            result[name] = NativeLibrary.TryGetExport(library, name, out _);
        return result;
    }

    private static JsonObject BuildExportReport(nint library, ExportSet exports, List<ManagedImport> imports, Dictionary<string, bool> declared)
    {
        var inventory = new JsonArray();
        foreach (var import in imports)
        {
            bool found = NativeLibrary.TryGetExport(library, import.ExportName, out _);
            inventory.Add(new JsonObject
            {
                ["declaringType"] = import.DeclaringType,
                ["method"] = import.MethodName,
                ["managedSignature"] = import.ManagedSignature,
                ["symbol"] = import.ExportName,
                ["nativeExportPresent"] = found
            });
        }
        var prototypeExports = new JsonArray();
        foreach (var item in exports.AllExports)
        {
            prototypeExports.Add(new JsonObject
            {
                ["name"] = item.Name,
                ["category"] = item.Category,
                ["signature"] = item.Signature,
                ["purpose"] = item.Purpose,
                ["resolved"] = declared.GetValueOrDefault(item.Name)
            });
        }
        var intentionallyNotCovered = new JsonArray();
        foreach (var item in exports.IntentionallyNotCovered)
            intentionallyNotCovered.Add(new JsonObject { ["name"] = item.Name, ["reason"] = item.Reason });
        return new JsonObject
        {
            ["declaredPrototypeSymbols"] = declared.Count,
            ["prototypeExports"] = prototypeExports,
            ["intentionallyNotCovered"] = intentionallyNotCovered,
            ["declaredPrototypeSymbolsResolved"] = declared.Count(pair => pair.Value),
            ["managedImportMethods"] = imports.Count,
            ["managedImportSymbolsPresent"] = imports.Count(import => NativeLibrary.TryGetExport(library, import.ExportName, out _)),
            ["managedImportSymbolsMissing"] = imports.Count(import => !NativeLibrary.TryGetExport(library, import.ExportName, out _)),
            ["managedImports"] = inventory,
            ["dynamicBridge"] = new JsonObject
            {
                ["callbackSlots"] = StringArray(exports.DataExportNames),
                ["nativeGetterFunctions"] = StringArray(exports.DynamicGetters),
                ["allSixResolved"] = exports.DataExportNames.Concat(exports.DynamicGetters).All(name => declared.GetValueOrDefault(name))
            }
        };
    }

    private static ProbeData ValidateProbe(JsonElement root, string rid)
    {
        RequireObject(root, "native ABI probe");
        RequireExactKeys(root, "native ABI probe", "schema", "schemaVersion", "upstream", "targetRid", "configuration", "scalars", "types", "bitFieldsNotIndividuallyAddressable");
        if (String(root, "schema", "native ABI probe") != ProbeSchema || Integer(root, "schemaVersion", "native ABI probe") != 1)
            throw new InvalidOperationException("Native probe schema/version is unsupported; refusing any context or field access.");
        var upstream = Property(root, "upstream", "native ABI probe");
        RequireExactKeys(upstream, "native ABI probe upstream", "version", "sourceCommit");
        string version = String(upstream, "version", "native ABI upstream");
        string sourceCommit = String(upstream, "sourceCommit", "native ABI upstream");
        if (version != "1.92.2") throw new InvalidOperationException($"Native ImGui version '{version}' is not the pinned 1.92.2.");
        if (sourceCommit != SourceCommit) throw new InvalidOperationException("Native probe source-commit mismatch.");
        string reportedRid = String(root, "targetRid", "native ABI probe");
        if (!string.Equals(reportedRid, rid, StringComparison.Ordinal)) throw new InvalidOperationException("Native probe target RID does not match the staged artifact/host.");
        var config = Property(root, "configuration", "native ABI probe");
        RequireExactKeys(config, "native ABI configuration", "IMGUI_USE_WCHAR32", "IMGUI_DISABLE_OBSOLETE_FUNCTIONS", "ImDrawIdxBytes");
        bool wchar32 = Boolean(config, "IMGUI_USE_WCHAR32", "native ABI configuration");
        bool disableObsolete = Boolean(config, "IMGUI_DISABLE_OBSOLETE_FUNCTIONS", "native ABI configuration");
        int drawIndex = Integer(config, "ImDrawIdxBytes", "native ABI configuration");
        if (!wchar32 || !disableObsolete || drawIndex != 2)
            throw new InvalidOperationException("Native config hypothesis mismatch (required WCHAR32, obsolete API disabled, 16-bit ImDrawIdx); no native context or field access is permitted.");
        var scalars = Property(root, "scalars", "native ABI probe");
        RequireExactKeys(scalars, "native ABI scalars", "boolBytes", "ImWcharBytes", "ImDrawIdxBytes", "ImTextureIDBytes", "sizeTBytes");
        int boolBytes = Integer(scalars, "boolBytes", "native ABI scalars");
        int wcharBytes = Integer(scalars, "ImWcharBytes", "native ABI scalars");
        int scalarDrawIndex = Integer(scalars, "ImDrawIdxBytes", "native ABI scalars");
        int textureIdBytes = Integer(scalars, "ImTextureIDBytes", "native ABI scalars");
        int sizeTBytes = Integer(scalars, "sizeTBytes", "native ABI scalars");
        if (boolBytes != 1 || wcharBytes != 4 || scalarDrawIndex != drawIndex || textureIdBytes != 8 || sizeTBytes != IntPtr.Size)
            throw new InvalidOperationException("Native scalar configuration does not match the managed host contract.");
        var typesElement = Property(root, "types", "native ABI probe");
        if (typesElement.ValueKind != JsonValueKind.Array || typesElement.GetArrayLength() > 128)
            throw new InvalidOperationException("Native ABI types must be a bounded array.");
        var types = new Dictionary<string, NativeType>(StringComparer.Ordinal);
        foreach (var item in typesElement.EnumerateArray())
        {
            RequireExactKeys(item, "native ABI type", "name", "sizeBytes", "alignmentBytes", "fields");
            string name = String(item, "name", "native ABI type");
            int size = Integer(item, "sizeBytes", "native ABI type");
            int alignment = Integer(item, "alignmentBytes", "native ABI type");
            if (size <= 0 || size > 1_048_576 || alignment <= 0 || alignment > 4096 || (alignment & (alignment - 1)) != 0 || size % alignment != 0)
                throw new InvalidOperationException($"Invalid or unsafe native layout size/alignment for {name}.");
            var fieldsElement = Property(item, "fields", "native ABI type");
            if (fieldsElement.ValueKind != JsonValueKind.Array || fieldsElement.GetArrayLength() > 1024)
                throw new InvalidOperationException($"Invalid native field array for {name}.");
            var fields = new Dictionary<string, NativeField>(StringComparer.Ordinal);
            foreach (var fieldElement in fieldsElement.EnumerateArray())
            {
                RequireExactKeys(fieldElement, $"native field in {name}", "name", "offsetBytes", "sizeBytes", "alignmentBytes");
                string fieldName = String(fieldElement, "name", $"native field in {name}");
                int offset = Integer(fieldElement, "offsetBytes", $"native field {name}.{fieldName}");
                int width = Integer(fieldElement, "sizeBytes", $"native field {name}.{fieldName}");
                int fieldAlignment = Integer(fieldElement, "alignmentBytes", $"native field {name}.{fieldName}");
                if (offset < 0 || width <= 0 || fieldAlignment <= 0 || fieldAlignment > alignment || (fieldAlignment & (fieldAlignment - 1)) != 0 ||
                    offset % fieldAlignment != 0 || offset > size - width)
                    throw new InvalidOperationException($"Native field {name}.{fieldName} has invalid alignment or lies outside its measured parent layout.");
                if (!fields.TryAdd(fieldName, new NativeField(fieldName, offset, width, fieldAlignment)))
                    throw new InvalidOperationException($"Duplicate native field record {name}.{fieldName}.");
            }
            if (!types.TryAdd(name, new NativeType(name, size, alignment, fields)))
                throw new InvalidOperationException($"Duplicate native type record {name}.");
        }
        foreach (var (required, fields) in RequiredProbeFields)
        {
            if (!types.TryGetValue(required, out var type)) throw new InvalidOperationException($"Native probe omits required safe-host layout '{required}'.");
            foreach (string field in fields)
                if (!type.Fields.ContainsKey(field)) throw new InvalidOperationException($"Native probe omits required safe-host field '{required}.{field}'.");
        }
        var bitfields = Property(root, "bitFieldsNotIndividuallyAddressable", "native ABI probe");
        if (bitfields.ValueKind != JsonValueKind.Array || bitfields.GetArrayLength() > 128)
            throw new InvalidOperationException("Native bitfield coverage report is missing or unbounded.");
        var seenBitFields = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var item in bitfields.EnumerateArray())
        {
            RequireExactKeys(item, "native bitfield record", "type", "fields");
            string name = String(item, "type", "native bitfield record");
            var fields = Property(item, "fields", "native bitfield record");
            if (!types.ContainsKey(name) || fields.ValueKind != JsonValueKind.Array || fields.GetArrayLength() == 0 ||
                !ExpectedBitfields.ContainsKey(name) || seenBitFields.ContainsKey(name))
                throw new InvalidOperationException($"Invalid, unexpected, or duplicate bitfield coverage entry for {name}.");
            var observed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in fields.EnumerateArray())
            {
                if (field.ValueKind != JsonValueKind.String || !observed.Add(field.GetString()!))
                    throw new InvalidOperationException("Invalid or duplicate native bitfield coverage entry.");
            }
            seenBitFields.Add(name, observed);
        }
        if (seenBitFields.Count != ExpectedBitfields.Count || ExpectedBitfields.Any(pair =>
                !seenBitFields.TryGetValue(pair.Key, out var fields) || !fields.SetEquals(pair.Value)))
            throw new InvalidOperationException("Native bitfield coverage list is incomplete or differs from the pinned schema; refusing to imply raw-bitfield parity.");
        return new ProbeData(version, sourceCommit, reportedRid, wchar32, disableObsolete, drawIndex, boolBytes, wcharBytes, textureIdBytes, sizeTBytes, types, bitfields.Clone());
    }

    private static LayoutComparison CompareLayouts(ProbeData probe)
    {
        var managedTypes = typeof(ImGui).Assembly.GetTypes().Concat(typeof(float2).Assembly.GetTypes())
            .GroupBy(type => type.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var records = new JsonArray();
        var gateFailures = new List<string>();
        int mappedTypes = 0, sizeMatchedTypes = 0, measuredFieldCount = 0, comparedFieldCount = 0, matchingFieldCount = 0;
        foreach (var native in probe.Types.Values.OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            Type? managed = ResolveManagedType(native.Name, managedTypes);
            bool required = RequiredProbeTypes.Contains(native.Name, StringComparer.Ordinal);
            bool risky = RiskyNativeTypes.Contains(native.Name);
            if (managed is null)
            {
                string failure = $"{native.Name}: no corresponding reflected managed value type";
                if (required) gateFailures.Add(failure);
                records.Add(new JsonObject
                {
                    ["nativeType"] = native.Name,
                    ["managedType"] = null,
                    ["classification"] = risky ? "risky-internal" : "public-or-adapter",
                    ["requiredSafeHostLayout"] = required,
                    ["comparison"] = "unmapped",
                    ["failure"] = failure,
                    ["nativeSizeBytes"] = native.Size,
                    ["nativeAlignmentBytes"] = native.Alignment,
                    ["managedAlignment"] = "not exposed by managed reflection"
                });
                continue;
            }
            ++mappedTypes;
            var fields = new JsonArray();
            int? managedSize = null;
            string? typeError = null;
            try { managedSize = UnsafeSizeOf(managed); }
            catch (Exception error) { typeError = error.Message; }
            bool sizeMatches = managedSize == native.Size;
            if (sizeMatches) ++sizeMatchedTypes;
            if (required && !sizeMatches) gateFailures.Add($"{native.Name}: native size {native.Size}, reflected managed size {managedSize?.ToString() ?? "unavailable"}");
            foreach (var nativeField in native.Fields.Values.OrderBy(field => field.Offset))
            {
                ++measuredFieldCount;
                FieldInfo? field = FindInstanceField(managed, nativeField.Name);
                if (field is null)
                {
                    string reason = $"Native field {native.Name}.{nativeField.Name} not found on reflected {managed.FullName}.";
                    if (required) gateFailures.Add(reason);
                    fields.Add(new JsonObject
                    {
                        ["nativeName"] = nativeField.Name,
                        ["nativeOffsetBytes"] = nativeField.Offset,
                        ["nativeSizeBytes"] = nativeField.Width,
                        ["managedField"] = null,
                        ["status"] = "unmapped"
                    });
                    continue;
                }
                try
                {
                    int managedOffset = checked((int)Marshal.OffsetOf(managed, field.Name));
                    int managedWidth = FieldSize(field);
                    bool offsetMatches = managedOffset == nativeField.Offset;
                    bool widthMatches = managedWidth == nativeField.Width;
                    bool match = offsetMatches && widthMatches;
                    ++comparedFieldCount;
                    if (match) ++matchingFieldCount;
                    if (required && !match)
                        gateFailures.Add($"{native.Name}.{nativeField.Name}: native offset/width {nativeField.Offset}/{nativeField.Width}, managed {managedOffset}/{managedWidth}");
                    fields.Add(new JsonObject
                    {
                        ["nativeName"] = nativeField.Name,
                        ["managedName"] = field.Name,
                        ["nativeOffsetBytes"] = nativeField.Offset,
                        ["managedOffsetBytes"] = managedOffset,
                        ["nativeSizeBytes"] = nativeField.Width,
                        ["managedSizeBytes"] = managedWidth,
                        ["nativeAlignmentBytes"] = nativeField.Alignment,
                        ["status"] = match ? "match" : "mismatch"
                    });
                }
                catch (Exception error)
                {
                    if (required) gateFailures.Add($"{native.Name}.{nativeField.Name}: managed offset/width unavailable ({error.Message})");
                    fields.Add(new JsonObject
                    {
                        ["nativeName"] = nativeField.Name,
                        ["managedName"] = field.Name,
                        ["nativeOffsetBytes"] = nativeField.Offset,
                        ["nativeSizeBytes"] = nativeField.Width,
                        ["status"] = "unmeasurable",
                        ["error"] = error.Message
                    });
                }
            }
            records.Add(new JsonObject
            {
                ["nativeType"] = native.Name,
                ["managedType"] = managed.FullName,
                ["managedVisibility"] = managed.IsPublic || managed.IsNestedPublic ? "public" : "non-public",
                ["classification"] = risky ? "risky-internal-or-bitfield-bearing" : "public-or-adapter",
                ["requiredSafeHostLayout"] = required,
                ["nativeSizeBytes"] = native.Size,
                ["managedSizeBytes"] = managedSize,
                ["nativeAlignmentBytes"] = native.Alignment,
                ["managedAlignment"] = "not exposed by managed reflection",
                ["sizeMatches"] = sizeMatches,
                ["typeMeasurementError"] = typeError,
                ["measuredFieldCount"] = native.Fields.Count,
                ["fields"] = fields
            });
        }

        var bitfieldsReport = BuildBitfieldReport(probe, managedTypes);
        var internalReport = new JsonArray();
        foreach (string name in RiskyNativeTypes.OrderBy(name => name, StringComparer.Ordinal))
        {
            var native = probe.Types.GetValueOrDefault(name);
            if (native is null) continue;
            Type? managed = ResolveManagedType(name, managedTypes);
            internalReport.Add(new JsonObject
            {
                ["nativeType"] = name,
                ["managedType"] = managed?.FullName,
                ["nativeSizeBytes"] = native.Size,
                ["managedSizeBytes"] = managed is null ? null : UnsafeSizeOf(managed),
                ["risk"] = "internal/raw layout is observational; no complete parity is inferred from sampled fields"
            });
        }
        var result = new JsonObject
        {
            ["nativeTypeRecords"] = probe.Types.Count,
            ["mappedManagedTypes"] = mappedTypes,
            ["managedTypeSizesMatching"] = sizeMatchedTypes,
            ["managedTypeSizeMismatches"] = mappedTypes - sizeMatchedTypes,
            ["measuredNativeFields"] = measuredFieldCount,
            ["comparableManagedFields"] = comparedFieldCount,
            ["matchingMeasuredFields"] = matchingFieldCount,
            ["fieldParityFraction"] = comparedFieldCount == 0 ? 0 : (double)matchingFieldCount / comparedFieldCount,
            ["alignmentComparison"] = "native alignment is measured, CLR reflection exposes no portable struct alignment query; never inferred from sample offsets",
            ["sampleOffsetsAreNotFullLayoutParity"] = true,
            ["requiredGatePassed"] = gateFailures.Count == 0,
            ["requiredGateFailures"] = StringArray(gateFailures),
            ["types"] = records,
            ["riskyInternalLayouts"] = internalReport,
            ["bitfieldCoverage"] = bitfieldsReport
        };
        return new LayoutComparison(result, gateFailures.Count == 0, gateFailures.ToArray());
    }

    private static JsonArray BuildBitfieldReport(ProbeData probe, Dictionary<string, Type[]> managedTypes)
    {
        var output = new JsonArray();
        foreach (var item in probe.Bitfields.EnumerateArray())
        {
            string typeName = String(item, "type", "native bitfield record");
            Type? managed = ResolveManagedType(typeName, managedTypes);
            var fields = new JsonArray();
            foreach (var fieldElement in Property(item, "fields", "native bitfield record").EnumerateArray())
            {
                string name = fieldElement.GetString()!;
                FieldInfo? managedField = managed is null ? null : FindInstanceField(managed, name);
                object? offset = null, width = null, fieldType = null;
                if (managedField is not null)
                {
                    fieldType = managedField.FieldType.FullName;
                    try { offset = checked((int)Marshal.OffsetOf(managed!, managedField.Name)); width = FieldSize(managedField); }
                    catch { /* The report records a coverage gap; it is never promoted to proof. */ }
                }
                fields.Add(new JsonObject
                {
                    ["name"] = name,
                    ["managedFieldFound"] = managedField is not null,
                    ["managedFieldType"] = fieldType as string,
                    ["managedOffsetBytes"] = offset as int?,
                    ["managedWidthBytes"] = width as int?,
                    ["nativeOffsetAndWidth"] = "not individually measurable with standard native offsetof/sizeof for bitfields",
                    ["status"] = "native-bitfield-parity-unproven"
                });
            }
            output.Add(new JsonObject
            {
                ["nativeType"] = typeName,
                ["managedType"] = managed?.FullName,
                ["fields"] = fields,
                ["status"] = "explicit coverage gap; do not infer parity"
            });
        }
        return output;
    }

    private static Type? ResolveManagedType(string nativeName, Dictionary<string, Type[]> types)
    {
        Type? alias = nativeName switch
        {
            "ImVec2" => typeof(float2),
            "ImVec4" => typeof(float4),
            "PlaygroundImVec2" => typeof(PlaygroundImVec2),
            "PlaygroundImVec4" => typeof(PlaygroundImVec4),
            "PlaygroundFloatRect" => typeof(PlaygroundFloatRect),
            "ImVector<ImWchar>" => typeof(ImVector<uint>),
            "ImVector<ImDrawVert>" => typeof(ImVector<ImDrawVert>),
            "ImVector<ImDrawIdx>" => typeof(ImVector<ushort>),
            "ImVector<ImDrawCmd>" => typeof(ImVector<ImDrawCmd>),
            "ImVector<ImDrawList*>" => typeof(ImVector<ImDrawListPtr>),
            "ImVector<ImTextureData*>" => typeof(ImVector<ImTextureDataPtr>),
            "ImRect" => typeof(floatRect),
            _ => null
        };
        if (alias is not null) return alias;
        if (!types.TryGetValue(nativeName, out var matches)) return null;
        return matches.Where(type => type.IsValueType).OrderBy(type => type.FullName, StringComparer.Ordinal).FirstOrDefault();
    }

    private static FieldInfo? FindInstanceField(Type type, string nativeName)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var all = type.GetFields(flags);
        return all.FirstOrDefault(field => string.Equals(field.Name, nativeName, StringComparison.Ordinal)) ??
               all.FirstOrDefault(field => Normalize(field.Name) == Normalize(nativeName));
    }

    private static string Normalize(string name) => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static int UnsafeSizeOf(Type type)
    {
        MethodInfo method = typeof(Unsafe).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(Unsafe.SizeOf) && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1);
        return (int)method.MakeGenericMethod(type).Invoke(null, null)!;
    }

    private static int FieldSize(FieldInfo field)
    {
        var fixedBuffer = field.GetCustomAttribute<FixedBufferAttribute>();
        return fixedBuffer is null ? UnsafeSizeOf(field.FieldType) : checked(UnsafeSizeOf(fixedBuffer.ElementType) * fixedBuffer.Length);
    }

    private static void RunNegativeChecks(Metadata metadata, string rid, string managedHash, ExportSet exports, Dictionary<string, bool> symbols, LayoutComparison comparison)
    {
        var activeReport = CurrentReport ?? throw new InvalidOperationException("Negative self-checks require an active ABI report.");
        var checks = (JsonArray)activeReport["negativeChecks"]!;
        ExpectNegative("bad-schema", () =>
        {
            using var doc = ParseStrictJson(metadata.NativeManifestText!, "negative schema fixture", MaximumManifestBytes);
            var node = JsonNode.Parse(doc.RootElement.GetRawText())!.AsObject();
            node["schema"] = "unknown-schema";
            using var altered = JsonDocument.Parse(node.ToJsonString());
            ValidateProbe(altered.RootElement, rid);
        }, checks);
        ExpectNegative("bad-version", () =>
        {
            using var doc = ParseStrictJson(metadata.NativeManifestText!, "negative version fixture", MaximumManifestBytes);
            var node = JsonNode.Parse(doc.RootElement.GetRawText())!.AsObject();
            node["upstream"]!["version"] = "9.9.9";
            using var altered = JsonDocument.Parse(node.ToJsonString());
            ValidateProbe(altered.RootElement, rid);
        }, checks);
        // Schema validation happens OUTSIDE ExpectNegative: these fixtures must specifically
        // reach and fail the native-versus-managed reflection gate, not an earlier parser.
        var sizeFixture = JsonNode.Parse(metadata.NativeManifestText!)!;
        var ioType = sizeFixture["types"]!.AsArray().First(item => item!["name"]!.GetValue<string>() == "ImGuiIO")!;
        ioType["sizeBytes"] = ioType["sizeBytes"]!.GetValue<int>() + ioType["alignmentBytes"]!.GetValue<int>();
        using var sizeDocument = JsonDocument.Parse(sizeFixture.ToJsonString());
        var sizeProbe = ValidateProbe(sizeDocument.RootElement, rid);
        ExpectNegative("mismatching-type-size", () => RequireSafeHostLayoutGate(CompareLayouts(sizeProbe)), checks);

        var offsetFixture = JsonNode.Parse(metadata.NativeManifestText!)!;
        var ioFields = offsetFixture["types"]!.AsArray().First(item => item!["name"]!.GetValue<string>() == "ImGuiIO")!["fields"]!.AsArray();
        var ini = ioFields.First(item => item!["name"]!.GetValue<string>() == "IniFilename")!;
        var log = ioFields.First(item => item!["name"]!.GetValue<string>() == "LogFilename")!;
        int iniOffset = ini["offsetBytes"]!.GetValue<int>();
        ini["offsetBytes"] = log["offsetBytes"]!.GetValue<int>();
        log["offsetBytes"] = iniOffset;
        using var offsetDocument = JsonDocument.Parse(offsetFixture.ToJsonString());
        var offsetProbe = ValidateProbe(offsetDocument.RootElement, rid);
        ExpectNegative("mismatching-field-offset", () => RequireSafeHostLayoutGate(CompareLayouts(offsetProbe)), checks);
        ExpectNegative("missing-required-field", () =>
        {
            var node = JsonNode.Parse(metadata.NativeManifestText!)!;
            var fields = node["types"]!.AsArray().First(item => item!["name"]!.GetValue<string>() == "ImDrawList")!["fields"]!.AsArray();
            fields.Remove(fields.First(item => item!["name"]!.GetValue<string>() == "VtxBuffer"));
            using var altered = JsonDocument.Parse(node.ToJsonString());
            ValidateProbe(altered.RootElement, rid);
        }, checks);
        ExpectNegative("empty-required-fields", () =>
        {
            var node = JsonNode.Parse(metadata.NativeManifestText!)!;
            node["types"]!.AsArray().First(item => item!["name"]!.GetValue<string>() == "ImGuiIO")!["fields"] = new JsonArray();
            using var altered = JsonDocument.Parse(node.ToJsonString());
            ValidateProbe(altered.RootElement, rid);
        }, checks);
        ExpectNegative("malformed-layout-alignment", () =>
        {
            using var doc = ParseStrictJson(metadata.NativeManifestText!, "negative layout fixture", MaximumManifestBytes);
            var node = JsonNode.Parse(doc.RootElement.GetRawText())!.AsObject();
            var firstRequired = node["types"]!.AsArray().First(item => item!["name"]!.GetValue<string>() == "ImGuiIO");
            firstRequired!["sizeBytes"] = firstRequired["sizeBytes"]!.GetValue<int>() + 1;
            using var altered = JsonDocument.Parse(node.ToJsonString());
            var alteredProbe = ValidateProbe(altered.RootElement, rid);
            var comparison = CompareLayouts(alteredProbe);
            RequireSafeHostLayoutGate(comparison);
        }, checks);
        ExpectNegative("bad-config", () =>
        {
            using var doc = ParseStrictJson(metadata.NativeManifestText!, "negative config fixture", MaximumManifestBytes);
            var node = JsonNode.Parse(doc.RootElement.GetRawText())!.AsObject();
            node["configuration"]!["IMGUI_USE_WCHAR32"] = false;
            using var altered = JsonDocument.Parse(node.ToJsonString());
            ValidateProbe(altered.RootElement, rid);
        }, checks);
        ExpectNegative("malformed-probe-report", () => ParseStrictJson("{\"schema\":\"playground_imgui.native-abi\",", "malformed probe fixture", MaximumManifestBytes), checks);
        ExpectNegative("missing-required-export", () =>
        {
            var noSymbols = new HashSet<string>(exports.AllSymbols, StringComparer.Ordinal);
            noSymbols.Remove("GetVersion");
            RequireAllExports(exports.AllSymbols, noSymbols.Contains);
        }, checks);
        ExpectNegative("binding-artifact-identity", () => RequireMatchingIdentity("staged Brutal.ImGui.dll", managedHash, new string('0', 64)), checks);
        ExpectNegative("native-artifact-identity", () => RequireMatchingIdentity("native artifact", metadata.ArtifactHash, new string('0', 64)), checks);
        ExpectNegative("missing-native-export", () =>
        {
            if (symbols.TryGetValue("__prototype_missing_export__", out _)) throw new InvalidOperationException("missing-export fixture collided with actual inventory");
            RequireAllExports(["__prototype_missing_export__"], name => NativeLibrary.TryGetExport(s_activeLibrary, name, out _));
        }, checks);
        if (comparison.SafeHostSubsetPassed is false)
            throw new InvalidOperationException("Required layout gate unexpectedly failed before negative self-checks.");
    }

    private static void RequireSafeHostLayoutGate(LayoutComparison comparison)
    {
        if (!comparison.SafeHostSubsetPassed)
            throw new InvalidOperationException("Required safe-host public layout gate failed; native contexts and native-field access are blocked. See layoutComparison.requiredGateFailures in the report.");
    }

    private static void ExpectNegative(string name, Action action, JsonArray checks)
    {
        try { action(); }
        catch (Exception)
        {
            checks.Add(new JsonObject { ["name"] = name, ["rejected"] = true });
            return;
        }
        throw new InvalidOperationException($"Negative check '{name}' did not fail closed.");
    }

    private static void RequireAllExports(IEnumerable<string> required, Func<string, bool> exported)
    {
        string[] missing = required.Where(name => !exported(name)).ToArray();
        if (missing.Length != 0) throw new InvalidOperationException("Frozen prototype artifact is missing required exports: " + string.Join(", ", missing));
    }

    private static void CheckProbeIdentityFunctions(nint library, string rid)
    {
        var commitFn = (delegate* unmanaged[Cdecl]<byte*>)NativeLibrary.GetExport(library, "playground_imgui_probe_upstream_commit");
        var targetFn = (delegate* unmanaged[Cdecl]<byte*>)NativeLibrary.GetExport(library, "playground_imgui_probe_target_rid");
        if (ReadNativeUtf8(commitFn(), "native source commit") != SourceCommit)
            throw new InvalidOperationException("Native identity helper disagrees with its ABI manifest/source lock.");
        if (ReadNativeUtf8(targetFn(), "native target RID") != rid)
            throw new InvalidOperationException("Native identity helper disagrees with the staged target RID.");
    }

    private static void InstallResolver(Assembly assembly, nint library)
    {
        NativeLibrary.SetDllImportResolver(assembly, (name, _, _) => name == "imgui" ? library : 0);
    }

    private static void CheckManagedVersionAndExistingLayoutGuard()
    {
        string version = ImGui.GetVersion().ToString();
        if (version != "1.92.2") throw new InvalidOperationException($"Managed P/Invoke GetVersion reported '{version}', expected 1.92.2.");
        if (!ImGui.DebugCheckVersionAndDataLayout(Constants.IMGUI_VERSION,
                (nuint)sizeof(ImGuiIO), (nuint)sizeof(ImGuiStyle), (nuint)sizeof(float2),
                (nuint)sizeof(float4), (nuint)sizeof(ImDrawVert), sizeof(ushort)))
            throw new InvalidOperationException("Existing BRUTAL DebugCheckVersionAndDataLayout guard rejected the measured native artifact.");
    }

    private static void ExerciseManagedAbi(nint library, ref ImGuiContextPtr context, ref bool contextCreated, JsonObject report)
    {
        var exercise = (JsonArray)report["exercises"]!;
        var rect = new floatRect { Min = new float2(1, 2), Max = new float2(9, 10) };
        var insidePoint = new float2(3, 4);
        var outsidePoint = new float2(-1, 4);
        Bool8 inside, outside;
        float4 rectVector;
        floatRect* rectPtr = &rect;
        float2* insidePointer = &insidePoint;
        float2* outsidePointer = &outsidePoint;
        inside = PInvoke.ImRect_Contains_internal_0(rectPtr, insidePointer);
        outside = PInvoke.ImRect_Contains_internal_0(rectPtr, outsidePointer);
        rectVector = PInvoke.ImRect_ToVec4_internal(rectPtr);
        if (!inside || outside || rectVector.X != 1 || rectVector.Y != 2 || rectVector.Z != 9 || rectVector.W != 10)
            throw new InvalidOperationException("Managed pointer POD rectangle/one-byte Bool8/float4 return exercise failed.");
        exercise.Add("managed P/Invoke: floatRect* + float2*, one-byte Bool8 true/false, float4 return");
        Persist(report);

        var colorConvert = (delegate* unmanaged[Cdecl]<uint, PlaygroundImVec4>)NativeLibrary.GetExport(library, "playground_imgui_abi_color_convert_u32");
        var convertedColor = colorConvert(0x80402010);
        float[] expectedColor = [16 / 255f, 32 / 255f, 64 / 255f, 128 / 255f];
        float[] actualColor = [convertedColor.x, convertedColor.y, convertedColor.z, convertedColor.w];
        for (int i = 0; i < expectedColor.Length; i++)
            if (MathF.Abs(actualColor[i] - expectedColor[i]) > 0.001f) throw new InvalidOperationException("Managed PlaygroundImVec4 by-value return failed.");
        var contains = (delegate* unmanaged[Cdecl]<PlaygroundFloatRect, PlaygroundImVec2, byte>)NativeLibrary.GetExport(library, "playground_imgui_abi_rect_contains");
        var toRectVec4 = (delegate* unmanaged[Cdecl]<PlaygroundFloatRect, PlaygroundImVec4>)NativeLibrary.GetExport(library, "playground_imgui_abi_rect_to_vec4");
        var podRect = new PlaygroundFloatRect(new PlaygroundImVec2(1, 2), new PlaygroundImVec2(9, 10));
        if (contains(podRect, new PlaygroundImVec2(3, 4)) != 1 || contains(podRect, new PlaygroundImVec2(-1, 4)) != 0)
            throw new InvalidOperationException("Managed by-value POD rectangle/uint8 bool exercise failed.");
        var podResult = toRectVec4(podRect);
        if (podResult.x != 1 || podResult.y != 2 || podResult.z != 9 || podResult.w != 10)
            throw new InvalidOperationException("Managed PlaygroundFloatRect argument/PlaygroundImVec4 return failed.");
        exercise.Add("managed C ABI calls: POD ImVec2/ImVec4/FloatRect by-value arguments/returns and uint8 bool");
        Persist(report);

        var allocation = ImGui.MemAlloc(37);
        if (allocation.IsNull()) throw new InvalidOperationException("Managed ImGui.MemAlloc returned null.");
        ImGui.MemFree(allocation);
        exercise.Add("managed P/Invoke allocator pair: MemAlloc(37) / MemFree");
        Persist(report);

        TestReverseCallbacks(library, exercise, report);

        report["contextPhase"] = "creating-after-all-gates";
        Persist(report);
        context = ImGui.CreateContext();
        if (context.IsNull()) throw new InvalidOperationException("Managed CreateContext returned null after all identity/export/layout gates.");
        contextCreated = true;
        report["contextPhase"] = "created";
        Persist(report);
        var io = ImGui.GetIO();
        string fontPath = Path.Combine(AppContext.BaseDirectory, "fonts", "JetBrainsMono-Regular.ttf");
        if (!File.Exists(fontPath)) throw new FileNotFoundException("Managed WCHAR32/context probe needs the staged playground font.", fontPath);
        io.FontDefault = io.Fonts.AddFontFromFileTTF(fontPath, 18);
        if (io.FontDefault.IsNull()) throw new InvalidOperationException("Managed font load failed before NewFrame.");
        io.DisplaySize = new float2(320, 200);
        io.DisplayFramebufferScale = new float2(1, 1);
        io.DeltaTime = 1.0f / 60.0f;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset | ImGuiBackendFlags.RendererHasTextures;
        report["contextPhase"] = "font-and-frame-input-configured";
        Persist(report);

        io.AddInputCharacter(0x00E9);
        io.AddInputCharacter(0x1F600);
        report["contextPhase"] = "character-events-queued";
        Persist(report);
        ImGui.NewFrame();
        report["contextPhase"] = "new-frame-completed";
        Persist(report);
        var characters = io.InputQueueCharacters;
        report["contextPhase"] = "input-vector-field-read";
        Persist(report);
        if (characters.Count != 2 || characters.DataRaw[0] != 0x00E9 || characters.DataRaw[1] != 0x1F600)
            throw new InvalidOperationException($"Managed WCHAR32 BMP/supplementary input failed: count={characters.Count}, values={(characters.Count > 0 ? characters.DataRaw[0].ToString("X") : "none")}/{(characters.Count > 1 ? characters.DataRaw[1].ToString("X") : "none")}.");
        exercise.Add("managed ImGuiIO.AddInputCharacter: BMP U+00E9 and supplementary U+1F600 retained as WCHAR32");
        Persist(report);

        var setWindowPos = (delegate* unmanaged[Cdecl]<PlaygroundImVec2, int, void>)NativeLibrary.GetExport(library, "playground_imgui_abi_set_next_window_pos");
        var getWindowPos = (delegate* unmanaged[Cdecl]<PlaygroundImVec2>)NativeLibrary.GetExport(library, "playground_imgui_abi_get_window_pos");
        setWindowPos(new PlaygroundImVec2(19, 23), (int)ImGuiCond.Always);
        var requestedPosition = new float2(19, 23);
        ImGui.SetNextWindowSize(new float2(120, 80), ImGuiCond.Always);
        Bool8 visible = ImGui.Begin("Managed ABI probe"u8);
        var returnedPosition = ImGui.GetWindowPos();
        var helperPosition = getWindowPos();
        if (MathF.Abs(returnedPosition.X - requestedPosition.X) > 0.01f || MathF.Abs(returnedPosition.Y - requestedPosition.Y) > 0.01f ||
            MathF.Abs(helperPosition.x - requestedPosition.X) > 0.01f || MathF.Abs(helperPosition.y - requestedPosition.Y) > 0.01f)
            throw new InvalidOperationException($"Managed float2 pointer/by-value position calls failed: public={returnedPosition.X},{returnedPosition.Y}; helper={helperPosition.x},{helperPosition.y}.");
        ImGui.Text("managed ABI"u8);
        const nint textureSentinel = 0x12345678;
        ImGui.Image(new ImTextureRef { _TexID = new ImTextureID(textureSentinel) }, new float2(8, 8));
        ImGui.End();
        // Independently test the ordinary BRUTAL pointer argument with different values.
        ImGui.SetNextWindowPos(new float2(41, 47), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new float2(120, 80), ImGuiCond.Always);
        ImGui.Begin("Managed pointer position"u8);
        returnedPosition = ImGui.GetWindowPos();
        if (returnedPosition.X != 41 || returnedPosition.Y != 47)
            throw new InvalidOperationException("Managed float2 pointer position call did not preserve values.");
        ImGui.End();
        ImGui.Render();
        var drawData = ImGui.GetDrawData();
        if (drawData.IsNull()) throw new InvalidOperationException("Managed draw-data pointer was null after Render.");
        bool sawTextureSentinel = false;
        foreach (var list in drawData.CmdLists.Span)
            foreach (ref var command in list.CmdBuffer.Span)
                sawTextureSentinel |= command.ElemCount > 0 && command.TexRef._TexData.IsNull() && command.TexRef._TexID.Value == textureSentinel;
        if (!sawTextureSentinel)
            throw new InvalidOperationException("Managed ImTextureRef by-value Image did not preserve the nonzero texture ID in emitted draw commands.");
        report["headlessFrame"] = new JsonObject
        {
            ["BeginReturnedVisible"] = (bool)visible,
            ["totalVertices"] = drawData.TotalVtxCount,
            ["totalIndices"] = drawData.TotalIdxCount,
            ["note"] = "Headless ABI test reads the draw-data layout but does not claim graphics output; renderer behavior is checked in the separate staged process."
        };
        exercise.Add("managed float2 pointer/by-value position, Bool8 Begin, ImTextureRef by-value Image, NewFrame/Render/draw-data pointers");
        report["contextPhase"] = "managed-calling-tests-passed";
        Persist(report);
    }

    private static void TestReverseCallbacks(nint library, JsonArray exercise, JsonObject report)
    {
        var callbackNames = new[]
        {
            "Platform_GetWindowPos_ManagedFunctionPointer",
            "Platform_GetWindowSize_ManagedFunctionPointer",
            "Platform_GetWindowFramebufferScale_ManagedFunctionPointer"
        };
        var getterNames = new[]
        {
            "Get_GetWindowPos_InteropPointer", "Get_GetWindowSize_InteropPointer",
            "Get_GetWindowFramebufferScale_InteropPointer"
        };
        var invokeNames = new[]
        {
            "playground_imgui_probe_invoke_window_pos", "playground_imgui_probe_invoke_window_size",
            "playground_imgui_probe_invoke_window_framebuffer_scale"
        };
        nint[] callbackPointers =
        [
            (nint)(delegate* unmanaged[Cdecl]<nint, PlaygroundImVec2>)&WindowPosCallback,
            (nint)(delegate* unmanaged[Cdecl]<nint, PlaygroundImVec2>)&WindowSizeCallback,
            (nint)(delegate* unmanaged[Cdecl]<nint, PlaygroundImVec2>)&FramebufferScaleCallback
        ];
        PlaygroundImVec2[] expected =
        [
            new(101.25f, 202.5f), new(303.75f, 404.125f), new(1.5f, 2.25f)
        ];
        nint sentinel = (nint)0x12345;
        for (int i = 0; i < callbackNames.Length; i++)
        {
            nint slot = NativeLibrary.GetExport(library, callbackNames[i]);
            Marshal.WriteIntPtr(slot, callbackPointers[i]);
            s_callbackViewport = 0;
            s_callbackIndex = i;
            PlaygroundImVec2 result = default;
            var invoke = (delegate* unmanaged[Cdecl]<nint, PlaygroundImVec2*, byte>)NativeLibrary.GetExport(library, invokeNames[i]);
            if (invoke(sentinel, &result) != 1 || s_callbackViewport != sentinel || !Same(result, expected[i]))
                throw new InvalidOperationException($"Managed reverse Cdecl callback slot {callbackNames[i]} failed by-value argument/return validation.");

            var getTrampoline = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(library, getterNames[i]);
            var getter = getTrampoline();
            if (getter == 0) throw new InvalidOperationException($"Native managed-callback trampoline getter {getterNames[i]} returned null.");
            s_callbackViewport = 0;
            var trampoline = (delegate* unmanaged[Cdecl]<nint, PlaygroundImVec2>)getter;
            result = trampoline(sentinel);
            if (s_callbackViewport != sentinel || !Same(result, expected[i]))
                throw new InvalidOperationException($"Managed call through native trampoline {getterNames[i]} failed ABI validation.");
        }
        exercise.Add("three rooted Cdecl reverse callbacks: writable slot, native invoke, managed call through native by-value ImVec2 trampoline");
        Persist(report);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static PlaygroundImVec2 WindowPosCallback(nint viewport) => Callback(viewport, 0);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static PlaygroundImVec2 WindowSizeCallback(nint viewport) => Callback(viewport, 1);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static PlaygroundImVec2 FramebufferScaleCallback(nint viewport) => Callback(viewport, 2);

    private static PlaygroundImVec2 Callback(nint viewport, int expectedIndex)
    {
        s_callbackViewport = viewport;
        if (s_callbackIndex != expectedIndex) return default;
        return expectedIndex switch
        {
            0 => new PlaygroundImVec2(101.25f, 202.5f),
            1 => new PlaygroundImVec2(303.75f, 404.125f),
            _ => new PlaygroundImVec2(1.5f, 2.25f)
        };
    }

    private static bool Same(PlaygroundImVec2 left, PlaygroundImVec2 right) => left.x == right.x && left.y == right.y;

    private static string ReadNativeUtf8(byte* pointer, string subject)
    {
        if (pointer is null) throw new InvalidOperationException($"Native {subject} pointer is null.");
        int length = 0;
        while (length < MaximumManifestBytes && pointer[length] != 0) ++length;
        if (length == MaximumManifestBytes) throw new InvalidOperationException($"Native {subject} exceeds the bounded UTF-8 string size.");
        try { return StrictUtf8.GetString(new ReadOnlySpan<byte>(pointer, length)); }
        catch (DecoderFallbackException error) { throw new InvalidOperationException($"Native {subject} is not valid UTF-8.", error); }
    }

    private static JsonDocument ParseStrictJson(string text, string subject, int maximumBytes)
    {
        if (Encoding.UTF8.GetByteCount(text) > maximumBytes) throw new InvalidOperationException($"{subject} exceeds the configured size bound.");
        JsonDocument document;
        try { document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 32 }); }
        catch (JsonException error) { throw new InvalidOperationException($"{subject} is malformed JSON: {error.Message}", error); }
        try { RejectDuplicateKeys(document.RootElement, subject, 0); }
        catch { document.Dispose(); throw; }
        return document;
    }

    private static void RejectDuplicateKeys(JsonElement element, string subject, int depth)
    {
        if (depth > 32) throw new InvalidOperationException($"{subject} exceeds the JSON nesting limit.");
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new InvalidOperationException($"{subject} contains duplicate JSON key '{property.Name}'.");
                RejectDuplicateKeys(property.Value, subject, depth + 1);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateKeys(item, subject, depth + 1);
    }

    private static void RequireExactKeys(JsonElement element, string subject, params string[] expected)
    {
        RequireObject(element, subject);
        var actual = element.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected))
            throw new InvalidOperationException($"{subject} has an unexpected/missing field set: expected [{string.Join(",", expected)}], got [{string.Join(",", actual.OrderBy(name => name, StringComparer.Ordinal))}].");
    }

    private static void RequireObject(JsonElement element, string subject)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidOperationException($"{subject} must be a JSON object.");
    }

    private static JsonElement Property(JsonElement element, string name, string subject)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            throw new InvalidOperationException($"{subject} is missing required property '{name}'.");
        return value;
    }

    private static string String(JsonElement element, string name, string subject)
    {
        var value = Property(element, name, subject);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(value.GetString()))
            throw new InvalidOperationException($"{subject}.{name} must be a non-empty string.");
        return value.GetString()!;
    }

    private static int Integer(JsonElement element, string name, string subject)
    {
        var value = Property(element, name, subject);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
            throw new InvalidOperationException($"{subject}.{name} must be a 32-bit integer.");
        return result;
    }

    private static long Integer64(JsonElement element, string name, string subject)
    {
        var value = Property(element, name, subject);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long result))
            throw new InvalidOperationException($"{subject}.{name} must be a 64-bit integer.");
        return result;
    }

    private static bool Boolean(JsonElement element, string name, string subject)
    {
        var value = Property(element, name, subject);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidOperationException($"{subject}.{name} must be a JSON boolean.");
        return value.GetBoolean();
    }

    private static string ReadBounded(string path, int maximumBytes)
    {
        var info = new FileInfo(path);
        if (info.Length > maximumBytes) throw new InvalidOperationException($"Metadata file exceeds its {maximumBytes}-byte bound: {Path.GetFileName(path)}.");
        return File.ReadAllText(path, StrictUtf8);
    }

    private static void RequireMatchingIdentity(string subject, string actual, string expected)
    {
        if (!FixedHashEquals(actual, expected)) throw new InvalidOperationException($"{subject} SHA-256 identity mismatch: expected {expected}, got {actual}.");
    }

    private static JsonArray StringArray(IEnumerable<string> strings)
    {
        var result = new JsonArray();
        foreach (string value in strings) result.Add(value);
        return result;
    }

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static bool IsHash(string value) => value.Length == 64 && value.All(character => Uri.IsHexDigit(character));
    private static bool FixedHashEquals(string left, string right) => IsHash(left) && IsHash(right) && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));

    private static int CountMissingImports(nint library, List<ManagedImport> imports)
    {
        return imports.Count(import => !NativeLibrary.TryGetExport(library, import.ExportName, out _));
    }

    private static void Persist(JsonObject report)
    {
        if (s_reportPath is not null) WriteReport(s_reportPath, report);
    }

    private static void WriteReport(string path, JsonObject report)
    {
        string fullPath = Path.GetFullPath(path);
        string? parent = Path.GetDirectoryName(fullPath);
        if (parent is null || !Directory.Exists(parent) || new DirectoryInfo(parent).LinkTarget is not null)
            throw new InvalidOperationException("ABI report parent directory must already exist and must not be a symlink.");
        File.WriteAllText(fullPath, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
    }

    private sealed record Metadata(string ExportsText, string ExportsHash, string LockHash, string ArtifactHash, string ConfigHash, string ActualConfigHash, string ExpectedManagedHash)
    {
        internal string? NativeManifestText { get; init; }
    }
    private sealed record ManagedImport(string DeclaringType, string MethodName, string ExportName, string ManagedSignature);
    private sealed record ExportDescription(string Name, string Category, string Signature, string Purpose);
    private sealed record UnsupportedEntry(string Name, string Reason);
    private sealed record ExportSet(ExportDescription[] ManagedImports, ExportDescription[] DataExports, ExportDescription[] ExtraFunctions, ExportDescription[] AllExports, UnsupportedEntry[] IntentionallyNotCovered)
    {
        internal string[] ManagedImportNames => ManagedImports.Select(item => item.Name).ToArray();
        internal string[] DataExportNames => DataExports.Select(item => item.Name).ToArray();
        internal string[] ExtraFunctionNames => ExtraFunctions.Select(item => item.Name).ToArray();
        internal string[] AllSymbols => AllExports.Select(item => item.Name).ToArray();
        internal string[] DynamicGetters => ExtraFunctions.Select(item => item.Name).Where(name => name.StartsWith("Get_", StringComparison.Ordinal)).ToArray();
    }
    private sealed record NativeField(string Name, int Offset, int Width, int Alignment);
    private sealed record NativeType(string Name, int Size, int Alignment, Dictionary<string, NativeField> Fields);
    private sealed record ProbeData(string Version, string SourceCommit, string TargetRid, bool Wchar32, bool DisableObsolete, int DrawIndexBytes, int BoolBytes, int WcharBytes, int TextureIdBytes, int SizeTBytes, Dictionary<string, NativeType> Types, JsonElement Bitfields);
    private sealed record LayoutComparison(JsonObject Report, bool SafeHostSubsetPassed, string[] GateFailures);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PlaygroundImVec2(float x, float y)
    {
        public readonly float x = x;
        public readonly float y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PlaygroundImVec4(float x, float y, float z, float w)
    {
        public readonly float x = x;
        public readonly float y = y;
        public readonly float z = z;
        public readonly float w = w;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PlaygroundFloatRect
    {
        public readonly PlaygroundImVec2 Min;
        public readonly PlaygroundImVec2 Max;
        public PlaygroundFloatRect(PlaygroundImVec2 min, PlaygroundImVec2 max) => (Min, Max) = (min, max);
    }
}
