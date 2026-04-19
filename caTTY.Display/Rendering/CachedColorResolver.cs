using System;
using System.Collections.Generic;
using Brutal.Numerics;
using caTTY.Core.Types;
using TerminalColor = caTTY.Core.Types.Color;

namespace caTTY.Display.Rendering;

/// <summary>
/// High-performance color resolver that caches resolved colors to avoid
/// repeated resolution during rendering. Addresses the #1 performance hotspot
/// identified in profiling: ~41% of render time spent in color resolution.
/// 
/// Key optimizations:
/// 1. Cache default fg/bg colors - resolved once per theme change, not per cell
/// 2. Cache named ANSI colors (0-15) - theme-dependent but rarely change
/// 3. Cache indexed colors (16-255) - computed colors that never change
/// 4. Cache RGB colors - direct conversions that never change
/// </summary>
public class CachedColorResolver : IDisposable
{
    private readonly ColorResolver _baseResolver;
    private readonly Performance.PerformanceStopwatch _perfWatch;
    
    // Cached default colors - resolved once per theme change
    private float4 _cachedDefaultForeground;
    private float4 _cachedDefaultBackground;
    private bool _defaultColorsValid;
    
    // Cached ANSI named colors (0-15) - theme-dependent
    // Index 0-15 = standard colors, same order as NamedColor enum
    private readonly float4[] _cachedNamedColors = new float4[16];
    private bool _namedColorsValid;
    
    // Cache for indexed colors (16-255) - these are computed and never change
    // We only cache 16-255 since 0-15 are theme-dependent and in _cachedNamedColors
    private readonly float4[] _cachedIndexedColors = new float4[240]; // 256 - 16 = 240
    private bool _indexedColorsInitialized;
    
    // Cache for RGB colors - keyed by packed RGB value
    // Using a simple dictionary since RGB colors are relatively rare in typical terminal output
    private readonly Dictionary<uint, float4> _cachedRgbColors = new(256);
    
    /// <summary>
    /// Initializes a new instance of the CachedColorResolver class.
    /// </summary>
    /// <param name="baseResolver">The underlying color resolver for cache misses</param>
    /// <param name="perfWatch">Performance stopwatch for timing measurements</param>
    public CachedColorResolver(ColorResolver baseResolver, Performance.PerformanceStopwatch perfWatch)
    {
        _baseResolver = baseResolver ?? throw new ArgumentNullException(nameof(baseResolver));
        _perfWatch = perfWatch ?? throw new ArgumentNullException(nameof(perfWatch));
        
        // Subscribe to theme changes to invalidate caches
        ThemeManager.ThemeChanged += OnThemeChanged;
        
        // Initialize caches on construction
        InvalidateThemeDependentCaches();
        InitializeIndexedColorCache();
    }
    
    /// <summary>
    /// Resolve SGR color type to ImGui float4 color value using cached values where possible.
    /// This is the hot path that replaces ColorResolver.Resolve() in the render loop.
    /// 
    /// PERFORMANCE CRITICAL: No timing, no try/finally, inline everything possible.
    /// Called ~1.2M times per frame (2x per cell for fg/bg).
    /// </summary>
    /// <param name="color">SGR color specification</param>
    /// <param name="isBackground">Whether this is a background color (affects defaults)</param>
    /// <returns>ImGui float4 color value</returns>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public float4 Resolve(TerminalColor? color, bool isBackground = false)
    {
        // Fast path: null/default color - most common case (~80% of cells)
        // Inline the cache check to avoid method call overhead
        if (!color.HasValue)
        {
            if (!_defaultColorsValid)
            {
                _cachedDefaultForeground = ThemeManager.GetDefaultForeground();
                _cachedDefaultBackground = ThemeManager.GetDefaultBackground();
                _defaultColorsValid = true;
            }
            return isBackground ? _cachedDefaultBackground : _cachedDefaultForeground;
        }

        // Handle specific color types
        var c = color.Value;
        switch (c.Type)
        {
            case ColorType.Named:
                return ResolveNamedColorCached(c.NamedColor);
                
            case ColorType.Indexed:
                return ResolveIndexedColorCached(c.Index);
                
            case ColorType.Rgb:
                return ResolveRgbColorCached(c.Red, c.Green, c.Blue);
                
            default:
                // Unknown type - fall back to default (rare path, ok to have method call)
                return isBackground ? GetCachedDefaultBackground() : GetCachedDefaultForeground();
        }
    }
    
    /// <summary>
    /// Gets the cached default foreground color.
    /// </summary>
    public float4 GetCachedDefaultForeground()
    {
        if (!_defaultColorsValid)
        {
            _cachedDefaultForeground = ThemeManager.GetDefaultForeground();
            _cachedDefaultBackground = ThemeManager.GetDefaultBackground();
            _defaultColorsValid = true;
        }
        return _cachedDefaultForeground;
    }
    
