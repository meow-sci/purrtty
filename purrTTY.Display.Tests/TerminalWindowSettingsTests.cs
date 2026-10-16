using NUnit.Framework;
using purrTTY.Display.Ghostty;

namespace purrTTY.Display.Tests;

/// <summary>
/// Pins the launcher's runtime-only window options on <see cref="TerminalWindowSettings"/>:
/// they default to off and are deliberately NOT part of a theme bundle (a theme save/apply
/// round-trip must neither capture nor reset them), so they never leak into saved themes or the
/// persisted new-window defaults. (The window itself cannot be constructed headlessly — it needs
/// the ImGui assemblies — so its Closed/NoResize wiring is verified in-game.)
/// </summary>
[TestFixture]
public sealed class TerminalWindowSettingsTests
{
    [Test]
    public void RuntimeOnlyOptions_DefaultToOff()
    {
        var settings = new TerminalWindowSettings();

        Assert.That(settings.NoResize, Is.False);
        Assert.That(settings.NoFocusOnAppearing, Is.False);
        Assert.That(settings.HideChromeWhenNotHovered, Is.Null);
    }

    [Test]
    public void RuntimeOnlyOptions_AreNotCapturedByAThemeSnapshot()
    {
        var source = new TerminalWindowSettings
        {
            NoResize = true,
            NoFocusOnAppearing = true,
            HideChromeWhenNotHovered = false,
        };

        var theme = source.ToThemeDefinition("snapshot");
        var target = new TerminalWindowSettings();
        target.ApplyThemeOverrides(theme);

        Assert.That(target.NoResize, Is.False);
        Assert.That(target.NoFocusOnAppearing, Is.False);
        Assert.That(target.HideChromeWhenNotHovered, Is.Null);
    }

    [Test]
    public void ApplyingATheme_LeavesRuntimeOnlyOptionsOfTheTargetUntouched()
    {
        var launched = new TerminalWindowSettings { NoResize = true, HideChromeWhenNotHovered = true };
        var theme = new TerminalWindowSettings().ToThemeDefinition("plain");

        launched.ApplyThemeOverrides(theme);

        Assert.That(launched.NoResize, Is.True);
        Assert.That(launched.HideChromeWhenNotHovered, Is.True);
    }
}
