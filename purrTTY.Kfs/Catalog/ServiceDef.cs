namespace purrTTY.Kfs.Catalog;

/// <summary>
/// One purrTTY kFS service: everything the services-table row, the kC prototype, the builtin name
/// and the documentation are generated from.
/// </summary>
/// <param name="Id">Service id, inside <see cref="KfsBuildInfo.ServiceIdFirst"/>–<see cref="KfsBuildInfo.ServiceIdLast"/>.</param>
/// <param name="Name">The kC function name (<c>x_…</c>); also the row's <c>name</c>.</param>
/// <param name="Kind"><see cref="ServiceKinds.Request"/> or <see cref="ServiceKinds.RequestResult"/>.</param>
/// <param name="Params">The parameters, in prototype order.</param>
/// <param name="ResultCType">The prototype's return type; <c>void</c> for a request.</param>
/// <param name="ResultCells">The row's <c>result_types</c>: empty, or exactly one cell.</param>
/// <param name="CostClass">A cost class of the game's cost table (<see cref="CostClasses"/>).</param>
/// <param name="Summary">One plain-text line: the row's <c>summary</c>, shown by the game's reference window.</param>
/// <param name="HostChecks">What only the host can reject (the row's <c>host_checks</c>); <c>—</c> for nothing.</param>
/// <param name="Doc">Longer documentation for the header comment, the language server and the reference.</param>
/// <param name="ResultDoc">What the result means; empty for a request.</param>
public sealed record ServiceDef(
    ushort Id,
    string Name,
    string Kind,
    IReadOnlyList<ParamDef> Params,
    string ResultCType,
    IReadOnlyList<string> ResultCells,
    string CostClass,
    string Summary,
    string HostChecks,
    string Doc,
    string ResultDoc = "")
{
    /// <summary>The kASM symbol: <c>SVC_</c> + the upper-cased name.</summary>
    public string Symbol => "SVC_" + Name.ToUpperInvariant();

    /// <summary>True when the prototype returns a value.</summary>
    public bool HasResult => ResultCType != "void";

    /// <summary>The row's <c>arg_types</c>: every parameter's cells, in order.</summary>
    public IReadOnlyList<string> ArgTypes => Params.SelectMany(p => p.CellTypes).ToArray();

    /// <summary>The row's <c>arg_domains</c>: each parameter's domain once per cell.</summary>
    public IReadOnlyList<string> ArgDomains => Params.SelectMany(p => p.CellTypes.Select(_ => p.Domain)).ToArray();

    /// <summary>For each argument cell, the index of the parameter it belongs to.</summary>
    public IReadOnlyList<int> CellOwners => Params.SelectMany((p, i) => p.CellTypes.Select(_ => i)).ToArray();

    /// <summary>The kC prototype without the trailing semicolon.</summary>
    public string Prototype =>
        $"{ResultCType} {Name}({(Params.Count == 0 ? "void" : string.Join(", ", Params.Select(p => $"{p.CType} {p.Name}")))})";
}

/// <summary>The service kinds a purrTTY service may have.</summary>
public static class ServiceKinds
{
    /// <summary>A request with no result.</summary>
    public const string Request = "request";

    /// <summary>A request whose single result cell the host supplies synchronously.</summary>
    public const string RequestResult = "request+result";
}

/// <summary>The cost classes of the game's cost table (<c>costs.json</c>, <c>service_classes</c>).</summary>
public static class CostClasses
{
    /// <summary>10 cycles (20 on K8).</summary>
    public const string Light = "Light";

    /// <summary>30 cycles (60 on K8).</summary>
    public const string Medium = "Medium";

    /// <summary>100 cycles (200 on K8).</summary>
    public const string Heavy = "Heavy";

    /// <summary>Every valid class.</summary>
    public static IReadOnlyList<string> All { get; } = [Light, Medium, Heavy];
}
