namespace purrTTY.Core.Terminal;

/// <summary>
///     A request to open a terminal, passed to <see cref="ITerminalLauncher.LaunchAsync"/>.
///     Pick a <see cref="Placement"/>, describe what runs in <see cref="Program"/>, and fill the
///     options object that matches the placement.
/// </summary>
public sealed class TerminalLaunchRequest
{
    /// <summary>Where the terminal appears. Default <see cref="TerminalPlacementKind.Window"/>.</summary>
    public TerminalPlacementKind Placement { get; set; } = TerminalPlacementKind.Window;

    /// <summary>What runs inside the terminal.</summary>
    public TerminalProgramSpec Program { get; set; } = new();

    /// <summary>
    ///     Options for <see cref="TerminalPlacementKind.Window"/> and
    ///     <see cref="TerminalPlacementKind.Tab"/> placements; null uses defaults.
    /// </summary>
    public TerminalWindowOptions? Window { get; set; }

    /// <summary>
    ///     Options for <see cref="TerminalPlacementKind.InWorldPart"/> and
    ///     <see cref="TerminalPlacementKind.InWorldBillboard"/> placements; null uses the
    ///     <see cref="TerminalInWorldOptions"/> defaults.
    /// </summary>
    public TerminalInWorldOptions? InWorld { get; set; }
}
