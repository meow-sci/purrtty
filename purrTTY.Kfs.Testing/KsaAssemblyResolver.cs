// RULE FOR THIS FILE: no game type (KSA.*, Brutal.*, Planet.*) may appear anywhere in it — not in
// a signature, a field, a local, an attribute or a generic argument. The resolver has to run
// before the first game type is touched, and the runtime loads every type a method mentions
// when it JIT-compiles that method; a game type here would ask for KSA.dll before the handler
// that supplies it exists. Keep this class free of them and keep it in its own file.

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace purrTTY.Kfs.Testing;

/// <summary>
/// Loads the game's assemblies (KSA.dll, Brutal.*, Planet.*) outside the game from a directory
/// holding the developer's or player's own copy. Projects reference them with
/// <c>Private=false</c>, so they are never in an output folder and every request for one
/// reaches the handler installed here.
/// </summary>
/// <remarks>
/// <para>
/// <c>KSA.dll</c> is IL-only but its PE header declares machine x64 (<c>0x8664</c>). An x64
/// process loads it as is. An arm64 process refuses it, so on arm64 the resolver reads the file
/// and changes the two machine bytes to ARM64 (<c>0xAA64</c>) <b>in memory</b> before
/// <see cref="AssemblyLoadContext.LoadFromStream(Stream)"/>. Nothing is copied and nothing on
/// disk is modified.
/// </para>
/// <para>
/// Every other assembly (all of them on x64 hosts) is loaded with
/// <see cref="AssemblyLoadContext.LoadFromAssemblyPath(string)"/>: the runtime maps the file
/// itself, <see cref="Assembly.Location"/> stays set, and debuggers and stack traces find the
/// file — a stream-loaded assembly has none of that.
/// </para>
/// </remarks>
public static class KsaAssemblyResolver
{
    /// <summary>Environment variable naming the directory that holds <c>KSA.dll</c>.</summary>
    public const string EnvironmentVariable = "KSA_DLL_DIR";

    /// <summary>
    /// Name of the <see cref="AssemblyMetadataAttribute"/> a test assembly is stamped with at
    /// build time, holding the MSBuild <c>$(KSAFolder)</c> it was compiled against.
    /// </summary>
    public const string MetadataKey = "KSAFolder";

    private const ushort MachineAmd64 = 0x8664;
    private const ushort MachineArm64 = 0xAA64;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, string> ResolvedFiles = new(StringComparer.OrdinalIgnoreCase);
    private static string? _directory;

    /// <summary>The directory the resolver was installed with, or null before <see cref="Install"/>.</summary>
    public static string? InstalledDirectory
    {
        get { lock (Gate) return _directory; }
    }

    /// <summary>
    /// Simple name → file path of every assembly this resolver has supplied so far.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Resolved
    {
        get { lock (Gate) return new Dictionary<string, string>(ResolvedFiles, StringComparer.OrdinalIgnoreCase); }
    }

