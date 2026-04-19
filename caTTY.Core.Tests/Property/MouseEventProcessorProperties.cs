using caTTY.Core.Input;
using caTTY.Core.Managers;
using caTTY.Core.Types;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
///     Property-based tests for mouse event processing and routing logic.
///     These tests verify universal properties for event routing between application and local handlers.
/// </summary>
[TestFixture]
[Category("Property")]
public class MouseEventProcessorProperties
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
    ///     Generator for valid 1-based terminal coordinates (1-100).
    /// </summary>
    public static Arbitrary<int> TerminalCoordinateArb =>
        Arb.From(Gen.Choose(1, 100));

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
    ///     Generator for mouse tracking modes.
    /// </summary>
    public static Arbitrary<MouseTrackingMode> MouseTrackingModeArb =>
        Arb.From(Gen.Elements(
            MouseTrackingMode.Off,
            MouseTrackingMode.Click,
            MouseTrackingMode.Button,
            MouseTrackingMode.Any));

    /// <summary>
    ///     **Feature: mouse-input-support, Property 3: Mouse Event Local Handling**
    ///     **Validates: Requirements R1.4, R6.1, R6.2**
    ///     Property: For any mouse event when tracking is disabled or shift key is held,
    ///     the terminal should route the event to local handlers instead of generating escape sequences.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MouseEventLocalHandlingWhenTrackingDisabledOrShiftHeld()
    {
        return Prop.ForAll(MouseEventTypeArb, MouseButtonArb, TerminalCoordinateArb,
            (eventType, button, x1) =>
            {
                var y1 = x1 + 1;
                var modifiers = MouseKeyModifiers.Shift; // Test with shift key

                // Arrange - Create managers and processor
                var trackingManager = new MouseTrackingManager();
                var stateManager = new MouseStateManager();
                var processor = new MouseEventProcessor(trackingManager, stateManager);

                // Test with tracking disabled
                trackingManager.SetTrackingMode(MouseTrackingMode.Off);

                // Create mouse event
                var mouseEvent = new MouseEvent(eventType, button, x1, y1, modifiers);

                // Act - Determine if should handle locally
                var shouldHandleLocallyWhenOff = processor.ShouldHandleLocally(mouseEvent);

                // Test with tracking enabled but shift held
                trackingManager.SetTrackingMode(MouseTrackingMode.Any);
                var shouldHandleLocallyWithShift = processor.ShouldHandleLocally(mouseEvent);

                // Test with tracking enabled and no shift
                var mouseEventNoShift = new MouseEvent(eventType, button, x1, y1, MouseKeyModifiers.None);
                var shouldHandleLocallyNoShift = processor.ShouldHandleLocally(mouseEventNoShift);

                // Assert - Local handling rules
                bool trackingOffHandledLocally = shouldHandleLocallyWhenOff;
                bool shiftKeyHandledLocally = shouldHandleLocallyWithShift;
                bool noShiftMayBeHandledByApp = !shouldHandleLocallyNoShift || !trackingManager.ShouldReportEvent(eventType, false);

                return trackingOffHandledLocally && shiftKeyHandledLocally && noShiftMayBeHandledByApp;
            });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 3b: Mouse Event Application Routing**
    ///     **Validates: Requirements R1.4, R6.1, R6.2**
    ///     Property: For any mouse event when tracking is enabled and shift key is not held,
    ///     events supported by the current mode should be routed to the application.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MouseEventApplicationRoutingWhenTrackingEnabled()
    {
        return Prop.ForAll(MouseEventTypeArb, MouseButtonArb, TerminalCoordinateArb,
            (eventType, button, x1) =>
            {
                var y1 = x1 + 1;

                // Arrange - Create managers with tracking enabled (no shift key)
                var trackingManager = new MouseTrackingManager();
                var stateManager = new MouseStateManager();
                var processor = new MouseEventProcessor(trackingManager, stateManager);

                // Set tracking mode to Any (supports all events)
                var config = new MouseTrackingConfig(MouseTrackingMode.Any, false, true);
                trackingManager.SetConfiguration(config);

                // Create mouse event without shift modifier
                var modifiers = MouseKeyModifiers.Alt | MouseKeyModifiers.Ctrl; // No shift
                var mouseEvent = new MouseEvent(eventType, button, x1, y1, modifiers);

                // Act - Determine routing
                var shouldHandleLocally = processor.ShouldHandleLocally(mouseEvent);

                // Assert - Should route to application (not local) when tracking enabled and no shift
                bool trackingEnabled = config.Mode != MouseTrackingMode.Off;
                bool noShift = !modifiers.HasFlag(MouseKeyModifiers.Shift);
                bool modeSupportsEvent = trackingManager.ShouldReportEvent(eventType, stateManager.HasButtonPressed);

                bool expectedApplicationRouting = trackingEnabled && noShift && modeSupportsEvent;

                return shouldHandleLocally != expectedApplicationRouting;
            });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 3c: Shift Key Selection Priority**
    ///     **Validates: Requirements R6.1, R6.2**
    ///     Property: For any mouse event with shift key held and selection priority enabled,
    ///     the event should always be handled locally regardless of tracking mode.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ShiftKeyEnforcesSelectionPriority()
    {
        return Prop.ForAll(MouseEventTypeArb, MouseButtonArb, TerminalCoordinateArb,
            (eventType, button, x1) =>
            {
                var y1 = x1 + 1;

                // Arrange - Create managers with selection priority enabled
                var trackingManager = new MouseTrackingManager();
                var stateManager = new MouseStateManager();
                var processor = new MouseEventProcessor(trackingManager, stateManager);

                var config = new MouseTrackingConfig(MouseTrackingMode.Any, false, selectionPriority: true);
                trackingManager.SetConfiguration(config);

                // Create mouse event with shift modifier
                var modifiers = MouseKeyModifiers.Shift | MouseKeyModifiers.Alt;
                var mouseEvent = new MouseEvent(eventType, button, x1, y1, modifiers);

                // Act - Determine routing
                var shouldHandleLocally = processor.ShouldHandleLocally(mouseEvent);

                // Assert - Should always handle locally when shift is held and selection priority is enabled
                return shouldHandleLocally;
            });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 3d: Event Processing Consistency**
    ///     **Validates: Requirements R1.4, R6.1, R6.2**
    ///     Property: For any mouse event, processing should be consistent and not throw exceptions
    ///     for valid input parameters.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MouseEventProcessingIsConsistent()
    {
        return Prop.ForAll(MouseEventTypeArb, MouseButtonArb, TerminalCoordinateArb,
            (eventType, button, x1) =>
            {
                var y1 = x1 + 1;
                var modifiers = MouseKeyModifiers.Alt;

                try
                {
                    // Arrange - Create managers and processor
                    var trackingManager = new MouseTrackingManager();
                    var stateManager = new MouseStateManager();
                    var processor = new MouseEventProcessor(trackingManager, stateManager);

                    var config = new MouseTrackingConfig(MouseTrackingMode.Any, false, true);
                    trackingManager.SetConfiguration(config);

                    // Create valid mouse event
                    var mouseEvent = new MouseEvent(eventType, button, x1, y1, modifiers);

                    // Act - Process the event (should not throw)
                    bool localHandling1 = processor.ShouldHandleLocally(mouseEvent);
                    bool localHandling2 = processor.ShouldHandleLocally(mouseEvent);

                    // Process the event through the full pipeline
                    bool eventProcessed = false;
                    bool localEventRaised = false;
                    bool appEventRaised = false;

                    processor.LocalMouseEvent += (_, _) => localEventRaised = true;
                    processor.MouseEventGenerated += (_, _) => appEventRaised = true;

                    processor.ProcessMouseEvent(mouseEvent);
                    eventProcessed = true;

                    // Assert - Processing should be consistent and complete
                    bool consistentRouting = localHandling1 == localHandling2;
                    bool eventRoutedCorrectly = (localEventRaised && !appEventRaised) ||
                                                (!localEventRaised && appEventRaised);

                    return consistentRouting && eventProcessed && eventRoutedCorrectly;
                }
                catch
                {
                    // Should not throw exceptions for valid input
                    return false;
                }
            });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 3e: Mouse State Integration**
    ///     **Validates: Requirements R1.4, R6.1, R6.2**
    ///     Property: For any sequence of mouse events, the processor should correctly
    ///     integrate with mouse state management for routing decisions.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MouseStateIntegrationAffectsRouting()
    {
        return Prop.ForAll(TerminalCoordinateArb, MouseButtonArb,
            (x1, button) =>
            {
                var y1 = x1 + 1;

                // Skip wheel buttons for press/release state tracking
                if (button == MouseButton.WheelUp || button == MouseButton.WheelDown)
                    return true;

                // Arrange - Create managers with button tracking mode
                var trackingManager = new MouseTrackingManager();
                var stateManager = new MouseStateManager();
                var processor = new MouseEventProcessor(trackingManager, stateManager);

                var config = new MouseTrackingConfig(MouseTrackingMode.Button, false, true);
                trackingManager.SetConfiguration(config);

                // Act - Simulate press, motion, release sequence
                var pressEvent = new MouseEvent(MouseEventType.Press, button, x1, y1);
                var motionEvent = new MouseEvent(MouseEventType.Motion, button, x1 + 1, y1);
                var releaseEvent = new MouseEvent(MouseEventType.Release, button, x1 + 1, y1);

                // Process press event
                processor.ProcessMouseEvent(pressEvent);
                bool motionShouldBeReported1 = !processor.ShouldHandleLocally(motionEvent);

                // Process motion event
                processor.ProcessMouseEvent(motionEvent);
                bool motionShouldBeReported2 = !processor.ShouldHandleLocally(motionEvent);

                // Process release event
                processor.ProcessMouseEvent(releaseEvent);
                bool motionShouldBeReported3 = !processor.ShouldHandleLocally(motionEvent);

                // Assert - Motion should be reported when button is pressed (mode Button)
                // but not when no button is pressed
                bool stateAffectsRouting = motionShouldBeReported1 && motionShouldBeReported2 && !motionShouldBeReported3;

                return stateAffectsRouting;
            });
    }
}
