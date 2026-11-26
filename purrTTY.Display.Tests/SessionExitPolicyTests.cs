using NUnit.Framework;
using purrTTY.Display.Ghostty;

namespace purrTTY.Display.Tests;

/// <summary>
/// Pins the close-on-exit host decision shared by the 2D window and the in-world
/// renderer (the ImGui-bound callers are not testable headlessly; the session-side
/// close is covered against real processes in <c>purrTTY.Terminal.Tests</c>'s
/// <c>SessionExitTests</c>).
/// </summary>
[TestFixture]
public sealed class SessionExitPolicyTests
{
    [Test]
    public void Retires_WhenTheLastSessionClosedOnExit()
        => Assert.That(SessionExitPolicy.ShouldRetireHost(closedThisTick: 1, remainingSessions: 0), Is.True);

    [Test]
    public void KeepsHost_WhenOtherTabsRemain()
        => Assert.That(SessionExitPolicy.ShouldRetireHost(closedThisTick: 1, remainingSessions: 2), Is.False);

    [Test]
    public void KeepsHost_WhenNothingClosedThisTick()
    {
        // An empty host that closed nothing is starting, or failed to start (it shows
        // its error text) — never retired by the exit path.
        Assert.That(SessionExitPolicy.ShouldRetireHost(closedThisTick: 0, remainingSessions: 0), Is.False);
        Assert.That(SessionExitPolicy.ShouldRetireHost(closedThisTick: 0, remainingSessions: 1), Is.False);
    }

    [Test]
    public void Retires_WhenSeveralTabsClosedAtOnce_LeavingNone()
        => Assert.That(SessionExitPolicy.ShouldRetireHost(closedThisTick: 3, remainingSessions: 0), Is.True);
}
