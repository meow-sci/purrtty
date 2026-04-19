namespace caTTY.Core.Parsing.Sgr;

/// <summary>
///     Parse context for SGR sequences.
///     Maintains state during parameter parsing including position, parameters, and separators.
/// </summary>
internal class SgrParseContext
{
    public int[] Params { get; set; } = Array.Empty<int>();
    public string[] Separators { get; set; } = Array.Empty<string>();
    public int Index { get; set; }
}
