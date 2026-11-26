using Brutal.ImGuiApi.Abstractions;
using KSA.Kfs;

namespace purrTTY.Kfs.Commands;

/// <summary>How the <c>purr</c> command stands with the game's console (part of <see cref="KfsExtensionStatus"/>).</summary>
public enum PurrCommandState
{
    /// <summary>No console: the extension was installed without one (the offline test runtime) or is not installed.</summary>
    None,
    /// <summary>Registered by this install.</summary>
    Registered,
    /// <summary>Still registered from an earlier install whose uninstall could not remove it; not registered again.</summary>
    Kept,
    /// <summary>Registering failed; the services work, the menu still runs the verbs.</summary>
    Failed,
    /// <summary>Registered by the mod itself (<see cref="PurrConsole.Register"/>) for as long as it is loaded; the install uses it and its uninstall leaves it.</summary>
    Standing,
}

/// <summary>
/// purrTTY's own console command, <c>purr</c> (plan §4.13): registered with the game's console the
/// way kFS registers <c>kfs</c> — a static <c>Execute(string args = "")</c> carrying
/// <c>[TerminalAction(…, ArgParseMode.Unparsed)]</c>, handed to
/// <c>TerminalInterface.RegisterCommand(delegate, owner)</c> — through the game services' console
/// host (<see cref="IKfsConsoleHost"/>). No patch. The verbs live in <see cref="Verbs"/>.
/// </summary>
/// <remarks>
/// <para><b>Standing registration.</b> The mod registers the command itself when it loads
/// (<see cref="Register"/>) and removes it when it unloads (<see cref="Unregister"/>), so
/// <c>purr</c> exists whether or not the extension installs: verbs that need the extension then
/// refuse with the usual message, the others (<c>help</c>, <c>status</c>, <c>nvim</c>, <c>lsp</c>…)
/// work. An install on the same console uses that registration (<see cref="PurrCommandState.Standing"/>)
/// and its uninstall leaves it in place.</para>
/// <para><b>Lifecycle.</b> The installer registers the command as its step 9
/// (<see cref="Attach"/>) and removes it at uninstall (<see cref="Detach"/>). A console that cannot
/// remove it keeps it: the command then answers that the extension is not active, and the next
/// install recognises that console (<see cref="IKfsConsoleHost.ConsoleIdentity"/>) and does not
/// register it again. A failure to register is reported in the status, never fails the install, and
/// leaves the host attached, so the purrTTY menu (which calls <see cref="Execute"/> directly) works.</para>
/// <para><b>Output</b> (in ASCII, <see cref="AsciiOutput"/>) goes through the host's <see cref="IKfsConsoleHost.Print"/> (the game:
/// <c>KfsIntegration.Print</c>, so it lands wherever <c>kfs</c> output lands, purrTTY's game-console
/// shell included); with no host attached, straight to <c>KfsIntegration.Print</c>.</para>
/// <para>Main thread only (console commands and menu items run in the UI phase).</para>
/// </remarks>
public static class PurrConsole
{
    /// <summary>The command name.</summary>
    public const string CommandName = "purr";

    /// <summary>The line the game's <c>help</c> shows for it.</summary>
    public const string Description = "purrTTY kFS extension: `purr help` lists the verbs, `purr parts` shows which x_ index is which part";

    private static readonly object Gate = new();
    private static IKfsConsoleHost? _host;
    // A console that still holds the command after an uninstall could not remove it, and the
    // host that registered it there (whose Print the inert command keeps writing through).
    private static object? _lingering;
    private static IKfsConsoleHost? _lingeringHost;
    // The mod's standing registration (Register): the console that holds it, and its host.
    private static object? _standing;
    private static IKfsConsoleHost? _standingHost;
    // The console the attached install actually registered the command with (Attach returned
    // Registered or Kept), else null: an attached host whose registration failed holds nothing.
    private static object? _attachedRegistration;

    /// <summary><see cref="Execute"/> as the delegate handed to the console (allocated once).</summary>
    public static Action<string> Command { get; } = Execute;

    /// <summary>The verbs: the built-in ones, plus whatever other parts of purrTTY add (<see cref="PurrVerbRegistry.Add"/>).</summary>
    public static PurrVerbRegistry Verbs { get; } = PurrBuiltinVerbs.Create();

    /// <summary>
    /// True (the default): every line <c>purr</c> prints goes through <see cref="ConsoleText.ToAscii"/>
    /// first, because the game console's font may lack <c>—</c>, <c>→</c> or <c>…</c>. False prints
    /// the texts as they are (e.g. for a destination known to render them).
    /// </summary>
    public static bool AsciiOutput { get; set; } = true;

