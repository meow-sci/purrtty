using System;
using System.IO;
using System.Reflection;
using caTTY.Core.Terminal;
using caTTY.Core.Tracing;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Tracing;

/// <summary>
///     Unit tests for unknown sequence tracing in the terminal parser.
///     Verifies that unknown/unhandled sequences are properly traced for debugging.
/// </summary>
[TestFixture]
[Category("Unit")]
public class UnknownSequenceTracingTests
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

    /// <summary>
    /// Helper method to get traces from the database.
    /// </summary>
    private List<TraceRecord> GetTraces()
    {
        TerminalTracer.Flush(); // Ensure all buffered entries are written
        
        var traces = new List<TraceRecord>();
        var connectionString = $"Data Source={_testDatabasePath}";
        
        try
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            
            var command = connection.CreateCommand();
            command.CommandText = "SELECT type, escape_seq, printable, direction, row, col FROM trace ORDER BY time";
            
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                traces.Add(new TraceRecord(
                    Type: reader.IsDBNull(0) ? null : reader.GetString(0),
                    EscapeSequence: reader.IsDBNull(1) ? null : reader.GetString(1),
                    PrintableText: reader.IsDBNull(2) ? null : reader.GetString(2),
                    Direction: reader.GetString(3),
                    Row: reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    Column: reader.IsDBNull(5) ? null : reader.GetInt32(5)
                ));
            }
        }
        catch (Exception ex)
        {
            Assert.Fail($"Failed to read traces from database: {ex.Message}");
        }
        
        return traces;
    }

    /// <summary>
    /// Record representing a trace entry from the database.
    /// </summary>
    private record TraceRecord(string? Type, string? EscapeSequence, string? PrintableText, string Direction, int? Row, int? Column);

    [Test]
    public void UnknownCsiSequence_IsTraced()
    {
        // Arrange - Send an unknown CSI sequence
        string unknownCsi = "\x1b[99z"; // 'z' is not a standard CSI final byte

        // Act
        _terminal.Write(unknownCsi);

        // Assert
        var traces = GetTraces();
        var csiTrace = traces.FirstOrDefault(t => t.Type == "CSI");
        
        Assert.That(csiTrace, Is.Not.Null, "Unknown CSI sequence should be traced");
        Assert.That(csiTrace.Direction, Is.EqualTo("output"));
        Assert.That(csiTrace.EscapeSequence, Does.Contain("\\x1b["));
        Assert.That(csiTrace.EscapeSequence, Does.Contain("z"));
    }

    [Test]
    public void UnknownOscSequence_IsTraced()
    {
        // Arrange - Send an unknown OSC sequence
        string unknownOsc = "\x1b]999;unknown data\x07"; // Command 999 is not implemented

        // Act
        _terminal.Write(unknownOsc);

        // Assert
        var traces = GetTraces();
        var oscTrace = traces.FirstOrDefault(t => t.Type == "OSC");
        
        Assert.That(oscTrace, Is.Not.Null, "Unknown OSC sequence should be traced");
        Assert.That(oscTrace.Direction, Is.EqualTo("output"));
        Assert.That(oscTrace.EscapeSequence, Does.Contain("\\x1b]"));
        Assert.That(oscTrace.EscapeSequence, Does.Contain("999"));
    }

    [Test]
    public void UnknownEscSequence_IsTraced()
    {
        // Arrange - Send an unknown ESC sequence
        string unknownEsc = "\x1bz"; // 'z' is not a standard ESC sequence

        // Act
        _terminal.Write(unknownEsc);

        // Assert
        var traces = GetTraces();
        var escTrace = traces.FirstOrDefault(t => t.Type == "ESC");
        
        Assert.That(escTrace, Is.Not.Null, "Unknown ESC sequence should be traced");
        Assert.That(escTrace.Direction, Is.EqualTo("output"));
        Assert.That(escTrace.EscapeSequence, Does.Contain("\\x1b"));
        Assert.That(escTrace.EscapeSequence, Does.Contain("z"));
    }

    [Test]
    public void UnknownDcsSequence_IsTraced()
    {
        // Arrange - Send an unknown DCS sequence
        string unknownDcs = "\x1bPzunknown data\x1b\\"; // 'z' is not a standard DCS command

        // Act
        _terminal.Write(unknownDcs);

        // Assert
        var traces = GetTraces();
        var dcsTrace = traces.FirstOrDefault(t => t.Type == "DCS");
        
        Assert.That(dcsTrace, Is.Not.Null, "Unknown DCS sequence should be traced");
        Assert.That(dcsTrace.Direction, Is.EqualTo("output"));
        Assert.That(dcsTrace.EscapeSequence, Does.Contain("\\x1bP"));
        Assert.That(dcsTrace.EscapeSequence, Does.Contain("z"));
    }

    [Test]
    public void MixedUnknownSequences_AllTraced()
    {
        // Arrange - Send multiple unknown sequences
        _terminal.Write("\x1b[99z");           // Unknown CSI
        _terminal.Write("\x1b]999;test\x07");  // Unknown OSC
        _terminal.Write("\x1bz");              // Unknown ESC
        _terminal.Write("\x1bPztest\x1b\\");   // Unknown DCS

        // Act & Assert
        var traces = GetTraces();
        
        Assert.That(traces.Count(t => t.Type == "CSI"), Is.EqualTo(1), "Should have one CSI trace");
        Assert.That(traces.Count(t => t.Type == "OSC"), Is.EqualTo(1), "Should have one OSC trace");
        Assert.That(traces.Count(t => t.Type == "ESC"), Is.EqualTo(1), "Should have one ESC trace");
        Assert.That(traces.Count(t => t.Type == "DCS"), Is.EqualTo(1), "Should have one DCS trace");
        
        // All should be output direction
        Assert.That(traces.All(t => t.Direction == "output"), Is.True, "All traces should be output direction");
    }

    [Test]
    public void KnownSequencesStillWork_WithUnknownSequences()
    {
        // Arrange - Mix known and unknown sequences
        _terminal.Write("\x1b[99z");           // Unknown CSI
        _terminal.Write("\x1b[H");             // Known CSI (cursor home)
        _terminal.Write("\x1b]999;test\x07");  // Unknown OSC
        _terminal.Write("\x1b]2;Title\x07");   // Known OSC (set title)

        // Act & Assert
        var traces = GetTraces();
        
        // Should have traces for both known and unknown sequences
        var csiTraces = traces.Where(t => t.Type == "CSI").ToList();
        Assert.That(csiTraces.Count, Is.EqualTo(2), "Should have two CSI traces");
        
        var oscTraces = traces.Where(t => t.Type == "OSC").ToList();
        Assert.That(oscTraces.Count, Is.EqualTo(2), "Should have two OSC traces");
        
        // Verify terminal still functions (cursor should be at home position)
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0));
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0));
    }
}