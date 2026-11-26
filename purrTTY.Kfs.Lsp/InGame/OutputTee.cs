// RULE FOR THIS FILE: no game type. The game's writer is reached only through the get/set delegates.

using System.Reflection;
using System.Text;

namespace purrTTY.Kfs.Lsp.InGame;

/// <summary>
/// The standing output tee of plan §6.7: installed in place of <c>KfsIntegration.Output</c>, it
/// forwards every call to the writer it replaced — the same overload, so the game's console writer
/// splits lines exactly as before — and passes the text to a sink (the output feed) and to the
/// captures of <c>RunVerb</c> calls.
/// <list type="bullet">
/// <item><b>Captures</b> (<see cref="BeginCapture"/>) collect what the opening thread writes until they
/// are disposed (nested captures each see it).</item>
/// <item><b>Clients only</b> (<see cref="EnterClientsOnly"/>): while the counter of the writing thread is
/// above zero the text goes to the sink and the captures but not to the original writer — and only
/// when the original writer is the game's own console writer (<see cref="OriginalIsGameConsole"/>).
/// The mod sets it around <c>KfsIntegration.Error</c>, which writes a line to <c>Output</c> only when
/// <c>Output</c> is not the game's console writer — on top of logging it, which already shows it in
/// the console (risk R17): the console keeps showing each error once. When another mod's writer was
/// in the slot before the tee, <c>Error</c> wrote to it without the tee, so the tee forwards too.</item>
/// <item><b>Console only</b> (<see cref="EnterConsoleOnly"/>): while the counter of the writing thread
/// is above zero the text goes to the original writer only — never to the sink (the editors' output
/// feed) or a capture. <c>purr lsp token</c> prints the token that way.</item>
/// <item><b>Detached</b> (<see cref="Detach"/>, at stop): a pure pass-through, for a tee that cannot be
/// taken out because another writer was installed on top of it.</item>
/// </list>
/// Every member is thread-safe and never throws from the sink or the captures.
/// </summary>
public sealed class OutputTee : TextWriter
{
    [ThreadStatic] private static int t_clientsOnly;
    [ThreadStatic] private static int t_consoleOnly;

    private readonly TextWriter _original;
    private readonly bool _originalIsGameConsole;
    private readonly object _gate = new();
    private readonly List<Capture> _captures = [];
    private Action<string>? _sink;
    private volatile bool _detached;

    /// <summary>A tee in front of <paramref name="original"/>, passing text to <paramref name="sink"/>.</summary>
    /// <param name="original">The writer it replaced.</param>
    /// <param name="sink">Where text goes besides (the output feed).</param>
    /// <param name="originalIsGameConsole">Whether <paramref name="original"/> is the game's own console writer (only then does clients-only suppress forwarding).</param>
    public OutputTee(TextWriter original, Action<string>? sink, bool originalIsGameConsole = true)
    {
        _original = original ?? throw new ArgumentNullException(nameof(original));
        _sink = sink;
        _originalIsGameConsole = originalIsGameConsole;
    }

    /// <summary>Whether the original writer is the game's own console writer.</summary>
    public bool OriginalIsGameConsole => _originalIsGameConsole;

    /// <summary>The writer this tee replaced (and forwards to).</summary>
    public TextWriter Original => _original;

    /// <summary>True once <see cref="Detach"/> made it a pass-through.</summary>
    public bool IsDetached => _detached;

    /// <inheritdoc/>
    public override Encoding Encoding => _original.Encoding;

    /// <summary>Stops passing text anywhere but the original writer (the service stopped). Idempotent.</summary>
    public void Detach()
    {
        _detached = true;
        lock (_gate)
        {
            _sink = null;
            _captures.Clear();
        }
    }

    /// <summary>Starts collecting what the calling thread writes, until the capture is disposed.</summary>
    public Capture BeginCapture()
    {
        var capture = new Capture(this, Environment.CurrentManagedThreadId);
        lock (_gate)
        {
            if (!_detached) _captures.Add(capture);
        }
        return capture;
    }