    /// <summary>The host attached by the current install, or null.</summary>
    public static IKfsConsoleHost? Host => Volatile.Read(ref _host);

    /// <summary>
    /// The console command: runs one <c>purr</c> command line against the attached host (main
    /// thread). Never throws. Also what the purrTTY menu calls.
    /// </summary>
    [TerminalAction(CommandName, Description, ArgParseMode.Unparsed)]
    public static void Execute(string args = "")
    {
        IKfsConsoleHost? host = Host;
        IKfsConsoleHost? fallback = host is null ? Volatile.Read(ref _lingeringHost) ?? Volatile.Read(ref _standingHost) : null;
        Run(args, host, fallback is null ? null : fallback.Print);
    }

    /// <summary>
    /// The dispatcher: runs <paramref name="args"/> (<c>parts lights</c>) with <paramref name="host"/>
    /// (null: the extension is not active), writing to <paramref name="print"/> (default: the host's
    /// <see cref="IKfsConsoleHost.Print"/>, without a host <c>KfsIntegration.Print</c>). Never throws.
    /// </summary>
    public static void Run(string? args, IKfsConsoleHost? host, Action<string>? print = null)
    {
        Action<string> output = print ?? (host is not null ? host.Print : DefaultPrint);
        bool ascii = AsciiOutput;
        Action<string> write = line =>
        {
            try { output(ascii ? ConsoleText.ToAscii(line) : line); }
            catch (Exception e) { KfsLog.Once("purr output", e); }
        };

        string text = (args ?? "").Trim();
        int space = IndexOfWhiteSpace(text);
        string name = space < 0 ? text : text[..space];
        string rest = space < 0 ? "" : text[(space + 1)..].Trim();
        if (name.Length == 0) name = "help";

        PurrVerb? verb = Verbs.Find(name);
        if (verb is null)
        {
            write($"unknown purr verb '{name}'; verbs: {string.Join(", ", Verbs.All.Select(v => v.Name))} (purr help explains them)");
            return;
        }

        bool active = host is not null && KfsExtension.IsActive;
        if (verb.NeedsExtension && !active)
        {
            write($"purr {verb.Name}: the purrTTY kFS extension is not active ({KfsExtension.StatusSummary}); purr status says more");
            return;
        }

        try
        {
            verb.Handler(new PurrContext(verb.Name, rest, write, active ? host : null, active));
        }
        catch (Exception e)
        {
            KfsLog.Once("purr " + verb.Name, e);
            write($"purr {verb.Name} failed, which is a bug in purrTTY and not in your program: {e.GetType().Name}: {e.Message}");
        }
    }

    // ------------------------------------------------------------------ lifecycle (the installer)

