using System;
using System.IO;
using caTTY.Display.Rendering;
using Tomlyn;
using Tomlyn.Model;
using NUnit.Framework;

namespace caTTY.Display.Tests.Unit;

/// <summary>
/// Debug tests for TOML validation logic.
/// </summary>
[TestFixture]
[Category("Unit")]
public class TomlValidationDebugTests
{
    [Test]
    public void DebugTomlValidation()
    {
        // Arrange
        var tomlContent = @"name = 'test'
version = '1.0'

[colors.normal]
black = '#040404'
";

        // Act
        try
        {
            // Use Tomlyn's TryToModel for graceful error handling
            if (!Toml.TryToModel<TomlTable>(tomlContent, out var tomlTable, out var diagnostics))
            {
                // Console.WriteLine("TOML parsing failed:");
                foreach (var diagnostic in diagnostics)
                {
                    // Console.WriteLine($"  {diagnostic}");
                }
                return;
            }
            
            // Console.WriteLine($"Root table keys: {string.Join(", ", tomlTable.Keys)}");
            
            if (tomlTable.TryGetValue("colors", out var colorsValue))
            {
                // Console.WriteLine($"Colors value type: {colorsValue?.GetType()}");
                if (colorsValue is TomlTable colorsTable)
                {
                    // Console.WriteLine($"Colors table keys: {string.Join(", ", colorsTable.Keys)}");
                    
                    foreach (var section in new[] { "normal", "bright", "primary", "cursor", "selection" })
                    {
                        if (colorsTable.TryGetValue(section, out var sectionValue))
                        {
                            // Console.WriteLine($"{section} value type: {sectionValue?.GetType()}");
                            if (sectionValue is TomlTable sectionTable)
                            {
                                // Console.WriteLine($"{section} table keys: {string.Join(", ", sectionTable.Keys)}");
                            }
                        }
                        else
                        {
                            // Console.WriteLine($"{section} section not found");
                        }
                    }
                }
            }
            else
            {
                // Console.WriteLine("Colors section not found");
            }
            
            // Test the actual validation
            var tempFile = Path.Combine(Path.GetTempPath(), $"debug_theme_{Guid.NewGuid():N}.toml");
            File.WriteAllText(tempFile, tomlContent);
            
            var theme = TomlThemeLoader.LoadThemeFromFile(tempFile);
            // Console.WriteLine($"Theme loaded: {theme.HasValue}");
            
            File.Delete(tempFile);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex}");
            throw;
        }
    }
}