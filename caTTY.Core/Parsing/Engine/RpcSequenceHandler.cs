using caTTY.Core.Rpc;

namespace caTTY.Core.Parsing.Engine;

/// <summary>
///     Handles RPC (Remote Procedure Call) sequence detection and processing.
///     Maintains clean separation between core terminal emulation and RPC functionality.
/// </summary>
public class RpcSequenceHandler
{
    private readonly IRpcSequenceDetector? _rpcSequenceDetector;
    private readonly IRpcSequenceParser? _rpcSequenceParser;
    private readonly IRpcHandler? _rpcHandler;

    /// <summary>
    ///     Creates a new RPC sequence handler with optional RPC components.
    /// </summary>
    /// <param name="rpcSequenceDetector">RPC sequence detector (optional)</param>
    /// <param name="rpcSequenceParser">RPC sequence parser (optional)</param>
    /// <param name="rpcHandler">RPC handler (optional)</param>
    public RpcSequenceHandler(
        IRpcSequenceDetector? rpcSequenceDetector,
        IRpcSequenceParser? rpcSequenceParser,
        IRpcHandler? rpcHandler)
    {
        _rpcSequenceDetector = rpcSequenceDetector;
        _rpcSequenceParser = rpcSequenceParser;
        _rpcHandler = rpcHandler;
    }

    /// <summary>
    ///     Checks if RPC handling is enabled and all required components are available.
    /// </summary>
    /// <returns>True if RPC handling is enabled</returns>
    public bool IsRpcHandlingEnabled()
    {
        return _rpcSequenceDetector != null &&
               _rpcSequenceParser != null &&
               _rpcHandler != null &&
               _rpcHandler.IsEnabled;
    }

    /// <summary>
    ///     Attempts to handle the current escape sequence as an RPC sequence.
    ///     This method maintains clean separation between core terminal emulation and RPC functionality.
    /// </summary>
    /// <param name="escapeSequence">The escape sequence to check and potentially handle as RPC</param>
    /// <returns>True if the sequence was handled as an RPC sequence</returns>
    public bool TryHandleRpcSequence(List<byte> escapeSequence)
    {
        if (!IsRpcHandlingEnabled())
        {
            return false;
        }

        ReadOnlySpan<byte> sequenceSpan = escapeSequence.ToArray().AsSpan();

        // First check if this is an RPC sequence
        if (!_rpcSequenceDetector!.IsRpcSequence(sequenceSpan))
        {
            return false;
        }

        // Get the sequence type for more detailed validation
        RpcSequenceType sequenceType = _rpcSequenceDetector.GetSequenceType(sequenceSpan);


        // Handle malformed sequences
        if (sequenceType != RpcSequenceType.Valid)
        {
            _rpcHandler!.HandleMalformedRpcSequence(sequenceSpan, sequenceType);
            return true; // Sequence was handled (even if malformed)
        }

        // Try to parse the valid RPC sequence
        if (_rpcSequenceParser!.TryParseRpcSequence(sequenceSpan, out RpcMessage? message) && message != null)
        {
            // Console.WriteLine($"!!! GOT VALID RPC SEQUENCE sequenceType={sequenceType} message={message}");

            _rpcHandler!.HandleRpcMessage(message);
            return true;
        }

        // Console.WriteLine($"!!! GOT INVALID RPC SEQUENCE");


        // Parsing failed for a supposedly valid sequence
        _rpcHandler!.HandleMalformedRpcSequence(sequenceSpan, RpcSequenceType.Malformed);
        return true;
    }
}
