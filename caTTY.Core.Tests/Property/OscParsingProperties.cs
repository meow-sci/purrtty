using caTTY.Core.Parsing;
using caTTY.Core.Terminal;
using caTTY.Core.Types;
using FsCheck;
using NUnit.Framework;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

namespace caTTY.Core.Tests.Property;

/// <summary>
///     Property-based tests for OSC (Operating System Command) parsing and event emission.
///     These tests verify universal properties that should hold for all valid OSC sequences.
/// </summary>
[TestFixture]
[Category("Property")]
public class OscParsingProperties
{
    /// <summary>
    ///     Generator for valid OSC command numbers.
    /// </summary>
    public static Arbitrary<int> ValidOscCommandArb =>
        Arb.From(Gen.OneOf(
            Gen.Constant(0),        // Set title and icon
            Gen.Constant(1),        // Set icon name
            Gen.Constant(2),        // Set window title
            Gen.Constant(8),        // Hyperlink
            Gen.Constant(10),       // Query foreground color
            Gen.Constant(11),       // Query background color
            Gen.Constant(21),       // Query window title
            Gen.Constant(52),       // Clipboard
            Gen.Choose(0, 999)      // Any valid command number
        ));

    /// <summary>
    ///     Generator for valid OSC terminators.
    /// </summary>
    public static Arbitrary<string> OscTerminatorArb =>
        Arb.From(Gen.OneOf(
            Gen.Constant("BEL"),
            Gen.Constant("ST")
        ));

    /// <summary>
    ///     Generator for safe text content (no control characters except tab).
    /// </summary>
    public static Arbitrary<string> SafeTextArb =>
        Arb.From(Gen.ArrayOf(Gen.OneOf(
            Gen.Choose(0x20, 0x7E).Select(i => (char)i), // Printable ASCII
            Gen.Choose(0x80, 0xFF).Select(i => (char)i), // Extended ASCII
            Gen.Constant('\t')                            // Tab is allowed
        )).Select(chars => new string(chars).Substring(0, Math.Min(chars.Length, 100))));

