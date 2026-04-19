namespace caTTY.Core.Rpc;

/// <summary>
/// Defines the types of RPC commands based on their final character.
/// </summary>
public enum RpcCommandType
{
    /// <summary>
    /// Fire-and-forget command (final character 'F').
    /// Commands that execute without expecting a response.
    /// </summary>
    FireAndForget = 'F',

    /// <summary>
    /// Query command (final character 'Q').
    /// Commands that request information and expect a response.
    /// </summary>
    Query = 'Q',

    /// <summary>
    /// Response (final character 'R').
    /// Responses to query commands.
    /// </summary>
    Response = 'R',

    /// <summary>
    /// Error response (final character 'E').
    /// Error responses for failed commands or timeouts.
    /// </summary>
    Error = 'E'
}