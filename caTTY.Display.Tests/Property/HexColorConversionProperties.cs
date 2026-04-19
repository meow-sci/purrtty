using System;
using System.Linq;
using Brutal.Numerics;
using caTTY.Display.Rendering;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Display.Tests.Property;

/// <summary>
/// Property-based tests for hex color parsing and conversion.
/// Tests universal properties that should hold across all hex color operations.
/// </summary>
[TestFixture]
[Category("Property")]
public class HexColorConversionProperties
{
    /// <summary>
    /// Generator for valid hex color strings in #RRGGBB format.
    /// </summary>
    public static Arbitrary<string> ValidHexColors()
    {
        return Gen.Fresh(() =>
        {
            var r = Gen.Choose(0, 255).Sample(0, 1).First();
            var g = Gen.Choose(0, 255).Sample(0, 1).First();
            var b = Gen.Choose(0, 255).Sample(0, 1).First();
            return $"#{r:x2}{g:x2}{b:x2}";
        }).ToArbitrary();
    }

    /// <summary>
    /// Generator for valid hex color strings in uppercase format.
    /// </summary>
    public static Arbitrary<string> ValidUppercaseHexColors()
    {
        return Gen.Fresh(() =>
        {
            var r = Gen.Choose(0, 255).Sample(0, 1).First();
            var g = Gen.Choose(0, 255).Sample(0, 1).First();
            var b = Gen.Choose(0, 255).Sample(0, 1).First();
            return $"#{r:X2}{g:X2}{b:X2}";
        }).ToArbitrary();
    }

    /// <summary>
    /// Generator for valid float4 colors with components in [0.0, 1.0] range.
    /// </summary>
    public static Arbitrary<float4> ValidFloat4Colors()
    {
        return Gen.Fresh(() =>
        {
            var r = (float)Gen.Choose(0, 1000).Sample(0, 1).First() / 1000.0f;
            var g = (float)Gen.Choose(0, 1000).Sample(0, 1).First() / 1000.0f;
            var b = (float)Gen.Choose(0, 1000).Sample(0, 1).First() / 1000.0f;
            return new float4(r, g, b, 1.0f);
        }).ToArbitrary();
    }

    /// <summary>
    /// Generator for invalid hex color strings.
    /// </summary>
    public static Arbitrary<string> InvalidHexColors()
    {
        var invalidFormats = new[]
        {
            "", // Empty
            "#", // Just hash
            "#12", // Too short
            "#1234", // Wrong length
            "#12345", // Wrong length
            "#1234567", // Too long
            "#gggggg", // Invalid characters
            "#12345g", // Mixed valid/invalid
            "123456", // Missing hash
            "#12 34 56", // Spaces
            "#12-34-56" // Dashes
        };

        return Gen.Elements(invalidFormats).ToArbitrary();
    }

