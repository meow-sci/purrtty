namespace caTTY.Core.Rpc;

/// <summary>
/// Interface for handling parsed RPC messages from private use area sequences.
/// Provides clean separation between core terminal emulation and RPC functionality.
/// </summary>
public interface IRpcHandler
{
    /// <summary>
    /// Handles a parsed RPC message from a private use area sequence.
    /// This method is called when the parser detects and successfully parses an RPC sequence.
    /// </summary>
    /// <param name="message">The parsed RPC message containing command details</param>
    void HandleRpcMessage(RpcMessage message);

    /// <summary>
    /// Handles a malformed RPC sequence that could not be parsed.
    /// This allows for logging and debugging of invalid sequences.
    /// </summary>
    /// <param name="rawSequence">The raw sequence bytes that could not be parsed</param>
    /// <param name="sequenceType">The type of parsing failure that occurred</param>
    void HandleMalformedRpcSequence(ReadOnlySpan<byte> rawSequence, RpcSequenceType sequenceType);

    /// <summary>
    /// Indicates whether RPC handling is currently enabled.
    /// When false, RPC sequences should be ignored and processed as standard terminal sequences.
    /// </summary>
    bool IsEnabled { get; set; }
}