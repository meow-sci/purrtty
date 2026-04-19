using System.Reflection;
using System.Text;
using caTTY.Core.Terminal;
using caTTY.CustomShells;
using caTTY.Display.Configuration;
using NUnit.Framework;

namespace caTTY.CustomShells.Tests.Unit;

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

        // Clean up any test configuration files
        ThemeConfiguration.OverrideConfigDirectory = Path.Combine(Path.GetTempPath(), $"catty-test-{Guid.NewGuid()}");
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

        // Clean up test configuration directory
        if (!string.IsNullOrEmpty(ThemeConfiguration.OverrideConfigDirectory)
            && Directory.Exists(ThemeConfiguration.OverrideConfigDirectory))
        {
            try
            {
                Directory.Delete(ThemeConfiguration.OverrideConfigDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        ThemeConfiguration.OverrideConfigDirectory = null;
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
        Assert.That(metadata.Author, Is.EqualTo("caTTY"));
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
        await Task.Delay(100); // Give output pump time to process

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
    public async Task GetPrompt_ReturnsDefaultPromptWhenNoConfig()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);

        // Act
        _shell.SendInitialOutput();
        await Task.Delay(100);

        // Assert
        var output = _outputBuffer.ToString();
        Assert.That(output, Does.Contain("ksa> "));
    }

    [Test]
    public async Task GetPrompt_ReturnsCustomPromptFromConfiguration()
    {
        // Arrange
        var config = new ThemeConfiguration
        {
            GameShellPrompt = "custom-prompt> "
        };
        config.Save();

        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);

        // Act
        _shell.SendInitialOutput();
        await Task.Delay(100);

        // Assert
        var output = _outputBuffer.ToString();
        Assert.That(output, Does.Contain("custom-prompt> "));
    }

    [Test]
    public async Task PromptConfiguration_LoadedOnStart()
    {
        // Arrange - Save custom prompt before starting shell
        var config = new ThemeConfiguration
        {
            GameShellPrompt = "loaded> "
        };
        config.Save();

        // Act
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        _shell.SendInitialOutput();
        await Task.Delay(100);

        // Assert
        var output = _outputBuffer.ToString();
        Assert.That(output, Does.Contain("loaded> "));
    }

    [Test]
    public async Task PromptConfiguration_FallsBackToDefaultOnLoadError()
    {
        // Arrange - Use invalid directory to force load error
        var invalidPath = Path.Combine(Path.GetTempPath(), "invalid-\0-path");
        ThemeConfiguration.OverrideConfigDirectory = invalidPath;

        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);

        // Act
        await _shell!.StartAsync(options);
        _shell.SendInitialOutput();
        await Task.Delay(100);

        // Assert - Should fall back to default "ksa> "
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
        await Task.Delay(100);

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
            await Task.Delay(100);

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
        await Task.Delay(100);

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
        await Task.Delay(100);
        _outputBuffer.Clear();

        // Act - Send just Enter
        _shell.SimulateCommandInput("\n");
        await Task.Delay(100);

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
        await Task.Delay(100);
        _outputBuffer.Clear();

        // Act - Send whitespace followed by Enter
        _shell.SimulateCommandInput("   \n");
        await Task.Delay(100);

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
        await Task.Delay(100);

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
        await Task.Delay(100);

        // Act - Press Up arrow (ESC[A)
        _shell.SimulateInput("\x1b[A");
        await Task.Delay(100);

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
        await Task.Delay(100);

        // Act - Press Up twice, then Down once
        _shell.SimulateInput("\x1b[A"); // Navigate to second-command
        await Task.Delay(50);
        _shell.SimulateInput("\x1b[A"); // Navigate to first-command
        await Task.Delay(50);
        _shell.SimulateInput("\x1b[B"); // Navigate forward to second-command
        await Task.Delay(50);

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
        await Task.Delay(50);
        _shell.SimulateCommandInput("duplicate\n");
        await Task.Delay(50);

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
        await Task.Delay(100);

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
        await Task.Delay(50);
        _shell.SimulateInput("\x7F\x7F"); // DEL backspace
        await Task.Delay(50);

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
        await Task.Delay(50);
        _shell.SimulateInput("\x08\x08"); // Ctrl+H delete word
        await Task.Delay(50);

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
        await Task.Delay(50);
        _shell.SimulateInput("\r"); // Enter
        await Task.Delay(100);

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
        await Task.Delay(100);

        // Assert - Should send clear sequence (ESC[2J ESC[H)
        var output = _outputBuffer.ToString();
        Assert.That(output, Does.Contain("\x1b[2J"));
        Assert.That(output, Does.Contain("\x1b[H"));
    }

    #endregion

    #region Output Capture Tests

    private static void InvokeOnConsolePrint(string output, uint color, object lineTypeValue)
    {
        var method = typeof(GameConsoleShell).GetMethod(nameof(GameConsoleShell.OnConsolePrint),
            BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        method!.Invoke(null, new[] { output, color, lineTypeValue });
    }

    private static uint GetConsoleWindowColor(string fieldName)
    {
        var consoleWindowType = typeof(GameConsoleShell).Assembly.GetType("KSA.ConsoleWindow");
        if (consoleWindowType == null)
        {
            Assert.Ignore("KSA ConsoleWindow type not available for unit tests.");
        }

        var field = consoleWindowType!.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        if (field == null)
        {
            Assert.Ignore($"ConsoleWindow.{fieldName} not available for unit tests.");
        }

        var value = field!.GetValue(null);
        if (value == null)
        {
            Assert.Ignore($"ConsoleWindow.{fieldName} value unavailable for unit tests.");
        }

        return (uint)value!;
    }

    private static object CreateConsoleLineTypeValue()
    {
        var consoleLineType = typeof(GameConsoleShell).Assembly.GetType("Brutal.ImGuiApi.Abstractions.ConsoleLineType");
        if (consoleLineType == null)
        {
            Assert.Ignore("ConsoleLineType enum not available for unit tests.");
        }

        var value = Enum.GetValues(consoleLineType!).GetValue(0);
        if (value == null)
        {
            Assert.Ignore("ConsoleLineType enum value not available for unit tests.");
        }

        return value!;
    }

    [Test]
    public async Task OnConsolePrint_ErrorColor_EmitsStderr()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        _shell.SetActiveForConsoleOutput(true);
        _shell.LastOutputType = ShellOutputType.Stdout;

        var errorColor = GetConsoleWindowColor("ErrorColor");
        var lineTypeValue = CreateConsoleLineTypeValue();

        // Act
        InvokeOnConsolePrint("boom", errorColor, lineTypeValue);
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.LastOutputType, Is.EqualTo(ShellOutputType.Stderr));
    }

    [Test]
    public async Task OnConsolePrint_NormalColor_EmitsStdout()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        _shell.SetActiveForConsoleOutput(true);
        _shell.LastOutputType = ShellOutputType.Stderr;

        var infoColor = GetConsoleWindowColor("InfoColor");
        var lineTypeValue = CreateConsoleLineTypeValue();

        // Act
        InvokeOnConsolePrint("ok", infoColor, lineTypeValue);
        await Task.Delay(100);

        // Assert
        Assert.That(_shell.LastOutputType, Is.EqualTo(ShellOutputType.Stdout));
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
        await Task.Delay(100);

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

        // Act
        await _shell.StopAsync();
        await Task.Delay(100);

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

        // Act
        await _shell.StopAsync();
        await Task.Delay(100);

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

            // Subscribe to output events to capture output for assertions
            OutputReceived += (sender, args) =>
            {
                var text = Encoding.UTF8.GetString(args.Data.ToArray());
                _testOutputBuffer.Append(text);
                LastOutputType = args.OutputType;
            };
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

        protected override async Task OnStartingAsync(CustomShellStartOptions options, CancellationToken cancellationToken)
        {
            // Skip the TerminalInterface check for testing
            // Load prompt from configuration
            LoadPromptFromConfigurationPublic();
            await Task.CompletedTask;
        }

        // Expose protected method for testing
        public void LoadPromptFromConfigurationPublic()
        {
            // Use reflection to call private method
            var method = typeof(GameConsoleShell).GetMethod("LoadPromptFromConfiguration",
                BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(this, null);
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

        // Helper method to simulate user input
        public void SimulateInput(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            WriteInputAsync(bytes).Wait();
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
