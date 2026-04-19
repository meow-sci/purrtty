using caTTY.Core.Types;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Terminal.ParserHandlers;

/// <summary>
///     Handles CSI device query and status report sequences.
/// </summary>
internal class CsiDeviceQueryHandler
{
    private readonly TerminalEmulator _terminal;
    private readonly ILogger _logger;

    public CsiDeviceQueryHandler(TerminalEmulator terminal, ILogger logger)
    {
        _terminal = terminal;
        _logger = logger;
    }

    /// <summary>
    ///     Handle Primary Device Attributes query (CSI c).
    /// </summary>
    public void HandleDeviceAttributesPrimary()
    {
        // Primary DA query: respond with device attributes
        string primaryResponse = DeviceResponses.GenerateDeviceAttributesPrimaryResponse();
        _terminal.EmitResponse(primaryResponse);
    }

    /// <summary>
    ///     Handle Secondary Device Attributes query (CSI > c).
    /// </summary>
    public void HandleDeviceAttributesSecondary()
    {
        // Secondary DA query: respond with terminal version
        string secondaryResponse = DeviceResponses.GenerateDeviceAttributesSecondaryResponse();
        _terminal.EmitResponse(secondaryResponse);
    }

    /// <summary>
    ///     Handle Cursor Position Report query (CSI 6 n).
    /// </summary>
    public void HandleCursorPositionReport()
    {
        // CPR query: respond with current cursor position
        string cprResponse =
            DeviceResponses.GenerateCursorPositionReport(_terminal.Cursor.Col, _terminal.Cursor.Row);
        _terminal.EmitResponse(cprResponse);
    }

    /// <summary>
    ///     Handle Device Status Report query (CSI 5 n).
    /// </summary>
    public void HandleDeviceStatusReport()
    {
        // DSR ready query: respond with CSI 0 n
        string dsrResponse = DeviceResponses.GenerateDeviceStatusReportResponse();
        _terminal.EmitResponse(dsrResponse);
    }

    /// <summary>
    ///     Handle Terminal Size Query (CSI 18 t).
    /// </summary>
    public void HandleTerminalSizeQuery()
    {
        // Terminal size query: respond with dimensions
        string sizeResponse = DeviceResponses.GenerateTerminalSizeResponse(_terminal.Height, _terminal.Width);
        _terminal.EmitResponse(sizeResponse);
    }

    /// <summary>
    ///     Handle Character Set Query (CSI ? 26 n).
    /// </summary>
    public void HandleCharacterSetQuery()
    {
        // Character set query: respond with current character set
        string charsetResponse = _terminal.GenerateCharacterSetQueryResponse();
        _terminal.EmitResponse(charsetResponse);
    }
}
