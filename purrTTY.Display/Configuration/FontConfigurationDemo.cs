using purrTTY.Core.Terminal;
using purrTTY.Logging;

namespace purrTTY.Display.Configuration;

/// <summary>
/// Demonstration program showing the three different font configuration approaches.
/// This class provides a simple way to test and compare different configuration methods.
/// </summary>
public static class FontConfigurationDemo
{
    /// <summary>
    /// Demonstrates all three font configuration approaches with detailed logging.
    /// This method shows the differences between explicit, automatic, and hybrid configuration.
    /// </summary>
    public static void DemonstrateAllApproaches()
    {
        ModLog.Log.Debug("=== Font Configuration Demonstration ===\n");

        // Create session manager for demonstration
        using var sessionManager = new SessionManager();
        var session = sessionManager.CreateSessionAsync().Result;

        DemonstrateExplicitConfiguration(sessionManager);
        ModLog.Log.Debug("");
        
        DemonstrateAutomaticDetection(sessionManager);
        ModLog.Log.Debug("");
        
        DemonstrateHybridConfiguration(sessionManager);
        ModLog.Log.Debug("");
        
        DemonstrateRuntimeUpdates(sessionManager);
    }

    /// <summary>
    /// Demonstrates explicit font configuration approach.
    /// </summary>
    private static void DemonstrateExplicitConfiguration(SessionManager sessionManager)
    {
        ModLog.Log.Debug("1. EXPLICIT CONFIGURATION (Recommended for Production)");
        ModLog.Log.Debug("   - Predictable behavior across environments");
        ModLog.Log.Debug("   - No runtime detection overhead");
        ModLog.Log.Debug("   - Full control over font selection");
        ModLog.Log.Debug("");

        // TestApp explicit configuration
        var testAppConfig = TerminalFontConfig.CreateForTestApp();
        ModLog.Log.Debug($"   TestApp Config: {testAppConfig.RegularFontName}, Size: {testAppConfig.FontSize}");
        
        using var testAppController = new Controllers.TerminalController(sessionManager, testAppConfig);
        ModLog.Log.Debug($"   TestApp Metrics: CharWidth={testAppController.CurrentCharacterWidth:F1}, LineHeight={testAppController.CurrentLineHeight:F1}");

        // GameMod explicit configuration
        var gameModConfig = TerminalFontConfig.CreateForGameMod();
        ModLog.Log.Debug($"   GameMod Config: {gameModConfig.RegularFontName}, Size: {gameModConfig.FontSize}");
        
        using var gameModController = new Controllers.TerminalController(sessionManager, gameModConfig);
        ModLog.Log.Debug($"   GameMod Metrics: CharWidth={gameModController.CurrentCharacterWidth:F1}, LineHeight={gameModController.CurrentLineHeight:F1}");
    }

    /// <summary>
    /// Demonstrates automatic detection approach.
    /// </summary>
    private static void DemonstrateAutomaticDetection(SessionManager sessionManager)
    {
        ModLog.Log.Debug("2. AUTOMATIC DETECTION (Convenient for Development)");
        ModLog.Log.Debug("   - Context-aware font selection");
        ModLog.Log.Debug("   - Minimal configuration required");
        ModLog.Log.Debug("   - Adapts to execution environment");
        ModLog.Log.Debug("");

        // Detect current execution context
        var detectedContext = FontContextDetector.DetectExecutionContext();
        ModLog.Log.Debug($"   Detected Context: {detectedContext}");

        // Method 1: Default constructor (implicit automatic detection)
        using var autoController1 = new Controllers.TerminalController(sessionManager);
        ModLog.Log.Debug($"   Auto Config (Implicit): {autoController1.CurrentRegularFontName}, Size: {autoController1.CurrentFontSize}");
        ModLog.Log.Debug($"   Auto Metrics (Implicit): CharWidth={autoController1.CurrentCharacterWidth:F1}, LineHeight={autoController1.CurrentLineHeight:F1}");

        // Method 2: Explicit automatic detection
        var autoConfig = FontContextDetector.DetectAndCreateConfig();
        ModLog.Log.Debug($"   Auto Config (Explicit): {autoConfig.RegularFontName}, Size: {autoConfig.FontSize}");
        
        using var autoController2 = new Controllers.TerminalController(sessionManager, autoConfig);
        ModLog.Log.Debug($"   Auto Metrics (Explicit): CharWidth={autoController2.CurrentCharacterWidth:F1}, LineHeight={autoController2.CurrentLineHeight:F1}");
    }

