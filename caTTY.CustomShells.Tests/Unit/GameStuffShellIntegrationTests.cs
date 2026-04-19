using caTTY.Core.Terminal;
using caTTY.CustomShells.GameStuffShell;
using NUnit.Framework;
using System.Text;

namespace caTTY.CustomShells.Tests.Unit;

[TestFixture]
public class GameStuffShellIntegrationTests
{
    [Test]
    public async Task ExecuteCommandLine_UnknownCommand_ReturnsCommandNotFound()
    {
        var shell = new GameStuffShell.GameStuffShell();
        var outputs = new List<(string text, ShellOutputType type)>();

        shell.OutputReceived += (sender, args) =>
        {
            var text = Encoding.UTF8.GetString(args.Data.Span);
            outputs.Add((text, args.OutputType));
        };

        await shell.StartAsync(new CustomShellStartOptions { InitialWidth = 80, InitialHeight = 24 });

        // Wait for initial output (banner + prompt)
        await Task.Delay(100);
        outputs.Clear();

        // Execute command
        var input = Encoding.UTF8.GetBytes("unknown_command\r");
        await shell.WriteInputAsync(input);

        // Wait for execution
        await Task.Delay(100);

        // Should see "command not found" error and prompt
        var allOutput = string.Join("", outputs.Select(o => o.text));
        Assert.That(allOutput, Does.Contain("command not found"));
        Assert.That(allOutput, Does.Contain("gstuff>"));

        await shell.StopAsync();
        shell.Dispose();
    }

    [Test]
    public async Task ExecuteCommandLine_EmptyCommand_ReturnsPrompt()
    {
        var shell = new GameStuffShell.GameStuffShell();
        var outputs = new List<(string text, ShellOutputType type)>();

        shell.OutputReceived += (sender, args) =>
        {
            var text = Encoding.UTF8.GetString(args.Data.Span);
            outputs.Add((text, args.OutputType));
        };

        await shell.StartAsync(new CustomShellStartOptions { InitialWidth = 80, InitialHeight = 24 });

        // Wait for initial output
        await Task.Delay(100);
        outputs.Clear();

        // Execute empty command
        var input = Encoding.UTF8.GetBytes("\r");
        await shell.WriteInputAsync(input);

        // Wait for execution
        await Task.Delay(100);

        // Should only see prompt
        var allOutput = string.Join("", outputs.Select(o => o.text));
        Assert.That(allOutput, Does.Contain("gstuff>"));
        Assert.That(allOutput, Does.Not.Contain("error"));

        await shell.StopAsync();
        shell.Dispose();
    }

    [Test]
    public async Task ExecuteCommandLine_ParseError_ReturnsErrorMessage()
    {
        var shell = new GameStuffShell.GameStuffShell();
        var outputs = new List<(string text, ShellOutputType type)>();

        shell.OutputReceived += (sender, args) =>
        {
            var text = Encoding.UTF8.GetString(args.Data.Span);
            outputs.Add((text, args.OutputType));
        };

        await shell.StartAsync(new CustomShellStartOptions { InitialWidth = 80, InitialHeight = 24 });

        // Wait for initial output
        await Task.Delay(100);
        outputs.Clear();

        // Execute command with parse error (pipe without right side)
        var input = Encoding.UTF8.GetBytes("echo hello |\r");
        await shell.WriteInputAsync(input);

        // Wait for execution
        await Task.Delay(100);

        // Should see parse error and prompt
        var allOutput = string.Join("", outputs.Select(o => o.text));
        Assert.That(allOutput, Does.Contain("Parse error").Or.Contain("Parser error"));
        Assert.That(allOutput, Does.Contain("gstuff>"));

        await shell.StopAsync();
        shell.Dispose();
    }

    [Test]
    public async Task ExecuteCommandLine_ListCommands_ExecutesInSequence()
    {
        var shell = new GameStuffShell.GameStuffShell();
        var outputs = new List<(string text, ShellOutputType type)>();

        shell.OutputReceived += (sender, args) =>
        {
            var text = Encoding.UTF8.GetString(args.Data.Span);
            outputs.Add((text, args.OutputType));
        };

        await shell.StartAsync(new CustomShellStartOptions { InitialWidth = 80, InitialHeight = 24 });

        // Wait for initial output
        await Task.Delay(100);
        outputs.Clear();

        // Execute multiple commands in sequence
        var input = Encoding.UTF8.GetBytes("cmd1 ; cmd2 ; cmd3\r");
        await shell.WriteInputAsync(input);

        // Wait for execution
        await Task.Delay(100);

        // Should see all three commands fail with "command not found"
        var allOutput = string.Join("", outputs.Select(o => o.text));
        var errorCount = CountOccurrences(allOutput, "command not found");
        Assert.That(errorCount, Is.EqualTo(3), "Expected 3 'command not found' errors");

        await shell.StopAsync();
        shell.Dispose();
    }

    [Test]
    public async Task SendInitialOutput_DisplaysBanner()
    {
        var shell = new GameStuffShell.GameStuffShell();
        var outputs = new List<string>();

        shell.OutputReceived += (sender, args) =>
        {
            var text = Encoding.UTF8.GetString(args.Data.Span);
            outputs.Add(text);
        };

        await shell.StartAsync(new CustomShellStartOptions { InitialWidth = 80, InitialHeight = 24 });
        shell.SendInitialOutput();

        // Wait for output
        await Task.Delay(100);

        var allOutput = string.Join("", outputs);
        Assert.That(allOutput, Does.Contain("Game Stuff Shell"));
        Assert.That(allOutput, Does.Contain("gstuff>"));

        await shell.StopAsync();
        shell.Dispose();
    }

    private static int CountOccurrences(string text, string substring)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }
}
