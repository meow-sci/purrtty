using caTTY.Core.Managers;
using caTTY.Core.Types;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
///     Property-based tests for mouse tracking mode management and event filtering.
///     These tests verify universal properties for mouse tracking behavior.
/// </summary>
[TestFixture]
[Category("Property")]
public class MouseTrackingProperties
{
    /// <summary>
    ///     Generator for valid mouse tracking modes.
    /// </summary>
    public static Arbitrary<MouseTrackingMode> MouseTrackingModeArb =>
        Arb.From(Gen.Elements(
            MouseTrackingMode.Off,
            MouseTrackingMode.Click,
            MouseTrackingMode.Button,
            MouseTrackingMode.Any));

    /// <summary>
    ///     Generator for arrays of mouse tracking modes (for testing mode resolution).
    /// </summary>
    public static Arbitrary<MouseTrackingMode[]> MouseTrackingModeArrayArb =>
        Arb.From(Gen.ArrayOf(Gen.Elements(
            MouseTrackingMode.Off,
            MouseTrackingMode.Click,
            MouseTrackingMode.Button,
            MouseTrackingMode.Any)));

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
    ///     **Feature: mouse-input-support, Property 2: Mouse Tracking Mode Resolution**
    ///     **Validates: Requirements R1.5**
    ///     Property: For any array of mouse tracking modes, the manager should use the
    ///     highest numbered mode (1003 > 1002 > 1000 > 0).
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MouseTrackingModeResolutionUsesHighestMode()
    {
        return Prop.ForAll(MouseTrackingModeArrayArb, modes =>
        {
            // Arrange
            var manager = new MouseTrackingManager();

            // Act - Set multiple modes
            manager.SetTrackingModes(modes);

            // Assert - Should use the highest numbered mode
            var expectedMode = MouseTrackingMode.Off;
            if (modes != null && modes.Length > 0)
            {
                foreach (var mode in modes)
                {
                    if ((int)mode > (int)expectedMode)
                    {
                        expectedMode = mode;
                    }
                }
            }

            bool correctModeSelected = manager.CurrentMode == expectedMode;
            bool configurationUpdated = manager.Configuration.Mode == expectedMode;

            return correctModeSelected && configurationUpdated;
        });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 2b: Single Mode Selection**
    ///     **Validates: Requirements R1.5**
    ///     Property: When a single mode is provided, that mode should be selected.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property SingleModeSelectionIsCorrect()
    {
        return Prop.ForAll(MouseTrackingModeArb, mode =>
        {
            // Arrange
            var manager = new MouseTrackingManager();

            // Act - Set single mode
            manager.SetTrackingModes(new[] { mode });

            // Assert - Should select that mode
            return manager.CurrentMode == mode;
        });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 1: Mouse Tracking Mode Event Reporting**
    ///     **Validates: Requirements R1.1, R1.2, R1.3**
    ///     Property: For any mouse event and tracking mode configuration, the terminal should
    ///     report events to the application only when the event type is supported by the current
    ///     tracking mode (click events in mode 1000+, drag events in mode 1002+, motion events in mode 1003 only).
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MouseTrackingModeEventReportingIsCorrect()
    {
        return Prop.ForAll(MouseTrackingModeArb, MouseEventTypeArb, Arb.Default.Bool(),
            (mode, eventType, hasButtonPressed) =>
        {
            // Arrange
            var manager = new MouseTrackingManager();
            manager.SetTrackingMode(mode);

            // Act
            bool shouldReport = manager.ShouldReportEvent(eventType, hasButtonPressed);

            // Assert - Event reporting follows mode rules
            bool expectedResult = mode switch
            {
                MouseTrackingMode.Off => false, // No events reported when off

                MouseTrackingMode.Click => eventType switch
                {
                    MouseEventType.Press => true,    // Press events in mode 1000+
                    MouseEventType.Release => true,  // Release events in mode 1000+
                    MouseEventType.Wheel => true,    // Wheel events in mode 1000+
                    MouseEventType.Motion => false,  // No motion events in mode 1000
                    _ => false
                },

                MouseTrackingMode.Button => eventType switch
                {
                    MouseEventType.Press => true,    // Press events in mode 1002+
                    MouseEventType.Release => true,  // Release events in mode 1002+
                    MouseEventType.Wheel => true,    // Wheel events in mode 1002+
                    MouseEventType.Motion => hasButtonPressed, // Motion only when button pressed in mode 1002
                    _ => false
                },

                MouseTrackingMode.Any => eventType switch
                {
                    MouseEventType.Press => true,    // Press events in mode 1003
                    MouseEventType.Release => true,  // Release events in mode 1003
                    MouseEventType.Wheel => true,    // Wheel events in mode 1003
                    MouseEventType.Motion => true,   // All motion events in mode 1003
                    _ => false
                },

                _ => false
            };

            return shouldReport == expectedResult;
        });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 1b: Click Mode Event Filtering**
    ///     **Validates: Requirements R1.1**
    ///     Property: In click mode (1000), only press, release, and wheel events should be reported.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ClickModeEventFilteringIsCorrect()
    {
        return Prop.ForAll(MouseEventTypeArb, Arb.Default.Bool(), (eventType, hasButtonPressed) =>
        {
            // Arrange
            var manager = new MouseTrackingManager();
            manager.SetTrackingMode(MouseTrackingMode.Click);

            // Act
            bool shouldReport = manager.ShouldReportEvent(eventType, hasButtonPressed);

            // Assert - Click mode rules
            bool expectedResult = eventType is MouseEventType.Press or MouseEventType.Release or MouseEventType.Wheel;
            return shouldReport == expectedResult;
        });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 1c: Button Mode Event Filtering**
    ///     **Validates: Requirements R1.2**
    ///     Property: In button mode (1002), press, release, wheel, and drag events should be reported.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ButtonModeEventFilteringIsCorrect()
    {
        return Prop.ForAll(MouseEventTypeArb, Arb.Default.Bool(), (eventType, hasButtonPressed) =>
        {
            // Arrange
            var manager = new MouseTrackingManager();
            manager.SetTrackingMode(MouseTrackingMode.Button);

            // Act
            bool shouldReport = manager.ShouldReportEvent(eventType, hasButtonPressed);

            // Assert - Button mode rules
            bool expectedResult = eventType switch
            {
                MouseEventType.Press => true,
                MouseEventType.Release => true,
                MouseEventType.Wheel => true,
                MouseEventType.Motion => hasButtonPressed, // Only when dragging
                _ => false
            };

            return shouldReport == expectedResult;
        });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 1d: Any Mode Event Filtering**
    ///     **Validates: Requirements R1.3**
    ///     Property: In any mode (1003), all mouse events should be reported.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property AnyModeEventFilteringIsCorrect()
    {
        return Prop.ForAll(MouseEventTypeArb, Arb.Default.Bool(), (eventType, hasButtonPressed) =>
        {
            // Arrange
            var manager = new MouseTrackingManager();
            manager.SetTrackingMode(MouseTrackingMode.Any);

            // Act
            bool shouldReport = manager.ShouldReportEvent(eventType, hasButtonPressed);

            // Assert - Any mode reports all events
            bool expectedResult = eventType is MouseEventType.Press or MouseEventType.Release
                                  or MouseEventType.Motion or MouseEventType.Wheel;
            return shouldReport == expectedResult;
        });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 1e: Off Mode Event Filtering**
    ///     **Validates: Requirements R1.1, R1.2, R1.3**
    ///     Property: When mouse tracking is off, no events should be reported to the application.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OffModeEventFilteringIsCorrect()
    {
        return Prop.ForAll(MouseEventTypeArb, Arb.Default.Bool(), (eventType, hasButtonPressed) =>
        {
            // Arrange
            var manager = new MouseTrackingManager();
            manager.SetTrackingMode(MouseTrackingMode.Off);

            // Act
            bool shouldReport = manager.ShouldReportEvent(eventType, hasButtonPressed);

            // Assert - Off mode reports no events
            return shouldReport == false;
        });
    }
}
