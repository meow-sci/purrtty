using System.Text;
using caTTY.Core.Terminal;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.Core.Tests.Integration;

/// <summary>
///     Integration tests for device query sequences with real terminal interaction.
///     These tests verify that device queries work correctly in a realistic scenario.
/// </summary>
[TestFixture]
public class DeviceQueryIntegrationTests
{
    [SetUp]
    public void SetUp()
    {
        _terminal = TerminalEmulator.Create(80, 24, NullLogger.Instance);
        _responses = new List<string>();

        // Capture responses
        _terminal.ResponseEmitted += (sender, args) =>
        {
            string responseText = Encoding.UTF8.GetString(args.ResponseData.Span);
            _responses.Add(responseText);
        };
    }

    [TearDown]
    public void TearDown()
    {
        _terminal.Dispose();
    }

    private TerminalEmulator _terminal = null!;
    private List<string> _responses = null!;

    [Test]
    public void DeviceQueries_WithMixedContent_ShouldRespondCorrectly()
    {
        // Simulate a realistic terminal session with mixed content and device queries

        // Write some normal content
        _terminal.Write("Hello World\r\n");

        // Move cursor to a specific position
        _terminal.Write("\x1b[5;10H");

        // Send device queries mixed with normal content
        _terminal.Write("Test");
        _terminal.Write("\x1b[c"); // Primary DA query
        _terminal.Write(" more text ");
        _terminal.Write("\x1b[6n"); // Cursor position report
        _terminal.Write("\x1b[5n"); // Device status report
        _terminal.Write("\r\nEnd");

        // Verify responses were generated correctly
        Assert.That(_responses, Has.Count.EqualTo(3));
        Assert.That(_responses[0], Is.EqualTo("\x1b[?1;2c")); // Primary DA response
        Assert.That(_responses[1],
            Is.EqualTo("\x1b[5;25R")); // CPR response (cursor at row 5, col 25 after "Test" + " more text ")
        Assert.That(_responses[2], Is.EqualTo("\x1b[0n")); // DSR response
    }

    [Test]
    public void DeviceQueries_InSequence_ShouldMaintainOrder()
    {
        // Send multiple device queries in rapid succession
        string[] queries = new[]
        {
            "\x1b[c", // Primary DA
            "\x1b[>c", // Secondary DA  
            "\x1b[5n", // DSR
            "\x1b[6n", // CPR
            "\x1b[18t", // Terminal size
            "\x1b[?26n" // Character set query
        };

        foreach (string query in queries)
        {
            _terminal.Write(query);
        }

        // Verify all responses were generated in order
        Assert.That(_responses, Has.Count.EqualTo(6));
        Assert.That(_responses[0], Is.EqualTo("\x1b[?1;2c")); // Primary DA
        Assert.That(_responses[1], Is.EqualTo("\x1b[>0;0;0c")); // Secondary DA
        Assert.That(_responses[2], Is.EqualTo("\x1b[0n")); // DSR
        Assert.That(_responses[3], Is.EqualTo("\x1b[1;1R")); // CPR (at origin)
        Assert.That(_responses[4], Is.EqualTo("\x1b[8;24;80t")); // Terminal size
        Assert.That(_responses[5], Is.EqualTo("\x1b[?26;utf-8\x1b\\")); // Character set (UTF-8 by default)
    }

    [Test]
    public void DeviceQueries_WithCursorMovement_ShouldReportCorrectPosition()
    {
        // Test cursor position reporting after various movements

        // Move to different positions and query each time
        (string, string)[] positions = new[]
        {
            ("\x1b[1;1H", "\x1b[1;1R"), // Top-left
            ("\x1b[12;40H", "\x1b[12;40R"), // Middle
            ("\x1b[24;80H", "\x1b[24;80R"), // Bottom-right
            ("\x1b[10;25H", "\x1b[10;25R") // Arbitrary position
        };

        foreach ((string moveCommand, string expectedResponse) in positions)
        {
            _responses.Clear();

            _terminal.Write(moveCommand); // Move cursor
            _terminal.Write("\x1b[6n"); // Query position

            Assert.That(_responses, Has.Count.EqualTo(1));
            Assert.That(_responses[0], Is.EqualTo(expectedResponse));
        }
    }

    [Test]
    public void DeviceQueries_WithEscapeSequenceFragmentation_ShouldWork()
    {
        // Test device queries sent in fragments (simulating network/pipe fragmentation)

        // Send primary DA query in fragments
        _terminal.Write("\x1b");
        _terminal.Write("[");
        _terminal.Write("c");

        // Send cursor position query in fragments
        _terminal.Write("\x1b[");
        _terminal.Write("6");
        _terminal.Write("n");

        // Verify both queries were processed correctly
        Assert.That(_responses, Has.Count.EqualTo(2));
        Assert.That(_responses[0], Is.EqualTo("\x1b[?1;2c")); // Primary DA
        Assert.That(_responses[1], Is.EqualTo("\x1b[1;1R")); // CPR
    }

    [Test]
    public void DeviceQueries_WithInvalidSequences_ShouldIgnoreInvalid()
    {
        // Send a mix of valid and invalid device queries

        _terminal.Write("\x1b[c"); // Valid: Primary DA
        _terminal.Write("\x1b[999n"); // Invalid: Unknown DSR parameter
        _terminal.Write("\x1b[5n"); // Valid: DSR
        _terminal.Write("\x1b[>999c"); // Invalid: Unknown DA parameter
        _terminal.Write("\x1b[6n"); // Valid: CPR

        // Should only respond to valid queries
        Assert.That(_responses, Has.Count.EqualTo(3));
        Assert.That(_responses[0], Is.EqualTo("\x1b[?1;2c")); // Primary DA
        Assert.That(_responses[1], Is.EqualTo("\x1b[0n")); // DSR
        Assert.That(_responses[2], Is.EqualTo("\x1b[1;1R")); // CPR
    }
}
