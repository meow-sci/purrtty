// No game types in this file (see Program.cs).

using System.Globalization;
using System.Net;

namespace purrTTY.Kfs.Cli;

/// <summary>A usage error: printed with the command's name, exit code 2.</summary>
internal sealed class UsageException(string message) : Exception(message);

/// <summary>What the process gives the tool: environment variables and the current directory (injectable for tests).</summary>
/// <param name="GetVariable">Reads an environment variable (null when unset).</param>
/// <param name="CurrentDirectory">The current directory: the default workspace.</param>
public sealed record CliEnvironment(Func<string, string?> GetVariable, string CurrentDirectory)
{
    /// <summary>The real process environment.</summary>
    public static CliEnvironment Process => new(Environment.GetEnvironmentVariable, Directory.GetCurrentDirectory())
    {
        OpenStandardInput = Console.OpenStandardInput,
        OpenStandardOutput = Console.OpenStandardOutput,
    };

    /// <summary>The process's standard input as a stream (<c>lsp --stdio</c>); null when there is none.</summary>
    public Func<Stream>? OpenStandardInput { get; init; }

    /// <summary>The process's raw standard output (<c>lsp --stdio</c> writes the protocol to it and nothing else); null when there is none.</summary>
    public Func<Stream>? OpenStandardOutput { get; init; }

    /// <summary>Cancelled to stop a long-running command (<c>lsp</c>): Ctrl+C or SIGTERM in the process, the test's own source in tests.</summary>
    public CancellationToken Cancellation { get; init; }
}

/// <summary>What <c>purr-kfs lsp</c> serves and how (no game types).</summary>
/// <param name="Workspace">The workspace folder, absolute.</param>
/// <param name="Stdio">True for <c>--stdio</c>, false for <c>--listen</c>.</param>
/// <param name="Address">The loopback address to listen on (<c>--listen</c>).</param>
/// <param name="Port">The port (0 picks one).</param>
/// <param name="Token">The token a client must send, or null (<c>--stdio</c> only: <c>--listen</c> always has one).</param>
/// <param name="TokenGenerated">True when <c>--listen</c> got no token and made one (it is printed on stdout).</param>
/// <param name="Layout">Explicit layout counts (thrusters, engines, gimbals) from the flags, or null.</param>
/// <param name="Verbose">Log debug lines too.</param>
internal sealed record LspOptions(string Workspace, bool Stdio, IPAddress Address, int Port, string? Token,
    (int Thrusters, int Engines, int Gimbals)? Layout, bool Verbose, bool TokenGenerated = false)
{
    /// <summary>The environment variable a token may come from (instead of <c>--token</c>, which <c>ps</c> shows).</summary>
    public const string TokenVariable = "PURR_KFS_LSP_TOKEN";

    public static LspOptions From(ParsedArgs parsed, string currentDirectory, Func<string, string?>? getVariable = null)
    {
        parsed.NoPositionals();
        bool stdio = parsed.Has("--stdio");
        string? listen = parsed.Value("--listen");
        if (stdio == (listen is not null)) throw new UsageException("give exactly one of --stdio and --listen <host>:<port>");
        string? workspace = parsed.Value("--workspace");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace ?? currentDirectory, currentDirectory));
        if (!Directory.Exists(root)) throw new UsageException($"workspace '{workspace ?? root}' is not a folder");
        (IPAddress address, int port) = listen is null ? (IPAddress.Loopback, 0) : Endpoint(listen);
        string? token = ResolveToken(parsed, currentDirectory, stdio ? null : getVariable);
        bool generated = false;
        if (token is null && !stdio)
        {
            // A TCP port on loopback is reachable by every local process (and a browser's requests):
            // never serve it without a token. Made here, printed on stdout after LISTENING.
            token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            generated = true;
        }
        (int, int, int)? layout = null;
        if (parsed.Has("--thrusters") || parsed.Has("--engines") || parsed.Has("--gimbals"))
        {
            VehicleOptions v = parsed.Vehicle();
            layout = (v.Thrusters, v.Engines, v.Gimbals);
        }
        return new LspOptions(root, stdio, address, port, token, layout, parsed.Has("--verbose"), generated);
    }

    /// <summary>The token from <c>--token</c>, <c>--token-file</c> (its first line) or <see cref="TokenVariable"/> (<c>--listen</c> only: a stdio client owns the pipes), in that order; at most one of the two options.</summary>
    private static string? ResolveToken(ParsedArgs parsed, string currentDirectory, Func<string, string?>? getVariable)
    {
        string? token = parsed.Value("--token");
        string? file = parsed.Value("--token-file");
        if (token is not null && file is not null) throw new UsageException("give at most one of --token and --token-file");
        if (token is { Length: 0 }) throw new UsageException("--token needs a non-empty value");
        if (token is not null) return token;
        if (file is not null)
        {
            string full = Path.GetFullPath(file, currentDirectory);
            string text;
            try
            {
                text = purrTTY.Kfs.Runtime.BoundedFile.ReadText(full, 4096);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                throw new UsageException($"--token-file: cannot read '{file}' ({e.Message})");
            }
            string first = text.Split('\n', 2)[0].Trim();
            if (first.Length == 0) throw new UsageException($"--token-file: '{file}' holds no token (its first line is empty)");
            return first;
        }
        string? fromEnvironment = getVariable?.Invoke(TokenVariable)?.Trim();
        return string.IsNullOrEmpty(fromEnvironment) ? null : fromEnvironment;
    }

    /// <summary><c>127.0.0.1:0</c>, <c>localhost:4711</c>, <c>[::1]:0</c>: a loopback address and a port.</summary>
    public static (IPAddress Address, int Port) Endpoint(string text)
    {
        int colon = text.LastIndexOf(':');
        if (colon <= 0 || colon == text.Length - 1) throw new UsageException($"--listen takes <host>:<port> (127.0.0.1:0 picks a free port), not '{text}'");
        string host = text[..colon].Trim('[', ']');
        string portText = text[(colon + 1)..];
        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port > IPEndPoint.MaxPort)
            throw new UsageException($"--listen: '{portText}' is not a port (0 to {IPEndPoint.MaxPort})");
        IPAddress address;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) address = IPAddress.Loopback;
        else if (!IPAddress.TryParse(host, out address!)) throw new UsageException($"--listen: '{host}' is not an IP address (use 127.0.0.1)");
        if (!IPAddress.IsLoopback(address)) throw new UsageException($"--listen: the language server listens on loopback only (127.0.0.1 or ::1), not {host}");
        return (address, port);
    }
}

