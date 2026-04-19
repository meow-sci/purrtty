using System.Reflection;
using caTTY.Core.Parsing;
using caTTY.Core.Terminal;
using caTTY.Core.Tests.Unit;
using caTTY.Core.Tracing;
using FsCheck;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
/// Property-based tests for terminal tracing functionality.
/// These tests verify universal properties that should hold for all valid inputs.
/// </summary>
[TestFixture]
[Category("Property")]
public class TerminalTracingProperties
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

        // Set unique database filename for this test using helper method
        _testDbFilename = TerminalTracer.SetupTestDatabase();

        // Reset the tracer to clean state
        TerminalTracer.Reset();

        // Enable tracing for tests
        TerminalTracer.Enabled = true;

        // Force initialization by making a trace call
        TerminalTracer.TraceEscape("INIT");

        // Clear the initialization trace
        ClearTraceDatabase();
    }

    [TearDown]
    public void TearDown()
    {
        // Restore original state
        TerminalTracer.Enabled = _originalEnabled;
        TerminalTracer.DbFilename = _originalDbFilename;
        TerminalTracer.Reset();

        // Clean up test database file
        if (_testDbFilename != null)
        {
            var databasePath = GetDatabasePath();
            if (databasePath != null && File.Exists(databasePath))
            {
                try
                {
                    File.Delete(databasePath);
                }
                catch
                {
                    // Ignore cleanup failures
                }
            }
        }
    }

    /// <summary>
    /// Generator for ESC sequences (non-CSI escape sequences).
    /// </summary>
    public static Arbitrary<string> EscSequenceArb =>
        Arb.From(Gen.Elements(new[]
        {
            "7", "8", "M", "D", "E", "H", "c", // Single-byte ESC sequences
            "(A", "(B", "(0", ")A", ")B", ")0", // Character set designation
            "*A", "*B", "+A", "+B", // More character set designations
            "=", ">", "N", "O" // Other ESC sequences
        }));

    /// <summary>
    /// Generator for valid escape sequences.
    /// </summary>
    public static Arbitrary<string> EscapeSequenceArb =>
        Arb.From(Gen.Elements(new[]
        {
            "ESC[H", "ESC[2J", "ESC[1;1H", "ESC]0;title\\x07", "ESC7", "ESCPtest ESC\\"
        }));

    /// <summary>
    /// Generator for DCS commands.
    /// </summary>
    public static Arbitrary<string> DcsCommandArb =>
        Arb.From(Gen.Elements(new[]
        {
            "q", "p", "s", "$q", "+q", "+p"
        }).Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>
    /// Generator for DCS parameters.
    /// </summary>
    public static Arbitrary<string> DcsParametersArb =>
        Arb.From(Gen.Elements(new[]
        {
            "", "1", "0;1", "1;2;3", "42"
        }));

    /// <summary>
    /// Generator for DCS data payloads.
    /// </summary>
    public static Arbitrary<string> DcsDataArb =>
        Arb.From(Gen.Elements(new[]
        {
            "", "test", "hello world", "data123", "complex;data;here"
        }));

    /// <summary>
    /// Generator for valid UTF-8 strings.
    /// </summary>
    public static Arbitrary<string> ValidUtf8StringArb =>
        Arb.From(Gen.Elements(new[]
        {
            "Hello", "café", "naïve", "résumé", "piñata", "αβγδε", "こんにちは", "你好世界",
            "🌟🚀💻🎉", "Ω≈∞∑∏", "Test αβγ 123", "A", "é", "中", "🎉"
        }));

    /// <summary>
    /// Generator for printable text.
    /// </summary>
    public static Arbitrary<string> PrintableTextArb =>
        Arb.From(Gen.Choose(1, 50).SelectMany(length =>
            Gen.ArrayOf(length, Gen.Choose(32, 126).Select(i => (char)i))
                .Select(chars => new string(chars))));

    /// <summary>
    /// Generator for OSC command numbers.
    /// </summary>
    public static Arbitrary<int> OscCommandArb =>
        Arb.From(Gen.Elements(new[]
        {
            0, 1, 2, 8, 10, 11, 21, 52
        }));

    /// <summary>
    /// Generator for OSC data payloads.
    /// </summary>
    public static Arbitrary<string> OscDataArb =>
        Arb.From(Gen.Elements(new[]
        {
            "", "title", "test data", "https://example.com", "clipboard data"
        }));

    /// <summary>
    /// Generator for wide characters (CJK, emoji, etc.).
    /// </summary>
    public static Arbitrary<char> WideCharacterArb =>
        Arb.From(Gen.Elements(new[]
        {
            // CJK Unified Ideographs
            '中', '国', '日', '本', '한', '국', '語', '言',
            // Hiragana
            'あ', 'い', 'う', 'え', 'お', 'か', 'き', 'く', 'け', 'こ',
            // Katakana
            'ア', 'イ', 'ウ', 'エ', 'オ', 'カ', 'キ', 'ク', 'ケ', 'コ',
            // Fullwidth forms
            'Ａ', 'Ｂ', 'Ｃ', '１', '２', '３',
            // CJK symbols
            '　', '、', '。', '「', '」'
        }));

    /// <summary>
    /// Generator for SGR sequences.
    /// </summary>
    public static Arbitrary<string> SgrSequenceArb =>
        Arb.From(Gen.Elements(new[]
        {
            "\x1b[0m",      // Reset
            "\x1b[1m",      // Bold
            "\x1b[3m",      // Italic
            "\x1b[4m",      // Underline
            "\x1b[7m",      // Inverse
            "\x1b[9m",      // Strikethrough
            "\x1b[31m",     // Red foreground
            "\x1b[42m",     // Green background
            "\x1b[1;31m",   // Bold red
            "\x1b[38;5;196m", // 256-color red
            "\x1b[38;2;255;128;64m", // RGB orange
            "\x1b[22m",     // Normal intensity
            "\x1b[39m",     // Default foreground
            "\x1b[49m"      // Default background
        }));

    /// <summary>
    /// Generator for trace directions.
    /// </summary>
    public static Arbitrary<TraceDirection> TraceDirectionArb =>
        Arb.From(Gen.Elements(TraceDirection.Input, TraceDirection.Output));

    /// <summary>
    /// Generator for control characters (0x00-0x1F, 0x7F).
    /// </summary>
    public static Arbitrary<byte> ControlCharacterArb =>
        Arb.From(Gen.Elements(new byte[]
        {
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, // NUL, SOH, STX, ETX, EOT, ENQ, ACK, BEL
            0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, // BS, HT, LF, VT, FF, CR, SO, SI
            0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, // DLE, DC1, DC2, DC3, DC4, NAK, SYN, ETB
            0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, // CAN, EM, SUB, ESC, FS, GS, RS, US
            0x7F // DEL
        }));

    /// <summary>
    /// **Feature: terminal-tracing-integration, Property 2: Control Character Tracing**
    /// **Validates: Requirements 1.5**
    /// Property: For any control character (0x00-0x1F, 0x7F) processed by the parser,
    /// the character should be traced using the appropriate control character name.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ControlCharacterTracing()
    {
        return Prop.ForAll(ControlCharacterArb, TraceDirectionArb, (controlByte, direction) =>
        {
            // Arrange - Clear any existing traces
            ClearTraceDatabase();

            // Act - Trace control character using TraceHelper
            TraceHelper.TraceControlChar(controlByte, direction);

            // Assert - Verify control character is recorded correctly
            var traces = GetTracesFromDatabaseWithFlush();
            if (traces.Count != 1) return false;

            var trace = traces[0];
            var expectedDirection = direction == TraceDirection.Input ? "input" : "output";

            // Build expected control character format based on TraceHelper.TraceControlChar implementation
            // New format uses \x{XX} hexadecimal notation
            var expectedSequence = $"\\x{controlByte:X2}";

            return trace.Escape == expectedSequence &&
                   trace.Direction == expectedDirection;
        });
    }

    /// <summary>
    /// **Feature: terminal-tracing-integration, Property 1: Escape Sequence Tracing Completeness (ESC portion)**
    /// **Validates: Requirements 1.3, 5.4**
    /// Property: For any valid ESC sequence processed by the parser, the sequence should appear
    /// in the trace database with correct sequence characters and direction information.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property EscSequenceTracingCompleteness()
    {
        return Prop.ForAll(EscSequenceArb, TraceDirectionArb, (escSequence, direction) =>
        {
            // Arrange - Clear any existing traces
            ClearTraceDatabase();

            // Act - Trace ESC sequence using TraceHelper
            TraceHelper.TraceEscSequence(escSequence, direction);

            // Assert - Verify ESC sequence is recorded correctly
            var traces = GetTracesFromDatabaseWithFlush();
            if (traces.Count != 1) return false;

            var trace = traces[0];
            var expectedDirection = direction == TraceDirection.Input ? "input" : "output";

            // Build expected ESC sequence format: "\x1bsequence"
            var expectedSequence = $"\\x1b{escSequence}";

            return trace.Escape == expectedSequence &&
                   trace.Direction == expectedDirection;
        });
    }

    /// <summary>
    /// **Feature: terminal-tracing-integration, Property 1: Escape Sequence Tracing Completeness (OSC portion)**
    /// **Validates: Requirements 1.2, 5.2**
    /// Property: For any valid OSC sequence processed by the parser, the sequence should appear
    /// in the trace database with correct command, data payload, and direction information.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OscSequenceTracingCompleteness()
    {
        return Prop.ForAll(OscCommandArb, OscDataArb, TraceDirectionArb, (command, data, direction) =>
        {
            // Arrange - Clear any existing traces
            ClearTraceDatabase();

            // Act - Trace OSC sequence using TraceHelper
            TraceHelper.TraceOscSequence(command, data, direction);

            // Assert - Verify OSC sequence is recorded correctly
            var traces = GetTracesFromDatabaseWithFlush();
            if (traces.Count != 1) return false;

            var trace = traces[0];
            var expectedDirection = direction == TraceDirection.Input ? "input" : "output";

            // Build expected OSC sequence format: "\x1b]command;data\x07"
            var expectedSequence = string.IsNullOrEmpty(data) ? $"\\x1b]{command}\\x07" : $"\\x1b]{command};{data}\\x07";

            return trace.Escape == expectedSequence &&
                   trace.Direction == expectedDirection;
        });
    }

    /// <summary>
    /// **Feature: terminal-tracing-integration, Property 1: Escape Sequence Tracing Completeness (DCS verification)**
    /// **Validates: Requirements 1.4, 5.5**
    /// Property: For any valid DCS sequence processed by the parser, the sequence should appear
    /// in the trace database with correct command, parameters, and direction information.
    /// </summary>
    [Test]
    public void DcsSequenceTracingCompletion_SimpleTest()
    {
        // Arrange - Clear any existing traces
        ClearTraceDatabase();

        // Create a mock handler to capture DCS messages
        var handlers = new TestParserHandlers();
        var options = new ParserOptions
        {
            Handlers = handlers,
            Logger = new TestLogger()
        };
        var parser = new Parser(options);

        // Build DCS sequence: ESC P 1 q ST (simple DCS query)
        var dcsSequence = "\x1bP1q\x1b\\";
        var dcsBytes = System.Text.Encoding.UTF8.GetBytes(dcsSequence);

        // Act - Process DCS sequence through parser
        parser.PushBytes(dcsBytes);

        // Flush buffered traces to database
        TerminalTracer.Flush();

        // Assert - First verify parser processed the DCS
        Assert.That(handlers.DcsMessages, Has.Count.EqualTo(1), "Parser should process DCS sequence");

        // Then verify tracing captured it
        var traces = GetTracesFromDatabaseWithFlush();
        Assert.That(traces, Has.Count.EqualTo(1), "Should have exactly one trace entry");

        var trace = traces[0];

        // Build expected DCS sequence format: "\x1bPparameterscommand\x1b\\"
        var expectedSequence = "\\x1bP1q\\x1b\\\\";

        Assert.That(trace.Escape, Is.EqualTo(expectedSequence), "Trace should contain correct DCS sequence");
        Assert.That(trace.Direction, Is.EqualTo("output"), "Direction should be output");
    }

    /// <summary>
    /// **Feature: terminal-tracing-integration, Property 1: Escape Sequence Tracing Completeness (DCS verification)**
    /// **Validates: Requirements 1.4, 5.5**
    /// Property: For any valid DCS sequence processed by the parser, the sequence should appear
    /// in the trace database with correct command, parameters, and direction information.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property DcsSequenceTracingCompletion()
    {
        return Prop.ForAll(DcsCommandArb, DcsParametersArb, TraceDirectionArb, (command, parameters, direction) =>
        {
            // Arrange - Clear any existing traces
            ClearTraceDatabase();

            // Act - Trace DCS sequence using TraceHelper (testing the helper method directly)
            TraceHelper.TraceDcsSequence(command, parameters, null, direction);

            // Assert - Verify DCS sequence is recorded correctly
            var traces = GetTracesFromDatabaseWithFlush();
            if (traces.Count != 1) return false;

            var trace = traces[0];
            var expectedDirection = direction == TraceDirection.Input ? "input" : "output";

            // Build expected DCS sequence format: "\x1bPparameterscommand\x1b\\"
            var expectedSequence = string.IsNullOrEmpty(parameters)
                ? $"\\x1bP{command}\\x1b\\\\"
                : $"\\x1bP{parameters}{command}\\x1b\\\\";

            return trace.Escape == expectedSequence &&
                   trace.Direction == expectedDirection;
        });
    }

    /// <summary>
    /// **Feature: terminal-tracing-integration, Property 7: Direction Tracking Accuracy**
    /// **Validates: Requirements 8.1, 8.2**
    /// Property: For any traced sequence or character, the direction field should correctly
    /// reflect whether the data originated from user input ("input") or program output ("output").
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property DirectionTrackingAccuracy()
    {
        return Prop.ForAll(EscapeSequenceArb, TraceDirectionArb, (escapeSequence, direction) =>
        {
            // Arrange - Clear any existing traces
            ClearTraceDatabase();

            // Act - Trace escape sequence with specific direction
            TerminalTracer.TraceEscape(escapeSequence, direction);

            // Assert - Verify direction is recorded correctly
            var traces = GetTracesFromDatabaseWithFlush();
            if (traces.Count != 1) return false;

            var trace = traces[0];
            var expectedDirection = direction == TraceDirection.Input ? "input" : "output";

            return trace.Escape == escapeSequence &&
                   trace.Direction == expectedDirection;
        });
    }

    /// <summary>
    /// **Feature: terminal-tracing-integration, Property 7b: Printable Direction Tracking**
    /// **Validates: Requirements 8.1, 8.2**
    /// Property: For any traced printable text, the direction field should correctly
    /// reflect the specified direction.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property PrintableDirectionTrackingAccuracy()
    {
        return Prop.ForAll(PrintableTextArb, TraceDirectionArb, (printableText, direction) =>
        {
            // Arrange - Clear any existing traces
            ClearTraceDatabase();

            // Act - Trace printable text with specific direction
            TerminalTracer.TracePrintable(printableText, direction);

            // Assert - Verify direction is recorded correctly
            var traces = GetTracesFromDatabaseWithFlush();
            if (traces.Count != 1) return false;

            var trace = traces[0];
            var expectedDirection = direction == TraceDirection.Input ? "input" : "output";

            return trace.Printable == printableText &&
                   trace.Direction == expectedDirection;
        });
    }

    /// <summary>
    /// **Feature: terminal-tracing-integration, Property 15: Default Direction Handling**
    /// **Validates: Requirements 10.5**
    /// Property: For any trace operation where direction is not explicitly specified,
    /// the system should use "output" as the default direction value.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property DefaultDirectionHandling()
    {
        return Prop.ForAll(EscapeSequenceArb, escapeSequence =>
        {
            // Arrange - Clear any existing traces
            ClearTraceDatabase();

            // Act - Trace escape sequence without specifying direction (should default to Output)
            TerminalTracer.TraceEscape(escapeSequence);

            // Assert - Verify direction defaults to "output"
            var traces = GetTracesFromDatabaseWithFlush();
            if (traces.Count != 1) return false;

            var trace = traces[0];

            return trace.Escape == escapeSequence &&
                   trace.Direction == "output";
        });
    }

    /// <summary>
    /// **Feature: terminal-tracing-integration, Property 3: Printable Character Tracing**
    /// **Validates: Requirements 2.1, 2.4**
    /// Property: For any printable character processed by the parser, the character should appear
    /// in the trace database with correct direction information.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property PrintableCharacterTracing()
    {
        var printableCharArb = Arb.From(Gen.Choose(32, 126).Select(i => (char)i));

        return Prop.ForAll(printableCharArb, character =>
        {
            // Arrange - Clear any existing traces and create terminal emulator
            ClearTraceDatabase();

            using var terminal = TerminalEmulator.Create(80, 24);

            // Act - Write character through the parser (which triggers tracing)
            // Convert character to bytes and process through Write method
            var characterBytes = System.Text.Encoding.UTF8.GetBytes(character.ToString());
            terminal.Write(characterBytes.AsSpan());

            // Assert - Verify character is traced with correct information
            var traces = GetTracesFromDatabaseWithFlush();
            if (traces.Count != 1) return false;

            var trace = traces[0];
            var expectedDirection = "output"; // Parser processing uses Output direction by default

            // The trace should contain just the character
            var expectedTrace = character.ToString();

            return trace.Printable == expectedTrace &&
                   trace.Direction == expectedDirection;
        });
    }

    /// <summary>
    /// **Feature: terminal-tracing-integration, Property 4: UTF-8 Character Tracing**
    /// **Validates: Requirements 2.2**
    /// Property: For any UTF-8 multi-byte sequence decoded by the terminal, the resulting character
    /// should be traced with correct Unicode representation.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property Utf8CharacterTracing()
    {
        return Prop.ForAll(ValidUtf8StringArb, TraceDirectionArb, (utf8Text, direction) =>
        {
            // Arrange - Clear any existing traces
            ClearTraceDatabase();

            // Act - Trace UTF-8 text using TraceHelper
            TraceHelper.TraceUtf8Text(utf8Text, direction);

            // Assert - Verify UTF-8 text is recorded correctly
            var traces = GetTracesFromDatabaseWithFlush();
            if (traces.Count != 1) return false;

            var trace = traces[0];
            var expectedDirection = direction == TraceDirection.Input ? "input" : "output";

            return trace.Printable == utf8Text &&
                   trace.Direction == expectedDirection;
        });
    }

    /// <summary>
    /// **Feature: terminal-tracing-integration, Property 15b: Default Direction for Printable**
    /// **Validates: Requirements 10.5**
    /// Property: For any printable trace operation where direction is not explicitly specified,
    /// the system should use "output" as the default direction value.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property DefaultDirectionHandlingForPrintable()
    {
        return Prop.ForAll(PrintableTextArb, printableText =>
        {
            // Arrange - Clear any existing traces
            ClearTraceDatabase();

            // Act - Trace printable text without specifying direction (should default to Output)
            TerminalTracer.TracePrintable(printableText);

            // Assert - Verify direction defaults to "output"
            var traces = GetTracesFromDatabaseWithFlush();
            if (traces.Count != 1) return false;

            var trace = traces[0];

            return trace.Printable == printableText &&
                   trace.Direction == "output";
        });
    }

    /// <summary>
    /// **Feature: terminal-tracing-integration, Property 5: Wide Character Tracing**
    /// **Validates: Requirements 2.3**
    /// Property: For any wide character (CJK, emoji) processed by the terminal, the character
    /// should be traced with appropriate width indication.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property WideCharacterTracing()
    {
        return Prop.ForAll(WideCharacterArb, TraceDirectionArb, (wideChar, direction) =>
        {
            // Arrange - Clear any existing traces
            ClearTraceDatabase();

            // Act - Trace wide character using TraceHelper
            TraceHelper.TraceWideCharacter(wideChar, direction);

            // Assert - Verify wide character is recorded with width indication
            var traces = GetTracesFromDatabaseWithFlush();
            if (traces.Count != 1) return false;

            var trace = traces[0];
            var expectedDirection = direction == TraceDirection.Input ? "input" : "output";

            // Build expected wide character format: "character (wide)"
            var expectedTrace = $"{wideChar} (wide)";

            return trace.Printable == expectedTrace &&
                   trace.Direction == expectedDirection;
        });
    }

    /// <summary>
    /// **Feature: terminal-tracing-integration, Property 6: SGR Sequence Tracing**
    /// **Validates: Requirements 5.3**
    /// Property: For any SGR (Select Graphic Rendition) sequence processed by the parser,
    /// the sequence should be traced with complete attribute change information.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property SgrSequenceTracing()
    {
        return Prop.ForAll(SgrSequenceArb, sgrSequence =>
        {
            // Arrange - Clear any existing traces and create terminal emulator
            ClearTraceDatabase();

            using var terminal = TerminalEmulator.Create(80, 24);

            // Get initial attributes before processing SGR
            var initialAttributes = terminal.AttributeManager.CurrentAttributes;

            // Act - Process SGR sequence through terminal emulator
            // This should trigger tracing through HandleSgrSequence
            var sgrBytes = System.Text.Encoding.UTF8.GetBytes(sgrSequence);
            terminal.Write(sgrBytes.AsSpan());

            // Assert - Verify SGR sequence is traced with attribute information
            var traces = GetTracesFromDatabaseWithFlush();
            if (traces.Count != 1) return false;

            var trace = traces[0];
            var expectedDirection = "output"; // SGR sequences are always Output direction

            // The trace should contain the formatted SGR sequence and attribute change information
            // Current format: "\x1b[params]m - attribute_changes"
            bool hasCorrectDirection = trace.Direction == expectedDirection;
            bool hasEscapeSequence = trace.Escape != null;

            // Check if trace contains the formatted sequence (with \x1b notation)
            // Extract parameters from raw sequence for comparison
            string expectedFormattedSequence = FormatSgrSequenceForTest(sgrSequence);
            bool containsFormattedSequence = trace.Escape?.Contains(expectedFormattedSequence) == true;

            // Check if trace contains attribute change information (indicated by ":" and "->")
            // Note: Some SGR sequences may not cause attribute changes (e.g., setting to current value)
            // In such cases, the trace will just contain the formatted sequence with " - " but no changes
            bool containsAttributeInfo = trace.Escape?.Contains(" - ") == true;

            return hasCorrectDirection && hasEscapeSequence && containsFormattedSequence && containsAttributeInfo;
        });
    }

    /// <summary>
    /// **Feature: terminal-tracing-integration, Property 12: Test Database Isolation**
    /// **Validates: Requirements 6.5**
    /// Property: Multiple test database instances should be completely isolated from each other,
    /// with each using separate UUID-based database files that don't interfere with each other.
    /// </summary>
    [Test]
    public void TestDatabaseIsolation()
    {
        const int numDatabases = 3;
        var databases = new List<TestTraceDatabase>();
        var testData = new List<string>();

        try
        {
            // Arrange - Create multiple isolated test databases
            for (int i = 0; i < numDatabases; i++)
            {
                var db = TestTraceDatabase.Create();
                databases.Add(db);

                // Generate unique test data for each database
                var uniqueData = $"test_data_{i}_{Guid.NewGuid():N}";
                testData.Add(uniqueData);
            }

            // Write unique data to each database separately
            for (int i = 0; i < numDatabases; i++)
            {
                var db = databases[i];
                var uniqueData = testData[i];

                // Store current state
                var originalFilename = TerminalTracer.DbFilename;
                var originalEnabled = TerminalTracer.Enabled;

                try
                {
                    // Configure tracer for this specific database
                    TerminalTracer.DbFilename = db.DatabaseFilename;
                    TerminalTracer.Reset();
                    TerminalTracer.Enabled = true;

                    // Clear any existing traces (including INIT traces)
                    TerminalTracer.Flush();
                    db.ClearDatabase();

                    // Now trace the unique data
                    TerminalTracer.TraceEscape(uniqueData);

                    // Flush buffered traces to ensure they're written to database
                    TerminalTracer.Flush();
                }
                finally
                {
                    // Always restore original state
                    TerminalTracer.DbFilename = originalFilename;
                    TerminalTracer.Enabled = originalEnabled;
                    TerminalTracer.Reset();
                }
            }

            // Act & Assert - Verify each database contains only its own data
            for (int i = 0; i < numDatabases; i++)
            {
                var db = databases[i];
                var expectedData = testData[i];

                // Get traces directly from the database without changing TerminalTracer state
                var traces = db.GetTraces();

                // Should have exactly one trace with the expected data
                Assert.That(traces, Has.Count.EqualTo(1), $"Database {i} should have exactly one trace");
                Assert.That(traces[0].EscapeSequence, Is.EqualTo(expectedData), $"Database {i} should contain its unique data");

                // Verify database filenames are unique
                for (int j = i + 1; j < numDatabases; j++)
                {
                    Assert.That(databases[i].DatabaseFilename, Is.Not.EqualTo(databases[j].DatabaseFilename),
                        $"Database {i} and {j} should have unique filenames");
                }

                // Verify database files exist and are separate
                Assert.That(db.DatabaseExists(), Is.True, $"Database {i} file should exist");

                // Verify other databases don't contain this data
                for (int j = 0; j < numDatabases; j++)
                {
                    if (i == j) continue;

                    var otherTraces = databases[j].GetTraces();
                    Assert.That(otherTraces.Any(t => t.EscapeSequence == expectedData), Is.False,
                        $"Database {j} should not contain data from database {i}");
                }
            }
        }
        finally
        {
            // Cleanup - Dispose all test databases
            foreach (var db in databases)
            {
                db.Dispose();
            }
        }
    }

    /// <summary>
    /// **Feature: terminal-tracing-integration, Property 16: Human-Readable Escape Sequence Formatting**
    /// **Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 12.7**
    /// Property: For any escape sequence traced to the database, the sequence should be formatted
    /// using consistent \x1b... notation with proper hexadecimal representation for control characters.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property HumanReadableEscapeSequenceFormatting()
    {
        return Prop.ForAll(EscapeSequenceTypeArb, TraceDirectionArb, (sequenceType, direction) =>
        {
            // Arrange - Clear any existing traces
            ClearTraceDatabase();

            // Act - Trace different types of escape sequences using TraceHelper
            switch (sequenceType.Type)
            {
                case "CSI":
                    TraceHelper.TraceCsiSequence(sequenceType.Command, sequenceType.Parameters, sequenceType.Prefix, direction);
                    break;
                case "OSC":
                    TraceHelper.TraceOscSequence(sequenceType.OscCommand, sequenceType.Data, direction);
                    break;
                case "ESC":
                    TraceHelper.TraceEscSequence(sequenceType.EscSequence, direction);
                    break;
                case "DCS":
                    TraceHelper.TraceDcsSequence(sequenceType.DcsCommand, sequenceType.Parameters, sequenceType.Data, direction);
                    break;
                case "CONTROL":
                    TraceHelper.TraceControlChar(sequenceType.ControlByte, direction);
                    break;
            }

            // Assert - Verify the sequence is formatted with \x1b notation
            var traces = GetTracesFromDatabaseWithFlush();
            if (traces.Count != 1) return false;

            var trace = traces[0];
            var expectedDirection = direction == TraceDirection.Input ? "input" : "output";

            if (trace.Direction != expectedDirection) return false;
            if (trace.Escape == null) return false;

            // Verify formatting based on sequence type
            switch (sequenceType.Type)
            {
                case "CSI":
                    // Should format as \x1b[parameters;command
                    if (!trace.Escape.StartsWith("\\x1b[")) return false;
                    if (!trace.Escape.Contains(sequenceType.Command.ToString())) return false;
                    if (!string.IsNullOrEmpty(sequenceType.Parameters) && !trace.Escape.Contains(sequenceType.Parameters)) return false;
                    if (sequenceType.Prefix.HasValue && !trace.Escape.Contains(sequenceType.Prefix.Value.ToString())) return false;
                    break;

                case "OSC":
                    // Should format as \x1b]command;data\x07
                    if (!trace.Escape.StartsWith("\\x1b]")) return false;
                    if (!trace.Escape.Contains(sequenceType.OscCommand.ToString())) return false;
                    if (!trace.Escape.EndsWith("\\x07")) return false;
                    if (!string.IsNullOrEmpty(sequenceType.Data) && !trace.Escape.Contains(sequenceType.Data)) return false;
                    break;

                case "ESC":
                    // Should format as \x1b followed by sequence characters
                    if (!trace.Escape.StartsWith("\\x1b")) return false;
                    if (!trace.Escape.Contains(sequenceType.EscSequence)) return false;
                    break;

                case "DCS":
                    // Should format as \x1bPparameterscommanddata\x1b\\
                    if (!trace.Escape.StartsWith("\\x1bP")) return false;
                    if (!trace.Escape.EndsWith("\\x1b\\\\")) return false;
                    if (!trace.Escape.Contains(sequenceType.DcsCommand)) return false;
                    if (!string.IsNullOrEmpty(sequenceType.Parameters) && !trace.Escape.Contains(sequenceType.Parameters)) return false;
                    if (!string.IsNullOrEmpty(sequenceType.Data) && !trace.Escape.Contains(sequenceType.Data)) return false;
                    break;

                case "CONTROL":
                    // Should format as \x{XX} hexadecimal notation
                    var expectedFormat = $"\\x{sequenceType.ControlByte:X2}";
                    if (trace.Escape != expectedFormat) return false;
                    break;
            }

            return true;
        });
    }

    /// <summary>
    /// Generator for different types of escape sequences to test formatting.
    /// </summary>
    public static Arbitrary<EscapeSequenceTestData> EscapeSequenceTypeArb =>
        Arb.From(Gen.OneOf(
            // CSI sequences
            Gen.Zip(
                Gen.Elements('A', 'B', 'C', 'D', 'H', 'J', 'K', 'm', 'n', 'r'),
                Gen.Zip(
                    Gen.OneOf(Gen.Constant(""), Gen.Elements("1", "1;2", "10;20")),
                    Gen.OneOf(Gen.Constant((char?)null), Gen.Elements('?', '>').Select(c => (char?)c))
                )
            ).Select(t => new EscapeSequenceTestData
            {
                Type = "CSI",
                Command = t.Item1,
                Parameters = t.Item2.Item1,
                Prefix = t.Item2.Item2
            }),

            // OSC sequences
            Gen.Zip(
                Gen.Choose(0, 999),
                Gen.OneOf(Gen.Constant(""), Gen.Elements("title", "icon", "color"))
            ).Select(t => new EscapeSequenceTestData
            {
                Type = "OSC",
                OscCommand = t.Item1,
                Data = t.Item2
            }),

            // ESC sequences
            Gen.Elements("7", "8", "D", "E", "M", "c").Select(seq => new EscapeSequenceTestData
            {
                Type = "ESC",
                EscSequence = seq
            }),

            // DCS sequences
            Gen.Zip(
                Gen.Elements("q", "p", "s"),
                Gen.Zip(
                    Gen.OneOf(Gen.Constant(""), Gen.Elements("1", "2;3")),
                    Gen.OneOf(Gen.Constant(""), Gen.Elements("data", "payload"))
                )
            ).Select(t => new EscapeSequenceTestData
            {
                Type = "DCS",
                DcsCommand = t.Item1,
                Parameters = t.Item2.Item1,
                Data = t.Item2.Item2
            }),

            // Control characters
            Gen.Elements((byte)0x00, (byte)0x07, (byte)0x08, (byte)0x09, (byte)0x0A, (byte)0x0D, (byte)0x1B, (byte)0x7F)
                .Select(b => new EscapeSequenceTestData
                {
                    Type = "CONTROL",
                    ControlByte = b
                })
        ));

    /// <summary>
    /// Test data structure for different escape sequence types.
    /// </summary>
    public class EscapeSequenceTestData
    {
        public string Type { get; set; } = "";
        public char Command { get; set; }
        public string? Parameters { get; set; }
        public char? Prefix { get; set; }
        public int OscCommand { get; set; }
        public string? Data { get; set; }
        public string EscSequence { get; set; } = "";
        public string DcsCommand { get; set; } = "";
        public byte ControlByte { get; set; }
    }

    private void ClearTraceDatabase()
    {
        // First flush any buffered traces
        TerminalTracer.Flush();

        var databasePath = GetDatabasePath();
        if (databasePath == null) return;

        try
        {
            var connectionString = $"Data Source={databasePath}";
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
    /// Helper method to flush traces and then get them from database.
    /// </summary>
    private List<TraceEntry> GetTracesFromDatabaseWithFlush()
    {
        // Flush any buffered traces first
        TerminalTracer.Flush();
        return GetTracesFromDatabase();
    }

    private List<TraceEntry> GetTracesFromDatabase()
    {
        var databasePath = GetDatabasePath();
        if (databasePath == null) return new List<TraceEntry>();

        var traces = new List<TraceEntry>();

        try
        {
            var connectionString = $"Data Source={databasePath}";

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var selectCommand = connection.CreateCommand();
            selectCommand.CommandText = "SELECT escape_seq, printable, direction FROM trace ORDER BY id";

            using var reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                traces.Add(new TraceEntry
                {
                    Escape = reader.IsDBNull(0) ? null : reader.GetString(0),
                    Printable = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Direction = reader.GetString(2)
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
    /// Formats an SGR sequence for test comparison by converting to \x1b notation.
    /// </summary>
    /// <param name="rawSgrSequence">Raw SGR sequence with actual ESC character</param>
    /// <returns>Formatted sequence with \x1b notation</returns>
    private static string FormatSgrSequenceForTest(string rawSgrSequence)
    {
        // Convert ESC character (0x1B) to \x1b notation
        return rawSgrSequence.Replace("\x1b", "\\x1b");
    }

    private string? GetDatabasePath()
    {
        // Get the database path from TerminalTracer
        return TerminalTracer.GetDatabasePath();
    }

    /// <summary>
    /// Property 17: Type Classification Accuracy
    /// For any traced entry, the type field should correctly classify the entry as CSI, OSC, ESC, DCS, SGR, printable, control, utf8, or wide based on the content.
    /// **Validates: Requirements 13.1-13.9**
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    [Category("Property")]
    public FsCheck.Property TypeClassification_ShouldAccuratelyClassifyTraceEntries()
    {
        return Prop.ForAll(TypeClassificationTestDataArb, (testData) =>
        {
            // Arrange - Clear any existing traces
            ClearTraceDatabase();

            // Act - Trace using the appropriate method based on test data type
            switch (testData.ExpectedType)
            {
                case "CSI":
                    TraceHelper.TraceCsiSequence(testData.CsiCommand, testData.Parameters, testData.Prefix);
                    break;
                case "OSC":
                    TraceHelper.TraceOscSequence(testData.OscCommand, testData.Data);
                    break;
                case "ESC":
                    TraceHelper.TraceEscSequence(testData.EscSequence);
                    break;
                case "DCS":
                    TraceHelper.TraceDcsSequence(testData.DcsCommand, testData.Parameters, testData.Data);
                    break;
                case "SGR":
                    TraceHelper.TraceSgrSequence(testData.SgrAttributes);
                    break;
                case "control":
                    TraceHelper.TraceControlChar(testData.ControlByte);
                    break;
                case "printable":
                    TraceHelper.TracePrintableChar(testData.PrintableChar);
                    break;
                case "utf8":
                    TraceHelper.TraceUtf8Text(testData.Utf8Text);
                    break;
                case "wide":
                    TraceHelper.TraceWideCharacter(testData.WideChar);
                    break;
            }

            // Assert - Verify the type field is correctly set
            var traces = GetTracesFromDatabaseWithType();
            if (traces.Count != 1)
            {
                Console.WriteLine($"Expected 1 trace, got {traces.Count}");
                return false;
            }

            var trace = traces[0];
            bool result = trace.Type == testData.ExpectedType;
            if (!result)
            {
                Console.WriteLine($"Type mismatch: Expected '{testData.ExpectedType}', got '{trace.Type}'");
                Console.WriteLine($"Escape: '{trace.Escape}'");
                Console.WriteLine($"Printable: '{trace.Printable}'");
            }
            return result;
        });
    }

    /// <summary>
    /// Generator for type classification test data.
    /// </summary>
    public static Arbitrary<TypeClassificationTestData> TypeClassificationTestDataArb =>
        Arb.From(Gen.OneOf(
            // CSI sequences
            Gen.Zip(
                Gen.Elements('A', 'B', 'C', 'D', 'H', 'J', 'K', 'm', 'n', 'r'),
                Gen.Zip(
                    Gen.OneOf(Gen.Constant(""), Gen.Elements("1", "1;2", "10;20")),
                    Gen.OneOf(Gen.Constant((char?)null), Gen.Elements('?', '>').Select(c => (char?)c))
                )
            ).Select(t => new TypeClassificationTestData
            {
                ExpectedType = "CSI",
                CsiCommand = t.Item1,
                Parameters = t.Item2.Item1,
                Prefix = t.Item2.Item2
            }),

            // OSC sequences
            Gen.Zip(
                Gen.Elements(0, 1, 2, 4, 10, 11, 12),
                Gen.OneOf(Gen.Constant(""), Gen.Elements("title", "test", "data"))
            ).Select(t => new TypeClassificationTestData
            {
                ExpectedType = "OSC",
                OscCommand = t.Item1,
                Data = t.Item2
            }),

            // ESC sequences
            Gen.Elements("7", "8", "M", "D", "E", "H", "c", "(A", "(B", "(0")
                .Select(seq => new TypeClassificationTestData
                {
                    ExpectedType = "ESC",
                    EscSequence = seq
                }),

            // DCS sequences
            Gen.Zip(
                Gen.Elements("q", "p", "s"),
                Gen.Zip(
                    Gen.OneOf(Gen.Constant(""), Gen.Elements("1", "2", "1;2")),
                    Gen.OneOf(Gen.Constant(""), Gen.Elements("test", "data"))
                )
            ).Select(t => new TypeClassificationTestData
            {
                ExpectedType = "DCS",
                DcsCommand = t.Item1,
                Parameters = t.Item2.Item1,
                Data = t.Item2.Item2
            }),

            // SGR sequences
            Gen.Elements("0", "1", "2", "3", "4", "7", "22", "23", "30", "31", "32", "39", "40", "41", "49")
                .Select(attr => new TypeClassificationTestData
                {
                    ExpectedType = "SGR",
                    SgrAttributes = attr
                }),

            // Control characters
            Gen.Elements((byte)0x07, (byte)0x08, (byte)0x09, (byte)0x0A, (byte)0x0D, (byte)0x1B, (byte)0x7F)
                .Select(b => new TypeClassificationTestData
                {
                    ExpectedType = "control",
                    ControlByte = b
                }),

            // Printable characters
            Gen.Elements('A', 'B', 'a', 'b', '1', '2', ' ', '!', '@', '#')
                .Select(c => new TypeClassificationTestData
                {
                    ExpectedType = "printable",
                    PrintableChar = c
                }),

            // UTF-8 text
            Gen.Elements("Hello", "世界", "🌍", "café", "naïve")
                .Select(text => new TypeClassificationTestData
                {
                    ExpectedType = "utf8",
                    Utf8Text = text
                }),

            // Wide characters (CJK characters that are single UTF-16 code units)
            Gen.Elements('世', '界', '中', '文', '日', '本')
                .Select(c => new TypeClassificationTestData
                {
                    ExpectedType = "wide",
                    WideChar = c
                })
        ));

    /// <summary>
    /// Get traces from database including type information.
    /// </summary>
    private List<TraceEntryWithType> GetTracesFromDatabaseWithType()
    {
        TerminalTracer.Flush(); // Ensure all traces are written

        var traces = new List<TraceEntryWithType>();
        var databasePath = GetDatabasePath();
        if (databasePath == null || !File.Exists(databasePath))
            return traces;

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT escape_seq, printable, direction, type FROM trace ORDER BY time";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            traces.Add(new TraceEntryWithType
            {
                Escape = reader.IsDBNull(0) ? null : reader.GetString(0),
                Printable = reader.IsDBNull(1) ? null : reader.GetString(1),
                Direction = reader.GetString(2),
                Type = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        }

        return traces;
    }

    /// <summary>
    /// Test data structure for type classification testing.
    /// </summary>
    public class TypeClassificationTestData
    {
        public string ExpectedType { get; set; } = "";

        // CSI data
        public char CsiCommand { get; set; }
        public string? Parameters { get; set; }
        public char? Prefix { get; set; }

        // OSC data
        public int OscCommand { get; set; }
        public string? Data { get; set; }

        // ESC data
        public string EscSequence { get; set; } = "";

        // DCS data
        public string DcsCommand { get; set; } = "";

        // SGR data
        public string SgrAttributes { get; set; } = "";

        // Control character data
        public byte ControlByte { get; set; }

        // Printable character data
        public char PrintableChar { get; set; }

        // UTF-8 text data
        public string Utf8Text { get; set; } = "";

        // Wide character data
        public char WideChar { get; set; }
    }

    private class TraceEntryWithType
    {
        public string? Escape { get; set; }
        public string? Printable { get; set; }
        public string Direction { get; set; } = "";
        public string? Type { get; set; }
    }

    private class TraceEntry
    {
        public string? Escape { get; set; }
        public string? Printable { get; set; }
        public string Direction { get; set; } = "";
    }
}
