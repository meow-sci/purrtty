using caTTY.Core.Rpc.Socket;
using caTTY.TermSequenceRpc.SocketRpc.Actions;
using Microsoft.Extensions.Logging;

namespace caTTY.TermSequenceRpc.SocketRpc;

/// <summary>
/// KSA-specific socket RPC handler.
/// Routes incoming requests to registered action handlers.
/// </summary>
public class KsaSocketRpcHandler : ISocketRpcHandler
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, ISocketRpcAction> _actions = new(StringComparer.OrdinalIgnoreCase);

    public KsaSocketRpcHandler(ILogger logger)
    {
        _logger = logger;
        RegisterDefaultActions();
    }

    /// <summary>
    /// Registers a custom action handler.
    /// </summary>
    public void RegisterAction(ISocketRpcAction action)
    {
        _actions[action.ActionName] = action;
        _logger.LogDebug("Registered socket RPC action: {ActionName}", action.ActionName);
    }

    /// <inheritdoc />
    public SocketRpcResponse HandleRequest(SocketRpcRequest request)
    {
        if (string.IsNullOrEmpty(request.Action))
        {
            return SocketRpcResponse.Fail("Missing action");
        }

        if (!_actions.TryGetValue(request.Action, out var action))
        {
            _logger.LogWarning("Unknown socket RPC action: {Action}", request.Action);
            return SocketRpcResponse.Fail($"Unknown action: {request.Action}");
        }

        _logger.LogDebug("Executing socket RPC action: {Action}", request.Action);
        return action.Execute(request.Params);
    }

    private void RegisterDefaultActions()
    {
        RegisterAction(new ListCraftsAction(_logger));
        RegisterAction(new GetCurrentCraftAction(_logger));
        RegisterAction(new IgniteAction(_logger));
        RegisterAction(new ShutdownAction(_logger));
    }
}
