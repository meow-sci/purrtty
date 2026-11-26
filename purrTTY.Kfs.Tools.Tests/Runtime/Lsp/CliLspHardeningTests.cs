using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using NUnit.Framework;
using purrTTY.Kfs.Cli;
using purrTTY.Kfs.Tools.Tests.Lsp;

namespace purrTTY.Kfs.Tools.Tests.Runtime.Lsp;

/// <summary>
/// <c>purr-kfs</c>'s side of the review: <c>lsp --stdio</c> ends on SIGTERM / Ctrl+C while its stdin
/// stays open (B4, a real child process), <c>lsp --listen</c> never runs without a token (B5) and
/// takes one from a file or the environment instead of the command line (D5), and <c>check</c>
/// reports a file name the file system refuses instead of dying (D1).
/// </summary>
[TestFixture]
public sealed class CliLspHardeningTests : ToolsRuntimeFixture
{
    private sealed class Lines : StringWriter
    {
        private readonly object _gate = new();
        private readonly List<string> _lines = [];

        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            lock (_gate)
            {
                _lines.Add(value ?? "");
                Monitor.PulseAll(_gate);
            }
        }

        public string? WaitFor(string prefix)
        {
            var deadline = DateTime.UtcNow + LspTestClient.Timeout;
            lock (_gate)
            {
                while (true)
                {
                    if (_lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal)) is { } line) return line[prefix.Length..];
                    TimeSpan left = deadline - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero) return null;
                    Monitor.Wait(_gate, left);
                }
            }
        }
    }

    /// <summary>Runs <c>purr-kfs lsp --listen</c> in process with <paramref name="variables"/>; returns the stdout lines and a stop.</summary>
    private static (Lines Stdout, Action Stop) Listen(TempWorkspace ws, string[] extra, Dictionary<string, string>? variables = null)
    {
        var stop = new CancellationTokenSource();
        var stdout = new Lines { NewLine = "\n" };
        var stderr = new StringWriter();
        var environment = new CliEnvironment(name => name == "KSA_DLL_DIR" ? ModuleInit.Directory : variables?.GetValueOrDefault(name), ws.Root) { Cancellation = stop.Token };
        var cli = new Thread(() => CliApp.Run(["lsp", "--listen", "127.0.0.1:0", "--workspace", ws.Root, .. extra], stdout, stderr, environment)) { IsBackground = true };
        cli.Start();
        return (stdout, () =>
        {
            stop.Cancel();
            Assert.That(cli.Join(LspTestClient.Timeout), Is.True, () => "purr-kfs lsp did not return: " + stderr);
            stop.Dispose();
        });
    }

    private static int PortOf(Lines stdout) =>
        int.Parse(stdout.WaitFor("LISTENING ") ?? throw new AssertionException("no LISTENING line: " + stdout), CultureInfo.InvariantCulture);

    private static bool Accepts(int port, string? token)
    {
        using LspTestClient client = ServerProtocolTests.Connect(port);
        return client.Request("initialize", LspTestClient.InitializeParams(token))["error"] is null;
    }

    [Test]
    public void Listen_Without_A_Token_Makes_One_And_Prints_It()
    {
        using var ws = new TempWorkspace();
        (Lines stdout, Action stop) = Listen(ws, []);
        try
        {
            int port = PortOf(stdout);
            string token = stdout.WaitFor("TOKEN ") ?? throw new AssertionException("no TOKEN line: " + stdout);
            Assert.That(token, Does.Match("^[A-Za-z0-9_-]{43}$"));
            Assert.That(Accepts(port, null), Is.False, "never driven blind (it used to accept any client)");
            Assert.That(Accepts(port, token), Is.True);
        }
        finally
        {
            stop();
        }
        Assert.That(stdout.ToString(), Does.Match("^LISTENING \\d+\nTOKEN [A-Za-z0-9_-]{43}\n$"), "two lines, the port first");
    }

    [Test]
    public void The_Token_Can_Come_From_A_File_Or_The_Environment()
    {
        using var ws = new TempWorkspace().Write("secret/token.txt", "from-a-file\nignored\n");
        (Lines fileOut, Action stopFile) = Listen(ws, ["--token-file", ws.Full("secret/token.txt")]);
        try
        {
            int port = PortOf(fileOut);
            Assert.That(Accepts(port, "from-a-file"), Is.True);
            Assert.That(Accepts(port, "wrong"), Is.False);
        }
        finally
        {
            stopFile();
        }
        Assert.That(fileOut.ToString(), Does.Not.Contain("TOKEN"), "a token that was given is not printed");

        (Lines envOut, Action stopEnv) = Listen(ws, [], new Dictionary<string, string> { ["PURR_KFS_LSP_TOKEN"] = "from-the-environment" });
        try
        {
            int port = PortOf(envOut);
            Assert.That(Accepts(port, "from-the-environment"), Is.True);
            Assert.That(Accepts(port, null), Is.False);
        }
        finally
        {
            stopEnv();
        }

        CliOutcome both = PurrKfs.Run(["lsp", "--listen", "127.0.0.1:0", "--token", "a", "--token-file", ws.Full("secret/token.txt"), "--workspace", ws.Root]);
        Assert.That(both.ExitCode, Is.EqualTo(CliApp.ExitUsage), both.ToString);
        Assert.That(both.Stderr, Does.Contain("give at most one of --token and --token-file"));
        CliOutcome missing = PurrKfs.Run(["lsp", "--listen", "127.0.0.1:0", "--token-file", ws.Full("nope.txt"), "--workspace", ws.Root]);
        Assert.That(missing.ExitCode, Is.EqualTo(CliApp.ExitUsage));
        Assert.That(missing.Stderr, Does.Contain("--token-file: cannot read"));
        Assert.That(PurrKfs.Run(["lsp", "--help"]).Stdout, Does.Contain("--token-file <file>").And.Contain("PURR_KFS_LSP_TOKEN"));
    }

    [Test]
    public void Check_Reports_A_Nul_In_An_Include_Name_Instead_Of_Dying()
    {
        using var ws = new TempWorkspace().Write("main.c", "#include <kfs.h>\n#include \"a\0b.h\"\n#pragma kfs tick_hz 10\n#pragma kfs task run rate_hz=10\nvoid run(void) { }\n");
        CliOutcome r = PurrKfs.In(ws, "check", "main.c");
        Assert.That(r.ExitCode, Is.EqualTo(CliApp.ExitOk).Or.EqualTo(1), r.ToString);   // it exited 134 (an unhandled ArgumentException)
        Assert.That(r.Stdout + r.Stderr, Does.Not.Contain("Exception"), r.ToString);
    }

    // ------------------------------------------------------------------ B4: a signal ends --stdio

    [Test]
    public void Stdio_Ends_On_SIGTERM_And_SIGINT_While_Stdin_Stays_Open()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("POSIX signals");
        string? dotnet = DotnetHost();
        if (dotnet is null) Assert.Ignore("no dotnet host next to the running runtime");
        using var ws = new TempWorkspace();
        // Both at once (each is a process start of the CLI and the game's assemblies).
        var children = new[] { "TERM", "INT" }.Select(signal => (Signal: signal, Process: StartStdio(dotnet, ws), Stderr: new StringBuilder())).ToList();
        try
        {
            foreach (var c in children)
            {
                StringBuilder stderr = c.Stderr;
                c.Process.ErrorDataReceived += (_, e) => { lock (stderr) stderr.AppendLine(e.Data); };
                c.Process.BeginErrorReadLine();
                byte[] frame = LspTestClient.Frame(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "initialize", ["params"] = LspTestClient.InitializeParams() });
                c.Process.StandardInput.BaseStream.Write(frame);
                c.Process.StandardInput.BaseStream.Flush();
            }
            foreach (var c in children)
            {
                // The answer proves the server is up and blocked reading stdin, which stays open.
                var head = new byte[16];
                int got = 0;
                Process p = c.Process;
                var read = Task.Run(() => { while (got < head.Length) { int n = p.StandardOutput.BaseStream.Read(head, got, head.Length - got); if (n <= 0) break; got += n; } });
                Assert.That(read.Wait(LspTestClient.Timeout) && got == head.Length, Is.True, () => "no answer to initialize: " + c.Stderr);
                Assert.That(Encoding.ASCII.GetString(head), Does.StartWith("Content-Length:"));
            }
            foreach (var c in children)
            {
                var clock = Stopwatch.StartNew();
                using (Process kill = Process.Start(new ProcessStartInfo("kill", ["-" + c.Signal, c.Process.Id.ToString(CultureInfo.InvariantCulture)]) { UseShellExecute = false })!)
                    Assert.That(kill.WaitForExit(LspTestClient.Timeout), Is.True);
                Assert.That(c.Process.WaitForExit(LspTestClient.Timeout), Is.True, $"SIG{c.Signal} did not end purr-kfs lsp --stdio while stdin is open (it used to run on)");
                Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), $"SIG{c.Signal}: promptly");
                lock (c.Stderr) Assert.That(c.Stderr.ToString(), Does.Not.Contain("Unhandled"));
            }
        }
        finally
        {
            foreach (var c in children)
            {
                if (!c.Process.HasExited) c.Process.Kill(entireProcessTree: true);
                c.Process.Dispose();
            }
        }
    }

    private static Process StartStdio(string dotnet, TempWorkspace ws)
    {
        var psi = new ProcessStartInfo(dotnet, [Path.Combine(AppContext.BaseDirectory, "purr-kfs.dll"), "lsp", "--stdio", "--workspace", ws.Root])
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["KSA_DLL_DIR"] = ModuleInit.Directory;
        return Process.Start(psi)!;
    }

    // The dotnet host of the running runtime: <root>/shared/Microsoft.NETCore.App/<version>/ -> <root>/dotnet.
    private static string? DotnetHost()
    {
        string runtime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        string root = Path.GetFullPath(Path.Combine(runtime, "..", "..", ".."));
        string candidate = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(candidate) ? candidate : null;
    }
}
