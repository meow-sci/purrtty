using caTTY.Core.Terminal;

namespace caTTY.Display.Configuration;

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
        Console.WriteLine("=== Font Configuration Demonstration ===\n");

        // Create session manager for demonstration
        using var sessionManager = new SessionManager();
        var session = sessionManager.CreateSessionAsync().Result;

        DemonstrateExplicitConfiguration(sessionManager);
        Console.WriteLine();
        
        DemonstrateAutomaticDetection(sessionManager);
        Console.WriteLine();
        
        DemonstrateHybridConfiguration(sessionManager);
        Console.WriteLine();
        
        DemonstrateRuntimeUpdates(sessionManager);
    }

    /// <summary>
    /// Demonstrates explicit font configuration approach.
    /// </summary>
    private static void DemonstrateExplicitConfiguration(SessionManager sessionManager)
    {
        Console.WriteLine("1. EXPLICIT CONFIGURATION (Recommended for Production)");
        Console.WriteLine("   - Predictable behavior across environments");
        Console.WriteLine("   - No runtime detection overhead");
        Console.WriteLine("   - Full control over font selection");
        Console.WriteLine();

        // TestApp explicit configuration
        var testAppConfig = TerminalFontConfig.CreateForTestApp();
        Console.WriteLine($"   TestApp Config: {testAppConfig.RegularFontName}, Size: {testAppConfig.FontSize}");
        
        using var testAppController = new Controllers.TerminalController(sessionManager, testAppConfig);
        Console.WriteLine($"   TestApp Metrics: CharWidth={testAppController.CurrentCharacterWidth:F1}, LineHeight={testAppController.CurrentLineHeight:F1}");

        // GameMod explicit configuration
        var gameModConfig = TerminalFontConfig.CreateForGameMod();
        Console.WriteLine($"   GameMod Config: {gameModConfig.RegularFontName}, Size: {gameModConfig.FontSize}");
        
        using var gameModController = new Controllers.TerminalController(sessionManager, gameModConfig);
        Console.WriteLine($"   GameMod Metrics: CharWidth={gameModController.CurrentCharacterWidth:F1}, LineHeight={gameModController.CurrentLineHeight:F1}");
    }

    /// <summary>
    /// Demonstrates automatic detection approach.
    /// </summary>
    private static void DemonstrateAutomaticDetection(SessionManager sessionManager)
    {
        Console.WriteLine("2. AUTOMATIC DETECTION (Convenient for Development)");
        Console.WriteLine("   - Context-aware font selection");
        Console.WriteLine("   - Minimal configuration required");
        Console.WriteLine("   - Adapts to execution environment");
        Console.WriteLine();

        // Detect current execution context
        var detectedContext = FontContextDetector.DetectExecutionContext();
        Console.WriteLine($"   Detected Context: {detectedContext}");

        // Method 1: Default constructor (implicit automatic detection)
        using var autoController1 = new Controllers.TerminalController(sessionManager);
        Console.WriteLine($"   Auto Config (Implicit): {autoController1.CurrentRegularFontName}, Size: {autoController1.CurrentFontSize}");
        Console.WriteLine($"   Auto Metrics (Implicit): CharWidth={autoController1.CurrentCharacterWidth:F1}, LineHeight={autoController1.CurrentLineHeight:F1}");

        // Method 2: Explicit automatic detection
        var autoConfig = FontContextDetector.DetectAndCreateConfig();
        Console.WriteLine($"   Auto Config (Explicit): {autoConfig.RegularFontName}, Size: {autoConfig.FontSize}");
        
        using var autoController2 = new Controllers.TerminalController(sessionManager, autoConfig);
        Console.WriteLine($"   Auto Metrics (Explicit): CharWidth={autoController2.CurrentCharacterWidth:F1}, LineHeight={autoController2.CurrentLineHeight:F1}");
    }

    /// <summary>
    /// Demonstrates hybrid configuration approach.
    /// </summary>
    private static void DemonstrateHybridConfiguration(SessionManager sessionManager)
    {
        Console.WriteLine("3. HYBRID CONFIGURATION (Advanced Customization)");
        Console.WriteLine("   - Context-aware defaults with custom overrides");
        Console.WriteLine("   - Best of both explicit and automatic approaches");
        Console.WriteLine("   - Flexible for complex requirements");
        Console.WriteLine();

        // Start with automatic detection
        var hybridConfig = FontContextDetector.DetectAndCreateConfig();
        Console.WriteLine($"   Base Auto Config: {hybridConfig.RegularFontName}, Size: {hybridConfig.FontSize}");

        // Apply custom overrides
        hybridConfig.FontSize = 18.0f; // Custom font size
        Console.WriteLine($"   Hybrid Config (Custom Size): {hybridConfig.RegularFontName}, Size: {hybridConfig.FontSize}");

        using var hybridController = new Controllers.TerminalController(sessionManager, hybridConfig);
        Console.WriteLine($"   Hybrid Metrics: CharWidth={hybridController.CurrentCharacterWidth:F1}, LineHeight={hybridController.CurrentLineHeight:F1}");

        // Alternative: Custom font family with auto-detected size
        var hybridConfig2 = FontContextDetector.DetectAndCreateConfig();
        var originalSize = hybridConfig2.FontSize;
        hybridConfig2.RegularFontName = "CustomFont-Regular";
        hybridConfig2.BoldFontName = "CustomFont-Bold";
        Console.WriteLine($"   Hybrid Config (Custom Font): {hybridConfig2.RegularFontName}, Size: {originalSize} (auto-detected)");
    }

    /// <summary>
    /// Demonstrates runtime font configuration updates.
    /// </summary>
    private static void DemonstrateRuntimeUpdates(SessionManager sessionManager)
    {
        Console.WriteLine("4. RUNTIME CONFIGURATION UPDATES");
        Console.WriteLine("   - Dynamic font changes without restart");
        Console.WriteLine("   - Immediate metric recalculation");
        Console.WriteLine("   - Maintains cursor position accuracy");
        Console.WriteLine();

        // Start with automatic detection
        using var controller = new Controllers.TerminalController(sessionManager);
        Console.WriteLine($"   Initial Config: {controller.CurrentRegularFontName}, Size: {controller.CurrentFontSize}");
        Console.WriteLine($"   Initial Metrics: CharWidth={controller.CurrentCharacterWidth:F1}, LineHeight={controller.CurrentLineHeight:F1}");

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

        Console.WriteLine($"   Updating to: {newConfig.RegularFontName}, Size: {newConfig.FontSize}");
        controller.UpdateFontConfig(newConfig);
        
        Console.WriteLine($"   Updated Config: {controller.CurrentRegularFontName}, Size: {controller.CurrentFontSize}");
        Console.WriteLine($"   Updated Metrics: CharWidth={controller.CurrentCharacterWidth:F1}, LineHeight={controller.CurrentLineHeight:F1}");
    }

    /// <summary>
    /// Demonstrates configuration selection based on deployment scenario.
    /// </summary>
    public static void DemonstrateScenarioBasedSelection()
    {
        Console.WriteLine("=== Scenario-Based Configuration Selection ===\n");

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
            Console.WriteLine($"Scenario: {scenario}");
            
            try
            {
                using var controller = FontConfigurationExamples.CreateForDeploymentScenario(sessionManager, scenario);
                // Cast to concrete type to access debugging properties
                if (controller is Controllers.TerminalController terminalController)
                {
                    Console.WriteLine($"  Font: {terminalController.CurrentRegularFontName}");
                    Console.WriteLine($"  Size: {terminalController.CurrentFontSize}");
                    Console.WriteLine($"  Metrics: CharWidth={terminalController.CurrentCharacterWidth:F1}, LineHeight={terminalController.CurrentLineHeight:F1}");
                }
                else
                {
                    Console.WriteLine($"  Controller type: {controller.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error: {ex.Message}");
            }
            
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Demonstrates error handling and fallback behavior.
    /// </summary>
    public static void DemonstrateErrorHandling()
    {
        Console.WriteLine("=== Error Handling and Fallback Behavior ===\n");

        using var sessionManager = new SessionManager();
        var session = sessionManager.CreateSessionAsync().Result;

        // Test invalid font configuration
        Console.WriteLine("1. Invalid Font Configuration:");
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
            Console.WriteLine($"   Validation Error: {ex.Message}");
        }

        // Test fallback behavior
        Console.WriteLine("\n2. Fallback Behavior:");
        using var fallbackController = FontConfigurationExamples.CreateWithDetectionFallback(sessionManager);
        // Cast to concrete type to access debugging properties
        if (fallbackController is Controllers.TerminalController fallbackTerminalController)
        {
            Console.WriteLine($"   Fallback Font: {fallbackTerminalController.CurrentRegularFontName}");
            Console.WriteLine($"   Fallback Size: {fallbackTerminalController.CurrentFontSize}");
        }

        Console.WriteLine("\n3. Runtime Update Error Handling:");
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
            Console.WriteLine($"   Runtime Update Error: {ex.Message}");
            if (fallbackController is Controllers.TerminalController errorTerminalController)
            {
                Console.WriteLine($"   Controller maintains previous config: {errorTerminalController.CurrentRegularFontName}");
            }
        }
    }
}