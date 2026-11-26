namespace purrTTY.Display.Ghostty;

/// <summary>
/// The close-on-exit decision shared by the 2D <see cref="TerminalWindow"/> and the
/// in-world <see cref="InWorldTerminalRenderer"/>, kept free of ImGui so it is unit-tested
/// headlessly. The backend (<c>SessionManager.CloseExitedSessions</c>) closes the exited
/// <c>CloseOnExit</c> sessions on the tick thread; the host then asks this whether it
/// must retire itself.
/// </summary>
public static class SessionExitPolicy
{
    /// <summary>
    /// True when the host must retire (window closes, in-world instance is removed):
    /// this tick closed at least one exited session and none remain. A host that closed
    /// nothing never retires here — an empty window whose session is still starting, or
    /// failed to start (it shows its error text), is left alone, and the last tab closed
    /// by the user keeps going through the window's own path.
    /// </summary>
    /// <param name="closedThisTick">Sessions just closed by <c>CloseExitedSessions</c>.</param>
    /// <param name="remainingSessions">Sessions the host still has afterwards.</param>
    public static bool ShouldRetireHost(int closedThisTick, int remainingSessions)
        => closedThisTick > 0 && remainingSessions == 0;
}
