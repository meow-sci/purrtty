using caTTY.Core.Tracing;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit;

/// <summary>
/// Tests for the new batched tracing system to verify it works correctly.
/// </summary>
[TestFixture]
[Category("Unit")]
public class BatchedTracingTests
{
    private bool _originalEnabled;
    private string? _originalDbFilename;
    private string? _testDbFilename;

    [SetUp]
    public void SetUp()
    {
        // Store original state
        _originalEnabled = TerminalTracer.Enabled;
        _originalDbFilename = TerminalTracer.DbFilename;
        
        // Set unique database filename for this test
        _testDbFilename = TerminalTracer.SetupTestDatabase();
        
        // Reset the tracer to clean state
        TerminalTracer.Reset();
        
        // Enable tracing for tests
        TerminalTracer.Enabled = true;
    }

    [TearDown]
    public void TearDown()
    {
        // Restore original state
        TerminalTracer.Enabled = _originalEnabled;
        TerminalTracer.DbFilename = _originalDbFilename;
        TerminalTracer.Reset();
        
        // Clean up test database file
        try
        {
            var dbPath = TerminalTracer.GetDatabasePath();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }

    [Test]
    public void BatchedTracing_ShouldBufferEntries()
    {
        // Act - Add some trace entries (should be buffered)
        TerminalTracer.TraceEscape("ESC[H");
        TerminalTracer.TracePrintable("Hello");
        TerminalTracer.TraceEscape("ESC[2J");
        
        // Assert - Should have buffered entries
        Assert.That(TerminalTracer.BufferedEntryCount, Is.GreaterThan(0), "Should have buffered entries");
        Assert.That(TerminalTracer.BufferedDataSize, Is.GreaterThan(0), "Should have buffered data");
    }

    [Test]
    public void BatchedTracing_FlushShouldWriteToDatabase()
    {
        // Arrange - Add some trace entries
        TerminalTracer.TraceEscape("ESC[H");
        TerminalTracer.TracePrintable("Hello");
        TerminalTracer.TraceEscape("ESC[2J");
        
        // Verify entries are buffered
        Assert.That(TerminalTracer.BufferedEntryCount, Is.GreaterThan(0), "Should have buffered entries before flush");
        
        // Act - Flush the buffer
        TerminalTracer.Flush();
        
        // Assert - Buffer should be empty and data should be in database
        Assert.That(TerminalTracer.BufferedEntryCount, Is.EqualTo(0), "Buffer should be empty after flush");
        Assert.That(TerminalTracer.BufferedDataSize, Is.EqualTo(0), "Buffer size should be zero after flush");
        
        // Verify data is in database
        var dbPath = TerminalTracer.GetDatabasePath();
        Assert.That(File.Exists(dbPath), Is.True, "Database file should exist");
    }

    [Test]
    public void BatchedTracing_AutoFlushOnBufferSize()
    {
        // Act - Add many entries to trigger auto-flush
        for (int i = 0; i < 150; i++) // More than MaxBufferSize (100)
        {
            TerminalTracer.TracePrintable($"Char{i}");
        }
        
        // Assert - Buffer should have been auto-flushed
        Assert.That(TerminalTracer.BufferedEntryCount, Is.LessThan(150), "Buffer should have been auto-flushed");
        
        // Verify database exists (indicating flush occurred)
        var dbPath = TerminalTracer.GetDatabasePath();
        Assert.That(File.Exists(dbPath), Is.True, "Database file should exist after auto-flush");
    }

    [Test]
    public void BatchedTracing_AutoFlushOnNewlines()
    {
        // Act - Add entry with newline (should trigger immediate flush)
        TerminalTracer.TracePrintable("Hello\nWorld");
        
        // Assert - Should have flushed immediately
        Assert.That(TerminalTracer.BufferedEntryCount, Is.EqualTo(0), "Buffer should be empty after newline flush");
        
        // Verify database exists
        var dbPath = TerminalTracer.GetDatabasePath();
        Assert.That(File.Exists(dbPath), Is.True, "Database file should exist after newline flush");
    }

    [Test]
    public void BatchedTracing_DisabledShouldNotBuffer()
    {
        // Arrange - Disable tracing
        TerminalTracer.Enabled = false;
        
        // Act - Try to trace
        TerminalTracer.TraceEscape("ESC[H");
        TerminalTracer.TracePrintable("Hello");
        
        // Assert - Should not have buffered anything
        Assert.That(TerminalTracer.BufferedEntryCount, Is.EqualTo(0), "Should not buffer when disabled");
        Assert.That(TerminalTracer.BufferedDataSize, Is.EqualTo(0), "Should not have buffered data when disabled");
    }

    [Test]
    public void BatchedTracing_TypeParameterShouldBeStoredInDatabase()
    {
        // Act - Add trace entries with type parameters
        TerminalTracer.TraceEscape("\\x1b[H", TraceDirection.Output, 0, 0, "CSI");
        TerminalTracer.TracePrintable("Hello", TraceDirection.Output, 0, 1, "printable");
        TerminalTracer.Trace("\\x1b]0;Title\\x07", null, TraceDirection.Output, 0, 6, "OSC");
        
        // Flush to database
        TerminalTracer.Flush();
        
        // Assert - Database should exist and contain type information
        var dbPath = TerminalTracer.GetDatabasePath();
        Assert.That(File.Exists(dbPath), Is.True, "Database file should exist");
        
        // Verify the database schema includes type column by attempting to read it
        // This is a basic verification that the type parameter functionality is working
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        
        var command = connection.CreateCommand();
        command.CommandText = "SELECT type, escape_seq, printable FROM trace ORDER BY id";
        
        using var reader = command.ExecuteReader();
        
        // Verify first entry (CSI)
        Assert.That(reader.Read(), Is.True, "Should have first trace entry");
        Assert.That(reader["type"], Is.EqualTo("CSI"), "First entry should have CSI type");
        Assert.That(reader["escape_seq"], Is.EqualTo("\\x1b[H"), "First entry should have correct escape sequence");
        
        // Verify second entry (printable)
        Assert.That(reader.Read(), Is.True, "Should have second trace entry");
        Assert.That(reader["type"], Is.EqualTo("printable"), "Second entry should have printable type");
        Assert.That(reader["printable"], Is.EqualTo("Hello"), "Second entry should have correct printable text");
        
        // Verify third entry (OSC)
        Assert.That(reader.Read(), Is.True, "Should have third trace entry");
        Assert.That(reader["type"], Is.EqualTo("OSC"), "Third entry should have OSC type");
        Assert.That(reader["escape_seq"], Is.EqualTo("\\x1b]0;Title\\x07"), "Third entry should have correct escape sequence");
    }
}