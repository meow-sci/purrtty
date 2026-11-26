using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace purrTTY.Kfs.Catalog;

/// <summary>The rule a <see cref="CatalogProblem"/> breaks.</summary>
public enum CatalogRule
{
    /// <summary>A service id is outside <c>0x5000–0x50FF</c>.</summary>
    IdOutsideBlock,
    /// <summary>Two services share an id.</summary>
    IdDuplicate,
    /// <summary>A service id is already used by the stock table.</summary>
    IdInStockTable,
    /// <summary>A service, parameter or enumeration name is not a valid identifier, lacks its prefix, or is not unique.</summary>
    Name,
    /// <summary>The kind is invalid or disagrees with the result.</summary>
    Kind,
    /// <summary>A parameter's C type does not flatten to exactly its declared cell types.</summary>
    ParamCells,
    /// <summary>The result is more than one cell or disagrees with the prototype's return type.</summary>
    Result,
    /// <summary>The <c>kc</c> argument names would not group onto the cells as the prototype lays them out.</summary>
    Grouping,
    /// <summary>A domain does not fit its cell type, is not one purrTTY uses, or leaves a float unchecked.</summary>
    Domain,
    /// <summary>An <c>enum:</c> domain names no catalogue enumeration, has a bad sub-range, admits a filler, or admits other values than intended.</summary>
    EnumDomain,
    /// <summary>The cost class is not one of the game's.</summary>
    CostClass,
    /// <summary>An enumeration definition is malformed (positions, fillers, keys, constants).</summary>
    Enum,
    /// <summary>Documentation text that would break the header or a table (blank, non-ASCII, comment end).</summary>
    Text,
    /// <summary>The emitted header does not parse back to the definitions.</summary>
    Header,
    /// <summary>An emitted services row would not be read as intended by the game's loader.</summary>
    Row,
    /// <summary>A definition is so malformed that checking it failed.</summary>
    Malformed,
}

/// <summary>One problem the self-check found.</summary>
/// <param name="Rule">The rule broken.</param>
/// <param name="Subject">What is wrong (a service as <c>0x5011 x_light_geti</c>, an enumeration key, <c>header</c>).</param>
/// <param name="Message">What exactly.</param>
public sealed record CatalogProblem(CatalogRule Rule, string Subject, string Message)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Subject}: {Message} [{Rule}]";
}

/// <summary>
/// Verifies a catalogue before anything is installed (plan §4.4): every mistake that would make
/// the game's loader reject the tables, make the kC compiler throw instead of diagnosing, or let
/// wrong values through silently. It re-derives the game's rules independently (its own
/// C-type → cell table, the loader's grouping algorithm) and parses the emitted header back.
/// Never throws for a bad definition; returns the problems (empty = pass).
/// </summary>
public static class CatalogSelfCheck
{
    // Independent of the catalogue's helpers: what the kC compiler pushes for each C type.
    private static readonly Dictionary<string, string[]> CTypeCells = new(StringComparer.Ordinal)
    {
        ["uint32_t"] = ["u32"],
        ["int32_t"] = ["i32"],
        ["float"] = ["f32"],
        ["vec3"] = ["f32", "f32", "f32"],
        ["quat"] = ["f32", "f32", "f32", "f32"],
        ["int64_t"] = ["i64"],
        ["double"] = ["f64"],
    };

    // The services table's cell types and how many VM cells each takes (GameTables.CellsOf).
    private static readonly Dictionary<string, int> CellSize = new(StringComparer.Ordinal)
    {
        ["u32"] = 1, ["i32"] = 1, ["f32"] = 1, ["ptr"] = 1, ["cell"] = 1, ["i64"] = 2, ["f64"] = 2,
    };

    // The keys of the game's ServiceRow; the reader rejects any other.
    private static readonly HashSet<string> RowKeys = new(StringComparer.Ordinal)
    {
        "id", "name", "args", "reserved", "kc", "kscript", "symbol", "kind", "arg_types", "results", "result_types",
        "cost_class", "per_value", "requires_stock", "arg_domains", "host_checks", "summary",
    };

