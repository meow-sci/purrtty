using caTTY.Core.Types;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Terminal.ParserHandlers;

/// <summary>
///     Handles cursor-related CSI sequences.
/// </summary>
internal class CsiCursorHandler
{
    private readonly TerminalEmulator _terminal;
    private readonly ILogger _logger;

    public CsiCursorHandler(TerminalEmulator terminal, ILogger logger)
    {
        _terminal = terminal;
        _logger = logger;
    }

    public void HandleCursorUp(CsiMessage message)
    {
        _terminal.MoveCursorUp(message.Count ?? 1);
    }

    public void HandleCursorDown(CsiMessage message)
    {
        _terminal.MoveCursorDown(message.Count ?? 1);
    }

    public void HandleCursorForward(CsiMessage message)
    {
        _terminal.MoveCursorForward(message.Count ?? 1);
    }

    public void HandleCursorBackward(CsiMessage message)
    {
        _terminal.MoveCursorBackward(message.Count ?? 1);
    }

    public void HandleCursorPosition(CsiMessage message)
    {
        _terminal.SetCursorPosition(message.Row ?? 1, message.Column ?? 1);
    }

    public void HandleCursorHorizontalAbsolute(CsiMessage message)
    {
        _terminal.SetCursorColumn(message.Count ?? 1);
    }

    public void HandleCursorNextLine(CsiMessage message)
    {
        _terminal.MoveCursorDown(message.Count ?? 1);
        _terminal.SetCursorColumn(1); // Move to beginning of line
    }

    public void HandleCursorPrevLine(CsiMessage message)
    {
        _terminal.MoveCursorUp(message.Count ?? 1);
        _terminal.SetCursorColumn(1); // Move to beginning of line
    }

    public void HandleVerticalPositionAbsolute(CsiMessage message)
    {
        _terminal.SetCursorPosition(message.Count ?? 1, _terminal.Cursor.Col + 1);
    }

    public void HandleSaveCursorPosition()
    {
        // ANSI cursor save (CSI s) - separate from DEC save (ESC 7)
        _terminal.SaveCursorPositionAnsi();
    }

    public void HandleRestoreCursorPosition()
    {
        // ANSI cursor restore (CSI u) - separate from DEC restore (ESC 8)
        _terminal.RestoreCursorPositionAnsi();
    }

    public void HandleCursorForwardTab(CsiMessage message)
    {
        _terminal.CursorForwardTab(message.Count ?? 1);
    }

    public void HandleCursorBackwardTab(CsiMessage message)
    {
        _terminal.CursorBackwardTab(message.Count ?? 1);
    }

    public void HandleSetCursorStyle(CsiMessage message)
    {
        // Set cursor style (CSI Ps SP q) - DECSCUSR
        if (message.CursorStyle.HasValue)
        {
            _terminal.SetCursorStyle(message.CursorStyle.Value);
        }
    }
}
