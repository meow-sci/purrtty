using caTTY.Core.Terminal;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles response operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalResponseOps
{
    private readonly Action<ResponseEmittedEventArgs> _onResponseEmitted;

    /// <summary>
    ///     Creates a new response operations handler.
    /// </summary>
    /// <param name="onResponseEmitted">Action to invoke when a response is emitted</param>
    public TerminalResponseOps(Action<ResponseEmittedEventArgs> onResponseEmitted)
    {
        _onResponseEmitted = onResponseEmitted;
    }

    /// <summary>
    ///     Emits a terminal response (e.g., device queries, status reports).
    ///     Used for device queries and other terminal responses.
    /// </summary>
    /// <param name="responseText">The response text to emit</param>
    public void EmitResponse(string responseText)
    {
        OnResponseEmitted(responseText);
    }

    /// <summary>
    ///     Raises the ResponseEmitted event.
    /// </summary>
    /// <param name="responseData">The response data to emit</param>
    public void OnResponseEmitted(ReadOnlyMemory<byte> responseData)
    {
        _onResponseEmitted(new ResponseEmittedEventArgs(responseData));
    }

    /// <summary>
    ///     Raises the ResponseEmitted event with string data.
    /// </summary>
    /// <param name="responseText">The response text to emit</param>
    public void OnResponseEmitted(string responseText)
    {
        _onResponseEmitted(new ResponseEmittedEventArgs(responseText));
    }
}
