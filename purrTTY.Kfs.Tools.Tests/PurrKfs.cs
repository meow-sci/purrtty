using purrTTY.Kfs.Cli;

namespace purrTTY.Kfs.Tools.Tests;

/// <summary>What one <see cref="CliApp.Run(string[], TextWriter, TextWriter, CliEnvironment)"/> call returned and printed.</summary>
internal sealed record CliOutcome(int ExitCode, string Stdout, string Stderr)
{
    /// <summary>For failure messages.</summary>
    public override string ToString() => $"exit {ExitCode}\n--- stdout ---\n{Stdout}--- stderr ---\n{Stderr}";
}

/// <summary>Runs <c>purr-kfs</c> in process with <c>\n</c> line ends and a controlled environment.</summary>
internal static class PurrKfs
{
    /// <summary>
    /// Runs the tool with <paramref name="args"/> in <paramref name="currentDirectory"/> (default: the
    /// temp folder), with <c>KSA_DLL_DIR</c> set to <paramref name="ksaDllDir"/> (default: the
    /// directory the test assembly's resolver was installed with; null in the environment when that is null).
    /// </summary>
    public static CliOutcome Run(string[] args, string? currentDirectory = null, string? ksaDllDir = null, bool noGame = false)
    {
        string? dir = noGame ? null : ksaDllDir ?? ModuleInit.Directory;
        var environment = new CliEnvironment(name => name == "KSA_DLL_DIR" ? dir : null, currentDirectory ?? Path.GetTempPath());
        using var stdout = new StringWriter { NewLine = "\n" };
        using var stderr = new StringWriter { NewLine = "\n" };
        int code = CliApp.Run(args, stdout, stderr, environment);
        return new CliOutcome(code, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Runs the tool on a workspace (<c>--workspace</c> added).</summary>
    public static CliOutcome In(TempWorkspace workspace, params string[] args) =>
        Run([.. args, "--workspace", workspace.Root]);
}
