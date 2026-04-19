namespace caTTY.Core.Rpc;

/// <summary>
/// Interface for validating RPC command parameters before execution.
/// Provides security validation and parameter safety checks.
/// </summary>
public interface IRpcParameterValidator
{
    /// <summary>
    /// Validates parameters for a specific command ID.
    /// </summary>
    /// <param name="commandId">The command ID being executed</param>
    /// <param name="parameters">The parameters to validate</param>
    /// <returns>A validation result indicating success or failure with details</returns>
    RpcParameterValidationResult ValidateParameters(int commandId, RpcParameters parameters);

    /// <summary>
    /// Registers validation rules for a specific command ID.
    /// </summary>
    /// <param name="commandId">The command ID to register rules for</param>
    /// <param name="validationRules">The validation rules to apply</param>
    void RegisterValidationRules(int commandId, RpcParameterValidationRules validationRules);

    /// <summary>
    /// Removes validation rules for a specific command ID.
    /// </summary>
    /// <param name="commandId">The command ID to remove rules for</param>
    void UnregisterValidationRules(int commandId);

    /// <summary>
    /// Checks if validation rules are registered for a command ID.
    /// </summary>
    /// <param name="commandId">The command ID to check</param>
    /// <returns>True if validation rules are registered</returns>
    bool HasValidationRules(int commandId);
}