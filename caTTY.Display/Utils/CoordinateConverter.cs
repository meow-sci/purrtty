using Brutal.Numerics;

namespace caTTY.Display.Utils;

/// <summary>
/// Converts between screen pixel coordinates and terminal cell coordinates.
/// Handles boundary checking, coordinate clamping, and metrics updates.
/// </summary>
public class CoordinateConverter
{
    private float _characterWidth;
    private float _lineHeight;
    private float2 _terminalOrigin;

    /// <summary>
    /// Initializes a new instance of the CoordinateConverter with default metrics.
    /// </summary>
    public CoordinateConverter()
    {
        // Initialize with fallback metrics (8px width, 16px height as per error handling spec)
        _characterWidth = 8.0f;
        _lineHeight = 16.0f;
        _terminalOrigin = new float2(0, 0);
    }

    /// <summary>
    /// Updates the character metrics and terminal origin for coordinate conversion.
    /// </summary>
    /// <param name="charWidth">Width of a single character in pixels</param>
    /// <param name="lineHeight">Height of a single line in pixels</param>
    /// <param name="origin">Top-left origin of the terminal area in screen coordinates</param>
    public void UpdateMetrics(float charWidth, float lineHeight, float2 origin)
    {
        // Validate metrics to prevent division by zero
        _characterWidth = charWidth > 0 ? charWidth : 8.0f;
        _lineHeight = lineHeight > 0 ? lineHeight : 16.0f;
        _terminalOrigin = origin;
    }

    /// <summary>
    /// Converts screen pixel position to 1-based terminal cell coordinates.
    /// </summary>
    /// <param name="pixelPos">Screen pixel position</param>
    /// <param name="terminalWidth">Terminal width in columns</param>
    /// <param name="terminalHeight">Terminal height in rows</param>
    /// <returns>1-based terminal coordinates (X1, Y1) or null if outside bounds</returns>
    public (int X1, int Y1)? PixelToCell(float2 pixelPos, int terminalWidth, int terminalHeight)
    {
        try
        {
            // Convert to relative position within terminal
            float2 relativePos = pixelPos - _terminalOrigin;

            // Convert to 0-based cell coordinates
            int col0 = (int)(relativePos.X / _characterWidth);
            int row0 = (int)(relativePos.Y / _lineHeight);

            // Clamp to valid terminal range (0-based)
            col0 = Math.Max(0, Math.Min(col0, terminalWidth - 1));
            row0 = Math.Max(0, Math.Min(row0, terminalHeight - 1));

            // Convert to 1-based coordinates as required by terminal protocols
            return (col0 + 1, row0 + 1);
        }
        catch
        {
            // Fallback to (1,1) coordinates on any error
            return (1, 1);
        }
    }

    /// <summary>
    /// Converts 1-based terminal cell coordinates to screen pixel position.
    /// </summary>
    /// <param name="x1">1-based column coordinate</param>
    /// <param name="y1">1-based row coordinate</param>
    /// <returns>Screen pixel position of the top-left corner of the cell</returns>
    public float2 CellToPixel(int x1, int y1)
    {
        // Convert to 0-based coordinates
        int col0 = Math.Max(0, x1 - 1);
        int row0 = Math.Max(0, y1 - 1);

        // Calculate pixel position
        float pixelX = _terminalOrigin.X + (col0 * _characterWidth);
        float pixelY = _terminalOrigin.Y + (row0 * _lineHeight);

        return new float2(pixelX, pixelY);
    }

    /// <summary>
    /// Checks if a pixel position is within the terminal bounds.
    /// </summary>
    /// <param name="pixelPos">Screen pixel position to check</param>
    /// <param name="terminalSize">Terminal size in pixels (width, height)</param>
    /// <returns>True if the position is within terminal bounds</returns>
    public bool IsWithinBounds(float2 pixelPos, float2 terminalSize)
    {
        float2 relativePos = pixelPos - _terminalOrigin;
        
        return relativePos.X >= 0 && 
               relativePos.Y >= 0 && 
               relativePos.X < terminalSize.X && 
               relativePos.Y < terminalSize.Y;
    }

    /// <summary>
    /// Gets the current character width in pixels.
    /// </summary>
    public float CharacterWidth => _characterWidth;

    /// <summary>
    /// Gets the current line height in pixels.
    /// </summary>
    public float LineHeight => _lineHeight;

    /// <summary>
    /// Gets the current terminal origin position.
    /// </summary>
    public float2 TerminalOrigin => _terminalOrigin;
}