using System;
using caTTY.Core.Types;
using Brutal.Numerics;

namespace caTTY.Display.Rendering;

/// <summary>
/// Style manager for handling text styling and attribute application.
/// Based on TypeScript DomStyleManager implementation.
/// </summary>
public class StyleManager
{
    private readonly Performance.PerformanceStopwatch _perfWatch;
    private readonly ColorResolver _colorResolver;

    /// <summary>
    /// Initializes a new instance of the StyleManager class.
    /// </summary>
    /// <param name="perfWatch">Performance stopwatch for timing measurements</param>
    /// <param name="colorResolver">Color resolver for resolving underline colors</param>
    public StyleManager(Performance.PerformanceStopwatch perfWatch, ColorResolver colorResolver)
    {
        _perfWatch = perfWatch ?? throw new ArgumentNullException(nameof(perfWatch));
        _colorResolver = colorResolver ?? throw new ArgumentNullException(nameof(colorResolver));
    }

    /// <summary>
    /// Apply SGR attributes to colors and return the final rendering colors.
    /// </summary>
    /// <param name="attributes">SGR attributes to apply</param>
    /// <param name="baseForeground">Base foreground color</param>
    /// <param name="baseBackground">Base background color</param>
    /// <returns>Tuple of (foreground, background) colors with attributes applied</returns>
    public (float4 foreground, float4 background) ApplyAttributes(
        SgrAttributes attributes,
        float4 baseForeground,
        float4 baseBackground)
    {
//        _perfWatch.Start("StyleManager.ApplyAttributes");
        try
        {
            float4 foreground = baseForeground;
            float4 background = baseBackground;

            // Apply bold (brighten foreground)
            if (attributes.Bold)
            {
                foreground = BrightenColor(foreground, 1.3f);
            }

            // Apply faint/dim (darken foreground)
            if (attributes.Faint)
            {
                foreground = DarkenColor(foreground, 0.7f);
            }

            // Apply inverse (swap foreground and background)
            if (attributes.Inverse)
            {
                (foreground, background) = (background, foreground);
            }

            // Apply hidden (make foreground same as background)
            if (attributes.Hidden)
            {
                foreground = background;
            }

            return (foreground, background);
        }
        finally
        {
//            _perfWatch.Stop("StyleManager.ApplyAttributes");
        }
    }

    /// <summary>
    /// Brighten a color by a factor.
    /// </summary>
    /// <param name="color">Original color</param>
    /// <param name="factor">Brightness factor (>1.0 brightens, <1.0 darkens)</param>
    /// <returns>Brightened color</returns>
    public float4 BrightenColor(float4 color, float factor)
    {
        return new float4(
            Math.Min(1.0f, color.X * factor),
            Math.Min(1.0f, color.Y * factor),
            Math.Min(1.0f, color.Z * factor),
            color.W
        );
    }

    /// <summary>
    /// Darken a color by a factor.
    /// </summary>
    /// <param name="color">Original color</param>
    /// <param name="factor">Darkness factor (0.0-1.0, where 0.0 is black)</param>
    /// <returns>Darkened color</returns>
    public float4 DarkenColor(float4 color, float factor)
    {
        return new float4(
            color.X * factor,
            color.Y * factor,
            color.Z * factor,
            color.W
        );
    }

    /// <summary>
    /// Check if underline should be rendered based on SGR attributes.
    /// </summary>
    /// <param name="attributes">SGR attributes</param>
    /// <returns>True if underline should be rendered</returns>
    public bool ShouldRenderUnderline(SgrAttributes attributes)
    {
        return attributes.Underline && attributes.UnderlineStyle != UnderlineStyle.None;
    }

    /// <summary>
    /// Get the underline color to use for rendering.
    /// </summary>
    /// <param name="attributes">SGR attributes</param>
    /// <param name="foregroundColor">Current foreground color</param>
    /// <returns>Color to use for underline</returns>
    public float4 GetUnderlineColor(SgrAttributes attributes, float4 foregroundColor)
    {
        if (attributes.UnderlineColor.HasValue)
        {
            var resolvedColor = _colorResolver.Resolve(attributes.UnderlineColor.Value, false);
            return resolvedColor;
        }

        return foregroundColor;
    }

    /// <summary>
    /// Get underline thickness based on style.
    /// </summary>
    /// <param name="style">Underline style</param>
    /// <returns>Thickness in pixels</returns>
    public float GetUnderlineThickness(UnderlineStyle style)
    {
        return style switch
        {
            UnderlineStyle.Single => 1.0f,
            UnderlineStyle.Double => 1.0f, // Will be drawn twice
            UnderlineStyle.Curly => 1.0f,
            UnderlineStyle.Dotted => 1.0f,
            UnderlineStyle.Dashed => 1.0f,
            _ => 1.0f
        };
    }

    /// <summary>
    /// Check if strikethrough should be rendered.
    /// </summary>
    /// <param name="attributes">SGR attributes</param>
    /// <returns>True if strikethrough should be rendered</returns>
    public bool ShouldRenderStrikethrough(SgrAttributes attributes)
    {
        return attributes.Strikethrough;
    }

    /// <summary>
    /// Check if blink effect should be applied (for future implementation).
    /// </summary>
    /// <param name="attributes">SGR attributes</param>
    /// <returns>True if blink should be applied</returns>
    public bool ShouldApplyBlink(SgrAttributes attributes)
    {
        return attributes.Blink;
    }

    /// <summary>
    /// Apply color modifications for special rendering modes.
    /// </summary>
    /// <param name="color">Base color</param>
    /// <param name="mode">Rendering mode</param>
    /// <returns>Modified color</returns>
    public float4 ApplyRenderingMode(float4 color, RenderingMode mode)
    {
        return mode switch
        {
            RenderingMode.Normal => color,
            RenderingMode.Selection => BlendWithSelection(color),
            RenderingMode.Cursor => BlendWithCursor(color),
            _ => color
        };
    }

    /// <summary>
    /// Blend color with selection background.
    /// </summary>
    private float4 BlendWithSelection(float4 color)
    {
        var selectionColor = ThemeManager.GetSelectionColor();
        // Simple alpha blend with selection color
        return new float4(
            color.X * 0.7f + selectionColor.X * 0.3f,
            color.Y * 0.7f + selectionColor.Y * 0.3f,
            color.Z * 0.7f + selectionColor.Z * 0.3f,
            color.W
        );
    }

    /// <summary>
    /// Blend color with cursor background.
    /// </summary>
    private float4 BlendWithCursor(float4 color)
    {
        var cursorColor = ThemeManager.GetCursorColor();
        // Invert for cursor visibility
        return new float4(
            1.0f - color.X,
            1.0f - color.Y,
            1.0f - color.Z,
            color.W
        );
    }
}

/// <summary>
/// Rendering mode for special color effects.
/// </summary>
public enum RenderingMode
{
    Normal,
    Selection,
    Cursor
}