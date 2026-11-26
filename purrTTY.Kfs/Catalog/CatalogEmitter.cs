using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace purrTTY.Kfs.Catalog;

/// <summary>
/// Pure functions turning a <see cref="ServiceCatalog"/> into every artefact the game and the
/// tooling need: <c>services.json</c> rows, <c>enums.json</c> entries, the text of
/// <c>&lt;purrtty.h&gt;</c> and <c>purrtty.kinc</c>, the builtin names and a markdown reference.
/// Output is byte-stable: the same catalogue always produces the same text, on every OS.
/// </summary>
public static class CatalogEmitter
{
    /// <summary>Line end of the header and the kASM include, as in the game's stock headers.</summary>
    public const string HeaderNewLine = "\r\n";

    /// <summary>
    /// Options for emitted JSON text: indented with <c>\n</c> on every OS, non-ASCII written as is.
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private const int WrapColumn = 96;

    // ------------------------------------------------------------------ services.json

    /// <summary>
    /// The <c>services.json</c> row of one service. Ids are plain JSON integers (a ushort-typed
    /// value broke the game's reader in spike 06); only keys the loader's <c>ServiceRow</c> knows.
    /// </summary>
    public static JsonObject Row(ServiceDef s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new JsonObject
        {
            ["id"] = (int)s.Id,
            ["name"] = s.Name,
            // The text before '(' is the name kC binds; the names inside group onto the cells.
            ["kc"] = $"`{s.Name}({string.Join(", ", s.Params.Select(p => p.Name))})`",
            ["kscript"] = "—",
            ["symbol"] = s.Symbol,
            ["kind"] = s.Kind,
            ["args"] = ArgsProse(s),
            ["arg_types"] = Strings(s.ArgTypes),
            ["results"] = s.ResultCells.Count == 0 ? "—" : $"`{string.Join(" ", s.ResultCells)} value`",
            ["result_types"] = Strings(s.ResultCells),
            ["cost_class"] = s.CostClass,
            ["requires_stock"] = null,
            ["arg_domains"] = Strings(s.ArgDomains),
            ["host_checks"] = s.HostChecks,
            ["summary"] = s.Summary,
        };
    }

    /// <summary>Every service row, in catalogue order (fresh nodes on every call).</summary>
    public static JsonArray ServiceRows(ServiceCatalog catalog) =>
        new(catalog.Services.Select(s => (JsonNode?)Row(s)).ToArray());

    /// <summary>The service rows as indented JSON text (the snapshot form).</summary>
    public static string ServiceRowsJson(ServiceCatalog catalog) => ServiceRows(catalog).ToJsonString(JsonOptions);

    private static string ArgsProse(ServiceDef s) =>
        s.Params.Count == 0
            ? "—"
            : string.Join(", ", s.Params.Select(p => p.CellTypes.Count == 1
                ? $"`{p.CellTypes[0]} {p.Name}`"
                : $"`{p.CType} {p.Name}` ({string.Join(", ", p.CellTypes)})"));

    private static JsonArray Strings(IEnumerable<string> values) =>
        new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    // ------------------------------------------------------------------ enums.json

    /// <summary>
    /// The <c>enums.json</c> entries: key → value names by position, <c>reservedN</c> fillers
    /// included so a position equals the value. Header-only groups are not included.
    /// </summary>
    public static IReadOnlyList<(string Key, IReadOnlyList<string> Values)> EnumEntries(ServiceCatalog catalog) =>
        catalog.Enums.Where(e => e.InTable).Select(e => (e.Key!, e.Values)).ToArray();

    /// <summary>The entries as a JSON object (fresh nodes on every call).</summary>
    public static JsonObject EnumsObject(ServiceCatalog catalog)
    {
        var obj = new JsonObject();
        foreach (var (key, values) in EnumEntries(catalog)) obj[key] = Strings(values);
        return obj;
    }

    /// <summary>The entries as indented JSON text (the snapshot form).</summary>
    public static string EnumsJson(ServiceCatalog catalog) => EnumsObject(catalog).ToJsonString(JsonOptions);

    // ------------------------------------------------------------------ builtins

    /// <summary>The names to add to the kC compiler's builtin set: one per service.</summary>
    public static IReadOnlyList<string> BuiltinNames(ServiceCatalog catalog) => catalog.Services.Select(s => s.Name).ToArray();

    // ------------------------------------------------------------------ <purrtty.h>

