using NUnit.Framework;
using purrTTY.Core.Terminal;

namespace PurrTTY.Terminal.Tests;

/// <summary>
/// Pins the caching contract the game menus rely on: shell/WSL detection runs at
/// most once per process, so the menu draw path never pays for a PATH scan or a
/// `wsl --list` spawn. (A previous 5-minute cache expiry re-ran wsl.exe on the
/// render thread mid-game, hanging the game for seconds.)
/// </summary>
[TestFixture]
public sealed class ShellDetectionCachingTests
{
    [Test]
    public void WslDistributions_AreCachedForProcessLifetime()
    {
        WslDistributionDetector.ClearCache();

        var first = WslDistributionDetector.GetInstalledDistributions();
        var second = WslDistributionDetector.GetInstalledDistributions();

        Assert.That(second, Is.SameAs(first),
            "repeat calls must return the cached list without re-detecting");
    }

    [Test]
    public void UnixShells_AreCachedForProcessLifetime()
    {
        if (OperatingSystem.IsWindows())
        {
            // Unix shell detection is Linux/macOS only - skip silently
            Assert.Ignore();
        }

        UnixShellDetector.ClearCache();

        var first = UnixShellDetector.GetInstalledShells();
        var second = UnixShellDetector.GetInstalledShells();

        Assert.That(second, Is.SameAs(first),
            "repeat calls must return the cached list without re-detecting");
    }

    [Test]
    public void IsShellAvailable_IsStableAcrossCalls()
    {
        foreach (ShellType shellType in Enum.GetValues<ShellType>())
        {
            bool first = ShellAvailabilityChecker.IsShellAvailable(shellType);
            bool second = ShellAvailabilityChecker.IsShellAvailable(shellType);
            Assert.That(second, Is.EqualTo(first), $"availability of {shellType} must be cached/stable");
        }
    }

    [Test]
    public void WslDistributions_OnNonWindows_AreEmpty()
    {
        if (OperatingSystem.IsWindows())
        {
            // Asserts the non-Windows degenerate case - skip silently on Windows
            Assert.Ignore();
        }

        WslDistributionDetector.ClearCache();

        Assert.That(WslDistributionDetector.GetInstalledDistributions(), Is.Empty);
    }
}
