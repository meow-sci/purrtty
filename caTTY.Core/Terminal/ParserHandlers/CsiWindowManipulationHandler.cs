using Microsoft.Extensions.Logging;

namespace caTTY.Core.Terminal.ParserHandlers;

/// <summary>
///     Handles CSI window manipulation sequences (CSI Ps t).
///     Implements title stack operations for vi compatibility and window size queries.
/// </summary>
internal class CsiWindowManipulationHandler
{
    private readonly TerminalEmulator _terminal;

    public CsiWindowManipulationHandler(TerminalEmulator terminal, ILogger logger)
    {
        _terminal = terminal;
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
        // Delegate to TerminalEmulator's window manipulation operations
        _terminal.HandleWindowManipulation(operation, parameters);
    }
}