    private static readonly HashSet<string> CKeywords = new(StringComparer.Ordinal)
    {
        "auto", "break", "case", "char", "const", "continue", "default", "do", "double", "else", "enum", "extern",
        "float", "for", "goto", "if", "inline", "int", "long", "register", "restrict", "return", "short", "signed",
        "sizeof", "static", "struct", "switch", "typedef", "union", "unsigned", "void", "volatile", "while",
        "uint32_t", "int32_t", "int64_t", "uint8_t", "int8_t", "uint16_t", "int16_t", "vec3", "quat", "bool",
    };

    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly Regex ServiceName = new("^x_[a-z0-9_]+$", RegexOptions.CultureInvariant);
    private static readonly Regex EnumKey = new("^x_[a-z0-9_]+$", RegexOptions.CultureInvariant);
    private static readonly Regex ValueName = new("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly Regex ConstantPrefix = new("^X_[A-Z0-9_]*_$", RegexOptions.CultureInvariant);
    private static readonly Regex EnumDomainSyntax = new(@"^enum:(?<key>[^\[\]]+)(\[(?<a>\d+)\.\.(?<b>\d+)\])?$", RegexOptions.CultureInvariant);
    private static readonly Regex Define = new(@"^#define\s+(?<name>\w+)\s+(?<value>\S+)$", RegexOptions.CultureInvariant);
    private static readonly Regex PrototypeLine = new(@"^(?<ret>\w+)\s+(?<name>\w+)\s*\((?<params>[^()]*)\)\s*;$", RegexOptions.CultureInvariant);

    /// <summary>Checks the real catalogue.</summary>
    public static IReadOnlyList<CatalogProblem> Run() => Run(XCatalog.Catalog);

    /// <summary>
    /// Checks <paramref name="catalog"/>. With <paramref name="stockIds"/> and
    /// <paramref name="stockEnumKeys"/> (from the stock tables) it also checks that no id or
    /// enumeration key collides with the game's.
    /// </summary>
    public static IReadOnlyList<CatalogProblem> Run(
        ServiceCatalog catalog,
        IReadOnlyCollection<ushort>? stockIds = null,
        IReadOnlyCollection<string>? stockEnumKeys = null)
    {
        var problems = new List<CatalogProblem>();
        try
        {
            CheckEnums(catalog, stockEnumKeys, problems);
            CheckServices(catalog, stockIds, problems);

            string header;
            try
            {
                header = CatalogEmitter.Header(catalog);
            }
            catch (Exception e)
            {
                problems.Add(new(CatalogRule.Header, "header", $"the emitter failed: {e.GetType().Name}: {e.Message}"));
                return problems;
            }
            problems.AddRange(CheckHeader(catalog, header));
            CheckRows(catalog, problems);
        }
        catch (Exception e)
        {
            problems.Add(new(CatalogRule.Malformed, "catalogue", $"checking failed: {e.GetType().Name}: {e.Message}"));
        }
        return problems;
    }

    // ------------------------------------------------------------------ enumerations

    private static void CheckEnums(ServiceCatalog catalog, IReadOnlyCollection<string>? stockEnumKeys, List<CatalogProblem> problems)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in catalog.Enums)
        {
            string who = e.Key ?? e.CPrefix;
            void Add(CatalogRule rule, string message) => problems.Add(new(rule, who, message));

            if (e.Key is not null)
            {
                if (!EnumKey.IsMatch(e.Key)) Add(CatalogRule.Name, $"enums.json key '{e.Key}' is not a lower-case identifier starting with '{XCatalog.NamePrefix}'");
                if (!keys.Add(e.Key)) Add(CatalogRule.Enum, $"enums.json key '{e.Key}' is defined twice");
                if (stockEnumKeys?.Contains(e.Key) == true) Add(CatalogRule.Enum, $"enums.json key '{e.Key}' collides with a stock enumeration");
            }
            if (!ConstantPrefix.IsMatch(e.CPrefix)) Add(CatalogRule.Name, $"constant prefix '{e.CPrefix}' must start with '{XCatalog.ConstantPrefix}' and end with '_'");
            if (e.Values.Count == 0) Add(CatalogRule.Enum, "has no values");

            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < e.Values.Count; i++)
            {
                string v = e.Values[i];
                if (EnumDef.IsFiller(v))
                {
                    if (v != EnumDef.Filler(i)) Add(CatalogRule.Enum, $"filler '{v}' at position {i} must be named '{EnumDef.Filler(i)}'");
                }
                else if (!ValueName.IsMatch(v))
                {
                    Add(CatalogRule.Name, $"value '{v}' at position {i} is not a lower-case identifier");
                }
                if (!names.Add(v)) Add(CatalogRule.Enum, $"value '{v}' is listed twice");
            }
            if (e.Values.Count > 0 && EnumDef.IsFiller(e.Values[^1])) Add(CatalogRule.Enum, "ends with a filler");
            if (e.FirstFloat is int first)
            {
                if (first <= 0 || first > e.Values.Count) Add(CatalogRule.Enum, $"first float property {first} is outside the list (1..{e.Values.Count})");
            }
            CheckText(who, "doc", e.Doc, problems);

