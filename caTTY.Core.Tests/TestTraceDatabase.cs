using System.Reflection;
using Microsoft.Data.Sqlite;
using caTTY.Core.Tracing;

namespace caTTY.Core.Tests;

/// <summary>
/// Helper class for managing test-specific trace databases with UUID-based filenames
/// and automatic cleanup mechanisms. Provides isolation between test runs.
/// </summary>
public class TestTraceDatabase : IDisposable
{
    private readonly string _testDbFilename;
    private readonly string? _originalDbFilename;
    private readonly bool _originalEnabled;
    private readonly string _databasePath;
    private bool _disposed = false;

    /// <summary>
    /// Gets the unique database filename for this test instance.
    /// </summary>
    public string DatabaseFilename => _testDbFilename;

    /// <summary>
    /// Gets the full path to the test database file.
    /// </summary>
    public string DatabasePath => _databasePath;

    /// <summary>
    /// Initializes a new test database with UUID-based filename in the assembly directory.
    /// Automatically configures TerminalTracer to use the test database.
    /// </summary>
    public TestTraceDatabase()
    {
        // Store original state for restoration
        _originalEnabled = TerminalTracer.Enabled;
        _originalDbFilename = TerminalTracer.DbFilename;

        // Generate unique database filename using UUID
        _testDbFilename = $"test_{Guid.NewGuid():N}.db";
        
        // Calculate the full database path independently
        string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        _databasePath = Path.Combine(dllDir, _testDbFilename);
        
        // Configure TerminalTracer to use test database
        TerminalTracer.DbFilename = _testDbFilename;
        TerminalTracer.Reset();
        TerminalTracer.Enabled = true;
        
        // Force initialization by making a trace call, then clear it
        TerminalTracer.TraceEscape("INIT");
        ClearDatabase();
    }

    /// <summary>
    /// Clears all trace entries from the test database.
    /// </summary>
    public void ClearDatabase()
    {
        if (string.IsNullOrEmpty(_databasePath)) return;

        try
        {
            var connectionString = $"Data Source={_databasePath}";
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var clearCommand = connection.CreateCommand();
            clearCommand.CommandText = "DELETE FROM trace";
            clearCommand.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Table might not exist yet, ignore
        }
    }

    /// <summary>
    /// Retrieves all trace entries from the test database.
    /// </summary>
    /// <returns>List of trace entries ordered by ID</returns>
    public List<TraceEntry> GetTraces()
    {
        if (string.IsNullOrEmpty(_databasePath)) return new List<TraceEntry>();

        var traces = new List<TraceEntry>();

        try
        {
            var connectionString = $"Data Source={_databasePath}";
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var selectCommand = connection.CreateCommand();
            selectCommand.CommandText = "SELECT escape_seq, printable, direction, row, col FROM trace ORDER BY id";

            using var reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                traces.Add(new TraceEntry
                {
                    EscapeSequence = reader.IsDBNull(0) ? null : reader.GetString(0),
                    PrintableText = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Direction = reader.GetString(2),
                    Row = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    Column = reader.IsDBNull(4) ? null : reader.GetInt32(4)
                });
            }
        }
        catch (SqliteException)
        {
            // Table might not exist yet, return empty list
        }

        return traces;
    }

    /// <summary>
    /// Retrieves trace entries filtered by direction.
    /// </summary>
    /// <param name="direction">The direction to filter by</param>
    /// <returns>List of trace entries with the specified direction</returns>
    public List<TraceEntry> GetTracesByDirection(TraceDirection direction)
    {
        var directionString = direction == TraceDirection.Input ? "input" : "output";
        return GetTraces().Where(t => t.Direction == directionString).ToList();
    }

    /// <summary>
    /// Checks if the test database exists and is accessible.
    /// </summary>
    /// <returns>True if the database file exists and can be accessed</returns>
    public bool DatabaseExists()
    {
        return !string.IsNullOrEmpty(_databasePath) && File.Exists(_databasePath);
    }

    /// <summary>
    /// Gets the count of trace entries in the database.
    /// </summary>
    /// <returns>Number of trace entries</returns>
    public int GetTraceCount()
    {
        return GetTraces().Count;
    }

    /// <summary>
    /// Disposes the test database and restores original TerminalTracer state.
    /// Automatically cleans up the test database file.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            // Restore original TerminalTracer state
            TerminalTracer.Enabled = _originalEnabled;
            TerminalTracer.DbFilename = _originalDbFilename;
            TerminalTracer.Reset();

            // Clean up test database file
            if (!string.IsNullOrEmpty(_databasePath) && File.Exists(_databasePath))
            {
                try
                {
                    File.Delete(_databasePath);
                }
                catch
                {
                    // Ignore cleanup failures - test framework will handle orphaned files
                }
            }
        }
        finally
        {
            _disposed = true;
        }
    }

    /// <summary>
    /// Creates a test database instance for use in using statements.
    /// Provides automatic cleanup when the using block exits.
    /// </summary>
    /// <returns>A new TestTraceDatabase instance</returns>
    public static TestTraceDatabase Create()
    {
        return new TestTraceDatabase();
    }
}

/// <summary>
/// Represents a trace entry retrieved from the test database.
/// </summary>
public class TraceEntry
{
    /// <summary>
    /// The escape sequence that was traced, if any.
    /// </summary>
    public string? EscapeSequence { get; set; }

    /// <summary>
    /// The printable text that was traced, if any.
    /// </summary>
    public string? PrintableText { get; set; }

    /// <summary>
    /// The direction of the trace entry ("input" or "output").
    /// </summary>
    public string Direction { get; set; } = "";

    /// <summary>
    /// The cursor row position when the trace was made, if available.
    /// </summary>
    public int? Row { get; set; }

    /// <summary>
    /// The cursor column position when the trace was made, if available.
    /// </summary>
    public int? Column { get; set; }

    /// <summary>
    /// Legacy property for backward compatibility with existing tests.
    /// </summary>
    public string? Escape => EscapeSequence;

    /// <summary>
    /// Legacy property for backward compatibility with existing tests.
    /// </summary>
    public string? Printable => PrintableText;
}