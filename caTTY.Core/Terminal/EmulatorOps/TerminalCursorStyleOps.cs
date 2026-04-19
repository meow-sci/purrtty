using caTTY.Core.Managers;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles cursor style operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalCursorStyleOps
{
    private readonly ICursorManager _cursorManager;
    private readonly Func<TerminalState> _getState;
    private readonly ILogger _logger;

    /// <summary>
    ///     Creates a new cursor style operations handler.
    /// </summary>
    /// <param name="cursorManager">The cursor manager for cursor operations</param>
    /// <param name="getState">Function to get the current terminal state</param>
    /// <param name="logger">Logger for debugging</param>
    public TerminalCursorStyleOps(ICursorManager cursorManager, Func<TerminalState> getState, ILogger logger)
    {
        _cursorManager = cursorManager;
        _getState = getState;
        _logger = logger;
    }

    /// <summary>
    ///     Sets the cursor style (DECSCUSR).
    /// </summary>
    /// <param name="style">Cursor style parameter from DECSCUSR sequence (0-6)</param>
    public void SetCursorStyle(int style)
    {
        // Validate and normalize cursor style using the new enum system
        CursorStyle validatedStyle = CursorStyleExtensions.ValidateStyle(style);

        // Update cursor manager
        _cursorManager.Style = validatedStyle;

        // Update terminal state
        _getState().CursorStyle = validatedStyle;
    }

    /// <summary>
    ///     Sets the cursor style using the CursorStyle enum.
    /// </summary>
    /// <param name="style">The cursor style to set</param>
    public void SetCursorStyle(CursorStyle style)
    {
        // Update cursor manager
        _cursorManager.Style = style;

        // Update terminal state
        _getState().CursorStyle = style;
    }
}
