using caTTY.Core.Parsing;
using caTTY.Core.Types;
using FsCheck;
using NUnit.Framework;
using Microsoft.Extensions.Logging.Abstractions;

namespace caTTY.Core.Tests.Property;

/// <summary>
///     Property-based tests for SGR (Select Graphic Rendition) parsing and application.
///     These tests verify universal properties that should hold for all valid SGR sequences.
/// </summary>
[TestFixture]
[Category("Property")]
public class SgrParsingProperties
{
    /// <summary>
    ///     Generator for valid SGR parameter values.
    /// </summary>
    public static Arbitrary<int> ValidSgrParameterArb =>
        Arb.From(Gen.OneOf(
            Gen.Choose(0, 107),     // Standard SGR parameters
            Gen.Choose(0, 255),     // Extended color indices
            Gen.Constant(0)         // Reset parameter
        ));

    /// <summary>
    ///     Generator for valid color component values (0-255).
    /// </summary>
    public static Arbitrary<byte> ColorComponentArb =>
        Arb.From(Gen.Choose(0, 255).Select(i => (byte)i));

    /// <summary>
    ///     Generator for simple SGR sequences.
    /// </summary>
    public static Arbitrary<string> SimpleSgrSequenceArb =>
        Arb.From(ValidSgrParameterArb.Generator.Select(param => $"\x1b[{param}m"));

