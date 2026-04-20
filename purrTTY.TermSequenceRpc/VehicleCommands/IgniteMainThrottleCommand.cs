using purrTTY.Core.Rpc;
using KSA;
using purrTTY.Logging;

namespace purrTTY.TermSequenceRpc.VehicleCommands;

/// <summary>
/// Fire-and-forget command to ignite the main throttle.
/// Command ID: 1001
/// </summary>
public class IgniteMainThrottleCommand : FireAndForgetCommandHandler
{
    /// <summary>
    /// Initializes a new instance of the IgniteMainThrottleCommand.
    /// </summary>
    public IgniteMainThrottleCommand() : base("Ignite Main Throttle")
    {
    }

    /// <inheritdoc />
    protected override void ExecuteAction(RpcParameters parameters)
    {
        ModLog.Log.Debug($"Ignite Main Throttle: {parameters}");
        var rocket = Program.ControlledVehicle;

        rocket?.SetEnum(VehicleEngine.MainIgnite);

        // TODO: Integrate with KSA game engine
        // rocket.SetEnum(VehicleEngine.MainIgnite);

        // For now, this is a placeholder implementation
        // The actual game integration will be implemented when KSA APIs are available
    }

    /// <inheritdoc />
    protected override bool ValidateParameters(RpcParameters parameters)
    {
        // This command takes no parameters
        return parameters != null;
    }
}
