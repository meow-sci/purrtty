using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KSA.Kfs.Host.Views;
using purrTTY.Kfs.Lsp.Protocol;

namespace purrTTY.Kfs.Lsp.Features;

/// <summary>The <c>workspace/executeCommand</c> commands of the kFS language server (plan §6.7).</summary>
public static class KfsCommands
{
    /// <summary><c>kfs.build [fileUri]</c> → <c>kfs build &lt;path&gt;</c>.</summary>
    public const string Build = "kfs.build";
    /// <summary><c>kfs.load [fileUri]</c> → <c>kfs load &lt;path&gt;</c>, with <c>--vehicle</c> when the target is a vehicle.</summary>
    public const string Load = "kfs.load";
    /// <summary><c>kfs.start []</c> → <c>kfs start</c>.</summary>
    public const string Start = "kfs.start";
    /// <summary><c>kfs.stop []</c> → <c>kfs stop</c>.</summary>
    public const string Stop = "kfs.stop";
    /// <summary><c>kfs.reset []</c> → <c>kfs reset</c>.</summary>
    public const string Reset = "kfs.reset";
    /// <summary><c>kfs.target ["vehicle" | "bench"]</c> → <c>kfs target vehicle</c>.</summary>
    public const string Target = "kfs.target";
    /// <summary><c>kfs.manual [code]</c> → <c>kfs manual KC-E003</c>.</summary>
    public const string Manual = "kfs.manual";
    /// <summary><c>kfs.parts []</c> or <c>["lights" | "deploy" | "actuators"]</c> → <c>purr parts [kind]</c>.</summary>
    public const string Parts = "kfs.parts";
    /// <summary><c>kfs.verb [line]</c> → <c>kfs &lt;line&gt;</c>; refused unless the configuration allows it.</summary>
    public const string Verb = "kfs.verb";

    /// <summary>Every command, in the order of the contract.</summary>
    public static IReadOnlyList<string> All { get; } = [Build, Load, Start, Stop, Reset, Target, Manual, Parts, Verb];
}

/// <summary>Where a command goes: a <c>kfs</c> line, a <c>purr</c> line, or nowhere (with why).</summary>
/// <param name="Kind">Which.</param>
/// <param name="Line">The argument line of <c>kfs</c> or <c>purr</c> (<c>load x_blink/blink.c --vehicle</c>).</param>
/// <param name="Error">Why nothing runs.</param>
public sealed record KfsCommandRoute(KfsCommandRouteKind Kind, string Line, string? Error)
{
    /// <summary>Runs <c>kfs &lt;line&gt;</c>.</summary>
    public static KfsCommandRoute Kfs(string line) => new(KfsCommandRouteKind.Kfs, line, null);

    /// <summary>Runs <c>purr &lt;line&gt;</c>.</summary>
    public static KfsCommandRoute Purr(string line) => new(KfsCommandRouteKind.Purr, line, null);

    /// <summary>Runs nothing.</summary>
    public static KfsCommandRoute Refuse(string error) => new(KfsCommandRouteKind.Refused, "", error);
}

/// <summary>What a <see cref="KfsCommandRoute"/> runs.</summary>
public enum KfsCommandRouteKind
{
    /// <summary>A <c>kfs</c> verb line (<c>KfsConsoleCommands.Execute</c> in game).</summary>
    Kfs,
    /// <summary>A <c>purr</c> verb line (the mod's own console command).</summary>
    Purr,
    /// <summary>Nothing runs; <see cref="KfsCommandRoute.Error"/> says why.</summary>
    Refused,
}

