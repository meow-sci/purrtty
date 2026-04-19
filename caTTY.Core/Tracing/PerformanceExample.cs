using System;
using System.Diagnostics;

namespace caTTY.Core.Tracing;

/// <summary>
/// Example demonstrating the performance characteristics of the tracing system.
/// </summary>
public static class PerformanceExample
{
  /// <summary>
  /// Demonstrates the performance difference between enabled and disabled tracing.
  /// </summary>
  public static void RunPerformanceTest()
  {
    const int iterations = 1_000_000;

    Console.WriteLine("Terminal Tracing Performance Test");
    Console.WriteLine($"Running {iterations:N0} trace operations...\n");

    // Test with tracing disabled (default)
    TerminalTracer.Enabled = false;
    var disabledTime = MeasureTraceOperations(iterations);
    Console.WriteLine($"Tracing DISABLED: {disabledTime.TotalMilliseconds:F2} ms ({disabledTime.TotalNanoseconds / iterations:F1} ns per call)");

    // Test with tracing enabled
    TerminalTracer.Enabled = true;
    var enabledTime = MeasureTraceOperations(iterations);
    Console.WriteLine($"Tracing ENABLED:  {enabledTime.TotalMilliseconds:F2} ms ({enabledTime.TotalNanoseconds / iterations:F1} ns per call)");

    // Calculate overhead
    var overhead = enabledTime.TotalMilliseconds - disabledTime.TotalMilliseconds;
    var overheadPercent = (overhead / disabledTime.TotalMilliseconds) * 100;

    Console.WriteLine($"\nOverhead: {overhead:F2} ms ({overheadPercent:F1}% increase)");
    Console.WriteLine($"Per-call overhead: {(enabledTime.TotalNanoseconds - disabledTime.TotalNanoseconds) / iterations:F1} ns");

    // Cleanup
    TerminalTracer.Enabled = false;
    TerminalTracer.Shutdown();
  }

  private static TimeSpan MeasureTraceOperations(int iterations)
  {
    // Warm up
    for (int i = 0; i < 1000; i++)
    {
      TerminalTracer.TraceEscape("CSI H", TraceDirection.Output, 0, 0);
      TerminalTracer.TracePrintable("test", TraceDirection.Output, 0, 4);
      TraceHelper.TraceCsiSequence('H', "1;1", null, TraceDirection.Output, 1, 1);
    }

    // Force garbage collection before measurement
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var stopwatch = Stopwatch.StartNew();

    for (int i = 0; i < iterations; i++)
    {
      // Mix of different trace operations to simulate real usage
      if (i % 4 == 0)
        TerminalTracer.TraceEscape("CSI 2 J", TraceDirection.Output, 0, 0);
      else if (i % 4 == 1)
        TerminalTracer.TracePrintable("Hello", TraceDirection.Output, 0, i % 80);
      else if (i % 4 == 2)
        TraceHelper.TraceCsiSequence('H', "1;1", null, TraceDirection.Output, 1, 1);
      else
        TraceHelper.TraceControlChar(0x0A, TraceDirection.Output, i % 24, 0);
    }

    stopwatch.Stop();
    return stopwatch.Elapsed;
  }

  /// <summary>
  /// Example of optimal tracing usage in hot paths.
  /// </summary>
  public static void OptimalUsageExample()
  {
    // BAD: Always creates string even when tracing is disabled
    // TerminalTracer.TraceEscape($"Complex calculation: {ExpensiveOperation()}");

    // GOOD: Check enabled flag first to avoid expensive operations
    if (TerminalTracer.Enabled)
    {
      TerminalTracer.TraceEscape($"Complex calculation: {ExpensiveOperation()}", TraceDirection.Output, 0, 0);
    }

    // BETTER: Use the helper methods which check internally
    TraceHelper.TraceCsiSequence('H', "1;1", null, TraceDirection.Output, 1, 1); // Already checks Enabled internally

    // BEST: For hot paths, cache the enabled state if checking frequently
    var tracingEnabled = TerminalTracer.Enabled;
    for (int i = 0; i < 10000; i++)
    {
      if (tracingEnabled)
      {
        TerminalTracer.TracePrintable($"Character {i}", TraceDirection.Output, i / 80, i % 80);
      }
      // ... hot path processing ...
    }
  }

  private static string ExpensiveOperation()
  {
    // Simulate expensive string formatting
    return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
  }
}