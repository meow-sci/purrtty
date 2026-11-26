using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Commands;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests;

/// <summary>
/// The <c>purr</c> dispatcher and its verb registry with the extension NOT installed (this
/// namespace runs with no runtime alive): help, unknown verbs, failing handlers, extra arguments,
/// the verbs that need the extension, and the catalogue verbs. Output is captured through the
/// dispatcher's writer; nothing reaches the game's console.
/// </summary>
[TestFixture]
public sealed class PurrConsoleTests : GameFixture
{
    private readonly List<string> _out = [];

    [SetUp]
    public void Clear()
    {
        _out.Clear();
        Assert.That(KfsExtension.IsActive, Is.False, "these tests run with the extension not installed");
    }

    private List<string> Run(string args, IKfsConsoleHost? host = null)
    {
        _out.Clear();
        PurrConsole.Run(args, host, _out.Add);
        return _out;
    }

    [Test]
    public void Console_Command_Owner_Is_Shared_By_Every_Load_Of_The_Assembly()
    {
        // A second load of the mod (a fresh load context after an unload that never ran) must file
        // purr under the same owner, or the game's help throws on the duplicate name.
        object owner = purrTTY.Kfs.Game.KsaConsoleHost.Owner;
        Assert.That(AppDomain.CurrentDomain.GetData(purrTTY.Kfs.Game.KsaConsoleHost.OwnerDataKey), Is.SameAs(owner));
        string key = "purrTTY.Kfs.Tests.owner-" + Guid.NewGuid().ToString("N");
        try
        {
            object first = purrTTY.Kfs.Game.KsaConsoleHost.SharedOwner(key, () => new object());
            object second = purrTTY.Kfs.Game.KsaConsoleHost.SharedOwner(key, () => new object());   // the other load's own factory
            Assert.That(second, Is.SameAs(first));
        }
        finally
        {
            AppDomain.CurrentDomain.SetData(key, null);
        }
    }

    [Test]
    public void Console_Output_Is_Ascii_By_Default()
    {
        Assert.That(PurrConsole.AsciiOutput, Is.True, "the game console's font cannot be checked from here");
        Assert.That(ConsoleText.ToAscii("active — 22 services 0x5000–0x5033 · 0.25 → 1.00, long nam… “q” ‘q’ ≤ ≥ ×"),
            Is.EqualTo("active - 22 services 0x5000-0x5033 - 0.25 -> 1.00, long nam... \"q\" 'q' <= >= x"));
        const string plain = "plain ASCII, and Kätzchen keeps its umlaut";
        Assert.That(ConsoleText.ToAscii(plain), Is.SameAs(plain), "nothing to replace: no copy");

        Assert.That(Run("services")[0], Does.Contain(") - Reads a vehicle-wide parts figure"));
        PurrConsole.AsciiOutput = false;
        try
        {
            Assert.That(Run("services")[0], Does.Contain(") — Reads a vehicle-wide parts figure"), "switched off: as written");
        }
        finally
        {
            PurrConsole.AsciiOutput = true;
        }
    }

    [Test]
    public void Help_Lists_Every_Verb_And_Says_The_Extension_Is_Not_Active()
    {
        string[] expected =
        [
            "purr: the purrTTY kFS extension is not active (not installed); verbs (purr <verb> [arguments]):",
            "  help      this list",
            "  status    the extension's status: hooks, tables, console, counters",
            "  parts     which x_ index is which part of the controlled vehicle [lights | deploy | actuators]",
            "  lights    restore the stock look of lights a program changed: reset [all] (the controlled vehicle, or every vehicle)",
            "  examples  write the purrTTY example programs into the kFS workspace [--force overwrites]",
            "  header    print <purrtty.h>, the kC header of the x_ services",
            "  kinc      print purrtty.kinc, the x_ constants for kASM",
            "  services  one line per x_ service: id, prototype, summary",
        ];
        Assert.That(Run(""), Is.EqualTo(expected), "no verb is help");
        Assert.That(Run("help"), Is.EqualTo(expected));
        Assert.That(Run("  HELP  "), Is.EqualTo(expected), "verbs are case-insensitive; white space is trimmed");
        Assert.That(PurrConsole.Verbs.All.Select(v => v.Name), Is.EqualTo(new[] { "help", "status", "parts", "lights", "examples", "header", "kinc", "services" }));
    }

    [Test]
    public void Unknown_Verb_Lists_The_Verbs()
    {
        Assert.That(Run("beam on"), Is.EqualTo(new[]
        {
            "unknown purr verb 'beam'; verbs: help, status, parts, lights, examples, header, kinc, services (purr help explains them)",
        }));
    }