    /// <summary>
    /// Gets the cached default background color.
    /// </summary>
    public float4 GetCachedDefaultBackground()
    {
        if (!_defaultColorsValid)
        {
            _cachedDefaultForeground = ThemeManager.GetDefaultForeground();
            _cachedDefaultBackground = ThemeManager.GetDefaultBackground();
            _defaultColorsValid = true;
        }
        return _cachedDefaultBackground;
    }
    
    /// <summary>
    /// Resolves a named ANSI color using the cache.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private float4 ResolveNamedColorCached(NamedColor namedColor)
    {
        if (!_namedColorsValid)
        {
            // Resolve all 16 ANSI colors from theme
            for (int i = 0; i < 16; i++)
            {
                _cachedNamedColors[i] = ThemeManager.ResolveThemeColor(i);
            }
            _namedColorsValid = true;
        }
        
        int index = (int)namedColor;
        // Array bounds check is unavoidable but fast
        return (uint)index < 16 ? _cachedNamedColors[index] : _cachedDefaultForeground;
    }
    
    /// <summary>
    /// Resolves an indexed color (0-255) using the cache.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private float4 ResolveIndexedColorCached(byte index)
    {
        // Indices 0-15 are theme-dependent ANSI colors
        if (index <= 15)
        {
            if (!_namedColorsValid)
            {
                for (int i = 0; i < 16; i++)
                {
                    _cachedNamedColors[i] = ThemeManager.ResolveThemeColor(i);
                }
                _namedColorsValid = true;
            }
            return _cachedNamedColors[index];
        }
        
        // Indices 16-255 are fixed computed colors
        if (!_indexedColorsInitialized)
        {
            InitializeIndexedColorCache();
        }
        return _cachedIndexedColors[index - 16];
    }
    
    /// <summary>
    /// Resolves an RGB color using the cache.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private float4 ResolveRgbColorCached(byte red, byte green, byte blue)
    {
        // Pack RGB into a single uint key
        uint key = ((uint)red << 16) | ((uint)green << 8) | blue;
        
        if (_cachedRgbColors.TryGetValue(key, out var cached))
        {
            return cached;
        }
        
        // Cache miss - compute and store
        var color = new float4(red / 255.0f, green / 255.0f, blue / 255.0f, 1.0f);
        
        // Limit cache size to prevent unbounded growth
        // RGB colors in typical terminal usage are relatively rare
        if (_cachedRgbColors.Count < 4096)
        {
            _cachedRgbColors[key] = color;
        }
        
        return color;
    }
    
    /// <summary>
    /// Initializes the cached indexed colors (16-255).
    /// </summary>
    private void InitializeIndexedColorCache()
    {
        // Colors 16-231: 6x6x6 color cube
        for (int i = 16; i <= 231; i++)
        {
            _cachedIndexedColors[i - 16] = GetCubeColor(i - 16);
        }
        
        // Colors 232-255: grayscale ramp
        for (int i = 232; i <= 255; i++)
        {
            _cachedIndexedColors[i - 16] = GetGrayscaleColor(i - 232);
        }
        
        _indexedColorsInitialized = true;
    }
    
    /// <summary>
    /// Generate color from 6x6x6 color cube.
    /// </summary>
    /// <param name="cubeIndex">Index in the color cube (0-215)</param>
    /// <returns>float4 color</returns>
    private static float4 GetCubeColor(int cubeIndex)
    {
        int r = cubeIndex / 36;
        int g = (cubeIndex % 36) / 6;
        int b = cubeIndex % 6;

        static float ToColorValue(int n) => n == 0 ? 0.0f : (55 + n * 40) / 255.0f;

        return new float4(ToColorValue(r), ToColorValue(g), ToColorValue(b), 1.0f);
    }

    /// <summary>
    /// Generate grayscale color.
    /// </summary>
    /// <param name="grayIndex">Index in grayscale ramp (0-23)</param>
    /// <returns>float4 color</returns>
    private static float4 GetGrayscaleColor(int grayIndex)
    {
        float gray = (8 + grayIndex * 10) / 255.0f;
        return new float4(gray, gray, gray, 1.0f);
    }
    
    /// <summary>
    /// Invalidates all theme-dependent caches. Called when the theme changes.
    /// </summary>
    public void InvalidateThemeDependentCaches()
    {
        _defaultColorsValid = false;
        _namedColorsValid = false;
        // Note: _indexedColorsInitialized is NOT reset - those colors don't depend on theme
        // Note: RGB cache is NOT cleared - RGB colors don't depend on theme
    }
    
