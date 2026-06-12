using System.Reflection;
using System.Text;
using Brutal.ImGuiApi.Abstractions;
using purrTTY.Core.Terminal;
using purrTTY.CustomShells;
using NUnit.Framework;

namespace purrTTY.CustomShells.Tests.Unit;

/// <summary>
/// Unit tests for GameConsoleShell implementation.
/// Tests focus on shell-specific logic that can be verified without full KSA runtime.
/// </summary>
[TestFixture]
public class GameConsoleShellTests
{
    private TestGameConsoleShell? _shell;
    private StringBuilder _outputBuffer = new();

    [SetUp]
    public void Setup()
    {
        _outputBuffer = new StringBuilder();
        _shell = new TestGameConsoleShell(_outputBuffer);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_shell != null && _shell.IsRunning)
        {
            await _shell.StopAsync();
        }
        _shell?.Dispose();
        _shell = null;
    }

    #region Lifecycle Tests

    [Test]
    public async Task StartAsync_SetsIsRunningToTrue()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);

        // Act
        await _shell!.StartAsync(options);

        // Assert
        Assert.That(_shell.IsRunning, Is.True);
    }

    [Test]
    public async Task StopAsync_SetsIsRunningToFalse()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);

        // Act
        await _shell.StopAsync();

        // Assert
        Assert.That(_shell.IsRunning, Is.False);
    }

    [Test]
    public void Metadata_ReturnsCorrectInformation()
    {
        // Act
        var metadata = _shell!.Metadata;

        // Assert
        Assert.That(metadata.Name, Is.EqualTo("Game Console"));
        Assert.That(metadata.Description, Is.EqualTo("KSA game console interface - execute game commands directly"));
        Assert.That(metadata.Version, Is.EqualTo(new Version(1, 0, 0)));
        Assert.That(metadata.Author, Is.EqualTo("purrTTY"));
        Assert.That(metadata.SupportedFeatures, Does.Contain("colors"));
        Assert.That(metadata.SupportedFeatures, Does.Contain("clear-screen"));
        Assert.That(metadata.SupportedFeatures, Does.Contain("command-execution"));
    }

    [Test]
    public async Task SendInitialOutput_IncludesBannerAndPrompt()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);

        // Act
        _shell.SendInitialOutput();
        await _shell!.FlushOutputAsync();

        // Assert
        var output = _outputBuffer.ToString();
        Assert.That(output, Does.Contain("KSA Game Console Shell"));
        Assert.That(output, Does.Contain("Type 'help' for available commands"));
        Assert.That(output, Does.Contain("Press Ctrl+L to clear screen"));
        Assert.That(output, Does.Contain("ksa> ")); // Default prompt
    }

    [Test]
    public void Dispose_DoesNotThrowException()
    {
        // Act & Assert
        Assert.DoesNotThrow(() => _shell!.Dispose());
    }

    #endregion

    #region Prompt Configuration Tests

    [Test]
    public async Task GetPrompt_ReturnsDefaultPromptWhenEnvironmentUnset()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);

        // Act
        _shell.SendInitialOutput();
        await _shell!.FlushOutputAsync();

        // Assert
        var output = _outputBuffer.ToString();
        Assert.That(output, Does.Contain("ksa> "));
    }

    [Test]
    public async Task GetPrompt_ReturnsCustomPromptFromEnvironment()
    {
        // Arrange - the launcher stamps the configured prompt into the shell environment
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        options.EnvironmentVariables[WellKnownShellEnvironment.GameShellPrompt] = "custom-prompt> ";
        await _shell!.StartAsync(options);

        // Act
        _shell.SendInitialOutput();
        await _shell!.FlushOutputAsync();

        // Assert
        var output = _outputBuffer.ToString();
        Assert.That(output, Does.Contain("custom-prompt> "));
    }

    [Test]
    public async Task GetPrompt_EmptyEnvironmentValue_KeepsDefault()
    {
        // Arrange - an empty configured prompt must not blank the prompt out
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        options.EnvironmentVariables[WellKnownShellEnvironment.GameShellPrompt] = "";
        await _shell!.StartAsync(options);

        // Act
        _shell.SendInitialOutput();
        await _shell!.FlushOutputAsync();

        // Assert
        var output = _outputBuffer.ToString();
        Assert.That(output, Does.Contain("ksa> "));
    }

    #endregion

    #region Built-in Command Tests

    [Test]
    public async Task ClearCommand_SendsClearScreenSequence()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        _outputBuffer.Clear();

        // Act
        _shell.SimulateCommandInput("clear\n");
        await _shell!.FlushOutputAsync();

        // Assert - ESC[3J = Clear screen and scrollback
        var output = _outputBuffer.ToString();
        Assert.That(output, Does.Contain("\x1b[3J"));
    }

    [Test]
    public async Task ClearCommand_IsCaseInsensitive()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);

        // Test various casings
        var testCases = new[] { "clear", "CLEAR", "Clear", "ClEaR" };

        foreach (var cmd in testCases)
        {
            _outputBuffer.Clear();

            // Act
            _shell.SimulateCommandInput(cmd + "\n");
            await _shell!.FlushOutputAsync();

            // Assert
            var output = _outputBuffer.ToString();
            Assert.That(output, Does.Contain("\x1b[3J"), $"Command '{cmd}' should clear screen");
        }
    }

    [Test]
    public async Task ClearCommand_WithWhitespace_StillWorks()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        _outputBuffer.Clear();

        // Act - Test with leading/trailing whitespace
        _shell.SimulateCommandInput("  clear  \n");
        await _shell!.FlushOutputAsync();

        // Assert
        var output = _outputBuffer.ToString();
        Assert.That(output, Does.Contain("\x1b[3J"));
    }

    [Test]
    public async Task EmptyCommand_DoesNotExecute()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        _shell.SendInitialOutput();
        await _shell!.FlushOutputAsync();
        _outputBuffer.Clear();

        // Act - Send just Enter
        _shell.SimulateCommandInput("\n");
        await _shell!.FlushOutputAsync();

        // Assert - Should only see the prompt, no execution
        var output = _outputBuffer.ToString();
        Assert.That(output, Does.Not.Contain("Error"));
        Assert.That(output, Does.Contain("ksa> ")); // Prompt should be sent
        Assert.That(_shell.LastExecutedCommand, Is.Null); // No command executed
    }

    [Test]
    public async Task WhitespaceOnlyCommand_DoesNotExecute()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        _shell.SendInitialOutput();
        await _shell!.FlushOutputAsync();
        _outputBuffer.Clear();

        // Act - Send whitespace followed by Enter
        _shell.SimulateCommandInput("   \n");
        await _shell!.FlushOutputAsync();

        // Assert
        var output = _outputBuffer.ToString();
        Assert.That(output, Does.Not.Contain("Error"));
        Assert.That(_shell.LastExecutedCommand, Is.Null); // No command executed
    }

    #endregion

    #region Command History Tests

    [Test]
    public async Task CommandExecution_AddsToHistory()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);

        // Act
        _shell.SimulateCommandInput("test-command\n");
        await _shell!.FlushOutputAsync();

        // Assert
        Assert.That(_shell.GetCommandHistory(), Does.Contain("test-command"));
    }

    [Test]
    public async Task UpArrow_NavigatesToPreviousCommand()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        _shell.SimulateCommandInput("first-command\n");
        _shell.SimulateCommandInput("second-command\n");
        await _shell!.FlushOutputAsync();

        // Act - Press Up arrow (ESC[A)
        _shell.SimulateInput("\x1b[A");
        await _shell!.FlushOutputAsync();

        // Assert - Current line should be "second-command"
        Assert.That(_shell.GetCurrentLine(), Is.EqualTo("second-command"));
    }

    [Test]
    public async Task DownArrow_NavigatesToNextCommand()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        _shell.SimulateCommandInput("first-command\n");
        _shell.SimulateCommandInput("second-command\n");
        await _shell!.FlushOutputAsync();

        // Act - Press Up twice, then Down once
        _shell.SimulateInput("\x1b[A"); // Navigate to second-command
        await _shell!.FlushOutputAsync();
        _shell.SimulateInput("\x1b[A"); // Navigate to first-command
        await _shell!.FlushOutputAsync();
        _shell.SimulateInput("\x1b[B"); // Navigate forward to second-command
        await _shell!.FlushOutputAsync();

        // Assert
        Assert.That(_shell.GetCurrentLine(), Is.EqualTo("second-command"));
    }

    [Test]
    public async Task CommandHistory_DoesNotDuplicateConsecutiveCommands()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);

        // Act - Execute same command twice
        _shell.SimulateCommandInput("duplicate\n");
        await _shell!.FlushOutputAsync();
        _shell.SimulateCommandInput("duplicate\n");
        await _shell!.FlushOutputAsync();

        // Assert - History should only have one entry
        var history = _shell.GetCommandHistory();
        var duplicateCount = history.Count(c => c == "duplicate");
        Assert.That(duplicateCount, Is.EqualTo(1));
    }

    #endregion

    #region Input Processing Tests

    [Test]
    public async Task PrintableCharacters_AreEchoed()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        _outputBuffer.Clear();

        // Act
        _shell.SimulateInput("test");
        await _shell!.FlushOutputAsync();

        // Assert - Characters should be echoed
        var output = _outputBuffer.ToString();
        Assert.That(output, Does.Contain("test"));
    }

    [Test]
    public async Task Backspace_RemovesCharacters()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);

        // Act - Type "test" then backspace twice
        _shell.SimulateInput("test");
        await _shell!.FlushOutputAsync();
        _shell.SimulateInput("\x7F\x7F"); // DEL backspace
        await _shell!.FlushOutputAsync();

        // Assert - Current line should be "te"
        Assert.That(_shell.GetCurrentLine(), Is.EqualTo("te"));
    }

    [Test]
    public async Task BackspaceAlt_RemovesCharacters()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);

        // Act - Type "test" then Ctrl+H (0x08) deletes previous word
        _shell.SimulateInput("test");
        await _shell!.FlushOutputAsync();
        _shell.SimulateInput("\x08\x08"); // Ctrl+H delete word
        await _shell!.FlushOutputAsync();

        // Assert
        Assert.That(_shell.GetCurrentLine(), Is.Empty);
    }

    [Test]
    public async Task Enter_ExecutesCommand()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);

        // Act
        _shell.SimulateInput("test-cmd");
        await _shell!.FlushOutputAsync();
        _shell.SimulateInput("\r"); // Enter
        await _shell!.FlushOutputAsync();

        // Assert
        Assert.That(_shell.LastExecutedCommand, Is.EqualTo("test-cmd"));
    }

    [Test]
    public async Task CtrlL_ClearsScreen()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        _outputBuffer.Clear();

        // Act - Send Ctrl+L
        _shell.SimulateInput("\x0C");
        await _shell!.FlushOutputAsync();

        // Assert - Should send clear sequence (ESC[2J ESC[H)
        var output = _outputBuffer.ToString();
        Assert.That(output, Does.Contain("\x1b[2J"));
        Assert.That(output, Does.Contain("\x1b[H"));
    }

    #endregion

    #region Output Capture Tests

    // These exercise the REAL capture entry point the Harmony patch calls —
    // OnConsolePrint(string, ImColor8) — against the real ConsoleWindow color
    // fields (plain constant statics in Brutal.ImGui.Abstractions, which the
    // test csproj copies beside the tests). This is the one piece of logic
    // CLAUDE.md flags as Brutal-version-sensitive, so it must have live
    // coverage: a Brutal bump that changes the sink signature or the color
    // fields should fail here, not in-game.

    [Test]
    public async Task OnConsolePrint_ErrorColor_EmitsStderr()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        _shell.SetActiveForConsoleOutput(true);
        _shell.LastOutputType = ShellOutputType.Stdout;

        // Act
        GameConsoleShell.OnConsolePrint("boom", ConsoleWindow.ErrorColor);
        await _shell!.FlushOutputAsync();

        // Assert
        Assert.That(_shell.LastOutputType, Is.EqualTo(ShellOutputType.Stderr));
        Assert.That(_outputBuffer.ToString(), Does.Contain("boom"));
    }

    [Test]
    public async Task OnConsolePrint_NormalColor_EmitsStdout()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        _shell.SetActiveForConsoleOutput(true);
        _shell.LastOutputType = ShellOutputType.Stderr;

        // Act
        GameConsoleShell.OnConsolePrint("ok", ConsoleWindow.InfoColor);
        await _shell!.FlushOutputAsync();

        // Assert
        Assert.That(_shell.LastOutputType, Is.EqualTo(ShellOutputType.Stdout));
        Assert.That(_outputBuffer.ToString(), Does.Contain("ok"));
    }

    [Test]
    public async Task OnConsolePrint_WhileNotCapturing_IsIgnored()
    {
        // Arrange — no active executing instance
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        _outputBuffer.Clear();

        // Act
        GameConsoleShell.OnConsolePrint("unrelated", ConsoleWindow.InfoColor);
        await _shell!.FlushOutputAsync();

        // Assert — output printed outside a command's execution is not ours
        Assert.That(_outputBuffer.ToString(), Does.Not.Contain("unrelated"));
    }

    [Test]
    public async Task RequestCancellation_SendsCancelMessage()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        _outputBuffer.Clear();

        // Act
        _shell.RequestCancellation();
        await _shell!.FlushOutputAsync();

        // Assert
        var output = _outputBuffer.ToString();
        Assert.That(output, Does.Contain("^C"));
        Assert.That(output, Does.Contain("\x1b[33m")); // Yellow color for ^C
    }

    #endregion


    #region Stop/Termination Tests

    [Test]
    public async Task StopAsync_SendsGoodbyeMessage()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        _outputBuffer.Clear();

        // Act - StopAsync drains the output pump before returning, so the goodbye
        // message is fully delivered when it completes (no wait needed)
        await _shell.StopAsync();

        // Assert
        var output = _outputBuffer.ToString();
        Assert.That(output, Does.Contain("Game Console shell terminated"));
        Assert.That(output, Does.Contain("\x1b[1;33m")); // Bold yellow
    }

    [Test]
    public async Task StopAsync_RaisesTerminatedEvent()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);

        bool terminatedEventRaised = false;
        int? exitCode = null;
        string? reason = null;

        _shell.Terminated += (sender, args) =>
        {
            terminatedEventRaised = true;
            exitCode = args.ExitCode;
            reason = args.Reason;
        };

        // Act - Terminated is raised synchronously inside StopAsync (OnStoppingAsync),
        // so it has fired by the time StopAsync returns
        await _shell.StopAsync();

        // Assert
        Assert.That(terminatedEventRaised, Is.True);
        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(reason, Is.EqualTo("User requested shutdown"));
    }

    #endregion

    /// <summary>
    /// Test implementation of GameConsoleShell that bypasses KSA dependencies
    /// </summary>
    private class TestGameConsoleShell : GameConsoleShell
    {
        private readonly StringBuilder _testOutputBuffer;
        public string? LastExecutedCommand { get; private set; }

        public TestGameConsoleShell(StringBuilder outputBuffer)
        {
            _testOutputBuffer = outputBuffer;

            // Subscribe to output events to capture output for assertions.
            // Zero-length events are FlushOutputAsync sentinels and carry no shell output;
            // skipping them keeps LastOutputType reflecting real output only.
            OutputReceived += (sender, args) =>
            {
                if (args.Data.Length == 0)
                {
                    return;
                }

                var text = Encoding.UTF8.GetString(args.Data.ToArray());
                _testOutputBuffer.Append(text);
                LastOutputType = args.OutputType;
            };
        }

        /// <summary>
        ///     Deterministically waits until all output queued so far has been raised via
        ///     OutputReceived (input processing is synchronous; only output delivery is async).
        ///     Queues an empty sentinel through the same FIFO output channel and waits for it
        ///     to come out the event side. Must not be called after StopAsync (the channel is
        ///     completed then — StopAsync already drains the pump before returning).
        /// </summary>
        public async Task FlushOutputAsync()
        {
            var sentinel = new byte[0];
            ReadOnlyMemory<byte> sentinelMemory = sentinel;
            var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object? sender, ShellOutputEventArgs args)
            {
                if (args.Data.Equals(sentinelMemory))
                {
                    delivered.TrySetResult();
                }
            }

            OutputReceived += Handler;
            try
            {
                QueueOutput(sentinel, ShellOutputType.Stdout);
                await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                OutputReceived -= Handler;
            }
        }

        public ShellOutputType LastOutputType { get; set; } = ShellOutputType.Stdout;

        public void SetActiveForConsoleOutput(bool isExecuting)
        {
            var activeInstanceField = typeof(GameConsoleShell)
                .GetField("_activeInstance", BindingFlags.NonPublic | BindingFlags.Static);
            var activeLockField = typeof(GameConsoleShell)
                .GetField("_activeLock", BindingFlags.NonPublic | BindingFlags.Static);
            var isExecutingField = typeof(GameConsoleShell)
                .GetField("_isExecutingCommand", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(activeInstanceField, Is.Not.Null);
            Assert.That(activeLockField, Is.Not.Null);
            Assert.That(isExecutingField, Is.Not.Null);

            var activeLock = activeLockField!.GetValue(null)!;
            lock (activeLock)
            {
                activeInstanceField!.SetValue(null, isExecuting ? this : null);
                isExecutingField!.SetValue(this, isExecuting);
            }
        }

        protected override Task OnStartingAsync(CustomShellStartOptions options, CancellationToken cancellationToken)
        {
            // Skip the TerminalInterface availability check for testing, but
            // keep the real prompt-from-environment behavior under test.
            ApplyPromptFromOptions(options);
            return Task.CompletedTask;
        }

        // Override ExecuteCommandLine to avoid KSA dependency
        protected override void ExecuteCommandLine(string commandLine)
        {
            // Track what was executed
            LastExecutedCommand = commandLine;

            // Call base to handle built-in commands
            if (TryHandleBuiltinCommandPublic(commandLine))
            {
                SendPrompt();
                return;
            }

            // Simulate successful execution without calling KSA
            SendOutput($"[TEST] Executed: {commandLine}\r\n");
            SendPrompt();
        }

        private bool TryHandleBuiltinCommandPublic(string command)
        {
            // Use reflection to call private method
            var method = typeof(GameConsoleShell).GetMethod("TryHandleBuiltinCommand",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return method != null && (bool)method.Invoke(this, new object[] { command })!;
        }

        // Helper method to simulate user input (WriteInputAsync completes
        // synchronously for line-buffered shells, so this never blocks).
        public void SimulateInput(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            WriteInputAsync(bytes).GetAwaiter().GetResult();
        }

        // Helper method to simulate complete command input (chars + enter)
        public void SimulateCommandInput(string commandLine)
        {
            SimulateInput(commandLine);
        }

        // Expose current line for testing
        public string GetCurrentLine()
        {
            return CurrentLine;
        }

        // Expose command history for testing
        public IReadOnlyList<string> GetCommandHistory()
        {
            return CommandHistory;
        }
    }
}
