using NUnit.Framework;
using purrTTY.Display.Ghostty;
using purrTTY.Display.Theming;

namespace purrTTY.Display.Tests;

/// <summary>
/// Pins the named-terminal registry: unique (case-insensitive) names, auto-naming,
/// rename rules, focus resolution, and prune-on-unregister. A fake terminal stands
/// in for the real <see cref="TerminalWindow"/>/in-world instances (which need ImGui
/// and GPU resources), exercising the same <see cref="INamedTerminal"/> contract.
/// </summary>
[TestFixture]
public sealed class TerminalTargetRegistryTests
{
    private sealed class FakeTerminal : INamedTerminal
    {
        public string Name { get; private set; }
        public TerminalKind Kind { get; }
        public bool HasFocus { get; set; }

        public FakeTerminal(string name, TerminalKind kind = TerminalKind.Window)
        {
            Name = name;
            Kind = kind;
        }

        public void ApplyTheme(ThemeDefinition theme) { }

        public bool TrySetGridSize(int cols, int rows) => Kind == TerminalKind.InWorld;

        public bool TryRename(string newName)
        {
            string trimmed = newName.Trim();
            if (!TerminalTargetRegistry.IsNameAvailable(trimmed, this))
            {
                return false;
            }

            Name = trimmed;
            return true;
        }
    }

    // The registry is a process-wide static; clear it around every test so run order
    // can't leak registrations between cases.
    [SetUp]
    [TearDown]
    public void Clear()
    {
        foreach (var terminal in TerminalTargetRegistry.All.ToList())
        {
            TerminalTargetRegistry.Unregister(terminal);
        }
    }

    [Test]
    public void Register_RejectsDuplicateAndBlankNames()
    {
        var first = new FakeTerminal("Terminal");
        Assert.That(TerminalTargetRegistry.Register(first), Is.True);

        // Case-insensitive clash with an existing name.
        Assert.That(TerminalTargetRegistry.Register(new FakeTerminal("terminal")), Is.False);
        // Blank / whitespace name.
        Assert.That(TerminalTargetRegistry.Register(new FakeTerminal("   ")), Is.False);

        Assert.That(TerminalTargetRegistry.All, Has.Count.EqualTo(1));
    }

    [Test]
    public void SuggestUniqueName_AvoidsCollisions()
    {
        Assert.That(TerminalTargetRegistry.SuggestUniqueName("Terminal"), Is.EqualTo("Terminal"));

        TerminalTargetRegistry.Register(new FakeTerminal("Terminal"));
        Assert.That(TerminalTargetRegistry.SuggestUniqueName("Terminal"), Is.EqualTo("Terminal 2"));

        TerminalTargetRegistry.Register(new FakeTerminal("Terminal 2"));
        Assert.That(TerminalTargetRegistry.SuggestUniqueName("Terminal"), Is.EqualTo("Terminal 3"));
    }

    [Test]
    public void TryRename_AllowsFreeOrSelf_RejectsTakenOrBlank()
    {
        var alpha = new FakeTerminal("Alpha");
        var beta = new FakeTerminal("Beta");
        TerminalTargetRegistry.Register(alpha);
        TerminalTargetRegistry.Register(beta);

        Assert.That(beta.TryRename("Gamma"), Is.True);
        Assert.That(beta.Name, Is.EqualTo("Gamma"));

        Assert.That(beta.TryRename("Alpha"), Is.False, "name taken by another terminal");
        Assert.That(beta.Name, Is.EqualTo("Gamma"), "rejected rename keeps the current name");

        Assert.That(beta.TryRename("  "), Is.False, "blank rename rejected");

        Assert.That(beta.TryRename("gamma"), Is.True, "renaming to its own name (case variant) is allowed");
        Assert.That(TerminalTargetRegistry.Resolve("ALPHA"), Is.SameAs(alpha));
    }

    [Test]
    public void Focused_ReturnsFocusedTerminalOrNull()
    {
        var alpha = new FakeTerminal("Alpha");
        var beta = new FakeTerminal("Beta", TerminalKind.InWorld);
        TerminalTargetRegistry.Register(alpha);
        TerminalTargetRegistry.Register(beta);

        Assert.That(TerminalTargetRegistry.Focused, Is.Null);

        beta.HasFocus = true;
        Assert.That(TerminalTargetRegistry.Focused, Is.SameAs(beta));
    }

    [Test]
    public void Unregister_PrunesAndFreesName()
    {
        var solo = new FakeTerminal("Solo");
        TerminalTargetRegistry.Register(solo);
        Assert.That(TerminalTargetRegistry.Resolve("Solo"), Is.SameAs(solo));

        TerminalTargetRegistry.Unregister(solo);

        Assert.That(TerminalTargetRegistry.All, Is.Empty);
        Assert.That(TerminalTargetRegistry.IsNameAvailable("Solo"), Is.True);
    }
}
