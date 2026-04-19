using System.Reflection;
using Microsoft.Data.Sqlite;

namespace caTTY.Core.Tracing;

/// <summary>
/// Represents the direction of data flow in terminal tracing.
/// </summary>
public enum TraceDirection
{
  /// <summary>
  /// Data flowing from user/application into the terminal (keyboard input, paste operations).
  /// </summary>
  Input,

  /// <summary>
  /// Data flowing from the running program to the terminal display (program output, escape sequences).
  /// </summary>
  Output
}

/// <summary>
/// Represents a buffered trace entry for batch processing.
/// </summary>
internal record TraceEntry(
  long Timestamp,
  string? EscapeSequence,
  string? PrintableText,
  TraceDirection Direction,
  int? Row,
  int? Col,
  string? Type
);

/// <summary>
/// Terminal tracing system that logs escape sequences and printable characters to SQLite database.
/// Uses batching and periodic flushing for optimal performance.
/// </summary>
public static class TerminalTracer
{
  private static SqliteConnection? _connection;
  private static readonly object _lock = new();
  private static bool _initialized = false;
  private static bool _disposed = false;

  // Batching system
  private static readonly List<TraceEntry> _traceBuffer = new();
  private static readonly Timer _flushTimer = new(FlushBufferCallback, null, 100, 100); // 100ms intervals
  private const int MaxBufferSize = 100; // Flush when buffer reaches this size
  private const int MaxBufferBytes = 8192; // Flush when buffer data exceeds this size
  private static int _currentBufferBytes = 0;

  /// <summary>
  /// Gets or sets whether tracing is enabled. When false, all tracing operations are no-ops.
  /// Default is false for performance.
  /// </summary>
  public static bool Enabled { get; set; } = false;

  /// <summary>
  /// Gets or sets the database path override. If set, this path will be used instead of the assembly directory.
  /// </summary>
  public static string? DbPath { get; set; } = null;

  /// <summary>
  /// Gets or sets the database filename override. If set, this filename will be used instead of "catty_trace.db".
  /// </summary>
  public static string? DbFilename { get; set; } = null;

  /// <summary>
  /// Helper method for test cases to set up a unique database filename and return it.
  /// This is a convenience method that generates a UUID-based filename and sets DbFilename.
  /// </summary>
  /// <returns>The generated unique database filename</returns>
  public static string SetupTestDatabase()
  {
    var testFilename = $"{Guid.NewGuid():N}.db";
    DbFilename = testFilename;
    return testFilename;
  }

  /// <summary>
  /// Periodic flush callback that processes buffered trace entries.
  /// </summary>
  private static void FlushBufferCallback(object? state)
  {
    if (!Enabled) return;
    
    lock (_lock)
    {
      FlushBufferInternal();
    }
  }

  /// <summary>
  /// Internal method to flush all buffered trace entries to the database.
  /// Must be called within a lock.
  /// </summary>
  private static void FlushBufferInternal()
  {
    if (_traceBuffer.Count == 0 || _disposed)
      return;

    if (!_initialized)
      Initialize();

    if (_connection == null || !_initialized)
    {
      // Clear buffer if we can't write to database
      _traceBuffer.Clear();
      _currentBufferBytes = 0;
      return;
    }

    try
    {
      // Use a transaction for better performance with multiple inserts
      using var transaction = _connection.BeginTransaction();
      
      var command = _connection.CreateCommand();
      command.Transaction = transaction;
      command.CommandText = @"
        INSERT INTO trace (time, type, escape_seq, printable, direction, row, col) 
        VALUES (@time, @type, @escape, @printable, @direction, @row, @col)";

      // Prepare parameters once
      var timeParam = command.Parameters.Add("@time", SqliteType.Integer);
      var typeParam = command.Parameters.Add("@type", SqliteType.Text);
      var escapeParam = command.Parameters.Add("@escape", SqliteType.Text);
      var printableParam = command.Parameters.Add("@printable", SqliteType.Text);
      var directionParam = command.Parameters.Add("@direction", SqliteType.Text);
      var rowParam = command.Parameters.Add("@row", SqliteType.Integer);
      var colParam = command.Parameters.Add("@col", SqliteType.Integer);

      // Insert all buffered entries
      foreach (var entry in _traceBuffer)
      {
        timeParam.Value = entry.Timestamp;
        typeParam.Value = entry.Type ?? (object)DBNull.Value;
        escapeParam.Value = entry.EscapeSequence ?? (object)DBNull.Value;
        printableParam.Value = entry.PrintableText ?? (object)DBNull.Value;
        directionParam.Value = entry.Direction == TraceDirection.Input ? "input" : "output";
        rowParam.Value = entry.Row ?? (object)DBNull.Value;
        colParam.Value = entry.Col ?? (object)DBNull.Value;

        command.ExecuteNonQuery();
      }

      transaction.Commit();
      
      // Clear buffer after successful write
      _traceBuffer.Clear();
      _currentBufferBytes = 0;
    }
    catch (Exception ex)
    {
      // Silently fail to avoid breaking terminal functionality
      Console.Error.WriteLine($"Failed to flush trace buffer: {ex.Message}");
      
      // Clear buffer to prevent memory buildup on persistent failures
      _traceBuffer.Clear();
      _currentBufferBytes = 0;
    }
  }