    /// <summary>From now on the calling thread's text does not reach the original writer (paired with <see cref="ExitClientsOnly"/>).</summary>
    public static void EnterClientsOnly() => t_clientsOnly++;

    /// <summary>Undoes one <see cref="EnterClientsOnly"/>.</summary>
    public static void ExitClientsOnly()
    {
        if (t_clientsOnly > 0) t_clientsOnly--;
    }

    /// <summary>The calling thread's clients-only depth (tests).</summary>
    public static int ClientsOnlyDepth => t_clientsOnly;

    /// <summary>From now on the calling thread's text reaches the original writer only — not the sink or a capture (paired with <see cref="ExitConsoleOnly"/>).</summary>
    public static void EnterConsoleOnly() => t_consoleOnly++;

    /// <summary>Undoes one <see cref="EnterConsoleOnly"/>.</summary>
    public static void ExitConsoleOnly()
    {
        if (t_consoleOnly > 0) t_consoleOnly--;
    }

    // ------------------------------------------------------------------ the TextWriter surface

    /// <inheritdoc/>
    public override void Write(char value)
    {
        if (Forward) _original.Write(value);
        Pass(value.ToString());
    }

    /// <inheritdoc/>
    public override void Write(string? value)
    {
        if (Forward) _original.Write(value);
        Pass(value);
    }

    /// <inheritdoc/>
    public override void Write(char[] buffer, int index, int count)
    {
        if (Forward) _original.Write(buffer, index, count);
        Pass(new string(buffer, index, count));
    }

    /// <inheritdoc/>
    public override void WriteLine()
    {
        if (Forward) _original.WriteLine();
        Pass("\n");
    }

    /// <inheritdoc/>
    public override void WriteLine(string? value)
    {
        if (Forward) _original.WriteLine(value);
        Pass((value ?? "") + "\n");
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        if (Forward) _original.Flush();
    }

    private bool Forward => _detached || t_clientsOnly == 0 || !_originalIsGameConsole || t_consoleOnly > 0;

    private void Pass(string? text)
    {
        if (_detached || t_consoleOnly > 0 || string.IsNullOrEmpty(text)) return;
        string normalized = text.Contains('\r') ? text.Replace("\r\n", "\n", StringComparison.Ordinal) : text;
        Action<string>? sink;
        lock (_gate)
        {
            sink = _sink;
            int thread = Environment.CurrentManagedThreadId;
            foreach (Capture capture in _captures)
            {
                if (capture.Thread == thread) capture.Append(normalized);
            }
        }
        if (sink is null) return;
        try { sink(normalized); }
        catch { /* the feed must never break the game's console */ }
    }

    private void Remove(Capture capture)
    {
        lock (_gate) _captures.Remove(capture);
    }

    /// <summary>What one thread wrote between <see cref="BeginCapture"/> and <see cref="Dispose"/>.</summary>
    public sealed class Capture : IDisposable
    {
        private readonly OutputTee _owner;
        private readonly StringBuilder _text = new();

        internal Capture(OutputTee owner, int thread)
        {
            _owner = owner;
            Thread = thread;
        }

        internal int Thread { get; }

        /// <summary>The text so far (<c>\n</c> line ends).</summary>
        public string Text
        {
            get { lock (_text) return _text.ToString(); }
        }

        internal void Append(string text)
        {
            lock (_text) _text.Append(text);
        }

        /// <summary>Stops collecting.</summary>
        public void Dispose() => _owner.Remove(this);
    }
}

/// <summary>How <see cref="OutputTeeInstallation.Restore"/> left the game's writer.</summary>
public enum OutputTeeRestore
{
    /// <summary>The tee was in place and the original writer is back.</summary>
    Restored,
    /// <summary>Another writer was installed on top of the tee: left alone, the tee is now a pass-through.</summary>
    Foreign,
    /// <summary>Already restored.</summary>
    AlreadyRestored,
}

