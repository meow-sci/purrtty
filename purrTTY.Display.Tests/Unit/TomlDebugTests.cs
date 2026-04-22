using System;
using System.IO;
using Tomlyn;
using Tomlyn.Model;
using NUnit.Framework;

namespace purrTTY.Display.Tests.Unit;

/// <summary>
/// Debug tests for TOML parsing.
/// </summary>
[TestFixture]
[Category("Unit")]
public class TomlDebugTests
{
    [Test]
    public void TestTomlParsingWithDifferentMethod()
    {
        // Arrange
        var tomlContent = @"name = 'test'
version = '1.0'";

        // Act & Assert
        try
        {
            // Try Tomlyn parsing approach
            var tomlTable = TomlSerializer.Deserialize<TomlTable>(tomlContent);
            // Console.WriteLine($"TomlTable result: {tomlTable != null}");
            if (tomlTable != null)
            {
                // Console.WriteLine($"Keys count: {tomlTable.Keys.Count}");
                // Console.WriteLine($"Keys: {string.Join(", ", tomlTable.Keys)}");
            }

            Assert.That(tomlTable, Is.Not.Null);

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex}");
            throw;
        }
    }

    [Test]
    public void TestAdventureTomlFile()
    {
        // Arrange
        var themePath = Path.Combine("TerminalThemes", "Adventure.toml");
        
        if (!File.Exists(themePath))
        {
            Assert.Ignore($"Theme file not found: {themePath}");
            return;
        }

        // Act & Assert
        try
        {
            var tomlContent = File.ReadAllText(themePath);
            // Console.WriteLine($"TOML content length: {tomlContent.Length}");
            // Console.WriteLine($"First 200 chars: {tomlContent.Substring(0, Math.Min(200, tomlContent.Length))}");
            
            var tomlTable = TomlSerializer.Deserialize<TomlTable>(tomlContent);
            Assert.That(tomlTable, Is.Not.Null);
            
            // Console.WriteLine($"Root table keys: {string.Join(", ", tomlTable.Keys)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex}");
            throw;
        }
    }
}