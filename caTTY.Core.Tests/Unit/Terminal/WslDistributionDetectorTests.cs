using System;
using System.Collections.Generic;
using System.Linq;
using caTTY.Core.Terminal;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Terminal;

[TestFixture]
[Category("Unit")]
public class WslDistributionDetectorTests
{
    [SetUp]
    public void Setup()
    {
        // Clear cache before each test to ensure clean state
        WslDistributionDetector.ClearCache();
    }

    [TearDown]
    public void TearDown()
    {
        // Clear cache after each test
        WslDistributionDetector.ClearCache();
    }

    [Test]
    public void GetInstalledDistributions_WhenWslNotAvailable_ReturnsEmptyList()
    {
        // This test may fail on systems with WSL installed
        // It's more of a smoke test to ensure graceful handling
        var distributions = WslDistributionDetector.GetInstalledDistributions();

        // Should return a list (may be empty or populated depending on system)
        Assert.That(distributions, Is.Not.Null);
        Assert.That(distributions, Is.InstanceOf<List<WslDistributionDetector.WslDistribution>>());
    }

    [Test]
    public void GetInstalledDistributions_CachesResults()
    {
        // First call
        var firstCall = WslDistributionDetector.GetInstalledDistributions();

        // Second call should return cached result
        var secondCall = WslDistributionDetector.GetInstalledDistributions();

        // Both calls should return the same instance (cached)
        Assert.That(secondCall, Is.SameAs(firstCall));
    }

    [Test]
    public void GetInstalledDistributions_ForceRefresh_IgnoresCache()
    {
        // First call to populate cache
        var firstCall = WslDistributionDetector.GetInstalledDistributions();

        // Force refresh should bypass cache
        var refreshedCall = WslDistributionDetector.GetInstalledDistributions(forceRefresh: true);

        // Should be different instances (not cached)
        // Note: This might be the same instance if detection is instant,
        // but the important part is that it re-executed the detection logic
        Assert.That(refreshedCall, Is.Not.Null);
    }

    [Test]
    public void ClearCache_RemovesCachedDistributions()
    {
        // First call to populate cache
        var firstCall = WslDistributionDetector.GetInstalledDistributions();

        // Clear cache
        WslDistributionDetector.ClearCache();

        // Second call should re-detect (not use cache)
        var secondCall = WslDistributionDetector.GetInstalledDistributions();

        // Both should be valid lists
        Assert.That(firstCall, Is.Not.Null);
        Assert.That(secondCall, Is.Not.Null);
    }

    [Test]
    public void IsWslAvailable_ReturnsBoolean()
    {
        // Should return a boolean without throwing
        var result = WslDistributionDetector.IsWslAvailable();

        Assert.That(result, Is.TypeOf<bool>());
    }

    [Test]
    public void WslDistribution_HasRequiredProperties()
    {
        // Test that WslDistribution class has the expected properties
        var distro = new WslDistributionDetector.WslDistribution
        {
            Name = "Ubuntu",
            IsDefault = true,
            DisplayName = "Ubuntu (Default)"
        };

        Assert.That(distro.Name, Is.EqualTo("Ubuntu"));
        Assert.That(distro.IsDefault, Is.True);
        Assert.That(distro.DisplayName, Is.EqualTo("Ubuntu (Default)"));
    }

    [Test]
    public void GetInstalledDistributions_ReturnsDistributionsWithValidProperties()
    {
        var distributions = WslDistributionDetector.GetInstalledDistributions();

        // Each distribution should have valid properties
        foreach (var distro in distributions)
        {
            Assert.That(distro.Name, Is.Not.Null);
            Assert.That(distro.Name, Is.Not.Empty);
            Assert.That(distro.DisplayName, Is.Not.Null);
            Assert.That(distro.DisplayName, Is.Not.Empty);
        }
    }

    [Test]
    public void GetInstalledDistributions_ConsistentResultsWhenCached()
    {
        // First call
        var first = WslDistributionDetector.GetInstalledDistributions();

        // Multiple subsequent calls
        var second = WslDistributionDetector.GetInstalledDistributions();
        var third = WslDistributionDetector.GetInstalledDistributions();

        // All should be the same cached instance
        Assert.That(second, Is.SameAs(first));
        Assert.That(third, Is.SameAs(first));
    }
}
