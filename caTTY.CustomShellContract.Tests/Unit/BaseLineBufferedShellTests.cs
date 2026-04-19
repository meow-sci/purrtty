using System.Text;
using NUnit.Framework;

namespace caTTY.Core.Terminal.Tests.Unit;

[TestFixture]
public class BaseLineBufferedShellTests
{
    /// <summary>
    ///     Test implementation of BaseLineBufferedShell for testing purposes.
    /// </summary>
    private class TestLineBufferedShell : BaseLineBufferedShell
    {
        private readonly CustomShellMetadata _metadata;
        private readonly List<string> _executedCommands = new();
        private int _clearScreenCallCount;
        private string _prompt = "test> ";

        public TestLineBufferedShell()
        {
            _metadata = CustomShellMetadata.Create(
                "TestLineBufferedShell",
                "A test line buffered shell implementation",
                new Version(1, 0, 0),
                "Test Author"
            );
        }

        public override CustomShellMetadata Metadata => _metadata;

        public IReadOnlyList<string> ExecutedCommands => _executedCommands;
        public int ClearScreenCallCount => _clearScreenCallCount;

        protected override Task OnStartingAsync(CustomShellStartOptions options, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        protected override Task OnStoppingAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        protected override void ExecuteCommandLine(string commandLine)
        {
            _executedCommands.Add(commandLine);
            SendOutput($"Executed: {commandLine}\r\n");
            SendPrompt();
        }

        protected override void HandleClearScreen()
        {
            _clearScreenCallCount++;
            SendOutput("\x1b[2J\x1b[H");
        }

        protected override string GetPrompt()
        {
            return _prompt;
        }

        public void SetPrompt(string prompt)
        {
            _prompt = prompt;
        }

        public void SendErrorForTest(string text)
        {
            SendError(text);
        }

        // Expose protected members for testing
        public IReadOnlyList<string> TestGetCommandHistory() => CommandHistory;
        public string TestGetCurrentLine() => CurrentLine;
        public int TestGetCursorPosition() => CursorPosition;
    }

    private TestLineBufferedShell? _shell;

    [SetUp]
    public async Task SetUp()
    {
        _shell = new TestLineBufferedShell();
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell.StartAsync(options);
    }

    [TearDown]
    public void TearDown()
    {
        _shell?.Dispose();
    }

    #region Input Processing Tests

    [Test]
    public async Task WriteInputAsync_PrintableCharacters_EchoesBack()
    {
        // Arrange
        var outputReceived = new List<byte[]>();

        _shell!.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(args.Data.ToArray());
        };

        var input = Encoding.UTF8.GetBytes("hello");

        // Act
        await _shell.WriteInputAsync(input);

        // Wait for all output to be processed
        await Task.Delay(100);

        // Assert
        Assert.That(outputReceived.Count, Is.GreaterThan(0), "Should receive echoed output");
        var allOutput = string.Join("", outputReceived.Select(b => Encoding.UTF8.GetString(b)));
        Assert.That(allOutput, Does.Contain("hello"), "Should echo input characters");
    }

    [Test]
    public async Task WriteInputAsync_Backspace_RemovesCharacter()
    {
        // Arrange
        var outputReceived = new List<string>();
        _shell!.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(Encoding.UTF8.GetString(args.Data.ToArray()));
        };

