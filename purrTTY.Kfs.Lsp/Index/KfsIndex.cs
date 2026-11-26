using System.Runtime.CompilerServices;
using KSA.Kfs.KC;
using KSA.Kfs.Vm.Tables;
using purrTTY.Kfs.Catalog;

namespace purrTTY.Kfs.Lsp.Index;

/// <summary>
/// Everything hover and completion look names up in (plan §6.5), for one (tables, build target,
/// system headers) triple: the <see cref="HeaderIndex"/> of <c>SystemHeaders.Files</c>, the
/// <see cref="TableIndex"/> of the tables and the target's part, the <see cref="TargetIndex"/> of the
/// target's layout, merged. Immutable; get one from <see cref="KfsIndexCache"/>.
/// </summary>
public sealed class KfsIndex
{
    internal KfsIndex(HeaderIndex headers, TableIndex tables, TargetIndex target, BuildTarget buildTarget)
    {
        Headers = headers;
        Tables = tables;
        Target = target;
        BuildTarget = buildTarget;

        // kC: services and sys first, then <vehicle.h>, then the system headers.
        var kc = new Dictionary<string, IndexEntry>(StringComparer.Ordinal);
        foreach ((string name, IndexEntry e) in tables.KcNames) kc[name] = e;
        foreach ((string name, IndexEntry e) in target.KcNames) kc.TryAdd(name, e);
        foreach ((string name, IndexEntry e) in headers.Names) kc.TryAdd(name, e);
        KcNames = kc;
        KcIdentifiers = kc.Values.Where(e => !e.Name.StartsWith("__", StringComparison.Ordinal))
            .Concat(tables.Keywords)
            .OrderBy(e => e.SortGroup).ThenBy(e => e.Name, StringComparer.Ordinal).ToArray();

        var kasm = new Dictionary<string, IndexEntry>(tables.KasmNames, StringComparer.Ordinal);
        foreach ((string name, IndexEntry e) in target.KasmNames) kasm.TryAdd(name, e);
        KasmNames = kasm;
        var types = new Dictionary<string, IndexEntry>(tables.KasmTypes, StringComparer.Ordinal);
        foreach ((string name, IndexEntry e) in target.KasmTypes) types.TryAdd(name, e);
        KasmTypes = types;

        SystemHeaders = headers.Headers.Concat(target.VehicleHeader.Headers).OrderBy(h => h.Entry.SortGroup).ThenBy(h => h.Name, StringComparer.Ordinal).ToArray();
    }

    /// <summary>The system headers' names.</summary>
    public HeaderIndex Headers { get; }

    /// <summary>The table names.</summary>
    public TableIndex Tables { get; }

    /// <summary>The layout names.</summary>
    public TargetIndex Target { get; }

    /// <summary>The build target this index is for.</summary>
    public BuildTarget BuildTarget { get; }

    /// <summary>Every kC name: services, <c>sys</c>, <c>&lt;vehicle.h&gt;</c>, the system headers.</summary>
    public IReadOnlyDictionary<string, IndexEntry> KcNames { get; }

    /// <summary>What an identifier completion in kC offers (names and keywords), sorted.</summary>
    public IReadOnlyList<IndexEntry> KcIdentifiers { get; }

    /// <summary>Every kASM symbol of the built-in includes and <c>purrtty.kinc</c>.</summary>
    public IReadOnlyDictionary<string, IndexEntry> KasmNames { get; }

    /// <summary>Every kASM type.</summary>
    public IReadOnlyDictionary<string, IndexEntry> KasmTypes { get; }

    /// <summary>The headers of <c>#include &lt;…&gt;</c>: the system headers and <c>vehicle.h</c>.</summary>
    public IReadOnlyList<HeaderInfo> SystemHeaders { get; }

    /// <summary>The shape of a C struct type, or null.</summary>
    public MemberShape? Shape(string typeName) =>
        typeName == "kfs_sys_t" ? Tables.Sys : Target.Shapes.GetValueOrDefault(typeName);

    /// <summary>The shape of a header object (<c>sys</c>, <c>commands</c>, <c>sensors</c>), or null.</summary>
    public MemberShape? Root(string name) => name == "sys" ? Tables.Sys : Target.Roots.GetValueOrDefault(name);
}

/// <summary>
/// Builds <see cref="KfsIndex"/>es and keeps the last few: one per (tables, part, layout, system
/// headers). A lookup compares references (the host replaces its <see cref="BuildTarget"/> snapshot
/// on a target change, the tables never change) and a stamp of <c>SystemHeaders.Files</c>, so a
/// changed target or a header registered later builds a new index on the next request; the old one
/// stays valid for whoever still holds it. Thread-safe: an index is built outside the lock and
/// published under it (two threads may both build one; the first published wins).
/// </summary>
public static class KfsIndexCache
{
    private const int Capacity = 8;

    private sealed record Slot(GameTables Tables, PartDescription Part, IoLayout Layout, string TargetName, long Stamp, KfsIndex Index);

    private static readonly object Gate = new();
    private static readonly List<Slot> Slots = [];
    private static (long Stamp, HeaderIndex Index)? _headers;
    private static readonly ConditionalWeakTable<GameTables, List<(PartDescription Part, long Stamp, TableIndex Index)>> TableIndexes = new();

    /// <summary>Indexes built so far (for tests and logs).</summary>
    public static int Builds { get; private set; }

