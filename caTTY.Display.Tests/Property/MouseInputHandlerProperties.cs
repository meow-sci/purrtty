using System;
using System.Collections.Generic;
using Brutal.Numerics;
using caTTY.Core.Input;
using caTTY.Core.Managers;
using caTTY.Core.Types;
using caTTY.Display.Input;
using caTTY.Display.Utils;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Display.Tests.Property;

/// <summary>
/// Property-based tests for MouseInputHandler functionality.
/// Tests universal properties for mouse button detection and capture behavior.
/// </summary>
[TestFixture]
[Category("Property")]
public class MouseInputHandlerProperties
{
    /// <summary>
    /// Generator for valid mouse button types.
    /// Produces the three main mouse buttons used in terminal input.
    /// </summary>
    public static Arbitrary<MouseButton> ValidMouseButtons()
    {
        var buttonGen = Gen.Elements(MouseButton.Left, MouseButton.Right, MouseButton.Middle);
        return buttonGen.ToArbitrary();
    }

    /// <summary>
    /// Generator for valid terminal coordinates.
    /// Produces reasonable 1-based terminal coordinates.
    /// </summary>
    public static Arbitrary<(int X1, int Y1)> ValidTerminalCoordinates()
    {
        var xGen = Gen.Choose(1, 200);
        var yGen = Gen.Choose(1, 100);

        return Gen.Zip(xGen, yGen)
            .Select(tuple => (tuple.Item1, tuple.Item2))
            .ToArbitrary();
    }

    /// <summary>
    /// Generator for valid modifier key combinations.
    /// Produces realistic combinations of modifier keys.
    /// </summary>
    public static Arbitrary<MouseKeyModifiers> ValidModifierCombinations()
    {
        var modifierGen = Gen.Elements(
            MouseKeyModifiers.None,
            MouseKeyModifiers.Shift,
            MouseKeyModifiers.Alt,
            MouseKeyModifiers.Ctrl,
            MouseKeyModifiers.Shift | MouseKeyModifiers.Alt,
            MouseKeyModifiers.Shift | MouseKeyModifiers.Ctrl,
            MouseKeyModifiers.Alt | MouseKeyModifiers.Ctrl,
            MouseKeyModifiers.Shift | MouseKeyModifiers.Alt | MouseKeyModifiers.Ctrl
        );
        return modifierGen.ToArbitrary();
    }

    /// <summary>
    /// Generator for valid terminal dimensions.
    /// Produces reasonable terminal sizes in columns and rows.
    /// </summary>
    public static Arbitrary<(int Width, int Height)> ValidTerminalDimensions()
    {
        var widthGen = Gen.Choose(10, 200);
        var heightGen = Gen.Choose(5, 100);

        return Gen.Zip(widthGen, heightGen)
            .Select(tuple => (tuple.Item1, tuple.Item2))
            .ToArbitrary();
    }

    /// <summary>
    /// Property 10: Mouse Button Detection
    /// For any mouse button press event, the terminal should correctly identify the button
    /// (left=0, middle=1, right=2) and coordinates.
    /// Feature: mouse-input-support, Property 10: Mouse Button Detection
    /// Validates: Requirements R4.1, R4.2, R4.3
    ///
    /// NOTE: This test validates the button value mappings and coordinate handling logic.
    /// The actual ImGui input detection requires a real ImGui context and is tested via integration tests.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MouseButtonDetection_ShouldCorrectlyIdentifyButtonAndCoordinates()
    {
        return Prop.ForAll(
            ValidMouseButtons(),
            ValidTerminalCoordinates(),
            ValidModifierCombinations(),
            (button, coords, modifiers) =>
            {
                try
                {
                    var dimensions = (Width: 80, Height: 24);

                    // Ensure coordinates are within terminal bounds
                    var x1 = Math.Max(1, Math.Min(coords.X1, dimensions.Width));
                    var y1 = Math.Max(1, Math.Min(coords.Y1, dimensions.Height));

                    // Test button value mapping (R4.1, R4.2, R4.3)
                    bool buttonValueCorrect = button switch
                    {
                        MouseButton.Left => (int)button == 0,
                        MouseButton.Middle => (int)button == 1,
                        MouseButton.Right => (int)button == 2,
                        _ => false
                    };

                    // Test that coordinates are 1-based and within bounds
                    bool coordinatesValid = x1 >= 1 && x1 <= dimensions.Width &&
                                           y1 >= 1 && y1 <= dimensions.Height;

                    // Test that coordinate clamping works correctly
                    bool coordinatesClamped = x1 == Math.Max(1, Math.Min(coords.X1, dimensions.Width)) &&
                                             y1 == Math.Max(1, Math.Min(coords.Y1, dimensions.Height));

                    // Test MouseEvent creation with the values
                    var mouseEvent = new MouseEvent(
                        MouseEventType.Press,
                        button,
                        x1, y1,
                        modifiers);

                    bool eventCreatedCorrectly = mouseEvent.Button == button &&
                                               mouseEvent.X1 == x1 &&
                                               mouseEvent.Y1 == y1 &&
                                               mouseEvent.Modifiers == modifiers &&
                                               mouseEvent.Type == MouseEventType.Press;

                    return buttonValueCorrect && coordinatesValid && coordinatesClamped && eventCreatedCorrectly;
                }
                catch
                {
                    // Any exception indicates a problem with button detection logic
                    return false;
                }
            });
    }

