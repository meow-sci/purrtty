namespace caTTY.Core.Rpc;

/// <summary>
/// Represents a parsed RPC message from a terminal sequence.
/// </summary>
public record RpcMessage
{
    /// <summary>
    /// The command ID (Pn parameter) that identifies the specific RPC command.
    /// Valid ranges: 1000-1999 (fire-and-forget), 2000-2999 (queries), 9000-9999 (system/error).
    /// </summary>
    public int CommandId { get; init; }

    /// <summary>
    /// The RPC protocol version (Pv parameter), currently 1.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// The command type based on the final character (Pc parameter).
    /// </summary>
    public RpcCommandType CommandType { get; init; }

    /// <summary>
    /// Parsed parameters from the sequence.
    /// </summary>
    public RpcParameters Parameters { get; init; } = new();

    /// <summary>
    /// The raw sequence string for logging and debugging.
    /// </summary>
    public string Raw { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether this is a fire-and-forget command.
    /// </summary>
    public bool IsFireAndForget => CommandType == RpcCommandType.FireAndForget;

    /// <summary>
    /// Indicates whether this is a query command.
    /// </summary>
    public bool IsQuery => CommandType == RpcCommandType.Query;

    /// <summary>
    /// Indicates whether this is a response.
    /// </summary>
    public bool IsResponse => CommandType == RpcCommandType.Response;

    /// <summary>
    /// Indicates whether this is an error response.
    /// </summary>
    public bool IsError => CommandType == RpcCommandType.Error;

    /// <summary>
    /// Validates that the command ID is in the correct range for the command type.
    /// </summary>
    /// <returns>True if the command ID is valid for the command type</returns>
    public bool IsValidCommandIdRange()
    {
        return CommandType switch
        {
            RpcCommandType.FireAndForget => CommandId >= 1000 && CommandId <= 1999,
            RpcCommandType.Query => CommandId >= 2000 && CommandId <= 2999,
            RpcCommandType.Response => CommandId >= 1000 && CommandId <= 2999,
            RpcCommandType.Error => CommandId >= 9000 && CommandId <= 9999,
            _ => false
        };
    }
}