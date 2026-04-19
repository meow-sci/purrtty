using caTTY.Core.Terminal;
using caTTY.Display.Controllers;

namespace caTTY.Display.Configuration;

/// <summary>
/// Example code demonstrating different font configuration approaches for the caTTY terminal emulator.
/// These examples show when and how to use explicit configuration, automatic detection, and hybrid approaches.
/// </summary>
public static class FontConfigurationExamples
{
    /// <summary>
    /// Example 1: Explicit TestApp Configuration (Recommended for Production)
    /// Use this approach when you need predictable, consistent font behavior.
    /// </summary>
    public static ITerminalController CreateTestAppWithExplicitConfig(
        SessionManager sessionManager)
    {
        // Create explicit font configuration optimized for TestApp development
        var fontConfig = TerminalFontConfig.CreateForTestApp();
        
        // This ensures consistent font behavior regardless of execution context
        return new TerminalController(sessionManager, fontConfig);
    }

    /// <summary>
    /// Example 2: Explicit GameMod Configuration (Recommended for Production)
    /// Use this approach when deploying to game mod context with specific requirements.
    /// </summary>
    public static ITerminalController CreateGameModWithExplicitConfig(
        SessionManager sessionManager)
    {
        // Create explicit font configuration optimized for GameMod integration
        var fontConfig = TerminalFontConfig.CreateForGameMod();
        
        // This ensures game-appropriate fonts and sizing
        return new TerminalController(sessionManager, fontConfig);
    }

    /// <summary>
    /// Example 3: Automatic Detection (Convenient for Development)
    /// Use this approach when you want the system to automatically choose appropriate fonts
    /// based on the execution context (TestApp vs GameMod).
    /// </summary>
    public static ITerminalController CreateWithAutomaticDetection(
        SessionManager sessionManager)
    {
        // The default constructor automatically detects context and applies appropriate font configuration
        // This is equivalent to: FontContextDetector.DetectAndCreateConfig()
        return new TerminalController(sessionManager);
    }

    /// <summary>
    /// Example 4: Explicit Automatic Detection
    /// Use this approach when you want to explicitly show that you're using automatic detection,
    /// or when you need to inspect the detected configuration before using it.
    /// </summary>
    public static ITerminalController CreateWithExplicitAutomaticDetection(
        SessionManager sessionManager)
    {
        // Explicitly request automatic detection and configuration creation
        var fontConfig = FontContextDetector.DetectAndCreateConfig();
        
        // You can inspect or log the detected configuration here if needed
        Console.WriteLine($"Detected context configuration: {fontConfig.RegularFontName}, Size: {fontConfig.FontSize}");
        
        return new TerminalController(sessionManager, fontConfig);
    }

    /// <summary>
    /// Example 5: Custom Font Configuration
    /// Use this approach when you need specific fonts or settings not covered by the defaults.
    /// </summary>
    public static ITerminalController CreateWithCustomConfig(
        SessionManager sessionManager)
    {
        // Create completely custom font configuration
        var fontConfig = new TerminalFontConfig
        {
            RegularFontName = "JetBrainsMono-Regular",
            BoldFontName = "JetBrainsMono-Bold", 
            ItalicFontName = "JetBrainsMono-Italic",
            BoldItalicFontName = "JetBrainsMono-BoldItalic",
            FontSize = 18.0f,
            AutoDetectContext = false // Disable automatic detection since we're explicit
        };
        
        return new TerminalController(sessionManager, fontConfig);
    }

    /// <summary>
    /// Example 6: Hybrid Configuration (Advanced)
    /// Use this approach when you want context-aware defaults with specific customizations.
    /// </summary>
    public static ITerminalController CreateWithHybridConfig(
        SessionManager sessionManager)
    {
        // Start with automatic detection to get context-appropriate defaults
        var fontConfig = FontContextDetector.DetectAndCreateConfig();
        
        // Then customize specific aspects as needed
        fontConfig.FontSize = 20.0f; // Override font size while keeping context-appropriate font family
        
        // You could also override font family while keeping context-appropriate size:
        // fontConfig.RegularFontName = "MyPreferredFont-Regular";
        
        return new TerminalController(sessionManager, fontConfig);
    }

