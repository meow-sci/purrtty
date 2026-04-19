using System;
using System.Linq;
using caTTY.Core.Input;
using caTTY.Core.Types;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
///     Property-based tests for mouse escape sequence generation.
///     These tests verify universal properties for mouse event encoding.
/// </summary>
[TestFixture]
[Category("Property")]
public class MouseEscapeSequenceProperties
{
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
    ///     **Feature: mouse-input-support, Property 4: Mouse Encoding Format Selection**
    ///     **Validates: Requirements R2.1, R2.2**
    ///     Property: For any mouse event requiring escape sequence generation, the terminal should
    ///     use SGR format when SGR encoding is enabled and standard X10/X11 format otherwise.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MouseEncodingFormatSelectionIsCorrect()
    {
        return Prop.ForAll(MouseButtonArb, TerminalCoordinateArb, MouseKeyModifiersArb,
            (button, x1, modifiers) =>
        {
            var y1 = x1 + 1; // Simple derived coordinate

            // Test both encoding formats
            var sgrSequence = EscapeSequenceGenerator.GenerateMousePress(button, x1, y1, modifiers, true);
            var x10Sequence = EscapeSequenceGenerator.GenerateMousePress(button, x1, y1, modifiers, false);

            // Assert - SGR format: ESC[<...M
            bool sgrIsCorrect = sgrSequence.StartsWith("\x1b[<") && sgrSequence.EndsWith("M");

            // Assert - X10 format: ESC[M + 3 bytes
            bool x10IsCorrect = x10Sequence.StartsWith("\x1b[M") && x10Sequence.Length == 6;

            return sgrIsCorrect && x10IsCorrect;
        });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 5: Mouse Coordinate Encoding Range**
    ///     **Validates: Requirements R2.3**
    ///     Property: For any mouse coordinates above 223, SGR encoding should handle them correctly
    ///     while standard encoding should clamp them to X10/X11 limitations.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MouseCoordinateEncodingRangeIsCorrect()
    {
        return Prop.ForAll(MouseButtonArb, MouseKeyModifiersArb,
            (button, modifiers) =>
        {
            // Use coordinates that will test the boundary (above 223)
            var x1 = 500; // Above X10 limit
            var y1 = 600; // Above X10 limit

            // Act - Generate sequences with both encoding formats
            var sgrSequence = EscapeSequenceGenerator.GenerateMousePress(button, x1, y1, modifiers, true);
            var x10Sequence = EscapeSequenceGenerator.GenerateMousePress(button, x1, y1, modifiers, false);

            // Assert - SGR encoding preserves large coordinates
            bool sgrHandlesLargeCoordinates = sgrSequence.Contains($";{x1};") && sgrSequence.Contains($";{y1}M");

            // Assert - X10 encoding clamps coordinates to 223
            bool x10ClampsCoordinates = true;
            if (x10Sequence.Length == 6)
            {
                var actualX = (int)x10Sequence[4] - 32;
                var actualY = (int)x10Sequence[5] - 32;
                x10ClampsCoordinates = actualX == 223 && actualY == 223; // Should be clamped to 223
            }

            return sgrHandlesLargeCoordinates && x10ClampsCoordinates;
        });
    }

    /// <summary>
    ///     **Feature: mouse-input-support, Property 6: Mouse Event Modifier Encoding**
    ///     **Validates: Requirements R2.4, R9.1, R9.2, R9.3, R9.4**
    ///     Property: For any mouse event with modifier keys held, the generated escape sequence should
    ///     correctly encode all active modifiers (shift, alt, ctrl) in the appropriate format.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MouseEventModifierEncodingIsCorrect()
    {
        return Prop.ForAll(MouseButtonArb, TerminalCoordinateArb, MouseKeyModifiersArb,
            (button, x1, modifiers) =>
        {
            var y1 = x1 + 1;

            // Act - Generate escape sequence with modifiers
            var sgrSequence = EscapeSequenceGenerator.GenerateMousePress(button, x1, y1, modifiers, true);
            var x10Sequence = EscapeSequenceGenerator.GenerateMousePress(button, x1, y1, modifiers, false);

            // Assert - Modifiers are correctly encoded
            bool sgrCorrect = ValidateSgrModifierEncoding(sgrSequence, button, modifiers);
            bool x10Correct = ValidateX10ModifierEncoding(x10Sequence, button, modifiers);

            return sgrCorrect && x10Correct;
        });
    }

    /// <summary>
    ///     Validates SGR modifier encoding.
    /// </summary>
    private static bool ValidateSgrModifierEncoding(string sequence, MouseButton button, MouseKeyModifiers modifiers)
    {
        if (!ExtractSgrButtonCode(sequence, out var buttonCode))
            return false;

        var expectedButtonCode = (int)button + (int)modifiers;
        return buttonCode == expectedButtonCode;
    }

    /// <summary>
    ///     Validates X10 modifier encoding.
    /// </summary>
    private static bool ValidateX10ModifierEncoding(string sequence, MouseButton button, MouseKeyModifiers modifiers)
    {
        if (sequence.Length != 6 || !sequence.StartsWith("\x1b[M"))
            return false;

        var buttonByte = (int)sequence[3] - 32;
        var expectedButtonCode = (int)button + (int)modifiers;
        return buttonByte == expectedButtonCode;
    }

    /// <summary>
    ///     Extracts button code from SGR format sequence.
    /// </summary>
    private static bool ExtractSgrButtonCode(string sequence, out int buttonCode)
    {
        buttonCode = 0;

        if (!sequence.StartsWith("\x1b[<"))
            return false;

        var endIndex = sequence.LastIndexOfAny(new[] { 'M', 'm' });
        if (endIndex == -1)
            return false;

        var paramsPart = sequence.Substring(3, endIndex - 3);
        var parts = paramsPart.Split(';');

        if (parts.Length != 3)
            return false;

        return int.TryParse(parts[0], out buttonCode);
    }
}