    [Test]
    public void Verbs_That_Need_The_Extension_Say_It_Is_Not_Active()
    {
        var host = new SimConsoleHost();
        Assert.That(Run("parts", host), Is.EqualTo(new[]
        {
            "purr parts: the purrTTY kFS extension is not active (not installed); purr status says more",
        }), "not installed, even with a host");
        Assert.That(Run("examples --force"), Is.EqualTo(new[]
        {
            "purr examples: the purrTTY kFS extension is not active (not installed); purr status says more",
        }));
        Assert.That(host.Writes, Is.Empty);

        // status works without the extension: the not-installed line and the (process-wide) counters.
        List<string> status = Run("status");
        Assert.That(status, Is.EqualTo(KfsExtension.Status.Describe().Split('\n').Select(ConsoleText.ToAscii)));
        Assert.That(status, Has.Count.EqualTo(4));
        Assert.That(status[0], Is.EqualTo("purrTTY kFS extension: not installed"));
        Assert.That(status[1], Is.EqualTo("  vehicles: 0 bound; requests 0 accepted, 0 rejected; 0 answered"));
    }

    [Test]
    public void Extra_Arguments_Are_Refused_With_The_Usage()
    {
        Assert.That(Run("status now"), Is.EqualTo(new[] { "purr status: unexpected argument 'now' (usage: purr status)" }));
        Assert.That(Run("header  please  "), Is.EqualTo(new[] { "purr header: unexpected argument 'please' (usage: purr header)" }));
        Assert.That(Run("services x y"), Is.EqualTo(new[] { "purr services: unexpected argument 'x y' (usage: purr services)" }));
    }

    [Test]
    public void A_Throwing_Handler_Is_Reported_And_Contained()
    {
        long failures = KfsLog.Count("purr boom");
        using (PurrConsole.Verbs.Add("boom", "always fails", _ => throw new InvalidOperationException("kaboom")))
        {
            Assert.DoesNotThrow(() => Run("boom"));
            Assert.That(_out, Is.EqualTo(new[]
            {
                "purr boom failed, which is a bug in purrTTY and not in your program: InvalidOperationException: kaboom",
            }));
            Assert.That(KfsLog.Count("purr boom"), Is.EqualTo(failures + 1), "logged once through KfsLog");
        }
        Assert.That(PurrConsole.Verbs.Find("boom"), Is.Null, "disposing the handle removes the verb");
    }

    [Test]
    public void A_Throwing_Writer_Never_Escapes()
    {
        Assert.DoesNotThrow(() => PurrConsole.Run("help", null, _ => throw new IOException("console gone")));
    }

    [Test]
    public void Added_Verbs_Get_Arguments_And_Show_In_Help()
    {
        var seen = new List<(string Args, string Words, bool Active, bool HasHost)>();
        using (PurrConsole.Verbs.Add("lsp", "the language server: address, clients", c =>
               {
                   seen.Add((c.Args, string.Join("|", c.Words), c.ExtensionActive, c.Host is not null));
                   c.Print("line one\r\nline two");
               }))
        {
            Assert.That(Run("lsp  restart   now "), Is.EqualTo(new[] { "line one", "line two" }), "multi-line text becomes one line each");
            Assert.That(seen, Is.EqualTo(new[] { ("restart   now", "restart|now", false, false) }));
            Assert.That(Run("help"), Has.Member("  lsp       the language server: address, clients"));
            Assert.Throws<InvalidOperationException>(() => PurrConsole.Verbs.Add("lsp", "again", _ => { }), "names are unique");
        }
        Assert.Throws<ArgumentException>(() => PurrConsole.Verbs.Add("Bad Name", "x", _ => { }));
        Assert.Throws<ArgumentException>(() => PurrConsole.Verbs.Add("ok", "two\nlines", _ => { }));
        Assert.Throws<InvalidOperationException>(() => PurrConsole.Verbs.Add("parts", "mine", _ => { }), "a built-in cannot be replaced");
        Assert.That(Run("lsp"), Has.Count.EqualTo(1).And.Some.StartsWith("unknown purr verb 'lsp'"));
    }

    [Test]
    public void Needs_Extension_Verbs_Added_Later_Are_Gated_Too()
    {
        bool ran = false;
        using (PurrConsole.Verbs.Add("nvim", "open a file in Neovim", _ => ran = true, needsExtension: true))
        {
            Assert.That(Run("nvim x_blink/blink.c", new SimConsoleHost()), Is.EqualTo(new[]
            {
                "purr nvim: the purrTTY kFS extension is not active (not installed); purr status says more",
            }));
        }
        Assert.That(ran, Is.False);
    }

    [Test]
    public void Header_Kinc_And_Services_Match_The_Catalogue()
    {
        Assert.That(string.Join("\r\n", Run("header")) + "\r\n", Is.EqualTo(CatalogEmitter.Header(XCatalog.Catalog)));
        Assert.That(string.Join("\r\n", Run("kinc")) + "\r\n", Is.EqualTo(CatalogEmitter.Kinc(XCatalog.Catalog)));

        List<string> services = Run("services");
        Assert.That(services, Has.Count.EqualTo(XCatalog.Services.Count));
        for (int i = 0; i < XCatalog.Services.Count; i++)
        {
            ServiceDef s = XCatalog.Services[i];
            Assert.That(services[i], Is.EqualTo(ConsoleText.ToAscii($"0x{s.Id:X4}  {s.Prototype} — {s.Summary}")));
        }
        Assert.That(services[0], Is.EqualTo("0x5000  uint32_t x_parts_info(uint32_t what) - Reads a vehicle-wide parts figure: the list generation, the dirty flag or a part count."));
    }
}
