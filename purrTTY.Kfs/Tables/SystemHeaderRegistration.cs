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
/// <see cref="SystemHeaders"/> is a pair of plain mutable collections the compiler reads: register
/// and dispose only while no compile is running. Fields hold BCL types only.
/// </remarks>
public sealed class SystemHeaderRegistration : IDisposable
{
    private readonly string _name;
    private readonly string? _previousText;
    private readonly List<string> _addedBuiltins = [];
    private bool _disposed;

    private SystemHeaderRegistration(string name, string text, IEnumerable<string> builtins)
    {
        _name = name;
        _previousText = SystemHeaders.Files.TryGetValue(name, out var previous) ? previous : null;
        SystemHeaders.Files[name] = text;
        foreach (string builtin in builtins)
        {
            if (SystemHeaders.Builtins.Add(builtin)) _addedBuiltins.Add(builtin);
        }
    }

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

    /// <summary>Restores the header statics to what they were before this registration.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_previousText is null) SystemHeaders.Files.Remove(_name);
        else SystemHeaders.Files[_name] = _previousText;
        foreach (string builtin in _addedBuiltins) SystemHeaders.Builtins.Remove(builtin);
    }
}
