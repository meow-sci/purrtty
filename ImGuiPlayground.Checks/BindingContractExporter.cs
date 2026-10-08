using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ImGuiPlayground.Checks;

// Reflection facts only. Native names, const/reference semantics, bit masks and callback
// ownership cannot be recovered from this assembly and must be reviewed separately.
internal sealed class BindingContractExporter
{
    internal const string Schema = "playground_imgui.managed-binding-contract";
    internal const int SchemaVersion = 1;
    private const int MaximumTypes = 8192;
    private const int MaximumMembers = 65536;
    private const int MaximumOutputBytes = 64 * 1024 * 1024;
    private const BindingFlags DeclaredMembers = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
    private readonly SortedDictionary<string, Type> types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonObject> records = new(StringComparer.Ordinal);
    private readonly HashSet<Assembly> attributeAssemblies = new();
    private int members;

    internal static void Run(string assemblyDirectory, string outputPath)
    {
        string input = Path.GetFullPath(assemblyDirectory);
        string output = Path.GetFullPath(outputPath);
        if (!Directory.Exists(input)) throw new DirectoryNotFoundException("Selected assembly directory does not exist.");
        if (!output.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Contract output must have a .json extension.");
        output = ValidateOutputBoundary(input, output);
        using var selection = new SelectedAssemblies(input);
        Assembly binding = selection.LoadBinding();
        var exporter = new BindingContractExporter();
        JsonObject contract = exporter.Build(binding);
        contract["assemblies"] = selection.Identities(exporter.types.Values.Select(type => type.Assembly).Concat(exporter.attributeAssemblies));
        selection.VerifyUnchanged();
        byte[] content = Encoding.UTF8.GetBytes(contract.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        if (content.Length > MaximumOutputBytes) throw new InvalidDataException("Contract output exceeds the 64 MiB bound.");
        // A failed export never replaces an existing contract with partial evidence.
        string directory = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, ".binding-contract-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(content);
            File.Move(temporary, output, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // Resolve directory aliases before any output creation. Use the resolved destination,
    // not its original alias. This guards existing redirects, not hostile concurrent swaps.
    private static string ValidateOutputBoundary(string input, string output)
    {
        int remainingLinks = 64;
        string protectedDirectory = ResolvePhysicalDirectory(input, ref remainingLinks);
        string parent = ResolvePhysicalDirectory(Path.GetDirectoryName(output)!, ref remainingLinks);
        string resolvedOutput = Path.Combine(parent, Path.GetFileName(output));
        string prefix = Path.EndsInDirectorySeparator(protectedDirectory)
            ? protectedDirectory : protectedDirectory + Path.DirectorySeparatorChar;
        // Conservative on case-sensitive volumes, so case aliases cannot bypass protection.
        if (resolvedOutput.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Contract output must not be inside the external assembly directory.");
        if (TryAttributes(resolvedOutput) is { } attributes &&
            (attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new ArgumentException("Contract output must be a regular file, not a redirected path or directory.");
        return resolvedOutput;
    }

    private static string ResolvePhysicalDirectory(string path, ref int remainingLinks)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string current = Path.GetPathRoot(full)!;
        foreach (string part in full[current.Length..].Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(current, part);
            FileAttributes? attributes = TryAttributes(candidate);
            if (attributes is { } value && (value & FileAttributes.ReparsePoint) != 0)
            {
                if (--remainingLinks < 0) throw new ArgumentException("Too many directory redirects in contract paths.");
                FileSystemInfo? target = new DirectoryInfo(candidate).ResolveLinkTarget(returnFinalTarget: true);
                if (target is null || !Directory.Exists(target.FullName))
                    throw new ArgumentException("Contract directory redirect cannot be resolved to an existing directory.");
                current = ResolvePhysicalDirectory(target.FullName, ref remainingLinks);
            }
            else
            {
                if (attributes is { } existing && (existing & FileAttributes.Directory) == 0)
                    throw new ArgumentException("Contract directory path contains a non-directory component.");
                current = candidate;
            }
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    private static FileAttributes? TryAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    internal JsonObject Build(Assembly binding)
    {
        string rid = ValidateHost(RuntimeInformation.ProcessArchitecture, IntPtr.Size, BitConverter.IsLittleEndian,
            OperatingSystem.IsMacOS() ? "osx" : OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" : "unknown");
        Type[] roots = binding.GetTypes(); // ReflectionTypeLoadException is deliberately fatal.
        foreach (Type type in roots.Where(type => type.IsValueType || IsDelegate(type))) Add(type);
        var imports = new List<(string Key, JsonObject Record)>();
        foreach (Type type in roots.OrderBy(Id, StringComparer.Ordinal))
        foreach (MethodInfo method in type.GetMethods(DeclaredMembers).OrderBy(method => method.MetadataToken))
        {
            DllImportAttribute? dll = method.GetCustomAttribute<DllImportAttribute>();
            CustomAttributeData? library = method.CustomAttributes.SingleOrDefault(attribute =>
                attribute.AttributeType.FullName == "System.Runtime.InteropServices.LibraryImportAttribute");
            if (dll is null && library is null) continue;
            string? libraryName = library?.ConstructorArguments.Single().Value as string;
            string? libraryEntry = library is null ? null : NamedString(library, "EntryPoint") ?? method.Name;
            string entry = dll?.EntryPoint ?? libraryEntry ?? throw new InvalidDataException("Import entry point missing.");
            string nativeLibrary = dll?.Value ?? libraryName ?? throw new InvalidDataException("Import library missing.");
            ValidateImportMetadata(nativeLibrary, entry, dll is not null, libraryName, libraryEntry);
            if (!method.IsStatic || method.ContainsGenericParameters)
                throw new InvalidDataException("Generic or instance import cannot define a native entry point.");
            JsonObject record = DescribeSignature(method);
            record["library"] = nativeLibrary;
            record["entryPoint"] = entry;
            record["dllImport"] = dll is null ? null : new JsonObject
            {
                ["callingConvention"] = dll.CallingConvention.ToString(), ["charSet"] = dll.CharSet.ToString(),
                ["exactSpelling"] = dll.ExactSpelling, ["setLastError"] = dll.SetLastError,
                ["preserveSig"] = dll.PreserveSig, ["bestFitMapping"] = dll.BestFitMapping,
                ["throwOnUnmappableChar"] = dll.ThrowOnUnmappableChar
            };
            record["libraryImport"] = library is null ? null : Attribute(library);
            imports.Add((nativeLibrary + "\0" + entry, record));
        }
        ValidateImportKeys(imports.Select(import => import.Key));
        if (imports.Count == 0) throw new InvalidDataException("Selected binding declares no native imports.");
        while (records.Count < types.Count)
        {
            var pending = types.Where(pair => !records.ContainsKey(pair.Key)).ToArray();
            foreach (var pair in pending) records.Add(pair.Key, DescribeType(pair.Value));
        }
        var importsArray = new JsonArray(imports.OrderBy(import => import.Key, StringComparer.Ordinal)
            .Select(import => (JsonNode)import.Record).ToArray());
        var typesArray = new JsonArray(records.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (JsonNode)pair.Value).ToArray());
        return new JsonObject
        {
            ["schema"] = Schema, ["schemaVersion"] = SchemaVersion, ["exporterVersion"] = 1,
            ["bindingAssembly"] = binding.FullName,
            ["dependencyScope"] = "strict reflected metadata closure; declared references retained, implementation-only runtime dependencies not qualified",
            ["host"] = new JsonObject
            {
                ["rid"] = rid, ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
                ["pointerSizeBytes"] = IntPtr.Size, ["endianness"] = "little",
                ["runtime"] = RuntimeInformation.FrameworkDescription,
                ["measurementScope"] = "this runtime and process architecture only; not native or cross-target proof"
            },
            ["limits"] = new JsonObject { ["types"] = MaximumTypes, ["members"] = MaximumMembers, ["outputBytes"] = MaximumOutputBytes },
            ["counts"] = new JsonObject
            {
                ["imports"] = imports.Count, ["types"] = types.Count,
                ["fields"] = records.Values.Sum(record => record["fields"]?.AsArray().Count ?? 0),
                ["instanceFields"] = records.Values.SelectMany(record => record["fields"]?.AsArray() ?? new JsonArray()).Count(field => !field!["isStatic"]!.GetValue<bool>()),
                ["fixedBuffers"] = records.Values.SelectMany(record => record["fields"]?.AsArray() ?? new JsonArray()).Count(field => field!["fixedBuffer"] is not null),
                ["inlineArrays"] = records.Values.Count(record => record["inlineArray"] is not null),
                ["refReturnProperties"] = records.Values.Sum(record => record["refReturnProperties"]?.AsArray().Count ?? 0),
                ["delegates"] = records.Values.Count(record => record["kind"]!.GetValue<string>() == "delegate"),
                ["functionPointers"] = types.Values.Count(type => type.IsFunctionPointer),
                ["closedGenerics"] = types.Values.Count(type => type.IsConstructedGenericType)
            },
            ["limitations"] = Strings(new[]
            {
                "No native implementation, native layout parity or behavior is certified by this metadata.",
                "Native bitfield masks, const/reference distinctions, template mappings and opaque nint callbacks require reviewed native mappings.",
                "Overlapping managed fields and ref-return properties are preserved, not declared safe or repaired.",
                "Runtime measurements of managed reference-containing and auto-layout types are not unmanaged ABI promises.",
                "Open generic and byref-like layout measurements are explicitly unavailable; no offsets are inferred.",
                "Dynamic bridge exports are a reviewed declaration supplement, not discovered P/Invokes."
            }),
            ["imports"] = importsArray, ["types"] = typesArray,
            ["dynamicExports"] = DynamicExports(binding)
        };
    }

    internal static string ValidateHost(Architecture architecture, int pointerSize, bool littleEndian, string os)
    {
        string rid = os + "-" + architecture.ToString().ToLowerInvariant();
        if (pointerSize != 8 || !littleEndian || rid is not ("osx-arm64" or "win-x64" or "linux-x64"))
            throw new PlatformNotSupportedException("Measurements require osx-arm64, win-x64 or linux-x64, 64-bit little-endian.");
        return rid;
    }

    internal static void ValidateImportMetadata(string library, string entry, bool hasDll, string? libraryImport, string? libraryEntry)
    {
        if (string.IsNullOrWhiteSpace(library) || string.IsNullOrWhiteSpace(entry))
            throw new InvalidDataException("Import library/entry point is empty.");
        if (hasDll && libraryImport is not null && (library != libraryImport || entry != libraryEntry))
            throw new InvalidDataException("Contradictory DllImport and LibraryImport library/entry point metadata.");
    }

    internal static void ValidateImportKeys(IEnumerable<string> keys)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (string key in keys)
            if (!unique.Add(key)) throw new InvalidDataException("Ambiguous duplicate native import entry point.");
    }

    private string Add(Type type)
    {
        string key = Id(type);
        if (types.TryGetValue(key, out Type? previous))
        {
            if (previous != type) throw new InvalidDataException("Ambiguous structural type identity: " + key);
            return key;
        }
        if (types.Count >= MaximumTypes) throw new InvalidDataException("Type graph exceeds its bound.");
        types.Add(key, type);
        return key;
    }

    internal static string Id(Type type)
    {
        if (type.IsFunctionPointer)
            return "fnptr[" + string.Join(",", type.GetFunctionPointerCallingConventions().Select(Id)) + ";" +
                (type.IsUnmanagedFunctionPointer ? "unmanaged" : "managed") + "](" +
                string.Join(",", type.GetFunctionPointerParameterTypes().Select(Id)) + ")->" + Id(type.GetFunctionPointerReturnType());
        if (type.IsPointer) return Id(type.GetElementType()!) + "*";
        if (type.IsByRef) return Id(type.GetElementType()!) + "&";
        if (type.IsArray) return Id(type.GetElementType()!) + (type.IsSZArray ? "[]" : "[rank=" + type.GetArrayRank() + "]");
        if (type.IsGenericParameter)
        {
            string owner = type.DeclaringMethod is { } method
                ? Id(method.DeclaringType!) + "::" + method.MetadataToken.ToString("x8", CultureInfo.InvariantCulture)
                : type.DeclaringType!.Assembly.GetName().Name + "::" + type.DeclaringType.FullName;
            return owner + "!" + type.GenericParameterPosition;
        }
        string name = type.IsGenericType ? type.GetGenericTypeDefinition().FullName! : type.FullName!;
        if (string.IsNullOrEmpty(name)) throw new InvalidDataException("Unnamed type in contract.");
        return type.Assembly.GetName().Name + "::" + name +
            (type.IsConstructedGenericType ? "<" + string.Join(",", type.GetGenericArguments().Select(Id)) + ">" : "");
    }

    internal JsonObject DescribeType(Type type)
    {
        string kind = type == typeof(void) ? "void" : type.IsPointer ? "pointer" : type.IsByRef ? "byref" :
            type.IsFunctionPointer ? "function-pointer" : type.IsArray ? "array" : type.IsGenericParameter ? "generic-parameter" :
            type.IsEnum ? "enum" : type.IsPrimitive ? "scalar" : IsDelegate(type) ? "delegate" : type.IsValueType ? "value" : "reference";
        var record = new JsonObject
        {
            ["id"] = Id(type), ["assemblyQualifiedName"] = type.AssemblyQualifiedName,
            ["kind"] = kind, ["name"] = type.FullName,
            ["assembly"] = type.Assembly.FullName,
            ["containsGenericParameters"] = type.ContainsGenericParameters,
            ["isByRefLike"] = type.IsByRefLike
        };
        if (type.HasElementType) record["elementType"] = Add(type.GetElementType()!);
        if (type.IsPointer)
        {
            int depth = 0;
            for (Type element = type; element.IsPointer; element = element.GetElementType()!) ++depth;
            record["pointerDepth"] = depth;
        }
        if (type.IsArray) { record["rank"] = type.GetArrayRank(); record["isSZArray"] = type.IsSZArray; }
        if (type.IsFunctionPointer)
        {
            record["signature"] = new JsonObject
            {
                ["conventionSource"] = "runtime Type may erase conventions; use the field/parameter modified signatureShape as authoritative",
                ["unmanaged"] = type.IsUnmanagedFunctionPointer,
                ["callingConventions"] = Strings(type.GetFunctionPointerCallingConventions().Select(Add)),
                ["returnType"] = Add(type.GetFunctionPointerReturnType()),
                ["parameterTypes"] = Strings(type.GetFunctionPointerParameterTypes().Select(Add))
            };
        }
        if (type.IsGenericParameter)
        {
            record["position"] = type.GenericParameterPosition;
            record["genericParameterAttributes"] = type.GenericParameterAttributes.ToString();
            record["constraints"] = Strings(type.GetGenericParameterConstraints().Select(Add));
        }
        if (type.IsGenericType)
        {
            record["genericDefinition"] = Add(type.GetGenericTypeDefinition());
            record["genericArguments"] = Strings(type.GetGenericArguments().Select(Add));
        }
        if (kind is "value" or "enum" or "scalar" or "delegate" or "reference")
        {
            record["metadataToken"] = type.MetadataToken;
            record["moduleMvid"] = type.Module.ModuleVersionId.ToString("D");
            record["typeAttributes"] = type.Attributes.ToString();
            record["attributes"] = Attributes(type.CustomAttributes);
            if (type.StructLayoutAttribute is { } layout)
                record["layout"] = new JsonObject
                {
                    ["kind"] = layout.Value.ToString(), ["declaredSizeBytes"] = layout.Size,
                    ["pack"] = layout.Pack, ["charSet"] = layout.CharSet.ToString()
                };
        }
        record["measurement"] = Measurement(type);
        record["marshalerMeasurement"] = MarshalerSize(type);
        if (type.IsPrimitive || type.IsEnum)
        {
            Type scalar = type.IsEnum ? type.GetEnumUnderlyingType() : type;
            record["scalar"] = new JsonObject
            {
                ["underlyingType"] = type.IsEnum ? Add(scalar) : Id(scalar),
                ["signedness"] = scalar == typeof(sbyte) || scalar == typeof(short) || scalar == typeof(int) || scalar == typeof(long) || scalar == typeof(nint)
                    ? "signed" : scalar == typeof(byte) || scalar == typeof(ushort) || scalar == typeof(uint) || scalar == typeof(ulong) || scalar == typeof(nuint) || scalar == typeof(char)
                    ? "unsigned" : "not-integer",
                ["pointerSized"] = scalar == typeof(nint) || scalar == typeof(nuint)
            };
        }
        if (type.IsEnum)
        {
            record["enumValues"] = new JsonArray(type.GetFields(DeclaredMembers).Where(field => field.IsLiteral)
                .OrderBy(field => field.MetadataToken).Select(field => (JsonNode)new JsonObject
                { ["name"] = field.Name, ["value"] = Constant(field.GetRawConstantValue()), ["metadataToken"] = field.MetadataToken }).ToArray());
        }
        // All primary value types are roots; dependency value types are expanded recursively.
        // Reference types are named shapes, not an invitation to export the entire BCL heap graph.
        if (type.IsValueType && !type.IsPrimitive && !type.IsEnum)
        {
            FieldInfo[] fields = type.GetFields(DeclaredMembers).OrderBy(field => field.MetadataToken).ToArray();
            if (fields.Length > 4096) throw new InvalidDataException("One value type exceeds the 4096-field bound.");
            record["fields"] = new JsonArray(fields.Select(field => (JsonNode)Field(field)).ToArray());
            var inline = type.GetCustomAttribute<InlineArrayAttribute>();
            if (inline is not null)
            {
                FieldInfo[] storage = fields.Where(field => !field.IsStatic).ToArray();
                if (inline.Length < 1 || storage.Length != 1) throw new InvalidDataException("Invalid inline-array declaration.");
                record["inlineArray"] = new JsonObject { ["length"] = inline.Length, ["elementType"] = Add(storage[0].FieldType), ["storageFieldToken"] = storage[0].MetadataToken };
            }
            record["overlaps"] = Overlaps(record["fields"]!.AsArray());
            record["refReturnProperties"] = new JsonArray(type.GetProperties(DeclaredMembers)
                .Where(property => property.PropertyType.IsByRef).OrderBy(property => property.MetadataToken)
                .Select(property => (JsonNode)new JsonObject
                {
                    ["name"] = property.Name, ["metadataToken"] = property.MetadataToken,
                    ["type"] = Add(property.PropertyType), ["attributes"] = Attributes(property.CustomAttributes),
                    ["getter"] = property.GetMethod is null ? null : DescribeSignature(property.GetMethod),
                    ["rawStorageLink"] = new JsonObject { ["status"] = "unresolved", ["reason"] = "requires reviewed mapping; method bodies are not inspected" }
                }).ToArray());
        }
        if (IsDelegate(type))
        {
            record["invoke"] = DescribeSignature(type.GetMethod("Invoke", DeclaredMembers) ?? throw new InvalidDataException("Delegate Invoke missing."));
            record["unmanagedFunctionPointer"] = type.CustomAttributes.SingleOrDefault(attribute => attribute.AttributeType == typeof(UnmanagedFunctionPointerAttribute)) is { } attribute
                ? Attribute(attribute) : null;
            if (type.GetCustomAttribute<UnmanagedFunctionPointerAttribute>() is { } convention)
                record["unmanagedFunctionPointerEffective"] = new JsonObject
                {
                    ["callingConvention"] = convention.CallingConvention.ToString(), ["charSet"] = convention.CharSet.ToString(),
                    ["setLastError"] = convention.SetLastError, ["bestFitMapping"] = convention.BestFitMapping,
                    ["throwOnUnmappableChar"] = convention.ThrowOnUnmappableChar
                };
            record["nativeOwnership"] = "unresolved; requires reviewed callback mapping";
        }
        return record;
    }

    private JsonObject Field(FieldInfo field)
    {
        CountMember();
        var record = new JsonObject
        {
            ["name"] = field.Name, ["metadataToken"] = field.MetadataToken,
            ["type"] = Add(field.FieldType), ["fieldAttributes"] = field.Attributes.ToString(),
            ["isStatic"] = field.IsStatic, ["isLiteral"] = field.IsLiteral, ["isInitOnly"] = field.IsInitOnly,
            ["constant"] = field.IsLiteral ? Constant(field.GetRawConstantValue()) : null,
            ["attributes"] = Attributes(field.CustomAttributes),
            ["requiredModifiers"] = Strings(field.GetRequiredCustomModifiers().Select(Add)),
            ["optionalModifiers"] = Strings(field.GetOptionalCustomModifiers().Select(Add)),
            ["declaredOffsetBytes"] = field.GetCustomAttribute<FieldOffsetAttribute>()?.Value,
            ["signatureShape"] = SignatureShape(field.IsLiteral ? field.FieldType : field.GetModifiedFieldType()),
            ["storageWidth"] = StorageWidth(field.FieldType),
            ["runtimeOffset"] = FieldOffset(field),
            ["marshalerOffset"] = MarshalerOffset(field),
            ["nativeDisposition"] = "unresolved; reflection does not classify bitfields, unions or native statics"
        };
        if (field.GetCustomAttribute<FixedBufferAttribute>() is { } fixedBuffer)
        {
            if (fixedBuffer.Length < 1) throw new InvalidDataException("Invalid fixed buffer length.");
            record["fixedBuffer"] = new JsonObject
            { ["elementType"] = Add(fixedBuffer.ElementType), ["length"] = fixedBuffer.Length, ["elementStorageWidth"] = StorageWidth(fixedBuffer.ElementType) };
        }
        return record;
    }

    internal JsonObject DescribeSignature(MethodInfo method)
    {
        CountMember();
        return new JsonObject
        {
            ["declaringType"] = Add(method.DeclaringType!), ["name"] = method.Name,
            ["metadataToken"] = method.MetadataToken, ["moduleMvid"] = method.Module.ModuleVersionId.ToString("D"),
            ["methodAttributes"] = method.Attributes.ToString(), ["implementationAttributes"] = method.GetMethodImplementationFlags().ToString(),
            ["managedCallingConvention"] = method.CallingConvention.ToString(),
            ["attributes"] = Attributes(method.CustomAttributes),
            ["return"] = Parameter(method.ReturnParameter),
            ["parameters"] = new JsonArray(method.GetParameters().Select(parameter => (JsonNode)Parameter(parameter)).ToArray())
        };
    }

    private JsonObject Parameter(ParameterInfo parameter) => new()
    {
        ["position"] = parameter.Position, ["name"] = parameter.Name, ["type"] = Add(parameter.ParameterType),
        ["signatureShape"] = SignatureShape(parameter.GetModifiedParameterType()),
        ["parameterAttributes"] = parameter.Attributes.ToString(), ["isIn"] = parameter.IsIn,
        ["isOut"] = parameter.IsOut, ["isOptional"] = parameter.IsOptional,
        ["hasDefaultValue"] = parameter.HasDefaultValue,
        ["defaultValue"] = parameter.HasDefaultValue ? Constant(parameter.RawDefaultValue) : null,
        ["requiredModifiers"] = Strings(parameter.GetRequiredCustomModifiers().Select(Add)),
        ["optionalModifiers"] = Strings(parameter.GetOptionalCustomModifiers().Select(Add)),
        ["attributes"] = Attributes(parameter.CustomAttributes)
    };

    // Preserve all custom attributes, including MarshalAs/MarshalUsing, without executing
    // constructors from an external assembly. Consumers must reject unsupported marshalling.
    private JsonArray Attributes(IEnumerable<CustomAttributeData> attributes) => new(attributes
        .OrderBy(attribute => attribute.AttributeType.FullName, StringComparer.Ordinal)
        .ThenBy(attribute => attribute.ToString(), StringComparer.Ordinal).Select(attribute => (JsonNode)Attribute(attribute)).ToArray());

    private JsonObject Attribute(CustomAttributeData attribute)
    {
        attributeAssemblies.Add(attribute.AttributeType.Assembly);
        return new JsonObject
        {
            ["type"] = attribute.AttributeType.AssemblyQualifiedName,
            ["constructorArguments"] = new JsonArray(attribute.ConstructorArguments.Select(argument => (JsonNode)AttributeArgument(argument)).ToArray()),
            ["namedArguments"] = new JsonArray(attribute.NamedArguments.OrderBy(argument => argument.MemberName, StringComparer.Ordinal)
                .Select(argument => (JsonNode)new JsonObject
                { ["name"] = argument.MemberName, ["isField"] = argument.IsField, ["value"] = AttributeArgument(argument.TypedValue) }).ToArray())
        };
    }

    private JsonObject AttributeArgument(CustomAttributeTypedArgument argument)
    {
        attributeAssemblies.Add(argument.ArgumentType.Assembly);
        return new JsonObject
        {
            ["type"] = argument.ArgumentType.AssemblyQualifiedName,
            ["value"] = argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> elements
                ? new JsonArray(elements.Select(element => (JsonNode)AttributeArgument(element)).ToArray())
                : argument.Value is Type type ? JsonValue.Create(Add(type)) : Constant(argument.Value)
        };
    }

    // Runtime Type alone erases some function-pointer calling-convention information.
    // Modified signature types retain it, including nested pointer/return modifiers.
    internal JsonObject SignatureShape(Type type)
    {
        var shape = new JsonObject
        {
            ["type"] = Add(type.UnderlyingSystemType),
            ["requiredModifiers"] = Strings(type.GetRequiredCustomModifiers().Select(Add)),
            ["optionalModifiers"] = Strings(type.GetOptionalCustomModifiers().Select(Add))
        };
        if (type.HasElementType) shape["element"] = SignatureShape(type.GetElementType()!);
        if (type.IsFunctionPointer)
        {
            shape["unmanaged"] = type.IsUnmanagedFunctionPointer;
            shape["callingConventions"] = Strings(type.GetFunctionPointerCallingConventions().Select(Add));
            shape["return"] = SignatureShape(type.GetFunctionPointerReturnType());
            shape["parameters"] = new JsonArray(type.GetFunctionPointerParameterTypes().Select(parameter => (JsonNode)SignatureShape(parameter)).ToArray());
        }
        return shape;
    }

    private static JsonNode? Constant(object? value) => value switch
    {
        null => null,
        string text => JsonValue.Create(text),
        char character => JsonValue.Create((int)character),
        bool boolean => JsonValue.Create(boolean),
        // Decimal strings keep all 64-bit integer/enum values exact for non-.NET consumers.
        byte or sbyte or short or ushort or int or uint or long or ulong or decimal => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture)),
        float number => JsonValue.Create(number.ToString("R", CultureInfo.InvariantCulture)),
        double number => JsonValue.Create(number.ToString("R", CultureInfo.InvariantCulture)),
        Missing => JsonValue.Create("<Missing>"),
        DBNull => JsonValue.Create("<DBNull>"),
        _ => throw new InvalidDataException("Unsupported metadata constant kind: " + value.GetType().FullName)
    };

    internal static JsonObject Measurement(Type type)
    {
        if (type == typeof(void)) return Unavailable("void has no storage");
        if (type.IsPointer || type.IsFunctionPointer) return new JsonObject { ["status"] = "measured", ["sizeBytes"] = IntPtr.Size, ["source"] = "process pointer size" };
        if (type.ContainsGenericParameters) return Unavailable("open generic shape");
        if (type.IsByRefLike) return Unavailable("byref-like types cannot instantiate the generic measurement helper");
        if (!type.IsValueType) return Unavailable("not a value type; reference storage is reported separately");
        var measurement = (JsonObject)typeof(BindingContractExporter).GetMethod(nameof(MeasureValue), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type).Invoke(null, null)!;
        return measurement;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Embedding<T> { public byte Prefix; public T Value; }

    private static JsonObject MeasureValue<T>()
    {
        T[] array = new T[2];
        var embedding = default(Embedding<T>);
        long stride = Unsafe.ByteOffset(ref Unsafe.As<T, byte>(ref array[0]), ref Unsafe.As<T, byte>(ref array[1]));
        long alignment = Unsafe.ByteOffset(ref embedding.Prefix, ref Unsafe.As<T, byte>(ref embedding.Value));
        return new JsonObject
        {
            ["status"] = "measured", ["sizeBytes"] = Unsafe.SizeOf<T>(), ["arrayStrideBytes"] = stride,
            ["containsReferences"] = RuntimeHelpers.IsReferenceOrContainsReferences<T>(),
            ["embeddingOffsetAfterBytePrefix"] = alignment,
            ["source"] = "Unsafe.SizeOf<T>, two-element CLR array address difference, sequential byte-prefix embedding; not native alignof"
        };
    }

    private static JsonObject MarshalerSize(Type type)
    {
        if (!type.IsValueType || type == typeof(void) || type.ContainsGenericParameters || type.IsByRefLike)
            return Unavailable("not a closed non-byref-like value type");
        try { return new JsonObject { ["status"] = "measured", ["sizeBytes"] = Marshal.SizeOf(type), ["source"] = "Marshal.SizeOf; not raw storage" }; }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        { return Unavailable("Marshal.SizeOf rejected this shape: " + error.GetType().Name); }
    }

    private static JsonObject MarshalerOffset(FieldInfo field)
    {
        if (field.IsStatic || field.DeclaringType!.ContainsGenericParameters || field.DeclaringType.IsByRefLike)
            return Unavailable("not an instance field on a closed non-byref-like value type");
        try { return new JsonObject { ["status"] = "measured", ["offsetBytes"] = (long)Marshal.OffsetOf(field.DeclaringType!, field.Name), ["source"] = "Marshal.OffsetOf; not raw storage" }; }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        { return Unavailable("Marshal.OffsetOf rejected this shape: " + error.GetType().Name); }
    }

    private static JsonObject StorageWidth(Type type)
    {
        if (type.ContainsGenericParameters) return Unavailable("open generic shape");
        if (type.IsByRef) return new JsonObject { ["status"] = "measured", ["sizeBytes"] = IntPtr.Size, ["source"] = "managed reference storage" };
        if (!type.IsValueType && !type.IsPointer && !type.IsFunctionPointer)
            return new JsonObject { ["status"] = "measured", ["sizeBytes"] = IntPtr.Size, ["source"] = "object reference slot; not an unmanaged object size" };
        JsonObject measurement = Measurement(type);
        if (measurement["status"]!.GetValue<string>() != "measured") return measurement;
        return new JsonObject { ["status"] = "measured", ["sizeBytes"] = measurement["sizeBytes"]!.DeepClone(), ["source"] = "raw managed storage, no marshalling" };
    }

    internal static JsonObject FieldOffset(FieldInfo field)
    {
        if (field.IsStatic) return Unavailable("static field; not instance storage");
        Type owner = field.DeclaringType!;
        if (owner.ContainsGenericParameters) return Unavailable("open generic shape");
        if (owner.IsByRefLike) return Unavailable("byref-like owner; runtime offset helper not supported");
        if (!owner.IsValueType) return Unavailable("reference-type owner");
        // Local initobj/ldflda only: never call external constructors/getters or read static data.
        var method = new DynamicMethod("ManagedFieldOffset", typeof(long), Type.EmptyTypes, typeof(BindingContractExporter).Module, skipVisibility: true);
        ILGenerator il = method.GetILGenerator();
        LocalBuilder local = il.DeclareLocal(owner);
        il.Emit(OpCodes.Ldloca, local); il.Emit(OpCodes.Initobj, owner);
        il.Emit(OpCodes.Ldloca, local); il.Emit(OpCodes.Ldflda, field); il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Ldloca, local); il.Emit(OpCodes.Conv_I); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Ret);
        long offset = ((Func<long>)method.CreateDelegate(typeof(Func<long>)))();
        if (offset < 0) throw new InvalidDataException("Negative observed field offset.");
        return new JsonObject { ["status"] = "measured", ["offsetBytes"] = offset, ["source"] = "ldflda minus value-local address; no marshaler" };
    }

    private static JsonArray Overlaps(JsonArray fields)
    {
        var output = new JsonArray();
        var measured = fields.OfType<JsonObject>().Where(field =>
            field["runtimeOffset"]!["status"]!.GetValue<string>() == "measured" &&
            field["storageWidth"]!["status"]!.GetValue<string>() == "measured").ToArray();
        for (int i = 0; i < measured.Length; ++i)
        for (int j = i + 1; j < measured.Length; ++j)
        {
            long a = measured[i]["runtimeOffset"]!["offsetBytes"]!.GetValue<long>();
            long b = measured[j]["runtimeOffset"]!["offsetBytes"]!.GetValue<long>();
            long endA = a + measured[i]["storageWidth"]!["sizeBytes"]!.GetValue<int>();
            long endB = b + measured[j]["storageWidth"]!["sizeBytes"]!.GetValue<int>();
            if (a < endB && b < endA)
                output.Add(new JsonObject { ["firstField"] = measured[i]["name"]!.DeepClone(), ["secondField"] = measured[j]["name"]!.DeepClone(),
                    ["startBytes"] = Math.Max(a, b), ["endBytesExclusive"] = Math.Min(endA, endB), ["nativeMeaning"] = "unresolved" });
        }
        return output;
    }

    private JsonArray DynamicExports(Assembly binding)
    {
        // Supplement derived from the public bridge declaration. Keep distinct from imports.
        Type viewport = binding.GetType("Brutal.ImGuiApi.ImGuiViewportPtr", throwOnError: true)!;
        Type vector = types.Values.Single(type => type.FullName == "Brutal.Numerics.float2");
        var result = new JsonArray();
        foreach (string name in new[] { "GetWindowFramebufferScale", "GetWindowPos", "GetWindowSize" })
        {
            result.Add(new JsonObject
            {
                ["name"] = "Platform_" + name + "_ManagedFunctionPointer", ["kind"] = "writable-data",
                ["storageBytes"] = IntPtr.Size, ["library"] = "imgui", ["evidence"] = "reviewed bridge declaration supplement v1",
                ["callback"] = new JsonObject { ["callingConvention"] = "Cdecl", ["returnType"] = Id(vector), ["parameterTypes"] = Strings(new[] { Id(viewport) }) },
                ["ownership"] = "caller keeps callback alive while installed; writable slot lives with native library",
                ["nullCallbackBehavior"] = "unresolved; not inferred from prototype"
            });
            result.Add(new JsonObject
            {
                ["name"] = "Get_" + name + "_InteropPointer", ["kind"] = "function", ["library"] = "imgui",
                ["evidence"] = "reviewed bridge declaration supplement v1", ["callingConvention"] = "Cdecl",
                ["returnShape"] = "pointer-sized native trampoline address", ["parameterTypes"] = new JsonArray(),
                ["nativeTrampolineSignature"] = "ImVec2(ImGuiViewport*)"
            });
        }
        return result;
    }

    private static bool IsDelegate(Type type) => type.BaseType == typeof(MulticastDelegate);
    private static string? NamedString(CustomAttributeData attribute, string name) =>
        attribute.NamedArguments.Where(argument => argument.MemberName == name).Select(argument => argument.TypedValue.Value as string).SingleOrDefault();
    private static JsonObject Unavailable(string reason) => new() { ["status"] = "unavailable", ["reason"] = reason };
    private static JsonArray Strings(IEnumerable<string> strings) => new(strings.Select(text => (JsonNode)JsonValue.Create(text)!).ToArray());
    private void CountMember() { if (++members > MaximumMembers) throw new InvalidDataException("Member graph exceeds its bound."); }

    // Never bind to potentially stale bin/ assemblies, and never fall back to arbitrary
    // probing directories. Only explicit selected files and this runtime's TPA set are allowed.
    private sealed class SelectedAssemblies : AssemblyLoadContext, IDisposable
    {
        private readonly string directory;
        private readonly Dictionary<string, string> framework;
        private readonly Dictionary<string, (Assembly Assembly, string Hash, string Source)> loaded = new(StringComparer.Ordinal);
        private readonly SortedDictionary<string, Assembly> requested = new(StringComparer.Ordinal);

        internal SelectedAssemblies(string directory) : base("BindingContractSelection", isCollectible: true)
        {
            this.directory = directory;
            string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            framework = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("TPA list missing."))
                .Split(Path.PathSeparator).Where(path => Path.GetDirectoryName(path) == runtimeDirectory).Distinct(StringComparer.Ordinal)
                .ToDictionary(path => Path.GetFileNameWithoutExtension(path)!, path => path, StringComparer.OrdinalIgnoreCase);
        }

        internal Assembly LoadBinding()
        {
            string path = Path.Combine(directory, "Brutal.ImGui.dll");
            if (!File.Exists(path)) throw new FileNotFoundException("Selected Brutal.ImGui.dll is missing.");
            AssemblyName name = AssemblyName.GetAssemblyName(path);
            if (name.Name != "Brutal.ImGui") throw new InvalidDataException("Selected file is not Brutal.ImGui.");
            return LoadExact(path, "selected-directory");
        }

        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name is null || name.Name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
                throw new InvalidDataException("Invalid dependency name.");
            if (framework.TryGetValue(name.Name, out string? runtimePath))
            {
                Assembly runtimeAssembly = LoadExact(runtimePath, "runtime-framework", useDefault: true);
                requested[name.FullName] = runtimeAssembly;
                return runtimeAssembly;
            }
            string path = Path.Combine(directory, name.Name + ".dll");
            if (!File.Exists(path)) throw new FileNotFoundException("Selected dependency is missing: " + name.Name);
            Assembly result = LoadExact(path, "selected-directory");
            ValidateReference(name, result);
            requested[name.FullName] = result;
            return result;
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName) =>
            throw new InvalidOperationException("Native library loading is forbidden during metadata export.");

        private Assembly LoadExact(string path, string source, bool useDefault = false)
        {
            if (loaded.Count >= 256) throw new InvalidDataException("Metadata dependency closure exceeds 256 assemblies.");
            string hash = Hash(path);
            Assembly assembly = useDefault ? Default.LoadFromAssemblyPath(path) : LoadFromAssemblyPath(path);
            if (Hash(assembly.Location) != hash) throw new InvalidDataException("Loaded assembly does not match selected bytes.");
            if (loaded.TryGetValue(assembly.FullName!, out var previous) && previous.Hash != hash)
                throw new InvalidDataException("Conflicting assembly bytes for one identity.");
            loaded[assembly.FullName!] = (assembly, hash, source);
            return assembly;
        }

        private void ValidateReference(AssemblyName reference, Assembly assembly)
        {
            if (GetLoadContext(assembly) == this && reference.FullName != assembly.FullName)
                throw new InvalidDataException("Selected dependency identity differs from requested metadata: " + reference.FullName);
        }

        internal JsonArray Identities(IEnumerable<Assembly> contributors)
        {
            foreach (Assembly contributor in contributors.Distinct())
                if (!loaded.ContainsKey(contributor.FullName!))
                {
                    if (GetLoadContext(contributor) != Default || !framework.ContainsValue(contributor.Location))
                        throw new InvalidDataException("Type contributor resolved outside the selected/runtime closure.");
                    loaded[contributor.FullName!] = (contributor, Hash(contributor.Location), "runtime-framework");
                }
            // Runtime caching may satisfy a later request without calling Load again. Reconcile
            // all declared references that resolve to contributors, not just load callbacks.
            foreach (var item in loaded.Values.ToArray())
            foreach (AssemblyName reference in item.Assembly.GetReferencedAssemblies())
            {
                var matches = loaded.Values.Where(value => value.Assembly.GetName().Name == reference.Name).ToArray();
                if (matches.Length > 1) throw new InvalidDataException("Ambiguous assembly simple name in metadata closure.");
                if (matches.Length == 1)
                {
                    ValidateReference(reference, matches[0].Assembly);
                    requested[reference.FullName] = matches[0].Assembly;
                }
            }
            return new JsonArray(loaded.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            {
                Assembly assembly = pair.Value.Assembly;
                string? VersionAttribute(string name) => assembly.CustomAttributes.SingleOrDefault(attribute => attribute.AttributeType.Name == name)
                    ?.ConstructorArguments.Single().Value as string;
                return (JsonNode)new JsonObject
                {
                    ["name"] = assembly.GetName().Name, ["fullName"] = assembly.FullName,
                    ["version"] = assembly.GetName().Version?.ToString(),
                    ["fileVersion"] = VersionAttribute(nameof(AssemblyFileVersionAttribute)),
                    ["informationalVersion"] = VersionAttribute(nameof(AssemblyInformationalVersionAttribute)),
                    ["sha256"] = pair.Value.Hash, ["moduleMvid"] = assembly.ManifestModule.ModuleVersionId.ToString("D"),
                    ["fileName"] = Path.GetFileName(assembly.Location), ["resolutionSource"] = pair.Value.Source,
                    ["requestedIdentities"] = Strings(requested.Where(item => item.Value == assembly).Select(item => item.Key)),
                    ["references"] = Strings(assembly.GetReferencedAssemblies().Select(reference => reference.FullName).Order(StringComparer.Ordinal))
                };
            }).ToArray());
        }

        internal void VerifyUnchanged()
        {
            foreach (var value in loaded.Values)
                if (Hash(value.Assembly.Location) != value.Hash) throw new InvalidDataException("Assembly changed during export.");
        }
        public void Dispose() => Unload();
        private static string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
    }
}
