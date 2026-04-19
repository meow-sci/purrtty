using NUnit.Framework;
using caTTY.Core.Terminal;
using System.Linq;

namespace caTTY.Core.Tests.Unit.Terminal;

/// <summary>
/// Unit tests for ShellAvailabilityChecker functionality.
/// </summary>
[TestFixture]
[Category("Unit")]
public class ShellAvailabilityCheckerTests
{
  [Test]
  public void IsShellAvailable_WithAutoShellType_ShouldReturnTrue()
  {
    // Act
    bool isAvailable = ShellAvailabilityChecker.IsShellAvailable(ShellType.Auto);

    // Assert
    Assert.That(isAvailable, Is.True, "Auto shell type should always be available");
  }

  [Test]
  public void IsShellAvailable_WithCustomShellType_ShouldReturnTrue()
  {
    // Act
    bool isAvailable = ShellAvailabilityChecker.IsShellAvailable(ShellType.Custom);

    // Assert
    Assert.That(isAvailable, Is.True, "Custom shell type should always be available (path validation happens later)");
  }

  [Test]
  public void IsShellAvailable_WithCustomGameShellType_ShouldReturnTrue()
  {
    // Act
    bool isAvailable = ShellAvailabilityChecker.IsShellAvailable(ShellType.CustomGame);

    // Assert
    Assert.That(isAvailable, Is.True, "Custom game shell type should always be available (managed by registry)");
  }

  [Test]
  public void IsShellAvailable_WithCmdShellType_ShouldReturnTrueOnWindows()
  {
    // Act
    bool isAvailable = ShellAvailabilityChecker.IsShellAvailable(ShellType.Cmd);

    // Assert
    Assert.That(isAvailable, Is.True, "Command Prompt should be available on Windows systems");
  }

  [Test]
  public void IsShellAvailable_WithPowerShellShellType_ShouldCheckAvailability()
  {
    // Act
    bool isAvailable = ShellAvailabilityChecker.IsShellAvailable(ShellType.PowerShell);

    // Assert
    // PowerShell availability depends on the system, so we just verify the method doesn't throw
    Assert.That(isAvailable, Is.TypeOf<bool>(), "PowerShell availability check should return a boolean");
  }

  [Test]
  public void IsShellAvailable_WithPowerShellCoreShellType_ShouldCheckAvailability()
  {
    // Act
    bool isAvailable = ShellAvailabilityChecker.IsShellAvailable(ShellType.PowerShellCore);

    // Assert
    // PowerShell Core availability depends on the system, so we just verify the method doesn't throw
    Assert.That(isAvailable, Is.TypeOf<bool>(), "PowerShell Core availability check should return a boolean");
  }

  [Test]
  public void IsShellAvailable_WithWslShellType_ShouldCheckAvailability()
  {
    // Act
    bool isAvailable = ShellAvailabilityChecker.IsShellAvailable(ShellType.Wsl);

    // Assert
    // WSL availability depends on the system, so we just verify the method doesn't throw
    Assert.That(isAvailable, Is.TypeOf<bool>(), "WSL availability check should return a boolean");

  }

  [Test]
  public void GetAvailableShells_ShouldReturnNonEmptyList()
  {
    // Act
    var availableShells = ShellAvailabilityChecker.GetAvailableShells();

    // Assert
    Assert.That(availableShells, Is.Not.Null, "Available shells list should not be null");
    Assert.That(availableShells.Count, Is.GreaterThan(0), "At least one shell should be available");

    // Auto and Custom should always be available
    Assert.That(availableShells, Contains.Item(ShellType.Auto), "Auto shell should always be available");
    Assert.That(availableShells, Contains.Item(ShellType.Custom), "Custom shell should always be available");
    Assert.That(availableShells, Contains.Item(ShellType.CustomGame), "Custom game shell should always be available");

    Console.WriteLine($"Available shells: {string.Join(", ", availableShells)}");
  }

  [Test]
  public void GetAvailableShells_ShouldNotContainDuplicates()
  {
    // Act
    var availableShells = ShellAvailabilityChecker.GetAvailableShells();

    // Assert
    var distinctShells = availableShells.Distinct().ToList();
    Assert.That(availableShells.Count, Is.EqualTo(distinctShells.Count), "Available shells list should not contain duplicates");
  }

  [Test]
  public void GetAvailableShellsWithNames_ShouldReturnNonEmptyList()
  {
    // Act
    var availableShells = ShellAvailabilityChecker.GetAvailableShellsWithNames();

    // Assert
    Assert.That(availableShells, Is.Not.Null, "Available shells with names list should not be null");
    Assert.That(availableShells.Count, Is.GreaterThan(0), "At least one shell should be available");

    // Verify all entries have valid display names
    foreach (var (shellType, displayName) in availableShells)
    {
      Assert.That(displayName, Is.Not.Null.And.Not.Empty, $"Display name for {shellType} should not be null or empty");
    }
  }

  [Test]
  public void GetAvailableShellsWithNames_ShouldNotContainDuplicateShellTypes()
  {
    // Act
    var availableShells = ShellAvailabilityChecker.GetAvailableShellsWithNames();

    // Assert
    var shellTypes = availableShells.Select(s => s.ShellType).ToList();
    var distinctShellTypes = shellTypes.Distinct().ToList();
    Assert.That(shellTypes.Count, Is.EqualTo(distinctShellTypes.Count), "Available shells with names should not contain duplicate shell types");
  }

  [Test]
  public void GetAvailableShells_AndGetAvailableShellsWithNames_ShouldReturnConsistentResults()
  {
    // Act
    var availableShells = ShellAvailabilityChecker.GetAvailableShells();
    var availableShellsWithNames = ShellAvailabilityChecker.GetAvailableShellsWithNames();

    // Assert
    var shellTypesFromList = availableShells.ToHashSet();
    var shellTypesFromNamed = availableShellsWithNames.Select(s => s.ShellType).ToHashSet();

    // The named list might be a subset (it excludes Auto), but all shells in named should be in the main list
    foreach (var shellType in shellTypesFromNamed)
    {
      Assert.That(shellTypesFromList, Contains.Item(shellType),
          $"Shell type {shellType} from GetAvailableShellsWithNames should also be in GetAvailableShells");
    }
  }

  [Test]
  public void IsShellAvailable_WithInvalidShellType_ShouldReturnFalse()
  {
    // Arrange - Use an invalid enum value
    var invalidShellType = (ShellType)999;

    // Act
    bool isAvailable = ShellAvailabilityChecker.IsShellAvailable(invalidShellType);

    // Assert
    Assert.That(isAvailable, Is.False, "Invalid shell type should return false");
  }

  [Test]
  public void ShellAvailabilityChecker_ShouldHandleExceptionsGracefully()
  {
    // This test verifies that the availability checker doesn't throw exceptions
    // even if there are issues with the system (e.g., PATH variable issues)

    // Act & Assert - Should not throw
    Assert.DoesNotThrow(() =>
    {
      foreach (ShellType shellType in System.Enum.GetValues<ShellType>())
      {
        ShellAvailabilityChecker.IsShellAvailable(shellType);
      }
    }, "Shell availability checking should handle exceptions gracefully");
  }
}