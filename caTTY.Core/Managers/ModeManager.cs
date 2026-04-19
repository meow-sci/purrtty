namespace caTTY.Core.Managers;

/// <summary>
///     Manages terminal mode state tracking including auto-wrap, cursor keys, and other terminal modes.
///     Handles both standard ANSI modes and DEC private modes.
/// </summary>
public class ModeManager : IModeManager
{
    private readonly Dictionary<int, bool> _modes;
    private readonly Dictionary<int, bool> _privateModes;
    private readonly Dictionary<int, bool> _savedModes;
    private readonly Dictionary<int, bool> _savedPrivateModes;

    /// <summary>
    ///     Creates a new mode manager with default mode states.
    /// </summary>
    public ModeManager()
    {
        _modes = new Dictionary<int, bool>();
        _privateModes = new Dictionary<int, bool>();
        _savedModes = new Dictionary<int, bool>();
        _savedPrivateModes = new Dictionary<int, bool>();
        
        Reset();
    }

    /// <summary>
    ///     Gets or sets auto-wrap mode. When true, cursor wraps to next line at right edge.
    /// </summary>
    public bool AutoWrapMode { get; set; } = true;

    /// <summary>
    ///     Gets or sets application cursor keys mode. When true, arrow keys send different escape sequences.
    /// </summary>
    public bool ApplicationCursorKeys { get; set; } = false;

    /// <summary>
    ///     Gets or sets bracketed paste mode. When true, paste content is wrapped with escape sequences.
    /// </summary>
    public bool BracketedPasteMode { get; set; } = false;

    /// <summary>
    ///     Gets or sets cursor visibility mode.
    /// </summary>
    public bool CursorVisible { get; set; } = true;

    /// <summary>
    ///     Gets or sets origin mode. When true, cursor positioning is relative to scroll region.
    /// </summary>
    public bool OriginMode { get; set; } = false;

    /// <summary>
    ///     Gets or sets UTF-8 mode.
    /// </summary>
    public bool Utf8Mode { get; set; } = true;

    /// <summary>
    ///     Gets or sets insert mode. When true, new characters are inserted, shifting existing characters right.
    ///     When false, new characters overwrite existing characters (default behavior).
    /// </summary>
    public bool InsertMode { get; set; } = false;

    /// <summary>
    ///     Sets a specific terminal mode by number.
    /// </summary>
    /// <param name="mode">Mode number</param>
    /// <param name="enabled">Whether the mode should be enabled</param>
    public void SetMode(int mode, bool enabled)
    {
        _modes[mode] = enabled;
        
        // Update specific mode properties for commonly used modes
        switch (mode)
        {
            case 4: // Insert/Replace mode (IRM)
                InsertMode = enabled;
                break;
            case 20: // Automatic Newline mode (LNM)
                // Will be implemented when line discipline is enhanced
                break;
        }
    }

    /// <summary>
    ///     Gets the state of a specific terminal mode by number.
    /// </summary>
    /// <param name="mode">Mode number</param>
    /// <returns>True if the mode is enabled, false otherwise</returns>
    public bool GetMode(int mode)
    {
        return _modes.TryGetValue(mode, out bool enabled) && enabled;
    }

    /// <summary>
    ///     Sets a private terminal mode by number (DEC modes).
    /// </summary>
    /// <param name="mode">Private mode number</param>
    /// <param name="enabled">Whether the mode should be enabled</param>
    public void SetPrivateMode(int mode, bool enabled)
    {
        _privateModes[mode] = enabled;
        
        // Update specific mode properties for commonly used private modes
        switch (mode)
        {
            case 1: // Application cursor keys (DECCKM)
                ApplicationCursorKeys = enabled;
                break;
            case 6: // Origin mode (DECOM)
                OriginMode = enabled;
                break;
            case 7: // Auto-wrap mode (DECAWM)
                SetAutoWrapMode(enabled);
                break;
            case 25: // Cursor visibility (DECTCEM)
                CursorVisible = enabled;
                break;
            case 47: // Alternate screen buffer
            case 1047: // Alternate screen buffer (xterm)
            case 1049: // Alternate screen buffer with cursor save/restore (xterm)
                // Will be handled by AlternateScreenManager when implemented
                break;
            case 2004: // Bracketed paste mode
                BracketedPasteMode = enabled;
                break;
            case 2027: // UTF-8 mode
                Utf8Mode = enabled;
                break;
        }
    }

