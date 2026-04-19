using System;
using System.IO;
using System.Reflection;
using caTTY.Core.Terminal;
using caTTY.Core.Tracing;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Tracing;

/// <summary>
///     Test to inspect actual tracing output for unknown sequences.
///     This test is designed to show what gets traced when unknown sequences are processed.
/// </summary>
[TestFixture]
[Category("Unit")]
public class TracingOutputInspectionTest
{
    private string _testDatabasePath = null!;
    private TerminalEmulator _terminal = null!;

    [SetUp]
    public void SetUp()
    {
        // Create test-specific database
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var testId = Guid.NewGuid().ToString("N");
        _testDatabasePath = Path.Combine(assemblyDir!, $"test_trace_{testId}.db");

        // Configure tracing for tests
        TerminalTracer.DbPath = assemblyDir;
        TerminalTracer.DbFilename = $"test_trace_{testId}.db";
        TerminalTracer.Enabled = true;
        TerminalTracer.Reset(); // Ensure clean state

        // Create terminal
        _terminal = TerminalEmulator.Create(80, 24, 1000);
    }

    [TearDown]
    public void TearDown()
    {
        _terminal?.Dispose();
        TerminalTracer.Enabled = false;
        TerminalTracer.Shutdown();

        // Clean up test database
        if (File.Exists(_testDatabasePath))
        {
            try
            {
                File.Delete(_testDatabasePath);
            }
            catch
            {
                // Ignore cleanup failures
            }
        }
    }

    [Test]
    public void InspectUnknownSequenceTracing()
    {
        // Send various unknown sequences
        
        _terminal.Write("\x1b[99z");           // Unknown CSI
        _terminal.Write("\x1b]999;test\x07");  // Unknown OSC
        _terminal.Write("\x1bz");              // Unknown ESC
        _terminal.Write("\x1bPztest\x1b\\");   // Unknown DCS
        _terminal.Write("Hello");              // Regular text
        
        // Flush and read traces
        TerminalTracer.Flush();
        
        var connectionString = $"Data Source={_testDatabasePath}";
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        
        var command = connection.CreateCommand();
        command.CommandText = "SELECT type, escape_seq, printable, direction FROM trace ORDER BY time";
        
        // Console.WriteLine("\nTraced sequences:");
        // Console.WriteLine("Type\t\tEscape Sequence\t\tPrintable\tDirection");
        // Console.WriteLine("----\t\t---------------\t\t---------\t---------");
        // 
        // using var reader = command.ExecuteReader();
        // while (reader.Read())
        // {
        //     string type = reader.IsDBNull(0) ? "NULL" : reader.GetString(0);
        //     string escSeq = reader.IsDBNull(1) ? "NULL" : reader.GetString(1);
        //     string printable = reader.IsDBNull(2) ? "NULL" : reader.GetString(2);
        //     string direction = reader.GetString(3);
            
        //     Console.WriteLine($"{type}\t\t{escSeq}\t\t{printable}\t{direction}");
        // }
        
        // This test always passes - it's just for inspection
        Assert.Pass();
    }
}