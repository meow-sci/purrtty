using caTTY.Core.Types;

namespace caTTY.Core.Managers;

/// <summary>
///     Interface for managing mouse tracking modes and event filtering.
///     Handles mouse tracking mode transitions and determines which events should be reported.
/// </summary>
public interface IMouseTrackingManager
{
    /// <summary>
    ///     Gets the current mouse tracking mode.
    /// </summary>
    MouseTrackingMode CurrentMode { get; }

    /// <summary>
    ///     Gets whether SGR encoding (mode 1006) is enabled for mouse events.
    /// </summary>
    bool SgrEncodingEnabled { get; }

    /// <summary>
    ///     Gets the current mouse tracking configuration.
    /// </summary>
    MouseTrackingConfig Configuration { get; }

    /// <summary>
    ///     Sets the mouse tracking mode.
    /// </summary>
    /// <param name="mode">The mouse tracking mode to set</param>
    void SetTrackingMode(MouseTrackingMode mode);

    /// <summary>
    ///     Sets multiple mouse tracking modes, using the highest numbered mode.
    /// </summary>
    /// <param name="modes">Array of mouse tracking modes</param>
    void SetTrackingModes(MouseTrackingMode[] modes);

    /// <summary>
    ///     Sets whether SGR encoding is enabled for mouse events.
    /// </summary>
    /// <param name="enabled">True to enable SGR encoding, false for standard X10/X11 encoding</param>
    void SetSgrEncoding(bool enabled);

    /// <summary>
    ///     Updates the complete mouse tracking configuration.
    /// </summary>
    /// <param name="config">The new mouse tracking configuration</param>
    void SetConfiguration(MouseTrackingConfig config);

    /// <summary>
    ///     Determines whether a mouse event should be reported to the application based on the current tracking mode.
    /// </summary>
    /// <param name="eventType">The type of mouse event</param>
    /// <returns>True if the event should be reported, false if it should be handled locally</returns>
    bool ShouldReportEvent(MouseEventType eventType);

    /// <summary>
    ///     Determines whether a mouse event should be reported based on the event type and button state.
    /// </summary>
    /// <param name="eventType">The type of mouse event</param>
    /// <param name="hasButtonPressed">Whether any mouse button is currently pressed</param>
    /// <returns>True if the event should be reported, false if it should be handled locally</returns>
    bool ShouldReportEvent(MouseEventType eventType, bool hasButtonPressed);

    /// <summary>
    ///     Resets mouse tracking to the default state (off).
    /// </summary>
    void Reset();
}