/// <summary>The simulated vehicle a build or run targets (shorthand flags, plan §5.6).</summary>
internal sealed record VehicleOptions(int Lights, int Deployables, int Actuators, int Thrusters, int Engines, int Gimbals)
{
    public const string Name = "sim";
    public const int MaxCount = 256;
}

/// <summary>The command line, split into a command, positional arguments and options.</summary>
internal sealed class ParsedArgs
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.Ordinal);
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);

    public string Command { get; private init; } = "";
    public List<string> Positionals { get; } = [];

    /// <summary>
    /// Splits <paramref name="args"/> for <paramref name="command"/>: <paramref name="flags"/> take
    /// no value, <paramref name="valued"/> take one (<c>--x v</c> or <c>--x=v</c>); anything else that
    /// starts with <c>-</c> is an error; <c>--</c> ends the options.
    /// </summary>
    public static ParsedArgs Parse(string command, IEnumerable<string> args, IReadOnlyCollection<string> flags, IReadOnlyCollection<string> valued)
    {
        var parsed = new ParsedArgs { Command = command };
        var queue = new Queue<string>(args);
        bool optionsEnded = false;
        while (queue.Count > 0)
        {
            string arg = queue.Dequeue();
            if (optionsEnded || arg.Length < 2 || arg[0] != '-')
            {
                parsed.Positionals.Add(arg);
                continue;
            }
            if (arg == "--")
            {
                optionsEnded = true;
                continue;
            }
            string name = arg;
            string? value = null;
            int equals = arg.IndexOf('=', StringComparison.Ordinal);
            if (arg.StartsWith("--", StringComparison.Ordinal) && equals > 2)
            {
                name = arg[..equals];
                value = arg[(equals + 1)..];
            }
            if (name == "-h") name = "--help";

            if (flags.Contains(name))
            {
                if (value is not null) throw new UsageException($"{name} takes no value");
                parsed.Set(name, null);
            }
            else if (valued.Contains(name))
            {
                if (value is null)
                {
                    // "--lights --json" is a forgotten value, not the value "--json".
                    if (queue.Count == 0 || queue.Peek().StartsWith("--", StringComparison.Ordinal)) throw new UsageException($"{name} needs a value");
                    value = queue.Dequeue();
                }
                parsed.Set(name, value);
            }
            else
            {
                throw new UsageException($"unknown option '{name}'");
            }
        }
        return parsed;
    }

    private void Set(string name, string? value)
    {
        if (!_used.Add(name)) throw new UsageException($"{name} given twice");
        _options[name] = value;
    }

    public bool Has(string name) => _options.ContainsKey(name);

    public string? Value(string name) => _options.TryGetValue(name, out string? v) ? v : null;

    public int Int(string name, int fallback, int min, int max)
    {
        string? text = Value(name);
        if (text is null) return fallback;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < min || n > max)
            throw new UsageException($"{name} takes a whole number from {min} to {max}, not '{text}'");
        return n;
    }

    public VehicleOptions Vehicle() => new(
        Int("--lights", 0, 0, VehicleOptions.MaxCount),
        Int("--deployables", 0, 0, VehicleOptions.MaxCount),
        Int("--actuators", 0, 0, VehicleOptions.MaxCount),
        Int("--thrusters", 1, 1, VehicleOptions.MaxCount),
        Int("--engines", 0, 0, VehicleOptions.MaxCount),
        Int("--gimbals", 0, 0, VehicleOptions.MaxCount));

    /// <summary>The one positional argument a command takes (its file).</summary>
    public string SinglePositional(string what)
    {
        if (Positionals.Count == 0) throw new UsageException($"missing {what}");
        if (Positionals.Count > 1) throw new UsageException($"unexpected argument '{Positionals[1]}'");
        return Positionals[0];
    }

    public void NoPositionals()
    {
        if (Positionals.Count > 0) throw new UsageException($"unexpected argument '{Positionals[0]}'");
    }
}

