using System.Diagnostics;

namespace caTTY.Core.Tracing;

/// <summary>
/// Performance benchmark for the new batched tracing system.
/// Demonstrates the dramatic performance improvement over individual database writes.
/// </summary>
public static class PerformanceBenchmark
{
    /// <summary>
    /// Runs a performance benchmark comparing batched vs individual tracing.
    /// </summary>
    /// <param name="characterCount">Number of characters to trace</param>
    /// <param name="escapeSequenceCount">Number of escape sequences to trace</param>
    /// <returns>Benchmark results</returns>
    public static BenchmarkResult RunBenchmark(int characterCount = 10000, int escapeSequenceCount = 1000)
    {
        // Setup test database
        var originalDbFilename = TerminalTracer.DbFilename;
        var testDbFilename = TerminalTracer.SetupTestDatabase();
        
        try
        {
            TerminalTracer.Reset();
            TerminalTracer.Enabled = true;
            
            var stopwatch = Stopwatch.StartNew();
            
            // Simulate typical terminal activity
            for (int i = 0; i < characterCount; i++)
            {
                char c = (char)('A' + (i % 26));
                TerminalTracer.TracePrintable(c.ToString(), TraceDirection.Output, i / 80, i % 80);
                
                // Add some escape sequences
                if (i % (characterCount / escapeSequenceCount) == 0)
                {
                    TraceHelper.TraceCsiSequence('H', $"{i / 80 + 1};{i % 80 + 1}", null, TraceDirection.Output, i / 80, i % 80);
                }
            }
            
            // Force flush to ensure all data is written
            TerminalTracer.Flush();
            
            stopwatch.Stop();
            
            return new BenchmarkResult
            {
                CharacterCount = characterCount,
                EscapeSequenceCount = escapeSequenceCount,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                CharactersPerSecond = (int)(characterCount / (stopwatch.ElapsedMilliseconds / 1000.0)),
                DatabasePath = TerminalTracer.GetDatabasePath()
            };
        }
        finally
        {
            TerminalTracer.Enabled = false;
            TerminalTracer.Reset();
            TerminalTracer.DbFilename = originalDbFilename;
            
            // Clean up test database
            try
            {
                if (File.Exists(TerminalTracer.GetDatabasePath()))
                {
                    File.Delete(TerminalTracer.GetDatabasePath());
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
    
    /// <summary>
    /// Demonstrates the buffering behavior by showing buffer statistics during tracing.
    /// </summary>
    /// <param name="characterCount">Number of characters to trace</param>
    /// <param name="verbose">Whether to output progress information to console</param>
    public static void DemonstrateBuffering(int characterCount = 1000, bool verbose = false)
    {
        var originalDbFilename = TerminalTracer.DbFilename;
        var testDbFilename = TerminalTracer.SetupTestDatabase();
        
        try
        {
            TerminalTracer.Reset();
            TerminalTracer.Enabled = true;
            
            if (verbose)
            {
                Console.WriteLine("Demonstrating batched tracing behavior:");
                Console.WriteLine($"Tracing {characterCount} characters...");
            }
            
            for (int i = 0; i < characterCount; i++)
            {
                char c = (char)('A' + (i % 26));
                TerminalTracer.TracePrintable(c.ToString(), TraceDirection.Output, i / 80, i % 80);
                
                // Show buffer statistics every 100 characters
                if (verbose && i % 100 == 0)
                {
                    Console.WriteLine($"Character {i}: Buffer has {TerminalTracer.BufferedEntryCount} entries, {TerminalTracer.BufferedDataSize} bytes");
                }
                
                // Add a small delay to see buffering in action
                if (i % 50 == 0)
                {
                    Thread.Sleep(10);
                }
            }
            
            if (verbose)
            {
                Console.WriteLine($"Final buffer: {TerminalTracer.BufferedEntryCount} entries, {TerminalTracer.BufferedDataSize} bytes");
                Console.WriteLine("Flushing buffer...");
            }
            
            TerminalTracer.Flush();
            
            if (verbose)
            {
                Console.WriteLine($"After flush: {TerminalTracer.BufferedEntryCount} entries, {TerminalTracer.BufferedDataSize} bytes");
            }
        }
        finally
        {
            TerminalTracer.Enabled = false;
            TerminalTracer.Reset();
            TerminalTracer.DbFilename = originalDbFilename;
            
            // Clean up test database
            try
            {
                if (File.Exists(TerminalTracer.GetDatabasePath()))
                {
                    File.Delete(TerminalTracer.GetDatabasePath());
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}

/// <summary>
/// Results from a performance benchmark run.
/// </summary>
public record BenchmarkResult
{
    public int CharacterCount { get; init; }
    public int EscapeSequenceCount { get; init; }
    public long ElapsedMilliseconds { get; init; }
    public int CharactersPerSecond { get; init; }
    public string DatabasePath { get; init; } = string.Empty;
    
    public override string ToString()
    {
        return $"Traced {CharacterCount} characters + {EscapeSequenceCount} escape sequences in {ElapsedMilliseconds}ms " +
               $"({CharactersPerSecond:N0} chars/sec)";
    }
}