using System;
using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace caTTY.Display.Rendering;

/// <summary>
///     Key that uniquely identifies the visual state of the terminal content.
///     If this key changes, the cached rendered texture must be invalidated.
/// </summary>
public readonly struct TerminalRenderKey : IEquatable<TerminalRenderKey>
{
    // Content state
    public long ContentRevision { get; }
    public int ViewportOffset { get; }

    // Visual configuration
    public int ThemeVersion { get; }
    public float FontSize { get; }
    public float CharWidth { get; }
    public float LineHeight { get; }

    // Layout
    public int Cols { get; }
    public int Rows { get; }

    // Window position (to invalidate cache when window moves)
    public float WindowX { get; }
    public float WindowY { get; }

    // Explicit invalidation signal (e.g. from context lost)
    public int InvalidationSequence { get; }

    public TerminalRenderKey(
        long contentRevision,
        int viewportOffset,
        int themeVersion,
        float fontSize,
        float charWidth,
        float lineHeight,
        int cols,
        int rows,
        float windowX,
        float windowY,
        int invalidationSequence)
    {
        ContentRevision = contentRevision;
        ViewportOffset = viewportOffset;
        ThemeVersion = themeVersion;
        FontSize = fontSize;
        CharWidth = charWidth;
        LineHeight = lineHeight;
        Cols = cols;
        Rows = rows;
        WindowX = windowX;
        WindowY = windowY;
        InvalidationSequence = invalidationSequence;
    }

    public bool Equals(TerminalRenderKey other)
    {
        return ContentRevision == other.ContentRevision &&
               ViewportOffset == other.ViewportOffset &&
               ThemeVersion == other.ThemeVersion &&
               Math.Abs(FontSize - other.FontSize) < 0.001f &&
               Math.Abs(CharWidth - other.CharWidth) < 0.001f &&
               Math.Abs(LineHeight - other.LineHeight) < 0.001f &&
               Cols == other.Cols &&
               Rows == other.Rows &&
               Math.Abs(WindowX - other.WindowX) < 0.001f &&
               Math.Abs(WindowY - other.WindowY) < 0.001f &&
               InvalidationSequence == other.InvalidationSequence;
    }

    public override bool Equals(object? obj)
    {
        return obj is TerminalRenderKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(ContentRevision);
        hashCode.Add(ViewportOffset);
        hashCode.Add(ThemeVersion);
        hashCode.Add(FontSize);
        hashCode.Add(CharWidth);
        hashCode.Add(LineHeight);
        hashCode.Add(Cols);
        hashCode.Add(Rows);
        hashCode.Add(WindowX);
        hashCode.Add(WindowY);
        hashCode.Add(InvalidationSequence);
        return hashCode.ToHashCode();
    }

    public static bool operator ==(TerminalRenderKey left, TerminalRenderKey right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(TerminalRenderKey left, TerminalRenderKey right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
///     Manages caching of rendered terminal content to avoid re-drawing every frame.
/// </summary>
public class TerminalViewportRenderCache : IDisposable
{
    private TerminalRenderKey _lastKey;
    private bool _isValid;
    private int _invalidationSequence = 0;
    
    private readonly ITerminalBackingStore? _backingStore;

    public TerminalViewportRenderCache(ITerminalBackingStore? backingStore)
    {
        _backingStore = backingStore;
    }

    /// <summary>
    ///     Gets whether the current cache is valid for the given key.
    /// </summary>
    public bool IsValid(TerminalRenderKey currentKey)
    {
        if (_backingStore == null || !_backingStore.IsReady)
            return false;
            
        return _isValid && _lastKey.Equals(currentKey);
    }
    
    /// <summary>
    ///     Attempts to begin capturing content for the new key.
    ///     Should be called when IsValid returns false.
    /// </summary>
    public bool BeginCapture(TerminalRenderKey key)
    {
        if (_backingStore == null)
            return false;
            
        // Invalidate first to be safe
        Invalidate();
        
        // Try to start capture with the dimensions from the key
        // Convert columns/rows to pixels? 
        // No, the key stores cols/rows, but we need pixel size.
        // We should calculate pixel size from the key params.
        int width = (int)(key.Cols * key.CharWidth);
        int height = (int)(key.Rows * key.LineHeight);
        
        // Add padding if needed, but usually terminal fits exactly.
        
        if (_backingStore.BeginCapture(width, height))
        {
            _lastKey = key;
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    ///     Ends the capture and marks the cache as valid.
    /// </summary>
    public void EndCapture()
    {
        _backingStore?.EndCapture();
        _isValid = true;
    }
    
    /// <summary>
    ///     Draws the cached content.
    /// </summary>
    public void Draw(ImDrawListPtr drawList, float2 position)
    {
        if (_backingStore == null || !_isValid)
            return;
            
        // Calculate size again or store it?
        // Let's assume the key is still the same (checked via IsValid).
        float width = _lastKey.Cols * _lastKey.CharWidth;
        float height = _lastKey.Rows * _lastKey.LineHeight;
        
        _backingStore.Draw(drawList, position, new float2(width, height));
    }

    /// <summary>
    ///     Updates the cache with the new key, marking it as valid.
    ///     Call this after successfully rendering/caching the content.
    ///     DEPRECATED: Use BeginCapture/EndCapture pattern.
    /// </summary>
    public void Update(TerminalRenderKey key)
    {
        _lastKey = key;
        _isValid = true;
    }

    /// <summary>
    ///     Invalidates the cache, forcing a refresh on the next frame.
    ///     Also increments the invalidation sequence to ensure keys mismatch.
    /// </summary>
    public void Invalidate()
    {
        _isValid = false;
        _invalidationSequence++;
    }

    /// <summary>
    ///     Gets the current invalidation sequence number.
    /// </summary>
    public int InvalidationSequence => _invalidationSequence;

    public void Dispose()
    {
        Invalidate();
        _backingStore?.Dispose();
    }
    
    public ITerminalBackingStore? GetBackingStore() => _backingStore;
}