    /// <summary>The index for <paramref name="host"/>'s tables and current target.</summary>
    public static KfsIndex For(IKfsLanguageHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return For(host.Tables, host.CurrentTarget);
    }

    /// <summary>The index for <paramref name="tables"/> and <paramref name="target"/>.</summary>
    public static KfsIndex For(GameTables tables, BuildTarget target)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(target);
        (long stamp, List<KeyValuePair<string, string>> headers) = SnapshotHeaders();
        lock (Gate)
        {
            foreach (Slot slot in Slots)
            {
                if (ReferenceEquals(slot.Tables, tables) && ReferenceEquals(slot.Part, target.Part) && ReferenceEquals(slot.Layout, target.Layout)
                    && slot.TargetName == target.DisplayName && slot.Stamp == stamp)
                {
                    if (Slots[0] != slot)
                    {
                        Slots.Remove(slot);
                        Slots.Insert(0, slot);
                    }
                    return slot.Index;
                }
            }
        }

        HeaderIndex headerIndex = HeadersFor(stamp, headers);
        TableIndex tableIndex = TablesFor(tables, target.Part, stamp, headerIndex);
        TargetIndex targetIndex = TargetIndex.Build(tables, target.Layout, target.DisplayName);
        var index = new KfsIndex(headerIndex, tableIndex, targetIndex, target);
        lock (Gate)
        {
            Builds++;
            Slots.Insert(0, new Slot(tables, target.Part, target.Layout, target.DisplayName, stamp, index));
            if (Slots.Count > Capacity) Slots.RemoveAt(Slots.Count - 1);
        }
        return index;
    }

    private static HeaderIndex HeadersFor(long stamp, List<KeyValuePair<string, string>> headers)
    {
        lock (Gate)
        {
            if (_headers is { } h && h.Stamp == stamp) return h.Index;
        }
        HashSet<string> builtins;
        try { builtins = new HashSet<string>(KSA.Kfs.KC.SystemHeaders.Builtins, StringComparer.Ordinal); }
        catch (InvalidOperationException) { builtins = new HashSet<string>(StringComparer.Ordinal); }
        HeaderIndex index = HeaderIndex.Scan(headers, builtins.Contains, Describe);
        lock (Gate) _headers = (stamp, index);
        return index;
    }

    private static TableIndex TablesFor(GameTables tables, PartDescription part, long stamp, HeaderIndex headers)
    {
        List<(PartDescription Part, long Stamp, TableIndex Index)> list = TableIndexes.GetValue(tables, _ => []);
        lock (Gate)
        {
            foreach (var (p, s, i) in list)
            {
                if (ReferenceEquals(p, part) && s == stamp) return i;
            }
        }
        TableIndex index = TableIndex.Build(tables, part, headers);
        lock (Gate)
        {
            list.Insert(0, (part, stamp, index));
            if (list.Count > Capacity) list.RemoveAt(list.Count - 1);
        }
        return index;
    }

    /// <summary>
    /// A copy of <c>SystemHeaders.Files</c> (<c>kfs.h</c> first, then <c>purrtty.h</c>, then by name)
    /// and a stamp of it: names, lengths and string identities, so a header registered or replaced
    /// later changes it. The dictionary is mutated only while the extension installs, before any
    /// server runs; a copy that races one is retried.
    /// </summary>
    private static (long Stamp, List<KeyValuePair<string, string>> Headers) SnapshotHeaders()
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var headers = KSA.Kfs.KC.SystemHeaders.Files.ToList();
                headers.Sort((a, b) => Rank(a.Key) != Rank(b.Key) ? Rank(a.Key).CompareTo(Rank(b.Key)) : string.CompareOrdinal(a.Key, b.Key));
                long stamp = 17;
                foreach ((string name, string text) in headers)
                    stamp = stamp * 31 + HashCode.Combine(name.GetHashCode(StringComparison.Ordinal), text.Length, RuntimeHelpers.GetHashCode(text));
                return (stamp, headers);
            }
            catch (InvalidOperationException) when (attempt < 3)
            {
                // Mutated while copied: try again.
            }
        }
    }

    private static int Rank(string header) => header == "kfs.h" ? 0 : header == XCatalog.HeaderName ? 1 : 2;

    private static string? Describe(string header) => header switch
    {
        "kfs.h" => "The kFS core: the system block `sys`, the stock services, the `KFS_*` constants and the vector types.",
        "math.h" => "float and double math: `sqrtf`, `fabsf`, `sqrt`, `fabs`, `isnan`, `isinf` are builtins; the rest are library routines from `<math.c>`.",
        "vecmat.h" => "vec3, quat and mat3 helpers (and their double variants), library routines from `<vecmat.c>`.",
        "string.h" => "`memcpy`, `memmove`, `memset`, `memcmp` (compiler builtins).",
        "stdint.h" => "Fixed-width integer types and their limits.",
        "stdbool.h" => "`bool`, `true`, `false`.",
        "stddef.h" => "`size_t`, `ptrdiff_t`, `NULL`, `offsetof`.",
        "limits.h" => "Integer limits.",
        "float.h" => "Floating-point limits.",
        "assert.h" => "`assert(e)`: a failed assertion raises the `USER` trap (off with `NDEBUG`).",
        "stdlib.h" => "Declares `malloc` & co., but dynamic allocation is not in kC: a call is `KC-E302`.",
        "stdio.h" => "Declares `printf` and `puts`, but kC has no standard output: a call is `KC-E645` (use `kfs_logf` or `trace`).",
        _ => null,
    };
}
