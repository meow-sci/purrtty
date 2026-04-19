using caTTY.Core.Rpc;
using caTTY.TermSequenceRpc.VehicleCommands;
using Microsoft.Extensions.Logging;

namespace caTTY.TermSequenceRpc;

/// <summary>
/// KSA-specific game action registry for managing vehicle control command registration.
/// Provides default vehicle control commands and supports custom command registration.
/// </summary>
public class KsaGameActionRegistry : IGameActionRegistry
{
    private readonly IRpcCommandRouter _commandRouter;
    private readonly ILogger _logger;
    private readonly IRpcParameterValidator? _parameterValidator;

    /// <summary>
    /// Initializes a new instance of the KsaGameActionRegistry.
    /// </summary>
    /// <param name="commandRouter">The command router for registering handlers</param>
    /// <param name="logger">Logger for debugging and error reporting</param>
    /// <param name="parameterValidator">Optional parameter validator for security validation</param>
    public KsaGameActionRegistry(IRpcCommandRouter commandRouter, ILogger logger, IRpcParameterValidator? parameterValidator = null)
    {
        _commandRouter = commandRouter ?? throw new ArgumentNullException(nameof(commandRouter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _parameterValidator = parameterValidator;
    }

    /// <inheritdoc />
    public void RegisterVehicleCommands()
    {
        _logger.LogDebug("Registering default KSA vehicle control commands");

        // Engine Control Commands (1001-1009)
        // Note: Command 1010 (JSON actions) is handled via OSC sequences, not CSI RPC
        RegisterCustomCommand(1001, new IgniteMainThrottleCommand());
        RegisterCustomCommand(1002, new ShutdownMainEngineCommand());

        // Navigation Commands (1011-1020) - Reserved for future implementation
        // RegisterCustomCommand(1011, new SetHeadingCommand());
        // RegisterCustomCommand(1012, new SetThrottleCommand());

        // System Commands (1021-1030) - Reserved for future implementation
        // RegisterCustomCommand(1021, new ToggleLightsCommand());
        // RegisterCustomCommand(1022, new ActivateRcsCommand());

        // Engine Query Commands (2001-2010)
        RegisterCustomCommand(2001, new GetThrottleStatusQuery());

        // Navigation Query Commands (2011-2020) - Reserved for future implementation
        // RegisterCustomCommand(2011, new GetPositionQuery());
        // RegisterCustomCommand(2012, new GetVelocityQuery());

        // System Query Commands (2021-2030) - Reserved for future implementation
        // RegisterCustomCommand(2021, new GetFuelLevelQuery());
        // RegisterCustomCommand(2022, new GetBatteryLevelQuery());

        // Register parameter validation rules if validator is available
        RegisterValidationRules();

        _logger.LogInformation("Registered {Count} default KSA vehicle control commands", GetRegisteredCommands().Count());
    }

    /// <inheritdoc />
    public void RegisterSystemCommands()
    {
        _logger.LogDebug("Registering default system commands");

        // System commands would be registered here
        // Currently no default system commands are defined

        _logger.LogInformation("Registered system commands");
    }

    /// <inheritdoc />
    public bool RegisterCustomCommand(int commandId, IRpcCommandHandler handler)
    {
        if (handler == null)
        {
            _logger.LogError($"Cannot register null handler for command {commandId}");
            return false;
        }

        // Validate command ID range
        if (!ValidateCommandId(commandId, handler.IsFireAndForget))
        {
            _logger.LogError($"Invalid command ID {commandId} for handler type (fire-and-forget: {handler.IsFireAndForget})");
            return false;
        }

        // Check if command is already registered
        if (IsCommandRegistered(commandId))
        {
            _logger.LogWarning($"Command {commandId} is already registered, skipping registration");
            return false;
        }

        // Register with the command router
        var success = _commandRouter.RegisterCommand(commandId, handler);
        if (success)
        {
            _logger.LogDebug($"Successfully registered custom command {commandId}: {handler.Description}");
        }
        else
        {
            _logger.LogError($"Failed to register custom command {commandId} with command router");
        }

        return success;
    }

    /// <inheritdoc />
    public bool UnregisterCommand(int commandId)
    {
        var success = _commandRouter.UnregisterCommand(commandId);
        if (success)
        {
            _logger.LogDebug($"Successfully unregistered command {commandId}");
        }
        else
        {
            _logger.LogWarning($"Failed to unregister command {commandId} (may not have been registered)");
        }

        return success;
    }

    /// <inheritdoc />
    public bool ValidateCommandId(int commandId, bool isFireAndForget)
    {
        if (isFireAndForget)
        {
            // Fire-and-forget commands: 1000-1999
            return commandId >= 1000 && commandId <= 1999;
        }
        else
        {
            // Query commands: 2000-2999
            return commandId >= 2000 && commandId <= 2999;
        }
    }

    /// <inheritdoc />
    public IEnumerable<int> GetRegisteredCommands()
    {
        return _commandRouter.GetRegisteredCommands();
    }

    /// <inheritdoc />
    public bool IsCommandRegistered(int commandId)
    {
        return _commandRouter.IsCommandRegistered(commandId);
    }

    /// <inheritdoc />
    public void ClearAllCommands()
    {
        _commandRouter.ClearAllCommands();
        _logger.LogDebug("Cleared all registered commands");
    }

    /// <inheritdoc />
    public int CommandCount => GetRegisteredCommands().Count();

    /// <summary>
    /// Registers parameter validation rules for default vehicle commands.
    /// </summary>
    private void RegisterValidationRules()
    {
        if (_parameterValidator == null)
        {
            _logger.LogDebug("No parameter validator available, skipping validation rule registration");
            return;
        }

        _logger.LogDebug("Registering parameter validation rules for KSA vehicle commands");

        // IgniteMainThrottle (1001) - No parameters expected, security-sensitive
        _parameterValidator.RegisterValidationRules(1001, new RpcParameterValidationRules
        {
            ExpectedNumericParameterCount = 0,
            ExpectedStringParameterCount = 0,
            Description = "Ignite Main Throttle - No parameters required"
        }.AsSecuritySensitive("Engine ignition is a critical safety operation"));

        // ShutdownMainEngine (1002) - No parameters expected, security-sensitive
        _parameterValidator.RegisterValidationRules(1002, new RpcParameterValidationRules
        {
            ExpectedNumericParameterCount = 0,
            ExpectedStringParameterCount = 0,
            Description = "Shutdown Main Engine - No parameters required"
        }.AsSecuritySensitive("Engine shutdown is a critical safety operation"));

        // Note: Command 1010 (JSON actions) is handled via OSC sequences, not CSI RPC

        // GetThrottleStatus (2001) - No parameters expected
        _parameterValidator.RegisterValidationRules(2001, new RpcParameterValidationRules
        {
            ExpectedNumericParameterCount = 0,
            ExpectedStringParameterCount = 0,
            Description = "Get Throttle Status - No parameters required"
        });

        // Example validation rules for future commands:

        // SetThrottle (1012) - Would require throttle percentage (0-100)
        // _parameterValidator.RegisterValidationRules(1012, new RpcParameterValidationRules
        // {
        //     ExpectedNumericParameterCount = 1,
        //     Description = "Set Throttle - Requires throttle percentage"
        // }.AddNumericRule(0, NumericParameterRule.Range(0, 100, "throttle_percentage", isSecuritySensitive: true))
        //  .AsSecuritySensitive("Throttle control affects vehicle safety"));

        // SetHeading (1011) - Would require heading in degrees (0-359)
        // _parameterValidator.RegisterValidationRules(1011, new RpcParameterValidationRules
        // {
        //     ExpectedNumericParameterCount = 1,
        //     Description = "Set Heading - Requires heading in degrees"
        // }.AddNumericRule(0, NumericParameterRule.Range(0, 359, "heading_degrees", isSecuritySensitive: true))
        //  .AsSecuritySensitive("Navigation control affects vehicle safety"));

        _logger.LogDebug("Registered parameter validation rules for {Count} KSA vehicle commands", 3);
    }
}
