using NUnit.Framework;

namespace purrTTY.Core.Terminal.Tests.Unit;

/// <summary>
/// Pins <see cref="TerminalLauncherRegistry"/>: <c>Changed</c> fires only when the published
/// launcher actually changes, null clears, and a throwing subscriber cannot stop the others.
/// The registry is process-wide static state, so every test restores it in <see cref="TearDown"/>.
/// </summary>
[TestFixture]
[NonParallelizable]
public class TerminalLauncherRegistryTests
{
    private readonly List<Action<ITerminalLauncher?>> _subscribed = new();

    [TearDown]
    public void TearDown()
    {
        foreach (var handler in _subscribed)
        {
            TerminalLauncherRegistry.Changed -= handler;
        }

        _subscribed.Clear();
        TerminalLauncherRegistry.Current = null;
    }

    private void Subscribe(Action<ITerminalLauncher?> handler)
    {
        _subscribed.Add(handler);
        TerminalLauncherRegistry.Changed += handler;
    }

    [Test]
    public void Current_DefaultsToNull()
    {
        Assert.That(TerminalLauncherRegistry.Current, Is.Null);
    }

    [Test]
    public void SettingCurrent_RaisesChanged_WithNewValue()
    {
        var launcher = new StubLauncher();
        var seen = new List<ITerminalLauncher?>();
        Subscribe(seen.Add);

        TerminalLauncherRegistry.Current = launcher;

        Assert.That(TerminalLauncherRegistry.Current, Is.SameAs(launcher));
        Assert.That(seen, Has.Count.EqualTo(1));
        Assert.That(seen[0], Is.SameAs(launcher));
    }

    [Test]
    public void SettingSameValueAgain_DoesNotRaiseChanged()
    {
        var launcher = new StubLauncher();
        TerminalLauncherRegistry.Current = launcher;

        int raised = 0;
        Subscribe(_ => raised++);

        TerminalLauncherRegistry.Current = launcher;

        Assert.That(raised, Is.EqualTo(0));
    }

    [Test]
    public void SettingNullWhileAlreadyNull_DoesNotRaiseChanged()
    {
        int raised = 0;
        Subscribe(_ => raised++);

        TerminalLauncherRegistry.Current = null;

        Assert.That(raised, Is.EqualTo(0));
    }

    [Test]
    public void SettingNull_ClearsCurrent_AndRaisesChangedWithNull()
    {
        TerminalLauncherRegistry.Current = new StubLauncher();
        var seen = new List<ITerminalLauncher?>();
        Subscribe(seen.Add);

        TerminalLauncherRegistry.Current = null;

        Assert.That(TerminalLauncherRegistry.Current, Is.Null);
        Assert.That(seen, Is.EqualTo(new ITerminalLauncher?[] { null }));
    }

    [Test]
    public void ReplacingLauncher_RaisesChangedForTheReplacement()
    {
        var first = new StubLauncher();
        var second = new StubLauncher();
        TerminalLauncherRegistry.Current = first;
        var seen = new List<ITerminalLauncher?>();
        Subscribe(seen.Add);

        TerminalLauncherRegistry.Current = second;

        Assert.That(TerminalLauncherRegistry.Current, Is.SameAs(second));
        Assert.That(seen, Has.Count.EqualTo(1));
        Assert.That(seen[0], Is.SameAs(second));
    }

    [Test]
    public void ThrowingHandler_DoesNotPreventOtherHandlers_OrTheSet()
    {
        var launcher = new StubLauncher();
        int before = 0;
        int after = 0;
        Subscribe(_ => before++);
        Subscribe(_ => throw new InvalidOperationException("handler failure"));
        Subscribe(_ => after++);

        Assert.DoesNotThrow(() => TerminalLauncherRegistry.Current = launcher);

        Assert.That(TerminalLauncherRegistry.Current, Is.SameAs(launcher));
        Assert.That(before, Is.EqualTo(1));
        Assert.That(after, Is.EqualTo(1));
    }

    [Test]
    public void UnsubscribedHandler_IsNotInvoked()
    {
        int raised = 0;
        void Handler(ITerminalLauncher? _) => raised++;
        TerminalLauncherRegistry.Changed += Handler;
        TerminalLauncherRegistry.Changed -= Handler;

        TerminalLauncherRegistry.Current = new StubLauncher();

        Assert.That(raised, Is.EqualTo(0));
    }

    [Test]
    public void HandlerMayReadAndSetCurrent_WithoutDeadlock()
    {
        var first = new StubLauncher();
        var second = new StubLauncher();
        ITerminalLauncher? observedInHandler = null;
        Subscribe(value =>
        {
            observedInHandler = TerminalLauncherRegistry.Current;
            if (ReferenceEquals(value, first))
            {
                // Re-entrant set from inside a handler: handlers run outside the lock.
                TerminalLauncherRegistry.Current = second;
            }
        });

        TerminalLauncherRegistry.Current = first;

        Assert.That(TerminalLauncherRegistry.Current, Is.SameAs(second));
        Assert.That(observedInHandler, Is.SameAs(second));
    }

    private sealed class StubLauncher : ITerminalLauncher
    {
        public bool IsAvailable => true;

        public IReadOnlyList<ITerminalHandle> Terminals { get; } = Array.Empty<ITerminalHandle>();

        public event Action<ITerminalHandle, TerminalLifecycleEvent>? Lifecycle
        {
            add { }
            remove { }
        }

        public Task<ITerminalHandle> LaunchAsync(TerminalLaunchRequest request, CancellationToken ct = default)
            => Task.FromException<ITerminalHandle>(new NotSupportedException());

        public ITerminalHandle? Find(string name) => null;
    }
}
