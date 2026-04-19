using caTTY.Core.Types;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
///     Property-based tests for mouse event structure and validation.
///     These tests verify universal properties that should hold for all valid mouse events.
/// </summary>
[TestFixture]
[Category("Property")]
public class MouseEventProperties
{
    /// <summary>
    ///     Generator for valid mouse event types.
    /// </summary>
    public static Arbitrary<MouseEventType> MouseEventTypeArb =>
        Arb.From(Gen.Elements(
            MouseEventType.Press,
            MouseEventType.Release,
            MouseEventType.Motion,
            MouseEventType.Wheel));

    /// <summary>
    ///     Generator for valid mouse buttons.
    /// </summary>
    public static Arbitrary<MouseButton> MouseButtonArb =>
        Arb.From(Gen.Elements(
            MouseButton.Left,
            MouseButton.Middle,
            MouseButton.Right,
            MouseButton.WheelUp,
            MouseButton.WheelDown));

    /// <summary>
    ///     Generator for valid 1-based terminal coordinates (1-1000).
    /// </summary>
    public static Arbitrary<int> TerminalCoordinateArb =>
        Arb.From(Gen.Choose(1, 1000));

    /// <summary>
    ///     Generator for valid mouse key modifiers.
    /// </summary>
    public static Arbitrary<MouseKeyModifiers> MouseKeyModifiersArb =>
        Arb.From(Gen.Elements(
            MouseKeyModifiers.None,
            MouseKeyModifiers.Shift,
            MouseKeyModifiers.Alt,
            MouseKeyModifiers.Ctrl,
            MouseKeyModifiers.Shift | MouseKeyModifiers.Alt,
            MouseKeyModifiers.Shift | MouseKeyModifiers.Ctrl,
            MouseKeyModifiers.Alt | MouseKeyModifiers.Ctrl,
            MouseKeyModifiers.Shift | MouseKeyModifiers.Alt | MouseKeyModifiers.Ctrl));

    /// <summary>
    ///     Generator for valid DateTime timestamps.
    /// </summary>
    public static Arbitrary<DateTime> TimestampArb =>
        Arb.From(Gen.Choose(-365, 365)
            .Select(days => DateTime.UtcNow.AddDays(days)));

