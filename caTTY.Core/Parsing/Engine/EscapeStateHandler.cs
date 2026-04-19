namespace caTTY.Core.Parsing.Engine;

/// <summary>
///     Handles bytes in escape sequence state.
///     Processes escape sequence bytes with optional C0 control character handling.
/// </summary>
public class EscapeStateHandler
{
    private readonly bool _processC0ControlsDuringEscapeSequence;
    private readonly Func<byte, bool> _handleC0ExceptEscape;
    private readonly Action<byte> _handleEscapeByte;

    /// <summary>
    ///     Creates a new escape state handler.
    /// </summary>
    /// <param name="processC0ControlsDuringEscapeSequence">Whether to process C0 controls during escape sequences</param>
    /// <param name="handleC0ExceptEscape">Delegate to handle C0 control characters (except ESC), returns true if handled</param>
    /// <param name="handleEscapeByte">Delegate to handle escape sequence bytes</param>
    public EscapeStateHandler(
        bool processC0ControlsDuringEscapeSequence,
        Func<byte, bool> handleC0ExceptEscape,
        Action<byte> handleEscapeByte)
    {
        _processC0ControlsDuringEscapeSequence = processC0ControlsDuringEscapeSequence;
        _handleC0ExceptEscape = handleC0ExceptEscape;
        _handleEscapeByte = handleEscapeByte;
    }

    /// <summary>
    ///     Handles bytes in escape sequence state.
    /// </summary>
    public void HandleEscapeState(byte b)
    {
        // Optional: still execute C0 controls while parsing ESC sequences (common terminal behavior)
        if (b < 0x20 && b != 0x1b && _processC0ControlsDuringEscapeSequence && _handleC0ExceptEscape(b))
        {
            return;
        }

        _handleEscapeByte(b);
    }
}
