using caTTY.Core.Managers;
using caTTY.Core.Types;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
///     Property-based tests for mouse state management and consistency.
///     These tests verify universal properties that should hold for all mouse state operations.
/// </summary>
[TestFixture]
[Category("Property")]
public class MouseStateProperties
{
    /// <summary>
    ///     Generator for valid mouse buttons (excluding wheel buttons for state tracking).
    /// </summary>
    public static Arbitrary<MouseButton> MouseButtonArb =>
        Arb.From(Gen.Elements(
            MouseButton.Left,
            MouseButton.Middle,
            MouseButton.Right));

    /// <summary>
    ///     Generator for valid 1-based terminal coordinates (1-1000).
    /// </summary>
    public static Arbitrary<int> TerminalCoordinateArb =>
        Arb.From(Gen.Choose(1, 1000));

    /// <summary>
    ///     Generator for sequences of mouse state operations.
    /// </summary>
    public static Arbitrary<MouseStateOperation[]> MouseStateOperationSequenceArb =>
        Arb.From(Gen.ArrayOf(Gen.OneOf(
            // Button press operations
            from type in Gen.Constant(MouseStateOperationType.Press)
            from button in MouseButtonArb.Generator
            from x1 in TerminalCoordinateArb.Generator
            from y1 in TerminalCoordinateArb.Generator
            select new MouseStateOperation(type, button, x1, y1),

            // Button release operations
            from type in Gen.Constant(MouseStateOperationType.Release)
            from button in MouseButtonArb.Generator
            select new MouseStateOperation(type, button, 1, 1),

            // Position update operations
            from type in Gen.Constant(MouseStateOperationType.UpdatePosition)
            from x1 in TerminalCoordinateArb.Generator
            from y1 in TerminalCoordinateArb.Generator
            select new MouseStateOperation(type, MouseButton.Left, x1, y1),

            // Reset operations
            Gen.Constant(new MouseStateOperation(MouseStateOperationType.Reset, MouseButton.Left, 1, 1))
        )));