/// <summary>
/// Installs an <see cref="OutputTee"/> in a writer slot (in game the public static property
/// <c>KfsIntegration.Output</c>, through <paramref name="get"/>/<paramref name="set"/> delegates) and
/// takes it out again.
/// <list type="bullet">
/// <item><b>Install</b> wraps whatever is there. A <i>detached</i> tee left in the slot (an earlier
/// load whose stop found a foreign writer on top, which has since been removed) is unwrapped first,
/// so reloads never stack tees. It is recognised by its full type name and its public
/// <see cref="OutputTee.IsDetached"/> and <see cref="OutputTee.Original"/> properties, read by name,
/// so a tee from another load context of this assembly (a different .NET type with the same
/// name) is unwrapped too (<see cref="StaleOriginal"/>).</item>
/// <item><b>Restore</b> puts the original back only when the slot still holds this tee; when another
/// writer was installed on top it is left alone (taking the tee out would cut that writer off) and
/// the tee becomes a pass-through. Either way the tee stops feeding.</item>
/// </list>
/// </summary>
public sealed class OutputTeeInstallation
{
    private readonly Func<TextWriter> _get;
    private readonly Action<TextWriter> _set;
    private bool _restored;

    private OutputTeeInstallation(Func<TextWriter> get, Action<TextWriter> set, OutputTee tee)
    {
        _get = get;
        _set = set;
        Tee = tee;
    }

    /// <summary>The installed tee.</summary>
    public OutputTee Tee { get; }

    /// <summary>Installs a tee in the slot (see the class remarks). Throws what the delegates throw.</summary>
    /// <param name="get">Reads the slot.</param>
    /// <param name="set">Writes the slot.</param>
    /// <param name="sink">Where the tee passes text besides.</param>
    /// <param name="isGameConsole">Tells whether a writer is the game's own console writer (null: assumed it is).</param>
    public static OutputTeeInstallation Install(Func<TextWriter> get, Action<TextWriter> set, Action<string>? sink, Func<TextWriter, bool>? isGameConsole = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        TextWriter current = get() ?? throw new InvalidOperationException("the output slot holds no writer");
        TextWriter original = current;
        // A chain is possible when earlier loads stacked tees; bounded against a writer that names itself.
        for (int depth = 0; depth < MaxUnwrap && StaleOriginal(original) is { } inner; depth++) original = inner;
        var tee = new OutputTee(original, sink, isGameConsole?.Invoke(original) ?? true);
        set(tee);
        return new OutputTeeInstallation(get, set, tee);
    }

    private const int MaxUnwrap = 32;

    /// <summary>
    /// The writer a <i>detached</i> tee forwards to, or null when <paramref name="writer"/> is not a
    /// detached tee. Recognises an <see cref="OutputTee"/> of this load directly and one of any other
    /// load context of this assembly by its full type name and its public <c>IsDetached</c> (bool)
    /// and <c>Original</c> (<see cref="TextWriter"/>) properties, looked up by name. Never throws.
    /// </summary>
    public static TextWriter? StaleOriginal(TextWriter? writer)
    {
        if (writer is null) return null;
        if (writer is OutputTee own) return own.IsDetached ? own.Original : null;
        try
        {
            Type type = writer.GetType();
            if (!string.Equals(type.FullName, typeof(OutputTee).FullName, StringComparison.Ordinal)) return null;
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;
            if (type.GetProperty(nameof(OutputTee.IsDetached), Flags)?.GetValue(writer) is not true) return null;
            return type.GetProperty(nameof(OutputTee.Original), Flags)?.GetValue(writer) is TextWriter original
                   && !ReferenceEquals(original, writer)
                ? original
                : null;
        }
        catch
        {
            return null;   // not recognisable: wrapped like any other writer
        }
    }

    /// <summary>Takes the tee out (see the class remarks). Never throws.</summary>
    public OutputTeeRestore Restore()
    {
        if (_restored) return OutputTeeRestore.AlreadyRestored;
        _restored = true;
        OutputTeeRestore outcome;
        try
        {
            if (ReferenceEquals(_get(), Tee))
            {
                _set(Tee.Original);
                outcome = OutputTeeRestore.Restored;
            }
            else
            {
                outcome = OutputTeeRestore.Foreign;
            }
        }
        catch
        {
            outcome = OutputTeeRestore.Foreign;
        }
        Tee.Detach();
        return outcome;
    }
}