    /// <summary>
    /// Demonstrates hybrid configuration approach.
    /// </summary>
    private static void DemonstrateHybridConfiguration(SessionManager sessionManager)
    {
        ModLog.Log.Debug("3. HYBRID CONFIGURATION (Advanced Customization)");
        ModLog.Log.Debug("   - Context-aware defaults with custom overrides");
        ModLog.Log.Debug("   - Best of both explicit and automatic approaches");
        ModLog.Log.Debug("   - Flexible for complex requirements");
        ModLog.Log.Debug("");

        // Start with automatic detection
        var hybridConfig = FontContextDetector.DetectAndCreateConfig();
        ModLog.Log.Debug($"   Base Auto Config: {hybridConfig.RegularFontName}, Size: {hybridConfig.FontSize}");

        // Apply custom overrides
        hybridConfig.FontSize = 18.0f; // Custom font size
        ModLog.Log.Debug($"   Hybrid Config (Custom Size): {hybridConfig.RegularFontName}, Size: {hybridConfig.FontSize}");

        using var hybridController = new Controllers.TerminalController(sessionManager, hybridConfig);
        ModLog.Log.Debug($"   Hybrid Metrics: CharWidth={hybridController.CurrentCharacterWidth:F1}, LineHeight={hybridController.CurrentLineHeight:F1}");

        // Alternative: Custom font family with auto-detected size
        var hybridConfig2 = FontContextDetector.DetectAndCreateConfig();
        var originalSize = hybridConfig2.FontSize;
        hybridConfig2.RegularFontName = "CustomFont-Regular";
        hybridConfig2.BoldFontName = "CustomFont-Bold";
        ModLog.Log.Debug($"   Hybrid Config (Custom Font): {hybridConfig2.RegularFontName}, Size: {originalSize} (auto-detected)");
    }

    /// <summary>
    /// Demonstrates runtime font configuration updates.
    /// </summary>
    private static void DemonstrateRuntimeUpdates(SessionManager sessionManager)
    {
        ModLog.Log.Debug("4. RUNTIME CONFIGURATION UPDATES");
        ModLog.Log.Debug("   - Dynamic font changes without restart");
        ModLog.Log.Debug("   - Immediate metric recalculation");
        ModLog.Log.Debug("   - Maintains cursor position accuracy");
        ModLog.Log.Debug("");

        // Start with automatic detection
        using var controller = new Controllers.TerminalController(sessionManager);
        ModLog.Log.Debug($"   Initial Config: {controller.CurrentRegularFontName}, Size: {controller.CurrentFontSize}");
        ModLog.Log.Debug($"   Initial Metrics: CharWidth={controller.CurrentCharacterWidth:F1}, LineHeight={controller.CurrentLineHeight:F1}");

        // Update to different configuration
        var newConfig = new TerminalFontConfig
        {
            RegularFontName = "HackNerdFontMono-Regular",
            BoldFontName = "HackNerdFontMono-Bold",
            ItalicFontName = "HackNerdFontMono-Italic",
            BoldItalicFontName = "HackNerdFontMono-BoldItalic",
            FontSize = 20.0f,
            AutoDetectContext = false
        };

        ModLog.Log.Debug($"   Updating to: {newConfig.RegularFontName}, Size: {newConfig.FontSize}");
        controller.UpdateFontConfig(newConfig);
        
        ModLog.Log.Debug($"   Updated Config: {controller.CurrentRegularFontName}, Size: {controller.CurrentFontSize}");
        ModLog.Log.Debug($"   Updated Metrics: CharWidth={controller.CurrentCharacterWidth:F1}, LineHeight={controller.CurrentLineHeight:F1}");
    }

