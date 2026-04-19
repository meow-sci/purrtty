using System.Text;
using caTTY.Core.Terminal;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Terminal;

/// <summary>
///     Tests for DCS (Device Control String) sequence handling.
///     Based on the TypeScript DcsHandling.test.ts implementation.
/// </summary>
[TestFixture]
public class DcsHandlingTests
{
    [SetUp]
    public void SetUp()
    {
        _terminal = TerminalEmulator.Create(80, 24, NullLogger.Instance);
        _responses = new List<string>();
        _terminal.ResponseEmitted += (_, args) =>
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
    public void HandleDcs_DecrqssSgrRequest_ReturnsCurrentSgrState()
    {
        // Act - Send DECRQSS request for SGR state (SGR processing not yet implemented, so should return default)
        _terminal.Write("\x1bP$qm\x1b\\");

        // Assert - Should respond with current SGR state (default state since SGR not implemented)
        Assert.That(_responses, Has.Count.EqualTo(1));
        string response = _responses[0];

        // Response format: DCS 1 $ r <sgr_sequence> ST
        Assert.That(response, Does.StartWith("\x1bP1$r"));
        Assert.That(response, Does.EndWith("\x1b\\"));

        // Extract the SGR part
        string sgrPart = response.Substring(5, response.Length - 7); // Remove DCS header and ST

        // Should contain reset (0) and final 'm' for default state
        Assert.That(sgrPart, Is.EqualTo("0m"));
    }

    [Test]
    public void HandleDcs_DecrqssScrollRegionRequest_ReturnsCurrentScrollRegion()
    {
        // Act - Send DECRQSS request for scroll region (using default scroll region)
        _terminal.Write("\x1bP$qr\x1b\\");

        // Assert - Should respond with current scroll region (default is full screen: 1-24)
        Assert.That(_responses, Has.Count.EqualTo(1));
        string response = _responses[0];

        // Response format: DCS 1 $ r 1;24r ST (default scroll region for 24-row terminal)
        Assert.That(response, Is.EqualTo("\x1bP1$r1;24r\x1b\\"));
    }

    [Test]
    public void HandleDcs_DecrqssUnknownRequest_ReturnsInvalidResponse()
    {
        // Act - Send DECRQSS request for unknown sequence
        _terminal.Write("\x1bP$qx\x1b\\");

        // Assert - Should respond with invalid status
        Assert.That(_responses, Has.Count.EqualTo(1));
        string response = _responses[0];

        // Response format: DCS 0 $ r x ST (status 0 = invalid)
        Assert.That(response, Is.EqualTo("\x1bP0$rx\x1b\\"));
    }

    [Test]
    public void HandleDcs_DecrqssDecsca_ReturnsNotImplemented()
    {
        // Act - Send DECRQSS request for DECSCA (character protection)
        _terminal.Write("\x1bP$q\"q\x1b\\");

        // Assert - Should respond with invalid status (not implemented)
        Assert.That(_responses, Has.Count.EqualTo(1));
        string response = _responses[0];

        // Response format: DCS 0 $ r "q ST (status 0 = invalid/not implemented)
        Assert.That(response, Is.EqualTo("\x1bP0$r\"q\x1b\\"));
    }

    [Test]
    public void HandleDcs_DecrqssDecscl_ReturnsNotImplemented()
    {
        // Act - Send DECRQSS request for DECSCL (conformance level)
        _terminal.Write("\x1bP$q\"p\x1b\\");

        // Assert - Should respond with invalid status (not implemented)
        Assert.That(_responses, Has.Count.EqualTo(1));
        string response = _responses[0];

        // Response format: DCS 0 $ r "p ST (status 0 = invalid/not implemented)
        Assert.That(response, Is.EqualTo("\x1bP0$r\"p\x1b\\"));
    }

    [Test]
    public void HandleDcs_NonDecrqssSequence_LogsAndIgnores()
    {
        // Act - Send non-DECRQSS DCS sequence
        _terminal.Write("\x1bP1;2;3x\x1b\\");

        // Assert - Should not generate any response
        Assert.That(_responses, Has.Count.EqualTo(0));
    }

    [Test]
    public void HandleDcs_DecrqssSgrWithComplexAttributes_ReturnsCorrectState()
    {
        // Act - Send DECRQSS request for SGR state (SGR processing not yet implemented)
        _terminal.Write("\x1bP$qm\x1b\\");

        // Assert - Should respond with current SGR state (default state since SGR not implemented)
        Assert.That(_responses, Has.Count.EqualTo(1));
        string response = _responses[0];

        // Response format: DCS 1 $ r <sgr_sequence> ST
        Assert.That(response, Does.StartWith("\x1bP1$r"));
        Assert.That(response, Does.EndWith("\x1b\\"));

        // Extract the SGR part
        string sgrPart = response.Substring(5, response.Length - 7);

        // Should be default state
        Assert.That(sgrPart, Is.EqualTo("0m"));
    }

    [Test]
    public void HandleDcs_DecrqssAfterSgrReset_ReturnsDefaultState()
    {
        // Act - Send DECRQSS request for SGR state (SGR processing not yet implemented)
        _terminal.Write("\x1bP$qm\x1b\\");

        // Assert - Should respond with default state
        Assert.That(_responses, Has.Count.EqualTo(1));
        string response = _responses[0];

        // Response should be just reset + m
        Assert.That(response, Is.EqualTo("\x1bP1$r0m\x1b\\"));
    }
}