  /// <summary>
  /// Adds a trace entry to the buffer and flushes if necessary.
  /// </summary>
  private static void BufferTraceEntry(string? escapeSequence, string? printableText, TraceDirection direction, int? row, int? col, string? type = null)
  {
    if (!Enabled || (string.IsNullOrEmpty(escapeSequence) && string.IsNullOrEmpty(printableText)))
      return;

    lock (_lock)
    {
      if (_disposed)
        return;

      var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
      var entry = new TraceEntry(timestamp, escapeSequence, printableText, direction, row, col, type);
      
      _traceBuffer.Add(entry);
      
      // Estimate buffer size (rough calculation for memory management)
      _currentBufferBytes += (escapeSequence?.Length ?? 0) + (printableText?.Length ?? 0) + (type?.Length ?? 0) + 50; // 50 bytes overhead per entry
      
      // Immediate flush conditions for responsiveness
      bool shouldFlush = _traceBuffer.Count >= MaxBufferSize ||
                        _currentBufferBytes >= MaxBufferBytes ||
                        (printableText?.Contains('\n') == true) || // Flush on newlines for better debugging
                        (escapeSequence?.Contains("CSI") == true); // Flush on important escape sequences
      
      if (shouldFlush)
      {
        FlushBufferInternal();
      }
      // Otherwise, timer will flush within 100ms
    }
  }

  /// <summary>
  /// Reset the tracing system to a clean state. This will close any existing connections,
  /// reset all state variables, and prepare the tracer for reuse.
  /// </summary>
  public static void Reset()
  {
    lock (_lock)
    {
      try
      {
        // Flush any remaining buffered entries before reset
        FlushBufferInternal();
        
        _connection?.Close();
        _connection?.Dispose();
        _connection = null;
      }
      catch (Exception ex)
      {
        Console.Error.WriteLine($"Failed to close connection during reset: {ex.Message}");
      }

      // Clear buffers
      _traceBuffer.Clear();
      _currentBufferBytes = 0;
      _initialized = false;
      _disposed = false;
    }
  }

  /// <summary>
  /// Initialize the tracing database. Called automatically on first trace.
  /// </summary>
  public static void Initialize()
  {
    lock (_lock)
    {
      if (_initialized || _disposed)
        return;

      try
      {
        string dllDir = DbPath ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        string filename = DbFilename ?? "catty_trace.db";

        var dbPath = Path.Combine(dllDir, filename);
        var connectionString = $"Data Source={dbPath}";

        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        // Create trace table if it doesn't exist
        var createTableCommand = _connection.CreateCommand();
        createTableCommand.CommandText = @"
                    CREATE TABLE IF NOT EXISTS trace (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        time INTEGER NOT NULL,
                        type TEXT,
                        escape_seq TEXT,
                        printable TEXT,
                        direction TEXT NOT NULL DEFAULT 'output',
                        row INTEGER,
                        col INTEGER
                    )";
        createTableCommand.ExecuteNonQuery();

        _initialized = true;
      }
      catch (Exception ex)
      {
        // Silently fail initialization to avoid breaking terminal functionality
        Console.Error.WriteLine($"Failed to initialize terminal tracer: {ex.Message}");
      }
    }
  }