/// <summary>
/// The contract's commands → the exact stock verb lines (pure; plan §6.7). The stock parsers, read
/// from the decompiled game, decide the shapes:
/// <list type="bullet">
/// <item><c>KfsConsoleCommands.Execute</c> splits the verb at the first space and passes the rest,
/// trimmed at the start, to the verb (KfsConsoleCommands.cs:193-232).</item>
/// <item><c>build</c>: <c>Workbench.Build</c> takes the whole rest, <c>Trim()</c>-ed, as the file
/// (Workbench.cs:659-678) — a workspace path with spaces works as it is; quotes would become part
/// of the name, so none are added.</item>
/// <item><c>load</c>: <c>Workbench.Load</c> splits at every space (<c>--part n</c>, <c>--vehicle</c>,
/// else the file; Workbench.cs:739-807), so a path with a space cannot be loaded at all: refused.
/// <c>--vehicle</c> is added when the target is a vehicle, exactly as <c>LoadIntoTarget</c>
/// (Workbench.cs:939-942); without it a load onto a vehicle target would retarget the bench.</item>
/// <item><c>target</c>: <c>bench</c> | <c>vehicle</c> (Workbench.cs:944-971); <c>manual</c>: one code,
/// trimmed (Workbench.cs:2123-2133); <c>purr parts [lights | deploy | actuators]</c> (PurrBuiltinVerbs).</item>
/// </list>
/// Malformed arguments and files outside the workspace are refused: nothing runs.
/// </summary>
public static partial class KfsCommandLine
{
    /// <summary>The route of <paramref name="command"/> with <paramref name="arguments"/>.</summary>
    /// <param name="command">A <see cref="KfsCommands"/> name.</param>
    /// <param name="arguments">The request's <c>arguments</c> (null: none).</param>
    /// <param name="pathOf">A file URI → its workspace path, or null when it is not inside the workspace.</param>
    /// <param name="targetIsVehicle">Whether the build target is a vehicle (<c>kfs.load</c> adds <c>--vehicle</c>).</param>
    /// <param name="allowVerb">Whether <c>kfs.verb</c> is allowed (<c>lsp_allow_vehicle_commands</c>).</param>
    public static KfsCommandRoute Resolve(string command, JsonArray? arguments, Func<string, string?> pathOf, bool targetIsVehicle, bool allowVerb)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(pathOf);
        int count = arguments?.Count ?? 0;
        switch (command)
        {
            case KfsCommands.Build:
            {
                if (FileArgument(command, arguments, pathOf, out string path) is { } why) return KfsCommandRoute.Refuse(why);
                return KfsCommandRoute.Kfs("build " + path);
            }
            case KfsCommands.Load:
            {
                if (FileArgument(command, arguments, pathOf, out string path) is { } why) return KfsCommandRoute.Refuse(why);
                if (path.Any(char.IsWhiteSpace))
                    return KfsCommandRoute.Refuse($"kfs load splits its arguments at spaces, so it cannot load '{path}': rename the file or its folder");
                return KfsCommandRoute.Kfs("load " + path + (targetIsVehicle ? " --vehicle" : ""));
            }
            case KfsCommands.Start or KfsCommands.Stop or KfsCommands.Reset:
                return count == 0
                    ? KfsCommandRoute.Kfs(command["kfs.".Length..])
                    : KfsCommandRoute.Refuse($"{command} takes no arguments");
            case KfsCommands.Target:
            {
                if (count != 1 || LspJson.String(arguments![0]) is not { } which || which is not ("vehicle" or "bench"))
                    return KfsCommandRoute.Refuse($"{command} takes one argument, \"vehicle\" or \"bench\"");
                return KfsCommandRoute.Kfs("target " + which);
            }
            case KfsCommands.Manual:
            {
                if (ManualCode(arguments) is not { } code) return KfsCommandRoute.Refuse($"{command} takes one diagnostic code, like \"KC-E003\"");
                return KfsCommandRoute.Kfs("manual " + code);
            }
            case KfsCommands.Parts:
            {
                if (count == 0) return KfsCommandRoute.Purr("parts");
                if (count != 1 || LspJson.String(arguments![0]) is not { } kind || kind is not ("lights" or "deploy" or "actuators"))
                    return KfsCommandRoute.Refuse($"{command} takes no argument or one of \"lights\", \"deploy\", \"actuators\"");
                return KfsCommandRoute.Purr("parts " + kind);
            }
            case KfsCommands.Verb:
            {
                if (!allowVerb) return KfsCommandRoute.Refuse("kfs.verb is switched off: set lsp_allow_vehicle_commands = true in kfs.toml to run any kfs command from the editor");
                if (count != 1 || LspJson.String(arguments![0]) is not { } text) return KfsCommandRoute.Refuse($"{command} takes one string, the kfs command line (\"state\")");
                string line = text.Trim();
                if (line.StartsWith("kfs ", StringComparison.OrdinalIgnoreCase)) line = line[4..].TrimStart();
                if (line.Length == 0 || line.IndexOfAny(['\r', '\n']) >= 0) return KfsCommandRoute.Refuse($"{command} takes one non-empty line");
                return KfsCommandRoute.Kfs(line);
            }
            default:
                return KfsCommandRoute.Refuse($"unknown command '{command}'");
        }
    }

    /// <summary>The diagnostic code of a <c>kfs.manual</c> argument list, or null when it is not one code.</summary>
    public static string? ManualCode(JsonArray? arguments)
    {
        if (arguments is not { Count: 1 } || LspJson.String(arguments[0]) is not { } text) return null;
        string code = text.Trim();
        return Code().IsMatch(code) ? code : null;
    }

    private static string? FileArgument(string command, JsonArray? arguments, Func<string, string?> pathOf, out string path)
    {
        path = "";
        if (arguments is not { Count: 1 } || LspJson.String(arguments[0]) is not { Length: > 0 } uri)
            return $"{command} takes one argument, the file's URI";
        if (pathOf(uri) is not { Length: > 0 } p) return $"{uri} is not a file inside the kFS workspace";
        if (p != p.Trim()) return $"'{p}' starts or ends with a space, which the kfs console cannot pass";
        path = p;
        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{0,31}$")]
    private static partial Regex Code();
}

