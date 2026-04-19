# Terminal Tracing System

This tracing system provides SQLite-based logging of terminal escape sequences and printable characters for debugging and analysis purposes.

## Features

- **SQLite Database**: Stores traces in a local SQLite database file
- **Automatic Initialization**: Database and table creation handled automatically
- **Thread-Safe**: Safe for concurrent access from multiple threads
- **Graceful Failure**: Tracing failures don't break terminal functionality
- **Flexible API**: Multiple convenience methods for different trace types

## Database Schema

The trace database contains a single table:

```sql
CREATE TABLE trace (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    time INTEGER NOT NULL,        -- Unix timestamp in milliseconds
    escape TEXT,                  -- Escape sequence (nullable)
    printable TEXT                -- Printable characters (nullable)
);
```

## Basic Usage

### Enabling/Disabling Tracing

```csharp
using caTTY.Core.Tracing;

// Enable tracing (disabled by default for performance)
TerminalTracer.Enabled = true;

// Disable tracing
TerminalTracer.Enabled = false;

// Check if tracing is enabled
if (TerminalTracer.Enabled)
{
    // Tracing operations will execute
}
```

### Simple Tracing

```csharp
using caTTY.Core.Tracing;

// Trace an escape sequence
TerminalTracer.TraceEscape("CSI 2 J");

// Trace printable text
TerminalTracer.TracePrintable("Hello World");

// Trace both in one call
TerminalTracer.Trace("ESC c", "Terminal Reset");
```

### Helper Methods

```csharp
using caTTY.Core.Tracing;

// Trace raw bytes as escape sequence
TraceHelper.TraceEscapeBytes(new byte[] { 0x1B, 0x5B, 0x32, 0x4A });

// Trace control characters with names
TraceHelper.TraceControlChar(0x0A); // Logs "<LF>"

// Trace CSI sequences with parameters
TraceHelper.TraceCsiSequence('H', "1;1"); // Logs "CSI 1;1 H"

// Trace OSC sequences
TraceHelper.TraceOscSequence(2, "Terminal Title"); // Logs "OSC 2 Terminal Title"
```

### Integration Example

```csharp
public class MyParser
{
    public void ProcessInput(ReadOnlySpan<byte> data)
    {
        // Trace raw input
        TracingIntegration.TraceInputBytes(data);
        
        // ... your parsing logic ...
        
        // Trace parsed sequences
        TracingIntegration.TraceParsedCsi('H', new[] { 1, 1 });
    }
}
```

### Application Shutdown

Register the shutdown hook during application startup:

```csharp
// In your Main method or startup code
TracingShutdown.RegisterShutdownHook();
```

Or manually shutdown when needed:

```csharp
// Manual shutdown
TerminalTracer.Shutdown();
```

## Database Location

The SQLite database is created at:
- **Windows**: `%TEMP%\catty_trace.db`
- **Linux/macOS**: `/tmp/catty_trace.db`

You can get the current path programmatically:

```csharp
string? dbPath = TerminalTracer.GetDatabasePath();
Console.WriteLine($"Trace database: {dbPath}");
```

## Querying Traces

You can query the database directly using any SQLite tool:

```sql
-- Get recent escape sequences
SELECT datetime(time/1000, 'unixepoch') as timestamp, escape 
FROM trace 
WHERE escape IS NOT NULL 
ORDER BY time DESC 
LIMIT 100;

-- Get printable text
SELECT datetime(time/1000, 'unixepoch') as timestamp, printable 
FROM trace 
WHERE printable IS NOT NULL 
ORDER BY time DESC 
LIMIT 100;

-- Get all traces in chronological order
SELECT 
    datetime(time/1000, 'unixepoch') as timestamp,
    COALESCE(escape, '') as escape,
    COALESCE(printable, '') as printable
FROM trace 
ORDER BY time ASC;
```

## Performance Considerations

- **Tracing is disabled by default** for optimal performance
- When disabled (`TerminalTracer.Enabled = false`), all tracing methods return immediately with minimal overhead
- When enabled, tracing adds some overhead but database writes are handled gracefully
- Database writes are synchronous but failures are handled gracefully
- Consider disabling tracing in production builds if performance is critical
- The database file will grow over time - consider periodic cleanup

**Recommendation**: Only enable tracing during development, debugging, or when specifically needed for analysis.

## Thread Safety

All tracing methods are thread-safe and can be called from multiple threads concurrently. The internal SQLite connection is protected by locks.

## Error Handling

The tracing system is designed to fail gracefully:
- Database initialization failures are logged but don't throw exceptions
- Individual trace write failures are logged but don't interrupt program flow
- If tracing fails, terminal functionality continues normally

## Conditional Compilation

You can conditionally compile tracing code using preprocessor directives:

```csharp
#if DEBUG
TerminalTracer.Enabled = true;
TerminalTracer.TraceEscape("Debug trace");
#endif
```

Or use the Enabled property for runtime control:

```csharp
// Enable tracing only in debug builds
#if DEBUG
TerminalTracer.Enabled = true;
#endif

// This will only execute if enabled
TerminalTracer.TraceEscape("Runtime controlled trace");
```