using System;
using System.IO;
using System.Reflection;
using caTTY.Core.Terminal;
using caTTY.Core.Tracing;
using FsCheck;
using FsCheck.NUnit;
using NUnit.Framework;
using Microsoft.Data.Sqlite;

namespace caTTY.Core.Tests.Property;

/// <summary>
///     Property-based tests for unknown sequence tracing in the terminal parser.
///     Verifies that all unknown/unhandled sequences are properly traced for debugging.
/// </summary>
[TestFixture]
[Category("Property")]
public class UnknownSequenceTracingProperties
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
        catch (Exception)
        {
            // Return empty list if database read fails
        }

        return traces;
    }

    /// <summary>
    /// Record representing a trace entry from the database.
    /// </summary>
    private record TraceRecord(string? Type, string? EscapeSequence, string? PrintableText, string Direction, int? Row, int? Column);

    /// <summary>
    ///     Generator for unknown CSI sequences with random final bytes not in the implemented set.
    /// </summary>
    public static Arbitrary<string> UnknownCsiSequenceArb =>
        Arb.From(Gen.OneOf(
            // Unknown final bytes (avoid implemented ones like A, B, C, D, H, J, K, etc.)
            Gen.Elements('z', 'y', 'x', 'w', 'v', 'Q', 'R', 'V', 'W', 'Y', 'Z')
                .Select(final => $"\x1b[{final}"),
            // Unknown CSI with parameters
            Gen.Choose(1, 99)
                .SelectMany(param => Gen.Elements('z', 'y', 'x', 'w', 'v')
                    .Select(final => $"\x1b[{param}{final}"))
        ));

    /// <summary>
    ///     Generator for unknown OSC sequences with command numbers not in the implemented set.
    /// </summary>
    public static Arbitrary<string> UnknownOscSequenceArb =>
        Arb.From(Gen.OneOf(
            // High command numbers (avoid 0, 1, 2, 8, 52)
            Gen.Choose(100, 999)
                .Select(cmd => $"\x1b]{cmd};unknown data\x07"),
            // Invalid command numbers
            Gen.Choose(1000, 9999)
                .Select(cmd => $"\x1b]{cmd};test\x07")
        ));

    /// <summary>
    ///     Generator for unknown ESC sequences (non-CSI, non-OSC).
    /// </summary>
    public static Arbitrary<string> UnknownEscSequenceArb =>
        Arb.From(Gen.OneOf(
            // Unknown single-byte ESC sequences (avoid 7, 8, D, E, H, M, c)
            Gen.Elements('a', 'b', 'e', 'f', 'g', 'i', 'j', 'k', 'l', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z')
                .Select(c => $"\x1b{c}"),
            // Unknown multi-byte ESC sequences
            Gen.Elements('!', '"', '#', '$', '%', '&', '\'')
                .SelectMany(intermediate => Gen.Elements('a', 'b', 'e', 'f')
                    .Select(final => $"\x1b{intermediate}{final}"))
        ));

    /// <summary>
    ///     Generator for unknown DCS sequences.
    /// </summary>
    public static Arbitrary<string> UnknownDcsSequenceArb =>
        Arb.From(Gen.OneOf(
            // Unknown DCS commands
            Gen.Elements('z', 'y', 'x', 'w', 'v')
                .Select(cmd => $"\x1bP{cmd}unknown data\x1b\\"),
            // DCS with parameters
            Gen.Choose(1, 99)
                .SelectMany(param => Gen.Elements('z', 'y', 'x')
                    .Select(cmd => $"\x1bP{param};{cmd}test\x1b\\"))
        ));

    /// <summary>
    ///     **Feature: terminal-tracing-integration, Property 20: Unknown CSI sequence tracing**
    ///     **Validates: Requirements 1.1, 5.1**
    ///     Property: For any unknown CSI sequence processed by the parser, the sequence should
    ///     appear in the trace database with correct command and direction information.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property UnknownCsiSequencesAreTraced()
    {
        return Prop.ForAll(UnknownCsiSequenceArb, unknownCsi =>
        {
            try
            {
                // Act - Send unknown CSI sequence
                _terminal.Write(unknownCsi);

                // Assert - Verify sequence was traced
                var traces = GetTraces();
                bool hasExpectedTrace = traces.Any(trace =>
                    trace.Type == "CSI" &&
                    trace.Direction == "output" &&
                    trace.EscapeSequence != null &&
                    trace.EscapeSequence.Contains("\\x1b["));

                return hasExpectedTrace.Label($"Unknown CSI sequence '{unknownCsi}' should be traced");
            }
            catch (Exception)
            {
                return false.Label($"Unknown CSI sequence '{unknownCsi}' should not cause exceptions");
            }
        });
    }

    /// <summary>
    ///     **Feature: terminal-tracing-integration, Property 21: Unknown OSC sequence tracing**
    ///     **Validates: Requirements 1.2, 5.2**
    ///     Property: For any unknown OSC sequence processed by the parser, the sequence should
    ///     appear in the trace database with correct command and direction information.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property UnknownOscSequencesAreTraced()
    {
        return Prop.ForAll(UnknownOscSequenceArb, unknownOsc =>
        {
            try
            {
                // Act - Send unknown OSC sequence
                _terminal.Write(unknownOsc);

                // Assert - Verify sequence was traced
                var traces = GetTraces();
                bool hasExpectedTrace = traces.Any(trace =>
                    trace.Type == "OSC" &&
                    trace.Direction == "output" &&
                    trace.EscapeSequence != null &&
                    trace.EscapeSequence.Contains("\\x1b]"));

                return hasExpectedTrace.Label($"Unknown OSC sequence '{unknownOsc}' should be traced");
            }
            catch (Exception)
            {
                return false.Label($"Unknown OSC sequence '{unknownOsc}' should not cause exceptions");
            }
        });
    }

    /// <summary>
    ///     **Feature: terminal-tracing-integration, Property 22: Unknown ESC sequence tracing**
    ///     **Validates: Requirements 1.3, 5.4**
    ///     Property: For any unknown ESC sequence processed by the parser, the sequence should
    ///     appear in the trace database with correct sequence and direction information.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property UnknownEscSequencesAreTraced()
    {
        return Prop.ForAll(UnknownEscSequenceArb, unknownEsc =>
        {
            try
            {
                // Act - Send unknown ESC sequence
                _terminal.Write(unknownEsc);

                // Assert - Verify sequence was traced
                var traces = GetTraces();
                bool hasExpectedTrace = traces.Any(trace =>
                    trace.Type == "ESC" &&
                    trace.Direction == "output" &&
                    trace.EscapeSequence != null &&
                    trace.EscapeSequence.Contains("\\x1b"));

                return hasExpectedTrace.Label($"Unknown ESC sequence '{unknownEsc}' should be traced");
            }
            catch (Exception)
            {
                return false.Label($"Unknown ESC sequence '{unknownEsc}' should not cause exceptions");
            }
        });
    }

    /// <summary>
    ///     **Feature: terminal-tracing-integration, Property 23: Unknown DCS sequence tracing**
    ///     **Validates: Requirements 1.4, 5.5**
    ///     Property: For any unknown DCS sequence processed by the parser, the sequence should
    ///     appear in the trace database with correct command and direction information.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property UnknownDcsSequencesAreTraced()
    {
        return Prop.ForAll(UnknownDcsSequenceArb, unknownDcs =>
        {
            try
            {
                // Act - Send unknown DCS sequence
                _terminal.Write(unknownDcs);

                // Assert - Verify sequence was traced
                var traces = GetTraces();
                bool hasExpectedTrace = traces.Any(trace =>
                    trace.Type == "DCS" &&
                    trace.Direction == "output" &&
                    trace.EscapeSequence != null &&
                    trace.EscapeSequence.Contains("\\x1bP"));

                return hasExpectedTrace.Label($"Unknown DCS sequence '{unknownDcs}' should be traced");
            }
            catch (Exception)
            {
                return false.Label($"Unknown DCS sequence '{unknownDcs}' should not cause exceptions");
            }
        });
    }

    /// <summary>
    ///     **Feature: terminal-tracing-integration, Property 24: Mixed unknown sequences tracing**
    ///     **Validates: Requirements 1.1, 1.2, 1.3, 1.4**
    ///     Property: For any combination of unknown sequences, all should be traced correctly
    ///     without interfering with each other.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 50, QuietOnSuccess = true)]
    public FsCheck.Property MixedUnknownSequencesAreAllTraced()
    {
        return Prop.ForAll(UnknownCsiSequenceArb, UnknownOscSequenceArb, UnknownEscSequenceArb,
            (unknownCsi, unknownOsc, unknownEsc) =>
        {
            try
            {
                // Act - Send mixed unknown sequences
                _terminal.Write(unknownCsi);
                _terminal.Write(unknownOsc);
                _terminal.Write(unknownEsc);

                // Assert - Verify all sequences were traced
                var traces = GetTraces();

                bool hasCsiTrace = traces.Any(trace => trace.Type == "CSI" && trace.Direction == "output");
                bool hasOscTrace = traces.Any(trace => trace.Type == "OSC" && trace.Direction == "output");
                bool hasEscTrace = traces.Any(trace => trace.Type == "ESC" && trace.Direction == "output");

                return (hasCsiTrace && hasOscTrace && hasEscTrace)
                    .Label($"All unknown sequences should be traced: CSI={hasCsiTrace}, OSC={hasOscTrace}, ESC={hasEscTrace}");
            }
            catch (Exception)
            {
                return false.Label("Mixed unknown sequences should not cause exceptions");
            }
        });
    }
}
