using System.Diagnostics;
using NUnit.Framework;
using PurrTTY.Terminal.Ghostty;

namespace PurrTTY.Terminal.Tests;

/// <summary>
/// The inbox backpressure contract (gotcha 18): a pump thread whose write would overflow the
/// 8 MiB inbox WAITS for the tick to drain instead of shearing the byte stream — a mid-stream
/// drop severs kitty APC starts and prints the remaining base64 into cells (seen live with
/// 1440x900 gatOS screen-stream frames, ~6.9 MB per unit). The tick thread itself never waits
/// (a custom shell may emit output from it), and a write that could never fit falls straight
/// through to the legacy drop+CAN/ST path.
/// </summary>
[TestFixture]
public sealed class InboxBackpressureTests
{
    private const int OneMiB = 1024 * 1024;

    [Test]
    public void PumpWriter_BurstBeyondTheCap_IsLosslessWhileTicking()
    {
        using var surface = new GhosttyTerminalSurface(80, 24);
        surface.BuildFrame(); // registers this thread as the tick thread

        // 24 MiB total — three times the inbox cap. Without backpressure the pump
        // outruns the tick and bytes shear; with it every write parks until drained.
        var chunk = new byte[OneMiB];
        Array.Fill(chunk, (byte)'x');
        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 24; i++)
            {
                surface.Write(chunk);
            }
        });

        var deadline = Stopwatch.GetTimestamp();
        while (!writer.IsCompleted)
        {
            surface.BuildFrame();
            Assert.That(Stopwatch.GetElapsedTime(deadline).TotalSeconds, Is.LessThan(30), "writer stuck");
        }

        writer.GetAwaiter().GetResult(); // surface any writer exception
        surface.BuildFrame();
        Assert.That(surface.LastFrameStats.InboxDropTotal, Is.EqualTo(0),
            "a ticking surface must never drop pump-thread bytes (no APC shearing)");
    }

    [Test]
    public void OversizedSingleWrite_SkipsTheWait_AndDrops()
    {
        using var surface = new GhosttyTerminalSurface(80, 24);
        surface.BuildFrame();

        // Larger than the whole inbox: waiting could never help, so the write must
        // return promptly via the legacy drop path instead of burning the budget.
        var oversized = new byte[GhosttyTerminalSurface.MaxInboxBytes + OneMiB];
        Array.Fill(oversized, (byte)'x');

        var sw = Stopwatch.StartNew();
        Task.Run(() => surface.Write(oversized)).GetAwaiter().GetResult();
        sw.Stop();

        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(400), "must not wait out the backpressure budget");
        surface.BuildFrame();
        Assert.That(surface.LastFrameStats.InboxDropTotal, Is.EqualTo(1));
    }

    [Test]
    public void TickThreadWriter_NeverWaits_DropsLikeBefore()
    {
        using var surface = new GhosttyTerminalSurface(80, 24);
        surface.BuildFrame(); // this thread IS the tick thread now

        // Fill to the cap, then overflow — from the tick thread. Waiting here would
        // self-deadlock (the drain runs on this very thread), so it must fall through
        // to the drop path immediately.
        var big = new byte[GhosttyTerminalSurface.MaxInboxBytes - OneMiB];
        Array.Fill(big, (byte)'x');
        surface.Write(big);

        var sw = Stopwatch.StartNew();
        surface.Write(big); // twice (cap - 1 MiB) > the cap
        sw.Stop();

        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(400), "the tick thread must never park in Write");
        surface.BuildFrame();
        Assert.That(surface.LastFrameStats.InboxDropTotal, Is.EqualTo(1));
    }
}