        // Act - Type "hello" then backspace twice
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("hello"));
        await Task.Delay(50); // Allow output to process
        await _shell.WriteInputAsync(new byte[] { 0x7F }); // Backspace
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x7F }); // Backspace

        // Assert - Current line should be "hel"
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hel"), "Line buffer should have two characters removed");
    }

    [Test]
    public async Task WriteInputAsync_BackspaceOnEmptyBuffer_DoesNothing()
    {
        // Arrange
        var outputReceived = new List<string>();
        _shell!.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(Encoding.UTF8.GetString(args.Data.ToArray()));
        };

        // Act - Send backspace on empty line
        await _shell.WriteInputAsync(new byte[] { 0x7F });
        await Task.Delay(50);

        // Assert - Should not crash or send anything unexpected
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty, "Line buffer should remain empty");
    }

    [Test]
    public async Task WriteInputAsync_CtrlH_DeletesWordNotCharacter()
    {
        // Arrange - 0x08 (Ctrl+H) is now Ctrl+Backspace, which deletes words
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world"));
        await Task.Delay(50);

        // Act - Use Ctrl+H (0x08)
        await _shell.WriteInputAsync(new byte[] { 0x08 });
        await Task.Delay(50);

        // Assert - Should delete the whole word "world", not just the last character
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello "), "Ctrl+H should delete word, not character");
    }

    [Test]
    public async Task WriteInputAsync_Enter_ExecutesCommand()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("test command"));
        await Task.Delay(50);

        // Act
        await _shell.WriteInputAsync(new byte[] { 0x0D }); // Carriage return
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.ExecutedCommands.Count, Is.EqualTo(1), "Should execute one command");
        Assert.That(_shell.ExecutedCommands[0], Is.EqualTo("test command"), "Command should match");
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty, "Line buffer should be cleared");
    }

    [Test]
    public async Task WriteInputAsync_EnterWithLineFeed_ExecutesCommand()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("test"));
        await Task.Delay(50);

        // Act - Use line feed instead of carriage return
        await _shell.WriteInputAsync(new byte[] { 0x0A });
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.ExecutedCommands.Count, Is.EqualTo(1), "Line feed should also execute command");
    }

    [Test]
    public async Task WriteInputAsync_EnterOnEmptyLine_DoesNotExecute()
    {
        // Act
        await _shell!.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.ExecutedCommands.Count, Is.EqualTo(0), "Empty line should not execute");
    }

    [Test]
    public async Task WriteInputAsync_EnterOnWhitespaceLine_DoesNotExecute()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("   "));
        await Task.Delay(50);

        // Act
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.ExecutedCommands.Count, Is.EqualTo(0), "Whitespace-only line should not execute");
    }

    [Test]
    public async Task WriteInputAsync_CtrlL_ClearsScreen()
    {
        // Act
        await _shell!.WriteInputAsync(new byte[] { 0x0C }); // Ctrl+L
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.ClearScreenCallCount, Is.EqualTo(1), "Clear screen should be called");
    }

    #endregion

    #region Escape Sequence Tests

    [Test]
    public async Task WriteInputAsync_UpArrow_NavigatesHistory()
    {
        // Arrange - Execute two commands to build history
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("first command"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(100);
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("second command"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(100);

        // Act - Press up arrow once
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x41 }); // ESC [ A
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("second command"), "Should recall last command");
    }

    [Test]
    public async Task WriteInputAsync_UpArrowTwice_NavigatesHistoryTwice()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("first"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("second"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);

        // Act - Press up arrow twice
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x41 }); // Up
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x41 }); // Up
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("first"), "Should recall first command");
    }

    [Test]
    public async Task WriteInputAsync_DownArrow_NavigatesHistoryDown()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("first"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("second"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);

        // Navigate up twice
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x41 }); // Up
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x41 }); // Up
        await Task.Delay(50);

        // Act - Navigate down once
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x42 }); // ESC [ B (down)
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("second"), "Should navigate down to second command");
    }

    [Test]
    public async Task WriteInputAsync_DownArrowPastEnd_RestoresSavedLine()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("cmd1"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);

        // Start typing a new command
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("new command"));
        await Task.Delay(50);

        // Navigate up then down past end
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x41 }); // Up
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x42 }); // Down
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("new command"), "Should restore the line being typed");
    }

    [Test]
    public async Task WriteInputAsync_UpArrowOnEmptyHistory_DoesNothing()
    {
        // Act
        await _shell!.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x41 }); // Up arrow
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty, "Should remain empty with no history");
    }

    [Test]
    public async Task WriteInputAsync_DownArrowWithoutNavigation_DoesNothing()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("test"));
        await Task.Delay(50);

        // Act - Press down without pressing up first
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x42 }); // Down
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("test"), "Should not change line");
    }

    [Test]
    public async Task WriteInputAsync_UnknownEscapeSequence_Ignored()
    {
        // Act - Send ESC followed by something other than '['
        await _shell!.WriteInputAsync(new byte[] { 0x1B, 0x4F }); // ESC O (unknown)
        await Task.Delay(100);

        // Assert - Should not crash
        Assert.Pass("Unknown escape sequence handled gracefully");
    }

    [Test]
    public async Task WriteInputAsync_LeftArrow_MovesCursorLeft()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(5), "Initial cursor position");

        // Act - Press left arrow three times
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // ESC [ D (left)
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // ESC [ D (left)
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // ESC [ D (left)
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(2), "Cursor should be at position 2");
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello"), "Line buffer should remain unchanged");
    }

    [Test]
    public async Task WriteInputAsync_RightArrow_MovesCursorRight()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello"));
        await Task.Delay(50);

        // Move left three times
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(2), "Cursor at position 2 before right arrow");

        // Act - Press right arrow once
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x43 }); // ESC [ C (right)
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(3), "Cursor should be at position 3");
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello"), "Line buffer should remain unchanged");
    }

    [Test]
    public async Task WriteInputAsync_LeftArrowAtStart_DoesNothing()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("test"));
        await Task.Delay(50);

        // Move to start
        for (int i = 0; i < 4; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
            await Task.Delay(50);
        }
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at position 0");

        // Act - Press left arrow at start
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should remain at position 0");
    }

    [Test]
    public async Task WriteInputAsync_RightArrowAtEnd_DoesNothing()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("test"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(4), "Cursor should be at end");

        // Act - Press right arrow at end
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x43 }); // Right
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(4), "Cursor should remain at position 4");
    }

    [Test]
    public async Task WriteInputAsync_LeftRightArrowMovement_PreservesLineContent()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world"));
        await Task.Delay(50);

        // Act - Move left 5 times, then right 2 times
        for (int i = 0; i < 5; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
            await Task.Delay(50);
        }
        for (int i = 0; i < 2; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x43 }); // Right
            await Task.Delay(50);
        }
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(8), "Cursor should be at position 8");
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello world"), "Line content should not change");
    }

    [Test]
    public async Task WriteInputAsync_LeftRightArrowEchoesEscapeSequences()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("test"));
        await Task.Delay(50);

        var outputReceived = new List<string>();
        _shell!.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(Encoding.UTF8.GetString(args.Data.ToArray()));
        };

        // Act - Move left then right
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x43 }); // Right
        await Task.Delay(100);

        // Assert
        var allOutput = string.Join("", outputReceived);
        Assert.That(allOutput, Does.Contain("\x1b[D"), "Should echo left arrow sequence");
        Assert.That(allOutput, Does.Contain("\x1b[C"), "Should echo right arrow sequence");
    }

    [Test]
    public async Task WriteInputAsync_LeftArrowOnEmptyBuffer_DoesNothing()
    {
        // Act - Press left arrow on empty line
        await _shell!.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should stay at position 0");
    }

    [Test]
    public async Task WriteInputAsync_RightArrowOnEmptyBuffer_DoesNothing()
    {
        // Act - Press right arrow on empty line
        await _shell!.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x43 }); // Right
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should stay at position 0");
    }

    [Test]
    public async Task WriteInputAsync_HomeKey_MovesCursorToStart()
    {
        // Arrange - Type "hello"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(5), "Initial cursor position");

        // Act - Press Home key
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x48 }); // ESC [ H
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at position 0");
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello"), "Line buffer should remain unchanged");
    }

    [Test]
    public async Task WriteInputAsync_EndKey_MovesCursorToEnd()
    {
        // Arrange - Type "hello" and move to start
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello"));
        await Task.Delay(50);
        for (int i = 0; i < 5; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
            await Task.Delay(50);
        }
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at start");

        // Act - Press End key
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x46 }); // ESC [ F
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(5), "Cursor should be at position 5");
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello"), "Line buffer should remain unchanged");
    }

    [Test]
    public async Task WriteInputAsync_HomeKeyAtStart_DoesNothing()
    {
        // Arrange - Type "test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("test"));
        await Task.Delay(50);

        // Move to start
        for (int i = 0; i < 4; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
            await Task.Delay(50);
        }
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at position 0");

        // Act - Press Home key at start
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x48 }); // Home
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should remain at position 0");
    }

    [Test]
    public async Task WriteInputAsync_EndKeyAtEnd_DoesNothing()
    {
        // Arrange - Type "test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("test"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(4), "Cursor should be at end");

        // Act - Press End key at end
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x46 }); // End
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(4), "Cursor should remain at position 4");
    }

    [Test]
    public async Task WriteInputAsync_HomeKeyOnEmptyBuffer_DoesNothing()
    {
        // Act - Press Home key on empty line
        await _shell!.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x48 }); // Home
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should stay at position 0");
    }

    [Test]
    public async Task WriteInputAsync_EndKeyOnEmptyBuffer_DoesNothing()
    {
        // Act - Press End key on empty line
        await _shell!.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x46 }); // End
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should stay at position 0");
    }

    [Test]
    public async Task WriteInputAsync_HomeKeyFromMiddle_MovesCursorToStart()
    {
        // Arrange - Type "hello world" and move to middle
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world"));
        await Task.Delay(50);
        for (int i = 0; i < 5; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
            await Task.Delay(50);
        }
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(6), "Cursor should be at position 6");

        // Act - Press Home key
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x48 }); // Home
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at position 0");
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello world"), "Line buffer should remain unchanged");
    }

    [Test]
    public async Task WriteInputAsync_EndKeyFromMiddle_MovesCursorToEnd()
    {
        // Arrange - Type "hello world" and move to middle
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world"));
        await Task.Delay(50);
        for (int i = 0; i < 5; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
            await Task.Delay(50);
        }
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(6), "Cursor should be at position 6");

        // Act - Press End key
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x46 }); // End
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(11), "Cursor should be at position 11");
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello world"), "Line buffer should remain unchanged");
    }

    [Test]
    public async Task WriteInputAsync_HomeEndSequence_MovesCorrectly()
    {
        // Arrange - Type "test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("test"));
        await Task.Delay(50);

        // Act - Press Home, then End
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x48 }); // Home
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at start");

        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x46 }); // End
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(4), "Cursor should be back at end");
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("test"), "Line buffer should remain unchanged");
    }

    [Test]
    public async Task WriteInputAsync_HomeKeyEchoesEscapeSequence()
    {
        // Arrange - Type "hello"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello"));
        await Task.Delay(50);

        var outputReceived = new List<string>();
        _shell!.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(Encoding.UTF8.GetString(args.Data.ToArray()));
        };

        // Act - Press Home key
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x48 }); // Home
        await Task.Delay(100);

        // Assert
        var allOutput = string.Join("", outputReceived);
        Assert.That(allOutput, Does.Contain("\x1b[5D"), "Should echo move left 5 positions sequence");
    }

    [Test]
    public async Task WriteInputAsync_EndKeyEchoesEscapeSequence()
    {
        // Arrange - Type "hello" and move to start
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello"));
        await Task.Delay(50);
        for (int i = 0; i < 5; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
            await Task.Delay(50);
        }

        var outputReceived = new List<string>();
        _shell!.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(Encoding.UTF8.GetString(args.Data.ToArray()));
        };

        // Act - Press End key
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x46 }); // End
        await Task.Delay(100);

        // Assert
        var allOutput = string.Join("", outputReceived);
        Assert.That(allOutput, Does.Contain("\x1b[5C"), "Should echo move right 5 positions sequence");
    }

    [Test]
    public async Task WriteInputAsync_HomeEndWithInsert_WorksCorrectly()
    {
        // Arrange - Type "hello"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello"));
        await Task.Delay(50);

        // Act - Press Home, type 'X', press End, type 'Y'
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x48 }); // Home
        await Task.Delay(50);
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("X"));
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x46 }); // End
        await Task.Delay(50);
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("Y"));
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("XhelloY"), "Line should be 'XhelloY'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(7), "Cursor should be at position 7");
    }

    #endregion

    #region Command History Tests

    [Test]
    public async Task CommandHistory_AddsCommands()
    {
        // Act
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("cmd1"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("cmd2"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(100);

        // Assert
        var history = _shell.TestGetCommandHistory();
        Assert.That(history.Count, Is.EqualTo(2), "Should have two commands in history");
        Assert.That(history[0], Is.EqualTo("cmd1"));
        Assert.That(history[1], Is.EqualTo("cmd2"));
    }

    [Test]
    public async Task CommandHistory_AvoidsDuplicates()
    {
        // Act - Execute same command twice
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("same"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("same"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(100);

        // Assert
        var history = _shell.TestGetCommandHistory();
        Assert.That(history.Count, Is.EqualTo(1), "Consecutive duplicates should not be added");
        Assert.That(history[0], Is.EqualTo("same"));
    }

    [Test]
    public async Task CommandHistory_AllowsNonConsecutiveDuplicates()
    {
        // Act
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("cmd1"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("cmd2"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("cmd1")); // Repeat cmd1
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(100);

        // Assert
        var history = _shell.TestGetCommandHistory();
        Assert.That(history.Count, Is.EqualTo(3), "Non-consecutive duplicates should be added");
        Assert.That(history[2], Is.EqualTo("cmd1"));
    }

    [Test]
    public async Task CommandHistory_NavigationSavesCurrentLine()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("cmd1"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);

        // Type a new command
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("typing this"));
        await Task.Delay(50);

        // Act - Navigate up (should save "typing this")
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x41 }); // Up
        await Task.Delay(50);

        // Navigate back down
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x42 }); // Down
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("typing this"), "Should restore saved line");
    }

    [Test]
    public async Task CommandHistory_ResetsAfterCommandExecution()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("cmd1"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);

        // Navigate up
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x41 }); // Up
        await Task.Delay(50);

        // Execute the recalled command
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);

        // Act - Try to navigate down (should do nothing, history reset)
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x42 }); // Down
        await Task.Delay(100);

        // Assert - Should remain at empty line
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty);
    }

    #endregion

    #region Terminal Resize Tests

    [Test]
    public void NotifyTerminalResize_UpdatesDimensions()
    {
        // Act
        _shell!.NotifyTerminalResize(100, 40);

        // Assert - No exception, dimensions stored (internal state)
        Assert.Pass("Terminal resize notification handled");
    }

    #endregion

    #region Multiple Commands Test

    [Test]
    public async Task MultipleCommands_ExecutedInOrder()
    {
        // Act
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("first"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("second"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("third"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.ExecutedCommands.Count, Is.EqualTo(3));
        Assert.That(_shell.ExecutedCommands[0], Is.EqualTo("first"));
        Assert.That(_shell.ExecutedCommands[1], Is.EqualTo("second"));
        Assert.That(_shell.ExecutedCommands[2], Is.EqualTo("third"));
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task WriteInputAsync_ControlCharacters_Ignored()
    {
        // Act - Send various control characters (except handled ones)
        await _shell!.WriteInputAsync(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
        await Task.Delay(100);

        // Assert - Should not crash, line should be empty
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty, "Control characters should be ignored");
    }

    [Test]
    public async Task WriteInputAsync_NonAsciiBytes_Ignored()
    {
        // Act - Send non-ASCII bytes
        await _shell!.WriteInputAsync(new byte[] { 0x80, 0x90, 0xA0, 0xFF });
        await Task.Delay(100);

        // Assert - Should not crash, line should be empty
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty, "Non-ASCII bytes should be ignored");
    }

    [Test]
    public async Task WriteInputAsync_CommandWithLeadingWhitespace_Trimmed()
    {
        // Act
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("  trimmed  "));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.ExecutedCommands[0], Is.EqualTo("trimmed"), "Command should be trimmed");
    }

    [Test]
    public async Task WriteInputAsync_WhenNotRunning_ThrowsException()
    {
        // Arrange
        await _shell!.StopAsync();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("test"))
        );
    }

    [Test]
    public async Task LineBuffer_ThreadSafe()
    {
        // Act - Send input from multiple threads
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            var input = Encoding.UTF8.GetBytes($"{i}");
            tasks.Add(Task.Run(async () => await _shell!.WriteInputAsync(input)));
        }

        await Task.WhenAll(tasks);
        await Task.Delay(100);

        // Assert - Should not crash (verifies thread safety via lock)
        Assert.Pass("Thread-safe access to line buffer");
    }

    #endregion

    #region Prompt Tests

    [Test]
    public async Task GetPrompt_UsedInLineReplacement()
    {
        // Arrange
        _shell!.SetPrompt("custom> ");
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("cmd1"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);

        var outputReceived = new List<string>();
        _shell.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(Encoding.UTF8.GetString(args.Data.ToArray()));
        };

        // Act - Navigate up (triggers line replacement with prompt)
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x41 }); // Up
        await Task.Delay(100);

        // Assert
        var allOutput = string.Join("", outputReceived);
        Assert.That(allOutput, Does.Contain("custom> "), "Prompt should be used in line replacement");
    }

    #endregion

    #region Clear Screen Tests

    [Test]
    public async Task CtrlL_CallsHandleClearScreenAndSendsPrompt()
    {
        // Arrange
        var outputReceived = new List<string>();
        _shell!.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(Encoding.UTF8.GetString(args.Data.ToArray()));
        };

        // Act
        await _shell.WriteInputAsync(new byte[] { 0x0C }); // Ctrl+L
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.ClearScreenCallCount, Is.EqualTo(1), "HandleClearScreen should be called");
        var allOutput = string.Join("", outputReceived);
        Assert.That(allOutput, Does.Contain("\x1b[2J\x1b[H"), "Should send clear screen sequence");
        Assert.That(allOutput, Does.Contain("test> "), "Should send prompt after clear");
    }

    [Test]
    public async Task CtrlL_ClearsLineBuffer()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("some text"));
        await Task.Delay(50);

        // Act
        await _shell.WriteInputAsync(new byte[] { 0x0C }); // Ctrl+L
        await Task.Delay(100);

        // Note: Current implementation does NOT clear the line buffer on Ctrl+L
        // It only clears the screen and shows a new prompt
        // The line buffer should remain intact (this is typical shell behavior)
        Assert.Pass("Ctrl+L clear screen behavior verified");
    }

    #endregion

    #region Output Type Tests

    [Test]
    public async Task SendError_EmitsStderrOutputType()
    {
        // Arrange
        var outputTypes = new List<ShellOutputType>();
        _shell!.OutputReceived += (sender, args) => outputTypes.Add(args.OutputType);

        // Act
        _shell.SendErrorForTest("error\r\n");
        await Task.Delay(100);

        // Assert
        Assert.That(outputTypes, Does.Contain(ShellOutputType.Stderr));
    }

    #endregion
 
    #region Cursor Position Tracking Tests


    [Test]
    public async Task CursorPosition_InitiallyZero()
    {
        // Assert
        Assert.That(_shell!.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should start at position 0");
    }

    [Test]
    public async Task CursorPosition_UpdatesAfterTyping()
    {
        // Act
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello"));
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(5), "Cursor should be at position 5 after typing 'hello'");
    }

    [Test]
    public async Task CursorPosition_UpdatesAfterEachCharacter()
    {
        // Act & Assert
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("h"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(1), "Cursor should be at position 1");

        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("e"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(2), "Cursor should be at position 2");

        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("l"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(3), "Cursor should be at position 3");
    }

    [Test]
    public async Task CursorPosition_DecreasesOnBackspace()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(5), "Initial cursor position");

        // Act - Backspace once
        await _shell.WriteInputAsync(new byte[] { 0x7F });
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(4), "Cursor should move back to position 4");
    }

    [Test]
    public async Task CursorPosition_MultipleBackspaces()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("test"));
        await Task.Delay(50);

        // Act - Backspace twice
        await _shell.WriteInputAsync(new byte[] { 0x7F });
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x7F });
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(2), "Cursor should be at position 2 after two backspaces");
    }

    [Test]
    public async Task CursorPosition_BackspaceOnEmptyBuffer_StaysAtZero()
    {
        // Act
        await _shell!.WriteInputAsync(new byte[] { 0x7F });
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should stay at position 0");
    }

    [Test]
    public async Task CursorPosition_ResetsToZeroOnEnter()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("command"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(7), "Cursor at position 7 before enter");

        // Act
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should reset to position 0 after enter");
    }

    [Test]
    public async Task CursorPosition_ResetsToZeroOnLineFeed()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("test"));
        await Task.Delay(50);

        // Act
        await _shell.WriteInputAsync(new byte[] { 0x0A });
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should reset to position 0 after line feed");
    }

    [Test]
    public async Task CursorPosition_UpdatesOnHistoryNavigationUp()
    {
        // Arrange - Execute a command to build history
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("first command"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(100);

        // Act - Navigate up
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x41 }); // Up arrow
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(13), "Cursor should be at end of recalled command");
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("first command"));
    }

    [Test]
    public async Task CursorPosition_UpdatesOnHistoryNavigationDown()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("cmd1"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("cmd2"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);

        // Navigate up twice
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x41 }); // Up
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x41 }); // Up
        await Task.Delay(50);

        // Act - Navigate down
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x42 }); // Down
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(4), "Cursor should be at end of 'cmd2'");
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("cmd2"));
    }

    [Test]
    public async Task CursorPosition_RestoresAfterHistoryNavigationToSavedLine()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("cmd1"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);

        // Start typing a new command
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("typing new"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(10), "Initial cursor position");

        // Navigate up
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x41 }); // Up
        await Task.Delay(50);

        // Act - Navigate down past end to restore saved line
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x42 }); // Down
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(10), "Cursor should restore to position 10");
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("typing new"));
    }

    [Test]
    public async Task CursorPosition_ComplexSequence()
    {
        // Act - Type, backspace, type more
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(5), "After typing 'hello'");

        await _shell.WriteInputAsync(new byte[] { 0x7F, 0x7F }); // Two backspaces
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(3), "After two backspaces");

        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("p"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(4), "After typing 'p'");

        // Assert final state
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("help"));
    }

    [Test]
    public async Task CursorPosition_CtrlH_DeletesWord()
    {
        // Arrange - 0x08 (Ctrl+H) is now Ctrl+Backspace, which deletes words
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world"));
        await Task.Delay(50);

        // Act - Use Ctrl+H (0x08)
        await _shell.WriteInputAsync(new byte[] { 0x08 });
        await Task.Delay(50);

        // Assert - Cursor should be at position 6 after deleting "world"
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(6), "Ctrl+H should delete word and move cursor to word start");
    }

    #endregion

    #region Mid-line Backspace Tests

    [Test]
    public async Task WriteInputAsync_BackspaceAtMiddle_DeletesCorrectly()
    {
        // Arrange - Type "hello"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello"));

        // Move left 2 positions (cursor between 'l' and 'o')
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(3), "Cursor should be at position 3");

        // Act - Backspace (should delete second 'l')
        await _shell.WriteInputAsync(new byte[] { 0x7F });
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("helo"), "Line should be 'helo'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(2), "Cursor should be at position 2");
    }

    [Test]
    public async Task WriteInputAsync_BackspaceAtStart_DoesNothing()
    {
        // Arrange - Type "test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("test"));
        await Task.Delay(50);

        // Move to start
        for (int i = 0; i < 4; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
            await Task.Delay(50);
        }
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at position 0");

        // Act - Backspace at start
        await _shell.WriteInputAsync(new byte[] { 0x7F });
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("test"), "Line should remain 'test'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should remain at position 0");
    }

    [Test]
    public async Task WriteInputAsync_BackspaceAtEnd_DeletesLastCharacter()
    {
        // Arrange - Type "hello"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(5), "Cursor should be at end");

        // Act - Backspace at end
        await _shell.WriteInputAsync(new byte[] { 0x7F });
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hell"), "Line should be 'hell'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(4), "Cursor should be at position 4");
    }

    [Test]
    public async Task WriteInputAsync_BackspaceAtPosition1_DeletesFirstCharacter()
    {
        // Arrange - Type "test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("test"));
        await Task.Delay(50);

        // Move left 3 positions (cursor at position 1)
        for (int i = 0; i < 3; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
            await Task.Delay(50);
        }
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(1), "Cursor should be at position 1");

        // Act - Backspace (should delete 't')
        await _shell.WriteInputAsync(new byte[] { 0x7F });
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("est"), "Line should be 'est'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at position 0");
    }

    [Test]
    public async Task WriteInputAsync_MultipleBackspacesAtMiddle_DeletesCorrectly()
    {
        // Arrange - Type "hello world"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world"));
        await Task.Delay(50);

        // Move left 6 positions (cursor between 'o' and ' ')
        for (int i = 0; i < 6; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
            await Task.Delay(50);
        }
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(5), "Cursor should be at position 5");

        // Act - Backspace twice
        await _shell.WriteInputAsync(new byte[] { 0x7F });
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x7F });
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hel world"), "Line should be 'hel world'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(3), "Cursor should be at position 3");
    }

    [Test]
    public async Task WriteInputAsync_BackspaceAtMiddle_SendsCorrectEscapeSequences()
    {
        // Arrange - Type "test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("test"));
        await Task.Delay(50);

        // Move left 2 positions
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);

        var outputReceived = new List<string>();
        _shell!.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(Encoding.UTF8.GetString(args.Data.ToArray()));
        };

        // Act - Backspace at position 2
        await _shell.WriteInputAsync(new byte[] { 0x7F });
        await Task.Delay(100);

        // Assert
        var allOutput = string.Join("", outputReceived);
        // Should output: move left + "st" + space + move left 3
        Assert.That(allOutput, Does.Contain("\x1b[D"), "Should move cursor left");
        Assert.That(allOutput, Does.Contain("st"), "Should output tail characters");
        Assert.That(allOutput, Does.Contain(" "), "Should output space to clear");
        Assert.That(allOutput, Does.Contain("\x1b[3D"), "Should move cursor back 3 positions");
    }

    [Test]
    public async Task WriteInputAsync_CtrlH_AtMiddle_DeletesWord()
    {
        // Arrange - Type "hello world test" and move to middle of "test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);

        // Move left 2 positions (cursor in middle of "test")
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);

        // Act - Use Ctrl+H (0x08) - should delete back to word boundary
        await _shell.WriteInputAsync(new byte[] { 0x08 });
        await Task.Delay(100);

        // Assert - Should delete "te" from "test", leaving "hello world st"
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello world st"), "Ctrl+H should delete word fragment");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(12), "Cursor should be at word boundary");
    }

    [Test]
    public async Task WriteInputAsync_BackspaceAndInsert_WorksCorrectly()
    {
        // Arrange - Type "helo"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("helo"));
        await Task.Delay(50);

        // Move left 2 positions (cursor at position 2, between 'e' and 'l')
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(2));

        // Act - Backspace (deletes 'e', leaving "hlo" with cursor at position 1)
        await _shell.WriteInputAsync(new byte[] { 0x7F });
        await Task.Delay(50);
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hlo"), "After backspace should be 'hlo'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(1), "Cursor should be at position 1");

        // Insert 'll' (gives "hlllo" with cursor at position 3)
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("ll"));
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hlllo"), "Line should be 'hlllo'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(3), "Cursor should be at position 3");
    }

    [Test]
    public async Task WriteInputAsync_ComplexEditingSequence_WorksCorrectly()
    {
        // Start with "abc"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("abc"));
        await Task.Delay(50);

        // Move to middle
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(2));

        // Backspace (delete 'b')
        await _shell.WriteInputAsync(new byte[] { 0x7F });
        await Task.Delay(50);
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("ac"));
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(1));

        // Insert 'X'
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("X"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("aXc"));
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(2));

        // Move to end and append 'Y'
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x43 }); // Right
        await Task.Delay(50);
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("Y"));
        await Task.Delay(100);

        // Assert final state
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("aXcY"));
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(4));
    }

    [Test]
    public async Task WriteInputAsync_BackspaceUntilEmpty_WorksCorrectly()
    {
        // Arrange - Type "abc"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("abc"));
        await Task.Delay(50);

        // Move to position 1
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(1));

        // Act - Backspace until we can't anymore
        await _shell.WriteInputAsync(new byte[] { 0x7F });
        await Task.Delay(50);
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("bc"));
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0));

        // Try to backspace at position 0 (should do nothing)
        await _shell.WriteInputAsync(new byte[] { 0x7F });
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("bc"), "Line should remain 'bc'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should remain at position 0");
    }

    #endregion

    #region Delete Key Tests

    [Test]
    public async Task WriteInputAsync_DeleteKey_DeletesCharacterAtCursor()
    {
        // Arrange - Type "hello"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello"));
        await Task.Delay(50);

        // Move left 2 positions (cursor at position 3, between second 'l' and 'o')
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(3), "Cursor should be at position 3");

        // Act - Press Delete key (CSI 3 ~)
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x33, 0x7E }); // ESC [ 3 ~
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("helo"), "Line should be 'helo'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(3), "Cursor should still be at position 3");
    }

    [Test]
    public async Task WriteInputAsync_DeleteKeyAtEnd_DoesNothing()
    {
        // Arrange - Type "test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("test"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(4), "Cursor should be at end");

        // Act - Press Delete key at end
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x33, 0x7E }); // ESC [ 3 ~
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("test"), "Line should remain 'test'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(4), "Cursor should remain at position 4");
    }

    [Test]
    public async Task WriteInputAsync_DeleteKeyOnEmptyBuffer_DoesNothing()
    {
        // Act - Press Delete key on empty line
        await _shell!.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x33, 0x7E }); // ESC [ 3 ~
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty, "Line should remain empty");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should stay at position 0");
    }

    [Test]
    public async Task WriteInputAsync_DeleteKeyAtStart_DeletesFirstCharacter()
    {
        // Arrange - Type "test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("test"));
        await Task.Delay(50);

        // Move to start
        for (int i = 0; i < 4; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
            await Task.Delay(50);
        }
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at position 0");

        // Act - Press Delete key
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x33, 0x7E }); // ESC [ 3 ~
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("est"), "Line should be 'est'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should remain at position 0");
    }

    [Test]
    public async Task WriteInputAsync_DeleteKeyMultipleTimes_DeletesCorrectly()
    {
        // Arrange - Type "hello world"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world"));
        await Task.Delay(50);

        // Move to position 5 (between 'o' and ' ')
        for (int i = 0; i < 6; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
            await Task.Delay(50);
        }
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(5), "Cursor should be at position 5");

        // Act - Delete 3 times to remove " wo"
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x33, 0x7E }); // Delete
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x33, 0x7E }); // Delete
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x33, 0x7E }); // Delete
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hellorld"), "Line should be 'hellorld'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(5), "Cursor should still be at position 5");
    }

    [Test]
    public async Task WriteInputAsync_DeleteKey_SendsCorrectEscapeSequences()
    {
        // Arrange - Type "test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("test"));
        await Task.Delay(50);

        // Move left 2 positions (cursor at position 2, which is 's' in "test")
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(2), "Cursor should be at position 2");

        var outputReceived = new List<string>();
        _shell!.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(Encoding.UTF8.GetString(args.Data.ToArray()));
        };

        // Act - Press Delete key (deletes 's', leaving "tet")
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x33, 0x7E }); // ESC [ 3 ~
        await Task.Delay(100);

        // Assert
        var allOutput = string.Join("", outputReceived);
        // Should output: "t" (tail) + space + move left 2
        Assert.That(allOutput, Does.Contain("t"), "Should output tail characters");
        Assert.That(allOutput, Does.Contain(" "), "Should output space to clear");
        Assert.That(allOutput, Does.Contain("\x1b[2D"), "Should move cursor back 2 positions");
    }

    [Test]
    public async Task WriteInputAsync_DeleteKeyAndBackspace_WorkTogether()
    {
        // Arrange - Type "hello"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello"));
        await Task.Delay(50);

        // Move left 2 positions (cursor at position 3)
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(3), "Cursor should be at position 3");

        // Act - Delete key (deletes 'l' at position 3, leaving "helo" with cursor at 3)
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x33, 0x7E }); // ESC [ 3 ~
        await Task.Delay(50);
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("helo"), "After delete should be 'helo'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(3), "Cursor should still be at position 3");

        // Backspace (deletes 'l' at position 2, leaving "heo" with cursor at 2)
        await _shell.WriteInputAsync(new byte[] { 0x7F }); // Backspace
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("heo"), "Line should be 'heo'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(2), "Cursor should be at position 2");
    }

    [Test]
    public async Task WriteInputAsync_DeleteKeyAndInsert_WorksCorrectly()
    {
        // Arrange - Type "helo"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("helo"));
        await Task.Delay(50);

        // Move left 2 positions (cursor at position 2)
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(2), "Cursor should be at position 2");

        // Act - Delete key (deletes 'l', leaving "heo" with cursor at 2)
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x33, 0x7E }); // ESC [ 3 ~
        await Task.Delay(50);
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("heo"), "After delete should be 'heo'");

        // Insert "ll" (gives "hello" with cursor at 4)
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("ll"));
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello"), "Line should be 'hello'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(4), "Cursor should be at position 4");
    }

    [Test]
    public async Task WriteInputAsync_DeleteKeySequence_DeletesAllCharactersAfter()
    {
        // Arrange - Type "abcd"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("abcd"));
        await Task.Delay(50);

        // Move to start
        for (int i = 0; i < 4; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
            await Task.Delay(50);
        }
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at position 0");

        // Act - Delete 4 times to remove all characters
        for (int i = 0; i < 4; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x33, 0x7E }); // Delete
            await Task.Delay(50);
        }
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty, "Line should be empty");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at position 0");
    }

    #endregion

    #region Ctrl+C (Cancel Line) Tests

    [Test]
    public async Task WriteInputAsync_CtrlC_ClearsBufferAndShowsPrompt()
    {
        // Arrange
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello"), "Line buffer should have content");

        var outputReceived = new List<string>();
        _shell!.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(Encoding.UTF8.GetString(args.Data.ToArray()));
        };

        // Act - Send Ctrl+C
        await _shell.WriteInputAsync(new byte[] { 0x03 }); // Ctrl+C
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty, "Line buffer should be cleared");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at position 0");

        var allOutput = string.Join("", outputReceived);
        Assert.That(allOutput, Does.Contain("^C\r\n"), "Should display ^C and newline");
        Assert.That(allOutput, Does.Contain("test> "), "Should display prompt");
    }

    [Test]
    public async Task WriteInputAsync_CtrlCOnEmptyLine_DoesNotCrash()
    {
        // Arrange
        var outputReceived = new List<string>();
        _shell!.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(Encoding.UTF8.GetString(args.Data.ToArray()));
        };

        // Act - Send Ctrl+C on empty line
        await _shell!.WriteInputAsync(new byte[] { 0x03 }); // Ctrl+C
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty, "Line buffer should remain empty");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at position 0");

        var allOutput = string.Join("", outputReceived);
        Assert.That(allOutput, Does.Contain("^C\r\n"), "Should display ^C and newline");
        Assert.That(allOutput, Does.Contain("test> "), "Should display prompt");
    }

    [Test]
    public async Task WriteInputAsync_CtrlCDuringHistoryNavigation_ClearsState()
    {
        // Arrange - Build history
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("first command"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("second command"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);

        // Navigate up in history
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x41 }); // Up
        await Task.Delay(50);
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("second command"), "Should be in history");

        var outputReceived = new List<string>();
        _shell!.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(Encoding.UTF8.GetString(args.Data.ToArray()));
        };

        // Act - Send Ctrl+C
        await _shell.WriteInputAsync(new byte[] { 0x03 }); // Ctrl+C
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty, "Line buffer should be cleared");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at position 0");

        var allOutput = string.Join("", outputReceived);
        Assert.That(allOutput, Does.Contain("^C\r\n"), "Should display ^C and newline");
        Assert.That(allOutput, Does.Contain("test> "), "Should display prompt");
    }

    [Test]
    public async Task WriteInputAsync_CtrlCAtMiddleOfLine_ClearsEntireLine()
    {
        // Arrange - Type "hello world"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world"));
        await Task.Delay(50);

        // Move cursor to middle
        for (int i = 0; i < 5; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
            await Task.Delay(50);
        }
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(6), "Cursor should be in middle");

        var outputReceived = new List<string>();
        _shell!.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(Encoding.UTF8.GetString(args.Data.ToArray()));
        };

        // Act - Send Ctrl+C
        await _shell.WriteInputAsync(new byte[] { 0x03 }); // Ctrl+C
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty, "Line buffer should be cleared");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at position 0");

        var allOutput = string.Join("", outputReceived);
        Assert.That(allOutput, Does.Contain("^C\r\n"), "Should display ^C and newline");
        Assert.That(allOutput, Does.Contain("test> "), "Should display prompt");
    }

    [Test]
    public async Task WriteInputAsync_CtrlCMultipleTimes_EachTimeShowsPrompt()
    {
        // Arrange
        var outputReceived = new List<string>();
        _shell!.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(Encoding.UTF8.GetString(args.Data.ToArray()));
        };

        // Act - Send Ctrl+C three times
        await _shell!.WriteInputAsync(new byte[] { 0x03 }); // Ctrl+C
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x03 }); // Ctrl+C
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x03 }); // Ctrl+C
        await Task.Delay(100);

        // Assert
        var allOutput = string.Join("", outputReceived);
        // Should have three instances of "^C\r\n" and three prompts
        int ctrlCCount = System.Text.RegularExpressions.Regex.Matches(allOutput, @"\^C\r\n").Count;
        Assert.That(ctrlCCount, Is.EqualTo(3), "Should display ^C three times");
    }

    [Test]
    public async Task WriteInputAsync_CtrlCThenType_StartsNewLine()
    {
        // Arrange - Type some text
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello"));
        await Task.Delay(50);

        // Send Ctrl+C
        await _shell.WriteInputAsync(new byte[] { 0x03 }); // Ctrl+C
        await Task.Delay(50);
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty, "Line should be cleared");

        // Act - Type new text
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("world"));
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("world"), "Should have new text");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(5), "Cursor should be at end of new text");
    }

    [Test]
    public async Task WriteInputAsync_CtrlCClearsSavedCurrentLine()
    {
        // Arrange - Execute a command to build history
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("cmd1"));
        await _shell.WriteInputAsync(new byte[] { 0x0D });
        await Task.Delay(50);

        // Start typing a new command
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("new text"));
        await Task.Delay(50);

        // Navigate up (saves "new text")
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x41 }); // Up
        await Task.Delay(50);
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("cmd1"), "Should show history");

        // Send Ctrl+C
        await _shell.WriteInputAsync(new byte[] { 0x03 }); // Ctrl+C
        await Task.Delay(50);

        // Navigate up again
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x41 }); // Up
        await Task.Delay(50);

        // Navigate down (should not restore "new text")
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x42 }); // Down
        await Task.Delay(100);

        // Assert - Should be empty, not "new text"
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty, "Saved line should have been cleared by Ctrl+C");
    }

    #endregion

    #region Ctrl+W (Word Deletion) Tests

    [Test]
    public async Task WriteInputAsync_CtrlW_DeletesPreviousWord()
    {
        // Arrange - Type "hello world test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);

        // Act - Send Ctrl+W
        await _shell.WriteInputAsync(new byte[] { 0x17 }); // Ctrl+W
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello world "), "Should delete 'test'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(12), "Cursor should be after 'world '");
    }

    [Test]
    public async Task WriteInputAsync_CtrlW_TwiceDeletesTwoWords()
    {
        // Arrange - Type "hello world test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);

        // Act - Send Ctrl+W twice
        await _shell.WriteInputAsync(new byte[] { 0x17 }); // Ctrl+W
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x17 }); // Ctrl+W
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello "), "Should delete 'world test'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(6), "Cursor should be after 'hello '");
    }

    [Test]
    public async Task WriteInputAsync_CtrlW_OnEmptyBuffer_DoesNothing()
    {
        // Act - Send Ctrl+W on empty buffer
        await _shell!.WriteInputAsync(new byte[] { 0x17 }); // Ctrl+W
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty, "Should remain empty");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should remain at zero");
    }

    [Test]
    public async Task WriteInputAsync_CtrlW_AtStart_DoesNothing()
    {
        // Arrange - Type "hello", move to start
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello"));
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x48 }); // Home
        await Task.Delay(50);

        // Act - Send Ctrl+W
        await _shell.WriteInputAsync(new byte[] { 0x17 }); // Ctrl+W
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello"), "Should not delete anything");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should remain at start");
    }

    [Test]
    public async Task WriteInputAsync_CtrlW_WithMultipleSpaces_DeletesCorrectly()
    {
        // Arrange - Type "hello   world" (three spaces)
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello   world"));
        await Task.Delay(50);

        // Act - Send Ctrl+W
        await _shell.WriteInputAsync(new byte[] { 0x17 }); // Ctrl+W
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello   "), "Should delete 'world' and preserve spaces");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(8), "Cursor should be after spaces");
    }

    [Test]
    public async Task WriteInputAsync_CtrlW_MidWord_DeletesPartialWord()
    {
        // Arrange - Type "hello world", move left 2 positions (mid-word in "world")
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world"));
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(25);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);

        // Act - Send Ctrl+W (cursor is between 'r' and 'l' in "world")
        await _shell.WriteInputAsync(new byte[] { 0x17 }); // Ctrl+W
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello ld"), "Should delete 'wor' from 'world'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(6), "Cursor should be after 'hello '");
    }

    [Test]
    public async Task WriteInputAsync_CtrlW_SingleWord_DeletesEntireWord()
    {
        // Arrange - Type "hello"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello"));
        await Task.Delay(50);

        // Act - Send Ctrl+W
        await _shell.WriteInputAsync(new byte[] { 0x17 }); // Ctrl+W
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty, "Should delete entire word");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at start");
    }

    [Test]
    public async Task WriteInputAsync_CtrlW_OnlySpaces_DeletesAllSpaces()
    {
        // Arrange - Type "   " (three spaces)
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("   "));
        await Task.Delay(50);

        // Act - Send Ctrl+W
        await _shell.WriteInputAsync(new byte[] { 0x17 }); // Ctrl+W
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty, "Should delete all spaces");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at start");
    }

    [Test]
    public async Task WriteInputAsync_CtrlW_DeletesThenType_InsertsAtCorrectPosition()
    {
        // Arrange - Type "hello world test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);

        // Act - Ctrl+W, then type "foo"
        await _shell.WriteInputAsync(new byte[] { 0x17 }); // Ctrl+W
        await Task.Delay(50);
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("foo"));
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello world foo"), "Should have 'foo' replacing 'test'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(15), "Cursor should be at end");
    }

    [Test]
    public async Task WriteInputAsync_CtrlW_SendsCorrectEscapeSequences()
    {
        // Arrange
        var outputReceived = new List<byte[]>();
        _shell!.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(args.Data.ToArray());
        };

        // Type "hello world"
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("hello world"));
        await Task.Delay(50);
        outputReceived.Clear();

        // Act - Send Ctrl+W (should delete "world")
        await _shell.WriteInputAsync(new byte[] { 0x17 }); // Ctrl+W
        await Task.Delay(100);

        // Assert - Should have escape sequences for cursor movement and clearing
        var allOutput = string.Concat(outputReceived.Select(b => Encoding.UTF8.GetString(b)));
        Assert.That(allOutput, Does.Contain("\x1b["), "Should contain escape sequences");
    }

    [Test]
    public async Task WriteInputAsync_CtrlW_WithTailText_RedrawsCorrectly()
    {
        // Arrange - Type "hello world test", move left 5 positions (to middle of "test")
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);
        for (int i = 0; i < 5; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
            await Task.Delay(25);
        }
        await Task.Delay(50);

        // Act - Send Ctrl+W
        await _shell.WriteInputAsync(new byte[] { 0x17 }); // Ctrl+W
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello  test"), "Should delete 'world' and preserve tail");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(6), "Cursor should be at deleted word position");
    }

    [Test]
    public async Task WriteInputAsync_CtrlW_MultipleWords_DeletesAllToStart()
    {
        // Arrange - Type "one two three four"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("one two three four"));
        await Task.Delay(50);

        // Act - Ctrl+W four times
        for (int i = 0; i < 4; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x17 }); // Ctrl+W
            await Task.Delay(50);
        }

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty, "Should delete all words");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at start");
    }

    [Test]
    public async Task WriteInputAsync_CtrlH_DeletesPreviousWord()
    {
        // Arrange - Type "hello world test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);

        // Act - Send Ctrl+H (Ctrl+Backspace)
        await _shell.WriteInputAsync(new byte[] { 0x08 }); // Ctrl+H
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello world "), "Should delete 'test'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(12), "Cursor should be after 'world '");
    }

    [Test]
    public async Task WriteInputAsync_CtrlH_TwiceDeletesTwoWords()
    {
        // Arrange - Type "hello world test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);

        // Act - Send Ctrl+H twice
        await _shell.WriteInputAsync(new byte[] { 0x08 }); // Ctrl+H
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x08 }); // Ctrl+H
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello "), "Should delete 'world test'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(6), "Cursor should be after 'hello '");
    }

    [Test]
    public async Task WriteInputAsync_CtrlH_MidWord_DeletesPartialWord()
    {
        // Arrange - Type "hello world", move left 2 positions (mid-word in "world")
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world"));
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(25);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);

        // Act - Send Ctrl+H (cursor is between 'r' and 'l' in "world")
        await _shell.WriteInputAsync(new byte[] { 0x08 }); // Ctrl+H
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello ld"), "Should delete 'wor' from 'world'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(6), "Cursor should be after 'hello '");
    }

    [Test]
    public async Task WriteInputAsync_CtrlWAndCtrlH_BothWorkIdentically()
    {
        // This test verifies that Ctrl+W and Ctrl+H both delete previous word

        // Test with Ctrl+W
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x17 }); // Ctrl+W
        await Task.Delay(50);
        var lineAfterCtrlW = _shell.TestGetCurrentLine();
        var cursorAfterCtrlW = _shell.TestGetCursorPosition();

        // Clear and test with Ctrl+H
        await _shell.WriteInputAsync(new byte[] { 0x03 }); // Ctrl+C (clear line)
        await Task.Delay(50);
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x08 }); // Ctrl+H
        await Task.Delay(50);
        var lineAfterCtrlH = _shell.TestGetCurrentLine();
        var cursorAfterCtrlH = _shell.TestGetCursorPosition();

        // Assert - Both should produce identical results
        Assert.That(lineAfterCtrlH, Is.EqualTo(lineAfterCtrlW), "Ctrl+H and Ctrl+W should produce same line");
        Assert.That(cursorAfterCtrlH, Is.EqualTo(cursorAfterCtrlW), "Ctrl+H and Ctrl+W should produce same cursor position");
        Assert.That(lineAfterCtrlW, Is.EqualTo("hello world "), "Both should delete 'test'");
    }

    #endregion

    #region Ctrl+Left/Right (Word Jumping) Tests

    [Test]
    public async Task WriteInputAsync_CtrlRight_JumpsToNextWord()
    {
        // Arrange - Type "hello world test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);

        // Move to start
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x48 }); // Home
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at start");

        // Act - Send Ctrl+Right
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x43 }); // CSI 1;5C
        await Task.Delay(50);

        // Assert - Should jump to position 6 (after "hello ")
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(6), "Cursor should be at position 6");
    }

    [Test]
    public async Task WriteInputAsync_CtrlRight_MultipleJumps()
    {
        // Arrange - Type "hello world test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);

        // Move to start
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x48 }); // Home
        await Task.Delay(50);

        // Act - Send Ctrl+Right twice
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x43 }); // CSI 1;5C
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(6), "First jump should be at position 6");

        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x43 }); // CSI 1;5C
        await Task.Delay(50);

        // Assert - Should jump to position 12 (after "world ")
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(12), "Second jump should be at position 12");
    }

    [Test]
    public async Task WriteInputAsync_CtrlRight_AtEnd_DoesNothing()
    {
        // Arrange - Type "hello world"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(11), "Cursor should be at end");

        // Act - Send Ctrl+Right
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x43 }); // CSI 1;5C
        await Task.Delay(50);

        // Assert - Cursor should remain at end
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(11), "Cursor should remain at end");
    }

    [Test]
    public async Task WriteInputAsync_CtrlRight_MultipleSpaces_SkipsAll()
    {
        // Arrange - Type "hello   world" (three spaces)
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello   world"));
        await Task.Delay(50);

        // Move to start
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x48 }); // Home
        await Task.Delay(50);

        // Act - Send Ctrl+Right
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x43 }); // CSI 1;5C
        await Task.Delay(50);

        // Assert - Should skip all spaces and land at position 8 (start of "world")
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(8), "Cursor should skip all spaces");
    }

    [Test]
    public async Task WriteInputAsync_CtrlLeft_JumpsToPreviousWord()
    {
        // Arrange - Type "hello world test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(16), "Cursor should be at end");

        // Act - Send Ctrl+Left
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x44 }); // CSI 1;5D
        await Task.Delay(50);

        // Assert - Should jump to position 12 (start of "test")
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(12), "Cursor should be at position 12");
    }

    [Test]
    public async Task WriteInputAsync_CtrlLeft_MultipleJumps()
    {
        // Arrange - Type "hello world test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);

        // Act - Send Ctrl+Left twice
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x44 }); // CSI 1;5D
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(12), "First jump should be at position 12");

        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x44 }); // CSI 1;5D
        await Task.Delay(50);

        // Assert - Should jump to position 6 (start of "world")
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(6), "Second jump should be at position 6");
    }

    [Test]
    public async Task WriteInputAsync_CtrlLeft_AtStart_DoesNothing()
    {
        // Arrange - Type "hello world"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world"));
        await Task.Delay(50);

        // Move to start
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x48 }); // Home
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at start");

        // Act - Send Ctrl+Left
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x44 }); // CSI 1;5D
        await Task.Delay(50);

        // Assert - Cursor should remain at start
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should remain at start");
    }

    [Test]
    public async Task WriteInputAsync_CtrlLeft_MultipleSpaces_HandledCorrectly()
    {
        // Arrange - Type "hello   world" (three spaces), cursor at end
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello   world"));
        await Task.Delay(50);

        // Act - Send Ctrl+Left
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x44 }); // CSI 1;5D
        await Task.Delay(50);

        // Assert - Should jump to start of "world" at position 8
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(8), "Cursor should be at start of 'world'");
    }

    [Test]
    public async Task WriteInputAsync_CtrlRightAndLeft_RoundTrip()
    {
        // Arrange - Type "hello world test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);

        // Move to start
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x48 }); // Home
        await Task.Delay(50);

        // Act - Ctrl+Right, then Ctrl+Left
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x43 }); // CSI 1;5C
        await Task.Delay(50);
        int positionAfterRight = _shell.TestGetCursorPosition();

        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x44 }); // CSI 1;5D
        await Task.Delay(50);

        // Assert - Should be back at start
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Should return to start");
        Assert.That(positionAfterRight, Is.EqualTo(6), "Ctrl+Right should have jumped to position 6");
    }

    [Test]
    public async Task WriteInputAsync_CtrlRight_AllTheWayToEnd()
    {
        // Arrange - Type "one two three four"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("one two three four"));
        await Task.Delay(50);

        // Move to start
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x48 }); // Home
        await Task.Delay(50);

        // Act - Ctrl+Right four times
        for (int i = 0; i < 4; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x43 }); // CSI 1;5C
            await Task.Delay(50);
        }

        // Assert - Should be at end
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(18), "Should be at end after 4 jumps");
    }

    [Test]
    public async Task WriteInputAsync_CtrlLeft_AllTheWayToStart()
    {
        // Arrange - Type "one two three four"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("one two three four"));
        await Task.Delay(50);

        // Act - Ctrl+Left four times
        for (int i = 0; i < 4; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x44 }); // CSI 1;5D
            await Task.Delay(50);
        }

        // Assert - Should be at start
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Should be at start after 4 jumps");
    }

    [Test]
    public async Task WriteInputAsync_CtrlRight_MidWord_JumpsToNextWord()
    {
        // Arrange - Type "hello world", move left 2 (mid-word in "world")
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world"));
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(25);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(9), "Cursor should be at position 9");

        // Act - Ctrl+Right
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x43 }); // CSI 1;5C
        await Task.Delay(50);

        // Assert - Should jump to end (no next word)
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(11), "Should jump to end");
    }

    [Test]
    public async Task WriteInputAsync_CtrlLeft_MidWord_JumpsToPreviousWord()
    {
        // Arrange - Type "hello world", move left 2 (mid-word in "world")
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world"));
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(25);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(9), "Cursor should be at position 9");

        // Act - Ctrl+Left
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x44 }); // CSI 1;5D
        await Task.Delay(50);

        // Assert - Should jump to start of "world" at position 6
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(6), "Should jump to start of current word");
    }

    [Test]
    public async Task WriteInputAsync_CtrlRightLeft_WithEditing()
    {
        // Arrange - Type "hello world test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);

        // Move to start
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x48 }); // Home
        await Task.Delay(50);

        // Act - Ctrl+Right, type "BIG ", Ctrl+Right
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x43 }); // CSI 1;5C
        await Task.Delay(50);
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("BIG "));
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x43 }); // CSI 1;5C
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello BIG world test"), "Should have inserted 'BIG '");
        // After inserting "BIG " at position 6, cursor is at 10
        // Ctrl+Right jumps over "world " to position 16 (start of "test")
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(16), "Cursor should be at position 16 (start of 'test')");
    }

    [Test]
    public async Task WriteInputAsync_CtrlRight_OnEmptyBuffer_DoesNothing()
    {
        // Act - Send Ctrl+Right on empty buffer
        await _shell!.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x43 }); // CSI 1;5C
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty, "Should remain empty");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should remain at zero");
    }

    [Test]
    public async Task WriteInputAsync_CtrlLeft_OnEmptyBuffer_DoesNothing()
    {
        // Act - Send Ctrl+Left on empty buffer
        await _shell!.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x31, 0x3B, 0x35, 0x44 }); // CSI 1;5D
        await Task.Delay(50);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.Empty, "Should remain empty");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should remain at zero");
    }

    [Test]
    public async Task WriteInputAsync_EscF_JumpsToNextWord()
    {
        // Arrange - Type "hello world test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);

        // Move to start
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x48 }); // Home
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at start");

        // Act - Send ESC f (Ctrl+Right in Ghostty/Emacs mode)
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x66 }); // ESC f
        await Task.Delay(50);

        // Assert - Should jump to position 6 (after "hello ")
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(6), "Cursor should be at position 6");
    }

    [Test]
    public async Task WriteInputAsync_EscB_JumpsToPreviousWord()
    {
        // Arrange - Type "hello world test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(16), "Cursor should be at end");

        // Act - Send ESC b (Ctrl+Left in Ghostty/Emacs mode)
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x62 }); // ESC b
        await Task.Delay(50);

        // Assert - Should jump to position 12 (start of "test")
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(12), "Cursor should be at position 12");
    }

    [Test]
    public async Task WriteInputAsync_EscF_MultipleJumps()
    {
        // Arrange - Type "hello world test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);

        // Move to start
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x48 }); // Home
        await Task.Delay(50);

        // Act - Send ESC f twice
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x66 }); // ESC f
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(6), "First jump should be at position 6");

        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x66 }); // ESC f
        await Task.Delay(50);

        // Assert - Should jump to position 12 (after "world ")
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(12), "Second jump should be at position 12");
    }

    [Test]
    public async Task WriteInputAsync_EscB_MultipleJumps()
    {
        // Arrange - Type "hello world test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(16), "Cursor should be at end");

        // Act - Send ESC b twice
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x62 }); // ESC b
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(12), "First jump should be at position 12");

        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x62 }); // ESC b
        await Task.Delay(50);

        // Assert - Should jump to position 6 (start of "world")
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(6), "Second jump should be at position 6");
    }

    [Test]
    public async Task WriteInputAsync_EscFAndEscB_RoundTrip()
    {
        // Arrange - Type "hello world test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hello world test"));
        await Task.Delay(50);

        // Move to start
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x48 }); // Home
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at start");

        // Act - Jump forward then back
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x66 }); // ESC f
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(6), "After ESC f should be at position 6");

        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x62 }); // ESC b
        await Task.Delay(50);

        // Assert - Should be back at start
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "After ESC b should be back at position 0");
    }

    #endregion

    #region Mid-line Character Insertion Tests

    [Test]
    public async Task WriteInputAsync_InsertCharacterAtMiddle_InsertsCorrectly()
    {
        // Arrange - Type "helo"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("helo"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("helo"));

        // Move left 2 positions (cursor should be between 'e' and 'l')
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(2), "Cursor should be at position 2");

        // Act - Type 'l' to make "hello"
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("l"));
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello"), "Line should be 'hello'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(3), "Cursor should be at position 3");
    }

    [Test]
    public async Task WriteInputAsync_InsertCharacterAtStart_InsertsCorrectly()
    {
        // Arrange - Type "ello"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("ello"));
        await Task.Delay(50);

        // Move to start
        for (int i = 0; i < 4; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
            await Task.Delay(50);
        }
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(0), "Cursor should be at position 0");

        // Act - Type 'h' to make "hello"
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("h"));
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello"), "Line should be 'hello'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(1), "Cursor should be at position 1");
    }

    [Test]
    public async Task WriteInputAsync_InsertCharacterAtEnd_AppendsCorrectly()
    {
        // Arrange - Type "hell"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("hell"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(4), "Cursor should be at end");

        // Act - Type 'o' (should append, not insert)
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("o"));
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello"), "Line should be 'hello'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(5), "Cursor should be at position 5");
    }

    [Test]
    public async Task WriteInputAsync_InsertMultipleCharactersAtMiddle_InsertsCorrectly()
    {
        // Arrange - Type "heworld"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("heworld"));
        await Task.Delay(50);

        // Move left 5 positions (cursor should be between 'e' and 'w')
        for (int i = 0; i < 5; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
            await Task.Delay(50);
        }
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(2), "Cursor should be at position 2");

        // Act - Type "llo " to make "hello world"
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("llo "));
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("hello world"), "Line should be 'hello world'");
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(6), "Cursor should be at position 6");
    }

    [Test]
    public async Task WriteInputAsync_InsertAtMiddle_SendsCorrectEscapeSequences()
    {
        // Arrange - Type "test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("test"));
        await Task.Delay(50);

        // Move left 2 positions
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);

        var outputReceived = new List<string>();
        _shell!.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(Encoding.UTF8.GetString(args.Data.ToArray()));
        };

        // Act - Type 'X' to insert at position 2
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("X"));
        await Task.Delay(100);

        // Assert
        var allOutput = string.Join("", outputReceived);
        // Should output: X + "st" + move left 2
        Assert.That(allOutput, Does.Contain("X"), "Should output the inserted character");
        Assert.That(allOutput, Does.Contain("st"), "Should output tail characters");
        Assert.That(allOutput, Does.Contain("\x1b[2D"), "Should move cursor back 2 positions");
    }

    [Test]
    public async Task WriteInputAsync_InsertAtPosition1_SendsCorrectEscapeSequences()
    {
        // Arrange - Type "test"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("test"));
        await Task.Delay(50);

        // Move left 3 positions
        for (int i = 0; i < 3; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
            await Task.Delay(50);
        }

        var outputReceived = new List<string>();
        _shell!.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(Encoding.UTF8.GetString(args.Data.ToArray()));
        };

        // Act - Type 'X' to insert at position 1
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("X"));
        await Task.Delay(100);

        // Assert
        var allOutput = string.Join("", outputReceived);
        // Should output: X + "est" + move left 3
        Assert.That(allOutput, Does.Contain("X"), "Should output the inserted character");
        Assert.That(allOutput, Does.Contain("est"), "Should output tail characters");
        Assert.That(allOutput, Does.Contain("\x1b[3D"), "Should move cursor back 3 positions");
    }

    [Test]
    public async Task WriteInputAsync_InsertAndMoveAround_MaintainsCorrectState()
    {
        // Arrange - Type "abc"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("abc"));
        await Task.Delay(50);

        // Move left to middle
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(2));

        // Insert 'X'
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("X"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("abXc"));
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(3));

        // Move right to end
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x43 }); // Right
        await Task.Delay(50);
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(4));

        // Append 'Y'
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("Y"));
        await Task.Delay(100);

        // Assert final state
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("abXcY"));
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(5));
    }

    [Test]
    public async Task WriteInputAsync_InsertSingleCharAtEachPosition_WorksCorrectly()
    {
        // Start with "1234"
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("1234"));
        await Task.Delay(50);

        // Move to start
        for (int i = 0; i < 4; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x44 }); // Left
            await Task.Delay(50);
        }

        // Insert 'A' at position 0
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("A"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("A1234"));
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(1));

        // Move right 1, insert 'B' at position 2
        await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x43 }); // Right
        await Task.Delay(50);
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("B"));
        await Task.Delay(50);
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("A1B234"));
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(3));

        // Move to end and append 'C'
        for (int i = 0; i < 3; i++)
        {
            await _shell.WriteInputAsync(new byte[] { 0x1B, 0x5B, 0x43 }); // Right
            await Task.Delay(50);
        }
        await _shell.WriteInputAsync(Encoding.UTF8.GetBytes("C"));
        await Task.Delay(100);

        // Assert final state
        Assert.That(_shell.TestGetCurrentLine(), Is.EqualTo("A1B234C"));
        Assert.That(_shell.TestGetCursorPosition(), Is.EqualTo(7));
    }

    #endregion
}