    /// <summary>
    ///     **Feature: mouse-input-support, Property 1: Mouse Event Structure Integrity**
    ///     **Validates: Requirements R4.1, R4.2, R4.3**
    ///     Property: For any valid mouse event parameters, creating a MouseEvent should
    ///     preserve all input values and maintain structural integrity.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MouseEventPreservesInputValues()
    {
        return Prop.ForAll(MouseEventTypeArb, MouseButtonArb, TerminalCoordinateArb,
            (eventType, button, x1) =>
            {
                var y1 = x1 + 1; // Simple derived coordinate
                var modifiers = MouseKeyModifiers.Shift | MouseKeyModifiers.Ctrl;
                var timestamp = DateTime.UtcNow;

                // Act - Create mouse event
                var mouseEvent = new MouseEvent(eventType, button, x1, y1, modifiers, timestamp);

                // Assert - All properties should match input values
                bool typeMatches = mouseEvent.Type == eventType;
                bool buttonMatches = mouseEvent.Button == button;
                bool coordinatesMatch = mouseEvent.X1 == x1 && mouseEvent.Y1 == y1;
                bool modifiersMatch = mouseEvent.Modifiers == modifiers;
                bool timestampMatches = mouseEvent.Timestamp == timestamp;

                return typeMatches && buttonMatches && coordinatesMatch &&
                       modifiersMatch && timestampMatches;
            });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 1b: Mouse Event Equality Consistency**
    ///     **Validates: Requirements R4.1, R4.2, R4.3**
    ///     Property: For any mouse event, creating two identical events should be equal,
    ///     and different events should not be equal.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MouseEventEqualityIsConsistent()
    {
        return Prop.ForAll(MouseEventTypeArb, MouseButtonArb, TerminalCoordinateArb,
            (eventType, button, x1) =>
            {
                var y1 = x1 + 1;
                var modifiers = MouseKeyModifiers.Alt;
                var timestamp = DateTime.UtcNow;

                // Arrange - Create two identical events
                var event1 = new MouseEvent(eventType, button, x1, y1, modifiers, timestamp);
                var event2 = new MouseEvent(eventType, button, x1, y1, modifiers, timestamp);

                // Create a different event (different button)
                var differentButton = button == MouseButton.Left ? MouseButton.Right : MouseButton.Left;
                var differentEvent = new MouseEvent(eventType, differentButton, x1, y1, modifiers, timestamp);

                // Assert - Equality behavior
                bool identicalEventsEqual = event1.Equals(event2);
                bool identicalEventsEqualOperator = event1 == event2;
                bool differentEventsNotEqual = !event1.Equals(differentEvent);
                bool differentEventsNotEqualOperator = event1 != differentEvent;
                bool hashCodesMatch = event1.GetHashCode() == event2.GetHashCode();

                return identicalEventsEqual && identicalEventsEqualOperator &&
                       differentEventsNotEqual && differentEventsNotEqualOperator && hashCodesMatch;
            });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 1c: Mouse Button Identification Correctness**
    ///     **Validates: Requirements R4.1, R4.2, R4.3**
    ///     Property: For any mouse button value, the button should be correctly identified
    ///     and match expected xterm protocol values.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MouseButtonIdentificationIsCorrect()
    {
        return Prop.ForAll(MouseButtonArb, button =>
        {
            // Act - Create event with the button
            var mouseEvent = new MouseEvent(MouseEventType.Press, button, 1, 1);

            // Assert - Button values match xterm protocol
            bool buttonValueCorrect = button switch
            {
                MouseButton.Left => (int)mouseEvent.Button == 0,
                MouseButton.Middle => (int)mouseEvent.Button == 1,
                MouseButton.Right => (int)mouseEvent.Button == 2,
                MouseButton.WheelUp => (int)mouseEvent.Button == 64,
                MouseButton.WheelDown => (int)mouseEvent.Button == 65,
                _ => false
            };

            bool buttonPreserved = mouseEvent.Button == button;

            return buttonValueCorrect && buttonPreserved;
        });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 1d: Mouse Coordinate Validation**
    ///     **Validates: Requirements R4.1, R4.2, R4.3**
    ///     Property: For any valid 1-based coordinates, the mouse event should preserve
    ///     the coordinate values exactly as provided.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MouseCoordinatesArePreserved()
    {
        return Prop.ForAll(TerminalCoordinateArb, TerminalCoordinateArb, (x1, y1) =>
        {
            // Act - Create event with coordinates
            var mouseEvent = new MouseEvent(MouseEventType.Press, MouseButton.Left, x1, y1);

            // Assert - Coordinates are preserved exactly
            bool coordinatesPreserved = mouseEvent.X1 == x1 && mouseEvent.Y1 == y1;
            bool coordinatesAreOneBased = mouseEvent.X1 >= 1 && mouseEvent.Y1 >= 1;

            return coordinatesPreserved && coordinatesAreOneBased;
        });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 1e: Mouse Modifier Encoding Correctness**
    ///     **Validates: Requirements R4.1, R4.2, R4.3**
    ///     Property: For any modifier key combination, the modifiers should be preserved
    ///     and match expected xterm protocol bit values.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MouseModifierEncodingIsCorrect()
    {
        return Prop.ForAll(MouseKeyModifiersArb, modifiers =>
        {
            // Act - Create event with modifiers
            var mouseEvent = new MouseEvent(MouseEventType.Press, MouseButton.Left, 1, 1, modifiers);

            // Assert - Modifiers are preserved and have correct bit values
            bool modifiersPreserved = mouseEvent.Modifiers == modifiers;

            // Check individual modifier bit values match xterm protocol
            bool shiftBitCorrect = (modifiers & MouseKeyModifiers.Shift) == 0 ||
                                   ((int)modifiers & 4) == 4;
            bool altBitCorrect = (modifiers & MouseKeyModifiers.Alt) == 0 ||
                                 ((int)modifiers & 8) == 8;
            bool ctrlBitCorrect = (modifiers & MouseKeyModifiers.Ctrl) == 0 ||
                                  ((int)modifiers & 16) == 16;

            return modifiersPreserved && shiftBitCorrect && altBitCorrect && ctrlBitCorrect;
        });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 1f: Mouse Event Timestamp Handling**
    ///     **Validates: Requirements R4.1, R4.2, R4.3**
    ///     Property: For any mouse event, the timestamp should be preserved when provided,
    ///     or set to current time when not provided.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MouseEventTimestampHandlingIsCorrect()
    {
        return Prop.ForAll(TimestampArb, providedTimestamp =>
        {
            // Act - Create event with explicit timestamp
            var eventWithTimestamp = new MouseEvent(
                MouseEventType.Press, MouseButton.Left, 1, 1,
                MouseKeyModifiers.None, providedTimestamp);

            // Create event without explicit timestamp (should use current time)
            var beforeCreation = DateTime.UtcNow;
            var eventWithoutTimestamp = new MouseEvent(
                MouseEventType.Press, MouseButton.Left, 1, 1);
            var afterCreation = DateTime.UtcNow;

            // Assert - Timestamp behavior
            bool explicitTimestampPreserved = eventWithTimestamp.Timestamp == providedTimestamp;
            bool implicitTimestampReasonable =
                eventWithoutTimestamp.Timestamp >= beforeCreation &&
                eventWithoutTimestamp.Timestamp <= afterCreation;

            return explicitTimestampPreserved && implicitTimestampReasonable;
        });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 1g: Mouse Event String Representation**
    ///     **Validates: Requirements R4.1, R4.2, R4.3**
    ///     Property: For any mouse event, the string representation should contain
    ///     all essential information in a readable format.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MouseEventStringRepresentationIsComplete()
    {
        return Prop.ForAll(MouseEventTypeArb, MouseButtonArb, TerminalCoordinateArb,
            (eventType, button, x1) =>
            {
                var y1 = x1 + 1;
                var modifiers = MouseKeyModifiers.Shift | MouseKeyModifiers.Alt;

                // Act - Create event and get string representation
                var mouseEvent = new MouseEvent(eventType, button, x1, y1, modifiers);
                var stringRep = mouseEvent.ToString();

                // Assert - String contains essential information
                bool containsEventType = stringRep.Contains(eventType.ToString());
                bool containsButton = stringRep.Contains(button.ToString());
                bool containsCoordinates = stringRep.Contains($"({x1},{y1})");
                bool containsModifiersWhenPresent = modifiers == MouseKeyModifiers.None ||
                                                    stringRep.Contains(modifiers.ToString());

                return containsEventType && containsButton && containsCoordinates &&
                       containsModifiersWhenPresent;
            });
    }
}