    /// <summary>
    /// Example 7: Runtime Font Configuration Updates
    /// Demonstrates how to update font configuration after controller creation.
    /// </summary>
    public static void DemonstrateRuntimeFontUpdates(TerminalController controller)
    {
        // Create new font configuration
        var newFontConfig = new TerminalFontConfig
        {
            RegularFontName = "HackNerdFontMono-Regular",
            BoldFontName = "HackNerdFontMono-Bold",
            ItalicFontName = "HackNerdFontMono-Italic", 
            BoldItalicFontName = "HackNerdFontMono-BoldItalic",
            FontSize = 14.0f,
            AutoDetectContext = false
        };
        
        // Update the controller's font configuration at runtime
        // This will immediately reload fonts and recalculate character metrics
        controller.UpdateFontConfig(newFontConfig);
        
        Console.WriteLine("Font configuration updated successfully");
    }

    /// <summary>
    /// Example 8: Context Detection with Fallback
    /// Demonstrates how to handle cases where automatic detection might not work as expected.
    /// </summary>
    public static ITerminalController CreateWithDetectionFallback(
        SessionManager sessionManager)
    {
        try
        {
            // Try automatic detection first
            var detectedConfig = FontContextDetector.DetectAndCreateConfig();
            
            // Verify the detected configuration is acceptable
            if (IsConfigurationAcceptable(detectedConfig))
            {
                Console.WriteLine($"Using detected configuration: {detectedConfig.RegularFontName}");
                return new TerminalController(sessionManager, detectedConfig);
            }
            else
            {
                Console.WriteLine("Detected configuration not acceptable, falling back to explicit TestApp config");
                var fallbackConfig = TerminalFontConfig.CreateForTestApp();
                return new TerminalController(sessionManager, fallbackConfig);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Font detection failed: {ex.Message}, using safe defaults");
            
            // Use safe default configuration if detection fails
            var safeConfig = TerminalFontConfig.CreateForTestApp();
            return new TerminalController(sessionManager, safeConfig);
        }
    }

    /// <summary>
    /// Helper method to validate font configuration acceptability.
    /// </summary>
    private static bool IsConfigurationAcceptable(TerminalFontConfig config)
    {
        // Add your own validation logic here
        // For example, check if required fonts are available, font size is reasonable, etc.
        return !string.IsNullOrWhiteSpace(config.RegularFontName) && 
               config.FontSize >= 8.0f && 
               config.FontSize <= 72.0f;
    }

    /// <summary>
    /// Example 9: Configuration for Different Deployment Scenarios
    /// Shows how to choose configuration based on deployment context.
    /// </summary>
    public static ITerminalController CreateForDeploymentScenario(
        SessionManager sessionManager,
        DeploymentScenario scenario)
    {
        return scenario switch
        {
            DeploymentScenario.Development => 
                // Use automatic detection for development convenience
                new TerminalController(sessionManager),
                
            DeploymentScenario.TestAppProduction => 
                // Use explicit TestApp configuration for production consistency
                new TerminalController(sessionManager, TerminalFontConfig.CreateForTestApp()),
                
            DeploymentScenario.GameModProduction => 
                // Use explicit GameMod configuration for production consistency
                new TerminalController(sessionManager, TerminalFontConfig.CreateForGameMod()),
                
            DeploymentScenario.CustomIntegration => 
                // Use hybrid approach for custom integrations
                CreateWithHybridConfig(sessionManager),
                
            _ => throw new ArgumentException($"Unknown deployment scenario: {scenario}")
        };
    }
}

/// <summary>
/// Enumeration of different deployment scenarios for font configuration selection.
/// </summary>
public enum DeploymentScenario
{
    /// <summary>
    /// Development and testing scenario - use automatic detection for convenience.
    /// </summary>
    Development,
    
    /// <summary>
    /// Production TestApp deployment - use explicit TestApp configuration for consistency.
    /// </summary>
    TestAppProduction,
    
    /// <summary>
    /// Production GameMod deployment - use explicit GameMod configuration for consistency.
    /// </summary>
    GameModProduction,
    
    /// <summary>
    /// Custom integration scenario - use hybrid configuration for flexibility.
    /// </summary>
    CustomIntegration
}