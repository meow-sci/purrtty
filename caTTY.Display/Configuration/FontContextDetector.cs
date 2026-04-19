using System;
using System.Linq;
using System.Reflection;

namespace caTTY.Display.Configuration;

/// <summary>
/// Utility class for detecting execution context and creating appropriate font configurations.
/// Uses assembly inspection to determine whether the application is running in TestApp or GameMod context.
/// 
/// This class provides automatic font configuration selection based on the execution environment,
/// allowing applications to use context-appropriate fonts without manual configuration.
/// 
/// <para><strong>Usage Scenarios:</strong></para>
/// <list type="bullet">
/// <item><description><strong>Development:</strong> Use automatic detection for convenience and rapid prototyping</description></item>
/// <item><description><strong>Testing:</strong> Verify that different contexts receive appropriate font configurations</description></item>
/// <item><description><strong>Hybrid Applications:</strong> Start with automatic detection, then customize as needed</description></item>
/// </list>
/// 
/// <para><strong>Detection Logic:</strong></para>
/// <list type="number">
/// <item><description>Inspects loaded assemblies for KSA-related names (KSA, Kitten, Space, Agency, BRUTAL, StarMap)</description></item>
/// <item><description>Checks for TestApp-specific assemblies (TestApp, Playground)</description></item>
/// <item><description>Falls back to entry assembly name inspection</description></item>
/// <item><description>Returns Unknown if context cannot be determined</description></item>
/// </list>
/// 
/// <para><strong>Configuration Mapping:</strong></para>
/// <list type="bullet">
/// <item><description><strong>GameMod Context:</strong> Uses smaller font size (14.0f) optimized for game integration</description></item>
/// <item><description><strong>TestApp Context:</strong> Uses larger font size (16.0f) optimized for development</description></item>
/// <item><description><strong>Unknown Context:</strong> Defaults to TestApp settings for safety</description></item>
/// </list>
/// </summary>
public static class FontContextDetector
{
    /// <summary>
    /// Detects the current execution context and creates an appropriate font configuration.
    /// Combines context detection with configuration creation for convenience.
    /// 
    /// This is the primary method for automatic font configuration. It analyzes the current
    /// execution environment and returns a TerminalFontConfig optimized for that context.
    /// 
    /// <para><strong>When to use this method:</strong></para>
    /// <list type="bullet">
    /// <item><description>Development and testing scenarios where convenience is prioritized</description></item>
    /// <item><description>Applications that need to work in both TestApp and GameMod contexts</description></item>
    /// <item><description>Rapid prototyping where specific font requirements are not critical</description></item>
    /// <item><description>As a starting point for hybrid configuration approaches</description></item>
    /// </list>
    /// 
    /// <para><strong>Alternative approaches:</strong></para>
    /// <list type="bullet">
    /// <item><description>Use <see cref="TerminalFontConfig.CreateForTestApp"/> for explicit TestApp configuration</description></item>
    /// <item><description>Use <see cref="TerminalFontConfig.CreateForGameMod"/> for explicit GameMod configuration</description></item>
    /// <item><description>Create custom <see cref="TerminalFontConfig"/> for specific requirements</description></item>
    /// </list>
    /// </summary>
    /// <returns>A TerminalFontConfig instance optimized for the detected execution context.</returns>
    /// <example>
    /// <code>
    /// // Automatic detection (recommended for development)
    /// var fontConfig = FontContextDetector.DetectAndCreateConfig();
    /// var controller = new TerminalController(sessionManager, fontConfig);
    /// 
    /// // Alternative: Use default constructor which calls this method automatically
    /// var controller = new TerminalController(sessionManager);
    /// </code>
    /// </example>
    public static TerminalFontConfig DetectAndCreateConfig()
    {
        var context = DetectExecutionContext();
        
        LogContextDetection(context);
        
        return context switch
        {
            ExecutionContext.TestApp => TerminalFontConfig.CreateForTestApp(),
            ExecutionContext.GameMod => TerminalFontConfig.CreateForGameMod(),
            _ => TerminalFontConfig.CreateForTestApp() // Safe default
        };
    }
    