    /// <summary>
    /// Demonstrates configuration selection based on deployment scenario.
    /// </summary>
    public static void DemonstrateScenarioBasedSelection()
    {
        ModLog.Log.Debug("=== Scenario-Based Configuration Selection ===\n");

        using var sessionManager = new SessionManager();
        var session = sessionManager.CreateSessionAsync().Result;

        var scenarios = new[]
        {
            DeploymentScenario.Development,
            DeploymentScenario.TestAppProduction,
            DeploymentScenario.GameModProduction,
            DeploymentScenario.CustomIntegration
        };

        foreach (var scenario in scenarios)
        {
            ModLog.Log.Debug($"Scenario: {scenario}");
            
            try
            {
                using var controller = FontConfigurationExamples.CreateForDeploymentScenario(sessionManager, scenario);
                // Cast to concrete type to access debugging properties
                if (controller is Controllers.TerminalController terminalController)
                {
                    ModLog.Log.Debug($"  Font: {terminalController.CurrentRegularFontName}");
                    ModLog.Log.Debug($"  Size: {terminalController.CurrentFontSize}");
                    ModLog.Log.Debug($"  Metrics: CharWidth={terminalController.CurrentCharacterWidth:F1}, LineHeight={terminalController.CurrentLineHeight:F1}");
                }
                else
                {
                    ModLog.Log.Debug($"  Controller type: {controller.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                ModLog.Log.Debug($"  Error: {ex.Message}");
            }
            
            ModLog.Log.Debug("");
        }
    }

    /// <summary>
    /// Demonstrates error handling and fallback behavior.
    /// </summary>
    public static void DemonstrateErrorHandling()
    {
        ModLog.Log.Debug("=== Error Handling and Fallback Behavior ===\n");

        using var sessionManager = new SessionManager();
        var session = sessionManager.CreateSessionAsync().Result;

        // Test invalid font configuration
        ModLog.Log.Debug("1. Invalid Font Configuration:");
        try
        {
            var invalidConfig = new TerminalFontConfig
            {
                RegularFontName = "", // Invalid: empty font name
                FontSize = 100.0f     // Invalid: font size too large
            };
            
            invalidConfig.Validate();
        }
        catch (ArgumentException ex)
        {
            ModLog.Log.Debug($"   Validation Error: {ex.Message}");
        }

        // Test fallback behavior
        ModLog.Log.Debug("\n2. Fallback Behavior:");
        using var fallbackController = FontConfigurationExamples.CreateWithDetectionFallback(sessionManager);
        // Cast to concrete type to access debugging properties
        if (fallbackController is Controllers.TerminalController fallbackTerminalController)
        {
            ModLog.Log.Debug($"   Fallback Font: {fallbackTerminalController.CurrentRegularFontName}");
            ModLog.Log.Debug($"   Fallback Size: {fallbackTerminalController.CurrentFontSize}");
        }

        ModLog.Log.Debug("\n3. Runtime Update Error Handling:");
        try
        {
            var invalidRuntimeConfig = new TerminalFontConfig
            {
                RegularFontName = null!, // This will cause validation error
                FontSize = 16.0f
            };
            
            // Cast to concrete type to access UpdateFontConfig method
            if (fallbackController is Controllers.TerminalController concreteController)
            {
                concreteController.UpdateFontConfig(invalidRuntimeConfig);
            }
        }
        catch (ArgumentException ex)
        {
            ModLog.Log.Debug($"   Runtime Update Error: {ex.Message}");
            if (fallbackController is Controllers.TerminalController errorTerminalController)
            {
                ModLog.Log.Debug($"   Controller maintains previous config: {errorTerminalController.CurrentRegularFontName}");
            }
        }
    }
}