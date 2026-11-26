using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KSA.Kfs.Host.Views;
using KSA.Kfs.KAsm;
using KSA.Kfs.Vm.Tables;
using purrTTY.Kfs.Catalog;

namespace purrTTY.Kfs.Lsp.Index;

/// <summary>
/// Everything of plan §6.5 that depends on the tables and the processor part but not on the vehicle
/// layout: services (stock and <c>x_</c>, with the catalogue's documentation for the mod's), kASM
/// mnemonics with stack effect and cost, <c>sys.*</c>, <c>&lt;kfs.kinc&gt;</c>'s constants and
/// types, <c>purrtty.kinc</c>'s constants, the <c>#pragma kfs</c> and preprocessor directives, the
/// kASM directives, the kC keywords and the diagnostic manual. Immutable once built.
/// </summary>
public sealed partial class TableIndex
{
    private readonly GameTables _tables;

    private TableIndex(GameTables tables, PartDescription part)
    {
        _tables = tables;
        Part = part;
    }

    /// <summary>The part costs are given for.</summary>
    public PartDescription Part { get; }

    /// <summary>kC names: services by their kC name, <c>sys</c>, the keywords.</summary>
    public IReadOnlyDictionary<string, IndexEntry> KcNames { get; private set; } = null!;

    /// <summary>kASM symbols of <c>&lt;kfs.kinc&gt;</c> (<c>SVC_*</c>, <c>sys</c>, <c>sys.*</c>, <c>TRAP_*</c>, catalogue enums) and <c>purrtty.kinc</c> (<c>X_*</c>), with <see cref="IndexEntry.Origin"/> naming the include.</summary>
    public IReadOnlyDictionary<string, IndexEntry> KasmNames { get; private set; } = null!;

    /// <summary>kASM types of <c>&lt;kfs.kinc&gt;</c> and the scalars.</summary>
    public IReadOnlyDictionary<string, IndexEntry> KasmTypes { get; private set; } = null!;

    /// <summary>kASM mnemonics, case-insensitive.</summary>
    public IReadOnlyDictionary<string, IndexEntry> Opcodes { get; private set; } = null!;

    /// <summary>kASM directives with their dot (<c>.func</c>), case-insensitive.</summary>
    public IReadOnlyDictionary<string, IndexEntry> KasmDirectives { get; private set; } = null!;

    /// <summary>Preprocessor directive names without the <c>#</c>.</summary>
    public IReadOnlyDictionary<string, IndexEntry> PreprocessorDirectives { get; private set; } = null!;

    /// <summary><c>#pragma kfs</c> directive names.</summary>
    public IReadOnlyDictionary<string, IndexEntry> Pragmas { get; private set; } = null!;

    /// <summary>The task options (<c>rate_hz</c>, <c>divisor</c>, <c>priority</c>).</summary>
    public IReadOnlyList<IndexEntry> TaskOptions { get; private set; } = null!;

    /// <summary>The accepted kC keywords.</summary>
    public IReadOnlyList<IndexEntry> Keywords { get; private set; } = null!;

    /// <summary><c>kfs_sys_t</c>: the members of <c>sys</c>.</summary>
    public MemberShape Sys { get; private set; } = null!;

    /// <summary>Services by id.</summary>
    public IReadOnlyDictionary<int, IndexEntry> ServicesById { get; private set; } = null!;

    /// <summary>The tick rates the tables allow.</summary>
    public IReadOnlyList<int> TickRates => _tables.AllowedTickHz;

    /// <summary>The default tick rate.</summary>
    public int DefaultTickHz => _tables.CatalogueData.DefaultTickHz;

    /// <summary>
    /// The manual page of a diagnostic code (<c>KC-E003</c>) as Markdown — the game's
    /// <c>DiagnosticManual.Page</c>, the only long-form text there is — or null for a code that is not
    /// in the registry.
    /// </summary>
    public string? Manual(string code)
    {
        string page = DiagnosticManual.Page(_tables, code);
        if (page.EndsWith(": not in the diagnostics registry", StringComparison.Ordinal)) return null;
        string[] lines = page.TrimEnd('\n').Split('\n');
        var md = new StringBuilder();
        md.Append("**").Append(lines[0]).Append("**");
        foreach (string line in lines.Skip(1)) md.Append("\n\n").Append(line);
        md.Append("\n\nFrom the game's diagnostics registry (`kfs manual ").Append(code).Append("`).");
        return md.ToString();
    }