    /// <summary>
    /// Detects the current execution context by inspecting loaded assemblies.
    /// Looks for KSA-related assemblies to determine if running in game mod context.
    /// </summary>
    /// <returns>The detected execution context.</returns>
    public static ExecutionContext DetectExecutionContext()
    {
        try
        {
            // Get all currently loaded assemblies
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            
            // Check for KSA game assemblies
            var hasKsaAssemblies = assemblies.Any(assembly => 
                IsKsaRelatedAssembly(assembly));
            
            // Check for TestApp-specific assemblies
            var hasTestAppAssemblies = assemblies.Any(assembly => 
                IsTestAppRelatedAssembly(assembly));
            
            LogAssemblyInspection(assemblies, hasKsaAssemblies, hasTestAppAssemblies);
            
            if (hasKsaAssemblies)
            {
                return ExecutionContext.GameMod;
            }
            
            if (hasTestAppAssemblies)
            {
                return ExecutionContext.TestApp;
            }
            
            // Fallback: check entry assembly name
            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly != null)
            {
                var entryName = entryAssembly.GetName().Name;
                LogEntryAssemblyCheck(entryName);
                
                if (entryName?.Contains("TestApp", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return ExecutionContext.TestApp;
                }
                
                if (entryName?.Contains("GameMod", StringComparison.OrdinalIgnoreCase) == true ||
                    entryName?.Contains("KSA", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return ExecutionContext.GameMod;
                }
            }
            
            return ExecutionContext.Unknown;
        }
        catch (Exception ex)
        {
            LogDetectionError(ex);
            return ExecutionContext.Unknown;
        }
    }
    
    /// <summary>
    /// Determines if an assembly is related to the KSA game environment.
    /// </summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>True if the assembly appears to be KSA-related, false otherwise.</returns>
    private static bool IsKsaRelatedAssembly(Assembly assembly)
    {
        try
        {
            var assemblyName = assembly.GetName().Name;
            if (string.IsNullOrEmpty(assemblyName))
                return false;
            
            // Check for KSA-specific assembly names
            return assemblyName.Contains("KSA", StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.Contains("Kitten", StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.Contains("Space", StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.Contains("Agency", StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.Contains("BRUTAL", StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.Contains("StarMap", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // If we can't inspect the assembly, assume it's not KSA-related
            return false;
        }
    }
    
    /// <summary>
    /// Determines if an assembly is related to the TestApp environment.
    /// </summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>True if the assembly appears to be TestApp-related, false otherwise.</returns>
    private static bool IsTestAppRelatedAssembly(Assembly assembly)
    {
        try
        {
            var assemblyName = assembly.GetName().Name;
            if (string.IsNullOrEmpty(assemblyName))
                return false;
            
            // Check for TestApp-specific assembly names
            return assemblyName.Contains("TestApp", StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.Contains("Playground", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // If we can't inspect the assembly, assume it's not TestApp-related
            return false;
        }
    }
    
    /// <summary>
    /// Logs the detected context for debugging purposes.
    /// </summary>
    /// <param name="context">The detected execution context.</param>
    private static void LogContextDetection(ExecutionContext context)
    {
        // Context detection logging removed for test performance
    }
    
    /// <summary>
    /// Logs assembly inspection results for debugging purposes.
    /// </summary>
    /// <param name="assemblies">All loaded assemblies.</param>
    /// <param name="hasKsaAssemblies">Whether KSA assemblies were found.</param>
    /// <param name="hasTestAppAssemblies">Whether TestApp assemblies were found.</param>
    private static void LogAssemblyInspection(Assembly[] assemblies, bool hasKsaAssemblies, bool hasTestAppAssemblies)
    {
        // Assembly inspection logging removed for test performance
    }
    
    /// <summary>
    /// Logs entry assembly inspection for debugging purposes.
    /// </summary>
    /// <param name="entryName">The name of the entry assembly.</param>
    private static void LogEntryAssemblyCheck(string? entryName)
    {
        // Entry assembly logging removed for test performance
    }
    
    /// <summary>
    /// Logs detection errors for debugging purposes.
    /// </summary>
    /// <param name="ex">The exception that occurred during detection.</param>
    private static void LogDetectionError(Exception ex)
    {
        // Detection error logging removed for test performance
    }
}