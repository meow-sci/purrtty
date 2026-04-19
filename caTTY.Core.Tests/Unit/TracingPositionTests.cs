using NUnit.Framework;
using caTTY.Core.Terminal;
using caTTY.Core.Tracing;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Reflection;

namespace caTTY.Core.Tests.Unit;

/// <summary>
/// Tests to verify that tracing includes cursor position information.
/// </summary>
[TestFixture]
[Category("Unit")]
public class TracingPositionTests
{
    private string? _testDbPath;
    private TerminalEmulator? _terminal;

    [SetUp]
    public void SetUp()
    {
        // Create unique test database
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        var testId = Guid.NewGuid().ToString("N");
        _testDbPath = Path.Combine(assemblyDir, $"test_trace_position_{testId}.db");
        
        TerminalTracer.DbPath = assemblyDir;
        TerminalTracer.DbFilename = Path.GetFileName(_testDbPath);
        TerminalTracer.Enabled = true;
        TerminalTracer.Reset();
        
        // Create terminal emulator
        _terminal = TerminalEmulator.Create(80, 24);
    }

    [TearDown]
    public void TearDown()
    {
        TerminalTracer.Enabled = false;
        TerminalTracer.Reset();
        
        // Clean up test database
        if (_testDbPath != null && File.Exists(_testDbPath))
        {
            try
            {
                File.Delete(_testDbPath);
            }
            catch
            {
                // Ignore cleanup failures
            }
        }
        
        _terminal?.Dispose();
    }

    [Test]
    public void TracePrintableCharacter_ShouldIncludeRowAndColumn()
    {
        // Arrange
        Assert.That(_terminal, Is.Not.Null);
        
        // Move cursor to position (5, 10)
        _terminal.Write("\x1b[6;11H"); // CSI 6;11 H (1-based positioning)
        
        // Act - Write a character at the cursor position
        _terminal.Write("X");
        
        // Flush buffered traces to database
        TerminalTracer.Flush();
        
        // Assert - Check that the trace includes the correct position
        using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        connection.Open();
        
        var command = connection.CreateCommand();
        command.CommandText = "SELECT printable, row, col FROM trace WHERE printable IS NOT NULL ORDER BY id DESC LIMIT 1";
        
        using var reader = command.ExecuteReader();
        Assert.That(reader.Read(), Is.True, "Should have at least one printable trace entry");
        
        var printable = reader.GetString(0);
        var row = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
        var col = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
        
        Assert.That(printable, Does.Contain("X"), "Should trace the character X");
        Assert.That(row, Is.EqualTo(5), "Should trace the correct row position (0-based)");
        Assert.That(col, Is.EqualTo(10), "Should trace the correct column position (0-based)");
    }

    [Test]
    public void TraceCsiSequence_ShouldIncludeRowAndColumn()
    {
        // Arrange
        Assert.That(_terminal, Is.Not.Null);
        
        // Move cursor to position (3, 7)
        _terminal.Write("\x1b[4;8H"); // CSI 4;8 H (1-based positioning)
        
        // Act - Send a CSI sequence
        _terminal.Write("\x1b[2J"); // Clear screen
        
        // Flush buffered traces to database
        TerminalTracer.Flush();
        
        // Assert - Check that the CSI trace includes the correct position
        using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        connection.Open();
        
        var command = connection.CreateCommand();
        command.CommandText = "SELECT escape_seq, row, col FROM trace WHERE escape_seq LIKE '%\\x1b[%J%' ORDER BY id DESC LIMIT 1";
        
        using var reader = command.ExecuteReader();
        Assert.That(reader.Read(), Is.True, "Should have at least one CSI trace entry");
        
        var escapeSeq = reader.GetString(0);
        var row = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
        var col = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
        
        Assert.That(escapeSeq, Does.Contain("\\x1b["), "Should trace \\x1b[ sequence");
        Assert.That(escapeSeq, Does.Contain("J"), "Should trace the J command");
        Assert.That(row, Is.EqualTo(3), "Should trace the correct row position (0-based)");
        Assert.That(col, Is.EqualTo(7), "Should trace the correct column position (0-based)");
    }
}