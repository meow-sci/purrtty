namespace caTTY.Core.Terminal;

/// <summary>
///     Exception thrown when a custom shell fails to start.
/// </summary>
public class CustomShellStartException : Exception
{
    /// <summary>
    ///     Creates a new CustomShellStartException.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="shellId">The custom shell ID that failed</param>
    public CustomShellStartException(string message, string? shellId = null) : base(message)
    {
        ShellId = shellId;
    }

    /// <summary>
    ///     Creates a new CustomShellStartException with an inner exception.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="innerException">The inner exception</param>
    /// <param name="shellId">The custom shell ID that failed</param>
    public CustomShellStartException(string message, Exception innerException, string? shellId = null)
        : base(message, innerException)
    {
        ShellId = shellId;
    }

    /// <summary>
    ///     Gets the custom shell ID that failed to start.
    /// </summary>
    public string? ShellId { get; }
}
