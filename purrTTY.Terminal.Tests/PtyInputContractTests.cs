using System.Text;
using NUnit.Framework;
using purrTTY.Core.Terminal;
using purrTTY.Core.Terminal.Process;

namespace PurrTTY.Terminal.Tests;

/// <summary>
/// Pins the PTY input-layer contracts that are pure logic (and therefore testable on
/// every host OS): the Windows command-line quoting in
/// <see cref="ShellCommandResolver"/> — joining argv with bare spaces is the
/// join-then-split bug the project documentation warns about — and the bounded
/// <see cref="PtyInputQueue"/> that keeps blocking PTY writes off the render thread.
/// </summary>
[TestFixture]
public sealed class PtyInputContractTests
{
    private static string Quote(string argument)
    {
        var builder = new StringBuilder();
        ShellCommandResolver.AppendQuotedArgument(builder, argument);
        return builder.ToString();
    }

    [Test]
    public void QuotedArgument_PlainToken_IsNotQuoted()
    {
        Assert.That(Quote("-NoLogo"), Is.EqualTo("-NoLogo"));
    }

    [Test]
    public void QuotedArgument_EmptyAndSpacedTokens_AreQuoted()
    {
        Assert.That(Quote(""), Is.EqualTo("\"\""));
        Assert.That(Quote(@"C:\Program Files\PowerShell\7\pwsh.exe"),
            Is.EqualTo("\"C:\\Program Files\\PowerShell\\7\\pwsh.exe\""));
    }

    [Test]
    public void QuotedArgument_EmbeddedQuotesAndBackslashes_FollowWindowsRules()
    {
        // Per CommandLineToArgvW: backslashes before a quote double, the quote is escaped.
        Assert.That(Quote("say \"hi\""), Is.EqualTo("\"say \\\"hi\\\"\""));
        Assert.That(Quote(@"back\slash value"), Is.EqualTo("\"back\\slash value\""));
        Assert.That(Quote(@"plain\backslash"), Is.EqualTo(@"plain\backslash"), "no space/tab/quote ⇒ no quoting needed");
        Assert.That(Quote(@"C:\dir name\"), Is.EqualTo("\"C:\\dir name\\\\\""), "trailing backslash inside quotes must double");
        Assert.That(Quote(@"mix\\""x"), Is.EqualTo("\"mix\\\\\\\\\\\"x\""));
    }

    [Test]
    public void ResolveShellCommandLine_QuotesPathAndArgumentsWithSpaces()
    {
        string shellDir = Directory.CreateTempSubdirectory("purrtty resolver test").FullName;
        try
        {
            string shellPath = Path.Combine(shellDir, "fake shell.exe");
            File.WriteAllText(shellPath, string.Empty);

            var options = ProcessLaunchOptions.CreateCustom(shellPath, "--cd", @"C:\Users\John Smith");
            (string resolvedPath, string commandLine) = ShellCommandResolver.ResolveShellCommandLine(options);

            Assert.That(resolvedPath, Is.EqualTo(shellPath));
            Assert.That(commandLine, Is.EqualTo($"\"{shellPath}\" --cd \"C:\\Users\\John Smith\""));
        }
        finally
        {
            Directory.Delete(shellDir, recursive: true);
        }
    }

    [Test]
    public void InputQueue_WritesChunksInOrder_OnWriterThread()
    {
        var written = new List<byte[]>();
        var allWritten = new ManualResetEventSlim();
        int mainThreadId = Environment.CurrentManagedThreadId;
        int? writerThreadId = null;

        using var queue = new PtyInputQueue(
            "test input queue",
            chunk =>
            {
                writerThreadId = Environment.CurrentManagedThreadId;
                lock (written)
                {
                    written.Add(chunk);
                    if (written.Count == 3)
                    {
                        allWritten.Set();
                    }
                }
            },
            (_, _) => { });

        queue.Write("one"u8);
        queue.Write("two"u8);
        queue.Write("three"u8);

        Assert.That(allWritten.Wait(5000), Is.True, "writer thread did not drain the queue");
        lock (written)
        {
            Assert.That(written.Select(c => Encoding.UTF8.GetString(c)), Is.EqualTo(new[] { "one", "two", "three" }));
        }

        Assert.That(writerThreadId, Is.Not.Null.And.Not.EqualTo(mainThreadId),
            "writes must happen on the dedicated writer thread, never the caller's");
    }

    [Test]
    public void InputQueue_Overflow_DropsAndReportsOnce()
    {
        var blockWriter = new ManualResetEventSlim();
        var firstChunkReached = new ManualResetEventSlim();
        int errors = 0;

        var queue = new PtyInputQueue(
            "test overflow queue",
            _ =>
            {
                firstChunkReached.Set();
                blockWriter.Wait();
            },
            (_, _) => Interlocked.Increment(ref errors));

        try
        {
            // First chunk occupies the (blocked) writer; the rest accumulate as pending.
            queue.Write(new byte[1024]);
            Assert.That(firstChunkReached.Wait(5000), Is.True);

            queue.Write(new byte[PtyInputQueue.MaxPendingBytes]); // fills the budget exactly
            queue.Write(new byte[1]);                             // over budget — dropped + reported
            queue.Write(new byte[1]);                             // same episode — not re-reported

            Assert.That(errors, Is.EqualTo(1));
        }
        finally
        {
            blockWriter.Set();
            queue.Dispose();
        }
    }

    [Test]
    public void InputQueue_WriteFailure_ReportsOnceAndDropsRest()
    {
        var errorSeen = new ManualResetEventSlim();
        int writeAttempts = 0;
        int errors = 0;

        using var queue = new PtyInputQueue(
            "test failing queue",
            _ =>
            {
                Interlocked.Increment(ref writeAttempts);
                throw new ProcessWriteException("boom");
            },
            (_, _) =>
            {
                Interlocked.Increment(ref errors);
                errorSeen.Set();
            });

        queue.Write("a"u8);
        Assert.That(errorSeen.Wait(5000), Is.True);
        queue.Write("b"u8);
        queue.Write("c"u8);

        Assert.That(queue.Shutdown(5000), Is.True);
        Assert.That(writeAttempts, Is.EqualTo(1), "chunks after a fatal write error must be dropped");
        Assert.That(errors, Is.EqualTo(1));
    }
}