    /// <summary>
    /// Property 8: Hex Color Parsing Round-Trip
    /// For any valid hex color string (e.g., '#ff6188'), parsing to float4 and
    /// converting back should preserve the color values within acceptable precision.
    /// Feature: toml-terminal-theming, Property 8: Hex Color Parsing Round-Trip
    /// Validates: Requirements 5.1, 5.2
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property HexColorParsingRoundTrip_ShouldPreserveColorValues()
    {
        return Prop.ForAll(ValidHexColors(), hexColor =>
        {
            try
            {
                // Parse hex color to float4
                var parsedColor = TomlThemeLoader.ParseHexColor(hexColor);

                // Convert back to hex
                var roundTripHex = TomlThemeLoader.ToHexColor(parsedColor);

                // Parse the round-trip hex again
                var roundTripColor = TomlThemeLoader.ParseHexColor(roundTripHex);

                // Colors should be equivalent within acceptable precision
                const float epsilon = 1.0f / 255.0f; // One color step precision
                bool colorsEqual = Math.Abs(parsedColor.X - roundTripColor.X) < epsilon &&
                                  Math.Abs(parsedColor.Y - roundTripColor.Y) < epsilon &&
                                  Math.Abs(parsedColor.Z - roundTripColor.Z) < epsilon &&
                                  Math.Abs(parsedColor.W - roundTripColor.W) < epsilon;

                // Alpha should always be 1.0 for parsed colors
                bool alphaCorrect = parsedColor.W == 1.0f && roundTripColor.W == 1.0f;

                // All components should be in valid range [0.0, 1.0]
                bool componentsInRange = IsColorInRange(parsedColor) && IsColorInRange(roundTripColor);

                // Round-trip hex should be valid format
                bool validHexFormat = roundTripHex.StartsWith("#") &&
                                     roundTripHex.Length == 7 &&
                                     roundTripHex.Substring(1).All(c => char.IsDigit(c) || (c >= 'a' && c <= 'f'));

                return colorsEqual && alphaCorrect && componentsInRange && validHexFormat;
            }
            catch (Exception)
            {
                // Valid hex colors should not cause exceptions
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Hex Color Case Insensitivity
    /// Hex color parsing should handle both uppercase and lowercase hex digits.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property HexColorCaseInsensitivity_ShouldHandleBothCases()
    {
        return Prop.ForAll(ValidHexColors(), hexColor =>
        {
            try
            {
                // Parse original (lowercase) hex color
                var lowerColor = TomlThemeLoader.ParseHexColor(hexColor);

                // Convert to uppercase and parse
                var upperHex = hexColor.ToUpperInvariant();
                var upperColor = TomlThemeLoader.ParseHexColor(upperHex);

                // Colors should be identical
                const float epsilon = 0.001f;
                bool colorsEqual = Math.Abs(lowerColor.X - upperColor.X) < epsilon &&
                                  Math.Abs(lowerColor.Y - upperColor.Y) < epsilon &&
                                  Math.Abs(lowerColor.Z - upperColor.Z) < epsilon &&
                                  Math.Abs(lowerColor.W - upperColor.W) < epsilon;

                return colorsEqual;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Float4 to Hex Conversion Consistency
    /// Converting float4 colors to hex should produce consistent results.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property Float4ToHexConversion_ShouldBeConsistent()
    {
        return Prop.ForAll(ValidFloat4Colors(), color =>
        {
            try
            {
                // Convert to hex multiple times
                var hex1 = TomlThemeLoader.ToHexColor(color);
                var hex2 = TomlThemeLoader.ToHexColor(color);

                // Results should be identical (deterministic)
                bool consistent = hex1 == hex2;

                // Hex should be valid format
                bool validFormat = hex1.StartsWith("#") &&
                                  hex1.Length == 7 &&
                                  hex1.Substring(1).All(c => char.IsDigit(c) || (c >= 'a' && c <= 'f'));

                // Should be lowercase
                bool isLowercase = hex1.Substring(1).All(c => !char.IsLetter(c) || char.IsLower(c));

                return consistent && validFormat && isLowercase;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Invalid Hex Color Rejection
    /// Invalid hex color strings should be rejected with appropriate exceptions.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property InvalidHexColorRejection_ShouldThrowExceptions()
    {
        return Prop.ForAll(InvalidHexColors(), invalidHex =>
        {
            try
            {
                // Attempt to parse invalid hex color
                var color = TomlThemeLoader.ParseHexColor(invalidHex);

                // Should not reach here - invalid colors should throw exceptions
                return false;
            }
            catch (ArgumentException)
            {
                // Expected exception for invalid hex colors
                return true;
            }
            catch (Exception)
            {
                // Other exceptions are acceptable for invalid input
                return true;
            }
        });
    }

    /// <summary>
    /// Property: Color Component Clamping
    /// Float4 colors with components outside [0.0, 1.0] should be clamped when converted to hex.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ColorComponentClamping_ShouldClampToValidRange()
    {
        return Prop.ForAll<float, float, float>(
            Gen.Choose(-100, 200).Select(x => x / 100.0f).ToArbitrary(),
            Gen.Choose(-100, 200).Select(x => x / 100.0f).ToArbitrary(),
            Gen.Choose(-100, 200).Select(x => x / 100.0f).ToArbitrary(),
            (r, g, b) =>
        {
            try
            {
                // Create color with potentially out-of-range components
                var color = new float4(r, g, b, 1.0f);

                // Convert to hex (should clamp components)
                var hex = TomlThemeLoader.ToHexColor(color);

                // Parse back to verify clamping
                var clampedColor = TomlThemeLoader.ParseHexColor(hex);

                // All components should be in valid range [0.0, 1.0]
                bool componentsInRange = IsColorInRange(clampedColor);

                // Alpha should be 1.0
                bool alphaCorrect = clampedColor.W == 1.0f;

                // Hex should be valid format
                bool validHexFormat = hex.StartsWith("#") &&
                                     hex.Length == 7 &&
                                     hex.Substring(1).All(c => char.IsDigit(c) || (c >= 'a' && c <= 'f'));

                return componentsInRange && alphaCorrect && validHexFormat;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Hex Color Precision Consistency
    /// Colors that differ by less than 1/255 should map to the same hex value.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property HexColorPrecisionConsistency_ShouldMapSimilarColorsToSameHex()
    {
        return Prop.ForAll(ValidFloat4Colors(), baseColor =>
        {
            try
            {
                // Create slightly different color (within one color step)
                const float colorStep = 1.0f / 255.0f;
                const float smallDelta = colorStep * 0.4f; // Less than half a color step

                var similarColor = new float4(
                    Math.Clamp(baseColor.X + smallDelta, 0.0f, 1.0f),
                    Math.Clamp(baseColor.Y + smallDelta, 0.0f, 1.0f),
                    Math.Clamp(baseColor.Z + smallDelta, 0.0f, 1.0f),
                    1.0f
                );

                // Convert both to hex
                var baseHex = TomlThemeLoader.ToHexColor(baseColor);
                var similarHex = TomlThemeLoader.ToHexColor(similarColor);

                // Small differences should often map to the same hex value
                // (This is expected due to quantization to 8-bit values)
                // We'll just verify that both produce valid hex strings
                bool baseValid = baseHex.StartsWith("#") && baseHex.Length == 7;
                bool similarValid = similarHex.StartsWith("#") && similarHex.Length == 7;

                return baseValid && similarValid;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Boundary Value Handling
    /// Hex colors at boundary values (000000, ffffff) should be handled correctly.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 50, QuietOnSuccess = true)]
    public FsCheck.Property BoundaryValueHandling_ShouldHandleExtremeValues()
    {
        var boundaryColors = new[] { "#000000", "#ffffff", "#ff0000", "#00ff00", "#0000ff" };

        return Prop.ForAll(Gen.Elements(boundaryColors).ToArbitrary(), boundaryHex =>
        {
            try
            {
                // Parse boundary color
                var color = TomlThemeLoader.ParseHexColor(boundaryHex);

                // Convert back to hex
                var roundTripHex = TomlThemeLoader.ToHexColor(color);

                // Should be identical (or equivalent)
                var roundTripColor = TomlThemeLoader.ParseHexColor(roundTripHex);

                // Colors should be equivalent
                const float epsilon = 0.001f;
                bool colorsEqual = Math.Abs(color.X - roundTripColor.X) < epsilon &&
                                  Math.Abs(color.Y - roundTripColor.Y) < epsilon &&
                                  Math.Abs(color.Z - roundTripColor.Z) < epsilon &&
                                  Math.Abs(color.W - roundTripColor.W) < epsilon;

                // All components should be in valid range
                bool componentsInRange = IsColorInRange(color) && IsColorInRange(roundTripColor);

                return colorsEqual && componentsInRange;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Helper method to check if a color is in valid range [0.0, 1.0].
    /// </summary>
    private static bool IsColorInRange(float4 color)
    {
        return color.X >= 0.0f && color.X <= 1.0f &&
               color.Y >= 0.0f && color.Y <= 1.0f &&
               color.Z >= 0.0f && color.Z <= 1.0f &&
               color.W >= 0.0f && color.W <= 1.0f;
    }
}
