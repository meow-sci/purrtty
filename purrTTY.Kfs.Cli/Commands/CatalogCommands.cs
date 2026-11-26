// No game types in this file: the catalogue needs no game, so header, kinc and services run
// without the game's assemblies.

using purrTTY.Kfs.Catalog;

namespace purrTTY.Kfs.Cli.Commands;

/// <summary><c>header</c>, <c>kinc</c> and <c>services</c>: the catalogue's generated texts.</summary>
internal static class CatalogCommands
{
    /// <summary>Prints <c>purrtty.h</c> exactly as the extension registers it (CRLF line ends).</summary>
    public static int Header(TextWriter stdout)
    {
        stdout.Write(CatalogEmitter.Header(XCatalog.Catalog));
        stdout.Flush();
        return CliApp.ExitOk;
    }

    /// <summary>Prints <c>purrtty.kinc</c> exactly as the emitter generates it (CRLF line ends).</summary>
    public static int Kinc(TextWriter stdout)
    {
        stdout.Write(CatalogEmitter.Kinc(XCatalog.Catalog));
        stdout.Flush();
        return CliApp.ExitOk;
    }

    /// <summary>The catalogue as an aligned text table, or the emitter's markdown reference.</summary>
    public static int Services(TextWriter stdout, bool markdown)
    {
        if (markdown) return CliApp.Lines(stdout, CatalogEmitter.MarkdownReference(XCatalog.Catalog));
        foreach (string line in Table(XCatalog.Catalog)) stdout.WriteLine(line);
        return CliApp.ExitOk;
    }

    /// <summary>One header row, then one row per service in catalogue order: id, kind, cost, prototype, summary.</summary>
    internal static IReadOnlyList<string> Table(ServiceCatalog catalog)
    {
        var rows = new List<string[]> { new[] { "ID", "KIND", "COST", "PROTOTYPE", "SUMMARY" } };
        rows.AddRange(catalog.Services.Select(s => new[] { $"0x{s.Id:X4}", s.Kind, s.CostClass, s.Prototype, s.Summary }));
        int columns = rows[0].Length;
        var widths = Enumerable.Range(0, columns - 1).Select(c => rows.Max(r => r[c].Length)).ToArray();
        return rows.Select(r => string.Join("  ", r.Take(columns - 1).Select((cell, c) => cell.PadRight(widths[c])).Append(r[^1])).TrimEnd()).ToList();
    }
}
