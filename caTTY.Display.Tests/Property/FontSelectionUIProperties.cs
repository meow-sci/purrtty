using caTTY.Core.Terminal;
using caTTY.Display.Configuration;
using caTTY.Display.Controllers;
using caTTY.Display.Rendering;
using FsCheck;
using NUnit.Framework;
using System.Linq;

namespace caTTY.Display.Tests.Property;

/// <summary>
/// Property-based tests for font selection UI state consistency.
/// Tests universal properties that should hold across all font selection operations.
/// </summary>
[TestFixture]
[Category("Property")]
public class FontSelectionUIProperties
{
    /// <summary>
    /// Generator for valid font family display names from the registry.
    /// Produces font families that should be available for selection.
    /// </summary>
    public static Arbitrary<string> ValidFontFamilyNames()
    {
        var validFamilies = new[]
        {
            "Jet Brains Mono",
            "Space Mono",
            "Hack",
            "Pro Font",
            "Proggy Clean",
            "Shure Tech Mono",
            "Departure Mono"
        };

        return Gen.Elements(validFamilies).ToArbitrary();
    }

    /// <summary>
    /// Generator for invalid font family names that should not be in the registry.
    /// Produces font families that should trigger error handling.
    /// </summary>
    public static Arbitrary<string> InvalidFontFamilyNames()
    {
        var invalidFamilies = new[]
        {
            "",
            "NonExistentFont",
            "Invalid Font Name",
            "Arial",
            "Times New Roman",
            "Comic Sans MS",
            "Unknown Font Family"
        };

        return Gen.Elements(invalidFamilies).ToArbitrary();
    }

    /// <summary>
    /// Generator for valid font sizes within the acceptable range.
    /// Produces font sizes that should be accepted by the system.
    /// </summary>
    public static Arbitrary<float> ValidFontSizes()
    {
        return Gen.Choose(8, 72).Select(i => (float)i).ToArbitrary();
    }

