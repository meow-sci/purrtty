using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Rpc;

/// <summary>
///     Handles OSC-based RPC commands for KSA game integration.
///     Uses OSC sequences in the private-use range (1000+) because they pass through
///     Windows ConPTY, unlike DCS sequences which are filtered.
///     
///     Supported commands:
///     - OSC 1010: JSON action dispatch (engine_ignite, engine_shutdown, etc.)
///     
///     Format: ESC ] {command} ; {json_payload} BEL/ST
///     Example: ESC ] 1010 ; {"action":"engine_ignite"} BEL
/// </summary>
public abstract class OscRpcHandler : IOscRpcHandler
{
    /// <summary>
    ///     Minimum command number for private/application-specific OSC commands.
    ///     Standard xterm OSC codes are 0-119, we use 1000+ for custom commands.
    /// </summary>
    private const int PrivateCommandRangeStart = 1000;

    /// <summary>
    ///     OSC command for JSON action dispatch.
    /// </summary>
    public const int JsonActionCommand = 1010;

    protected ILogger Logger { get; }

    public OscRpcHandler(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsPrivateCommand(int command)
    {
        return command >= PrivateCommandRangeStart;
    }

    /// <inheritdoc />
    public void HandleCommand(int command, string? payload)
    {
        switch (command)
        {
            case JsonActionCommand:
                HandleJsonAction(payload);
                break;

            default:
                Logger.LogWarning("OSC RPC: Unknown private command {Command}", command);
                break;
        }
    }

    /// <summary>
    ///     Handles JSON action commands from OSC 1010 sequences.
    ///     Format: {"action":"action_name", ...optional_params}
    /// </summary>
    private void HandleJsonAction(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            Logger.LogWarning("OSC 1010: JSON action command received with empty payload");
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (!root.TryGetProperty("action", out var actionElement))
            {
                Logger.LogWarning("OSC 1010: JSON payload missing 'action' property: {Payload}", payload);
                return;
            }

            string? action = actionElement.GetString();
            if (string.IsNullOrEmpty(action))
            {
                Logger.LogWarning("OSC 1010: JSON 'action' property is empty");
                return;
            }

            Logger.LogInformation("OSC 1010: Executing action '{Action}'", action);
            DispatchAction(action, root);
        }
        catch (JsonException ex)
        {
            Logger.LogWarning("OSC 1010: Failed to parse JSON payload: {Error}. Payload: {Payload}", ex.Message, payload);
        }
    }

    /// <summary>
    ///     Dispatches a parsed action to the game-specific handler.
    ///     Implement this method in subclasses to handle actions specific to your game/application.
    /// </summary>
    /// <param name="action">The action name parsed from the JSON payload</param>
    /// <param name="root">The full JSON document root element for accessing additional parameters</param>
    protected abstract void DispatchAction(string action, JsonElement root);
}
