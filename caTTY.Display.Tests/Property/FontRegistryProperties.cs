using caTTY.Display.Configuration;
using caTTY.Display.Rendering;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Display.Tests.Property;

/// <summary>
/// Property-based tests for font registry completeness and accuracy.
/// Tests universal properties that should hold across all font family definitions.
/// </summary>
[TestFixture]
[Category("Property")]
public class FontRegistryProperties
{
    /// <summary>
    /// Generator for expected font family display names.
    /// Produces the hardcoded font families that should be registered in the system.
    /// </summary>
    public static Arbitrary<string> ExpectedFontFamilyNames()
    {
        var expectedFamilies = new[]
        {
            "Jet Brains Mono",
            "Space Mono",
            "Hack",
            "Pro Font",
            "Proggy Clean",
            "Shure Tech Mono",
            "Departure Mono"
        };

        return Gen.Elements(expectedFamilies).ToArbitrary();
    }

    /// <summary>
    /// Generator for expected font base names.
    /// Produces the technical font base names that should map to display names.
    /// </summary>
    public static Arbitrary<(string DisplayName, string BaseName)> ExpectedFontMappings()
    {
        var expectedMappings = new[]
        {
            ("Jet Brains Mono", "JetBrainsMonoNerdFontMono"),
            ("Space Mono", "SpaceMonoNerdFontMono"),
            ("Hack", "HackNerdFontMono"),
            ("Pro Font", "ProFontWindowsNerdFontMono"),
            ("Proggy Clean", "ProggyCleanNerdFontMono"),
            ("Shure Tech Mono", "ShureTechMonoNerdFontMono"),
            ("Departure Mono", "DepartureMonoNerdFont")
        };

        return Gen.Elements(expectedMappings).ToArbitrary();
    }

    /// <summary>
    /// Generator for fonts with expected variant availability.
    /// Produces font families with their expected variant flags.
    /// </summary>
    public static Arbitrary<(string DisplayName, bool HasAll4Variants)> ExpectedVariantAvailability()
    {
        var expectedVariants = new[]
        {
            ("Jet Brains Mono", true),   // Has all 4 variants
            ("Space Mono", true),        // Has all 4 variants
            ("Hack", true),              // Has all 4 variants
            ("Pro Font", false),         // Regular only
            ("Proggy Clean", false),     // Regular only
            ("Shure Tech Mono", false),  // Regular only
            ("Departure Mono", false)    // Regular only
        };

        return Gen.Elements(expectedVariants).ToArbitrary();
    }

