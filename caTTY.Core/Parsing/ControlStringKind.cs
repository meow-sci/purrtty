namespace caTTY.Core.Parsing;

/// <summary>
///     Represents the type of control string being parsed.
/// </summary>
public enum ControlStringKind
{
    /// <summary>
    ///     Start of String (SOS) - ESC X
    /// </summary>
    Sos,

    /// <summary>
    ///     Privacy Message (PM) - ESC ^
    /// </summary>
    Pm,

    /// <summary>
    ///     Application Program Command (APC) - ESC _
    /// </summary>
    Apc
}
