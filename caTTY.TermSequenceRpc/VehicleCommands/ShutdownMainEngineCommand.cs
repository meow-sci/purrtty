using caTTY.Core.Rpc;
using KSA;

namespace caTTY.TermSequenceRpc.VehicleCommands;

/// <summary>
/// Fire-and-forget command to shutdown the main engine.
/// Command ID: 1002
/// </summary>
public class ShutdownMainEngineCommand : FireAndForgetCommandHandler
{
    /// <summary>
    /// Initializes a new instance of the ShutdownMainEngineCommand.
    /// </summary>
    public ShutdownMainEngineCommand() : base("Shutdown Main Engine")
    {
    }

    /// <inheritdoc />
    protected override void ExecuteAction(RpcParameters parameters)
    {
        Console.WriteLine($"Ignite Main Throttle: {parameters}");

        var rocket = Program.ControlledVehicle;

        rocket?.SetEnum(VehicleEngine.MainShutdown);
        // TODO: Integrate with KSA game engine
        // rocket.SetEnum(VehicleEngine.MainShutdown);

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
