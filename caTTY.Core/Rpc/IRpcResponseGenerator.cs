namespace caTTY.Core.Rpc;

/// <summary>
/// Interface for generating RPC response sequences in the proper terminal escape sequence format.
/// Handles encoding response data and error messages back to the terminal.
/// </summary>
public interface IRpcResponseGenerator
{
    /// <summary>
    /// Generates a response sequence for a successful query command.
    /// Uses the format: ESC [ > Pn ; 1 ; R [; additional parameters]
    /// </summary>
    /// <param name="commandId">The original command ID from the query</param>
    /// <param name="data">The response data to encode (can be null for simple acknowledgments)</param>
    /// <returns>The formatted response sequence as bytes</returns>
    byte[] GenerateResponse(int commandId, object? data);

    /// <summary>
    /// Generates an error response sequence for a failed command.
    /// Uses the format: ESC [ > 9999 ; 1 ; E [; error details]
    /// </summary>
    /// <param name="commandId">The original command ID that failed</param>
    /// <param name="errorMessage">The error message to include</param>
    /// <returns>The formatted error sequence as bytes</returns>
    byte[] GenerateError(int commandId, string errorMessage);

    /// <summary>
    /// Generates a timeout error response for a query command that exceeded its timeout.
    /// Uses the format: ESC [ > 9999 ; 1 ; E with timeout-specific error details
    /// </summary>
    /// <param name="commandId">The original command ID that timed out</param>
    /// <returns>The formatted timeout error sequence as bytes</returns>
    byte[] GenerateTimeout(int commandId);

    /// <summary>
    /// Generates a system error response for general RPC system failures.
    /// Uses the format: ESC [ > 9999 ; 1 ; E with system error details
    /// </summary>
    /// <param name="errorMessage">The system error message</param>
    /// <returns>The formatted system error sequence as bytes</returns>
    byte[] GenerateSystemError(string errorMessage);
}