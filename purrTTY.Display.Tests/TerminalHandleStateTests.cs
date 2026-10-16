using NUnit.Framework;
using purrTTY.Core.Terminal;
using purrTTY.Display.Launcher;

namespace purrTTY.Display.Tests;

/// <summary>
/// Pins <see cref="TerminalHandleState"/>, the pure state machine that decides which lifecycle
/// events a launcher terminal handle raises: one-shot events fire once, transitions-only events
/// fire on change, and nothing fires after Closed.
/// </summary>
[TestFixture]
public sealed class TerminalHandleStateTests
{
    private static readonly TerminalLifecycleEvent[] None = [];

    private static TerminalHandleState Open()
    {
        var state = new TerminalHandleState();
        state.MarkCreated();
        return state;
    }

    // ---- initial state ----

    [Test]
    public void NewState_IsEmpty()
    {
        var state = new TerminalHandleState();

        Assert.That(state.IsOpen, Is.False);
        Assert.That(state.IsRunning, Is.False);
        Assert.That(state.ExitCode, Is.Null);
        Assert.That(state.Cols, Is.EqualTo(0));
        Assert.That(state.Rows, Is.EqualTo(0));
        Assert.That(state.Focused, Is.False);
    }

    // ---- Created ----

    [Test]
    public void MarkCreated_RaisesCreatedOnce_AndOpens()
    {
        var state = new TerminalHandleState();

        Assert.That(state.MarkCreated(), Is.EqualTo(new[] { TerminalLifecycleEvent.Created }));
        Assert.That(state.IsOpen, Is.True);
        Assert.That(state.MarkCreated(), Is.EqualTo(None));
    }

    // ---- Started ----

    [Test]
    public void MarkStarted_RaisesStartedOnce_AndSetsRunning()
    {
        var state = Open();

        Assert.That(state.MarkStarted(), Is.EqualTo(new[] { TerminalLifecycleEvent.Started }));
        Assert.That(state.IsRunning, Is.True);
        Assert.That(state.MarkStarted(), Is.EqualTo(None));
        Assert.That(state.IsRunning, Is.True);
    }

    [Test]
    public void MarkStarted_AfterExited_IsIgnored()
    {
        var state = Open();
        state.MarkExited(1);

        Assert.That(state.MarkStarted(), Is.EqualTo(None));
        Assert.That(state.IsRunning, Is.False);
    }

    [Test]
    public void MarkStarted_AfterClosed_IsIgnored()
    {
        var state = Open();
        state.MarkClosed();

        Assert.That(state.MarkStarted(), Is.EqualTo(None));
        Assert.That(state.IsRunning, Is.False);
    }

    // ---- Focus ----

    [Test]
    public void ObserveFocus_RaisesOnlyOnTransitions()
    {
        var state = Open();

        Assert.That(state.ObserveFocus(false), Is.EqualTo(None), "already unfocused");
        Assert.That(state.ObserveFocus(true), Is.EqualTo(new[] { TerminalLifecycleEvent.Focused }));
        Assert.That(state.Focused, Is.True);
        Assert.That(state.ObserveFocus(true), Is.EqualTo(None), "no repeat");
        Assert.That(state.ObserveFocus(false), Is.EqualTo(new[] { TerminalLifecycleEvent.Unfocused }));
        Assert.That(state.Focused, Is.False);
        Assert.That(state.ObserveFocus(false), Is.EqualTo(None));
    }

    [Test]
    public void ObserveFocus_BeforeCreated_IsIgnored()
    {
        var state = new TerminalHandleState();

        Assert.That(state.ObserveFocus(true), Is.EqualTo(None));
        Assert.That(state.Focused, Is.False);
    }

    [Test]
    public void ObserveFocus_AfterClosed_IsIgnored()
    {
        var state = Open();
        state.MarkClosed();

        Assert.That(state.ObserveFocus(true), Is.EqualTo(None));
        Assert.That(state.Focused, Is.False);
    }

    // ---- Grid ----

    [Test]
    public void ObserveGrid_FirstObservation_RecordsWithoutEvent()
    {
        var state = Open();

        Assert.That(state.ObserveGrid(80, 24), Is.EqualTo(None));
        Assert.That(state.Cols, Is.EqualTo(80));
        Assert.That(state.Rows, Is.EqualTo(24));
    }

    [Test]
    public void ObserveGrid_RaisesResizedOnlyWhenTheSizeChanges()
    {
        var state = Open();
        state.ObserveGrid(80, 24);

        Assert.That(state.ObserveGrid(80, 24), Is.EqualTo(None), "unchanged");
        Assert.That(state.ObserveGrid(100, 24), Is.EqualTo(new[] { TerminalLifecycleEvent.Resized }), "cols changed");
        Assert.That(state.ObserveGrid(100, 30), Is.EqualTo(new[] { TerminalLifecycleEvent.Resized }), "rows changed");
        Assert.That(state.ObserveGrid(100, 30), Is.EqualTo(None));
        Assert.That(state.Cols, Is.EqualTo(100));
        Assert.That(state.Rows, Is.EqualTo(30));
    }

    [TestCase(0, 0)]
    [TestCase(0, 24)]
    [TestCase(80, 0)]
    [TestCase(-1, -1)]
    public void ObserveGrid_NonPositiveSize_IsIgnored(int cols, int rows)
    {
        var state = Open();
        state.ObserveGrid(80, 24);

        Assert.That(state.ObserveGrid(cols, rows), Is.EqualTo(None));
        Assert.That(state.Cols, Is.EqualTo(80));
        Assert.That(state.Rows, Is.EqualTo(24));
    }