    /// <summary>
    ///     **Feature: catty-ksa, Property 21: SGR parsing and application**
    ///     **Validates: Requirements 12.1, 12.2, 12.4, 12.5**
    ///     Property: For any valid SGR sequence, parsing should succeed and produce
    ///     a valid SgrSequence with consistent message structure.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property SgrParsingProducesValidSequence()
    {
        return Prop.ForAll(SimpleSgrSequenceArb, sequence =>
        {
            // Arrange
            var parser = new SgrParser(NullLogger.Instance);
            byte[] sequenceBytes = System.Text.Encoding.UTF8.GetBytes(sequence);

            // Act
            var result = parser.ParseSgrSequence(sequenceBytes, sequence);

            // Assert - Basic structure validation
            bool hasValidType = result.Type == "sgr";
            bool hasValidRaw = result.Raw == sequence;
            bool hasMessages = result.Messages != null && result.Messages.Length > 0;
            bool allMessagesHaveType = result.Messages?.All(m => !string.IsNullOrEmpty(m.Type)) ?? false;

            return hasValidType && hasValidRaw && hasMessages && allMessagesHaveType;
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 21b: SGR parameter parsing consistency**
    ///     **Validates: Requirements 12.1, 12.4**
    ///     Property: For any valid parameter string, parsing should be consistent
    ///     regardless of separator type (semicolon vs colon).
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property SgrParameterParsingIsConsistent()
    {
        return Prop.ForAll(ValidSgrParameterArb, ValidSgrParameterArb, (param1, param2) =>
        {
            // Arrange
            var parser = new SgrParser(NullLogger.Instance);
            string semicolonParams = $"{param1};{param2}";
            string colonParams = $"{param1}:{param2}";

            // Act
            bool semicolonSuccess = parser.TryParseParameters(semicolonParams, out int[] semicolonResult);
            bool colonSuccess = parser.TryParseParameters(colonParams, out int[] colonResult);

            // Assert - Both should succeed and produce same parameters
            bool bothSucceed = semicolonSuccess && colonSuccess;
            bool sameLength = semicolonResult.Length == colonResult.Length;
            bool sameValues = semicolonResult.SequenceEqual(colonResult);

            return bothSucceed && sameLength && sameValues;
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 21c: SGR attribute application idempotency**
    ///     **Validates: Requirements 12.2, 12.5**
    ///     Property: For any SGR reset sequence (CSI 0 m), applying it multiple times
    ///     should produce the same result as applying it once.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property SgrResetIsIdempotent()
    {
        return Prop.ForAll<bool, bool>((bold, italic) =>
        {
            // Arrange - Create a simple initial state
            var initialAttributes = new SgrAttributes(bold: bold, italic: italic);
            var parser = new SgrParser(NullLogger.Instance);
            var resetSequence = parser.ParseSgrSequence(
                System.Text.Encoding.UTF8.GetBytes("\x1b[0m"), "\x1b[0m");

            // Act - Apply reset once
            var afterOneReset = parser.ApplyAttributes(initialAttributes, resetSequence.Messages);

            // Apply reset again
            var afterTwoResets = parser.ApplyAttributes(afterOneReset, resetSequence.Messages);

            // Assert - Both results should be identical and equal to default
            bool sameAfterMultipleResets = afterOneReset.Equals(afterTwoResets);
            bool equalsDefault = afterOneReset.Equals(SgrAttributes.Default);

            return sameAfterMultipleResets && equalsDefault;
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 21d: SGR color parsing correctness**
    ///     **Validates: Requirements 12.1, 12.4**
    ///     Property: For any valid RGB color sequence, the parsed color should
    ///     match the input RGB values exactly.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property RgbColorParsingIsCorrect()
    {
        return Prop.ForAll(ColorComponentArb, ColorComponentArb, ColorComponentArb, (r, g, b) =>
        {
            // Arrange - Test foreground color
            var parser = new SgrParser(NullLogger.Instance);
            string sequence = $"\x1b[38;2;{r};{g};{b}m";
            byte[] sequenceBytes = System.Text.Encoding.UTF8.GetBytes(sequence);

            // Act
            var result = parser.ParseSgrSequence(sequenceBytes, sequence);
            var colorMessage = result.Messages.FirstOrDefault(m =>
                m.Type == "sgr.foregroundColor" || m.Type == "sgr.backgroundColor");

            // Assert - Color should match input values
            if (colorMessage?.Data is Color color && color.Type == ColorType.Rgb)
            {
                return color.Red == r && color.Green == g && color.Blue == b;
            }

            return false; // Should have found a color message
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 21e: SGR attribute application preserves unrelated attributes**
    ///     **Validates: Requirements 12.2, 12.5**
    ///     Property: For any SGR sequence that modifies specific attributes, unrelated
    ///     attributes should remain unchanged.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property SgrApplicationPreservesUnrelatedAttributes()
    {
        return Prop.ForAll<bool, bool>((initialItalic, initialUnderline) =>
        {
            // Arrange - Create initial attributes with some values
            var initialAttributes = new SgrAttributes(italic: initialItalic, underline: initialUnderline);
            var parser = new SgrParser(NullLogger.Instance);
            var boldSequence = parser.ParseSgrSequence(
                System.Text.Encoding.UTF8.GetBytes("\x1b[1m"), "\x1b[1m");

            // Act
            var result = parser.ApplyAttributes(initialAttributes, boldSequence.Messages);

            // Assert - Only bold should change, other attributes preserved
            bool boldChanged = result.Bold == true;
            bool otherAttributesPreserved =
                result.Italic == initialAttributes.Italic &&
                result.Underline == initialAttributes.Underline;

            return boldChanged && otherAttributesPreserved;
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 21f: SGR indexed color parsing correctness**
    ///     **Validates: Requirements 12.1, 12.4**
    ///     Property: For any valid 256-color sequence, the parsed color should
    ///     match the input index exactly.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property IndexedColorParsingIsCorrect()
    {
        return Prop.ForAll(Arb.From(Gen.Choose(0, 255)), index =>
        {
            // Arrange - Test foreground color
            var parser = new SgrParser(NullLogger.Instance);
            string sequence = $"\x1b[38;5;{index}m";
            byte[] sequenceBytes = System.Text.Encoding.UTF8.GetBytes(sequence);

            // Act
            var result = parser.ParseSgrSequence(sequenceBytes, sequence);
            var colorMessage = result.Messages.FirstOrDefault(m =>
                m.Type == "sgr.foregroundColor" || m.Type == "sgr.backgroundColor");

            // Assert - Color should match input index
            if (colorMessage?.Data is Color color && color.Type == ColorType.Indexed)
            {
                return color.Index == index;
            }

            return false; // Should have found a color message
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 21g: SGR underline style parsing correctness**
    ///     **Validates: Requirements 12.1, 12.2**
    ///     Property: For any valid underline style sequence, the parsed underline style
    ///     should match the expected style for the input parameter.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property UnderlineStyleParsingIsCorrect()
    {
        return Prop.ForAll(Arb.From(Gen.Choose(0, 5)), styleParam =>
        {
            // Arrange
            var parser = new SgrParser(NullLogger.Instance);
            string sequence = $"\x1b[4:{styleParam}m";
            byte[] sequenceBytes = System.Text.Encoding.UTF8.GetBytes(sequence);

            // Act
            var result = parser.ParseSgrSequence(sequenceBytes, sequence);
            var initialAttributes = SgrAttributes.Default;
            var finalAttributes = parser.ApplyAttributes(initialAttributes, result.Messages);

            // Assert - Handle special case where 4:0 means "not underlined"
            if (styleParam == 0)
            {
                // 4:0 should disable underline
                return finalAttributes.Underline == false;
            }
            else
            {
                // Expected style mapping for non-zero parameters
                UnderlineStyle expectedStyle = styleParam switch
                {
                    1 => UnderlineStyle.Single,
                    2 => UnderlineStyle.Double,
                    3 => UnderlineStyle.Curly,
                    4 => UnderlineStyle.Dotted,
                    5 => UnderlineStyle.Dashed,
                    _ => UnderlineStyle.Single
                };

                // Underline should be enabled with correct style
                bool underlineEnabled = finalAttributes.Underline;
                bool correctStyle = finalAttributes.UnderlineStyle == expectedStyle;

                return underlineEnabled && correctStyle;
            }
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 22: SGR reset behavior**
    ///     **Validates: Requirements 12.3**
    ///     Property: For any SGR attributes state, applying a reset SGR sequence (CSI 0 m)
    ///     should always result in the default attributes state, regardless of the initial state.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property SgrResetAlwaysProducesDefaultState()
    {
        return Prop.ForAll<bool, bool>((bold, italic) =>
        {
            // Arrange - Create initial attributes with various properties set
            var initialAttributes = new SgrAttributes(
                bold: bold,
                faint: true, // Always set some attributes to ensure they get reset
                italic: italic,
                underline: true,
                underlineStyle: UnderlineStyle.Double,
                blink: true,
                inverse: true,
                hidden: true,
                strikethrough: true,
                foregroundColor: new Color(255, 128, 64), // Some arbitrary colors
                backgroundColor: new Color(32, 64, 128),
                underlineColor: new Color(200, 100, 50),
                font: 3); // Some arbitrary font

            var parser = new SgrParser(NullLogger.Instance);

            // Test both explicit reset (CSI 0 m) and implicit reset (CSI m)
            var explicitResetSequence = parser.ParseSgrSequence(
                System.Text.Encoding.UTF8.GetBytes("\x1b[0m"), "\x1b[0m");
            var implicitResetSequence = parser.ParseSgrSequence(
                System.Text.Encoding.UTF8.GetBytes("\x1b[m"), "\x1b[m");

            // Act - Apply both reset sequences
            var afterExplicitReset = parser.ApplyAttributes(initialAttributes, explicitResetSequence.Messages);
            var afterImplicitReset = parser.ApplyAttributes(initialAttributes, implicitResetSequence.Messages);

            // Assert - Both should produce default attributes
            bool explicitResetWorks = afterExplicitReset.Equals(SgrAttributes.Default);
            bool implicitResetWorks = afterImplicitReset.Equals(SgrAttributes.Default);
            bool bothResetsSame = afterExplicitReset.Equals(afterImplicitReset);

            // Verify specific properties are reset
            bool allBooleanAttributesReset =
                !afterExplicitReset.Bold &&
                !afterExplicitReset.Faint &&
                !afterExplicitReset.Italic &&
                !afterExplicitReset.Underline &&
                !afterExplicitReset.Blink &&
                !afterExplicitReset.Inverse &&
                !afterExplicitReset.Hidden &&
                !afterExplicitReset.Strikethrough;

            bool allColorsReset =
                !afterExplicitReset.ForegroundColor.HasValue &&
                !afterExplicitReset.BackgroundColor.HasValue &&
                !afterExplicitReset.UnderlineColor.HasValue;

            bool underlineStyleReset = afterExplicitReset.UnderlineStyle == UnderlineStyle.None;
            bool fontReset = afterExplicitReset.Font == 0;

            return explicitResetWorks && implicitResetWorks && bothResetsSame &&
                   allBooleanAttributesReset && allColorsReset && underlineStyleReset && fontReset;
        });
    }
}
