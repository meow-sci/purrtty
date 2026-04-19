using System;

namespace caTTY.Core.Types;

/// <summary>
///     Configuration for mouse tracking behavior.
///     Defines how mouse events are processed and reported.
/// </summary>
public readonly struct MouseTrackingConfig : IEquatable<MouseTrackingConfig>
{
    /// <summary>
    ///     The current mouse tracking mode.
    /// </summary>
    public MouseTrackingMode Mode { get; }

    /// <summary>
    ///     Whether SGR encoding (mode 1006) is enabled for mouse events.
    /// </summary>
    public bool SgrEncodingEnabled { get; }

    /// <summary>
    ///     Whether selection has priority over mouse tracking when shift is held.
    /// </summary>
    public bool SelectionPriority { get; }

    /// <summary>
    ///     Creates a new mouse tracking configuration.
    /// </summary>
    /// <param name="mode">The mouse tracking mode</param>
    /// <param name="sgrEncodingEnabled">Whether SGR encoding is enabled</param>
    /// <param name="selectionPriority">Whether selection has priority with shift key</param>
    public MouseTrackingConfig(
        MouseTrackingMode mode = MouseTrackingMode.Off,
        bool sgrEncodingEnabled = false,
        bool selectionPriority = true)
    {
        Mode = mode;
        SgrEncodingEnabled = sgrEncodingEnabled;
        SelectionPriority = selectionPriority;
    }

    /// <summary>
    ///     Gets the default mouse tracking configuration.
    /// </summary>
    public static MouseTrackingConfig Default => new(
        MouseTrackingMode.Off,
        sgrEncodingEnabled: false,
        selectionPriority: true);

    /// <summary>
    ///     Creates a new configuration with the specified mode.
    /// </summary>
    /// <param name="mode">The new mouse tracking mode</param>
    /// <returns>A new configuration with the updated mode</returns>
    public MouseTrackingConfig WithMode(MouseTrackingMode mode)
    {
        return new MouseTrackingConfig(mode, SgrEncodingEnabled, SelectionPriority);
    }

    /// <summary>
    ///     Creates a new configuration with the specified SGR encoding setting.
    /// </summary>
    /// <param name="enabled">Whether SGR encoding should be enabled</param>
    /// <returns>A new configuration with the updated SGR encoding setting</returns>
    public MouseTrackingConfig WithSgrEncoding(bool enabled)
    {
        return new MouseTrackingConfig(Mode, enabled, SelectionPriority);
    }

    /// <summary>
    ///     Creates a new configuration with the specified selection priority setting.
    /// </summary>
    /// <param name="enabled">Whether selection should have priority</param>
    /// <returns>A new configuration with the updated selection priority setting</returns>
    public MouseTrackingConfig WithSelectionPriority(bool enabled)
    {
        return new MouseTrackingConfig(Mode, SgrEncodingEnabled, enabled);
    }

    /// <summary>
    ///     Determines whether the specified MouseTrackingConfig is equal to the current MouseTrackingConfig.
    /// </summary>
    public bool Equals(MouseTrackingConfig other)
    {
        return Mode == other.Mode &&
               SgrEncodingEnabled == other.SgrEncodingEnabled &&
               SelectionPriority == other.SelectionPriority;
    }

    /// <summary>
    ///     Determines whether the specified object is equal to the current MouseTrackingConfig.
    /// </summary>
    public override bool Equals(object? obj)
    {
        return obj is MouseTrackingConfig other && Equals(other);
    }

    /// <summary>
    ///     Returns the hash code for this MouseTrackingConfig.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(Mode, SgrEncodingEnabled, SelectionPriority);
    }

    /// <summary>
    ///     Determines whether two MouseTrackingConfig instances are equal.
    /// </summary>
    public static bool operator ==(MouseTrackingConfig left, MouseTrackingConfig right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two MouseTrackingConfig instances are not equal.
    /// </summary>
    public static bool operator !=(MouseTrackingConfig left, MouseTrackingConfig right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    ///     Returns a string representation of the MouseTrackingConfig.
    /// </summary>
    public override string ToString()
    {
        var encoding = SgrEncodingEnabled ? "SGR" : "X10";
        var priority = SelectionPriority ? " +SelectionPriority" : "";
        return $"MouseTracking({Mode}, {encoding}{priority})";
    }
}