/// <summary>
/// <c>workspace/executeCommand</c> for <see cref="KfsCommands"/>: resolves the line
/// (<see cref="KfsCommandLine"/>) and runs it through the host (<see cref="IKfsLanguageHost.RunVerb"/>,
/// or the <c>purr</c> runner of <see cref="HostFeatureOptions"/>). The result is always
/// <c>{ "ok": bool, "output": string, "error": string | null }</c>; a refusal never becomes a JSON-RPC
/// error. A host that cannot control a vehicle refuses every command except <c>kfs.manual</c>,
/// which it answers from the tables (<c>DiagnosticManual.Page</c>, the text <c>kfs manual</c> prints).
/// </summary>
public sealed class KfsCommandProvider : IExecuteCommandHandler
{
    private readonly HostFeatureOptions _options;

    /// <summary>A provider with <paramref name="options"/>.</summary>
    public KfsCommandProvider(HostFeatureOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Commands => KfsCommands.All;

    /// <inheritdoc/>
    public async Task<JsonNode?> Execute(LspRequestContext context, string command, JsonArray? arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        IKfsLanguageHost host = context.Server.Host;
        var uris = context.Server.Documents.Uris;
        KfsCommandRoute route = KfsCommandLine.Resolve(command, arguments, uris.ToPath, SafeIsVehicle(host), _options.AllowVerb);
        if (route.Kind == KfsCommandRouteKind.Refused) return Result(CommandResult.Refused(route.Error!));

        if (!host.CanControlVehicle)
        {
            if (command == KfsCommands.Manual && KfsCommandLine.ManualCode(arguments) is { } code)
                return Result(new CommandResult(true, ManualPage(host, code)));
            return Result(CommandResult.Refused(NeedsTheGame(command)));
        }

        CommandResult result;
        try
        {
            if (route.Kind == KfsCommandRouteKind.Purr)
            {
                result = _options.RunPurr is { } runPurr
                    ? await runPurr(route.Line, cancellationToken).ConfigureAwait(false)
                    : CommandResult.Refused($"{command} needs the purr console command, which this host does not have");
            }
            else
            {
                result = await host.RunVerb(route.Line, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;   // the connection answers RequestCancelled
        }
        catch (Exception e)
        {
            result = CommandResult.Refused($"{command} failed: {e.GetType().Name}: {e.Message}");
        }
        return Result(result ?? CommandResult.Refused($"{command}: the host returned nothing"));
    }

    /// <summary>
    /// The refusal of a host that cannot control a vehicle (the standalone <c>purr-kfs lsp</c>): the one
    /// message the player sees for it — the Neovim client shows it as it comes.
    /// </summary>
    public static string NeedsTheGame(string command) =>
        $"{command} needs the game: this language server runs outside it (diagnostics, hover, completion and kfs.manual only); in KSA, open the file with purr nvim";

    /// <summary>The wire shape of a result.</summary>
    public static JsonObject Result(CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new JsonObject
        {
            ["ok"] = result.Ok,
            ["output"] = result.Output ?? "",
            ["error"] = result.Ok ? null : result.Error ?? "failed",
        };
    }

    private static bool SafeIsVehicle(IKfsLanguageHost host)
    {
        try { return host.CurrentTarget.IsVehicle; }
        catch { return false; }
    }

    private static string ManualPage(IKfsLanguageHost host, string code)
    {
        try { return DiagnosticManual.Page(host.Tables, code); }
        catch (Exception e) { return $"{code}: the manual page could not be built ({e.GetType().Name}: {e.Message})"; }
    }
}