    /// <summary>
    /// The text of <c>&lt;purrtty.h&gt;</c>: <c>#pragma once</c>, <c>#include &lt;kfs.h&gt;</c>, one
    /// <c>#define</c> per real enumeration value (none for fillers), then one commented prototype
    /// per service. No function bodies, so including it adds no code. <c>\r\n</c> line ends.
    /// </summary>
    public static string Header(ServiceCatalog catalog)
    {
        var h = new Lines();
        h.Add("#pragma once");
        h.Add($"/* <{XCatalog.HeaderName}> - purrTTY vehicle-parts services for kFS.");
        h.Add("   Generated from purrTTY's service catalogue; do not edit.");
        h.Add($"   Every function is a kFS service (SYSCALL 0x{KfsBuildInfo.ServiceIdFirst:X4}-0x{KfsBuildInfo.ServiceIdLast:X4}) provided by the");
        h.Add("   purrTTY mod: an image that calls one does not load without it. Indices are 0-based,");
        h.Add("   per vehicle, and frozen when a run starts - load and start, reset, stop and start -");
        h.Add("   until x_parts_rescan. Where there is no part (an index out of range, a part that has");
        h.Add("   left) every property reads 0, which for some properties is also a real value: check");
        h.Add("   the PRESENT property first. Writes are applied at the next frame boundary: read a");
        h.Add("   value back no earlier than the next tick. */");
        h.Add("#include <kfs.h>");

        foreach (var e in catalog.Enums)
        {
            h.Add("");
            h.Add($"/* {(e.Key ?? "constants")}: {e.Doc} */");
            foreach (var (name, value) in e.Constants) h.Add($"#define {name} {value}");
        }

        foreach (var group in catalog.Services.GroupBy(s => s.Id >> 4))
        {
            foreach (var s in group)
            {
                h.Add("");
                var comment = new List<string>(Wrap($"{s.Name} - {s.Summary}"));
                foreach (var p in s.Params) comment.AddRange(Wrap($"{p.Name}: {p.Doc}."));
                if (s.HasResult) comment.AddRange(Wrap($"Returns {s.ResultDoc}."));
                if (s.Doc.Length > 0) comment.AddRange(Wrap(s.Doc));
                if (s.HostChecks != "—") comment.AddRange(Wrap($"Rejected (program log, sys.rejected_requests) when: {s.HostChecks}."));
                comment.Add($"Service 0x{s.Id:X4} {s.Symbol}, cost class {s.CostClass}.");
                for (int i = 0; i < comment.Count; i++)
                {
                    string prefix = i == 0 ? "/* " : "   ";
                    string suffix = i == comment.Count - 1 ? " */" : "";
                    h.Add(prefix + comment[i] + suffix);
                }
                h.Add(s.Prototype + ";");
            }
        }
        return h.ToString();
    }

    // ------------------------------------------------------------------ purrtty.kinc

    /// <summary>
    /// The text of <c>purrtty.kinc</c>: <c>.equ</c> lines for the same constants as the header's
    /// <c>#define</c>s, for kASM programs (<c>.include "purrtty.kinc"</c>). kASM gets the
    /// <c>SVC_X_*</c> symbols from the services table by itself.
    /// </summary>
    public static string Kinc(ServiceCatalog catalog)
    {
        var k = new Lines();
        k.Add($"; {XCatalog.KincName} - purrTTY kFS constants for kASM. Generated from purrTTY's service");
        k.Add($"; catalogue; do not edit. The SVC_{XCatalog.ConstantPrefix}* service symbols come from <kfs.kinc>.");
        foreach (var e in catalog.Enums)
        {
            k.Add("");
            k.Add($"; {(e.Key ?? "constants")}: {e.Doc}");
            foreach (var (name, value) in e.Constants) k.Add($".equ {name}, {value}");
        }
        return k.ToString();
    }

    // ------------------------------------------------------------------ one line per service

    /// <summary>
    /// One line per service, in catalogue order (<c>purr services</c>):
    /// <c>0x5013  void x_light_set_on(uint32_t light, uint32_t on) — Switches a light's part on or off; …</c>.
    /// </summary>
    public static IReadOnlyList<string> ServiceLines(ServiceCatalog catalog) =>
        catalog.Services.Select(s => $"0x{s.Id:X4}  {s.Prototype} — {s.Summary}").ToArray();

    // ------------------------------------------------------------------ markdown

    /// <summary>A markdown reference of every service and constant (<c>\n</c> line ends).</summary>
    public static string MarkdownReference(ServiceCatalog catalog)
    {
        var md = new StringBuilder();
        md.Append($"# <{XCatalog.HeaderName}> reference\n\n");
        md.Append("Generated from purrTTY's service catalogue.\n\n");
        md.Append("| Id | Prototype | Kind | Cost | Summary |\n| --- | --- | --- | --- | --- |\n");
        foreach (var s in catalog.Services)
        {
            md.Append($"| `0x{s.Id:X4}` | `{s.Prototype}` | {s.Kind} | {s.CostClass} | {Cell(s.Summary)} |\n");
        }
        foreach (var s in catalog.Services)
        {
            md.Append($"\n## `{s.Name}`\n\n```c\n{s.Prototype};\n```\n\n{s.Summary}\n\n");
            foreach (var p in s.Params) md.Append($"- `{p.Name}` (`{p.CType}`, domain `{p.Domain}`): {p.Doc}.\n");
            if (s.HasResult) md.Append($"- Returns {s.ResultDoc}.\n");
            if (s.HostChecks != "—") md.Append($"- Rejected when: {s.HostChecks}.\n");
            md.Append($"- Symbol `{s.Symbol}`, cost class {s.CostClass}.\n");
            if (s.Doc.Length > 0) md.Append($"\n{s.Doc}\n");
        }
        foreach (var e in catalog.Enums)
        {
            md.Append($"\n## {(e.Key is null ? "Constants" : $"`{e.Key}`")}: {e.Doc}\n\n| Constant | Value |\n| --- | ---: |\n");
            foreach (var (name, value) in e.Constants) md.Append($"| `{name}` | {value} |\n");
        }
        return md.ToString();

        static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ helpers

    private static IEnumerable<string> Wrap(string text)
    {
        var line = new StringBuilder();
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > WrapColumn)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) yield return line.ToString();
    }

    private sealed class Lines
    {
        private readonly StringBuilder _text = new();
        public void Add(string line) => _text.Append(line).Append(HeaderNewLine);
        public override string ToString() => _text.ToString();
    }
}
