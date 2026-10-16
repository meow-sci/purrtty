namespace purrTTY.Core.Terminal;

/// <summary>
///     Lifecycle transitions reported through <see cref="ITerminalHandle.Lifecycle"/> and
///     <see cref="ITerminalLauncher.Lifecycle"/>. Values are append-only.
/// </summary>
public enum TerminalLifecycleEvent
{
    /// <summary>The terminal exists (raised once, before the first <see cref="Started"/>).</summary>
    Created,

    /// <summary>The program inside the terminal started running (raised at most once).</summary>
    Started,

    /// <summary>The terminal gained input focus.</summary>
    Focused,

    /// <summary>The terminal lost input focus.</summary>
    Unfocused,

    /// <summary>The terminal grid changed size (not raised for the first measurement).</summary>
    Resized,

    /// <summary>The program inside the terminal exited (raised at most once; see <see cref="ITerminalHandle.ExitCode"/>).</summary>
    Exited,

    /// <summary>The terminal was closed, by the user or through <see cref="ITerminalHandle.CloseAsync"/> (raised once, last).</summary>
    Closed,
}