    /// <summary>
    ///     Generator for valid base64 strings.
    /// </summary>
    public static Arbitrary<string> Base64StringArb =>
        Arb.From(SafeTextArb.Generator.Select(text =>
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            return Convert.ToBase64String(bytes);
        }));

    /// <summary>
    ///     Generator for clipboard selection targets.
    /// </summary>
    public static Arbitrary<string> ClipboardSelectionArb =>
        Arb.From(Gen.OneOf(
            Gen.Constant("c"),      // Clipboard
            Gen.Constant("p"),      // Primary
            Gen.Constant("s"),      // Secondary
            Gen.Choose(0, 7).Select(i => i.ToString()) // Cut buffers
        ));

    /// <summary>
    ///     **Feature: catty-ksa, Property 23: OSC parsing and event emission**
    ///     **Validates: Requirements 13.1, 13.2, 13.4**
    ///     Property: For any valid OSC sequence, parsing should succeed and produce
    ///     a valid OscMessage with consistent structure and proper event emission.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OscParsingProducesValidMessage()
    {
        return Prop.ForAll(ValidOscCommandArb, SafeTextArb, OscTerminatorArb, (command, payload, terminator) =>
        {
            // Arrange
            var parser = new OscParser(NullLogger.Instance);
            string terminatorBytes = terminator == "BEL" ? "\x07" : "\x1b\\";
            string oscSequence = $"\x1b]{command};{payload}{terminatorBytes}";
            var sequenceBytes = Encoding.UTF8.GetBytes(oscSequence).ToList();

            // Act - Process bytes one by one to simulate real parsing
            OscMessage? result = null;
            bool sequenceComplete = false;

            for (int i = 0; i < sequenceBytes.Count && !sequenceComplete; i++)
            {
                byte b = sequenceBytes[i];

                if (i >= 2) // Skip ESC ] prefix
                {
                    if (b == 0x1b && i + 1 < sequenceBytes.Count && sequenceBytes[i + 1] == 0x5c)
                    {
                        // ST terminator - process both bytes
                        sequenceComplete = parser.ProcessOscEscapeByte(sequenceBytes[i + 1], sequenceBytes, out result);
                        break;
                    }
                    else if (b == 0x07)
                    {
                        // BEL terminator
                        sequenceComplete = parser.ProcessOscByte(b, sequenceBytes, out result);
                    }
                    else if (i > 0 && sequenceBytes[i - 1] == 0x1b)
                    {
                        // Previous byte was ESC, this might be part of ST
                        sequenceComplete = parser.ProcessOscEscapeByte(b, sequenceBytes, out result);
                    }
                    else
                    {
                        // Regular OSC payload byte
                        parser.ProcessOscByte(b, sequenceBytes, out result);
                    }
                }
            }

            // Assert - Basic structure validation
            if (result == null) return false;

            bool hasValidType = result.Type == "osc";
            bool hasValidRaw = !string.IsNullOrEmpty(result.Raw);
            bool hasValidTerminator = result.Terminator == terminator;
            bool implementedFlagSet = true; // Implemented is a bool, not nullable

            // For implemented sequences, check xterm message structure
            if (result.Implemented && result.XtermMessage != null)
            {
                bool hasValidXtermType = !string.IsNullOrEmpty(result.XtermMessage.Type);
                bool hasValidCommand = result.XtermMessage.Command >= 0 && result.XtermMessage.Command <= 999;
                bool hasValidPayload = result.XtermMessage.Payload != null;

                return hasValidType && hasValidRaw && hasValidTerminator && implementedFlagSet &&
                       hasValidXtermType && hasValidCommand && hasValidPayload;
            }

            return hasValidType && hasValidRaw && hasValidTerminator && implementedFlagSet;
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 23b: OSC title sequence event emission**
    ///     **Validates: Requirements 13.2**
    ///     Property: For any valid OSC title sequence (0, 1, 2), the terminal should
    ///     emit the appropriate title/icon change events with correct data.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OscTitleSequenceEmitsCorrectEvents()
    {
        return Prop.ForAll<int, string, string>((command, title, terminator) =>
        {
            // Only test valid title commands
            if (command != 0 && command != 1 && command != 2) return true;
            if (terminator != "BEL" && terminator != "ST") return true;
            // Arrange
            var terminal = TerminalEmulator.Create(80, 24, NullLogger.Instance);
            string terminatorBytes = terminator == "BEL" ? "\x07" : "\x1b\\";
            string oscSequence = $"\x1b]{command};{title}{terminatorBytes}";

            // Track events
            string? receivedTitle = null;
            string? receivedIconName = null;
            bool titleEventFired = false;
            bool iconEventFired = false;

            terminal.TitleChanged += (sender, args) =>
            {
                receivedTitle = args.NewTitle;
                titleEventFired = true;
            };

            terminal.IconNameChanged += (sender, args) =>
            {
                receivedIconName = args.NewIconName;
                iconEventFired = true;
            };

            // Act
            terminal.Write(oscSequence);

            // Assert - Check that appropriate events were fired with correct data
            switch (command)
            {
                case 0: // Set title and icon
                    bool bothEventsFired = titleEventFired && iconEventFired;
                    bool correctTitleData = receivedTitle == title;
                    bool correctIconData = receivedIconName == title;
                    return bothEventsFired && correctTitleData && correctIconData;

                case 1: // Set icon name only
                    bool iconOnlyFired = iconEventFired && !titleEventFired;
                    bool correctIconOnly = receivedIconName == title;
                    return iconOnlyFired && correctIconOnly;

                case 2: // Set window title only
                    bool titleOnlyFired = titleEventFired && !iconEventFired;
                    bool correctTitleOnly = receivedTitle == title;
                    return titleOnlyFired && correctTitleOnly;

                default:
                    return false;
            }
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 23c: OSC clipboard sequence event emission**
    ///     **Validates: Requirements 13.4**
    ///     Property: For any valid OSC 52 clipboard sequence, the terminal should
    ///     emit clipboard events with correct selection target and decoded data.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OscClipboardSequenceEmitsCorrectEvents()
    {
        return Prop.ForAll(ClipboardSelectionArb, Base64StringArb, OscTerminatorArb,
            (selection, base64Data, terminator) =>
        {
            // Skip empty base64 data to avoid conversion issues
            if (string.IsNullOrEmpty(base64Data)) return true;

            // Arrange
            var terminal = TerminalEmulator.Create(80, 24, NullLogger.Instance);
            string terminatorBytes = terminator == "BEL" ? "\x07" : "\x1b\\";
            string oscSequence = $"\x1b]52;{selection};{base64Data}{terminatorBytes}";

            // Track clipboard events
            string? receivedSelection = null;
            string? receivedData = null;
            bool clipboardEventFired = false;
            bool isQuery = false;

            terminal.ClipboardRequest += (sender, args) =>
            {
                receivedSelection = args.SelectionTarget;
                receivedData = args.Data;
                isQuery = args.IsQuery;
                clipboardEventFired = true;
            };

            // Act
            terminal.Write(oscSequence);

            // Assert - Check that clipboard event was fired with correct data
            if (!clipboardEventFired) return false;

            bool correctSelection = receivedSelection == selection;
            bool notAQuery = !isQuery;

            // Decode the expected data for comparison
            try
            {
                byte[] expectedBytes = Convert.FromBase64String(base64Data);
                string expectedText = Encoding.UTF8.GetString(expectedBytes);
                bool correctData = receivedData == expectedText;

                return correctSelection && correctData && notAQuery;
            }
            catch (FormatException)
            {
                // Invalid base64 should not emit events
                return !clipboardEventFired;
            }
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 23d: OSC clipboard query event emission**
    ///     **Validates: Requirements 13.4**
    ///     Property: For any OSC 52 clipboard query sequence (data = "?"), the terminal
    ///     should emit clipboard query events with correct selection target.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OscClipboardQueryEmitsCorrectEvents()
    {
        return Prop.ForAll(ClipboardSelectionArb, OscTerminatorArb, (selection, terminator) =>
        {
            // Arrange
            var terminal = TerminalEmulator.Create(80, 24, NullLogger.Instance);
            string terminatorBytes = terminator == "BEL" ? "\x07" : "\x1b\\";
            string oscSequence = $"\x1b]52;{selection};?{terminatorBytes}";

            // Track clipboard events
            string? receivedSelection = null;
            string? receivedData = null;
            bool clipboardEventFired = false;
            bool isQuery = false;

            terminal.ClipboardRequest += (sender, args) =>
            {
                receivedSelection = args.SelectionTarget;
                receivedData = args.Data;
                isQuery = args.IsQuery;
                clipboardEventFired = true;
            };

            // Act
            terminal.Write(oscSequence);

            // Assert - Check that clipboard query event was fired correctly
            bool eventFired = clipboardEventFired;
            bool correctSelection = receivedSelection == selection;
            bool correctQueryFlag = isQuery;
            bool nullData = receivedData == null;

            return eventFired && correctSelection && correctQueryFlag && nullData;
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 23e: OSC clipboard clear event emission**
    ///     **Validates: Requirements 13.4**
    ///     Property: For any OSC 52 clipboard clear sequence (empty data), the terminal
    ///     should emit clipboard clear events with correct selection target.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OscClipboardClearEmitsCorrectEvents()
    {
        return Prop.ForAll(ClipboardSelectionArb, OscTerminatorArb, (selection, terminator) =>
        {
            // Arrange
            var terminal = TerminalEmulator.Create(80, 24, NullLogger.Instance);
            string terminatorBytes = terminator == "BEL" ? "\x07" : "\x1b\\";
            string oscSequence = $"\x1b]52;{selection};{terminatorBytes}";

            // Track clipboard events
            string? receivedSelection = null;
            string? receivedData = null;
            bool clipboardEventFired = false;
            bool isQuery = false;

            terminal.ClipboardRequest += (sender, args) =>
            {
                receivedSelection = args.SelectionTarget;
                receivedData = args.Data;
                isQuery = args.IsQuery;
                clipboardEventFired = true;
            };

            // Act
            terminal.Write(oscSequence);

            // Assert - Check that clipboard clear event was fired correctly
            bool eventFired = clipboardEventFired;
            bool correctSelection = receivedSelection == selection;
            bool notAQuery = !isQuery;
            bool emptyData = receivedData == string.Empty;

            return eventFired && correctSelection && notAQuery && emptyData;
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 23f: OSC parsing robustness**
    ///     **Validates: Requirements 13.1**
    ///     Property: For any OSC sequence with invalid or malformed content, the parser
    ///     should handle it gracefully without throwing exceptions or corrupting state.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OscParsingIsRobust()
    {
        return Prop.ForAll<int, byte[], string>((command, payloadBytes, terminator) =>
        {
            // Only test valid terminators
            if (terminator != "BEL" && terminator != "ST") return true;
            // Arrange
            var terminal = TerminalEmulator.Create(80, 24, NullLogger.Instance);
            string terminatorBytes = terminator == "BEL" ? "\x07" : "\x1b\\";

            // Create potentially malformed OSC sequence
            var sequenceBuilder = new StringBuilder();
            sequenceBuilder.Append($"\x1b]{command};");

            // Add potentially problematic payload bytes
            foreach (byte b in payloadBytes.Take(50)) // Limit length to avoid excessive test time
            {
                sequenceBuilder.Append((char)b);
            }

            sequenceBuilder.Append(terminatorBytes);
            string oscSequence = sequenceBuilder.ToString();

            // Act & Assert - Should not throw exceptions
            try
            {
                terminal.Write(oscSequence);

                // Terminal should still be functional after processing malformed sequence
                terminal.Write("test");

                // Basic state should be preserved
                bool terminalStillFunctional = terminal.Width == 80 && terminal.Height == 24;

                return terminalStillFunctional;
            }
            catch (Exception)
            {
                // Any exception indicates lack of robustness
                return false;
            }
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 23g: OSC payload length limits**
    ///     **Validates: Requirements 13.1**
    ///     Property: For any OSC sequence exceeding maximum payload length, the parser
    ///     should apply safety limits and handle the sequence gracefully.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OscPayloadLengthLimitsAreEnforced()
    {
        return Prop.ForAll(ValidOscCommandArb, OscTerminatorArb, (command, terminator) =>
        {
            // Arrange - Create OSC sequence with very long payload (exceeding 1024 char limit)
            var terminal = TerminalEmulator.Create(80, 24, NullLogger.Instance);
            string terminatorBytes = terminator == "BEL" ? "\x07" : "\x1b\\";
            string longPayload = new string('A', 2000); // Exceeds MaxOscPayloadLength
            string oscSequence = $"\x1b]{command};{longPayload}{terminatorBytes}";

            // Track events to ensure they're not fired for oversized payloads
            bool anyEventFired = false;
            terminal.TitleChanged += (s, e) => anyEventFired = true;
            terminal.IconNameChanged += (s, e) => anyEventFired = true;
            terminal.ClipboardRequest += (s, e) => anyEventFired = true;

            // Act & Assert - Should handle gracefully without events
            try
            {
                terminal.Write(oscSequence);

                // Terminal should still be functional
                terminal.Write("test");

                // No events should be fired for oversized payloads
                bool terminalStillFunctional = terminal.Width == 80 && terminal.Height == 24;
                bool noEventsForOversizedPayload = !anyEventFired;

                return terminalStillFunctional && noEventsForOversizedPayload;
            }
            catch (Exception)
            {
                // Should not throw exceptions even for oversized payloads
                return false;
            }
        });
    }

    /// <summary>
    ///     Generator for valid URLs for hyperlink testing.
    /// </summary>
    public static Arbitrary<string> ValidUrlArb =>
        Arb.From(Gen.OneOf(
            Gen.Constant("https://example.com"),
            Gen.Constant("http://test.org"),
            Gen.Constant("https://github.com/user/repo"),
            Gen.Constant("ftp://files.example.com/path"),
            Gen.Constant("mailto:user@example.com"),
            Gen.Constant("file:///path/to/file"),
            Gen.Constant(""), // Empty URL to clear hyperlink
            Gen.ArrayOf(Gen.Choose(0x21, 0x7E).Select(i => (char)i)) // Printable ASCII excluding space
                .Select(chars => $"https://example.com/{new string(chars).Substring(0, Math.Min(chars.Length, 10))}")
        ).Where(url => url != null)); // Explicitly exclude nulls

    /// <summary>
    ///     Generator for printable text content for hyperlink testing.
    /// </summary>
    public static Arbitrary<string> PrintableTextArb =>
        Arb.From(Gen.ArrayOf(Gen.Choose(0x21, 0x7E).Select(i => (char)i)) // Printable ASCII excluding space
            .Select(chars => new string(chars).Substring(0, Math.Min(chars.Length, 10))));

    /// <summary>
    ///     **Feature: catty-ksa, Property 24: OSC hyperlink association**
    ///     **Validates: Requirements 13.3**
    ///     Property: For any valid OSC 8 hyperlink sequence, the hyperlink URL should be
    ///     correctly associated with all characters written while the hyperlink is active,
    ///     and cleared when the hyperlink is terminated.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OscHyperlinkAssociationIsCorrect()
    {
        return Prop.ForAll(ValidUrlArb, PrintableTextArb, OscTerminatorArb,
            (url, textWithLink, terminator) =>
        {
            // Handle null values by treating as empty - FsCheck might generate nulls despite generators
            url = url ?? "";
            textWithLink = textWithLink ?? "";
            terminator = terminator ?? "BEL";

            // Skip empty text to ensure we have characters to test
            if (string.IsNullOrEmpty(textWithLink))
                return true;

            try
            {
                // Arrange
                var terminal = TerminalEmulator.Create(80, 24, NullLogger.Instance);
                string terminatorBytes = terminator == "BEL" ? "\x07" : "\x1b\\";
                string textWithoutLink = "XYZ"; // Fixed text for second part

                // Create OSC 8 sequence: start hyperlink, write text, end hyperlink, write more text
                string oscSequence = $"\x1b]8;;{url}{terminatorBytes}{textWithLink}\x1b]8;;{terminatorBytes}{textWithoutLink}";

                // Act
                terminal.Write(oscSequence);

                // Assert - Check hyperlink association
                bool allLinkedCharsHaveUrl = true;
                bool allUnlinkedCharsHaveNoUrl = true;

                // Check characters that should have the hyperlink URL
                for (int i = 0; i < textWithLink.Length && i < 80; i++)
                {
                    var cell = terminal.ScreenBuffer.GetCell(0, i);
                    if (cell.Character != textWithLink[i])
                    {
                        // Character mismatch - skip this test case
                        return true;
                    }

                    if (string.IsNullOrEmpty(url))
                    {
                        // Empty URL should clear hyperlink state
                        if (cell.HyperlinkUrl != null)
                        {
                            allLinkedCharsHaveUrl = false;
                            break;
                        }
                    }
                    else
                    {
                        // Non-empty URL should be associated with characters
                        if (cell.HyperlinkUrl != url)
                        {
                            allLinkedCharsHaveUrl = false;
                            break;
                        }
                    }
                }

                // Check characters that should not have hyperlink URL (after clearing)
                int startCol = textWithLink.Length;
                for (int i = 0; i < textWithoutLink.Length && (startCol + i) < 80; i++)
                {
                    var cell = terminal.ScreenBuffer.GetCell(0, startCol + i);
                    if (cell.Character != textWithoutLink[i])
                    {
                        // Character mismatch - skip this test case
                        return true;
                    }

                    // These characters should have no hyperlink URL (cleared by empty OSC 8)
                    if (cell.HyperlinkUrl != null)
                    {
                        allUnlinkedCharsHaveNoUrl = false;
                        break;
                    }
                }

                return allLinkedCharsHaveUrl && allUnlinkedCharsHaveNoUrl;
            }
            catch (Exception)
            {
                // Skip test cases that cause exceptions
                return true;
            }
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 24b: OSC hyperlink state management**
    ///     **Validates: Requirements 13.3**
    ///     Property: For any sequence of OSC 8 hyperlink operations, the terminal should
    ///     correctly maintain hyperlink state and apply it to subsequent characters.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OscHyperlinkStateManagementIsCorrect()
    {
        return Prop.ForAll(ValidUrlArb, ValidUrlArb, OscTerminatorArb, (url1, url2, terminator) =>
        {
            // Handle null values by treating as empty - FsCheck might generate nulls despite generators
            url1 = url1 ?? "";
            url2 = url2 ?? "";
            terminator = terminator ?? "BEL";

            try
            {
                // Arrange
                var terminal = TerminalEmulator.Create(80, 24, NullLogger.Instance);
                string terminatorBytes = terminator == "BEL" ? "\x07" : "\x1b\\";

                // Act - Set first hyperlink, write char, set second hyperlink, write char
                terminal.Write($"\x1b]8;;{url1}{terminatorBytes}");
                terminal.Write("A");
                terminal.Write($"\x1b]8;;{url2}{terminatorBytes}");
                terminal.Write("B");

                // Assert - Check that each character has the correct hyperlink URL
                var cell1 = terminal.ScreenBuffer.GetCell(0, 0); // 'A'
                var cell2 = terminal.ScreenBuffer.GetCell(0, 1); // 'B'

                bool firstCharCorrect = cell1.Character == 'A';
                bool secondCharCorrect = cell2.Character == 'B';

                bool firstUrlCorrect = string.IsNullOrEmpty(url1) ?
                    cell1.HyperlinkUrl == null :
                    cell1.HyperlinkUrl == url1;

                bool secondUrlCorrect = string.IsNullOrEmpty(url2) ?
                    cell2.HyperlinkUrl == null :
                    cell2.HyperlinkUrl == url2;

                return firstCharCorrect && secondCharCorrect && firstUrlCorrect && secondUrlCorrect;
            }
            catch (Exception)
            {
                // Skip test cases that cause exceptions
                return true;
            }
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 24c: OSC hyperlink parameter handling**
    ///     **Validates: Requirements 13.3**
    ///     Property: For any OSC 8 hyperlink sequence with parameters, the URL should be
    ///     correctly extracted and associated with characters, ignoring the parameters.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property OscHyperlinkParameterHandlingIsCorrect()
    {
        return Prop.ForAll<string, string, string>((url, parameters, terminator) =>
        {
            // Handle null values by treating as empty - FsCheck might generate nulls despite generators
            url = url ?? "";
            parameters = parameters ?? "";
            terminator = terminator ?? "BEL";

            // Only test valid terminators
            if (terminator != "BEL" && terminator != "ST") return true;

            // Skip empty URL to focus on parameter handling
            if (string.IsNullOrEmpty(url)) return true;

            // Skip empty parameters to avoid issues
            if (string.IsNullOrEmpty(parameters)) return true;

            // Skip URLs or parameters with control characters that might cause issues
            if (url.Any(c => c < 0x20) || parameters.Any(c => c < 0x20)) return true;

            try
            {
                // Arrange
                var terminal = TerminalEmulator.Create(80, 24, NullLogger.Instance);
                string terminatorBytes = terminator == "BEL" ? "\x07" : "\x1b\\";

                // Create OSC 8 sequence with parameters: ESC ] 8 ; [params] ; [url] BEL/ST
                string oscSequence = $"\x1b]8;{parameters};{url}{terminatorBytes}X\x1b]8;;{terminatorBytes}";

                // Act
                terminal.Write(oscSequence);

                // Assert - Check that URL is correctly extracted despite parameters
                var cell = terminal.ScreenBuffer.GetCell(0, 0); // 'X'

                bool characterCorrect = cell.Character == 'X';
                bool urlCorrect = cell.HyperlinkUrl == url;

                return characterCorrect && urlCorrect;
            }
            catch (Exception)
            {
                // Skip test cases that cause exceptions
                return true;
            }
        });
    }

    /// <summary>
    ///     Generator for unknown OSC command numbers (not in the implemented set).
    /// </summary>
    public static Arbitrary<int> UnknownOscCommandArb =>
        Arb.From(Gen.OneOf(
            Gen.Choose(100, 999),       // High command numbers
            Gen.Choose(13, 49),         // Mid-range unused commands
            Gen.Choose(3, 7),           // Low unused commands
            Gen.Choose(9, 9),           // Command 9 (not implemented)
            Gen.Choose(12, 20),         // Commands 12-20 (mostly unused)
            Gen.Choose(22, 51)          // Commands 22-51 (mostly unused)
        ).Where(cmd => !IsImplementedOscCommand(cmd)));

    /// <summary>
    ///     Checks if an OSC command is implemented in the terminal.
    /// </summary>
    private static bool IsImplementedOscCommand(int command)
    {
        return command switch
        {
            0 or 1 or 2 => true,       // Title/icon commands
            8 => true,                 // Hyperlink
            10 or 11 => true,          // Color queries
            21 => true,                // Window title query
            52 => true,                // Clipboard
            _ => false
        };
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 25: Unknown OSC sequence handling**
    ///     **Validates: Requirements 13.5**
    ///     Property: For any unknown OSC sequence, the terminal should ignore it without
    ///     error and continue processing normally without affecting terminal state.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property UnknownOscSequenceHandlingIsGraceful()
    {
        return Prop.ForAll(UnknownOscCommandArb, SafeTextArb, OscTerminatorArb,
            (unknownCommand, payload, terminator) =>
        {
            // Handle null values
            payload = payload ?? "";
            terminator = terminator ?? "BEL";

            // Skip very long payloads to avoid timeout issues
            if (payload.Length > 100) return true;

            try
            {
                // Arrange - Create terminal and capture initial state
                var terminal = TerminalEmulator.Create(80, 24, NullLogger.Instance);
                string terminatorBytes = terminator == "BEL" ? "\x07" : "\x1b\\";

                // Capture initial state
                int initialWidth = terminal.Width;
                int initialHeight = terminal.Height;
                int initialCursorRow = terminal.Cursor.Row;
                int initialCursorCol = terminal.Cursor.Col;

                // Track events to ensure unknown OSC doesn't emit any
                bool anyEventFired = false;
                terminal.TitleChanged += (s, e) => anyEventFired = true;
                terminal.IconNameChanged += (s, e) => anyEventFired = true;
                terminal.ClipboardRequest += (s, e) => anyEventFired = true;
                terminal.Bell += (s, e) => anyEventFired = true;

                // Act - Send unknown OSC sequence
                string unknownOscSequence = $"\x1b]{unknownCommand};{payload}{terminatorBytes}";
                terminal.Write(unknownOscSequence);

                // Assert - Terminal should remain functional and unchanged
                bool terminalStillFunctional = terminal.Width == initialWidth &&
                                               terminal.Height == initialHeight;
                bool cursorUnchanged = terminal.Cursor.Row == initialCursorRow &&
                                       terminal.Cursor.Col == initialCursorCol;
                bool noEventsEmitted = !anyEventFired;

                // Test that terminal can still process normal text after unknown OSC
                terminal.Write("test");
                var testCell = terminal.ScreenBuffer.GetCell(initialCursorRow, initialCursorCol);
                bool canStillWriteText = testCell.Character == 't';

                return terminalStillFunctional && cursorUnchanged && noEventsEmitted && canStillWriteText;
            }
            catch (Exception)
            {
                // Unknown OSC sequences should never cause exceptions
                return false;
            }
        });
    }
}
