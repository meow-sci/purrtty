using System;

namespace caTTY.Core.Types;

/// <summary>
///     Represents a mouse event with coordinates and modifiers.
///     Used for mouse tracking and input processing.
/// </summary>
public readonly struct MouseEvent : IEquatable<MouseEvent>
{
    /// <summary>
    ///     The type of mouse event (press, release, motion, wheel).
    /// </summary>
    public MouseEventType Type { get; }

    /// <summary>
    ///     The mouse button involved in the event.
    /// </summary>
    public MouseButton Button { get; }

    /// <summary>
    ///     X coordinate in 1-based terminal coordinates.
    /// </summary>
    public int X1 { get; }

    /// <summary>
    ///     Y coordinate in 1-based terminal coordinates.
    /// </summary>
    public int Y1 { get; }

    /// <summary>
    ///     Modifier keys held during the event.
    /// </summary>
    public MouseKeyModifiers Modifiers { get; }

    /// <summary>
    ///     Timestamp when the event occurred.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    ///     Creates a new mouse event.
    /// </summary>
    /// <param name="type">The type of mouse event</param>
    /// <param name="button">The mouse button</param>
    /// <param name="x1">X coordinate (1-based)</param>
    /// <param name="y1">Y coordinate (1-based)</param>
    /// <param name="modifiers">Modifier keys</param>
    /// <param name="timestamp">Event timestamp</param>
    public MouseEvent(
        MouseEventType type,
        MouseButton button,
        int x1,
        int y1,
        MouseKeyModifiers modifiers = MouseKeyModifiers.None,
        DateTime timestamp = default)
    {
        Type = type;
        Button = button;
        X1 = x1;
        Y1 = y1;
        Modifiers = modifiers;
        Timestamp = timestamp == default ? DateTime.UtcNow : timestamp;
    }

    /// <summary>
    ///     Determines whether the specified MouseEvent is equal to the current MouseEvent.
    /// </summary>
    public bool Equals(MouseEvent other)
    {
        return Type == other.Type &&
               Button == other.Button &&
               X1 == other.X1 &&
               Y1 == other.Y1 &&
               Modifiers == other.Modifiers &&
               Timestamp == other.Timestamp;
    }

    /// <summary>
    ///     Determines whether the specified object is equal to the current MouseEvent.
    /// </summary>
    public override bool Equals(object? obj)
    {
        return obj is MouseEvent other && Equals(other);
    }

    /// <summary>
    ///     Returns the hash code for this MouseEvent.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(Type, Button, X1, Y1, Modifiers, Timestamp);
    }

    /// <summary>
    ///     Determines whether two MouseEvent instances are equal.
    /// </summary>
    public static bool operator ==(MouseEvent left, MouseEvent right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two MouseEvent instances are not equal.
    /// </summary>
    public static bool operator !=(MouseEvent left, MouseEvent right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    ///     Returns a string representation of the MouseEvent.
    /// </summary>
    public override string ToString()
    {
        var modStr = Modifiers != MouseKeyModifiers.None ? $" +{Modifiers}" : "";
        return $"{Type}({Button}) at ({X1},{Y1}){modStr}";
    }
}

/// <summary>
///     Specifies the type of mouse event.
/// </summary>
public enum MouseEventType
{
    /// <summary>
    ///     Mouse button press event.
    /// </summary>
    Press,

    /// <summary>
    ///     Mouse button release event.
    /// </summary>
    Release,

    /// <summary>
    ///     Mouse motion event (with or without button pressed).
    /// </summary>
    Motion,

    /// <summary>
    ///     Mouse wheel scroll event.
    /// </summary>
    Wheel
}

/// <summary>
///     Specifies mouse buttons and wheel directions.
///     Values match xterm mouse protocol encoding.
/// </summary>
public enum MouseButton
{
    /// <summary>
    ///     Left mouse button (button 0).
    /// </summary>
    Left = 0,

    /// <summary>
    ///     Middle mouse button (button 1).
    /// </summary>
    Middle = 1,

    /// <summary>
    ///     Right mouse button (button 2).
    /// </summary>
    Right = 2,

    /// <summary>
    ///     Mouse wheel up (button 64 in xterm protocol).
    /// </summary>
    WheelUp = 64,

    /// <summary>
    ///     Mouse wheel down (button 65 in xterm protocol).
    /// </summary>
    WheelDown = 65
}

/// <summary>
///     Modifier keys that can be held during mouse events.
///     Values match xterm mouse protocol encoding.
/// </summary>
[Flags]
public enum MouseKeyModifiers
{
    /// <summary>
    ///     No modifier keys.
    /// </summary>
    None = 0,

    /// <summary>
    ///     Shift key modifier (bit 2).
    /// </summary>
    Shift = 4,

    /// <summary>
    ///     Alt key modifier (bit 3).
    /// </summary>
    Alt = 8,

    /// <summary>
    ///     Ctrl key modifier (bit 4).
    /// </summary>
    Ctrl = 16
}