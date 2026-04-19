using caTTY.CustomShells.GameStuffShell.Execution;
using caTTY.CustomShells.GameStuffShell.Programs;
using NUnit.Framework;

namespace caTTY.CustomShells.Tests.Unit;

[TestFixture]
public class GameStuffShellProgramsGameApiTests
{
    [Test]
    public async Task CraftsProgram_WithCrafts_PrintsOnePerLine()
    {
        var gameApi = new FakeGameApi(new[] { "Rocket Alpha", "Shuttle Beta", "Probe Gamma" });
        var program = new CraftsProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(new[] { "crafts" }, gameApi);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("Rocket Alpha\nShuttle Beta\nProbe Gamma\n"));
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task CraftsProgram_NoCrafts_PrintsNothing()
    {
        var gameApi = new FakeGameApi(Array.Empty<string>());
        var program = new CraftsProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(new[] { "crafts" }, gameApi);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.Empty);
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task CraftsProgram_NoGameApi_ReturnsError()
    {
        var program = new CraftsProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(new[] { "crafts" }, gameApi: null);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(stdoutWriter.GetContent(), Is.Empty);
        Assert.That(stderrWriter.GetContent(), Does.Contain("game API not available"));
    }

    [Test]
    public async Task FollowProgram_ValidCraft_Succeeds()
    {
        var gameApi = new FakeGameApi(new[] { "Rocket Alpha", "Shuttle Beta" });
        var program = new FollowProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(new[] { "follow", "Rocket Alpha" }, gameApi);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.Empty);
        Assert.That(stderrWriter.GetContent(), Is.Empty);
        Assert.That(gameApi.LastFollowedCraft, Is.EqualTo("Rocket Alpha"));
    }

    [Test]
    public async Task FollowProgram_InvalidCraft_ReturnsErrorWithMessage()
    {
        var gameApi = new FakeGameApi(new[] { "Rocket Alpha" });
        var program = new FollowProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(new[] { "follow", "Unknown Craft" }, gameApi);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(stdoutWriter.GetContent(), Is.Empty);
        Assert.That(stderrWriter.GetContent(), Does.Contain("craft not found"));
    }

    [Test]
    public async Task FollowProgram_NoArgs_ReturnsUsageError()
    {
        var gameApi = new FakeGameApi(new[] { "Rocket Alpha" });
        var program = new FollowProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(new[] { "follow" }, gameApi);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(stdoutWriter.GetContent(), Is.Empty);
        Assert.That(stderrWriter.GetContent(), Does.Contain("missing craft name"));
    }

    [Test]
    public async Task FollowProgram_NoGameApi_ReturnsError()
    {
        var program = new FollowProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(new[] { "follow", "SomeCraft" }, gameApi: null);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(stdoutWriter.GetContent(), Is.Empty);
        Assert.That(stderrWriter.GetContent(), Does.Contain("game API not available"));
    }

    private static (ProgramContext, BufferedStreamWriter, BufferedStreamWriter) CreateTestContext(
        string[] argv,
        IGameStuffApi? gameApi)
    {
        var stdoutWriter = new BufferedStreamWriter();
        var stderrWriter = new BufferedStreamWriter();
        var streams = new StreamSet(
            EmptyStreamReader.Instance,
            stdoutWriter,
            stderrWriter);

        var context = new ProgramContext(
            argv: argv,
            streams: streams,
            programResolver: new ProgramRegistry(),
            gameApi: gameApi,
            environment: new Dictionary<string, string>(),
            terminalWidth: 80,
            terminalHeight: 24);

        return (context, stdoutWriter, stderrWriter);
    }

    /// <summary>
    /// Fake implementation of IGameStuffApi for testing.
    /// </summary>
    private sealed class FakeGameApi : IGameStuffApi
    {
        private readonly IReadOnlyList<string> _craftNames;

        public string? LastFollowedCraft { get; private set; }

        public FakeGameApi(IReadOnlyList<string> craftNames)
        {
            _craftNames = craftNames;
        }

        public IReadOnlyList<string> GetCraftNames()
        {
            return _craftNames;
        }

        public bool TryFollowCraft(string craftName, out string? error)
        {
            if (_craftNames.Contains(craftName))
            {
                LastFollowedCraft = craftName;
                error = null;
                return true;
            }
            else
            {
                error = $"craft not found: {craftName}";
                return false;
            }
        }
    }
}
