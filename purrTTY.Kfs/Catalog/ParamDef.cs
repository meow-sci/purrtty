namespace purrTTY.Kfs.Catalog;

/// <summary>
/// One parameter of a purrTTY kFS service, as the kC prototype declares it and as the services
/// table describes it.
/// </summary>
/// <param name="Name">The kC parameter name; also the argument name in the row's <c>kc</c> column.</param>
/// <param name="CType">The kC type in the prototype (<c>uint32_t</c>, <c>float</c>, <c>vec3</c>, …).</param>
/// <param name="CellTypes">
/// The cells this parameter occupies in the row's <c>arg_types</c>, in push order. Declared
/// explicitly so <see cref="CatalogSelfCheck"/> can verify it against what <see cref="CType"/>
/// flattens to: a <c>float</c> prototype against a <c>u32</c> cell compiles silently and sends
/// float bits.
/// </param>
/// <param name="Domain">
/// The argument domain the VM checks at issue time, repeated once per cell in <c>arg_domains</c>
/// (<c>any</c>, <c>finite</c>, <c>finite:&gt;=0</c>, <c>enum:&lt;key&gt;</c>,
/// <c>enum:&lt;key&gt;[a..b]</c>).
/// </param>
/// <param name="Doc">One line of documentation for the header and the reference.</param>
/// <param name="Selects">
/// For an <c>enum:</c> domain, the values the parameter is meant to admit; the self-check
/// requires the domain to admit exactly these. Null for every other domain.
/// </param>
public sealed record ParamDef(
    string Name,
    string CType,
    IReadOnlyList<string> CellTypes,
    string Domain,
    string Doc,
    IReadOnlyList<int>? Selects = null);