  /// <summary>
  /// Log an escape sequence to the trace database.
  /// </summary>
  /// <param name="escapeSequence">The escape sequence to log</param>
  /// <param name="direction">The direction of data flow (default: Output)</param>
  /// <param name="row">The cursor row position (0-based, nullable)</param>
  /// <param name="col">The cursor column position (0-based, nullable)</param>
  /// <param name="type">The type classification of the trace entry (nullable)</param>
  public static void TraceEscape(string escapeSequence, TraceDirection direction = TraceDirection.Output, int? row = null, int? col = null, string? type = null)
  {
    BufferTraceEntry(escapeSequence, null, direction, row, col, type);
  }

  /// <summary>
  /// Log printable characters to the trace database.
  /// </summary>
  /// <param name="printableText">The printable text to log</param>
  /// <param name="direction">The direction of data flow (default: Output)</param>
  /// <param name="row">The cursor row position (0-based, nullable)</param>
  /// <param name="col">The cursor column position (0-based, nullable)</param>
  /// <param name="type">The type classification of the trace entry (nullable)</param>
  public static void TracePrintable(string printableText, TraceDirection direction = TraceDirection.Output, int? row = null, int? col = null, string? type = null)
  {
    BufferTraceEntry(null, printableText, direction, row, col, type);
  }

  /// <summary>
  /// Log both escape sequence and printable text (for combined entries).
  /// </summary>
  /// <param name="escapeSequence">The escape sequence to log</param>
  /// <param name="printableText">The printable text to log</param>
  /// <param name="direction">The direction of data flow (default: Output)</param>
  /// <param name="row">The cursor row position (0-based, nullable)</param>
  /// <param name="col">The cursor column position (0-based, nullable)</param>
  /// <param name="type">The type classification of the trace entry (nullable)</param>
  public static void Trace(string? escapeSequence, string? printableText, TraceDirection direction = TraceDirection.Output, int? row = null, int? col = null, string? type = null)
  {
    BufferTraceEntry(escapeSequence, printableText, direction, row, col, type);
  }

  /// <summary>
  /// Legacy internal trace method - now redirects to buffering system.
  /// Kept for backward compatibility.
  /// </summary>
  private static void TraceInternal(string? escapeSequence, string? printableText, TraceDirection direction, int? row = null, int? col = null)
  {
    BufferTraceEntry(escapeSequence, printableText, direction, row, col, null);
  }

  /// <summary>
  /// Shutdown the tracing system and close database connections.
  /// Call this during application shutdown.
  /// </summary>
  public static void Shutdown()
  {
    lock (_lock)
    {
      if (_disposed)
        return;

      try
      {
        // Flush any remaining buffered entries
        FlushBufferInternal();
        
        // Dispose timer
        _flushTimer?.Dispose();
        
        // Close database connection
        _connection?.Close();
        _connection?.Dispose();
        _connection = null;
        _disposed = true;
      }
      catch (Exception ex)
      {
        Console.Error.WriteLine($"Failed to shutdown terminal tracer: {ex.Message}");
      }
      finally
      {
        // Ensure buffers are cleared even if flush fails
        _traceBuffer.Clear();
        _currentBufferBytes = 0;
      }
    }
  }

  /// <summary>
  /// Get the current database file path for debugging purposes.
  /// </summary>
  /// <returns>The path to the SQLite database file, or null if not initialized</returns>
  public static string GetDatabasePath()
  {
    lock (_lock)
    {
      string dllDir = DbPath ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
      string filename = DbFilename ?? "catty_trace.db";
      return Path.Combine(dllDir, filename);
    }
  }

  /// <summary>
  /// Check if the tracer is currently active and ready to log.
  /// </summary>
  public static bool IsActive => Enabled && _initialized && !_disposed && _connection != null;

  /// <summary>
  /// Manually flush all buffered trace entries to the database.
  /// Useful for testing or ensuring data is written before shutdown.
  /// </summary>
  public static void Flush()
  {
    if (!Enabled) return;
    
    lock (_lock)
    {
      FlushBufferInternal();
    }
  }

  /// <summary>
  /// Get the current number of buffered trace entries.
  /// Useful for monitoring and testing.
  /// </summary>
  public static int BufferedEntryCount
  {
    get
    {
      lock (_lock)
      {
        return _traceBuffer.Count;
      }
    }
  }

  /// <summary>
  /// Get the estimated size of buffered data in bytes.
  /// Useful for monitoring memory usage.
  /// </summary>
  public static int BufferedDataSize
  {
    get
    {
      lock (_lock)
      {
        return _currentBufferBytes;
      }
    }
  }
}