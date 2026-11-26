// No game types in this file (see Program.cs).

using System.Reflection;
using System.Runtime.CompilerServices;
using purrTTY.Kfs.Cli.Commands;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Cli;

/// <summary>
/// <c>purr-kfs</c> (plan §5.6): parses the command line, locates and installs the game's assemblies
/// for the commands that need them (<c>check</c>, <c>run</c>), and dispatches. Output is
/// deterministic (no timestamps, stable order, invariant culture); diagnostics and results go to
/// <c>stdout</c>, usage and environment problems to <c>stderr</c>. Exit codes: 0 success, 1 build
/// errors or a trapped program, 2 usage or environment problem. Nothing is written to disk.
/// </summary>
/// <remarks>
/// A host process (a test) may already own the process's <see cref="KfsRuntime"/>; the commands then
/// use it and leave it running, otherwise they start one and dispose it before returning.
/// </remarks>
public static class CliApp
{
    /// <summary>Exit code: success.</summary>
    public const int ExitOk = 0;

    /// <summary>Exit code: build errors, a trapped or faulted program.</summary>
    public const int ExitFailed = 1;

    /// <summary>Exit code: a usage or environment problem.</summary>
    public const int ExitUsage = 2;

    private static readonly string[] CommandNames = ["check", "run", "lsp", "header", "kinc", "services"];

    /// <summary>Runs the tool with the process environment.</summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr) =>
        Run(args, stdout, stderr, CliEnvironment.Process);

    /// <summary>Runs the tool with <paramref name="environment"/> standing in for the process's.</summary>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, CliEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(environment);

        if (args.Length == 0)
        {
            Lines(stderr, HelpText.General);
            return ExitUsage;
        }

        string first = args[0];
        if (first is "--version") return Print(stdout, "purr-kfs " + Version);
        if (first is "--help" or "-h" or "help")
        {
            if (args.Length > 1 && HelpFor(args[1]) is { } text) return Lines(stdout, text);
            if (args.Length > 1) return Usage(stderr, null, $"unknown command '{args[1]}'");
            return Lines(stdout, HelpText.General);
        }
        if (!CommandNames.Contains(first, StringComparer.Ordinal))
        {
            return first.StartsWith('-')
                ? Usage(stderr, null, $"unknown option '{first}' (options follow the command)")
                : Usage(stderr, null, $"unknown command '{first}'");
        }
        if (args.Skip(1).Any(a => a is "--help" or "-h")) return Lines(stdout, HelpFor(first)!);