    [Test]
    public void ObserveGrid_NonPositiveFirstObservation_DoesNotCountAsTheFirstGrid()
    {
        var state = Open();

        Assert.That(state.ObserveGrid(0, 0), Is.EqualTo(None));
        Assert.That(state.ObserveGrid(80, 24), Is.EqualTo(None), "this is still the first real observation");
        Assert.That(state.ObserveGrid(81, 24), Is.EqualTo(new[] { TerminalLifecycleEvent.Resized }));
    }

    [Test]
    public void ObserveGrid_BeforeCreated_IsIgnored()
    {
        var state = new TerminalHandleState();

        Assert.That(state.ObserveGrid(80, 24), Is.EqualTo(None));
        Assert.That(state.Cols, Is.EqualTo(0));
    }

    [Test]
    public void ObserveGrid_AfterClosed_IsIgnored()
    {
        var state = Open();
        state.ObserveGrid(80, 24);
        state.MarkClosed();

        Assert.That(state.ObserveGrid(120, 40), Is.EqualTo(None));
        Assert.That(state.Cols, Is.EqualTo(80));
    }

    // ---- Exited ----

    [Test]
    public void MarkExited_RaisesExitedOnce_RecordsCode_AndStopsRunning()
    {
        var state = Open();
        state.MarkStarted();

        Assert.That(state.MarkExited(3), Is.EqualTo(new[] { TerminalLifecycleEvent.Exited }));
        Assert.That(state.IsRunning, Is.False);
        Assert.That(state.ExitCode, Is.EqualTo(3));
        Assert.That(state.MarkExited(9), Is.EqualTo(None), "second exit ignored");
        Assert.That(state.ExitCode, Is.EqualTo(3), "first exit code wins");
    }

    [Test]
    public void MarkExited_WithUnknownCode_LeavesExitCodeNull()
    {
        var state = Open();
        state.MarkStarted();

        Assert.That(state.MarkExited(null), Is.EqualTo(new[] { TerminalLifecycleEvent.Exited }));
        Assert.That(state.ExitCode, Is.Null);
    }

    [Test]
    public void MarkExited_WithoutStarted_StillRaisesExited()
    {
        // A start failure exits without ever starting.
        var state = Open();

        Assert.That(state.MarkExited(null), Is.EqualTo(new[] { TerminalLifecycleEvent.Exited }));
    }

    [Test]
    public void MarkExited_AfterClosed_IsIgnored()
    {
        var state = Open();
        state.MarkStarted();
        state.MarkClosed();

        Assert.That(state.MarkExited(0), Is.EqualTo(None));
        Assert.That(state.ExitCode, Is.Null);
    }

    // ---- Closed ----

    [Test]
    public void MarkClosed_RaisesClosedOnce_AndClearsState()
    {
        var state = Open();
        state.MarkStarted();
        state.ObserveFocus(true);

        Assert.That(state.MarkClosed(), Is.EqualTo(new[] { TerminalLifecycleEvent.Closed }));
        Assert.That(state.IsOpen, Is.False);
        Assert.That(state.IsRunning, Is.False);
        Assert.That(state.Focused, Is.False);
        Assert.That(state.MarkClosed(), Is.EqualTo(None));
    }

    [Test]
    public void MarkClosed_DoesNotImplyExited()
    {
        var state = Open();
        state.MarkStarted();

        Assert.That(state.MarkClosed(), Is.EqualTo(new[] { TerminalLifecycleEvent.Closed }));
        Assert.That(state.ExitCode, Is.Null);
    }

    [Test]
    public void EveryMethodAfterClosed_ReturnsEmpty()
    {
        var state = Open();
        state.MarkClosed();

        Assert.That(state.MarkCreated(), Is.EqualTo(None));
        Assert.That(state.MarkStarted(), Is.EqualTo(None));
        Assert.That(state.ObserveFocus(true), Is.EqualTo(None));
        Assert.That(state.ObserveGrid(80, 24), Is.EqualTo(None));
        Assert.That(state.MarkExited(0), Is.EqualTo(None));
        Assert.That(state.MarkClosed(), Is.EqualTo(None));
    }

    // ---- a realistic sequence ----

    [Test]
    public void TypicalLifecycle_ProducesTheExpectedEventSequence()
    {
        var state = new TerminalHandleState();
        var seen = new List<TerminalLifecycleEvent>();

        seen.AddRange(state.MarkCreated());
        seen.AddRange(state.ObserveGrid(40, 12));
        seen.AddRange(state.MarkStarted());
        seen.AddRange(state.ObserveFocus(true));
        seen.AddRange(state.ObserveGrid(40, 12));
        seen.AddRange(state.ObserveGrid(60, 12));
        seen.AddRange(state.ObserveFocus(false));
        seen.AddRange(state.MarkExited(0));
        seen.AddRange(state.MarkClosed());

        Assert.That(seen, Is.EqualTo(new[]
        {
            TerminalLifecycleEvent.Created,
            TerminalLifecycleEvent.Started,
            TerminalLifecycleEvent.Focused,
            TerminalLifecycleEvent.Resized,
            TerminalLifecycleEvent.Unfocused,
            TerminalLifecycleEvent.Exited,
            TerminalLifecycleEvent.Closed,
        }));
    }
}
