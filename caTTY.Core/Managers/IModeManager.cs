namespace caTTY.Core.Managers;

/// <summary>
///     Interface for managing terminal mode state tracking including auto-wrap, cursor keys, and other modes.
/// </summary>
public interface IModeManager
{
    /// <summary>
    ///     Gets or sets auto-wrap mode. When true, cursor wraps to next line at right edge.
    /// </summary>
    bool AutoWrapMode { get; set; }

    /// <summary>
    ///     Gets or sets application cursor keys mode. When true, arrow keys send different escape sequences.
    /// </summary>
    bool ApplicationCursorKeys { get; set; }

    /// <summary>
    ///     Gets or sets bracketed paste mode. When true, paste content is wrapped with escape sequences.
    /// </summary>
    bool BracketedPasteMode { get; set; }

    /// <summary>
    ///     Gets or sets cursor visibility mode.
    /// </summary>
    bool CursorVisible { get; set; }

    /// <summary>
    ///     Gets or sets origin mode. When true, cursor positioning is relative to scroll region.
    /// </summary>
    bool OriginMode { get; set; }

    /// <summary>
    ///     Gets or sets UTF-8 mode.
    /// </summary>
    bool Utf8Mode { get; set; }

    /// <summary>
    ///     Gets or sets insert mode. When true, new characters are inserted, shifting existing characters right.
    ///     When false, new characters overwrite existing characters (default behavior).
    /// </summary>
    bool InsertMode { get; set; }

    /// <summary>
    ///     Sets a specific terminal mode by number.
    /// </summary>
    /// <param name="mode">Mode number</param>
    /// <param name="enabled">Whether the mode should be enabled</param>
    void SetMode(int mode, bool enabled);

    /// <summary>
    ///     Gets the state of a specific terminal mode by number.
    /// </summary>
    /// <param name="mode">Mode number</param>
    /// <returns>True if the mode is enabled, false otherwise</returns>
    bool GetMode(int mode);

    /// <summary>
    ///     Sets a private terminal mode by number (DEC modes).
    /// </summary>
    /// <param name="mode">Private mode number</param>
    /// <param name="enabled">Whether the mode should be enabled</param>
    void SetPrivateMode(int mode, bool enabled);

    /// <summary>
    ///     Gets the state of a private terminal mode by number (DEC modes).
    /// </summary>
    /// <param name="mode">Private mode number</param>
    /// <returns>True if the private mode is enabled, false otherwise</returns>
    bool GetPrivateMode(int mode);

    /// <summary>
    ///     Saves the current state of all modes for later restoration.
    /// </summary>
    void SaveModes();

    /// <summary>
    ///     Saves the current state of specific modes for later restoration.
    /// </summary>
    /// <param name="modes">Array of mode numbers to save</param>
    void SaveModes(int[] modes);

    /// <summary>
    ///     Restores the previously saved mode states.
    /// </summary>
    void RestoreModes();

    /// <summary>
    ///     Restores the previously saved state of specific modes.
    /// </summary>
    /// <param name="modes">Array of mode numbers to restore</param>
    void RestoreModes(int[] modes);

    /// <summary>
    ///     Saves the current state of private modes for later restoration.
    /// </summary>
    void SavePrivateModes();

    /// <summary>
    ///     Saves the current state of specific private modes for later restoration.
    /// </summary>
    /// <param name="modes">Array of private mode numbers to save</param>
    void SavePrivateModes(int[] modes);

    /// <summary>
    ///     Restores the previously saved private mode states.
    /// </summary>
    void RestorePrivateModes();

    /// <summary>
    ///     Restores the previously saved state of specific private modes.
    /// </summary>
    /// <param name="modes">Array of private mode numbers to restore</param>
    void RestorePrivateModes(int[] modes);

    /// <summary>
    ///     Resets all modes to their default values.
    /// </summary>
    void Reset();
}