/// <summary>A file named on the command line, located in its workspace.</summary>
/// <param name="Workspace">The workspace folder, absolute.</param>
/// <param name="Path">The file, workspace-relative with forward slashes.</param>
internal sealed record WorkspaceFile(string Workspace, string Path)
{
    /// <summary>
    /// The workspace is <c>--workspace</c> (relative to the current directory) or the current
    /// directory; a relative <paramref name="file"/> is taken relative to the workspace (as the
    /// game's <c>kfs build proj/main.c</c>), an absolute one must lie inside it. The file must exist.
    /// </summary>
    public static WorkspaceFile Locate(string file, string? workspace, string currentDirectory)
    {
        string root = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(workspace ?? currentDirectory, currentDirectory));
        if (!Directory.Exists(root)) throw new UsageException($"workspace '{workspace ?? root}' is not a folder");
        string full = System.IO.Path.GetFullPath(file, root);
        if (Directory.Exists(full)) throw new UsageException($"'{file}' is a folder; name a .c or .kasm file in it");
        if (!File.Exists(full)) throw new UsageException($"'{file}': no such file in the workspace {root}");
        string relative = System.IO.Path.GetRelativePath(root, full).Replace('\\', '/');
        if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative))
            throw new UsageException($"'{file}' is outside the workspace {root} (pass --workspace <dir>)");
        return new WorkspaceFile(root, relative);
    }
}

/// <summary>Parsing of <c>--for</c> durations and <c>--frame</c> rates.</summary>
internal static class Units
{
    /// <summary><c>10s</c>, <c>500ms</c>, <c>1.5s</c>, <c>2m</c>, or a bare number of seconds; 0 &lt; d ≤ 1 day.</summary>
    public static double Seconds(string option, string text)
    {
        string t = text.Trim();
        double scale = 1;
        if (t.EndsWith("ms", StringComparison.Ordinal)) { scale = 0.001; t = t[..^2]; }
        else if (t.EndsWith('s')) { t = t[..^1]; }
        else if (t.EndsWith('m')) { scale = 60; t = t[..^1]; }
        if (!double.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double v) || !double.IsFinite(v) || v * scale <= 0 || v * scale > 86400)
            throw new UsageException($"{option} takes a duration such as 10s, 500ms or 2m (more than 0, at most 24 h), not '{text}'");
        return v * scale;
    }

    /// <summary>A frame rate in Hz, 1 to 1000.</summary>
    public static double Hz(string option, string text)
    {
        string t = text.Trim();
        if (t.EndsWith("hz", StringComparison.OrdinalIgnoreCase)) t = t[..^2];
        if (!double.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double v) || !double.IsFinite(v) || v < 1 || v > 1000)
            throw new UsageException($"{option} takes a frame rate in Hz from 1 to 1000, not '{text}'");
        return v;
    }
}
