namespace caTTY.Core.Rpc.Socket;

/// <summary>
/// Interface for handling socket-based RPC requests.
/// Implementations dispatch actions to game code and return responses.
/// </summary>
public interface ISocketRpcHandler
{
    /// <summary>
    /// Handles an RPC request and returns a response.
    /// Called synchronously on the socket server thread.
    /// </summary>
    /// <param name="request">The deserialized RPC request</param>
    /// <returns>Response to send back to the client</returns>
    SocketRpcResponse HandleRequest(SocketRpcRequest request);
}
