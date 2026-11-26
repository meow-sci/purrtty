using System.Text.Json.Nodes;
using KSA.Kfs.Host;
using NUnit.Framework;
using purrTTY.Kfs.Lsp;
using purrTTY.Kfs.Lsp.Documents;
using purrTTY.Kfs.Lsp.Features;
using purrTTY.Kfs.Testing;
using purrTTY.Kfs.Tools.Tests.Lsp;

namespace purrTTY.Kfs.Tools.Tests.Runtime.Lsp;

/// <summary>
/// The <c>workspace/executeCommand</c> contract of task C4 (<see cref="HostFeatures"/>,
/// <see cref="KfsCommandProvider"/>): each command reaches the host as the exact stock verb line;
/// refusals are <c>ok: false</c> results, never JSON-RPC errors, and run nothing.
/// </summary>
[TestFixture]
public sealed class HostCommandTests : ToolsRuntimeFixture
{
    /// <summary>A host that records the verb lines and can act on a vehicle.</summary>
    private sealed class RecordingHost(KfsLanguageHost inner) : KfsLanguageHost(inner.Tables, inner.WorkspaceRoot, inner.CurrentTarget)
    {
        public List<string> Verbs { get; } = [];

        public override bool CanControlVehicle => true;

        public override Task<CommandResult> RunVerb(string verb, CancellationToken ct)
        {
            lock (Verbs) Verbs.Add(verb);
            if (verb == "explode") throw new InvalidOperationException("the host broke");
            return Task.FromResult(new CommandResult(true, "ran kfs " + verb + "\n"));
        }
    }

    private static RecordingHost NewHost(TempWorkspace ws) => new(KfsLanguageHost.ForBench(Runtime.Tables, ws.Root));

    private static void TargetVehicle(KfsLanguageHost host) =>
        host.SetTarget(new BuildTarget(Runtime.Tables.BenchPart, LayoutGenerator.Vehicle("v", 2, 0, 0), "vehicle 7") { IsVehicle = true });

    private static JsonObject Execute(ServerHarness h, string command, JsonArray? arguments = null)
    {
        var p = new JsonObject { ["command"] = command };
        if (arguments is not null) p["arguments"] = arguments;
        JsonObject response = h.Client.Request("workspace/executeCommand", p);
        Assert.That(response["error"], Is.Null, $"{command}: a refusal is a result, not a JSON-RPC error: {response.ToJsonString()}");
        return (JsonObject)response["result"]!;
    }

    private static string Uri(TempWorkspace ws, string path) => WorkspaceUris.FileUri(ws.Full(path), OperatingSystem.IsWindows());

    private static void AssertOk(JsonObject result, string output)
    {
        Assert.That(result["ok"]!.GetValue<bool>(), Is.True, result.ToJsonString());
        Assert.That(result["output"]!.GetValue<string>(), Is.EqualTo(output));
        Assert.That(result.ContainsKey("error") && result["error"] is null, Is.True, "error is present and null");
    }

    private static string Refused(JsonObject result)
    {
        Assert.That(result["ok"]!.GetValue<bool>(), Is.False, result.ToJsonString());
        Assert.That(result["output"]!.GetValue<string>(), Is.Empty);
        return result["error"]!.GetValue<string>();
    }

