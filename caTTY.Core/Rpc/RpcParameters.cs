namespace caTTY.Core.Rpc;

/// <summary>
/// Represents parameters extracted from an RPC sequence.
/// </summary>
public record RpcParameters
{
    /// <summary>
    /// Numeric parameters from the sequence.
    /// </summary>
    public int[] NumericParameters { get; init; } = Array.Empty<int>();

    /// <summary>
    /// String parameters from the sequence (for future extensibility).
    /// </summary>
    public string[] StringParameters { get; init; } = Array.Empty<string>();

    /// <summary>
    /// JSON payload string for commands that accept arbitrary JSON data (e.g., command 1010).
    /// This is parsed separately from the standard parameter structure.
    /// </summary>
    public string? JsonPayload { get; init; }

    /// <summary>
    /// Extended parameters for complex commands (for future extensibility).
    /// </summary>
    public Dictionary<string, object> ExtendedParameters { get; init; } = new();

    /// <summary>
    /// Gets a numeric parameter by index with a fallback value.
    /// </summary>
    /// <param name="index">The parameter index</param>
    /// <param name="fallback">The fallback value if parameter is missing</param>
    /// <returns>The parameter value or fallback</returns>
    public int GetNumericParameter(int index, int fallback = 0)
    {
        return index >= 0 && index < NumericParameters.Length ? NumericParameters[index] : fallback;
    }

    /// <summary>
    /// Gets a string parameter by index with a fallback value.
    /// </summary>
    /// <param name="index">The parameter index</param>
    /// <param name="fallback">The fallback value if parameter is missing</param>
    /// <returns>The parameter value or fallback</returns>
    public string GetStringParameter(int index, string fallback = "")
    {
        return index >= 0 && index < StringParameters.Length ? StringParameters[index] : fallback;
    }
}