    /// <summary>Builds the index; <paramref name="headers"/> supplies the prototypes of stock services.</summary>
    public static TableIndex Build(GameTables tables, PartDescription part, HeaderIndex headers)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(headers);
        var index = new TableIndex(tables, part);
        index.Fill(headers);
        return index;
    }

    private void Fill(HeaderIndex headers)
    {
        var kc = new Dictionary<string, IndexEntry>(StringComparer.Ordinal);
        var kasm = new Dictionary<string, IndexEntry>(StringComparer.Ordinal);
        var byId = new Dictionary<int, IndexEntry>();
        string ticks = string.Join(", ", _tables.AllowedTickHz.Select(t => t.ToString(CultureInfo.InvariantCulture)));
        string defaultTick = _tables.CatalogueData.DefaultTickHz.ToString(CultureInfo.InvariantCulture);
        string Fill(string doc) => doc.Replace("{tick}", ticks, StringComparison.Ordinal).Replace("{default}", defaultTick, StringComparison.Ordinal);

        // Services.
        foreach (ServiceRow row in _tables.ServicesData.Services)
        {
            if (row.Reserved || row.Kc is null) continue;
            string? kcName = KcName().Match(row.Kc) is { Success: true } m ? m.Groups[1].Value : null;
            _tables.Services.TryGetValue((ushort)row.Id, out ServiceInfo? info);
            ServiceDef? def = KfsBuildInfo.Owns((ushort)row.Id) && XCatalog.Catalog.ById((ushort)row.Id) is { } d && d.Name == kcName ? d : null;
            string? prototype = def is null ? null : def.Prototype + ";";
            if (prototype is null && kcName is not null && headers.Names.TryGetValue(kcName, out IndexEntry? proto) && proto.Kind == EntryKind.Function)
                prototype = proto.Signature;
            string body = ServiceBody(row, info, def);
            if (kcName is not null)
            {
                var entry = new IndexEntry(kcName, EntryKind.Service, prototype is null ? $"service 0x{row.Id:X4}" : Shorten(prototype.TrimEnd(';')),
                    prototype ?? row.Kc.Trim('`'), Markup.Join(body, ServiceFooter(row, def, kasm: false)))
                {
                    Origin = def is not null ? XCatalog.HeaderName : "kfs.h",
                    SortGroup = 1,
                    Parameters = def is not null ? def.Params.Select(p => p.Name).ToArray()
                        : prototype is not null && Parens().Match(prototype) is { Success: true } pm ? HeaderIndex.SplitParameters(pm.Groups[1].Value) : null,
                };
                kc[kcName] = entry;
                byId[row.Id] = entry;
            }
            if (row.Symbol is { } symbol)
            {
                kasm[symbol] = new IndexEntry(symbol, EntryKind.Constant, $"= 0x{row.Id:X4} ({row.Name})",
                    $".include <kfs.kinc>\nSYSCALL {symbol}    ; = 0x{row.Id:X4}", Markup.Join(
                        kcName is null ? null : $"The service {Markup.Code(kcName)}" + (prototype is null ? "." : $": {Markup.Code(prototype.TrimEnd(';'))}."),
                        body, KasmCells(info), ServiceFooter(row, def, kasm: true)), "kasm")
                {
                    Origin = "kfs.kinc",
                    SortGroup = 1,
                };
            }
        }

        // The system block.
        var sysMembers = new List<ShapeMember>();
        foreach (SysBlockField f in _tables.SysBlockData.Fields)
        {
            string ctype = ShapeBuilder.CType(f.Type);
            uint address = 0xA0000000u + (uint)f.Offset;
            string unit = Markup.UnitOf(f.Name) is { } u ? $" Unit: {u}." : "";
            string doc = Markup.Join($"{f.Meaning}.{unit}",
                $"Offset {f.Offset} of the read-only system block (`sys` at 0xA0000000: address 0x{address:X8}). Writing it traps (`READ_ONLY`).");
            sysMembers.Add(new ShapeMember(f.Name, new IndexEntry(f.Name, EntryKind.Field, $"{ctype} {f.Name}", $"const {ctype} sys.{f.Name};", doc) { SortGroup = 1 }, null));
            kasm["sys." + f.Name] = new IndexEntry("sys." + f.Name, EntryKind.Constant, $"= 0x{address:X8} ({ctype})",
                $".include <kfs.kinc>\nLDG{(ctype.EndsWith("64_t", StringComparison.Ordinal) ? "64" : "")} sys.{f.Name}    ; = 0x{address:X8}", doc, "kasm")
            {
                Origin = "kfs.kinc",
                SortGroup = 2,
            };
        }
        Sys = new MemberShape("kfs_sys_t", sysMembers);
        kc["sys"] = new IndexEntry("sys", EntryKind.Variable, "const kfs_sys_t sys", "extern const kfs_sys_t sys __at(0xA0000000);",
            Markup.Join("The read-only system block: cycle counts, time, task and reset information. Members: "
                + string.Join(", ", sysMembers.Select(s => Markup.Code(s.Name))) + ".", "Declared in `<kfs.h>`."))
        {
            Origin = "kfs.h",
            SortGroup = 2,
        };
        kasm["sys"] = new IndexEntry("sys", EntryKind.Constant, "= 0xA0000000", ".include <kfs.kinc>\n.equ sys, 0xA0000000", "The address of the read-only system block; `sys.<field>` names each field's address.", "kasm")
        {
            Origin = "kfs.kinc",
            SortGroup = 2,
        };

        // Traps.
        foreach (TrapRow trap in _tables.TrapsData.Traps)
        {
            kasm["TRAP_" + trap.Name] = new IndexEntry("TRAP_" + trap.Name, EntryKind.Constant, $"= {trap.Code}", $".include <kfs.kinc>\n.equ TRAP_{trap.Name}, {trap.Code}",
                $"Trap code {trap.Code}: {trap.Cause}.", "kasm")
            {
                Origin = "kfs.kinc",
                SortGroup = 3,
            };
        }

        // <kfs.kinc>'s other constants: the catalogue enums (also KFS_* defines of <kfs.h>), and its types.
        var builtin = new BuiltinIncludes(_tables, _tables.BenchLayout, "kfs.kinc");
        foreach ((string name, long value) in builtin.Constants)
        {
            if (kasm.ContainsKey(name)) continue;
            string? kcDoc = headers.Names.TryGetValue(name, out IndexEntry? define) ? $"Also {Markup.Code($"#define {name}")} in `<{define.Origin}>`." : null;
            kasm[name] = new IndexEntry(name, EntryKind.Constant, $"= {value}", $".include <kfs.kinc>\n.equ {name}, {value}",
                Markup.Join("A catalogue enumeration value of the game's tables.", kcDoc), "kasm")
            {
                Origin = "kfs.kinc",
                SortGroup = 3,
            };
        }
        var types = new Dictionary<string, IndexEntry>(StringComparer.Ordinal);
        foreach ((string name, AsmType type) in BuiltinIncludes.Scalars) types[name] = KasmType(type, null);
        foreach ((string name, AsmType type) in builtin.Types) types[name] = KasmType(type, "kfs.kinc");
        KasmTypes = types;

        // purrtty.kinc: the catalogue's constants for kASM (.include "purrtty.kinc", a project file).
        foreach (EnumDef e in XCatalog.Catalog.Enums)
        {
            foreach ((string name, int value) in e.Constants)
            {
                kasm.TryAdd(name, new IndexEntry(name, EntryKind.Constant, $"= {value}", $".include \"{XCatalog.KincName}\"\n.equ {name}, {value}",
                    Markup.Join($"{e.Key ?? "constants"}: {e.Doc}.",
                        $"From purrTTY's `{XCatalog.KincName}` (a project file: `purr-kfs kinc` prints it); the same value as `#define {name}` in `<{XCatalog.HeaderName}>`."), "kasm")
                {
                    Origin = XCatalog.KincName,
                    SortGroup = 3,
                });
            }
        }

        // Opcodes.
        var opcodes = new Dictionary<string, IndexEntry>(StringComparer.OrdinalIgnoreCase);
        var rows = new Dictionary<string, OpcodeRow>(StringComparer.OrdinalIgnoreCase);
        foreach (OpcodeRow row in _tables.OpcodesData.Rows)
        {
            foreach (string[] op in row.Ops)
            {
                if (op.Length > 0) rows.TryAdd(op[0], row);
            }
        }
        foreach (OpcodeInfo info in _tables.OpcodesByMnemonic.Values) opcodes[info.Mnemonic] = OpcodeEntry(info, rows.GetValueOrDefault(info.Mnemonic));
        Opcodes = opcodes;

        // Directives, pragmas, keywords.
        KasmDirectives = StaticTables.KasmDirectives.ToDictionary(d => d.Name,
            d => new IndexEntry(d.Name, EntryKind.KasmDirective, d.Syntax, d.Syntax, Fill(d.Doc), "kasm") { SortGroup = 1 }, StringComparer.OrdinalIgnoreCase);
        PreprocessorDirectives = StaticTables.PreprocessorDirectives.ToDictionary(d => d.Name,
            d => new IndexEntry(d.Name, EntryKind.Directive, d.Syntax, d.Syntax, d.Doc) { SortGroup = d.Name is "include" or "define" or "pragma" ? 0 : 1 }, StringComparer.Ordinal);
        Pragmas = StaticTables.Pragmas.ToDictionary(d => d.Name,
            d => new IndexEntry(d.Name, EntryKind.Pragma, d.Syntax, d.Syntax, Fill(d.Doc)) { SortGroup = d.Name == "strict" ? 9 : 1 }, StringComparer.Ordinal);
        TaskOptions = StaticTables.TaskOptions.Select(o => new IndexEntry(o.Name, EntryKind.Value, o.Name + "=", null, o.Doc) { InsertText = o.Name + "=" }).ToArray();
        Keywords = StaticTables.Keywords.Select(k => new IndexEntry(k, EntryKind.Keyword, "keyword", null, "") { SortGroup = 6 }).ToArray();

        KcNames = kc;
        KasmNames = kasm;
        ServicesById = byId;
    }

    // ------------------------------------------------------------------ services

    private string ServiceBody(ServiceRow row, ServiceInfo? info, ServiceDef? def)
    {
        var parts = new List<string?>();
        parts.Add(def?.Summary ?? row.Summary);
        if (def is not null)
        {
            parts.Add(def.Params.Count == 0
                ? "**Arguments:** none."
                : "**Arguments:**\n" + string.Join("\n", def.Params.Select(p =>
                    $"- {Markup.Code(p.Name)} ({Markup.Code(p.CType)}{(p.Domain == "any" ? "" : ", checked " + Markup.Code(p.Domain))}): {p.Doc}")));
            parts.Add(def.HasResult ? $"**Returns** {Markup.Code(def.ResultCType)}: {def.ResultDoc}" : "**Returns** nothing (a request).");
            parts.Add(def.Doc);
        }
        else
        {
            parts.Add(row.Args is { Length: > 0 } args && args != "—" ? "**Arguments:** " + args : "**Arguments:** none.");
            string? checks = Domains(info);
            if (checks is not null) parts.Add("**Checked by the VM when issued:** " + checks);
            parts.Add(row.Results is { Length: > 0 } results && results != "—" ? "**Returns:** " + results : "**Returns** nothing (a request).");
        }
        if (row.HostChecks is { Length: > 0 } host && host != "—") parts.Add("**Rejected by the host when:** " + host.TrimEnd('.') + ".");
        if (row.RequiresStock is { } stock)
        {
            string message = stock switch
            {
                "GUIDANCE" => "MSG_ATT_TARGET",
                "ATTITUDE_CONTROL" => "MSG_WRENCH_CMD",
                "NAVIGATION" => "MSG_NAV_STATE",
                _ => null,
            } ?? "its message";
            parts.Add($"**Steers the stock {stock} block:** pilot mode only. A program that claims `ALLOCATION` or publishes `{message}` cannot call it (`KC-E501`).");
        }
        return Markup.Join(parts.ToArray());
    }

    private static string? Domains(ServiceInfo? info)
    {
        if (info?.Domains is not { Length: > 0 } domains) return null;
        var checks = new List<string>();
        for (int i = 0; i < domains.Length; i++)
        {
            if (domains[i] == "any") continue;
            int arg = info.Argument(i);
            string name = info.ArgumentNames is { } names && arg < names.Length ? names[arg] : $"cell {i}";
            string text = $"{Markup.Code(name)} {Markup.Code(domains[i])}";
            if (!checks.Contains(text)) checks.Add(text);
        }
        return checks.Count == 0 ? null : string.Join(", ", checks) + ".";
    }

    private static string? KasmCells(ServiceInfo? info)
    {
        if (info is null) return null;
        string args = info.ArgCells == 0 ? "no argument cells" : $"{info.ArgCells} argument cell{(info.ArgCells == 1 ? "" : "s")} ({string.Join(", ", info.ArgTypes)}, first pushed first)";
        return $"In kASM: push {args}, then `SYSCALL`; it leaves {info.ResultCells} result cell{(info.ResultCells == 1 ? "" : "s")}.";
    }

    private string ServiceFooter(ServiceRow row, ServiceDef? def, bool kasm)
    {
        string cost = row.CostClass is { } cls ? $"cost class {cls}{Cycles(cls)}" : "no cost class";
        if (row.PerValue) cost += $" + {_tables.CostsData.LogCyclesPerValue} per formatted value";
        string where = def is not null
            ? kasm ? $"purrTTY mod service (`.include <kfs.kinc>`; constants in `{XCatalog.KincName}`)" : $"purrTTY mod service, `#include <{XCatalog.HeaderName}>`; an image that calls it loads only with the mod"
            : kasm ? "stock service (`.include <kfs.kinc>`)" : "stock service, `#include <kfs.h>`";
        string symbol = row.Symbol is { } s ? $" · {Markup.Code(s)}" : "";
        return $"Service id `0x{row.Id:X4}` ({row.Id.ToString(CultureInfo.InvariantCulture)}){symbol} · {row.Kind ?? "request"} · {cost} · {where}.";
    }

    private string Cycles(string costClass) =>
        Part.Costs.ServiceClassCycles.TryGetValue(costClass, out int cycles)
            ? $" ({cycles.ToString(CultureInfo.InvariantCulture)} cycles on {Part.Name}, {Part.Costs.Core})"
            : "";

    // ------------------------------------------------------------------ opcodes

    private IndexEntry OpcodeEntry(OpcodeInfo info, OpcodeRow? row)
    {
        var immediates = new List<string>();
        for (int i = 0; i < info.Imm.Length; i++)
        {
            string name = i < info.ImmNames.Length && info.ImmNames[i].Length > 0 ? info.ImmNames[i] : "imm";
            immediates.Add($"{name}:{info.Imm[i].ToString().ToLowerInvariant()}");
        }
        string signature = immediates.Count == 0 ? info.Mnemonic : $"{info.Mnemonic} {string.Join(", ", immediates)}";
        string pops = info.Pops?.ToString(CultureInfo.InvariantCulture) ?? "variable";
        string pushes = info.Pushes?.ToString(CultureInfo.InvariantCulture) ?? "variable";
        string stack = row?.Stack is { Length: > 0 } prose ? $"**Stack:** {prose} (pops {pops}, pushes {pushes})." : $"**Stack:** pops {pops}, pushes {pushes}.";
        string summary = info.Summary.Length > 0 ? info.Summary : row?.Summary is { Count: > 0 } s ? string.Join(" ", s) : "";
        string immediatesDoc = immediates.Count == 0 ? "No immediate." : "**Immediate" + (immediates.Count == 1 ? ":** " : "s:** ") + string.Join(", ", immediates.Select(Markup.Code)) + ".";
        return new IndexEntry(info.Mnemonic, EntryKind.Opcode, $"{signature} — {Cost(info)}", signature,
            Markup.Join(summary, stack, immediatesDoc,
                $"**Cost on {Part.Name} ({Part.Costs.Core}):** {Cost(info)}.",
                $"Opcode `0x{info.Code:X2}`, {info.Size} byte{(info.Size == 1 ? "" : "s")}, category `{info.Category}`, kVM spec §{info.Section}."), "kasm")
        {
            SortGroup = 1,
        };
    }

    private string Cost(OpcodeInfo info)
    {
        CoreCostTable costs = Part.Costs;
        int cycles;
        bool soft;
        if (info.Code < costs.OpcodeOverride.Length && costs.OpcodeOverride[info.Code] is { } o) (cycles, soft) = o;
        else if (info.CategoryIndex >= 0 && info.CategoryIndex < costs.Cycles.Length) (cycles, soft) = (costs.Cycles[info.CategoryIndex], costs.Soft[info.CategoryIndex]);
        else return "unknown";
        string text = $"{cycles.ToString(CultureInfo.InvariantCulture)} cycle{(cycles == 1 ? "" : "s")}";
        if (info.CategoryIndex >= 0 && info.CategoryIndex < costs.PerWord.Length && costs.PerWord[info.CategoryIndex] > 0)
            text += $" + {costs.PerWord[info.CategoryIndex].ToString(CultureInfo.InvariantCulture)} per word";
        if (soft) text += " (a soft routine on this core)";
        return text;
    }

    private static IndexEntry KasmType(AsmType type, string? origin)
    {
        string fields = type.Fields.Count == 0 ? "a scalar" : $"fields {string.Join(", ", type.Fields.Take(12).Select(f => $"{Markup.Code(f.Key)} +{f.Value}"))}{(type.Fields.Count > 12 ? ", …" : "")}";
        return new IndexEntry(type.Name, EntryKind.Type, $"size {type.Size}, align {type.Align}", null,
            $"kASM type {Markup.Code(type.Name)}: size {type.Size}, align {type.Align}; {fields}." + (origin is null ? " Always available." : $" From `<{origin}>`."))
        {
            Origin = origin,
            SortGroup = 4,
        };
    }

    private static string Shorten(string s) => s.Length <= 80 ? s : s[..77] + "…";

    [GeneratedRegex(@"`?([A-Za-z_]\w*)\s*\(")]
    private static partial Regex KcName();

    [GeneratedRegex(@"\(([^()]*)\)")]
    private static partial Regex Parens();
}