    [Test]
    public void Commands_Reach_The_Host()
    {
        using var ws = new TempWorkspace();
        ws.Write("x_blink/blink.c", "void f(void) { }\n").Write("my prog/main.c", "\n").Write("ünï code/ä.c", "\n");
        RecordingHost host = NewHost(ws);
        var purr = new List<string>();
        var options = new HostFeatureOptions
        {
            RunPurr = (line, _) =>
            {
                lock (purr) purr.Add(line);
                return Task.FromResult(new CommandResult(true, "ran purr " + line + "\n"));
            },
        };
        using var h = ServerHarness.Start(host, configure: s => HostFeatures.AddTo(s, options));
        JsonObject caps = h.Client.Initialize()["capabilities"]!.AsObject();
        Assert.That(caps["executeCommandProvider"]!["commands"]!.AsArray().Select(n => n!.GetValue<string>()),
            Is.EqualTo(KfsCommands.All.Order(StringComparer.Ordinal)));
        Assert.That(caps["experimental"]!["purrtty"]!.ToJsonString(), Is.EqualTo("""{"subscribe":false,"vehicleCommands":true,"verb":false}"""),
            "no live feed: subscribe false (nothing would ever be sent)");

        string blink = Uri(ws, "x_blink/blink.c");
        AssertOk(Execute(h, KfsCommands.Build, [blink]), "ran kfs build x_blink/blink.c\n");
        Assert.That(Uri(ws, "my prog/main.c"), Does.Contain("my%20prog"), "the URI is percent-encoded");
        AssertOk(Execute(h, KfsCommands.Build, [Uri(ws, "my prog/main.c")]), "ran kfs build my prog/main.c\n");
        string lower = Uri(ws, "ünï code/ä.c").Replace("%C3%BC", "%c3%bc", StringComparison.Ordinal);
        AssertOk(Execute(h, KfsCommands.Build, [lower]), "ran kfs build ünï code/ä.c\n");
        AssertOk(Execute(h, KfsCommands.Load, [blink]), "ran kfs load x_blink/blink.c\n");
        AssertOk(Execute(h, KfsCommands.Start), "ran kfs start\n");
        AssertOk(Execute(h, KfsCommands.Stop, []), "ran kfs stop\n");
        AssertOk(Execute(h, KfsCommands.Reset, []), "ran kfs reset\n");
        AssertOk(Execute(h, KfsCommands.Target, ["vehicle"]), "ran kfs target vehicle\n");
        AssertOk(Execute(h, KfsCommands.Target, ["bench"]), "ran kfs target bench\n");
        AssertOk(Execute(h, KfsCommands.Manual, ["KC-E003"]), "ran kfs manual KC-E003\n");
        AssertOk(Execute(h, KfsCommands.Parts, []), "ran purr parts\n");
        AssertOk(Execute(h, KfsCommands.Parts, ["lights"]), "ran purr parts lights\n");
        AssertOk(Execute(h, KfsCommands.Parts, ["deploy"]), "ran purr parts deploy\n");
        AssertOk(Execute(h, KfsCommands.Parts, ["actuators"]), "ran purr parts actuators\n");

        // A vehicle target: load adds --vehicle, as Workbench.LoadIntoTarget does.
        TargetVehicle(host);
        AssertOk(Execute(h, KfsCommands.Load, [blink]), "ran kfs load x_blink/blink.c --vehicle\n");

        lock (host.Verbs)
        {
            Assert.That(host.Verbs, Is.EqualTo(new[]
            {
                "build x_blink/blink.c", "build my prog/main.c", "build ünï code/ä.c", "load x_blink/blink.c", "start", "stop", "reset",
                "target vehicle", "target bench", "manual KC-E003", "load x_blink/blink.c --vehicle",
            }));
        }
        lock (purr) Assert.That(purr, Is.EqualTo(new[] { "parts", "parts lights", "parts deploy", "parts actuators" }));

        // Refusals: ok false, a clear error, nothing run.
        int before = host.Verbs.Count;
        Assert.That(Refused(Execute(h, KfsCommands.Load, [Uri(ws, "my prog/main.c")])), Does.Contain("splits its arguments at spaces"));
        string outside = WorkspaceUris.FileUri(Path.Combine(Path.GetTempPath(), "elsewhere", "x.c"), OperatingSystem.IsWindows());
        Assert.That(Refused(Execute(h, KfsCommands.Build, [outside])), Does.Contain("is not a file inside the kFS workspace"));
        Assert.That(Refused(Execute(h, KfsCommands.Build, [Uri(ws, "x_blink/../../x.c")])), Does.Contain("not a file inside"));
        Assert.That(Refused(Execute(h, KfsCommands.Build, ["https://example.org/x.c"])), Does.Contain("not a file inside"));
        Assert.That(Refused(Execute(h, KfsCommands.Build)), Does.Contain("takes one argument, the file's URI"));
        Assert.That(Refused(Execute(h, KfsCommands.Build, [42])), Does.Contain("takes one argument"));
        Assert.That(Refused(Execute(h, KfsCommands.Build, [blink, blink])), Does.Contain("takes one argument"));
        Assert.That(Refused(Execute(h, KfsCommands.Start, ["now"])), Is.EqualTo("kfs.start takes no arguments"));
        Assert.That(Refused(Execute(h, KfsCommands.Target, ["moon"])), Does.Contain("\"vehicle\" or \"bench\""));
        Assert.That(Refused(Execute(h, KfsCommands.Target)), Does.Contain("\"vehicle\" or \"bench\""));
        Assert.That(Refused(Execute(h, KfsCommands.Manual, ["KC E003"])), Does.Contain("one diagnostic code"));
        Assert.That(Refused(Execute(h, KfsCommands.Parts, ["wings"])), Does.Contain("\"lights\", \"deploy\", \"actuators\""));
        Assert.That(Refused(Execute(h, KfsCommands.Verb, ["state"])), Does.Contain("lsp_allow_vehicle_commands = true"), "kfs.verb is off by default");
        Assert.That(host.Verbs, Has.Count.EqualTo(before), "no refused command ran");
        Assert.That(h.Client.Request("workspace/executeCommand", new JsonObject { ["command"] = "kfs.nope" })["error"], Is.Not.Null,
            "an unknown command stays a JSON-RPC error (the server's own rule)");
    }

