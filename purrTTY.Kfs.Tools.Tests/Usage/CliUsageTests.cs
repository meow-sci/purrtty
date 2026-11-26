using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Cli;

namespace purrTTY.Kfs.Tools.Tests.Usage;

/// <summary>
/// The parts of <c>purr-kfs</c> that need no game: usage errors (rejected before the game's
/// assemblies are looked for), a missing game directory, help, version and the catalogue commands.
/// No runtime is alive in this namespace.
/// </summary>
[TestFixture]
public sealed class CliUsageTests
{
    [Test]
    public void Cli_Check_Usage_Errors_Exit_2()
    {
        using var ws = new TempWorkspace().Write("main.c", "").Write("proj/main.c", "");
        string outside = Path.GetTempPath();

        var cases = new (string[] Args, string Message)[]
        {
            (["check"], "purr-kfs check: missing <file>\n"),
            (["check", "main.c", "proj/main.c"], "purr-kfs check: unexpected argument 'proj/main.c'\n"),
            (["check", "main.c", "--frobnicate"], "purr-kfs check: unknown option '--frobnicate'\n"),
            (["check", "main.c", "--lights", "many"], "purr-kfs check: --lights takes a whole number from 0 to 256, not 'many'\n"),
            (["check", "main.c", "--thrusters", "0"], "purr-kfs check: --thrusters takes a whole number from 1 to 256, not '0'\n"),
            (["check", "main.c", "--lights"], "purr-kfs check: --lights needs a value\n"),
            (["check", "main.c", "--json=yes"], "purr-kfs check: --json takes no value\n"),
            (["check", "main.c", "--json", "--json"], "purr-kfs check: --json given twice\n"),
            (["check", "nothing.c"], $"purr-kfs check: 'nothing.c': no such file in the workspace {ws.Root}\n"),
            (["check", "proj"], "purr-kfs check: 'proj' is a folder; name a .c or .kasm file in it\n"),
            (["check", Path.Combine(outside, "x.c")], null!),
            (["run", "main.c", "--for", "forever"], "purr-kfs run: --for takes a duration such as 10s, 500ms or 2m (more than 0, at most 24 h), not 'forever'\n"),
            (["run", "main.c", "--frame", "0"], "purr-kfs run: --frame takes a frame rate in Hz from 1 to 1000, not '0'\n"),
            (["run", "main.c", "--json"], "purr-kfs run: unknown option '--json'\n"),
        };

        foreach ((string[] args, string? message) in cases)
        {
            CliOutcome r = PurrKfs.In(ws, args);
            Assert.That(r.ExitCode, Is.EqualTo(2), () => string.Join(' ', args) + "\n" + r);
            Assert.That(r.Stdout, Is.Empty, () => string.Join(' ', args));
            if (message is not null) Assert.That(r.Stderr, Does.StartWith(message), () => string.Join(' ', args));
            Assert.That(r.Stderr, Does.EndWith($"Run 'purr-kfs {args[0]} --help' for usage.\n"), () => string.Join(' ', args));
        }

        // Unknown commands and options before the command.
        Assert.That(PurrKfs.Run(["build", "x.c"]), Is.EqualTo(new CliOutcome(2, "", "purr-kfs: unknown command 'build'\nRun 'purr-kfs --help' for usage.\n")));
        Assert.That(PurrKfs.Run(["--json", "check"]).ExitCode, Is.EqualTo(2));
        CliOutcome none = PurrKfs.Run([]);
        Assert.That((none.ExitCode, none.Stdout), Is.EqualTo((2, "")));
        Assert.That(none.Stderr, Does.StartWith("purr-kfs - check and run kFS programs"));
    }

    [Test]
    public void Cli_Check_Outside_The_Workspace_Exits_2()
    {
        using var ws = new TempWorkspace().Write("inner/main.c", "");
        using var other = new TempWorkspace().Write("main.c", "");

        CliOutcome r = PurrKfs.Run(["check", other.Full("main.c"), "--workspace", ws.Full("inner")]);

        Assert.That(r.ExitCode, Is.EqualTo(2), r.ToString);
        Assert.That(r.Stderr, Does.StartWith($"purr-kfs check: '{other.Full("main.c")}' is outside the workspace {ws.Full("inner")} (pass --workspace <dir>)\n"));
    }

    [Test]
    public void Cli_Check_Without_A_Game_Directory_Exits_2_Naming_Both_Ways_To_Configure_It()
    {
        using var ws = new TempWorkspace().Write("main.c", "");

        CliOutcome missing = PurrKfs.Run(["check", "main.c", "--workspace", ws.Root], noGame: true);
        Assert.That(missing.ExitCode, Is.EqualTo(2), missing.ToString);
        Assert.That(missing.Stdout, Is.Empty);
        Assert.That(missing.Stderr, Is.EqualTo("purr-kfs: the game's assemblies were not found. Pass --ksa <dir> or set the KSA_DLL_DIR " +
            "environment variable to the folder that holds KSA.dll (the game's install folder).\n"));

        CliOutcome run = PurrKfs.Run(["run", "main.c", "--workspace", ws.Root], noGame: true);
        Assert.That((run.ExitCode, run.Stderr), Is.EqualTo((2, missing.Stderr)));

        // A directory that is not one, or has no KSA.dll.
        string nowhere = Path.Combine(ws.Root, "no-such-dir");
        CliOutcome bad = PurrKfs.Run(["check", "main.c", "--workspace", ws.Root, "--ksa", nowhere], noGame: true);
        Assert.That((bad.ExitCode, bad.Stderr), Is.EqualTo((2, $"purr-kfs: --ksa names '{nowhere}', which is not a folder (it must hold KSA.dll).\n")));
        CliOutcome empty = PurrKfs.Run(["check", "main.c", "--workspace", ws.Root], ksaDllDir: ws.Root);
        Assert.That((empty.ExitCode, empty.Stderr), Is.EqualTo((2, $"purr-kfs: KSA_DLL_DIR names '{ws.Root}', which has no KSA.dll.\n")));
    }

