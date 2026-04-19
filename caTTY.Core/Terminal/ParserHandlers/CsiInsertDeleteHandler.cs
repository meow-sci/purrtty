using caTTY.Core.Types;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Terminal.ParserHandlers;

/// <summary>
///     Handles CSI insert/delete operations (lines and characters).
/// </summary>
internal class CsiInsertDeleteHandler
{
    private readonly TerminalEmulator _terminal;
    private readonly ILogger _logger;

    public CsiInsertDeleteHandler(TerminalEmulator terminal, ILogger logger)
    {
        _terminal = terminal;
        _logger = logger;
    }

    /// <summary>
    ///     Insert Lines (CSI L) - insert blank lines at cursor position.
    /// </summary>
    public void HandleInsertLines(CsiMessage message)
    {
        _terminal.InsertLinesInRegion(message.Count ?? 1);
    }

    /// <summary>
    ///     Delete Lines (CSI M) - delete lines at cursor position.
    /// </summary>
    public void HandleDeleteLines(CsiMessage message)
    {
        _terminal.DeleteLinesInRegion(message.Count ?? 1);
    }

    /// <summary>
    ///     Insert Characters (CSI @) - insert blank characters at cursor position.
    /// </summary>
    public void HandleInsertChars(CsiMessage message)
    {
        _terminal.InsertCharactersInLine(message.Count ?? 1);
    }

    /// <summary>
    ///     Delete Characters (CSI P) - delete characters at cursor position.
    /// </summary>
    public void HandleDeleteChars(CsiMessage message)
    {
        _terminal.DeleteCharactersInLine(message.Count ?? 1);
    }
}