    [Test]
    public void Verb_Runs_Any_Line_When_Allowed()
    {
        using var ws = new TempWorkspace();
        RecordingHost host = NewHost(ws);
        using var h = ServerHarness.Start(host, configure: s => HostFeatures.AddTo(s, new HostFeatureOptions { AllowVerb = true }));
        JsonObject caps = h.Client.Initialize()["capabilities"]!.AsObject();
        Assert.That(caps["experimental"]!["purrtty"]!["verb"]!.GetValue<bool>(), Is.True);

        AssertOk(Execute(h, KfsCommands.Verb, ["state"]), "ran kfs state\n");
        AssertOk(Execute(h, KfsCommands.Verb, ["  kfs peek count 4 "]), "ran kfs peek count 4\n");
        Assert.That(Refused(Execute(h, KfsCommands.Verb, ["state\nstop"])), Does.Contain("one non-empty line"));
        Assert.That(Refused(Execute(h, KfsCommands.Verb, ["   "])), Does.Contain("one non-empty line"));
        Assert.That(Refused(Execute(h, KfsCommands.Verb, [])), Does.Contain("one string"));
        Assert.That(Refused(Execute(h, KfsCommands.Verb, ["explode"])), Is.EqualTo("kfs.verb failed: InvalidOperationException: the host broke"),
            "a host that throws gives a refusal");
        Assert.That(Refused(Execute(h, KfsCommands.Parts)), Does.Contain("needs the purr console command"), "no purr runner: refused");
        lock (host.Verbs) Assert.That(host.Verbs, Is.EqualTo(new[] { "state", "peek count 4", "explode" }));
    }

    [Test]
    public void A_Host_That_Cannot_Control_A_Vehicle_Refuses_And_Answers_The_Manual()
    {
        using var ws = new TempWorkspace();
        ws.Write("a.c", "\n");
        using var h = ServerHarness.Start(KfsLanguageHost.ForBench(Runtime.Tables, ws.Root), configure: s => HostFeatures.AddTo(s));
        JsonObject caps = h.Client.Initialize()["capabilities"]!.AsObject();
        Assert.That(caps["experimental"]!["purrtty"]!.ToJsonString(), Is.EqualTo("""{"subscribe":false,"vehicleCommands":false,"verb":false}"""));

        foreach (string command in new[] { KfsCommands.Start, KfsCommands.Stop, KfsCommands.Reset })
            Assert.That(Refused(Execute(h, command)), Does.Contain("needs the game"), command);
        Assert.That(Refused(Execute(h, KfsCommands.Build, [Uri(ws, "a.c")])), Is.EqualTo(KfsCommandProvider.NeedsTheGame("kfs.build")));
        Assert.That(KfsCommandProvider.NeedsTheGame("kfs.build"), Does.StartWith("kfs.build needs the game: this language server runs outside it"));
        Assert.That(Refused(Execute(h, KfsCommands.Parts)), Does.Contain("needs the game"));
        Assert.That(Refused(Execute(h, KfsCommands.Build, ["file:///nowhere/a.c"])), Does.Contain("not a file inside"), "argument checks first");

        JsonObject manual = Execute(h, KfsCommands.Manual, ["KC-E003"]);
        Assert.That(manual["ok"]!.GetValue<bool>(), Is.True, manual.ToJsonString());
        Assert.That(manual["output"]!.GetValue<string>(), Does.Contain("E003").And.Not.Contain("not in the diagnostics registry"),
            "answered from the tables (DiagnosticManual.Page, the text kfs manual prints)");

        // The status request and the subscription work without a game (nothing is ever sent).
        JsonNode? status = h.Client.Call(HostFeatures.StatusMethod);
        Assert.That(status!["vehicleCommands"]!.GetValue<bool>(), Is.False);
        Assert.That(status["workspaceRoot"]!.GetValue<string>(), Is.EqualTo(Path.GetFullPath(ws.Root).TrimEnd(Path.DirectorySeparatorChar)));
        h.Client.Notify(HostFeatures.SubscribeMethod, new JsonObject { ["topics"] = new JsonArray("log", "state") });
        h.Client.Sync();
    }

    [Test]
    public void Subscribe_Topics_Parse()
    {
        Assert.That(HostFeatures.ParseTopics(new JsonObject { ["topics"] = new JsonArray("log", "state", "bogus", 3) }), Is.EqualTo(LiveTopics.Log | LiveTopics.State));
        Assert.That(HostFeatures.ParseTopics(new JsonObject { ["topics"] = new JsonArray() }), Is.EqualTo(LiveTopics.None));
        Assert.That(HostFeatures.ParseTopics(new JsonObject { ["topics"] = new JsonArray("output") }), Is.EqualTo(LiveTopics.Output));
        Assert.That(HostFeatures.ParseTopics(new JsonObject()), Is.Null);
        Assert.That(HostFeatures.ParseTopics(null), Is.Null);
    }
}
