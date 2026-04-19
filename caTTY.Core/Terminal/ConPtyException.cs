namespace caTTY.Core.Terminal;

/// <summary>
///     Exception thrown when ConPTY operations fail.
/// </summary>
public class ConPtyException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the ConPtyException class.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="win32ErrorCode">The Win32 error code</param>
    public ConPtyException(string message, int win32ErrorCode) : base(message)
    {
        Win32ErrorCode = win32ErrorCode;
    }

    /// <summary>
    ///     Initializes a new instance of the ConPtyException class.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="win32ErrorCode">The Win32 error code</param>
    /// <param name="innerException">The inner exception</param>
    public ConPtyException(string message, int win32ErrorCode, Exception innerException) : base(message, innerException)
    {
        Win32ErrorCode = win32ErrorCode;
    }

    /// <summary>
    ///     Gets the Win32 error code associated with the ConPTY failure.
    /// </summary>
    public int Win32ErrorCode { get; }
}