    /// <summary>
    /// Registers the resolving handler for <paramref name="ksaDllDirectory"/>. Idempotent: a
    /// second call with the same directory does nothing; a call with a different directory throws,
    /// because assemblies already loaded from the first cannot be replaced.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    /// <exception cref="FileNotFoundException">The directory has no <c>KSA.dll</c>.</exception>
    public static void Install(string ksaDllDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ksaDllDirectory);
        string directory = Path.GetFullPath(ksaDllDirectory);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"KSA game assemblies not found: directory '{directory}' does not exist. {HowToConfigure}");
        }

        if (!File.Exists(Path.Combine(directory, "KSA.dll")))
        {
            throw new FileNotFoundException(
                $"KSA game assemblies not found: '{directory}' has no KSA.dll. {HowToConfigure}",
                Path.Combine(directory, "KSA.dll"));
        }

        lock (Gate)
        {
            if (_directory is not null)
            {
                if (SameDirectory(_directory, directory)) return;
                throw new InvalidOperationException(
                    $"KsaAssemblyResolver is already installed for '{_directory}'; it cannot switch to '{directory}' in the same process.");
            }

            _directory = directory;
            // Default is the right context: console programs, and the dotnet test host (which
            // loads the test assembly, NUnit and the game assemblies there), all bind in it.
            AssemblyLoadContext.Default.Resolving += OnResolving;
        }
    }

    /// <summary>
    /// Finds the game assembly directory without an explicit argument: the
    /// <c>KSA_DLL_DIR</c> environment variable when set, otherwise the <c>KSAFolder</c>
    /// assembly metadata of the calling assembly when that directory exists. Returns null when
    /// neither is configured (a contributor without the game files).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string? FindDirectory() => FindDirectory(Assembly.GetCallingAssembly());

    /// <summary>
    /// As <see cref="FindDirectory()"/>, reading the build-time metadata from
    /// <paramref name="stampedAssembly"/>.
    /// </summary>
    public static string? FindDirectory(Assembly stampedAssembly)
    {
        ArgumentNullException.ThrowIfNull(stampedAssembly);

        string? fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment;

        // Directory.Build.props always gives KSAFolder a value (it falls back to a per-OS
        // default), so a stamped folder only counts as configured when it exists.
        string? stamped = stampedAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == MetadataKey)?.Value;
        if (!string.IsNullOrWhiteSpace(stamped) && Directory.Exists(stamped)) return stamped;

        return null;
    }

    private const string HowToConfigure =
        "Set the KSA_DLL_DIR environment variable to the folder that holds KSA.dll, or build the test " +
        "project with MSBuild's KSAFolder pointing at it (KSA_DLL_DIR at build time, or a ksa-game-assemblies " +
        "checkout next to this repository; see Directory.Build.props), which stamps it into the test assembly " +
        "as [AssemblyMetadata(\"KSAFolder\", ...)].";

    private static bool SameDirectory(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static Assembly? OnResolving(AssemblyLoadContext context, AssemblyName name)
    {
        string? directory = _directory;
        if (directory is null || string.IsNullOrEmpty(name.Name)) return null;

        string path = Path.Combine(directory, name.Name + ".dll");
        if (!File.Exists(path)) return null;

        lock (Gate)
        {
            // Two threads can miss the same assembly at once; the second must get the first's
            // copy rather than load the file twice into one context.
            foreach (Assembly loaded in context.Assemblies)
            {
                if (string.Equals(loaded.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase)) return loaded;
            }

            Assembly assembly = Load(context, path);
            ResolvedFiles[name.Name] = path;
            return assembly;
        }
    }

    private static Assembly Load(AssemblyLoadContext context, string path)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.Arm64) return context.LoadFromAssemblyPath(path);

        byte[] image = File.ReadAllBytes(path);
        if (!TryGetMachineOffset(image, out int machineOffset)
            || BitConverter.ToUInt16(image, machineOffset) != MachineAmd64)
        {
            return context.LoadFromAssemblyPath(path);
        }

        // IL-only image marked x64: retarget the in-memory copy to ARM64. The file is untouched.
        image[machineOffset] = MachineArm64 & 0xFF;
        image[machineOffset + 1] = MachineArm64 >> 8;
        using var stream = new MemoryStream(image, writable: false);
        return context.LoadFromStream(stream);
    }

    /// <summary>Offset of IMAGE_FILE_HEADER.Machine, after checking the DOS and PE signatures.</summary>
    private static bool TryGetMachineOffset(byte[] image, out int offset)
    {
        offset = 0;
        if (image.Length < 0x40 || image[0] != (byte)'M' || image[1] != (byte)'Z') return false;

        int pe = BitConverter.ToInt32(image, 0x3C);                    // e_lfanew
        if (pe < 0 || pe > image.Length - 6) return false;
        if (image[pe] != (byte)'P' || image[pe + 1] != (byte)'E' || image[pe + 2] != 0 || image[pe + 3] != 0) return false;

        offset = pe + 4;
        return true;
    }
}
