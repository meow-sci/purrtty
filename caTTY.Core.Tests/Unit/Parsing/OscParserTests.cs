using System.Text;
using caTTY.Core.Parsing;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Parsing;

/// <summary>
///     Unit tests for the OSC sequence parser.
/// </summary>
[TestFixture]
[Category("Unit")]
public class OscParserTests
{
    private OscParser _parser = null!;
    private TestLogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = new TestLogger();
        _parser = new OscParser(_logger);
    }

    [Test]
    public void ProcessOscByte_BelTerminator_ReturnsCompleteMessage()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b, 0x5d, 0x30, 0x3b }; // ESC ] 0 ;
        byte b = 0x07; // BEL

        // Act
        bool isComplete = _parser.ProcessOscByte(b, escapeSequence, out OscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Type, Is.EqualTo("osc"));
        Assert.That(message.Raw, Is.EqualTo("\x1b]0;\x07"));
        Assert.That(message.Terminator, Is.EqualTo("BEL"));
        Assert.That(message.Implemented, Is.True); // Should be true for recognized xterm OSC
        Assert.That(message.XtermMessage, Is.Not.Null);
        Assert.That(message.XtermMessage!.Type, Is.EqualTo("osc.setTitleAndIcon"));
        Assert.That(message.XtermMessage.Command, Is.EqualTo(0));
        Assert.That(message.XtermMessage.Title, Is.EqualTo(string.Empty));
        Assert.That(escapeSequence, Has.Count.EqualTo(5));
        Assert.That(escapeSequence[4], Is.EqualTo(0x07));
    }

    [Test]
    public void ProcessOscByte_ValidByte_ContinuesParsing()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b, 0x5d }; // ESC ]
        byte b = 0x30; // 0

        // Act
        bool isComplete = _parser.ProcessOscByte(b, escapeSequence, out OscMessage? message);

        // Assert
        Assert.That(isComplete, Is.False);
        Assert.That(message, Is.Null);
        Assert.That(escapeSequence, Has.Count.EqualTo(3));
        Assert.That(escapeSequence[2], Is.EqualTo(0x30));
    }

    [Test]
    public void ProcessOscByte_EscByte_ContinuesParsing()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b, 0x5d, 0x30 }; // ESC ] 0
        byte b = 0x1b; // ESC

        // Act
        bool isComplete = _parser.ProcessOscByte(b, escapeSequence, out OscMessage? message);

        // Assert
        Assert.That(isComplete, Is.False);
        Assert.That(message, Is.Null);
        Assert.That(escapeSequence, Has.Count.EqualTo(4));
        Assert.That(escapeSequence[3], Is.EqualTo(0x1b));
    }

    [Test]
    public void ProcessOscByte_ByteOutOfRange_LogsWarningAndContinues()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b, 0x5d }; // ESC ]
        byte b = 0x10; // Control character (out of range)

        // Act
        bool isComplete = _parser.ProcessOscByte(b, escapeSequence, out OscMessage? message);

        // Assert
        Assert.That(isComplete, Is.False);
        Assert.That(message, Is.Null);
        Assert.That(_logger.LogMessages, Has.Some.Contains("OSC: control character byte 0x10"));
        Assert.That(escapeSequence, Has.Count.EqualTo(2)); // Byte not added
    }

    [Test]
    public void ProcessOscEscapeByte_StTerminator_ReturnsCompleteMessage()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b, 0x5d, 0x30, 0x3b, 0x1b }; // ESC ] 0 ; ESC
        byte b = 0x5c; // \

        // Act
        bool isComplete = _parser.ProcessOscEscapeByte(b, escapeSequence, out OscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Type, Is.EqualTo("osc"));
        Assert.That(message.Raw, Is.EqualTo("\x1b]0;\x1b\\"));
        Assert.That(message.Terminator, Is.EqualTo("ST"));
        Assert.That(message.Implemented, Is.True); // Should be true for recognized xterm OSC
        Assert.That(message.XtermMessage, Is.Not.Null);
        Assert.That(message.XtermMessage!.Type, Is.EqualTo("osc.setTitleAndIcon"));
        Assert.That(message.XtermMessage.Command, Is.EqualTo(0));
        Assert.That(message.XtermMessage.Title, Is.EqualTo(string.Empty));
        Assert.That(escapeSequence, Has.Count.EqualTo(6));
        Assert.That(escapeSequence[5], Is.EqualTo(0x5c));
    }

    [Test]
    public void ProcessOscEscapeByte_BelAfterEsc_ReturnsCompleteMessage()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b, 0x5d, 0x30, 0x3b, 0x1b }; // ESC ] 0 ; ESC
        byte b = 0x07; // BEL

        // Act
        bool isComplete = _parser.ProcessOscEscapeByte(b, escapeSequence, out OscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Type, Is.EqualTo("osc"));
        Assert.That(message.Raw, Is.EqualTo("\x1b]0;\x1b\x07"));
        Assert.That(message.Terminator, Is.EqualTo("BEL"));
        Assert.That(message.Implemented, Is.False);
    }

    [Test]
    public void ProcessOscEscapeByte_OtherByte_ContinuesParsing()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b, 0x5d, 0x30, 0x3b, 0x1b }; // ESC ] 0 ; ESC
        byte b = 0x41; // A

        // Act
        bool isComplete = _parser.ProcessOscEscapeByte(b, escapeSequence, out OscMessage? message);

        // Assert
        Assert.That(isComplete, Is.False);
        Assert.That(message, Is.Null);
        Assert.That(escapeSequence, Has.Count.EqualTo(6));
        Assert.That(escapeSequence[5], Is.EqualTo(0x41));
    }

    [Test]
    public void ProcessOscByte_ComplexSequence_HandlesCorrectly()
    {
        // Arrange - OSC 0;title BEL sequence
        var escapeSequence = new List<byte> { 0x1b, 0x5d }; // ESC ]
        byte[] titleBytes = { 0x30, 0x3b, 0x54, 0x65, 0x73, 0x74 }; // 0;Test

        // Act - Process title bytes
        bool isComplete = false;
        OscMessage? message = null;
        foreach (byte titleByte in titleBytes)
        {
            isComplete = _parser.ProcessOscByte(titleByte, escapeSequence, out message);
            if (isComplete) break;
        }

        // Process BEL terminator
        if (!isComplete)
        {
            isComplete = _parser.ProcessOscByte(0x07, escapeSequence, out message);
        }

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Raw, Is.EqualTo("\x1b]0;Test\x07"));
        Assert.That(message.Terminator, Is.EqualTo("BEL"));
        Assert.That(message.Implemented, Is.True); // Should be true for recognized xterm OSC
        Assert.That(message.XtermMessage, Is.Not.Null);
        Assert.That(message.XtermMessage!.Type, Is.EqualTo("osc.setTitleAndIcon"));
        Assert.That(message.XtermMessage.Title, Is.EqualTo("Test"));
        Assert.That(_logger.LogMessages, Has.Some.Contains("OSC (xterm, BEL): \x1b]0;Test\x07"));
    }

    [Test]
    public void ProcessOscByte_HyperlinkWithUrl_ParsesCorrectly()
    {
        // Arrange - OSC 8;;https://example.com BEL sequence
        var escapeSequence = new List<byte> { 0x1b, 0x5d }; // ESC ]
        byte[] hyperlinkBytes = { 0x38, 0x3b, 0x3b }; // 8;;
        byte[] urlBytes = "https://example.com"u8.ToArray();

        // Act - Process hyperlink command and URL bytes
        bool isComplete = false;
        OscMessage? message = null;
        
        // Process command bytes
        foreach (byte cmdByte in hyperlinkBytes)
        {
            isComplete = _parser.ProcessOscByte(cmdByte, escapeSequence, out message);
            if (isComplete) break;
        }
        
        // Process URL bytes
        if (!isComplete)
        {
            foreach (byte urlByte in urlBytes)
            {
                isComplete = _parser.ProcessOscByte(urlByte, escapeSequence, out message);
                if (isComplete) break;
            }
        }

        // Process BEL terminator
        if (!isComplete)
        {
            isComplete = _parser.ProcessOscByte(0x07, escapeSequence, out message);
        }

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Raw, Is.EqualTo("\x1b]8;;https://example.com\x07"));
        Assert.That(message.Terminator, Is.EqualTo("BEL"));
        Assert.That(message.Implemented, Is.True);
        Assert.That(message.XtermMessage, Is.Not.Null);
        Assert.That(message.XtermMessage!.Type, Is.EqualTo("osc.hyperlink"));
        Assert.That(message.XtermMessage.Command, Is.EqualTo(8));
        Assert.That(message.XtermMessage.HyperlinkUrl, Is.EqualTo("https://example.com"));
    }

    [Test]
    public void ProcessOscByte_HyperlinkWithParameters_ParsesUrlCorrectly()
    {
        // Arrange - OSC 8;id=link1;https://example.com BEL sequence
        var escapeSequence = new List<byte> { 0x1b, 0x5d }; // ESC ]
        byte[] hyperlinkBytes = { 0x38, 0x3b }; // 8;
        byte[] paramUrlBytes = "id=link1;https://example.com"u8.ToArray();

        // Act - Process hyperlink command and parameter/URL bytes
        bool isComplete = false;
        OscMessage? message = null;
        
        // Process command bytes
        foreach (byte cmdByte in hyperlinkBytes)
        {
            isComplete = _parser.ProcessOscByte(cmdByte, escapeSequence, out message);
            if (isComplete) break;
        }
        
        // Process parameter and URL bytes
        if (!isComplete)
        {
            foreach (byte paramByte in paramUrlBytes)
            {
                isComplete = _parser.ProcessOscByte(paramByte, escapeSequence, out message);
                if (isComplete) break;
            }
        }

        // Process BEL terminator
        if (!isComplete)
        {
            isComplete = _parser.ProcessOscByte(0x07, escapeSequence, out message);
        }

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Raw, Is.EqualTo("\x1b]8;id=link1;https://example.com\x07"));
        Assert.That(message.Terminator, Is.EqualTo("BEL"));
        Assert.That(message.Implemented, Is.True);
        Assert.That(message.XtermMessage, Is.Not.Null);
        Assert.That(message.XtermMessage!.Type, Is.EqualTo("osc.hyperlink"));
        Assert.That(message.XtermMessage.Command, Is.EqualTo(8));
        Assert.That(message.XtermMessage.HyperlinkUrl, Is.EqualTo("https://example.com"));
    }

    [Test]
    public void ProcessOscByte_HyperlinkClear_ParsesEmptyUrl()
    {
        // Arrange - OSC 8;; BEL sequence (clear hyperlink)
        var escapeSequence = new List<byte> { 0x1b, 0x5d }; // ESC ]
        byte[] hyperlinkBytes = { 0x38, 0x3b, 0x3b }; // 8;;

        // Act - Process hyperlink command bytes and BEL terminator
        bool isComplete = false;
        OscMessage? message = null;
        
        // Process command bytes
        foreach (byte cmdByte in hyperlinkBytes)
        {
            isComplete = _parser.ProcessOscByte(cmdByte, escapeSequence, out message);
            if (isComplete) break;
        }

        // Process BEL terminator
        if (!isComplete)
        {
            isComplete = _parser.ProcessOscByte(0x07, escapeSequence, out message);
        }

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Raw, Is.EqualTo("\x1b]8;;\x07"));
        Assert.That(message.Terminator, Is.EqualTo("BEL"));
        Assert.That(message.Implemented, Is.True);
        Assert.That(message.XtermMessage, Is.Not.Null);
        Assert.That(message.XtermMessage!.Type, Is.EqualTo("osc.hyperlink"));
        Assert.That(message.XtermMessage.Command, Is.EqualTo(8));
        Assert.That(message.XtermMessage.HyperlinkUrl, Is.EqualTo(string.Empty));
    }

    [Test]
    public void ProcessOscByte_UnrecognizedCommand_ReturnsUnimplementedMessage()
    {
        // Arrange - OSC 999;unknown BEL sequence (unrecognized command)
        var escapeSequence = new List<byte> { 0x1b, 0x5d, 0x39, 0x39, 0x39, 0x3b, 0x75, 0x6e, 0x6b, 0x6e, 0x6f, 0x77, 0x6e }; // ESC ] 999;unknown
        byte b = 0x07; // BEL

        // Act
        bool isComplete = _parser.ProcessOscByte(b, escapeSequence, out OscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Type, Is.EqualTo("osc"));
        Assert.That(message.Raw, Is.EqualTo("\x1b]999;unknown\x07"));
        Assert.That(message.Terminator, Is.EqualTo("BEL"));
        Assert.That(message.Implemented, Is.False); // Should be false for unrecognized commands
        Assert.That(message.XtermMessage, Is.Null);
        Assert.That(_logger.LogMessages, Has.Some.Contains("OSC (opaque, BEL): \x1b]999;unknown\x07"));
    }

    [Test]
    public void ProcessOscByte_WindowTitleCommand_ParsesCorrectly()
    {
        // Arrange - OSC 2;Window Title BEL sequence
        var escapeSequence = new List<byte> { 0x1b, 0x5d }; // ESC ]
        byte[] titleBytes = { 0x32, 0x3b, 0x57, 0x69, 0x6e, 0x64, 0x6f, 0x77, 0x20, 0x54, 0x69, 0x74, 0x6c, 0x65 }; // 2;Window Title

        // Act - Process title bytes
        bool isComplete = false;
        OscMessage? message = null;
        foreach (byte titleByte in titleBytes)
        {
            isComplete = _parser.ProcessOscByte(titleByte, escapeSequence, out message);
            if (isComplete) break;
        }

        // Process BEL terminator
        if (!isComplete)
        {
            isComplete = _parser.ProcessOscByte(0x07, escapeSequence, out message);
        }

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Implemented, Is.True);
        Assert.That(message.XtermMessage, Is.Not.Null);
        Assert.That(message.XtermMessage!.Type, Is.EqualTo("osc.setWindowTitle"));
        Assert.That(message.XtermMessage.Command, Is.EqualTo(2));
        Assert.That(message.XtermMessage.Title, Is.EqualTo("Window Title"));
    }

    [Test]
    public void ProcessOscByte_QueryCommand_ParsesCorrectly()
    {
        // Arrange - OSC 21 BEL sequence (query window title)
        var escapeSequence = new List<byte> { 0x1b, 0x5d, 0x32, 0x31 }; // ESC ] 21
        byte b = 0x07; // BEL

        // Act
        bool isComplete = _parser.ProcessOscByte(b, escapeSequence, out OscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Implemented, Is.True);
        Assert.That(message.XtermMessage, Is.Not.Null);
        Assert.That(message.XtermMessage!.Type, Is.EqualTo("osc.queryWindowTitle"));
        Assert.That(message.XtermMessage.Command, Is.EqualTo(21));
        Assert.That(message.XtermMessage.Payload, Is.EqualTo(string.Empty));
    }

    [Test]
    public void ProcessOscByte_ColorQuery_ParsesCorrectly()
    {
        // Arrange - OSC 10;? BEL sequence (query foreground color)
        var escapeSequence = new List<byte> { 0x1b, 0x5d, 0x31, 0x30, 0x3b, 0x3f }; // ESC ] 10;?
        byte b = 0x07; // BEL

        // Act
        bool isComplete = _parser.ProcessOscByte(b, escapeSequence, out OscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Implemented, Is.True);
        Assert.That(message.XtermMessage, Is.Not.Null);
        Assert.That(message.XtermMessage!.Type, Is.EqualTo("osc.queryForegroundColor"));
        Assert.That(message.XtermMessage.Command, Is.EqualTo(10));
        Assert.That(message.XtermMessage.Payload, Is.EqualTo("?"));
    }

    [Test]
    public void ProcessOscByte_BackgroundColorQuery_ParsesCorrectly()
    {
        // Arrange - OSC 11;? ST sequence (query background color)
        var escapeSequence = new List<byte> { 0x1b, 0x5d, 0x31, 0x31, 0x3b, 0x3f, 0x1b }; // ESC ] 11;? ESC
        byte b = 0x5c; // \

        // Act
        bool isComplete = _parser.ProcessOscEscapeByte(b, escapeSequence, out OscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Implemented, Is.True);
        Assert.That(message.XtermMessage, Is.Not.Null);
        Assert.That(message.XtermMessage!.Type, Is.EqualTo("osc.queryBackgroundColor"));
        Assert.That(message.XtermMessage.Command, Is.EqualTo(11));
        Assert.That(message.XtermMessage.Payload, Is.EqualTo("?"));
    }

    [Test]
    public void ProcessOscByte_ColorQueryWithParameters_ParsesCorrectly()
    {
        // Arrange - OSC 10;?;extra BEL sequence (query with extra parameters)
        var escapeSequence = new List<byte> { 0x1b, 0x5d, 0x31, 0x30, 0x3b, 0x3f, 0x3b, 0x65, 0x78, 0x74, 0x72, 0x61 }; // ESC ] 10;?;extra
        byte b = 0x07; // BEL

        // Act
        bool isComplete = _parser.ProcessOscByte(b, escapeSequence, out OscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Implemented, Is.True);
        Assert.That(message.XtermMessage, Is.Not.Null);
        Assert.That(message.XtermMessage!.Type, Is.EqualTo("osc.queryForegroundColor"));
        Assert.That(message.XtermMessage.Command, Is.EqualTo(10));
        Assert.That(message.XtermMessage.Payload, Is.EqualTo("?;extra"));
    }

    [Test]
    public void ProcessOscByte_PayloadTooLong_ReturnsNull()
    {
        // Arrange - Create a very long payload that exceeds the limit
        var escapeSequence = new List<byte> { 0x1b, 0x5d, 0x30, 0x3b }; // ESC ] 0 ;
        string longTitle = new('A', 2000); // Much longer than MaxOscPayloadLength (1024)
        byte[] titleBytes = Encoding.UTF8.GetBytes(longTitle);

        // Act - Process title bytes
        bool isComplete = false;
        OscMessage? message = null;
        foreach (byte titleByte in titleBytes)
        {
            isComplete = _parser.ProcessOscByte(titleByte, escapeSequence, out message);
            if (isComplete) break;
        }

        // Process BEL terminator
        if (!isComplete)
        {
            isComplete = _parser.ProcessOscByte(0x07, escapeSequence, out message);
        }

        // Assert - Should return unimplemented message due to payload length limit
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Implemented, Is.False);
        Assert.That(message.XtermMessage, Is.Null);
        Assert.That(_logger.LogMessages, Has.Some.Contains("OSC payload exceeds maximum length"));
    }

    [Test]
    public void ProcessOscByte_PrivateCommandNumber_ReturnsImplementedMessage()
    {
        // Arrange - OSC with private-use command number (1000+)
        // Commands 1000+ are used for private/application-specific purposes (e.g., RPC)
        var escapeSequence = new List<byte> { 0x1b, 0x5d, 0x31, 0x30, 0x30, 0x30, 0x3b, 0x74, 0x65, 0x73, 0x74 }; // ESC ] 1000;test
        byte b = 0x07; // BEL

        // Act
        bool isComplete = _parser.ProcessOscByte(b, escapeSequence, out OscMessage? message);

        // Assert - private commands are implemented and handled by RPC layer
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.XtermMessage, Is.Not.Null);
        Assert.That(message.XtermMessage!.Type, Is.EqualTo("osc.private"));
        Assert.That(message.XtermMessage.Implemented, Is.True);
    }

    [Test]
    public void ProcessOscByte_InvalidCommandNumber_ReturnsUnimplementedMessage()
    {
        // Arrange - OSC with invalid command number (10000, > 9999)
        var escapeSequence = new List<byte> { 0x1b, 0x5d, 0x31, 0x30, 0x30, 0x30, 0x30, 0x3b, 0x74, 0x65, 0x73, 0x74 }; // ESC ] 10000;test
        byte b = 0x07; // BEL

        // Act
        bool isComplete = _parser.ProcessOscByte(b, escapeSequence, out OscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Implemented, Is.False);
        Assert.That(message.XtermMessage, Is.Null);
    }

    [Test]
    public void ProcessOscByte_Utf8Bytes_AcceptsAndProcesses()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b, 0x5d, 0x32, 0x3b }; // ESC ] 2 ;
        
        // UTF-8 bytes for "éñ中文" 
        byte[] utf8Bytes = Encoding.UTF8.GetBytes("éñ中文");
        
        // Act & Assert - Process each UTF-8 byte
        foreach (byte b in utf8Bytes)
        {
            bool isComplete = _parser.ProcessOscByte(b, escapeSequence, out OscMessage? message);
            Assert.That(isComplete, Is.False); // Should not complete until BEL
            Assert.That(message, Is.Null);
        }
        
        // Add BEL terminator
        bool finalComplete = _parser.ProcessOscByte(0x07, escapeSequence, out OscMessage? finalMessage);
        
        // Assert
        Assert.That(finalComplete, Is.True);
        Assert.That(finalMessage, Is.Not.Null);
        Assert.That(finalMessage!.Implemented, Is.True);
        Assert.That(finalMessage.XtermMessage, Is.Not.Null);
        Assert.That(finalMessage.XtermMessage!.Title, Is.EqualTo("éñ中文"));
        
        // Verify no warnings were logged for UTF-8 bytes
        Assert.That(_logger.LogMessages, Has.None.Contains("control character"));
        Assert.That(_logger.LogMessages, Has.None.Contains("out of range"));
    }
}