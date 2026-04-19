namespace caTTY.Core.Rpc;

/// <summary>
/// Interface for parsing RPC sequences into structured messages.
/// Handles the ESC [ > Pn ; Pv ; Pc format parsing.
/// </summary>
public interface IRpcSequenceParser
{
    /// <summary>
    /// Attempts to parse a complete RPC sequence into an RpcMessage.
    /// </summary>
    /// <param name="sequence">The complete RPC sequence bytes (including ESC [ >)</param>
    /// <param name="message">The parsed RPC message if successful</param>
    /// <returns>True if parsing was successful</returns>
    bool TryParseRpcSequence(ReadOnlySpan<byte> sequence, out RpcMessage? message);

    /// <summary>
    /// Attempts to parse RPC parameters from a parameter string.
    /// Extracts Pn (command ID), Pv (version), and additional parameters.
    /// </summary>
    /// <param name="parameterString">The parameter portion of the RPC sequence</param>
    /// <param name="parameters">The parsed RPC parameters if successful</param>
    /// <returns>True if parsing was successful</returns>
    bool TryParseParameters(ReadOnlySpan<char> parameterString, out RpcParameters? parameters);

    /// <summary>
    /// Extracts the command ID (Pn) from the parameter string.
    /// </summary>
    /// <param name="parameterString">The parameter string</param>
    /// <param name="commandId">The extracted command ID</param>
    /// <returns>True if command ID was successfully extracted</returns>
    bool TryExtractCommandId(ReadOnlySpan<char> parameterString, out int commandId);

    /// <summary>
    /// Extracts the version (Pv) from the parameter string.
    /// </summary>
    /// <param name="parameterString">The parameter string</param>
    /// <param name="version">The extracted version</param>
    /// <returns>True if version was successfully extracted</returns>
    bool TryExtractVersion(ReadOnlySpan<char> parameterString, out int version);

    /// <summary>
    /// Determines the command type from the final character.
    /// </summary>
    /// <param name="finalChar">The final character of the sequence</param>
    /// <param name="commandType">The determined command type</param>
    /// <returns>True if the final character maps to a valid command type</returns>
    bool TryGetCommandType(byte finalChar, out RpcCommandType commandType);
}