    /// <summary>
    ///     Sets auto-wrap mode and clears wrap pending if disabled.
    ///     Matches TypeScript setAutoWrapMode behavior.
    /// </summary>
    /// <param name="enabled">Whether auto-wrap mode should be enabled</param>
    private void SetAutoWrapMode(bool enabled)
    {
        AutoWrapMode = enabled;
        // Note: Wrap pending clearing is handled by the terminal emulator
        // when it calls this through SetDecMode, which syncs with cursor manager
    }

    /// <summary>
    ///     Gets the state of a private terminal mode by number (DEC modes).
    /// </summary>
    /// <param name="mode">Private mode number</param>
    /// <returns>True if the private mode is enabled, false otherwise</returns>
    public bool GetPrivateMode(int mode)
    {
        return _privateModes.TryGetValue(mode, out bool enabled) && enabled;
    }

    /// <summary>
    ///     Saves the current state of all modes for later restoration.
    /// </summary>
    public void SaveModes()
    {
        _savedModes.Clear();
        foreach (var kvp in _modes)
        {
            _savedModes[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    ///     Saves the current state of specific modes for later restoration.
    /// </summary>
    /// <param name="modes">Array of mode numbers to save</param>
    public void SaveModes(int[] modes)
    {
        foreach (int mode in modes)
        {
            if (_modes.TryGetValue(mode, out bool value))
            {
                _savedModes[mode] = value;
            }
            else
            {
                _savedModes[mode] = false; // Default to disabled if not set
            }
        }
    }

    /// <summary>
    ///     Restores the previously saved mode states.
    /// </summary>
    public void RestoreModes()
    {
        foreach (var kvp in _savedModes)
        {
            SetMode(kvp.Key, kvp.Value);
        }
    }

    /// <summary>
    ///     Restores the previously saved state of specific modes.
    /// </summary>
    /// <param name="modes">Array of mode numbers to restore</param>
    public void RestoreModes(int[] modes)
    {
        foreach (int mode in modes)
        {
            if (_savedModes.TryGetValue(mode, out bool value))
            {
                SetMode(mode, value);
            }
        }
    }

    /// <summary>
    ///     Saves the current state of private modes for later restoration.
    /// </summary>
    public void SavePrivateModes()
    {
        _savedPrivateModes.Clear();
        foreach (var kvp in _privateModes)
        {
            _savedPrivateModes[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    ///     Saves the current state of specific private modes for later restoration.
    /// </summary>
    /// <param name="modes">Array of private mode numbers to save</param>
    public void SavePrivateModes(int[] modes)
    {
        foreach (int mode in modes)
        {
            if (_privateModes.TryGetValue(mode, out bool value))
            {
                _savedPrivateModes[mode] = value;
            }
            else
            {
                _savedPrivateModes[mode] = GetDefaultPrivateModeValue(mode);
            }
        }
    }

    /// <summary>
    ///     Restores the previously saved private mode states.
    /// </summary>
    public void RestorePrivateModes()
    {
        foreach (var kvp in _savedPrivateModes)
        {
            SetPrivateMode(kvp.Key, kvp.Value);
        }
    }

    /// <summary>
    ///     Restores the previously saved state of specific private modes.
    /// </summary>
    /// <param name="modes">Array of private mode numbers to restore</param>
    public void RestorePrivateModes(int[] modes)
    {
        foreach (int mode in modes)
        {
            if (_savedPrivateModes.TryGetValue(mode, out bool value))
            {
                SetPrivateMode(mode, value);
            }
        }
    }

    /// <summary>
    ///     Resets all modes to their default values.
    /// </summary>
    public void Reset()
    {
        _modes.Clear();
        _privateModes.Clear();
        _savedModes.Clear();
        _savedPrivateModes.Clear();
        
        // Reset mode properties to defaults
        AutoWrapMode = true;
        ApplicationCursorKeys = false;
        BracketedPasteMode = false;
        CursorVisible = true;
        OriginMode = false;
        Utf8Mode = true;
        InsertMode = false;
    }

    /// <summary>
    ///     Gets the default value for a private mode when not explicitly set.
    /// </summary>
    /// <param name="mode">Private mode number</param>
    /// <returns>Default value for the mode</returns>
    private bool GetDefaultPrivateModeValue(int mode)
    {
        return mode switch
        {
            1 => false,    // Application cursor keys - default off
            6 => false,    // Origin mode - default off  
            7 => true,     // Auto-wrap mode - default on
            25 => true,    // Cursor visibility - default on
            47 => false,   // Alternate screen - default off
            1047 => false, // Alternate screen with cursor save - default off
            1049 => false, // Alternate screen with cursor save and clear - default off
            2004 => false, // Bracketed paste mode - default off
            2027 => true,  // UTF-8 mode - default on
            _ => false     // Unknown modes default to off
        };
    }
}