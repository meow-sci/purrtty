using caTTY.Core.Tracing;
using caTTY.Core.Types;

namespace caTTY.Core.Parsing.Engine;

/// <summary>
///     Handles bytes in normal text processing state.
///     Processes C0 control characters, escape sequences, and normal printable bytes.
/// </summary>
public class NormalStateHandler
{
    private readonly ICursorPositionProvider? _cursorPositionProvider;
    private readonly Func<byte, bool> _handleC0ExceptEscape;
    private readonly Action<byte> _startEscapeSequence;
    private readonly Action<byte> _handleNormalByte;

    /// <summary>
    ///     Creates a new normal state handler.
    /// </summary>
    /// <param name="cursorPositionProvider">Cursor position provider for tracing</param>
    /// <param name="handleC0ExceptEscape">Delegate to handle C0 control characters (except ESC), returns true if handled</param>
    /// <param name="startEscapeSequence">Delegate to start an escape sequence</param>
    /// <param name="handleNormalByte">Delegate to handle normal printable bytes</param>
    public NormalStateHandler(
        ICursorPositionProvider? cursorPositionProvider,
        Func<byte, bool> handleC0ExceptEscape,
        Action<byte> startEscapeSequence,
        Action<byte> handleNormalByte)
    {
        _cursorPositionProvider = cursorPositionProvider;
        _handleC0ExceptEscape = handleC0ExceptEscape;
        _startEscapeSequence = startEscapeSequence;
        _handleNormalByte = handleNormalByte;
    }

    /// <summary>
    ///     Handles bytes in normal text processing state.
    /// </summary>
    public void HandleNormalState(byte b)
    {
        // C0 controls (including BEL/BS/TAB/LF/CR) execute immediately in normal mode
        if (b < 0x20 && b != 0x1b && _handleC0ExceptEscape(b))
        {
            return;
        }

        // Handle other C0 control characters that aren't explicitly handled
        if (b < 0x20 && b != 0x1b)
        {
            TraceHelper.TraceControlChar(b, TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);
            // These control characters are typically ignored or have no specific handler
            return;
        }

        if (b == 0x1b) // ESC
        {
            _startEscapeSequence(b);
            return;
        }

        // DEL (0x7F) should be ignored in terminal emulation
        if (b == 0x7F)
        {
            TraceHelper.TraceControlChar(b, TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);
            return;
        }

        _handleNormalByte(b);
    }
}
