using caTTY.Core.Tracing;
using NUnit.Framework;
using System.Diagnostics;

namespace caTTY.Core.Tests.Unit;

/// <summary>
/// Demonstrates the performance improvement of the batched tracing system.
/// </summary>
[TestFixture]
[Category("Unit")]
public class PerformanceDemoTests
{
    [Test]
    public void DemonstratePerformanceImprovement()
    {
        // Run the performance benchmark
        var result = PerformanceBenchmark.RunBenchmark(1000, 100);
        
        // Verify reasonable performance (should be much faster than individual writes)
        Assert.That(result.CharactersPerSecond, Is.GreaterThan(1000), 
            "Should process at least 1000 characters per second with batching");
        
        Assert.That(result.ElapsedMilliseconds, Is.LessThan(5000), 
            "Should complete 1000 characters + 100 escape sequences in under 5 seconds");
    }
    
    [Test]
    public void DemonstrateBufferingBehavior()
    {
        // This will show how buffering works in practice (silent mode for tests)
        PerformanceBenchmark.DemonstrateBuffering(500, verbose: false);
        
        // Just verify it completes without error
        Assert.Pass();
    }
}