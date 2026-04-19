namespace caTTY.Display.Controllers;

/// <summary>
///     Terminal-specific settings for future multi-terminal support.
///     Contains configuration options that apply to individual terminal instances.
///     Font configuration is handled separately by TerminalFontConfig.
/// </summary>
public class TerminalSettings
{
    /// <summary>Whether to show line numbers (future feature)</summary>
    public bool ShowLineNumbers { get; set; } = false;

    /// <summary>Whether to enable word wrap (future feature)</summary>
    public bool WordWrap { get; set; } = false;

    /// <summary>Terminal title for tab display</summary>
    public string Title { get; set; } = "Terminal 1";

    /// <summary>Whether this terminal instance is active</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Validates the terminal settings for consistency and reasonable values.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when settings contain invalid values</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            throw new ArgumentException("Title cannot be null or empty");
        }
    }

    /// <summary>
    /// Creates a copy of the current settings.
    /// </summary>
    /// <returns>A new TerminalSettings instance with the same values</returns>
    public TerminalSettings Clone()
    {
        return new TerminalSettings
        {
            ShowLineNumbers = ShowLineNumbers,
            WordWrap = WordWrap,
            Title = Title,
            IsActive = IsActive
        };
    }
}