    /// <summary>
    /// Property 1: Font Registry Completeness and Accuracy
    /// For any hardcoded font family in the system, the font registry should contain a complete
    /// and accurate FontFamilyDefinition with correct display name, font base name, and variant
    /// availability flags.
    /// Feature: font-selection-ui, Property 1: Font Registry Completeness and Accuracy
    /// Validates: Requirements 1.1, 1.2, 1.3, 1.4, 5.2, 5.3, 5.4, 9.1-9.7, 10.1-10.7
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FontRegistryCompletenessAndAccuracy_ShouldContainAllExpectedFontFamilies()
    {
        return Prop.ForAll(ExpectedFontFamilyNames(), displayName =>
        {
            try
            {
                // Ensure font registry is initialized by calling LoadFonts
                // This is safe to call multiple times due to the _fontsLoaded guard
                CaTTYFontManager.LoadFonts();

                // Test that the font family is registered
                var availableFamilies = CaTTYFontManager.GetAvailableFontFamilies();
                bool isRegistered = availableFamilies.Contains(displayName);

                if (!isRegistered)
                {
                    return false.ToProperty().Label($"Font family '{displayName}' not found in registry");
                }

                // Test that the font family definition can be retrieved
                var definition = CaTTYFontManager.GetFontFamilyDefinition(displayName);
                bool definitionExists = definition != null;

                if (!definitionExists)
                {
                    return false.ToProperty().Label($"Font family definition for '{displayName}' is null");
                }

                // Test that the definition has correct display name
                bool displayNameCorrect = definition!.DisplayName == displayName;

                if (!displayNameCorrect)
                {
                    return false.ToProperty().Label($"Display name mismatch: expected '{displayName}', got '{definition.DisplayName}'");
                }

                // Test that the definition has a valid font base name
                bool baseNameValid = !string.IsNullOrWhiteSpace(definition.FontBaseName);

                if (!baseNameValid)
                {
                    return false.ToProperty().Label($"Font base name is null or empty for '{displayName}'");
                }

                // Test that HasRegular is always true (requirement)
                bool hasRegularTrue = definition.HasRegular;

                if (!hasRegularTrue)
                {
                    return false.ToProperty().Label($"HasRegular should be true for all fonts, but was false for '{displayName}'");
                }

                // Test that variant flags are consistent with expected values
                var expectedVariants = GetExpectedVariantFlags(displayName);
                bool variantsCorrect = definition.HasBold == expectedVariants.HasBold &&
                                      definition.HasItalic == expectedVariants.HasItalic &&
                                      definition.HasBoldItalic == expectedVariants.HasBoldItalic;

                if (!variantsCorrect)
                {
                    return false.ToProperty().Label($"Variant flags incorrect for '{displayName}': " +
                        $"Expected Bold={expectedVariants.HasBold}, Italic={expectedVariants.HasItalic}, BoldItalic={expectedVariants.HasBoldItalic}; " +
                        $"Got Bold={definition.HasBold}, Italic={definition.HasItalic}, BoldItalic={definition.HasBoldItalic}");
                }

                // Test that ToString() method works correctly
                string toStringResult = definition.ToString();
                bool toStringValid = !string.IsNullOrWhiteSpace(toStringResult) &&
                                    toStringResult.Contains(displayName);

                if (!toStringValid)
                {
                    return false.ToProperty().Label($"ToString() result invalid for '{displayName}': '{toStringResult}'");
                }

                return true.ToProperty();
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception testing font family '{displayName}': {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Property: Font Registry Mapping Accuracy
    /// For any expected font family mapping, the registry should correctly map display names
    /// to technical font base names according to the hardcoded specifications.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FontRegistryMappingAccuracy_ShouldMapDisplayNamesToCorrectBaseNames()
    {
        return Prop.ForAll(ExpectedFontMappings(), mapping =>
        {
            try
            {
                // Ensure font registry is initialized
                CaTTYFontManager.LoadFonts();

                var (displayName, expectedBaseName) = mapping;

                // Test that the mapping exists
                var definition = CaTTYFontManager.GetFontFamilyDefinition(displayName);
                bool definitionExists = definition != null;

                if (!definitionExists)
                {
                    return false.ToProperty().Label($"No definition found for display name '{displayName}'");
                }

                // Test that the base name matches expected value
                bool baseNameCorrect = definition!.FontBaseName == expectedBaseName;

                if (!baseNameCorrect)
                {
                    return false.ToProperty().Label($"Base name mismatch for '{displayName}': expected '{expectedBaseName}', got '{definition.FontBaseName}'");
                }

                // Test that the mapping is bidirectional (can find display name from base name)
                var availableFamilies = CaTTYFontManager.GetAvailableFontFamilies();
                bool displayNameInList = availableFamilies.Any(family =>
                {
                    var def = CaTTYFontManager.GetFontFamilyDefinition(family);
                    return def?.FontBaseName == expectedBaseName && def.DisplayName == displayName;
                });

                if (!displayNameInList)
                {
                    return false.ToProperty().Label($"Cannot find display name '{displayName}' for base name '{expectedBaseName}' in available families");
                }

                return true.ToProperty();
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception testing mapping '{mapping.DisplayName}' -> '{mapping.BaseName}': {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Property: Font Registry Variant Consistency
    /// For any font family with expected variant availability, the registry should correctly
    /// reflect which variants are available according to the hardcoded specifications.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FontRegistryVariantConsistency_ShouldReflectCorrectVariantAvailability()
    {
        return Prop.ForAll(ExpectedVariantAvailability(), variantInfo =>
        {
            try
            {
                // Ensure font registry is initialized
                CaTTYFontManager.LoadFonts();

                var (displayName, hasAll4Variants) = variantInfo;

                // Test that the font family exists
                var definition = CaTTYFontManager.GetFontFamilyDefinition(displayName);
                bool definitionExists = definition != null;

                if (!definitionExists)
                {
                    return false.ToProperty().Label($"No definition found for font family '{displayName}'");
                }

                // Test variant availability matches expectations
                if (hasAll4Variants)
                {
                    // Should have all 4 variants
                    bool allVariantsAvailable = definition!.HasRegular && definition.HasBold &&
                                               definition.HasItalic && definition.HasBoldItalic;

                    if (!allVariantsAvailable)
                    {
                        return false.ToProperty().Label($"Font '{displayName}' should have all 4 variants, but has: " +
                            $"Regular={definition.HasRegular}, Bold={definition.HasBold}, " +
                            $"Italic={definition.HasItalic}, BoldItalic={definition.HasBoldItalic}");
                    }
                }
                else
                {
                    // Should have only Regular variant
                    bool onlyRegularAvailable = definition!.HasRegular && !definition.HasBold &&
                                               !definition.HasItalic && !definition.HasBoldItalic;

                    if (!onlyRegularAvailable)
                    {
                        return false.ToProperty().Label($"Font '{displayName}' should have only Regular variant, but has: " +
                            $"Regular={definition.HasRegular}, Bold={definition.HasBold}, " +
                            $"Italic={definition.HasItalic}, BoldItalic={definition.HasBoldItalic}");
                    }
                }

                // Test that ToString() reflects variant availability correctly
                string toStringResult = definition!.ToString();
                bool toStringReflectsVariants = true;

                if (hasAll4Variants)
                {
                    toStringReflectsVariants = toStringResult.Contains("Regular") &&
                                              toStringResult.Contains("Bold") &&
                                              toStringResult.Contains("Italic") &&
                                              toStringResult.Contains("BoldItalic");
                }
                else
                {
                    toStringReflectsVariants = toStringResult.Contains("Regular") &&
                                              !toStringResult.Contains("Bold") &&
                                              !toStringResult.Contains("Italic") &&
                                              !toStringResult.Contains("BoldItalic");
                }

                if (!toStringReflectsVariants)
                {
                    return false.ToProperty().Label($"ToString() doesn't reflect correct variants for '{displayName}': '{toStringResult}'");
                }

                return true.ToProperty();
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception testing variant consistency for '{variantInfo.DisplayName}': {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Property: Font Registry Completeness
    /// The font registry should contain exactly the expected number of font families
    /// and no unexpected entries.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 10, QuietOnSuccess = true)]
    public FsCheck.Property FontRegistryCompleteness_ShouldContainExactlyExpectedFamilies()
    {
        return Prop.ForAll<bool>(Gen.Constant(true).ToArbitrary(), _ =>
        {
            try
            {
                // Ensure font registry is initialized
                CaTTYFontManager.LoadFonts();

                var availableFamilies = CaTTYFontManager.GetAvailableFontFamilies();
                var expectedFamilies = new[]
                {
                    "Jet Brains Mono", "Space Mono", "Hack", "Pro Font",
                    "Proggy Clean", "Shure Tech Mono", "Departure Mono"
                };

                // Test that we have exactly the expected number of families
                bool correctCount = availableFamilies.Count == expectedFamilies.Length;

                if (!correctCount)
                {
                    return false.ToProperty().Label($"Expected {expectedFamilies.Length} font families, but found {availableFamilies.Count}");
                }

                // Test that all expected families are present
                foreach (var expectedFamily in expectedFamilies)
                {
                    bool isPresent = availableFamilies.Contains(expectedFamily);
                    if (!isPresent)
                    {
                        return false.ToProperty().Label($"Expected font family '{expectedFamily}' not found in registry");
                    }
                }

                // Test that no unexpected families are present
                foreach (var actualFamily in availableFamilies)
                {
                    bool isExpected = expectedFamilies.Contains(actualFamily);
                    if (!isExpected)
                    {
                        return false.ToProperty().Label($"Unexpected font family '{actualFamily}' found in registry");
                    }
                }

                // Test that all families have valid definitions
                foreach (var family in availableFamilies)
                {
                    var definition = CaTTYFontManager.GetFontFamilyDefinition(family);
                    bool hasValidDefinition = definition != null &&
                                             !string.IsNullOrWhiteSpace(definition.DisplayName) &&
                                             !string.IsNullOrWhiteSpace(definition.FontBaseName) &&
                                             definition.HasRegular; // All fonts should have Regular

                    if (!hasValidDefinition)
                    {
                        return false.ToProperty().Label($"Font family '{family}' has invalid definition");
                    }
                }

                return true.ToProperty();
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception testing registry completeness: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Helper method to get expected variant flags for a given display name.
    /// </summary>
    private static (bool HasBold, bool HasItalic, bool HasBoldItalic) GetExpectedVariantFlags(string displayName)
    {
        return displayName switch
        {
            "Jet Brains Mono" => (true, true, true),
            "Space Mono" => (true, true, true),
            "Hack" => (true, true, true),
            "Pro Font" => (false, false, false),
            "Proggy Clean" => (false, false, false),
            "Shure Tech Mono" => (false, false, false),
            "Departure Mono" => (false, false, false),
            _ => (false, false, false) // Default for unknown fonts
        };
    }

    /// <summary>
    /// Generator for valid font sizes.
    /// Produces font sizes within the valid range for terminal fonts.
    /// </summary>
    public static Arbitrary<float> ValidFontSizes()
    {
        return Gen.Choose(8, 72).Select(i => (float)i).ToArbitrary();
    }

    /// <summary>
    /// Property 2: Font Configuration Generation with Variant Fallback
    /// For any font family selection, the system should generate a TerminalFontConfig where
    /// fonts with all variants use appropriate variant names, and fonts with only Regular
    /// variant use Regular for all styles (Bold, Italic, BoldItalic).
    /// Feature: font-selection-ui, Property 2: Font Configuration Generation with Variant Fallback
    /// Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FontConfigurationGenerationWithVariantFallback_ShouldCreateCorrectConfigurations()
    {
        return Prop.ForAll(ExpectedFontFamilyNames(), ValidFontSizes(), (displayName, fontSize) =>
        {
            try
            {
                // Ensure font registry is initialized
                CaTTYFontManager.LoadFonts();

                // Test that CreateFontConfigForFamily works for valid font families
                var config = CaTTYFontManager.CreateFontConfigForFamily(displayName, fontSize);
                bool configCreated = config != null;

                if (!configCreated)
                {
                    return false.ToProperty().Label($"CreateFontConfigForFamily returned null for '{displayName}'");
                }

                // Test that the configuration is valid
                try
                {
                    config!.Validate();
                }
                catch (Exception ex)
                {
                    return false.ToProperty().Label($"Generated configuration for '{displayName}' failed validation: {ex.Message}");
                }

                // Test that font size is set correctly
                bool fontSizeCorrect = Math.Abs(config!.FontSize - fontSize) < 0.001f;

                if (!fontSizeCorrect)
                {
                    return false.ToProperty().Label($"Font size mismatch for '{displayName}': expected {fontSize}, got {config.FontSize}");
                }

                // Test that AutoDetectContext is disabled
                bool autoDetectDisabled = !config.AutoDetectContext;

                if (!autoDetectDisabled)
                {
                    return false.ToProperty().Label($"AutoDetectContext should be false for generated configs, but was true for '{displayName}'");
                }

                // Get the font family definition to check variant availability
                var definition = CaTTYFontManager.GetFontFamilyDefinition(displayName);
                if (definition == null)
                {
                    return false.ToProperty().Label($"Font family definition not found for '{displayName}'");
                }

                // Test that Regular font name is always set correctly
                string expectedRegular = $"{definition.FontBaseName}-Regular";
                bool regularCorrect = config.RegularFontName == expectedRegular;

                if (!regularCorrect)
                {
                    return false.ToProperty().Label($"Regular font name incorrect for '{displayName}': expected '{expectedRegular}', got '{config.RegularFontName}'");
                }

                // Test variant fallback logic
                string expectedBold = definition.HasBold ? $"{definition.FontBaseName}-Bold" : expectedRegular;
                string expectedItalic = definition.HasItalic ? $"{definition.FontBaseName}-Italic" : expectedRegular;
                string expectedBoldItalic = definition.HasBoldItalic ? $"{definition.FontBaseName}-BoldItalic" : expectedRegular;

                bool boldCorrect = config.BoldFontName == expectedBold;
                bool italicCorrect = config.ItalicFontName == expectedItalic;
                bool boldItalicCorrect = config.BoldItalicFontName == expectedBoldItalic;

                if (!boldCorrect)
                {
                    return false.ToProperty().Label($"Bold font name incorrect for '{displayName}': expected '{expectedBold}', got '{config.BoldFontName}'");
                }

                if (!italicCorrect)
                {
                    return false.ToProperty().Label($"Italic font name incorrect for '{displayName}': expected '{expectedItalic}', got '{config.ItalicFontName}'");
                }

                if (!boldItalicCorrect)
                {
                    return false.ToProperty().Label($"BoldItalic font name incorrect for '{displayName}': expected '{expectedBoldItalic}', got '{config.BoldItalicFontName}'");
                }

                // Test that fonts with only Regular variant use Regular for all styles
                if (!definition.HasBold && !definition.HasItalic && !definition.HasBoldItalic)
                {
                    bool allUseRegular = config.BoldFontName == expectedRegular &&
                                        config.ItalicFontName == expectedRegular &&
                                        config.BoldItalicFontName == expectedRegular;

                    if (!allUseRegular)
                    {
                        return false.ToProperty().Label($"Font '{displayName}' with only Regular variant should use Regular for all styles");
                    }
                }

                // Test that fonts with all variants use appropriate variant names
                if (definition.HasBold && definition.HasItalic && definition.HasBoldItalic)
                {
                    bool allVariantsUsed = config.BoldFontName.EndsWith("-Bold") &&
                                          config.ItalicFontName.EndsWith("-Italic") &&
                                          config.BoldItalicFontName.EndsWith("-BoldItalic");

                    if (!allVariantsUsed)
                    {
                        return false.ToProperty().Label($"Font '{displayName}' with all variants should use appropriate variant names");
                    }
                }

                return true.ToProperty();
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception testing font configuration generation for '{displayName}': {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Property: Font Configuration Generation Error Handling
    /// For any invalid font family name, CreateFontConfigForFamily should return a default
    /// configuration without throwing exceptions.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FontConfigurationGenerationErrorHandling_ShouldHandleInvalidFontFamilies()
    {
        return Prop.ForAll(Gen.Elements("", "NonExistentFont", "Invalid Font Name", null!).ToArbitrary(),
                          ValidFontSizes(), (invalidDisplayName, fontSize) =>
        {
            try
            {
                // Ensure font registry is initialized
                CaTTYFontManager.LoadFonts();

                // Test that CreateFontConfigForFamily handles invalid names gracefully
                var config = CaTTYFontManager.CreateFontConfigForFamily(invalidDisplayName, fontSize);
                bool configCreated = config != null;

                if (!configCreated)
                {
                    return false.ToProperty().Label($"CreateFontConfigForFamily returned null for invalid name '{invalidDisplayName}'");
                }

                // Test that the returned configuration is valid (should be default)
                try
                {
                    config!.Validate();
                }
                catch (Exception ex)
                {
                    return false.ToProperty().Label($"Default configuration for invalid name '{invalidDisplayName}' failed validation: {ex.Message}");
                }

                // Test that the configuration uses default font names (from CreateForTestApp)
                var defaultConfig = TerminalFontConfig.CreateForTestApp();
                bool usesDefaultFonts = config!.RegularFontName == defaultConfig.RegularFontName &&
                                       config.BoldFontName == defaultConfig.BoldFontName &&
                                       config.ItalicFontName == defaultConfig.ItalicFontName &&
                                       config.BoldItalicFontName == defaultConfig.BoldItalicFontName;

                if (!usesDefaultFonts)
                {
                    return false.ToProperty().Label($"Configuration for invalid name '{invalidDisplayName}' should use default font names");
                }

                return true.ToProperty();
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception testing error handling for invalid font name '{invalidDisplayName}': {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Generator for TerminalFontConfig instances based on registered font families.
    /// Produces configurations that should be detectable by GetCurrentFontFamily.
    /// </summary>
    public static Arbitrary<TerminalFontConfig> ValidTerminalFontConfigs()
    {
        return Gen.Elements(new[]
        {
            "Jet Brains Mono", "Space Mono", "Hack", "Pro Font",
            "Proggy Clean", "Shure Tech Mono", "Departure Mono"
        }).SelectMany(displayName =>
            ValidFontSizes().Generator.Select(fontSize =>
                CaTTYFontManager.CreateFontConfigForFamily(displayName, fontSize)
            )
        ).ToArbitrary();
    }

    /// <summary>
    /// Generator for TerminalFontConfig instances that should NOT be detectable.
    /// Produces configurations with font names that don't match any registered family.
    /// </summary>
    public static Arbitrary<TerminalFontConfig> UndetectableTerminalFontConfigs()
    {
        var unknownFontNames = new[]
        {
            "UnknownFont-Regular",
            "NonExistentFont-Regular",
            "CustomFont-Regular",
            "Arial-Regular",
            "Times-Regular"
        };

        return Gen.Elements(unknownFontNames).SelectMany(fontName =>
            ValidFontSizes().Generator.Select(fontSize =>
                new TerminalFontConfig
                {
                    RegularFontName = fontName,
                    BoldFontName = fontName.Replace("-Regular", "-Bold"),
                    ItalicFontName = fontName.Replace("-Regular", "-Italic"),
                    BoldItalicFontName = fontName.Replace("-Regular", "-BoldItalic"),
                    FontSize = fontSize,
                    AutoDetectContext = false
                }
            )
        ).ToArbitrary();
    }

    /// <summary>
    /// Property 4: Current Font Family Detection
    /// For any existing TerminalFontConfig, the system should correctly identify which
    /// registered font family it corresponds to by matching the RegularFontName against
    /// registered font base names, or return null if no match is found.
    /// Feature: font-selection-ui, Property 4: Current Font Family Detection
    /// Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CurrentFontFamilyDetection_ShouldCorrectlyIdentifyFontFamilies()
    {
        return Prop.ForAll(ValidTerminalFontConfigs(), config =>
        {
            try
            {
                // Ensure font registry is initialized
                CaTTYFontManager.LoadFonts();

                // Test that GetCurrentFontFamily works for valid configurations
                var detectedFamily = CaTTYFontManager.GetCurrentFontFamily(config);
                bool familyDetected = detectedFamily != null;

                if (!familyDetected)
                {
                    return false.ToProperty().Label($"GetCurrentFontFamily returned null for valid config with RegularFontName: {config.RegularFontName}");
                }

                // Test that the detected family is in the registry
                var availableFamilies = CaTTYFontManager.GetAvailableFontFamilies();
                bool familyInRegistry = availableFamilies.Contains(detectedFamily!);

                if (!familyInRegistry)
                {
                    return false.ToProperty().Label($"Detected family '{detectedFamily}' is not in the registry");
                }

                // Test that the detected family can generate the same configuration
                var regeneratedConfig = CaTTYFontManager.CreateFontConfigForFamily(detectedFamily!, config.FontSize);
                bool configMatches = regeneratedConfig.RegularFontName == config.RegularFontName;

                if (!configMatches)
                {
                    return false.ToProperty().Label($"Regenerated config doesn't match original: expected '{config.RegularFontName}', got '{regeneratedConfig.RegularFontName}'");
                }

                // Test that the detection is consistent (calling again returns same result)
                var detectedFamilyAgain = CaTTYFontManager.GetCurrentFontFamily(config);
                bool detectionConsistent = detectedFamily == detectedFamilyAgain;

                if (!detectionConsistent)
                {
                    return false.ToProperty().Label($"Font family detection inconsistent: first call returned '{detectedFamily}', second call returned '{detectedFamilyAgain}'");
                }

                // Test that the detected family has the correct font base name
                var definition = CaTTYFontManager.GetFontFamilyDefinition(detectedFamily!);
                if (definition == null)
                {
                    return false.ToProperty().Label($"No definition found for detected family '{detectedFamily}'");
                }

                string expectedRegular = $"{definition.FontBaseName}-Regular";
                bool baseNameMatches = config.RegularFontName == expectedRegular;

                if (!baseNameMatches)
                {
                    return false.ToProperty().Label($"Base name mismatch: config has '{config.RegularFontName}', expected '{expectedRegular}' for family '{detectedFamily}'");
                }

                return true.ToProperty();
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception testing font family detection for config with RegularFontName '{config.RegularFontName}': {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Property: Current Font Family Detection Null Handling
    /// For any TerminalFontConfig that doesn't match registered font families,
    /// GetCurrentFontFamily should return null without throwing exceptions.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CurrentFontFamilyDetectionNullHandling_ShouldReturnNullForUnknownFonts()
    {
        return Prop.ForAll(UndetectableTerminalFontConfigs(), config =>
        {
            try
            {
                // Ensure font registry is initialized
                CaTTYFontManager.LoadFonts();

                // Test that GetCurrentFontFamily returns null for unknown fonts
                var detectedFamily = CaTTYFontManager.GetCurrentFontFamily(config);
                bool returnedNull = detectedFamily == null;

                if (!returnedNull)
                {
                    return false.ToProperty().Label($"GetCurrentFontFamily should return null for unknown font '{config.RegularFontName}', but returned '{detectedFamily}'");
                }

                // Test that calling multiple times is consistent
                var detectedFamilyAgain = CaTTYFontManager.GetCurrentFontFamily(config);
                bool consistentlyNull = detectedFamilyAgain == null;

                if (!consistentlyNull)
                {
                    return false.ToProperty().Label($"GetCurrentFontFamily inconsistent: first call returned null, second call returned '{detectedFamilyAgain}'");
                }

                return true.ToProperty();
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception testing null handling for unknown font '{config.RegularFontName}': {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Property: Current Font Family Detection Error Handling
    /// For any invalid TerminalFontConfig (null or invalid), GetCurrentFontFamily
    /// should handle errors gracefully and return null.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 50, QuietOnSuccess = true)]
    public FsCheck.Property CurrentFontFamilyDetectionErrorHandling_ShouldHandleInvalidConfigs()
    {
        return Prop.ForAll<bool>(Gen.Constant(true).ToArbitrary(), _ =>
        {
            try
            {
                // Ensure font registry is initialized
                CaTTYFontManager.LoadFonts();

                // Test null config
                var detectedFamilyNull = CaTTYFontManager.GetCurrentFontFamily(null!);
                bool nullConfigHandled = detectedFamilyNull == null;

                if (!nullConfigHandled)
                {
                    return false.ToProperty().Label($"GetCurrentFontFamily should return null for null config, but returned '{detectedFamilyNull}'");
                }

                // Test config with null RegularFontName
                var configWithNullFont = new TerminalFontConfig
                {
                    RegularFontName = null!,
                    BoldFontName = "SomeFont-Bold",
                    ItalicFontName = "SomeFont-Italic",
                    BoldItalicFontName = "SomeFont-BoldItalic",
                    FontSize = 32.0f,
                    AutoDetectContext = false
                };

                var detectedFamilyNullFont = CaTTYFontManager.GetCurrentFontFamily(configWithNullFont);
                bool nullFontHandled = detectedFamilyNullFont == null;

                if (!nullFontHandled)
                {
                    return false.ToProperty().Label($"GetCurrentFontFamily should return null for config with null RegularFontName, but returned '{detectedFamilyNullFont}'");
                }

                // Test config with empty RegularFontName
                var configWithEmptyFont = new TerminalFontConfig
                {
                    RegularFontName = "",
                    BoldFontName = "SomeFont-Bold",
                    ItalicFontName = "SomeFont-Italic",
                    BoldItalicFontName = "SomeFont-BoldItalic",
                    FontSize = 32.0f,
                    AutoDetectContext = false
                };

                var detectedFamilyEmptyFont = CaTTYFontManager.GetCurrentFontFamily(configWithEmptyFont);
                bool emptyFontHandled = detectedFamilyEmptyFont == null;

                if (!emptyFontHandled)
                {
                    return false.ToProperty().Label($"GetCurrentFontFamily should return null for config with empty RegularFontName, but returned '{detectedFamilyEmptyFont}'");
                }

                return true.ToProperty();
            }
            catch (Exception ex)
            {
                return false.ToProperty().Label($"Exception testing error handling for invalid configs: {ex.Message}");
            }
        });
    }
}
