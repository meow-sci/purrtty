using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ghostty.Vt.Native;

/// <summary>
/// Resolves the native <c>ghostty-vt</c> library next to the <see cref="Ghostty.Vt"/>
/// assembly. This is required inside hosts (e.g. KSA's plugin
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>) where the default
/// probing logic does not reliably search the assembly directory or
/// <c>runtimes/&lt;rid&gt;/native</c>.
/// </summary>
/// <remarks>
/// purrtty addition (not in upstream binding). Registered via a module
/// initializer so it is always active the moment the binding is first used,
/// regardless of which ALC loaded it.
/// </remarks>
internal static class NativeLibraryResolver
{
    private const string LibraryName = "ghostty-vt";

    private static int _registered;

    [ModuleInitializer]
    internal static void Register()
    {
        // Idempotent: SetDllImportResolver throws if called twice for one assembly.
        if (Interlocked.Exchange(ref _registered, 1) != 0)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            return nint.Zero;
        }

        var fileName = PlatformFileName();
        var assemblyDir = Path.GetDirectoryName(assembly.Location);

        foreach (var candidate in CandidatePaths(assemblyDir, fileName))
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        // Fall back to the OS loader's default search (handles cases where the
        // library is installed system-wide or already resolvable by name).
        return NativeLibrary.TryLoad(libraryName, assembly, searchPath, out var fallback)
            ? fallback
            : nint.Zero;
    }

    private static IEnumerable<string> CandidatePaths(string? assemblyDir, string fileName)
    {
        if (string.IsNullOrEmpty(assemblyDir))
        {
            yield break;
        }

        // 1. Directly beside the assembly (our default copy target).
        yield return Path.Combine(assemblyDir, fileName);

        // 2. NuGet-style runtimes/<rid>/native layout, in case a packaged copy lands there.
        var rid = RuntimeInformation.RuntimeIdentifier;
        yield return Path.Combine(assemblyDir, "runtimes", rid, "native", fileName);
    }

    private static string PlatformFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "ghostty-vt.dll";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "libghostty-vt.dylib";
        }

        return "libghostty-vt.so";
    }
}
