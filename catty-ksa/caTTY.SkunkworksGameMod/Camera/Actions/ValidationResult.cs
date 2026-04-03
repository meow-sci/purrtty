namespace caTTY.SkunkworksGameMod.Camera.Actions;

/// <summary>
/// Result of validating a camera action's parameters.
/// </summary>
public sealed class ValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }

    public static ValidationResult Success() => new() { IsValid = true };
    public static ValidationResult Fail(string message) => new() { IsValid = false, ErrorMessage = message };
}