            foreach (var (name, value) in e.Constants)
            {
                if (constants.TryGetValue(name, out var other)) Add(CatalogRule.Enum, $"constant {name} is also defined by {other}");
                else constants[name] = who;
                if (!Identifier.IsMatch(name)) Add(CatalogRule.Name, $"constant {name} is not a valid identifier");
            }
        }
    }

    // ------------------------------------------------------------------ services

    private static void CheckServices(ServiceCatalog catalog, IReadOnlyCollection<ushort>? stockIds, List<CatalogProblem> problems)
    {
        var ids = new Dictionary<ushort, string>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var s in catalog.Services)
        {
            string who = $"0x{s.Id:X4} {s.Name}";
            void Add(CatalogRule rule, string message) => problems.Add(new(rule, who, message));

            // Ids.
            if (!KfsBuildInfo.Owns(s.Id))
                Add(CatalogRule.IdOutsideBlock, $"id 0x{s.Id:X4} is outside purrTTY's block 0x{KfsBuildInfo.ServiceIdFirst:X4}-0x{KfsBuildInfo.ServiceIdLast:X4}");
            if (ids.TryGetValue(s.Id, out var first)) Add(CatalogRule.IdDuplicate, $"id 0x{s.Id:X4} is also used by {first}");
            else ids[s.Id] = s.Name;
            if (stockIds?.Contains(s.Id) == true) Add(CatalogRule.IdInStockTable, $"id 0x{s.Id:X4} is already in the stock services table");

            // Names.
            if (!ServiceName.IsMatch(s.Name)) Add(CatalogRule.Name, $"name '{s.Name}' is not a lower-case C identifier starting with '{XCatalog.NamePrefix}'");
            if (!names.Add(s.Name)) Add(CatalogRule.Name, $"name '{s.Name}' is used twice");
            var paramNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in s.Params)
            {
                if (!Identifier.IsMatch(p.Name) || CKeywords.Contains(p.Name)) Add(CatalogRule.Name, $"parameter name '{p.Name}' is not a usable C identifier");
                if (!paramNames.Add(p.Name)) Add(CatalogRule.Name, $"parameter name '{p.Name}' is used twice");
            }

            // Kind and cost.
            if (s.Kind is not (ServiceKinds.Request or ServiceKinds.RequestResult))
                Add(CatalogRule.Kind, $"kind '{s.Kind}' is not '{ServiceKinds.Request}' or '{ServiceKinds.RequestResult}'");
            else if ((s.Kind == ServiceKinds.RequestResult) != (s.ResultCells.Count > 0))
                Add(CatalogRule.Kind, $"kind '{s.Kind}' disagrees with {s.ResultCells.Count} result cell(s)");
            if (!CostClasses.All.Contains(s.CostClass)) Add(CatalogRule.CostClass, $"cost class '{s.CostClass}' is not one of {string.Join(", ", CostClasses.All)}");

            // Parameter cells: types, not just counts.
            foreach (var p in s.Params)
            {
                foreach (var cell in p.CellTypes)
                {
                    if (!CellSize.ContainsKey(cell)) Add(CatalogRule.ParamCells, $"parameter '{p.Name}' declares unknown cell type '{cell}'");
                }
                if (!CTypeCells.TryGetValue(p.CType, out var expected))
                    Add(CatalogRule.ParamCells, $"parameter '{p.Name}' has C type '{p.CType}', which the self-check cannot flatten");
                else if (!expected.SequenceEqual(p.CellTypes))
                    Add(CatalogRule.ParamCells, $"parameter '{p.Name}' is '{p.CType}', which pushes [{string.Join(",", expected)}], but declares cells [{string.Join(",", p.CellTypes)}]");
            }

            // Result: zero or one cell, matching the prototype.
            int resultCells = s.ResultCells.Sum(c => CellSize.GetValueOrDefault(c, 1));
            if (resultCells > 1) Add(CatalogRule.Result, $"result of {resultCells} cells; a result must be one cell (a wider one makes the kC compiler throw)");
            if (s.ResultCType == "void")
            {
                if (s.ResultCells.Count != 0) Add(CatalogRule.Result, "prototype returns void but the row has a result");
            }
            else if (s.ResultCells.Count == 0)
            {
                Add(CatalogRule.Result, $"prototype returns '{s.ResultCType}' but the row has no result");
            }
            else if (!CTypeCells.TryGetValue(s.ResultCType, out var rc) || !rc.SequenceEqual(s.ResultCells))
            {
                Add(CatalogRule.Result, $"prototype returns '{s.ResultCType}' but the row's result is [{string.Join(",", s.ResultCells)}]");
            }

            // Domains.
            foreach (var p in s.Params)
            {
                foreach (var cell in p.CellTypes.Distinct())
                {
                    string? why = DomainMisfit(p.Domain, cell);
                    if (why is not null) Add(CatalogRule.Domain, $"parameter '{p.Name}': {why}");
                }
                if (p.Domain.StartsWith("enum:", StringComparison.Ordinal)) CheckEnumDomain(catalog, s, p, problems);
                else if (p.Selects is not null) Add(CatalogRule.EnumDomain, $"parameter '{p.Name}' states selector values but its domain '{p.Domain}' is not an enumeration");
            }

            CheckGrouping(s, problems);

            // Text.
            if (string.IsNullOrWhiteSpace(s.Summary)) Add(CatalogRule.Text, "summary is blank (the loader rejects the row)");
            CheckText(who, "summary", s.Summary, problems);
            if (string.IsNullOrWhiteSpace(s.HostChecks)) Add(CatalogRule.Text, "host checks are blank (use '—' for none)");
            else if (s.HostChecks != "—") CheckText(who, "host checks", s.HostChecks, problems);
            CheckText(who, "doc", s.Doc, problems);
            CheckText(who, "result doc", s.ResultDoc, problems);
            if (s.ResultCType != "void" && string.IsNullOrWhiteSpace(s.ResultDoc)) Add(CatalogRule.Text, "result is undocumented");
            foreach (var p in s.Params) CheckText(who, $"parameter '{p.Name}' doc", p.Doc, problems);
        }
    }

    /// <summary>Why <paramref name="domain"/> cannot apply to a <paramref name="cell"/> cell, or null.</summary>
    private static string? DomainMisfit(string domain, string cell)
    {
        switch (domain)
        {
            case "any":
                return cell is "f32" or "f64" ? "a float cell with domain 'any' lets NaN and infinities through; use 'finite' or a range" : null;
            case "finite":
            case "finite:>=0":
                return cell == "f32" ? null : $"domain '{domain}' does not fit a '{cell}' cell";
        }
        if (domain.StartsWith("finite:[", StringComparison.Ordinal))
        {
            if (cell != "f32") return $"domain '{domain}' does not fit a '{cell}' cell";
            string[] parts = domain["finite:[".Length..].TrimEnd(']').Split(',');
            bool ok = domain.EndsWith(']') && parts.Length == 2
                && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float lo)
                && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hi)
                && lo <= hi;
            return ok ? null : $"domain '{domain}' is not a valid range";
        }
        if (domain.StartsWith("enum:", StringComparison.Ordinal))
        {
            return cell == "u32" ? null : $"domain '{domain}' does not fit a '{cell}' cell";
        }
        return $"domain '{domain}' is not one purrTTY services use (any, finite, finite:>=0, finite:[lo,hi], enum:)";
    }

    private static void CheckEnumDomain(ServiceCatalog catalog, ServiceDef s, ParamDef p, List<CatalogProblem> problems)
    {
        string who = $"0x{s.Id:X4} {s.Name}";
        void Add(string message) => problems.Add(new(CatalogRule.EnumDomain, who, $"parameter '{p.Name}': {message}"));

        var m = EnumDomainSyntax.Match(p.Domain);
        if (!m.Success)
        {
            Add($"domain '{p.Domain}' is not enum:<key> or enum:<key>[a..b]");
            return;
        }
        string key = m.Groups["key"].Value;
        var e = catalog.Enums.FirstOrDefault(x => x.Key == key);
        if (e is null)
        {
            Add($"domain '{p.Domain}' names no catalogue enumeration (the loader rejects the whole table)");
            return;
        }

        int lo = 0, hi = e.Values.Count - 1;
        if (m.Groups["a"].Success)
        {
            if (!int.TryParse(m.Groups["a"].Value, out lo) || !int.TryParse(m.Groups["b"].Value, out hi) || lo > hi || hi >= e.Values.Count)
            {
                Add($"sub-range of '{p.Domain}' is not inside {key}'s 0..{e.Values.Count - 1}");
                return;
            }
        }

        var admitted = Enumerable.Range(lo, hi - lo + 1).ToArray();
        var fillers = admitted.Where(i => EnumDef.IsFiller(e.Values[i])).ToArray();
        if (fillers.Length > 0) Add($"domain '{p.Domain}' admits filler value(s) {string.Join(", ", fillers)}");

        if (p.Selects is null)
        {
            Add($"domain '{p.Domain}' states no intended values (ParamDef.Selects)");
        }
        else if (!admitted.Where(i => !EnumDef.IsFiller(e.Values[i])).SequenceEqual(p.Selects.Order()))
        {
            Add($"domain '{p.Domain}' admits [{string.Join(",", admitted)}] but the parameter selects [{string.Join(",", p.Selects.Order())}]");
        }

        if (e.FirstFloat is int firstFloat && s.ResultCType is "uint32_t" or "float")
        {
            bool floatGetter = s.ResultCType == "float";
            var wrong = admitted.Where(i => (i >= firstFloat) != floatGetter).ToArray();
            if (wrong.Length > 0)
                Add($"a '{s.ResultCType}' getter admits {(floatGetter ? "integer" : "float")} properties {string.Join(", ", wrong)} of {key}");
        }
    }

    /// <summary>
    /// The loader's grouping of <c>kc</c> names onto cells (GameTables.Arguments): one-to-one when
    /// the counts match or no names are given; otherwise consecutive cells with the same domain
    /// string merge left to right. The result must be the prototype's own layout.
    /// </summary>
    private static void CheckGrouping(ServiceDef s, List<CatalogProblem> problems)
    {
        string who = $"0x{s.Id:X4} {s.Name}";
        var domains = s.ArgDomains;
        var intended = s.CellOwners;
        int count = domains.Count, names = s.Params.Count;
        if (names == count || names == 0) return;   // one to one, which is the prototype's layout

        var owner = new int[count];
        int group = 0, merges = count - names;
        for (int i = 1; i < count; i++)
        {
            if (merges > 0 && domains[i] == domains[i - 1]) merges--;
            else group++;
            owner[i] = group;
        }
        if (group + 1 != names || merges != 0)
        {
            problems.Add(new(CatalogRule.Grouping, who,
                $"the loader cannot group {names} kc names onto {count} cells (domains [{string.Join(",", domains)}]); the whole table load would fail"));
        }
        else if (!owner.SequenceEqual(intended))
        {
            problems.Add(new(CatalogRule.Grouping, who,
                $"the loader would group the cells as [{string.Join(",", owner)}] but the prototype lays them out as [{string.Join(",", intended)}]; give adjacent parameters different domains"));
        }
    }

    private static void CheckText(string who, string what, string text, List<CatalogProblem> problems)
    {
        if (text is null)
        {
            problems.Add(new(CatalogRule.Text, who, $"{what} is null"));
            return;
        }
        if (text.Any(c => c > 0x7E || (c < 0x20)))
            problems.Add(new(CatalogRule.Text, who, $"{what} must be printable ASCII on one line (it goes into the header)"));
        if (text.Contains("*/", StringComparison.Ordinal) || text.Contains("/*", StringComparison.Ordinal))
            problems.Add(new(CatalogRule.Text, who, $"{what} contains a C comment delimiter"));
        if (text.Contains(EnumDef.FillerPrefix, StringComparison.OrdinalIgnoreCase))
            problems.Add(new(CatalogRule.Text, who, $"{what} contains '{EnumDef.FillerPrefix}', which the header must not"));
    }

    // ------------------------------------------------------------------ header parse-back

    /// <summary>
    /// Parses <paramref name="header"/> back and checks it against <paramref name="catalog"/>:
    /// <c>\r\n</c> line ends, ASCII, <c>#pragma once</c> and <c>#include &lt;kfs.h&gt;</c>, no
    /// function body, no filler name, exactly the expected <c>#define</c>s, and exactly one
    /// prototype per service whose return type, parameter types, names and flattened cells match
    /// the definition.
    /// </summary>
    public static IReadOnlyList<CatalogProblem> CheckHeader(ServiceCatalog catalog, string header)
    {
        var problems = new List<CatalogProblem>();
        void Add(string message) => problems.Add(new(CatalogRule.Header, "header", message));

        for (int i = 0; i < header.Length; i++)
        {
            if (header[i] == '\n' && (i == 0 || header[i - 1] != '\r')) { Add("has a line end that is not \\r\\n"); break; }
            if (header[i] == '\r' && (i + 1 == header.Length || header[i + 1] != '\n')) { Add("has a lone \\r"); break; }
        }
        if (header.Any(c => c > 0x7E || (c < 0x20 && c is not '\r' and not '\n'))) Add("is not printable ASCII");
        if (header.Contains(EnumDef.FillerPrefix, StringComparison.OrdinalIgnoreCase)) Add($"mentions '{EnumDef.FillerPrefix}'");

        string code = StripComments(header, out bool unterminated);
        if (unterminated) Add("has an unterminated comment");
        if (code.Contains('{') || code.Contains('}')) Add("contains a function body or other braces; it must hold only #defines and prototypes");

        var lines = code.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.FirstOrDefault() != "#pragma once") Add("does not start with #pragma once");
        if (!lines.Contains("#include <kfs.h>")) Add("does not include <kfs.h>");

        var expectedDefines = catalog.Enums.SelectMany(e => e.Constants).GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First().Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var seenDefines = new HashSet<string>(StringComparer.Ordinal);
        var prototypes = new Dictionary<string, Match>(StringComparer.Ordinal);

        foreach (string line in lines)
        {
            if (line is "#pragma once" or "#include <kfs.h>") continue;
            var d = Define.Match(line);
            if (d.Success)
            {
                string name = d.Groups["name"].Value, value = d.Groups["value"].Value;
                if (!seenDefines.Add(name)) Add($"defines {name} twice");
                if (!expectedDefines.TryGetValue(name, out var want)) Add($"defines {name}, which is no catalogue constant");
                else if (want != value) Add($"defines {name} as {value}, the catalogue says {want}");
                continue;
            }
            var p = PrototypeLine.Match(line);
            if (p.Success)
            {
                string name = p.Groups["name"].Value;
                if (!prototypes.TryAdd(name, p)) Add($"declares {name} twice");
                continue;
            }
            Add($"has an unexpected line: {line}");
        }
        foreach (var name in expectedDefines.Keys.Where(n => !seenDefines.Contains(n))) Add($"does not define {name}");

        foreach (var s in catalog.Services)
        {
            string who = $"0x{s.Id:X4} {s.Name}";
            if (!prototypes.Remove(s.Name, out var proto))
            {
                problems.Add(new(CatalogRule.Header, who, "has no prototype in the header"));
                continue;
            }
            void P(string message) => problems.Add(new(CatalogRule.Header, who, message));

            string ret = proto.Groups["ret"].Value;
            if (ret != s.ResultCType) P($"header returns '{ret}', the definition '{s.ResultCType}'");
            string[] retCells = ret == "void" ? [] : CTypeCells.GetValueOrDefault(ret) ?? ["?"];
            if (!retCells.SequenceEqual(s.ResultCells)) P($"header return '{ret}' pushes [{string.Join(",", retCells)}], the row returns [{string.Join(",", s.ResultCells)}]");

            string ps = proto.Groups["params"].Value.Trim();
            var parsed = ps == "void"
                ? []
                : ps.Split(',').Select(x => x.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
            if (parsed.Any(x => x.Length != 2))
            {
                P($"header parameter list '{ps}' is not 'type name, ...'");
                continue;
            }
            if (parsed.Count != s.Params.Count)
            {
                P($"header declares {parsed.Count} parameter(s), the definition {s.Params.Count}");
                continue;
            }
            for (int i = 0; i < parsed.Count; i++)
            {
                if (parsed[i][0] != s.Params[i].CType || parsed[i][1] != s.Params[i].Name)
                    P($"header parameter {i} is '{parsed[i][0]} {parsed[i][1]}', the definition '{s.Params[i].CType} {s.Params[i].Name}'");
            }
            var headerCells = parsed.SelectMany(x => CTypeCells.GetValueOrDefault(x[0]) ?? ["?"]).ToArray();
            if (!headerCells.SequenceEqual(s.ArgTypes))
                P($"header parameters push [{string.Join(",", headerCells)}], the row declares [{string.Join(",", s.ArgTypes)}]");
        }
        foreach (var extra in prototypes.Keys) Add($"declares {extra}, which is no catalogue service");
        return problems;
    }

    private static string StripComments(string text, out bool unterminated)
    {
        unterminated = false;
        var sb = new System.Text.StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*')
            {
                int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) { unterminated = true; break; }
                // Keep line structure so a comment never joins two lines.
                sb.Append(' ').Append('\n', text.AsSpan(i, end - i).Count('\n'));
                i = end + 2;
            }
            else if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
            }
            else
            {
                if (text[i] != '\r') sb.Append(text[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ rows

    private static void CheckRows(ServiceCatalog catalog, List<CatalogProblem> problems)
    {
        foreach (var s in catalog.Services)
        {
            string who = $"0x{s.Id:X4} {s.Name}";
            JsonObject row;
            try
            {
                // Through text, as the game reads it.
                row = JsonNode.Parse(CatalogEmitter.Row(s).ToJsonString())!.AsObject();
            }
            catch (Exception e)
            {
                problems.Add(new(CatalogRule.Row, who, $"row could not be emitted: {e.Message}"));
                continue;
            }
            foreach (var key in row.Select(kv => kv.Key).Where(k => !RowKeys.Contains(k)))
                problems.Add(new(CatalogRule.Row, who, $"row has key '{key}', which the game's reader rejects"));
            if (row["id"] is not JsonValue id || id.GetValueKind() != JsonValueKind.Number || !id.TryGetValue(out int idValue) || idValue != s.Id)
                problems.Add(new(CatalogRule.Row, who, "row id is not a plain JSON integer equal to the service id"));
        }
    }
}
