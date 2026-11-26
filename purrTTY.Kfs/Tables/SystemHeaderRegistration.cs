using KSA.Kfs.KC;
using purrTTY.Kfs.Catalog;

namespace purrTTY.Kfs.Tables;

/// <summary>
/// Puts <c>&lt;purrtty.h&gt;</c> and the catalogue's builtin names into the game's process-wide
/// <see cref="SystemHeaders.Files"/> and <see cref="SystemHeaders.Builtins"/> (plan §4.5 step 7),
/// and on <see cref="Dispose"/> restores both exactly: a header text that was there before is put
/// back, and only the builtin names this registration added are removed. The one mechanism behind
/// the installer, the test runtime and the tests' scoped registration.
/// </summary>
/// <remarks>
/// <see cref="SystemHeaders"/> is a pair of plain mutable collections the compiler reads — on the
/// game's background build of the stock editor and on the language server's compile threads too:
/// register and dispose only while no compile is running. The installer's uninstall therefore uses
/// <see cref="Release"/>, which leaves the registration in place while one may be (see
/// <see cref="SystemHeaderReaders"/>); the next registration of the same header adopts what that one
/// had to restore, so a reload never makes it permanent. Fields hold BCL types only.
/// </remarks>
public sealed class SystemHeaderRegistration : IDisposable
{
    private static readonly object LeftoverGate = new();
    private static SystemHeaderRegistration? _leftover;

    private readonly string _name;
    private readonly string? _previousText;
    private readonly List<string> _addedBuiltins = [];
    private bool _disposed;

    private SystemHeaderRegistration(string name, string text, IEnumerable<string> builtins)
    {
        _name = name;
        SystemHeaderRegistration? leftover;
        lock (LeftoverGate)
        {
            leftover = _leftover is { } l && l._name == name ? l : null;
            if (leftover is not null) _leftover = null;
        }
        if (leftover is not null)
        {
            // An earlier registration was left in place (a compile was running at its uninstall):
            // what is in the statics now is ours; restore what it would have restored.
            _previousText = leftover._previousText;
            _addedBuiltins.AddRange(leftover._addedBuiltins);
        }
        else
        {
            _previousText = SystemHeaders.Files.TryGetValue(name, out var previous) ? previous : null;
        }
        SystemHeaders.Files[name] = text;
        foreach (string builtin in builtins)
        {
            if (SystemHeaders.Builtins.Add(builtin)) _addedBuiltins.Add(builtin);
        }
    }

    /// <summary>The reason the last <see cref="Release"/> left a registration in place, while it still is; else null.</summary>
    public static string? LeftInPlace
    {
        get { lock (LeftoverGate) return _leftover?._leftReason; }
    }

    private string? _leftReason;

    /// <summary>Registers the real catalogue's header and builtin names.</summary>
    public static SystemHeaderRegistration Install() =>
        Install(CatalogEmitter.Header(XCatalog.Catalog), CatalogEmitter.BuiltinNames(XCatalog.Catalog));

    /// <summary>Registers <paramref name="text"/> as <c>purrtty.h</c> with <paramref name="builtins"/>.</summary>
    public static SystemHeaderRegistration Install(string text, IEnumerable<string> builtins)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(builtins);
        return new SystemHeaderRegistration(XCatalog.HeaderName, text, builtins);
    }

    /// <summary>True when nothing of purrTTY's is in the game's header statics.</summary>
    public static bool IsClean() =>
        !SystemHeaders.Files.ContainsKey(XCatalog.HeaderName)
        && !XCatalog.Services.Any(s => SystemHeaders.Builtins.Contains(s.Name));

    /// <summary>
    /// Restores the header statics (as <see cref="Dispose"/>) unless <paramref name="inUse"/> says a
    /// compile may be reading them right now: then nothing is changed — mutating the game's plain
    /// <c>Dictionary</c> / <c>HashSet</c> under a reader can corrupt them — and the registration is
    /// remembered (<see cref="LeftInPlace"/>) for the next one to take over. True when restored.
    /// </summary>
    public bool Release(string? inUse)
    {
        if (_disposed) return true;
        if (inUse is null)
        {
            Dispose();
            return true;
        }
        _disposed = true;
        _leftReason = inUse;
        lock (LeftoverGate) _leftover = this;
        return false;
    }

    /// <summary>Restores the header statics to what they were before this registration.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_previousText is null) SystemHeaders.Files.Remove(_name);
        else SystemHeaders.Files[_name] = _previousText;
        foreach (string builtin in _addedBuiltins) SystemHeaders.Builtins.Remove(builtin);
    }

    /// <summary>
    /// Tests: restores a registration a <see cref="Release"/> left in place, as its <see cref="Dispose"/>
    /// would have (only when nothing reads the statics). True when there was one.
    /// </summary>
    public static bool RestoreLeftover()
    {
        SystemHeaderRegistration? leftover;
        lock (LeftoverGate)
        {
            leftover = _leftover;
            _leftover = null;
        }
        if (leftover is null) return false;
        leftover._disposed = false;
        leftover.Dispose();
        return true;
    }
}

/// <summary>
/// Who may be reading the game's <see cref="SystemHeaders"/> off the main thread right now, so the
/// header registration is never changed under a reader: purrTTY's own compiles (the language
/// server's compile threads, counted by <see cref="EnterCompile"/> for as long as each compile runs,
/// an abandoned one included) and, through the game services, the stock editor's background build.
/// No game type in a signature; any thread.
/// </summary>
public static class SystemHeaderReaders
{
    private static int _compiles;

    /// <summary>purrTTY compiles running now (any thread).</summary>
    public static int Compiles => Volatile.Read(ref _compiles);

    /// <summary>Counts one compile until the returned scope is disposed.</summary>
    public static Scope EnterCompile()
    {
        Interlocked.Increment(ref _compiles);
        return new Scope(true);
    }

    /// <summary>
    /// Why the header statics must not be changed now, or null when they may: purrTTY compiles still
    /// running, or <paramref name="gameReader"/>'s reason (the stock editor is building).
    /// </summary>
    public static string? InUse(Func<string?>? gameReader = null)
    {
        int compiles = Compiles;
        if (compiles > 0) return $"{compiles} language-server compile{(compiles == 1 ? " is" : "s are")} still running";
        try { return gameReader?.Invoke(); }
        catch (Exception e) { return $"the game's build state could not be read ({e.GetType().Name})"; }
    }

    /// <summary>One counted compile (dispose once).</summary>
    public struct Scope(bool counted) : IDisposable
    {
        private bool _counted = counted;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!_counted) return;
            _counted = false;
            Interlocked.Decrement(ref _compiles);
        }
    }
}
