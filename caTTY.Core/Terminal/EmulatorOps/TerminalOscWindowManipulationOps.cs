using caTTY.Core.Types;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles OSC window manipulation operations for the terminal emulator.
///     Extracted from CsiWindowManipulationHandler to reduce file size and improve maintainability.
/// </summary>
internal class TerminalOscWindowManipulationOps
{
    private readonly Func<TerminalState> _getState;
    private readonly Action<string> _setWindowTitle;
    private readonly Action<string> _setIconName;
    private readonly Action<string> _emitResponse;
    private readonly Func<int> _getHeight;
    private readonly Func<int> _getWidth;
    private readonly ILogger _logger;

    /// <summary>
    ///     Creates a new OSC window manipulation operations handler.
    /// </summary>
    /// <param name="getState">Function to get the current terminal state</param>
    /// <param name="setWindowTitle">Action to set the window title</param>
    /// <param name="setIconName">Action to set the icon name</param>
    /// <param name="emitResponse">Action to emit a response to the shell</param>
    /// <param name="getHeight">Function to get terminal height</param>
    /// <param name="getWidth">Function to get terminal width</param>
    /// <param name="logger">Logger for debugging</param>
    public TerminalOscWindowManipulationOps(
        Func<TerminalState> getState,
        Action<string> setWindowTitle,
        Action<string> setIconName,
        Action<string> emitResponse,
        Func<int> getHeight,
        Func<int> getWidth,
        ILogger logger)
    {
        _getState = getState;
        _setWindowTitle = setWindowTitle;
        _setIconName = setIconName;
        _emitResponse = emitResponse;
        _getHeight = getHeight;
        _getWidth = getWidth;
        _logger = logger;
    }

    /// <summary>
    ///     Handles window manipulation sequences (CSI Ps t).
    ///     Implements title stack operations for vi compatibility and window size queries.
    ///     Gracefully handles unsupported operations (minimize/restore) in game context.
    /// </summary>
    /// <param name="operation">The window manipulation operation code</param>
    /// <param name="parameters">Additional parameters for the operation</param>
    public void HandleWindowManipulation(int operation, int[] parameters)
    {
        switch (operation)
        {
            case 22:
                // Push title/icon name to stack
                if (parameters.Length >= 1)
                {
                    int subOperation = parameters[0];
                    if (subOperation == 1)
                    {
                        // CSI 22;1t - Push icon name to stack
                        _getState().IconNameStack.Add(_getState().WindowProperties.IconName);
                        _logger.LogDebug("Pushed icon name to stack: \"{IconName}\"", _getState().WindowProperties.IconName);
                    }
                    else if (subOperation == 2)
                    {
                        // CSI 22;2t - Push window title to stack
                        _getState().TitleStack.Add(_getState().WindowProperties.Title);
                        _logger.LogDebug("Pushed window title to stack: \"{Title}\"", _getState().WindowProperties.Title);
                    }
                }
                break;

            case 23:
                // Pop title/icon name from stack
                if (parameters.Length >= 1)
                {
                    int subOperation = parameters[0];
                    if (subOperation == 1)
                    {
                        // CSI 23;1t - Pop icon name from stack
                        if (_getState().IconNameStack.Count > 0)
                        {
                            string poppedIconName = _getState().IconNameStack[^1];
                            _getState().IconNameStack.RemoveAt(_getState().IconNameStack.Count - 1);
                            _setIconName(poppedIconName);
                            _logger.LogDebug("Popped icon name from stack: \"{IconName}\"", poppedIconName);
                        }
                        else
                        {
                            _logger.LogDebug("Attempted to pop icon name from empty stack");
                        }
                    }
                    else if (subOperation == 2)
                    {
                        // CSI 23;2t - Pop window title from stack
                        if (_getState().TitleStack.Count > 0)
                        {
                            string poppedTitle = _getState().TitleStack[^1];
                            _getState().TitleStack.RemoveAt(_getState().TitleStack.Count - 1);
                            _setWindowTitle(poppedTitle);
                            _logger.LogDebug("Popped window title from stack: \"{Title}\"", poppedTitle);
                        }
                        else
                        {
                            _logger.LogDebug("Attempted to pop window title from empty stack");
                        }
                    }
                }
                break;

            case 18:
                // Terminal size query - respond with current dimensions
                string sizeResponse = DeviceResponses.GenerateTerminalSizeResponse(_getHeight(), _getWidth());
                _emitResponse(sizeResponse);
                _logger.LogDebug("Terminal size query response: {Response}", sizeResponse);
                break;

            default:
                // Other window manipulation commands - gracefully ignore
                // This includes minimize (2), restore (1), resize (8), etc.
                // These are not applicable in a game context and should be ignored
                _logger.LogDebug("Window manipulation operation {Operation} with params [{Parameters}] - gracefully ignored",
                    operation, string.Join(", ", parameters));
                break;
        }
    }
}
