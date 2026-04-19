using caTTY.Core.Rpc;

namespace caTTY.TermSequenceRpc.VehicleCommands;

/// <summary>
/// Query command to get the current throttle status.
/// Command ID: 2001
/// </summary>
public class GetThrottleStatusQuery : QueryCommandHandler
{
    /// <summary>
    /// Initializes a new instance of the GetThrottleStatusQuery.
    /// </summary>
    public GetThrottleStatusQuery() : base("Get Throttle Status", TimeSpan.FromSeconds(3))
    {
    }

    /// <inheritdoc />
    protected override object? ExecuteQuery(RpcParameters parameters)
    {
        // TODO: Integrate with KSA game engine to get actual throttle status
        // var throttleEnabled = rocket.GetBool(VehicleEngine.MainThrottleEnabled);
        // var throttleLevel = rocket.GetFloat(VehicleEngine.MainThrottleLevel);

        // For now, return mock data
        var throttleEnabled = true; // Mock: engine is enabled
        var throttleLevel = 75; // Mock: 75% throttle

        return CreateResponse("enabled", throttleLevel, new
        {
            enabled = throttleEnabled,
            level = throttleLevel,
            unit = "percent"
        });
    }

    /// <inheritdoc />
    protected override bool ValidateParameters(RpcParameters parameters)
    {
        // This query takes no parameters
        return parameters != null;
    }
}