    /// <summary>
    /// Handler for theme change events.
    /// </summary>
    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        InvalidateThemeDependentCaches();
    }
    
    /// <summary>
    /// Clears all caches. Primarily useful for testing.
    /// </summary>
    public void ClearAllCaches()
    {
        _defaultColorsValid = false;
        _namedColorsValid = false;
        _indexedColorsInitialized = false;
        _cachedRgbColors.Clear();
    }
    
    /// <summary>
    /// Gets cache statistics for diagnostics.
    /// </summary>
    public (int RgbCacheSize, bool DefaultColorsValid, bool NamedColorsValid, bool IndexedColorsInitialized) GetCacheStats()
    {
        return (_cachedRgbColors.Count, _defaultColorsValid, _namedColorsValid, _indexedColorsInitialized);
    }
    
    /// <summary>
    /// Resolves and processes cell colors in a single fused operation.
    /// Returns final uint colors ready for ImGui draw calls.
    /// 
    /// This method combines:
    /// - Color resolution (fg/bg)
    /// - SGR attribute application (inverse, faint, bold color changes)
    /// - Opacity application
    /// - Float4 to uint32 conversion
    /// 
    /// Optimizations:
    /// - Skips background resolution if not needed
    /// - Early exit for hidden cells
    /// - Direct uint output (no intermediate float4 storage needed by caller)
    /// </summary>
    /// <param name="attributes">Cell SGR attributes</param>
    /// <param name="fgColorU32">Output: final foreground color as uint32</param>
    /// <param name="bgColorU32">Output: final background color as uint32</param>
    /// <param name="needsBackground">Output: true if background needs to be drawn</param>
    /// <param name="fgColor">Output: final foreground color as float4 (for decorations)</param>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void ResolveCellColors(
        SgrAttributes attributes,
        out uint fgColorU32,
        out uint bgColorU32,
        out bool needsBackground,
        out float4 fgColor)
    {
        // Resolve base colors
        float4 baseFg = Resolve(attributes.ForegroundColor, false);
        float4 baseBg = Resolve(attributes.BackgroundColor, true);
        
        // Apply SGR attributes inline (avoiding StyleManager.ApplyAttributes call)
        fgColor = baseFg;
        float4 bgColor = baseBg;
        
        // Apply bold (brighten foreground)
        if (attributes.Bold)
        {
            fgColor = new float4(
                Math.Min(1.0f, fgColor.X * 1.3f),
                Math.Min(1.0f, fgColor.Y * 1.3f),
                Math.Min(1.0f, fgColor.Z * 1.3f),
                fgColor.W
            );
        }
        
        // Apply faint/dim (darken foreground)
        if (attributes.Faint)
        {
            fgColor = new float4(
                fgColor.X * 0.7f,
                fgColor.Y * 0.7f,
                fgColor.Z * 0.7f,
                fgColor.W
            );
        }
        
        // Apply inverse (swap foreground and background)
        if (attributes.Inverse)
        {
            (fgColor, bgColor) = (bgColor, fgColor);
        }
        
        // Apply hidden (make foreground same as background)
        if (attributes.Hidden)
        {
            fgColor = bgColor;
        }
        
        // Apply opacity
        fgColor = new float4(fgColor.X, fgColor.Y, fgColor.Z, fgColor.W * OpacityManager.CurrentForegroundOpacity);
        bgColor = new float4(bgColor.X, bgColor.Y, bgColor.Z, bgColor.W * OpacityManager.CurrentCellBackgroundOpacity);
        
        // Determine if background needs to be drawn
        needsBackground = attributes.BackgroundColor.HasValue || attributes.Inverse;
        
        // Convert to uint32 for ImGui
        fgColorU32 = ColorToU32(fgColor);
        bgColorU32 = needsBackground ? ColorToU32(bgColor) : 0;
    }
    
    /// <summary>
    /// Converts a float4 color to uint32 format for ImGui.
    /// This is the same conversion ImGui.ColorConvertFloat4ToU32 does,
    /// but inlined here to avoid the function call overhead.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static uint ColorToU32(float4 color)
    {
        // ImGui uses ABGR format (alpha in high bits)
        uint r = (uint)(Math.Clamp(color.X, 0f, 1f) * 255f + 0.5f);
        uint g = (uint)(Math.Clamp(color.Y, 0f, 1f) * 255f + 0.5f);
        uint b = (uint)(Math.Clamp(color.Z, 0f, 1f) * 255f + 0.5f);
        uint a = (uint)(Math.Clamp(color.W, 0f, 1f) * 255f + 0.5f);
        return (a << 24) | (b << 16) | (g << 8) | r;
    }
    
    /// <summary>
    /// Disposes the cached color resolver and unsubscribes from events.
    /// </summary>
    public void Dispose()
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _cachedRgbColors.Clear();
    }
}
