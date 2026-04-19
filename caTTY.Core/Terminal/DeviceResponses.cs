namespace caTTY.Core.Terminal;

/// <summary>
///     Device query response generation methods.
///     Based on the TypeScript responses.ts implementation.
/// </summary>
public static class DeviceResponses
{
    /// <summary>
    ///     Generate Device Attributes (Primary DA) response.
    ///     Reports terminal type and supported features.
    ///     Format: CSI ? 1 ; 2 c (VT100 with Advanced Video Option)
    /// </summary>
    /// <returns>The primary DA response string</returns>
    public static string GenerateDeviceAttributesPrimaryResponse()
    {
        // Report as VT100 with Advanced Video Option
        // This is a minimal response that most applications will accept
        return "\x1b[?1;2c";
    }

    /// <summary>
    ///     Generate Device Attributes (Secondary DA) response.
    ///     Reports terminal version and firmware level.
    ///     Format: CSI > 0 ; version ; 0 c
    /// </summary>
    /// <returns>The secondary DA response string</returns>
    public static string GenerateDeviceAttributesSecondaryResponse()
    {
        // Report as VT100 compatible terminal, version 0
        return "\x1b[>0;0;0c";
    }

    /// <summary>
    ///     Generate Cursor Position Report (CPR) response.
    ///     Reports current cursor position to the application.
    ///     Format: CSI row ; col R (1-indexed coordinates)
    /// </summary>
    /// <param name="cursorX">Current cursor X position (0-indexed)</param>
    /// <param name="cursorY">Current cursor Y position (0-indexed)</param>
    /// <returns>The cursor position report response string</returns>
    public static string GenerateCursorPositionReport(int cursorX, int cursorY)
    {
        // Convert from 0-indexed to 1-indexed coordinates
        int row = cursorY + 1;
        int col = cursorX + 1;
        return $"\x1b[{row};{col}R";
    }

    /// <summary>
    ///     Generate Device Status Report (DSR) "ready" response.
    ///     Format: CSI 0 n
    /// </summary>
    /// <returns>The device status report response string</returns>
    public static string GenerateDeviceStatusReportResponse()
    {
        return "\x1b[0n";
    }

    /// <summary>
    ///     Generate Terminal Size Query response.
    ///     Reports terminal dimensions in characters.
    ///     Format: CSI 8 ; rows ; cols t
    /// </summary>
    /// <param name="rows">Terminal height in rows</param>
    /// <param name="cols">Terminal width in columns</param>
    /// <returns>The terminal size response string</returns>
    public static string GenerateTerminalSizeResponse(int rows, int cols)
    {
        return $"\x1b[8;{rows};{cols}t";
    }

    /// <summary>
    ///     Generate Character Set Query response.
    ///     Reports current character set designation.
    ///     Format: CSI ? 26 ; charset n
    /// </summary>
    /// <param name="charset">Current character set identifier</param>
    /// <returns>The character set query response string</returns>
    public static string GenerateCharacterSetQueryResponse(string charset = "0")
    {
        // Default to ASCII character set (0)
        return $"\x1b[?26;{charset}n";
    }

    /// <summary>
    ///     Generate Foreground Color Query response.
    ///     Reports current foreground color in RGB format.
    ///     Format: OSC 10 ; rgb:rrrr/gggg/bbbb ST
    /// </summary>
    /// <param name="red">Red component (0-255)</param>
    /// <param name="green">Green component (0-255)</param>
    /// <param name="blue">Blue component (0-255)</param>
    /// <returns>The foreground color query response string</returns>
    public static string GenerateForegroundColorResponse(byte red, byte green, byte blue)
    {
        // Convert 8-bit RGB values to 16-bit format (multiply by 257 to scale 0-255 to 0-65535)
        int r16 = red * 257;
        int g16 = green * 257;
        int b16 = blue * 257;
        
        // Format as 4-digit hex values
        return $"\x1b]10;rgb:{r16:x4}/{g16:x4}/{b16:x4}\x1b\\";
    }

    /// <summary>
    ///     Generate Background Color Query response.
    ///     Reports current background color in RGB format.
    ///     Format: OSC 11 ; rgb:rrrr/gggg/bbbb ST
    /// </summary>
    /// <param name="red">Red component (0-255)</param>
    /// <param name="green">Green component (0-255)</param>
    /// <param name="blue">Blue component (0-255)</param>
    /// <returns>The background color query response string</returns>
    public static string GenerateBackgroundColorResponse(byte red, byte green, byte blue)
    {
        // Convert 8-bit RGB values to 16-bit format (multiply by 257 to scale 0-255 to 0-65535)
        int r16 = red * 257;
        int g16 = green * 257;
        int b16 = blue * 257;
        
        // Format as 4-digit hex values
        return $"\x1b]11;rgb:{r16:x4}/{g16:x4}/{b16:x4}\x1b\\";
    }
}
