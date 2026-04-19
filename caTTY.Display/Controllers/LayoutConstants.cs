namespace caTTY.Display.Controllers;

/// <summary>
///     Layout constants for the terminal window design.
///     Defines heights, spacing, and dimensions for the structured layout with constrained variable sizing.
/// </summary>
public static class LayoutConstants
{
    /// <summary>Height of the menu bar area in pixels (fixed)</summary>
    public const float MENU_BAR_HEIGHT = 25.0f;

    /// <summary>Fixed height of the tab area in pixels for single terminal</summary>
    public const float TAB_AREA_HEIGHT = 50.0f;

    /// <summary>Minimum height of the tab area in pixels</summary>
    public const float MIN_TAB_AREA_HEIGHT = 30.0f;

    /// <summary>Maximum height of the tab area in pixels</summary>
    public const float MAX_TAB_AREA_HEIGHT = 60.0f;

    /// <summary>Height per additional tab beyond the first</summary>
    public const float TAB_HEIGHT_PER_EXTRA_TAB = 0.0f; // Single row of tabs

    /// <summary>Minimum height of the settings area in pixels</summary>
    public const float MIN_SETTINGS_AREA_HEIGHT = 40.0f;

    /// <summary>Maximum height of the settings area in pixels</summary>
    public const float MAX_SETTINGS_AREA_HEIGHT = 80.0f;

    /// <summary>Height per additional settings control row</summary>
    public const float SETTINGS_HEIGHT_PER_CONTROL_ROW = 25.0f;

    /// <summary>Width of the add button in the tab area</summary>
    public const float ADD_BUTTON_WIDTH = 30.0f;

    /// <summary>General spacing between UI elements</summary>
    public const float ELEMENT_SPACING = 5.0f;

    /// <summary>Window padding for content areas</summary>
    public const float WINDOW_PADDING = 10.0f;

    /// <summary>Minimum window width for proper layout</summary>
    public const float MIN_WINDOW_WIDTH = 400.0f;

    /// <summary>Minimum window height for proper layout</summary>
    public const float MIN_WINDOW_HEIGHT = 200.0f;

    /// <summary>Maximum reasonable font size for validation</summary>
    public const float MAX_FONT_SIZE = 72.0f;

    /// <summary>Minimum reasonable font size for validation</summary>
    public const float MIN_FONT_SIZE = 4.0f;
}