    /// <summary>
    /// Installer step 9: attaches <paramref name="host"/> and registers the command with its
    /// console, unless that console still holds it from an earlier install. Never throws.
    /// </summary>
    /// <param name="host">The console host of the game services.</param>
    /// <param name="message">Why registering failed (<see cref="PurrCommandState.Failed"/>), else empty.</param>
    internal static PurrCommandState Attach(IKfsConsoleHost host, out string message)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (Gate)
        {
            Volatile.Write(ref _host, host);
            message = "";
            object? identity;
            try
            {
                identity = host.ConsoleIdentity;
            }
            catch (Exception e)
            {
                message = $"{e.GetType().Name}: {e.Message}";
                KfsLog.Once("purr console", e);
                return PurrCommandState.Failed;
            }
            if (identity is not null && ReferenceEquals(identity, _standing)) return PurrCommandState.Standing;
            if (identity is not null && ReferenceEquals(identity, _lingering))
            {
                Volatile.Write(ref _lingeringHost, null);   // the new host is attached; the command reaches it through Host
                _attachedRegistration = identity;
                return PurrCommandState.Kept;
            }
            try
            {
                host.RegisterCommand(Command);
            }
            catch (Exception e)
            {
                message = $"{e.GetType().Name}: {e.Message}";
                KfsLog.Once("register purr command", e);
                return PurrCommandState.Failed;
            }
            _lingering = null;
            Volatile.Write(ref _lingeringHost, null);
            _attachedRegistration = identity;
            return PurrCommandState.Registered;
        }
    }

    /// <summary>
    /// Uninstall (and the undo of a failed install): detaches <paramref name="host"/> and removes
    /// the command when <paramref name="state"/> says it is registered. A console that cannot
    /// remove it is remembered, so the next <see cref="Attach"/> does not register it twice. Never throws.
    /// </summary>
    internal static void Detach(IKfsConsoleHost host, PurrCommandState state)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (Gate)
        {
            Interlocked.CompareExchange(ref _host, null, host);
            if (state is not (PurrCommandState.Registered or PurrCommandState.Kept)) return;
            object? identity = _attachedRegistration;
            _attachedRegistration = null;
            try
            {
                identity = host.ConsoleIdentity;
                // The mod registered the command for its whole life (after this install did): it stays.
                if (identity is not null && ReferenceEquals(identity, _standing)) return;
                if (host.UnregisterCommand())
                {
                    if (ReferenceEquals(identity, _lingering))
                    {
                        _lingering = null;
                        Volatile.Write(ref _lingeringHost, null);
                    }
                    return;
                }
            }
            catch (Exception e)
            {
                KfsLog.Once("unregister purr command", e);
            }
            // Still registered there: inert (it says the extension is not active) until the next
            // install, which recognises the console and does not register it again.
            _lingering = identity;
            Volatile.Write(ref _lingeringHost, host);
        }
    }

    // ------------------------------------------------------------------ the mod's standing registration

    /// <summary>
    /// Registers <c>purr</c> with <paramref name="console"/> for as long as the mod is loaded,
    /// independent of the extension (call it once at load; <see cref="Unregister"/> at unload).
    /// When an install already registered it with that console, that registration becomes the
    /// standing one. Never throws.
    /// </summary>
    /// <param name="console">The game's console host (<c>KsaConsoleHost</c>).</param>
    /// <param name="message">Why registering failed (<see cref="PurrCommandState.Failed"/>), else empty.</param>
    public static PurrCommandState Register(IKfsConsoleHost console, out string message)
    {
        ArgumentNullException.ThrowIfNull(console);
        lock (Gate)
        {
            message = "";
            object? identity;
            try
            {
                identity = console.ConsoleIdentity;
            }
            catch (Exception e)
            {
                message = $"{e.GetType().Name}: {e.Message}";
                return PurrCommandState.Failed;
            }
            if (identity is not null && ReferenceEquals(identity, _standing)) return PurrCommandState.Standing;
            // Held: the console still has the command from an uninstall that could not remove it, or
            // the attached install registered it there — not merely "a host is attached" (its
            // registration may have failed).
            bool held = identity is not null && (ReferenceEquals(identity, _lingering) || ReferenceEquals(identity, _attachedRegistration));
            if (!held)
            {
                try
                {
                    console.RegisterCommand(Command);
                }
                catch (Exception e)
                {
                    message = $"{e.GetType().Name}: {e.Message}";
                    KfsLog.Once("register purr command (standing)", e);
                    return PurrCommandState.Failed;
                }
            }
            if (ReferenceEquals(identity, _lingering))
            {
                _lingering = null;
                Volatile.Write(ref _lingeringHost, null);
            }
            _standing = identity ?? new object();
            Volatile.Write(ref _standingHost, console);
            if (ReferenceEquals(identity, _attachedRegistration)) _attachedRegistration = null;   // standing now: the uninstall leaves it
            return PurrCommandState.Registered;
        }
    }

    /// <summary>
    /// Removes the standing registration (mod unload, after the extension's uninstall: an install
    /// still attached loses the command too). A console
    /// that cannot remove it keeps an inert command, recognised by the next load. Never throws.
    /// </summary>
    public static void Unregister()
    {
        lock (Gate)
        {
            IKfsConsoleHost? console = Volatile.Read(ref _standingHost);
            object? identity = _standing;
            _standing = null;
            Volatile.Write(ref _standingHost, null);
            if (console is null) return;
            try
            {
                if (console.UnregisterCommand()) return;
            }
            catch (Exception e)
            {
                KfsLog.Once("unregister purr command (standing)", e);
            }
            _lingering = identity;
            Volatile.Write(ref _lingeringHost, console);
        }
    }

    /// <summary>True while the mod's standing registration is in place (tests).</summary>
    public static bool HasStandingCommand
    {
        get { lock (Gate) return _standing is not null; }
    }

    /// <summary>True when a console still holds the command although the extension was uninstalled (tests).</summary>
    internal static bool HasLingeringCommand
    {
        get { lock (Gate) return _lingering is not null; }
    }

    private static void DefaultPrint(string line) => KfsIntegration.Print(line);

    private static int IndexOfWhiteSpace(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i])) return i;
        }
        return -1;
    }
}
