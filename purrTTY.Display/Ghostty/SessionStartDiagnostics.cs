using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using purrTTY.Logging;

namespace purrTTY.Display.Ghostty;

/// <summary>Best-effort diagnostics for SSH/crypto dependency failures during session startup.</summary>
internal static class SessionStartDiagnostics
{
    private static readonly string[] DependencyNames =
        ["Renci.SshNet", "BouncyCastle.Cryptography", "System.Security.Cryptography"];

    internal static void LogLoadedAssemblies()
    {
        try
        {
            ModLog.Log.Error($"Session startup runtime: {RuntimeInformation.FrameworkDescription}; "
                + $"OS={RuntimeInformation.OSDescription}; process={RuntimeInformation.ProcessArchitecture}");

            // Inspect only loaded assemblies. Loading a dependency here could change the
            // binding that caused the failure, or trigger another failed type initializer.
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var dependencyName in DependencyNames)
            {
                var found = false;
                foreach (var assembly in assemblies)
                {
                    var name = assembly.GetName();
                    if (!string.Equals(name.Name, dependencyName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    found = true;
                    var context = AssemblyLoadContext.GetLoadContext(assembly);
                    var contextId = context is null
                        ? "<unknown>"
                        : $"{context.Name ?? "<unnamed>"}#{RuntimeHelpers.GetHashCode(context):x}";
                    try
                    {
                        var location = assembly.Location;
                        var fileVersion = string.IsNullOrEmpty(location)
                            ? "<unavailable>"
                            : FileVersionInfo.GetVersionInfo(location).FileVersion ?? "<unavailable>";
                        ModLog.Log.Error($"Session startup assembly: {name.FullName}; fileVersion={fileVersion}; "
                            + $"path={location}; loadContext={contextId}");
                    }
                    catch (Exception ex)
                    {
                        ModLog.Log.Error($"Session startup assembly: {name.FullName}; loadContext={contextId}; "
                            + $"file metadata unavailable: {ex.Message}");
                    }
                }

                if (!found)
                    ModLog.Log.Error($"Session startup assembly: {dependencyName}: not loaded");
            }
        }
        catch (Exception ex)
        {
            // Failure diagnostics must not prevent the controller from notifying the window.
            ModLog.Log.Warning($"Could not collect session startup assembly diagnostics: {ex.Message}");
        }
    }
}
