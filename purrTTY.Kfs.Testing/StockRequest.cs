using System.Globalization;
using KSA.Kfs.Vm;
using KSA.Kfs.Vm.Tables;

namespace purrTTY.Kfs.Testing;

/// <summary>One decoded argument of a <see cref="StockRequest"/>.</summary>
/// <param name="Name">The argument's name from the services table (<c>heading_rad</c>, <c>at</c> …).</param>
/// <param name="CellType">The cell type: <c>f32</c>, <c>u32</c>, <c>i32</c>, <c>i64</c>, <c>cell</c>.</param>
/// <param name="Value">The value: the float, the integer, or the enumerator's number.</param>
/// <param name="Symbol">For an argument whose domain is an enumeration, the enumerator's name in lower case
/// (<c>apoapsis</c>, <c>up</c>, <c>vlf_prograde</c>); otherwise null.</param>
public sealed record StockArgument(string Name, string CellType, double Value, string? Symbol)
{
    /// <summary>The symbol, or the value: a float with at least one decimal (<c>1.0</c>, <c>1.5707964</c>), an integer as is.</summary>
    public override string ToString()
    {
        if (Symbol is not null) return Symbol;
        if (CellType != "f32") return Value.ToString(CultureInfo.InvariantCulture);
        string text = ((float)Value).ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') || !float.IsFinite((float)Value) ? text : text + ".0";
    }
}

/// <summary>
/// A request the stock host accepted (stage, throttle, attitude holds, burn planning …), decoded
/// with the services table: <c>ToString()</c> gives <c>stage</c>, <c>set_throttle(1.0)</c>,
/// <c>hold_heading(1.5707964, 1.5707964, up)</c>, <c>plan_circularize(apoapsis)</c>.
/// </summary>
/// <param name="TimeS">Simulated time of the dispatch whose ticks issued it, in seconds.</param>
/// <param name="Id">The service id.</param>
/// <param name="Name">The service name (<c>set_throttle</c>).</param>
/// <param name="Arguments">The arguments, one per argument cell group of the table (<c>i64</c> spans two cells).</param>
/// <param name="Cells">The raw argument cells.</param>
/// <param name="Results">The result cells the host issued synchronously (a burn handle for <c>plan_*</c>); empty otherwise.</param>
public sealed record StockRequest(double TimeS, ushort Id, string Name, IReadOnlyList<StockArgument> Arguments,
    IReadOnlyList<uint> Cells, IReadOnlyList<uint> Results)
{
    /// <summary>The burn handle a <c>plan_*</c> request returned, or null.</summary>
    public uint? Handle => Results.Count > 0 ? Results[0] : null;

    /// <summary>Argument <paramref name="index"/>'s value as a float.</summary>
    public float F(int index) => (float)Arguments[index].Value;

    /// <summary>Argument <paramref name="index"/>'s value as an unsigned integer.</summary>
    public uint U(int index) => (uint)Arguments[index].Value;

    /// <summary><c>name</c> for a service without arguments, else <c>name(arg, …)</c>.</summary>
    public override string ToString() => Arguments.Count == 0 ? Name : $"{Name}({string.Join(", ", Arguments)})";

    internal static StockRequest Decode(GameTables tables, ServiceRequest request, double timeS)
    {
        uint[] cells = request.Args ?? [];
        uint[] results = request.Results ?? [];
        if (!tables.Services.TryGetValue(request.Id, out ServiceInfo? svc))
            return new StockRequest(timeS, request.Id, $"0x{request.Id:X4}", [], cells, results);

        var args = new List<StockArgument>();
        int at = 0;
        for (int i = 0; i < svc.ArgTypes.Length; i++)
        {
            string type = svc.ArgTypes[i];
            int width = GameTables.CellsOf(type);
            if (at + width > cells.Length) break;
            uint cell = cells[at];
            double value = type switch
            {
                "f32" => BitConverter.UInt32BitsToSingle(cell),
                "i32" => (int)cell,
                "i64" => (long)(cell | ((ulong)cells[at + 1] << 32)),
                _ => cell,
            };
            string? domain = svc.Domains is { } d && i < d.Length ? d[i] : null;
            string name = svc.ArgumentNames is { } n && svc.Argument(i) < n.Length ? n[svc.Argument(i)] : $"arg{i}";
            args.Add(new StockArgument(name, type, value, EnumSymbol(tables, domain, cell)));
            at += width;
        }
        return new StockRequest(timeS, request.Id, svc.Name, args, cells, results);
    }

    private static string? EnumSymbol(GameTables tables, string? domain, uint value)
    {
        if (domain is null || !domain.StartsWith("enum:", StringComparison.Ordinal)) return null;
        if (tables.EnumDomain(domain) is not { } e) return null;
        if (e.Name == "roll_mode")
        {
            // Roll modes come from MSG_ATT_TARGET's field meaning (KFS_ROLL_*), not enums.json.
            foreach ((string sym, int v) in CatalogueEnums.All(tables))
            {
                if (v == value && sym.StartsWith("KFS_ROLL_", StringComparison.Ordinal))
                    return sym["KFS_ROLL_".Length..].ToLowerInvariant();
            }
            return null;
        }
        return tables.EnumsData.Enums.TryGetValue(e.Name, out List<string>? names) && value < (uint)names.Count
            ? names[(int)value].ToLowerInvariant()
            : null;
    }
}
