using System;
using System.Text;
using caTTY.Core.Types;

namespace caTTY.Core.Input;

/// <summary>
///     Generates terminal escape sequences for mouse events.
///     Supports both standard X10/X11 and SGR encoding formats.
/// </summary>
public static class EscapeSequenceGenerator
{
    // X10/X11 coordinate limits (1-based coordinates + 32 offset = max 255, so max coordinate is 223)
    private const int X10MaxCoordinate = 223;
    
    /// <summary>
    ///     Generates escape sequence for mouse button press event.
    /// </summary>
    /// <param name="button">Mouse button that was pressed</param>
    /// <param name="x1">X coordinate (1-based)</param>
    /// <param name="y1">Y coordinate (1-based)</param>
    /// <param name="modifiers">Modifier keys held during press</param>
    /// <param name="sgrEncoding">Whether to use SGR encoding format</param>
    /// <returns>Escape sequence string</returns>
    public static string GenerateMousePress(
        MouseButton button,
        int x1,
        int y1,
        MouseKeyModifiers modifiers,
        bool sgrEncoding)
    {
        var buttonCode = GetButtonCode(button, modifiers);
        var (clampedX, clampedY) = ClampCoordinates(x1, y1, sgrEncoding);
        
        if (sgrEncoding)
        {
            // SGR format: ESC[<button;x;yM
            return $"\x1b[<{buttonCode};{clampedX};{clampedY}M";
        }
        
        // Standard X10/X11 format: ESC[M + 3 bytes (button, x, y with 32 offset)
        return GenerateX10Sequence(buttonCode, clampedX, clampedY);
    }
    
    /// <summary>
    ///     Generates escape sequence for mouse button release event.
    /// </summary>
    /// <param name="button">Mouse button that was released</param>
    /// <param name="x1">X coordinate (1-based)</param>
    /// <param name="y1">Y coordinate (1-based)</param>
    /// <param name="modifiers">Modifier keys held during release</param>
    /// <param name="sgrEncoding">Whether to use SGR encoding format</param>
    /// <returns>Escape sequence string</returns>
    public static string GenerateMouseRelease(
        MouseButton button,
        int x1,
        int y1,
        MouseKeyModifiers modifiers,
        bool sgrEncoding)
    {
        var (clampedX, clampedY) = ClampCoordinates(x1, y1, sgrEncoding);
        
        if (sgrEncoding)
        {
            // SGR format: ESC[<button;x;ym (lowercase 'm' for release)
            var buttonCode = GetButtonCode(button, modifiers);
            return $"\x1b[<{buttonCode};{clampedX};{clampedY}m";
        }
        
        // Standard X10/X11 format: button 3 indicates release (regardless of which button)
        var releaseButtonCode = 3 + GetModifierBits(modifiers);
        return GenerateX10Sequence(releaseButtonCode, clampedX, clampedY);
    }
    
    /// <summary>
    ///     Generates escape sequence for mouse motion event.
    /// </summary>
    /// <param name="button">Mouse button held during motion (or Left if no button)</param>
    /// <param name="x1">X coordinate (1-based)</param>
    /// <param name="y1">Y coordinate (1-based)</param>
    /// <param name="modifiers">Modifier keys held during motion</param>
    /// <param name="sgrEncoding">Whether to use SGR encoding format</param>
    /// <returns>Escape sequence string</returns>
    public static string GenerateMouseMotion(
        MouseButton button,
        int x1,
        int y1,
        MouseKeyModifiers modifiers,
        bool sgrEncoding)
    {
        var (clampedX, clampedY) = ClampCoordinates(x1, y1, sgrEncoding);
        
        if (sgrEncoding)
        {
            // SGR format: ESC[<button;x;yM (same as press for motion)
            var buttonCode = GetMotionButtonCode(button, modifiers);
            return $"\x1b[<{buttonCode};{clampedX};{clampedY}M";
        }
        
        // Standard X10/X11 format: motion uses button + 32 for drag
        var motionButtonCode = GetMotionButtonCode(button, modifiers);
        return GenerateX10Sequence(motionButtonCode, clampedX, clampedY);
    }
    
    /// <summary>
    ///     Generates escape sequence for mouse wheel event.
    /// </summary>
    /// <param name="directionUp">True for wheel up, false for wheel down</param>
    /// <param name="x1">X coordinate (1-based)</param>
    /// <param name="y1">Y coordinate (1-based)</param>
    /// <param name="modifiers">Modifier keys held during wheel event</param>
    /// <param name="sgrEncoding">Whether to use SGR encoding format</param>
    /// <returns>Escape sequence string</returns>
    public static string GenerateMouseWheel(
        bool directionUp,
        int x1,
        int y1,
        MouseKeyModifiers modifiers,
        bool sgrEncoding)
    {
        var wheelButton = directionUp ? MouseButton.WheelUp : MouseButton.WheelDown;
        var buttonCode = GetButtonCode(wheelButton, modifiers);
        var (clampedX, clampedY) = ClampCoordinates(x1, y1, sgrEncoding);
        
        if (sgrEncoding)
        {
            // SGR format: ESC[<button;x;yM (wheel events are press-only)
            return $"\x1b[<{buttonCode};{clampedX};{clampedY}M";
        }
        
        // Standard X10/X11 format
        return GenerateX10Sequence(buttonCode, clampedX, clampedY);
    }
    
    /// <summary>
    ///     Gets the button code for a mouse button with modifiers.
    /// </summary>
    private static int GetButtonCode(MouseButton button, MouseKeyModifiers modifiers)
    {
        var baseCode = (int)button;
        var modifierBits = GetModifierBits(modifiers);
        return baseCode + modifierBits;
    }
    
    /// <summary>
    ///     Gets the button code for mouse motion events.
    /// </summary>
    private static int GetMotionButtonCode(MouseButton button, MouseKeyModifiers modifiers)
    {
        // For motion events, add 32 to indicate motion in X10/X11 protocol
        var baseCode = (int)button + 32;
        var modifierBits = GetModifierBits(modifiers);
        return baseCode + modifierBits;
    }
    
    /// <summary>
    ///     Converts modifier flags to bit values for mouse protocol.
    /// </summary>
    private static int GetModifierBits(MouseKeyModifiers modifiers)
    {
        return (int)modifiers;
    }
    
    /// <summary>
    ///     Clamps coordinates to valid ranges based on encoding format.
    /// </summary>
    private static (int x, int y) ClampCoordinates(int x1, int y1, bool sgrEncoding)
    {
        // Ensure coordinates are at least 1
        var clampedX = Math.Max(1, x1);
        var clampedY = Math.Max(1, y1);
        
        // For X10/X11 encoding, clamp to maximum representable coordinate
        if (!sgrEncoding)
        {
            clampedX = Math.Min(X10MaxCoordinate, clampedX);
            clampedY = Math.Min(X10MaxCoordinate, clampedY);
        }
        
        return (clampedX, clampedY);
    }
    
    /// <summary>
    ///     Generates X10/X11 format escape sequence.
    /// </summary>
    private static string GenerateX10Sequence(int buttonCode, int x1, int y1)
    {
        // X10/X11 format: ESC[M + button byte + x byte + y byte (all with 32 offset)
        var buttonByte = (char)(32 + buttonCode);
        var xByte = (char)(32 + x1);
        var yByte = (char)(32 + y1);
        
        var sb = new StringBuilder(6);
        sb.Append('\x1b');
        sb.Append('[');
        sb.Append('M');
        sb.Append(buttonByte);
        sb.Append(xByte);
        sb.Append(yByte);
        
        return sb.ToString();
    }
}