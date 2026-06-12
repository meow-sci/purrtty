namespace Ghostty.Vt;

public sealed class GhosttyException : Exception
{
    public int Result { get; }

    public GhosttyException(int result)
        : base($"Ghostty error: {ResultName(result)} ({result})")
    {
        Result = result;
    }

    public GhosttyException(string message)
        : base(message)
    {
        Result = -1;
    }

    internal static void ThrowIfFailure(int result)
    {
        if (result != 0)
            throw new GhosttyException(result);
    }

    // purrtty addition: name the stable GhosttyResult codes (types.h) so a
    // logged failure reads as more than a bare integer.
    private static string ResultName(int result) => result switch
    {
        0 => "SUCCESS",
        -1 => "OUT_OF_MEMORY",
        -2 => "INVALID_VALUE",
        -3 => "OUT_OF_SPACE",
        -4 => "NO_VALUE",
        _ => "UNKNOWN",
    };
}
