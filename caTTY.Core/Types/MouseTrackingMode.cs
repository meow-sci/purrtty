namespace caTTY.Core.Types;

/// <summary>
///     Mouse tracking modes for terminal mouse input.
///     Values correspond to xterm mouse tracking mode numbers.
/// </summary>
public enum MouseTrackingMode
{
    /// <summary>
    ///     Mouse tracking disabled. All mouse events handled locally.
    /// </summary>
    Off = 0,

    /// <summary>
    ///     X10 compatibility mode (mode 1000).
    ///     Reports mouse press and release events only.
    /// </summary>
    Click = 1000,

    /// <summary>
    ///     Button event tracking (mode 1002).
    ///     Reports press, release, and drag events.
    /// </summary>
    Button = 1002,

    /// <summary>
    ///     Any event tracking (mode 1003).
    ///     Reports all mouse events including motion without buttons.
    /// </summary>
    Any = 1003
}