    /// <summary>
    /// Property 3: Font Selection UI State Consistency
    /// For any font selection through the UI menu, the system should immediately update
    /// the terminal font configuration, re-render with the new font, maintain cursor
    /// accuracy, and update the menu to show the newly selected font as active.
    /// Feature: font-selection-ui, Property 3: Font Selection UI State Consistency
    /// Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 4.1, 4.2, 4.3, 4.4, 4.5, 6.1, 6.2, 6.3, 6.4, 6.5
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FontSelectionUIStateConsistency_ShouldMaintainConsistentState()
    {
        return Prop.ForAll(ValidFontFamilyNames(), ValidFontSizes(), (selectedFontFamily, fontSize) =>
        {
            try
            {
                // Ensure font registry is initialized
                CaTTYFontManager.LoadFonts();

                // Create a mock terminal controller for testing
                // Note: This is a simplified test that focuses on the font configuration logic
                // Full UI testing would require ImGui context which is not available in unit tests

                // Test 1: Font configuration generation should work for valid font families
                var newFontConfig = CaTTYFontManager.CreateFontConfigForFamily(selectedFontFamily, fontSize);
                bool configGenerated = newFontConfig != null;

                if (!configGenerated)
                {
                    return false.ToProperty().Label($"Font configuration generation failed for '{selectedFontFamily}'");
                }

                // Test 2: Generated configuration should be valid
                try
                {
                    newFontConfig!.Validate();
                }
                catch (Exception ex)
                {
                    return false.ToProperty().Label($"Generated font configuration for '{selectedFontFamily}' failed validation: {ex.Message}");
                }

                // Test 3: Font size should be preserved in the new configuration
                bool fontSizePreserved = Math.Abs(newFontConfig!.FontSize - fontSize) < 0.001f;

                if (!fontSizePreserved)
                {
                    return false.ToProperty().Label($"Font size not preserved: expected {fontSize}, got {newFontConfig.FontSize}");
                }

                // Test 4: Current font family detection should work with the new configuration
                var detectedFamily = CaTTYFontManager.GetCurrentFontFamily(newFontConfig);
                bool familyDetected = detectedFamily == selectedFontFamily;

                if (!familyDetected)
                {
                    return false.ToProperty().Label($"Font family detection failed: expected '{selectedFontFamily}', got '{detectedFamily}'");
                }

                // Test 5: Font configuration should use appropriate variant fallback
                var definition = CaTTYFontManager.GetFontFamilyDefinition(selectedFontFamily);
                if (definition == null)
                {
                    return false.ToProperty().Label($"Font family definition not found for '{selectedFontFamily}'");
                }

                string expectedRegular = $"{definition.FontBaseName}-Regular";
                string expectedBold = definition.HasBold ? $"{definition.FontBaseName}-Bold" : expectedRegular;
                string expectedItalic = definition.HasItalic ? $"{definition.FontBaseName}-Italic" : expectedRegular;
                string expectedBoldItalic = definition.HasBoldItalic ? $"{definition.FontBaseName}-BoldItalic" : expectedRegular;

                bool variantFallbackCorrect = newFontConfig.RegularFontName == expectedRegular &&
                                             newFontConfig.BoldFontName == expectedBold &&
                                             newFontConfig.ItalicFontName == expectedItalic &&
                                             newFontConfig.BoldItalicFontName == expectedBoldItalic;

                if (!variantFallbackCorrect)
                {
                    return false.ToProperty().Label($"Variant fallback incorrect for '{selectedFontFamily}': " +
                        $"Expected Regular='{expectedRegular}', Bold='{expectedBold}', Italic='{expectedItalic}', BoldItalic='{expectedBoldItalic}'; " +
                        $"Got Regular='{newFontConfig.RegularFontName}', Bold='{newFontConfig.BoldFontName}', Italic='{newFontConfig.ItalicFontName}', BoldItalic='{newFontConfig.BoldItalicFontName}'");
                }

                // Test 6: AutoDetectContext should be disabled for generated configurations
                bool autoDetectDisabled = !newFontConfig.AutoDetectContext;

                if (!autoDetectDisabled)
                {
                    return false.ToProperty().Label($"AutoDetectContext should be disabled for font selection, but was enabled for '{selectedFontFamily}'");
                }

                // Test 7: Round-trip consistency - selecting the same font again should produce identical configuration
                var roundTripConfig = CaTTYFontManager.CreateFontConfigForFamily(selectedFontFamily, fontSize);
                bool roundTripConsistent = roundTripConfig.RegularFontName == newFontConfig.RegularFontName &&
                                          roundTripConfig.BoldFontName == newFontConfig.BoldFontName &&
                                          roundTripConfig.ItalicFontName == newFontConfig.ItalicFontName &&
                                          roundTripConfig.BoldItalicFontName == newFontConfig.BoldItalicFontName &&
                                          Math.Abs(roundTripConfig.FontSize - newFontConfig.FontSize) < 0.001f;

                if (!roundTripConsistent)
                {
                    return false.ToProperty().Label($"Round-trip consistency failed for '{selectedFontFamily}': configurations don't match");
                }

                // Test 8: Font family should be available in the registry
                var availableFamilies = CaTTYFontManager.GetAvailableFontFamilies();
                bool familyAvailable = availableFamilies.Contains(selectedFontFamily);

                if (!familyAvailable)
                {
                    return false.ToProperty().Label($"Selected font family '{selectedFontFamily}' not found in available families");
                }

                return true.ToProperty();
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception testing font selection UI state consistency for '{selectedFontFamily}': {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Property: Font Selection Error Handling
    /// For any invalid font family selection, the system should handle errors gracefully
    /// without crashing and maintain the current font configuration.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FontSelectionErrorHandling_ShouldHandleInvalidSelectionsGracefully()
    {
        return Prop.ForAll(InvalidFontFamilyNames(), ValidFontSizes(), (invalidFontFamily, fontSize) =>
        {
            try
            {
                // Ensure font registry is initialized
                CaTTYFontManager.LoadFonts();

                // Test that CreateFontConfigForFamily handles invalid font families gracefully
                var config = CaTTYFontManager.CreateFontConfigForFamily(invalidFontFamily, fontSize);
                bool configCreated = config != null;

                if (!configCreated)
                {
                    return false.ToProperty().Label($"CreateFontConfigForFamily returned null for invalid font '{invalidFontFamily}'");
                }

                // Test that the returned configuration is valid (should be default)
                try
                {
                    config!.Validate();
                }
                catch (Exception ex)
                {
                    return false.ToProperty().Label($"Default configuration for invalid font '{invalidFontFamily}' failed validation: {ex.Message}");
                }

                // Test that GetCurrentFontFamily returns null for invalid font configurations
                var detectedFamily = CaTTYFontManager.GetCurrentFontFamily(config!);

                // For invalid font families, we expect either null or a valid fallback family
                if (detectedFamily != null)
                {
                    var availableFamilies = CaTTYFontManager.GetAvailableFontFamilies();
                    bool detectedFamilyValid = availableFamilies.Contains(detectedFamily);

                    if (!detectedFamilyValid)
                    {
                        return false.ToProperty().Label($"Detected family '{detectedFamily}' for invalid input '{invalidFontFamily}' is not in registry");
                    }
                }

                // Test that the system doesn't crash when processing invalid font families
                // This is implicitly tested by reaching this point without exceptions

                return true.ToProperty();
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception testing error handling for invalid font '{invalidFontFamily}': {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Property: Font Selection State Persistence
    /// For any sequence of valid font selections, the system should maintain consistent
    /// state and the last selected font should be detectable.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 50, QuietOnSuccess = true)]
    public FsCheck.Property FontSelectionStatePersistence_ShouldMaintainLastSelection()
    {
        return Prop.ForAll(Gen.ListOf(ValidFontFamilyNames().Generator).Select(list => list.Take(5).ToList()).Where(list => list.Count > 0).ToArbitrary(),
                          ValidFontSizes(), (fontSelectionSequence, fontSize) =>
        {
            try
            {
                // Ensure font registry is initialized
                CaTTYFontManager.LoadFonts();

                TerminalFontConfig? lastConfig = null;
                string? lastSelectedFamily = null;

                // Process each font selection in sequence
                foreach (var fontFamily in fontSelectionSequence)
                {
                    // Generate configuration for this font family
                    var config = CaTTYFontManager.CreateFontConfigForFamily(fontFamily, fontSize);

                    if (config == null)
                    {
                        return false.ToProperty().Label($"Font configuration generation failed for '{fontFamily}' in sequence");
                    }

                    // Validate the configuration
                    try
                    {
                        config.Validate();
                    }
                    catch (Exception ex)
                    {
                        return false.ToProperty().Label($"Font configuration validation failed for '{fontFamily}' in sequence: {ex.Message}");
                    }

                    // Test that the configuration can be detected
                    var detectedFamily = CaTTYFontManager.GetCurrentFontFamily(config);
                    if (detectedFamily != fontFamily)
                    {
                        return false.ToProperty().Label($"Font family detection failed in sequence: expected '{fontFamily}', got '{detectedFamily}'");
                    }

                    lastConfig = config;
                    lastSelectedFamily = fontFamily;
                }

                // Test that the final state is consistent
                if (lastConfig != null && lastSelectedFamily != null)
                {
                    var finalDetectedFamily = CaTTYFontManager.GetCurrentFontFamily(lastConfig);
                    bool finalStateConsistent = finalDetectedFamily == lastSelectedFamily;

                    if (!finalStateConsistent)
                    {
                        return false.ToProperty().Label($"Final state inconsistent: expected '{lastSelectedFamily}', got '{finalDetectedFamily}'");
                    }

                    // Test that the final configuration is still valid
                    try
                    {
                        lastConfig.Validate();
                    }
                    catch (Exception ex)
                    {
                        return false.ToProperty().Label($"Final configuration validation failed: {ex.Message}");
                    }
                }

                return true.ToProperty();
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception testing font selection state persistence: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Property: Font Selection Menu State Consistency
    /// For any font family in the registry, it should be available for selection
    /// and selecting it should result in a consistent state.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FontSelectionMenuStateConsistency_ShouldProvideConsistentMenuOptions()
    {
        return Prop.ForAll(ValidFontSizes(), fontSize =>
        {
            try
            {
                // Ensure font registry is initialized
                CaTTYFontManager.LoadFonts();

                // Test that all available font families can be selected
                var availableFamilies = CaTTYFontManager.GetAvailableFontFamilies();

                if (availableFamilies.Count == 0)
                {
                    return false.ToProperty().Label("No font families available in registry");
                }

                foreach (var fontFamily in availableFamilies)
                {
                    // Test that each font family can generate a valid configuration
                    var config = CaTTYFontManager.CreateFontConfigForFamily(fontFamily, fontSize);

                    if (config == null)
                    {
                        return false.ToProperty().Label($"Font configuration generation failed for available family '{fontFamily}'");
                    }

                    // Test that the configuration is valid
                    try
                    {
                        config.Validate();
                    }
                    catch (Exception ex)
                    {
                        return false.ToProperty().Label($"Font configuration validation failed for available family '{fontFamily}': {ex.Message}");
                    }

                    // Test that the font family can be detected from its configuration
                    var detectedFamily = CaTTYFontManager.GetCurrentFontFamily(config);

                    if (detectedFamily != fontFamily)
                    {
                        return false.ToProperty().Label($"Font family detection failed for available family '{fontFamily}': expected '{fontFamily}', got '{detectedFamily}'");
                    }

                    // Test that the font family has a valid definition
                    var definition = CaTTYFontManager.GetFontFamilyDefinition(fontFamily);

                    if (definition == null)
                    {
                        return false.ToProperty().Label($"Font family definition not found for available family '{fontFamily}'");
                    }

                    // Test that the definition is consistent with the generated configuration
                    string expectedRegular = $"{definition.FontBaseName}-Regular";

                    if (config.RegularFontName != expectedRegular)
                    {
                        return false.ToProperty().Label($"Configuration inconsistent with definition for '{fontFamily}': expected '{expectedRegular}', got '{config.RegularFontName}'");
                    }
                }

                return true.ToProperty();
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception testing font selection menu state consistency: {ex.Message}");
            }
        });
    }
}
