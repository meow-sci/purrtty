namespace caTTY.Core.Terminal;

/// <summary>
///     Handles validation logic for session operations.
/// </summary>
internal class SessionValidator
{
    /// <summary>
    ///     Throws ObjectDisposedException if the manager has been disposed.
    /// </summary>
    /// <param name="disposed">Whether the manager is disposed</param>
    /// <param name="typeName">Name of the type being validated</param>
    /// <exception cref="ObjectDisposedException">Thrown if disposed is true</exception>
    public static void ThrowIfDisposed(bool disposed, string typeName)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(typeName);
        }
    }

    /// <summary>
    ///     Validates that maximum sessions has not been reached.
    /// </summary>
    /// <param name="sessionCount">Current number of sessions</param>
    /// <param name="maxSessions">Maximum allowed sessions</param>
    /// <exception cref="InvalidOperationException">Thrown if at maximum capacity</exception>
    public static void ValidateMaxSessionsNotReached(int sessionCount, int maxSessions)
    {
        if (sessionCount >= maxSessions)
        {
            throw new InvalidOperationException($"Maximum number of sessions ({maxSessions}) reached");
        }
    }

    /// <summary>
    ///     Validates that the maximum sessions value is valid.
    /// </summary>
    /// <param name="maxSessions">Maximum sessions value to validate</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if maxSessions is invalid</exception>
    public static void ValidateMaxSessions(int maxSessions)
    {
        if (maxSessions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSessions), "Maximum sessions must be greater than zero");
        }
    }
}