    [Test]
    public void Cli_Header_And_Services_Match_The_Catalogue()
    {
        // No game directory needed: the catalogue is game-free.
        CliOutcome header = PurrKfs.Run(["header"], noGame: true);
        Assert.That(header, Is.EqualTo(new CliOutcome(0, CatalogEmitter.Header(XCatalog.Catalog), "")));
        Assert.That(header.Stdout, Does.StartWith("#pragma once\r\n"), "the header keeps its CRLF line ends");

        CliOutcome kinc = PurrKfs.Run(["kinc"], noGame: true);
        Assert.That(kinc, Is.EqualTo(new CliOutcome(0, CatalogEmitter.Kinc(XCatalog.Catalog), "")));

        CliOutcome markdown = PurrKfs.Run(["services", "--markdown"], noGame: true);
        Assert.That(markdown.ExitCode, Is.EqualTo(0));
        Assert.That(markdown.Stdout, Is.EqualTo(CatalogEmitter.MarkdownReference(XCatalog.Catalog).TrimEnd('\n') + "\n"));

        CliOutcome table = PurrKfs.Run(["services"], noGame: true);
        Assert.That(table.ExitCode, Is.EqualTo(0));
        string[] rows = table.Stdout.TrimEnd('\n').Split('\n');
        Assert.That(rows, Has.Length.EqualTo(XCatalog.Catalog.Services.Count + 1));
        Assert.That(rows[0], Does.StartWith("ID      KIND            COST    PROTOTYPE"));
        for (int i = 0; i < XCatalog.Catalog.Services.Count; i++)
        {
            ServiceDef s = XCatalog.Catalog.Services[i];
            Assert.That(rows[i + 1], Does.StartWith($"0x{s.Id:X4}  {s.Kind}"), s.Name);
            Assert.That(rows[i + 1], Does.Contain($"  {s.CostClass}").And.Contain($"  {s.Prototype}  ").And.EndWith(s.Summary), s.Name);
        }

        Assert.That(PurrKfs.Run(["header", "extra"]).ExitCode, Is.EqualTo(2));
        Assert.That(PurrKfs.Run(["services", "--json"]).ExitCode, Is.EqualTo(2));
    }

    [Test]
    public void Cli_Help_And_Version()
    {
        CliOutcome help = PurrKfs.Run(["--help"], noGame: true);
        Assert.That((help.ExitCode, help.Stderr), Is.EqualTo((0, "")));
        Assert.That(help.Stdout, Does.StartWith("purr-kfs - check and run kFS programs").And.Contain("\n  check <file> ").And.Contain("KSA_DLL_DIR"));
        Assert.That(PurrKfs.Run(["-h"], noGame: true), Is.EqualTo(help));
        Assert.That(PurrKfs.Run(["help"], noGame: true), Is.EqualTo(help));

        foreach (string command in new[] { "check", "run", "header", "kinc", "services" })
        {
            CliOutcome h = PurrKfs.Run([command, "--help"], noGame: true);
            Assert.That((h.ExitCode, h.Stderr), Is.EqualTo((0, "")), command);
            Assert.That(h.Stdout, Does.StartWith($"Usage: purr-kfs {command}"), command);
            Assert.That(PurrKfs.Run(["help", command], noGame: true), Is.EqualTo(h), command);
            // --help wins over anything else on the line, even an invalid argument.
            Assert.That(PurrKfs.Run([command, "--bogus", "-h"], noGame: true), Is.EqualTo(h), command);
        }
        Assert.That(PurrKfs.Run(["check", "--help"]).Stdout, Does.Contain("file:line:col: severity code: message"));
        Assert.That(PurrKfs.Run(["help", "nope"]).ExitCode, Is.EqualTo(2));

        CliOutcome version = PurrKfs.Run(["--version"], noGame: true);
        Assert.That(version, Is.EqualTo(new CliOutcome(0, $"purr-kfs {CliApp.Version}\n", "")));
        Assert.That(CliApp.Version, Does.Match("^[0-9]+\\.[0-9]+\\.[0-9]+"));
    }

    [Test]
    public void No_Game_Assemblies_In_The_Tool_Outputs()
    {
        var offenders = new List<string>();
        offenders.AddRange(GameAssembliesIn(AppContext.BaseDirectory, recursive: false));
        string? root = FindRepositoryRoot(AppContext.BaseDirectory);
        if (root is not null)
        {
            string bin = Path.Combine(root, "purrTTY.Kfs.Cli", "bin");
            if (Directory.Exists(bin)) offenders.AddRange(GameAssembliesIn(bin, recursive: true));
        }
        Assert.That(offenders, Is.Empty, "game assemblies must be resolved at run time, never copied");
    }

    private static IEnumerable<string> GameAssembliesIn(string directory, bool recursive)
    {
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return new[] { "KSA.dll", "Brutal.*.dll", "Planet.*.dll" }
            .SelectMany(pattern => Directory.EnumerateFiles(directory, pattern, option));
    }

    private static string? FindRepositoryRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "purrtty.slnx"))) return dir.FullName;
        }
        return null;
    }
}
