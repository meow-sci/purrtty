using caTTY.Core.Types;

namespace caTTY.Core.Managers;

/// <summary>
///     Manages mouse tracking modes and event filtering for terminal mouse input.
///     Handles mode transitions and determines which mouse events should be reported to applications.
/// </summary>
public class MouseTrackingManager : IMouseTrackingManager
{
    private MouseTrackingConfig _configuration;

    /// <summary>
    ///     Creates a new mouse tracking manager with default configuration.
    /// </summary>
    public MouseTrackingManager()
    {
        _configuration = MouseTrackingConfig.Default;
    }

    /// <summary>
    ///     Gets the current mouse tracking mode.
    /// </summary>
    public MouseTrackingMode CurrentMode => _configuration.Mode;

    /// <summary>
    ///     Gets whether SGR encoding (mode 1006) is enabled for mouse events.
    /// </summary>
    public bool SgrEncodingEnabled => _configuration.SgrEncodingEnabled;

    /// <summary>
    ///     Gets the current mouse tracking configuration.
    /// </summary>
    public MouseTrackingConfig Configuration => _configuration;

    /// <summary>
    ///     Sets the mouse tracking mode.
    /// </summary>
    /// <param name="mode">The mouse tracking mode to set</param>
    public void SetTrackingMode(MouseTrackingMode mode)
    {
        _configuration = _configuration.WithMode(mode);
    }

    /// <summary>
    ///     Sets multiple mouse tracking modes, using the highest numbered mode.
    ///     Implements requirement R1.5: when multiple tracking modes are set, use the highest numbered mode.
    /// </summary>
    /// <param name="modes">Array of mouse tracking modes</param>
    public void SetTrackingModes(MouseTrackingMode[] modes)
    {
        if (modes == null || modes.Length == 0)
        {
            SetTrackingMode(MouseTrackingMode.Off);
            return;
        }

        // Find the highest numbered mode (1003 > 1002 > 1000 > 0)
        MouseTrackingMode highestMode = MouseTrackingMode.Off;
        foreach (var mode in modes)
        {
            if ((int)mode > (int)highestMode)
            {
                highestMode = mode;
            }
        }

        SetTrackingMode(highestMode);
    }

    /// <summary>
    ///     Sets whether SGR encoding is enabled for mouse events.
    /// </summary>
    /// <param name="enabled">True to enable SGR encoding, false for standard X10/X11 encoding</param>
    public void SetSgrEncoding(bool enabled)
    {
        _configuration = _configuration.WithSgrEncoding(enabled);
    }

    /// <summary>
    ///     Updates the complete mouse tracking configuration.
    /// </summary>
    /// <param name="config">The new mouse tracking configuration</param>
    public void SetConfiguration(MouseTrackingConfig config)
    {
        _configuration = config;
    }

    /// <summary>
    ///     Determines whether a mouse event should be reported to the application based on the current tracking mode.
    /// </summary>
    /// <param name="eventType">The type of mouse event</param>
    /// <returns>True if the event should be reported, false if it should be handled locally</returns>
    public bool ShouldReportEvent(MouseEventType eventType)
    {
        return ShouldReportEvent(eventType, hasButtonPressed: false);
    }

    /// <summary>
    ///     Determines whether a mouse event should be reported based on the event type and button state.
    ///     Implements requirements R1.1, R1.2, R1.3: different modes report different event types.
    /// </summary>
    /// <param name="eventType">The type of mouse event</param>
    /// <param name="hasButtonPressed">Whether any mouse button is currently pressed</param>
    /// <returns>True if the event should be reported, false if it should be handled locally</returns>
    public bool ShouldReportEvent(MouseEventType eventType, bool hasButtonPressed)
    {
        // If mouse tracking is disabled, handle all events locally
        if (CurrentMode == MouseTrackingMode.Off)
        {
            return false;
        }

        return eventType switch
        {
            // Press and release events are reported in all tracking modes (1000+)
            MouseEventType.Press or MouseEventType.Release => true,

            // Wheel events are reported in all tracking modes (1000+)
            MouseEventType.Wheel => true,

            // Motion events depend on the mode and button state
            MouseEventType.Motion => CurrentMode switch
            {
                // Mode 1000 (Click): No motion events
                MouseTrackingMode.Click => false,

                // Mode 1002 (Button): Motion events only when button is pressed (drag)
                MouseTrackingMode.Button => hasButtonPressed,

                // Mode 1003 (Any): All motion events
                MouseTrackingMode.Any => true,

                // Default: No motion events
                _ => false
            },

            // Unknown event types are not reported
            _ => false
        };
    }

    /// <summary>
    ///     Resets mouse tracking to the default state (off).
    /// </summary>
    public void Reset()
    {
        _configuration = MouseTrackingConfig.Default;
    }
}