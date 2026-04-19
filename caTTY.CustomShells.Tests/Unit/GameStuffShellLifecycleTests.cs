using System.Text;
using caTTY.Core.Terminal;
using NUnit.Framework;
using GameStuffShellType = caTTY.CustomShells.GameStuffShell.GameStuffShell;

namespace caTTY.CustomShells.Tests.Unit;

[TestFixture]
public class GameStuffShellLifecycleTests
{
    private GameStuffShellType? _shell;
    private readonly List<ShellOutputEventArgs> _outputEvents = new();

    [SetUp]
    public async Task Setup()
    {
        _shell = new GameStuffShellType();
        _shell.OutputReceived += (sender, args) => _outputEvents.Add(args);

        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell.StartAsync(options);
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
        _outputEvents.Clear();
    }

    [Test]
    public void Metadata_UsesExpectedDefaults()
    {
        var metadata = _shell!.Metadata;

        Assert.That(metadata.Name, Is.EqualTo("Game Stuff"));
        Assert.That(metadata.Description, Is.EqualTo("Game Stuff shell - bash-like command interpreter"));
        Assert.That(metadata.Version, Is.EqualTo(new Version(1, 0, 0)));
        Assert.That(metadata.Author, Is.EqualTo("caTTY"));
        Assert.That(metadata.SupportedFeatures, Does.Contain("line-editing"));
        Assert.That(metadata.SupportedFeatures, Does.Contain("history"));
        Assert.That(metadata.SupportedFeatures, Does.Contain("clear-screen"));
    }

    [Test]
    public async Task SendInitialOutput_IncludesBannerAndPrompt()
    {
        _shell!.SendInitialOutput();
        await Task.Delay(100);

        var output = CombineOutputText();
        Assert.That(output, Does.Contain("Game Stuff Shell v1.0.0"));
        Assert.That(output, Does.Contain("Press Ctrl+L to clear screen"));
        Assert.That(output, Does.Contain("gstuff> "));
    }

    [Test]
    public async Task StartStop_TogglesIsRunning()
    {
        Assert.That(_shell!.IsRunning, Is.True);

        await _shell.StopAsync();

        Assert.That(_shell.IsRunning, Is.False);
    }

    [Test]
    public async Task ExecuteCommandLine_PrintsNotImplementedToStderr()
    {
        await _shell!.WriteInputAsync(Encoding.UTF8.GetBytes("status\r"));
        await Task.Delay(150);

        Assert.That(_outputEvents.Any(evt => evt.OutputType == ShellOutputType.Stderr), Is.True);
    }

    private string CombineOutputText()
    {
        var builder = new StringBuilder();
        foreach (var output in _outputEvents)
        {
            builder.Append(Encoding.UTF8.GetString(output.Data.ToArray()));
        }
        return builder.ToString();
    }
}
