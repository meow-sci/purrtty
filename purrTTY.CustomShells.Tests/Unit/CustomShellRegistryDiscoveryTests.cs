using System.Linq;
using NUnit.Framework;
using purrTTY.Core.Terminal;

namespace purrTTY.CustomShells.Tests.Unit;

/// <summary>
/// Unit tests for production custom shell discovery behavior.
/// </summary>
[TestFixture]
[Category("Unit")]
public class CustomShellRegistryDiscoveryTests
{
    [Test]
    public void GetAvailableShells_FindsGameConsoleShell()
    {
        // Arrange
        var registry = new CustomShellRegistry();

        // Act
        var availableShells = registry.GetAvailableShells().ToList();
        var gameShell = availableShells.SingleOrDefault(shell => shell.Id == nameof(GameConsoleShell));

        // Assert
        Assert.That(gameShell.Id, Is.EqualTo(nameof(GameConsoleShell)));
        Assert.That(gameShell.Metadata.Name, Is.EqualTo("Game Console"));
        Assert.That(gameShell.Metadata.Description, Does.Contain("KSA game console interface"));
    }
}