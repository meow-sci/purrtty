using System.Text.RegularExpressions;

namespace purrTTY.Kfs.Commands;

/// <summary>Runs one <c>purr</c> verb.</summary>
/// <param name="context">The arguments after the verb, the output, and the console host.</param>
public delegate void PurrVerbHandler(PurrContext context);

/// <summary>One verb of the <c>purr</c> command.</summary>
/// <param name="Name">The verb (lower case: letters, digits, <c>-</c>).</param>
/// <param name="Help">One line for <c>purr help</c>.</param>
/// <param name="Handler">What it does.</param>
/// <param name="NeedsExtension">
/// True when it only makes sense while the extension is installed (it reads the controlled
/// vehicle or writes the workspace through the game services): while the extension is not active
/// the dispatcher answers with that instead of calling it.
/// </param>
public sealed record PurrVerb(string Name, string Help, PurrVerbHandler Handler, bool NeedsExtension);

/// <summary>
/// The verbs of <c>purr</c> (plan §4.13): the built-in ones (<see cref="PurrBuiltinVerbs"/>) and
/// any another part of purrTTY adds — <c>purrTTY.GameMod</c> adds <c>purr lsp</c> and
/// <c>purr nvim</c> through <see cref="PurrConsole.Verbs"/> without touching this assembly.
/// Thread-safe; <see cref="All"/> is a snapshot in registration order.
/// </summary>
public sealed partial class PurrVerbRegistry
{
    private readonly object _gate = new();
    private PurrVerb[] _verbs = [];

    /// <summary>Every verb, in registration order (a snapshot).</summary>
    public IReadOnlyList<PurrVerb> All => Volatile.Read(ref _verbs);

    /// <summary>The verb named <paramref name="name"/> (case-insensitive), or null.</summary>
    public PurrVerb? Find(string name)
    {
        foreach (PurrVerb verb in Volatile.Read(ref _verbs))
        {
            if (string.Equals(verb.Name, name, StringComparison.OrdinalIgnoreCase)) return verb;
        }
        return null;
    }

    /// <summary>
    /// Adds a verb. Returns a handle whose <see cref="IDisposable.Dispose"/> removes exactly this
    /// verb again (a later verb of the same name is left alone).
    /// </summary>
    /// <exception cref="ArgumentException">The name is not lower-case letters, digits and <c>-</c> (starting with a letter), or the help text is empty or spans lines.</exception>
    /// <exception cref="InvalidOperationException">A verb of that name exists.</exception>
    public IDisposable Add(string name, string help, PurrVerbHandler handler, bool needsExtension = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(help);
        ArgumentNullException.ThrowIfNull(handler);
        if (!VerbName().IsMatch(name)) throw new ArgumentException($"'{name}' is not a verb name: lower-case letters, digits and '-', starting with a letter", nameof(name));
        if (help.Trim().Length == 0 || help.Contains('\n') || help.Contains('\r')) throw new ArgumentException("the help text must be one non-empty line", nameof(help));

        var verb = new PurrVerb(name, help.Trim(), handler, needsExtension);
        lock (_gate)
        {
            if (Find(name) is not null) throw new InvalidOperationException($"purr {name} is already registered");
            Volatile.Write(ref _verbs, [.. _verbs, verb]);
        }
        return new Removal(this, verb);
    }

    /// <summary>Removes the verb named <paramref name="name"/>; false when there is none.</summary>
    public bool Remove(string name)
    {
        lock (_gate)
        {
            PurrVerb? verb = Find(name);
            return verb is not null && RemoveLocked(verb);
        }
    }

    private bool Remove(PurrVerb verb)
    {
        lock (_gate) return RemoveLocked(verb);
    }

    private bool RemoveLocked(PurrVerb verb)
    {
        if (Array.IndexOf(_verbs, verb) < 0) return false;
        Volatile.Write(ref _verbs, _verbs.Where(v => !ReferenceEquals(v, verb)).ToArray());
        return true;
    }

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex VerbName();

    private sealed class Removal(PurrVerbRegistry registry, PurrVerb verb) : IDisposable
    {
        private PurrVerbRegistry? _registry = registry;

        public void Dispose() => Interlocked.Exchange(ref _registry, null)?.Remove(verb);
    }
}

/// <summary>What a verb handler gets: its arguments, the output, and the game's console host.</summary>
public sealed class PurrContext
{
    private readonly Action<string> _print;

    internal PurrContext(string verb, string args, Action<string> print, IKfsConsoleHost? host, bool extensionActive)
    {
        Verb = verb;
        Args = args;
        _print = print;
        Host = host;
        ExtensionActive = extensionActive;
    }

    /// <summary>The verb as registered.</summary>
    public string Verb { get; }

    /// <summary>Everything after the verb, trimmed (empty for none).</summary>
    public string Args { get; }

    /// <summary><see cref="Args"/> split on white space.</summary>
    public IReadOnlyList<string> Words => Args.Length == 0 ? [] : Args.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The console host while the extension is installed with one (the game), else null.</summary>
    public IKfsConsoleHost? Host { get; }

    /// <summary>True while the extension is installed.</summary>
    public bool ExtensionActive { get; }

    /// <summary>Writes text; every line of it (<c>\r\n</c> or <c>\n</c>) becomes one output line.</summary>
    public void Print(string text)
    {
        if (text is null) return;
        foreach (string line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')) _print(line);
    }

    /// <summary>Writes each of <paramref name="lines"/>.</summary>
    public void Print(IEnumerable<string> lines)
    {
        foreach (string line in lines) Print(line);
    }

    /// <summary>
    /// For a verb that takes no arguments: true when there are none; otherwise writes
    /// <c>purr status: unexpected argument 'x' (usage: purr status)</c> and returns false.
    /// </summary>
    public bool NoArguments()
    {
        if (Args.Length == 0) return true;
        _print($"purr {Verb}: unexpected argument '{Args}' (usage: purr {Verb})");
        return false;
    }
}