    /// <summary>
    ///     **Feature: mouse-input-support, Property 16: Mouse State Consistency**
    ///     **Validates: Requirements R8.1, R8.3, R8.4, R8.5**
    ///     Property: For any sequence of mouse state operations, the mouse state manager
    ///     should maintain consistent state and recover from any inconsistencies.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MouseStateRemainsConsistentThroughOperations()
    {
        return Prop.ForAll(MouseStateOperationSequenceArb, operations =>
        {
            // Arrange
            var stateManager = new MouseStateManager();

            // Act - Apply all operations
            foreach (var operation in operations)
            {
                ApplyOperation(stateManager, operation);

                // Assert - State should always be consistent after each operation
                if (!stateManager.IsConsistent())
                {
                    // If inconsistent, recovery should restore consistency
                    stateManager.RecoverFromInconsistentState();

                    // After recovery, state must be consistent
                    if (!stateManager.IsConsistent())
                    {
                        return false;
                    }
                }
            }

            // Final state should be consistent
            return stateManager.IsConsistent();
        });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 16a: Button Press State Tracking**
    ///     **Validates: Requirements R8.1**
    ///     Property: For any button press operation, the state manager should correctly
    ///     track which button is pressed and maintain consistent state.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ButtonPressStateIsTrackedCorrectly()
    {
        return Prop.ForAll(MouseButtonArb, TerminalCoordinateArb, TerminalCoordinateArb,
            (button, x1, y1) =>
            {
                // Arrange
                var stateManager = new MouseStateManager();

                // Act - Press button
                stateManager.SetButtonPressed(button, x1, y1);

                // Assert - State should reflect button press
                bool buttonTracked = stateManager.PressedButton == button;
                bool hasButtonPressed = stateManager.HasButtonPressed;
                bool positionTracked = stateManager.LastPosition?.X1 == x1 &&
                                       stateManager.LastPosition?.Y1 == y1;
                bool notDraggingYet = !stateManager.IsDragging; // Drag starts on motion, not press
                bool stateConsistent = stateManager.IsConsistent();

                return buttonTracked && hasButtonPressed && positionTracked &&
                       notDraggingYet && stateConsistent;
            });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 16b: Button Release State Cleanup**
    ///     **Validates: Requirements R8.3**
    ///     Property: For any button release operation, the state manager should correctly
    ///     clear button state and stop dragging while maintaining consistency.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ButtonReleaseStateIsCleanedCorrectly()
    {
        return Prop.ForAll(MouseButtonArb, TerminalCoordinateArb, TerminalCoordinateArb,
            (button, x1, y1) =>
            {
                // Arrange - Press button first
                var stateManager = new MouseStateManager();
                stateManager.SetButtonPressed(button, x1, y1);

                // Start dragging by moving
                stateManager.UpdatePosition(x1 + 1, y1 + 1);

                // Act - Release button
                stateManager.SetButtonReleased(button);

                // Assert - State should be cleaned up
                bool buttonCleared = !stateManager.PressedButton.HasValue;
                bool hasButtonPressedCleared = !stateManager.HasButtonPressed;
                bool draggingStopped = !stateManager.IsDragging;
                bool positionPreserved = stateManager.LastPosition.HasValue; // Position kept for optimization
                bool stateConsistent = stateManager.IsConsistent();

                return buttonCleared && hasButtonPressedCleared && draggingStopped &&
                       positionPreserved && stateConsistent;
            });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 16c: Position Update and Drag Detection**
    ///     **Validates: Requirements R8.4**
    ///     Property: For any position update operation, the state manager should correctly
    ///     track position changes and detect drag operations when appropriate.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property PositionUpdateAndDragDetectionIsCorrect()
    {
        return Prop.ForAll(MouseButtonArb, TerminalCoordinateArb, TerminalCoordinateArb,
            (button, x1, y1) =>
            {
                var x2 = x1 + 1; // Simple derived coordinate
                var y2 = y1 + 1;

                // Arrange - Press button first
                var stateManager = new MouseStateManager();
                stateManager.SetButtonPressed(button, x1, y1);

                // Act - Update position
                bool shouldTriggerDrag = stateManager.UpdatePosition(x2, y2);

                // Assert - Drag detection and position tracking
                bool positionUpdated = stateManager.LastPosition?.X1 == x2 &&
                                       stateManager.LastPosition?.Y1 == y2;

                // Should trigger drag if position changed and button is pressed
                bool positionChanged = x1 != x2 || y1 != y2;
                bool expectedDragTrigger = positionChanged;
                bool dragTriggerCorrect = shouldTriggerDrag == expectedDragTrigger;

                // If position changed, should be dragging
                bool draggingStateCorrect = !positionChanged || stateManager.IsDragging;

                bool stateConsistent = stateManager.IsConsistent();

                return positionUpdated && dragTriggerCorrect && draggingStateCorrect && stateConsistent;
            });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 16d: State Recovery from Corruption**
    ///     **Validates: Requirements R8.5**
    ///     Property: For any corrupted state, the recovery mechanism should restore
    ///     the state manager to a consistent and valid state.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property StateRecoveryRestoresConsistency()
    {
        return Prop.ForAll(MouseButtonArb, TerminalCoordinateArb, TerminalCoordinateArb,
            (button, x1, y1) =>
            {
                // Arrange - Create state manager and put it in a valid state
                var stateManager = new MouseStateManager();
                stateManager.SetButtonPressed(button, x1, y1);
                stateManager.UpdatePosition(x1 + 1, y1 + 1);

                // Verify it starts consistent
                bool initiallyConsistent = stateManager.IsConsistent();

                // Act - Force recovery (simulates corruption detection and recovery)
                stateManager.RecoverFromInconsistentState();

                // Assert - After recovery, state should be consistent
                bool recoveredConsistent = stateManager.IsConsistent();

                // After recovery, state should be reset to initial values
                bool stateReset = !stateManager.HasButtonPressed &&
                                  !stateManager.IsDragging;

                return initiallyConsistent && recoveredConsistent && stateReset;
            });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 16e: Wheel Button Handling**
    ///     **Validates: Requirements R8.1, R8.3, R8.4, R8.5**
    ///     Property: For any wheel button operation, the state manager should not
    ///     track wheel buttons as pressed buttons and maintain consistency.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property WheelButtonsAreNotTrackedAsPressed()
    {
        return Prop.ForAll(TerminalCoordinateArb, TerminalCoordinateArb, (x1, y1) =>
        {
            // Arrange
            var stateManager = new MouseStateManager();

            // Act - Try to press wheel buttons
            stateManager.SetButtonPressed(MouseButton.WheelUp, x1, y1);
            bool wheelUpNotTracked = !stateManager.HasButtonPressed;

            stateManager.SetButtonPressed(MouseButton.WheelDown, x1, y1);
            bool wheelDownNotTracked = !stateManager.HasButtonPressed;

            // Assert - Wheel buttons should not be tracked as pressed
            bool stateConsistent = stateManager.IsConsistent();

            return wheelUpNotTracked && wheelDownNotTracked && stateConsistent;
        });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 16f: Invalid Coordinate Handling**
    ///     **Validates: Requirements R8.1, R8.3, R8.4, R8.5**
    ///     Property: For any invalid coordinates (< 1), the state manager should
    ///     reject the operation and maintain consistency.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property InvalidCoordinatesAreRejected()
    {
        return Prop.ForAll(MouseButtonArb, (button) =>
        {
            // Arrange
            var stateManager = new MouseStateManager();

            // Act - Try operations with invalid coordinates
            stateManager.SetButtonPressed(button, 0, 1); // Invalid X
            bool invalidXRejected = !stateManager.HasButtonPressed;

            stateManager.SetButtonPressed(button, 1, 0); // Invalid Y
            bool invalidYRejected = !stateManager.HasButtonPressed;

            stateManager.SetButtonPressed(button, -1, -1); // Both invalid
            bool bothInvalidRejected = !stateManager.HasButtonPressed;

            // Try position update with invalid coordinates
            stateManager.SetButtonPressed(button, 1, 1); // Valid press first
            bool positionUpdateRejected = !stateManager.UpdatePosition(0, 1); // Invalid update

            // Assert - Invalid coordinates should be rejected
            bool stateConsistent = stateManager.IsConsistent();

            return invalidXRejected && invalidYRejected && bothInvalidRejected &&
                   positionUpdateRejected && stateConsistent;
        });
    }

    /// <summary>
    ///     Applies a mouse state operation to the state manager.
    /// </summary>
    private static void ApplyOperation(IMouseStateManager stateManager, MouseStateOperation operation)
    {
        switch (operation.Type)
        {
            case MouseStateOperationType.Press:
                stateManager.SetButtonPressed(operation.Button, operation.X1, operation.Y1);
                break;
            case MouseStateOperationType.Release:
                stateManager.SetButtonReleased(operation.Button);
                break;
            case MouseStateOperationType.UpdatePosition:
                stateManager.UpdatePosition(operation.X1, operation.Y1);
                break;
            case MouseStateOperationType.Reset:
                stateManager.Reset();
                break;
        }
    }
}

/// <summary>
///     Represents a mouse state operation for property testing.
/// </summary>
public record MouseStateOperation(
    MouseStateOperationType Type,
    MouseButton Button,
    int X1,
    int Y1);

/// <summary>
///     Types of mouse state operations for property testing.
/// </summary>
public enum MouseStateOperationType
{
    Press,
    Release,
    UpdatePosition,
    Reset
}
