namespace caTTY.Core.Rpc;

/// <summary>
/// Defines the types of RPC sequences that can be detected.
/// </summary>
public enum RpcSequenceType
{
    /// <summary>
    /// Not an RPC sequence.
    /// </summary>
    None,

    /// <summary>
    /// Valid RPC sequence with proper format.
    /// </summary>
    Valid,

    /// <summary>
    /// Malformed RPC sequence that should be ignored.
    /// </summary>
    Malformed,

    /// <summary>
    /// RPC sequence with invalid command ID range.
    /// </summary>
    InvalidCommandId,

    /// <summary>
    /// RPC sequence with invalid final character.
    /// </summary>
    InvalidFinalCharacter
}