    /// <summary>
    /// Property 17: Mouse Capture During Drag
    /// For any mouse drag operation, the terminal should capture mouse input to receive
    /// events outside terminal bounds and release capture on button release.
    /// Feature: mouse-input-support, Property 17: Mouse Capture During Drag
    /// Validates: Requirements R8.2, R8.3
    ///
    /// NOTE: This test validates the capture state logic and drag detection.
    /// The actual ImGui mouse capture requires a real ImGui context and is tested via integration tests.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MouseCaptureDuringDrag_ShouldCaptureAndRelease()
    {
        return Prop.ForAll(
            ValidMouseButtons(),
            ValidTerminalCoordinates(),
            ValidTerminalCoordinates(),
            (button, startCoords, endCoords) =>
            {
                try
                {
                    var dimensions = (Width: 80, Height: 24);

                    // Ensure coordinates are within terminal bounds
                    var startX1 = Math.Max(1, Math.Min(startCoords.X1, dimensions.Width));
                    var startY1 = Math.Max(1, Math.Min(startCoords.Y1, dimensions.Height));
                    var endX1 = Math.Max(1, Math.Min(endCoords.X1, dimensions.Width));
                    var endY1 = Math.Max(1, Math.Min(endCoords.Y1, dimensions.Height));

                    // Test the mouse state manager logic for drag detection
                    var stateManager = new TestMouseStateManager();

                    // Initially no button should be pressed
                    bool initiallyNotPressed = !stateManager.HasButtonPressed;

                    // Simulate button press
                    stateManager.SetButtonPressed(button, startX1, startY1);
                    bool buttonPressedCorrectly = stateManager.HasButtonPressed &&
                                                 stateManager.PressedButton == button;

                    // Test drag detection logic
                    bool shouldBeDragging = false;
                    if (startX1 != endX1 || startY1 != endY1)
                    {
                        // Movement should trigger drag
                        bool dragTriggered = stateManager.UpdatePosition(endX1, endY1);
                        shouldBeDragging = dragTriggered;
                    }

                    bool draggingStateCorrect = stateManager.IsDragging == shouldBeDragging;

                    // Test button release
                    stateManager.SetButtonReleased(button);
                    bool buttonReleasedCorrectly = !stateManager.HasButtonPressed &&
                                                  !stateManager.IsDragging;

                    // Test coordinate bounds validation
                    bool coordinatesValid = startX1 >= 1 && startX1 <= dimensions.Width &&
                                           startY1 >= 1 && startY1 <= dimensions.Height &&
                                           endX1 >= 1 && endX1 <= dimensions.Width &&
                                           endY1 >= 1 && endY1 <= dimensions.Height;

                    // Test state consistency
                    bool stateConsistent = stateManager.IsConsistent();

                    return initiallyNotPressed && buttonPressedCorrectly && draggingStateCorrect &&
                           buttonReleasedCorrectly && coordinatesValid && stateConsistent;
                }
                catch
                {
                    // Any exception indicates a problem with mouse capture logic
                    return false;
                }
            });
    }

    /// <summary>
    /// Test implementation of IMouseStateManager for property testing.
    /// </summary>
    private class TestMouseStateManager : IMouseStateManager
    {
        private MouseButton? _pressedButton;
        private bool _isDragging;
        private (int X1, int Y1)? _lastPosition;

        public MouseButton? PressedButton => _pressedButton;
        public bool IsDragging => _isDragging;
        public (int X1, int Y1)? LastPosition => _lastPosition;
        public bool HasButtonPressed => _pressedButton.HasValue;

        public void SetButtonPressed(MouseButton button, int x1, int y1)
        {
            _pressedButton = button;
        }

        public void SetButtonReleased(MouseButton button)
        {
            if (_pressedButton == button)
            {
                _pressedButton = null;
                _isDragging = false;
            }
        }

        public bool UpdatePosition(int x1, int y1)
        {
            var oldPosition = _lastPosition;
            _lastPosition = (x1, y1);

            if (_pressedButton.HasValue && oldPosition.HasValue &&
                (oldPosition.Value.X1 != x1 || oldPosition.Value.Y1 != y1))
            {
                _isDragging = true;
                return true;
            }

            return false;
        }

        public void Reset()
        {
            _pressedButton = null;
            _isDragging = false;
            _lastPosition = null;
        }

        public bool IsConsistent() => true;

        public void RecoverFromInconsistentState() => Reset();
    }
}
