using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using NUnit.Framework;
using purrTTY.Kfs.Lsp.InGame;

namespace purrTTY.Kfs.Tools.Tests.Lsp;

/// <summary>
/// <see cref="OutputTee"/> and <see cref="OutputTeeInstallation"/> (no game: the slot is a local
/// variable standing in for <c>KfsIntegration.Output</c>).
/// </summary>
[TestFixture]
public sealed class OutputTeeTests
{
    /// <summary>A writer that records each call by overload, like the game's line-splitting console writer would see them.</summary>
    private sealed class Recorder : TextWriter
    {
        public List<string> Calls { get; } = [];
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) => Calls.Add($"Write('{value}')");
        public override void Write(string? value) => Calls.Add($"Write(\"{value}\")");
        public override void WriteLine(string? value) => Calls.Add($"WriteLine(\"{value}\")");
        public override void WriteLine() => Calls.Add("WriteLine()");
    }

    [Test]
    public void Forwards_Each_Overload_And_Feeds_The_Sink_And_Captures()
    {
        var original = new Recorder();
        var sink = new StringBuilder();
        var tee = new OutputTee(original, t => sink.Append(t));

        using (OutputTee.Capture capture = tee.BeginCapture())
        {
            tee.WriteLine("kfs is on");
            tee.Write("line 1\r\nline 2\n");
            tee.Write('x');
            tee.WriteLine();
            Assert.That(capture.Text, Is.EqualTo("kfs is on\nline 1\nline 2\nx\n"));
        }
        tee.WriteLine("after the capture");

        Assert.That(original.Calls, Is.EqualTo(new[] { "WriteLine(\"kfs is on\")", "Write(\"line 1\r\nline 2\n\")", "Write('x')", "WriteLine()", "WriteLine(\"after the capture\")" }),
            "the original sees the same calls, so it splits lines as before");
        Assert.That(sink.ToString(), Is.EqualTo("kfs is on\nline 1\nline 2\nx\nafter the capture\n"));
    }

    [Test]
    public void Captures_Belong_To_Their_Thread_And_Nest()
    {
        var tee = new OutputTee(new Recorder(), null);
        using OutputTee.Capture outer = tee.BeginCapture();
        using (OutputTee.Capture inner = tee.BeginCapture())
        {
            tee.WriteLine("both");
            var other = new Thread(() => tee.WriteLine("another thread"));
            other.Start();
            Assert.That(other.Join(TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(inner.Text, Is.EqualTo("both\n"));
        }
        tee.WriteLine("outer only");
        Assert.That(outer.Text, Is.EqualTo("both\nouter only\n"));
    }

    [Test]
    public void Clients_Only_Keeps_Text_From_The_Original()
    {
        var original = new Recorder();
        var sink = new StringBuilder();
        var tee = new OutputTee(original, t => sink.Append(t));
        using OutputTee.Capture capture = tee.BeginCapture();

        OutputTee.EnterClientsOnly();
        try
        {
            tee.WriteLine("unknown kfs command 'nope'");   // what KfsIntegration.Error writes to Output
        }
        finally
        {
            OutputTee.ExitClientsOnly();
        }
        tee.WriteLine("kfs is on");

        Assert.That(original.Calls, Is.EqualTo(new[] { "WriteLine(\"kfs is on\")" }), "the error line reaches the console through the log only");
        Assert.That(capture.Text, Is.EqualTo("unknown kfs command 'nope'\nkfs is on\n"), "the editor gets both");
        Assert.That(sink.ToString(), Is.EqualTo(capture.Text));
        Assert.That(OutputTee.ClientsOnlyDepth, Is.Zero);
        OutputTee.ExitClientsOnly();
        Assert.That(OutputTee.ClientsOnlyDepth, Is.Zero, "never below zero");
    }

    [Test]
    public void A_Throwing_Sink_Never_Breaks_The_Writer()
    {
        var original = new Recorder();
        var tee = new OutputTee(original, _ => throw new InvalidOperationException("feed gone"));
        Assert.DoesNotThrow(() => tee.WriteLine("still printed"));
        Assert.That(original.Calls, Is.EqualTo(new[] { "WriteLine(\"still printed\")" }));
    }

    [Test]
    public void Install_And_Restore_Put_The_Original_Back()
    {
        TextWriter slot = new Recorder();
        TextWriter original = slot;
        var sink = new StringBuilder();
        OutputTeeInstallation installed = OutputTeeInstallation.Install(() => slot, w => slot = w, t => sink.Append(t));
        Assert.That(slot, Is.SameAs(installed.Tee));
        Assert.That(installed.Tee.Original, Is.SameAs(original));

        slot.WriteLine("through the tee");
        Assert.That(installed.Restore(), Is.EqualTo(OutputTeeRestore.Restored));
        Assert.That(slot, Is.SameAs(original));
        Assert.That(installed.Restore(), Is.EqualTo(OutputTeeRestore.AlreadyRestored));

        // A writer that still holds the tee (the workbench's copy until its next refresh) passes through.
        installed.Tee.WriteLine("late");
        Assert.That(sink.ToString(), Is.EqualTo("through the tee\n"), "a restored tee feeds nothing");
        Assert.That(((Recorder)original).Calls, Is.EqualTo(new[] { "WriteLine(\"through the tee\")", "WriteLine(\"late\")" }));
    }

    [Test]
    public void Restore_Leaves_A_Foreign_Writer_On_Top()
    {
        var original = new Recorder();
        TextWriter slot = original;
        OutputTeeInstallation installed = OutputTeeInstallation.Install(() => slot, w => slot = w, null);
        var foreign = new Recorder();
        slot = foreign;   // another mod replaced Output after us

        Assert.That(installed.Restore(), Is.EqualTo(OutputTeeRestore.Foreign));
        Assert.That(slot, Is.SameAs(foreign), "never restored over a writer that is not ours");
        Assert.That(installed.Tee.IsDetached, Is.True);

        // The foreign writer goes away, leaving our detached tee in the slot: the next install
        // unwraps it rather than stacking a second tee.
        slot = installed.Tee;
        OutputTeeInstallation next = OutputTeeInstallation.Install(() => slot, w => slot = w, null);
        Assert.That(next.Tee.Original, Is.SameAs(original));
        Assert.That(next.Restore(), Is.EqualTo(OutputTeeRestore.Restored));
        Assert.That(slot, Is.SameAs(original));
    }

    [Test]
    public void Install_Unwraps_A_Detached_Tee_From_Another_Load_Context()
    {
        // A second load of purrTTY.Kfs.Lsp.dll, as a reload in another load context would have:
        // its OutputTee has the same full name but is a different .NET type.
        var context = new AssemblyLoadContext("second purrTTY.Kfs.Lsp", isCollectible: true);
        try
        {
            Assembly second = context.LoadFromAssemblyPath(typeof(OutputTee).Assembly.Location);
            Type foreignType = second.GetType(typeof(OutputTee).FullName!, throwOnError: true)!;
            Assert.That(foreignType, Is.Not.EqualTo(typeof(OutputTee)), "a different type of the same name");

            var original = new Recorder();
            var stale = (TextWriter)Activator.CreateInstance(foreignType, original, null, true)!;
            Assert.That(OutputTeeInstallation.StaleOriginal(stale), Is.Null, "a live tee is not unwrapped");
            foreignType.GetMethod(nameof(OutputTee.Detach))!.Invoke(stale, null);
            Assert.That(OutputTeeInstallation.StaleOriginal(stale), Is.SameAs(original));

            // Two loads stacked: our own detached tee over the foreign one. Both are unwrapped.
            var ownStale = new OutputTee(stale, null);
            ownStale.Detach();
            TextWriter slot = ownStale;
            OutputTeeInstallation installed = OutputTeeInstallation.Install(() => slot, w => slot = w, null);
            Assert.That(installed.Tee.Original, Is.SameAs(original), "no tee stacked on a detached one");
            Assert.That(installed.Restore(), Is.EqualTo(OutputTeeRestore.Restored));
            Assert.That(slot, Is.SameAs(original));
        }
        finally
        {
            context.Unload();
        }
    }

    [Test]
    public void Only_A_Detached_Tee_Is_Unwrapped()
    {
        Assert.That(OutputTeeInstallation.StaleOriginal(new Recorder()), Is.Null);
        Assert.That(OutputTeeInstallation.StaleOriginal(null), Is.Null);
        var live = new OutputTee(new Recorder(), null);
        Assert.That(OutputTeeInstallation.StaleOriginal(live), Is.Null, "live: wrapped, not unwrapped");
    }
}