        try
        {
            ParsedArgs parsed = first switch
            {
                "check" => ParsedArgs.Parse(first, args.Skip(1), ["--json"], ["--ksa", "--workspace", .. VehicleFlags]),
                "run" => ParsedArgs.Parse(first, args.Skip(1), ["--log", "--state", "--requests"], ["--ksa", "--workspace", "--for", "--frame", .. VehicleFlags]),
                "services" => ParsedArgs.Parse(first, args.Skip(1), ["--markdown"], ["--ksa"]),
                "lsp" => ParsedArgs.Parse(first, args.Skip(1), ["--stdio", "--verbose"], ["--ksa", "--workspace", "--listen", "--token", "--token-file", "--thrusters", "--engines", "--gimbals"]),
                _ => ParsedArgs.Parse(first, args.Skip(1), [], ["--ksa"]),
            };

            switch (first)
            {
                case "header":
                    parsed.NoPositionals();
                    return CatalogCommands.Header(stdout);
                case "kinc":
                    parsed.NoPositionals();
                    return CatalogCommands.Kinc(stdout);
                case "services":
                    parsed.NoPositionals();
                    return CatalogCommands.Services(stdout, parsed.Has("--markdown"));
            }

            if (first == "lsp")
            {
                LspOptions lsp = LspOptions.From(parsed, environment.CurrentDirectory, environment.GetVariable);
                if (InstallGame(parsed.Value("--ksa"), environment, stderr) is { } noGame) return noGame;
                return Serve(lsp, environment, stdout, stderr);
            }

            // check / run: validate everything that needs no game first, then the game.
            WorkspaceFile file = WorkspaceFile.Locate(parsed.SinglePositional("<file>"), parsed.Value("--workspace"), environment.CurrentDirectory);
            VehicleOptions vehicle = parsed.Vehicle();
            RunOptions? run = first == "run" ? RunOptions.From(parsed) : null;

            if (InstallGame(parsed.Value("--ksa"), environment, stderr) is { } failed) return failed;

            return run is null
                ? Check(file, vehicle, parsed.Has("--json"), stdout, stderr)
                : Execute(file, vehicle, run, stdout, stderr);
        }
        catch (UsageException e)
        {
            return Usage(stderr, first, e.Message);
        }
    }

    private static readonly string[] VehicleFlags = ["--lights", "--deployables", "--actuators", "--thrusters", "--engines", "--gimbals"];

    // Separate methods, never inlined: the command bodies mention game types, so they may be
    // JIT-compiled only after the resolver is installed.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Check(WorkspaceFile file, VehicleOptions vehicle, bool json, TextWriter stdout, TextWriter stderr) =>
        CheckCommand.Run(file, vehicle, json, stdout, stderr);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Execute(WorkspaceFile file, VehicleOptions vehicle, RunOptions run, TextWriter stdout, TextWriter stderr) =>
        RunCommand.Run(file, vehicle, run, stdout, stderr);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Serve(LspOptions options, CliEnvironment environment, TextWriter stdout, TextWriter stderr) =>
        LspCommand.Run(options, environment, stdout, stderr);

    /// <summary>
    /// Installs <see cref="KsaAssemblyResolver"/> for <c>--ksa</c> or <c>KSA_DLL_DIR</c>; null on
    /// success, else the exit code after printing one message.
    /// </summary>
    private static int? InstallGame(string? option, CliEnvironment environment, TextWriter stderr)
    {
        string? directory = option ?? environment.GetVariable(KsaAssemblyResolver.EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return Fail(stderr, "purr-kfs: the game's assemblies were not found. Pass --ksa <dir> or set the KSA_DLL_DIR " +
                "environment variable to the folder that holds KSA.dll (the game's install folder).");
        }
        string full = Path.GetFullPath(directory, environment.CurrentDirectory);
        string source = option is null ? "KSA_DLL_DIR" : "--ksa";
        if (!Directory.Exists(full)) return Fail(stderr, $"purr-kfs: {source} names '{directory}', which is not a folder (it must hold KSA.dll).");
        if (!File.Exists(Path.Combine(full, "KSA.dll"))) return Fail(stderr, $"purr-kfs: {source} names '{directory}', which has no KSA.dll.");
        try
        {
            KsaAssemblyResolver.Install(full);
            return null;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return Fail(stderr, "purr-kfs: " + e.Message);
        }
    }

    /// <summary>The tool's version (the informational version without build metadata).</summary>
    public static string Version
    {
        get
        {
            string? v = typeof(CliApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(v)) return typeof(CliApp).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            int plus = v.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? v[..plus] : v;
        }
    }

    private static string? HelpFor(string command) => command switch
    {
        "check" => HelpText.Check,
        "run" => HelpText.Run,
        "lsp" => HelpText.Lsp,
        "header" => HelpText.Header,
        "kinc" => HelpText.Kinc,
        "services" => HelpText.Services,
        _ => null,
    };

    private static int Usage(TextWriter stderr, string? command, string message)
    {
        string who = command is null ? "purr-kfs" : "purr-kfs " + command;
        stderr.WriteLine($"{who}: {message}");
        stderr.WriteLine($"Run '{who} --help' for usage.");
        return ExitUsage;
    }

    private static int Fail(TextWriter stderr, string message)
    {
        stderr.WriteLine(message);
        return ExitUsage;
    }

    private static int Print(TextWriter writer, string line)
    {
        writer.WriteLine(line);
        return ExitOk;
    }

    /// <summary>Writes <paramref name="text"/> line by line (the writer's own line ends).</summary>
    internal static int Lines(TextWriter writer, string text)
    {
        foreach (string line in text.TrimEnd('\n').Split('\n')) writer.WriteLine(line.TrimEnd('\r'));
        return ExitOk;
    }
}

/// <summary>What <c>run</c> prints and for how long it runs.</summary>
internal sealed record RunOptions(double Seconds, double FrameHz, bool Log, bool Requests, bool State)
{
    public static RunOptions From(ParsedArgs parsed)
    {
        double seconds = parsed.Value("--for") is { } d ? Units.Seconds("--for", d) : 10.0;
        double hz = parsed.Value("--frame") is { } f ? Units.Hz("--frame", f) : 60.0;
        bool log = parsed.Has("--log"), requests = parsed.Has("--requests"), state = parsed.Has("--state");
        if (!log && !requests && !state) log = requests = state = true;
        return new RunOptions(seconds, hz, log, requests, state);
    }
}
