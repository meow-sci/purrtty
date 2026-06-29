using NUnit.Framework;
using purrTTY.Display.Ghostty;
using purrTTY.Display.Theming;
using PurrTTY.Terminal.Rendering;

namespace purrTTY.Display.Tests;

/// <summary>
/// Pins the theme TOML format: a full user-theme round-trip (colors + the
/// optional [font]/[window]/[cursor]/[focus]/[lock]/[meta] sections), the
/// "absent means keep-current" convention for colors-only files, and that every
/// bundled color scheme that ships with the mod actually parses.
/// </summary>
[TestFixture]
public sealed class ThemeTomlFormatTests
{
    private string _dir = null!;

    [SetUp]
    public void Setup() => _dir = Directory.CreateTempSubdirectory("purrtty-theme-tests").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, recursive: true);

    private static ThemeColors SampleColors()
    {
        var colors = new ThemeColors
        {
            Foreground = new RgbaColor(0xAA, 0xBB, 0xCC),
            Background = new RgbaColor(0x10, 0x20, 0x30),
            Cursor = new RgbaColor(0xFF, 0x00, 0xFF),
            SelectionBackground = new RgbaColor(0x33, 0x55, 0x88),
        };
        for (int i = 0; i < 16; i++)
        {
            colors.Ansi[i] = new RgbaColor((byte)(i * 10), (byte)(255 - i * 10), (byte)i);
        }

        return colors;
    }

    [Test]
    public void SaveThenLoad_FullUserTheme_RoundTripsEveryField()
    {
        string path = Path.Combine(_dir, "full.toml");
        var theme = new ThemeDefinition
        {
            Name = "Round Trip",
            Source = ThemeSource.UserFile,
            Colors = SampleColors(),
            FontFamily = "Hack",
            FontSize = 28f,
            BackgroundOpacity = 0.9f,
            ForegroundOpacity = 0.8f,
            CellBackgroundOpacity = 0.7f,
            CursorStyle = CursorShape.Underline,
            CursorBlink = false,
            BorderOnFocus = true,
            BorderOnHover = false,
            BorderOpacity = 0.55f,
            LockMode = true,
            HotZoneEnabled = false,
            HotZonePlacement = HotZonePlacement.BottomLeft,
            HotZoneWidth = 64f,
            HotZoneHeight = 24f,
            HotZoneColor = new RgbaColor(0x12, 0x34, 0x56),
            HotZoneOpacity = 0.4f,
            HotZoneHoverOpacity = 0.95f,
        };

        ThemeTomlFormat.Save(path, theme);
        var loaded = ThemeTomlFormat.Load(path, ThemeSource.UserFile);

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Name, Is.EqualTo("Round Trip"), "[meta] name carries the display name");
        Assert.That(loaded.Colors.Foreground, Is.EqualTo(theme.Colors.Foreground));
        Assert.That(loaded.Colors.Background, Is.EqualTo(theme.Colors.Background));
        Assert.That(loaded.Colors.Cursor, Is.EqualTo(theme.Colors.Cursor));
        Assert.That(loaded.Colors.SelectionBackground, Is.EqualTo(theme.Colors.SelectionBackground));
        Assert.That(loaded.Colors.Ansi, Is.EqualTo(theme.Colors.Ansi));

        Assert.That(loaded.FontFamily, Is.EqualTo("Hack"));
        Assert.That(loaded.FontSize, Is.EqualTo(28f));
        Assert.That(loaded.BackgroundOpacity, Is.EqualTo(0.9f).Within(0.001f));
        Assert.That(loaded.ForegroundOpacity, Is.EqualTo(0.8f).Within(0.001f));
        Assert.That(loaded.CellBackgroundOpacity, Is.EqualTo(0.7f).Within(0.001f));
        Assert.That(loaded.CursorStyle, Is.EqualTo(CursorShape.Underline));
        Assert.That(loaded.CursorBlink, Is.False);
        Assert.That(loaded.BorderOnFocus, Is.True);
        Assert.That(loaded.BorderOnHover, Is.False);
        Assert.That(loaded.BorderOpacity, Is.EqualTo(0.55f).Within(0.001f));
        Assert.That(loaded.LockMode, Is.True);
        Assert.That(loaded.HotZoneEnabled, Is.False);
        Assert.That(loaded.HotZonePlacement, Is.EqualTo(HotZonePlacement.BottomLeft));
        Assert.That(loaded.HotZoneWidth, Is.EqualTo(64f).Within(0.001f));
        Assert.That(loaded.HotZoneHeight, Is.EqualTo(24f).Within(0.001f));
        Assert.That(loaded.HotZoneColor, Is.EqualTo(new RgbaColor(0x12, 0x34, 0x56)));
        Assert.That(loaded.HotZoneOpacity, Is.EqualTo(0.4f).Within(0.001f));
        Assert.That(loaded.HotZoneHoverOpacity, Is.EqualTo(0.95f).Within(0.001f));
    }

    [Test]
    public void Load_ColorsOnlyTheme_LeavesDisplaySettingsNull()
    {
        // Bundled themes define only colors; every display setting must come
        // back null ("keep the window's current value" when applied).
        string path = Path.Combine(_dir, "colors-only.toml");
        ThemeTomlFormat.Save(path, new ThemeDefinition
        {
            Name = "Colors Only",
            Source = ThemeSource.UserFile,
            Colors = SampleColors(),
            // no optional fields
        });

        // Saving a definition with null optionals must not write the sections;
        // strip any [meta] too by re-saving... simpler: assert directly.
        var loaded = ThemeTomlFormat.Load(path, ThemeSource.BuiltIn);

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.FontFamily, Is.Null);
        Assert.That(loaded.FontSize, Is.Null);
        Assert.That(loaded.BackgroundOpacity, Is.Null);
        Assert.That(loaded.CursorStyle, Is.Null);
        Assert.That(loaded.CursorBlink, Is.Null);
        Assert.That(loaded.BorderOnFocus, Is.Null);
        Assert.That(loaded.LockMode, Is.Null);
        Assert.That(loaded.HotZonePlacement, Is.Null);
        Assert.That(loaded.HotZoneColor, Is.Null);
    }

    [Test]
    public void BundledThemes_AllParse()
    {
        // The schemes shipped in the mod zip (linked into the test output from
        // purrTTY.GameMod/TerminalThemes) — a malformed one would otherwise ship
        // undetected and silently vanish from the Theme menu.
        string themesDir = Path.Combine(TestContext.CurrentContext.TestDirectory, "TerminalThemes");
        var files = Directory.GetFiles(themesDir, "*.toml");

        Assert.That(files, Has.Length.EqualTo(18), "expected the 18 bundled color schemes");

        foreach (string file in files)
        {
            var theme = ThemeTomlFormat.Load(file, ThemeSource.BuiltIn);
            Assert.That(theme, Is.Not.Null, $"bundled theme failed to parse: {Path.GetFileName(file)}");
            Assert.That(theme!.Name, Is.Not.Empty);
            Assert.That(theme.Colors.Background, Is.Not.EqualTo(theme.Colors.Foreground),
                $"degenerate colors in {Path.GetFileName(file)} suggest a parse fallback");
        }
    }

    [Test]
    public void ApplyThemeOverrides_AbsentFieldsKeepCurrentValues()
    {
        // The keep-current contract end to end: applying a colors-only theme to
        // live window settings must not disturb any display setting.
        var settings = new TerminalWindowSettings
        {
            FontFamily = "CustomFont",
            FontSize = 20f,
            LockMode = true,
            HotZoneWidth = 100f,
            CursorStyle = CursorShape.Bar,
        };

        settings.ApplyThemeOverrides(new ThemeDefinition
        {
            Name = "Colors Only",
            Colors = SampleColors(),
        });

        Assert.That(settings.FontFamily, Is.EqualTo("CustomFont"));
        Assert.That(settings.FontSize, Is.EqualTo(20f));
        Assert.That(settings.LockMode, Is.True);
        Assert.That(settings.HotZoneWidth, Is.EqualTo(100f));
        Assert.That(settings.CursorStyle, Is.EqualTo(CursorShape.Bar));
    }

    [Test]
    public void Settings_SnapshotThenSaveLoadApply_RoundTripsEveryDisplayField()
    {
        // The window-level "save current as theme" path, exercised at the testable
        // settings layer: TerminalWindowSettings.ToThemeDefinition (what
        // TerminalWindow.SnapshotAsTheme delegates to) → TOML → fresh settings.
        // A display field added to TerminalWindowSettings but forgotten in
        // ToThemeDefinition or ApplyThemeOverrides would drop silently — this guards
        // the "a theme encompasses everything" contract end to end.
        var original = new TerminalWindowSettings
        {
            Colors = SampleColors(),
            FontFamily = "JetBrains Mono",
            FontSize = 24f,
            BackgroundOpacity = 0.85f,
            ForegroundOpacity = 0.75f,
            CellBackgroundOpacity = 0.65f,
            CursorStyle = CursorShape.Bar,
            CursorBlink = false,
            BorderOnFocus = true,
            BorderOnHover = true,
            BorderOpacity = 0.42f,
            LockMode = true,
            HotZoneEnabled = false,
            HotZonePlacement = HotZonePlacement.MiddleRight,
            HotZoneWidth = 40f,
            HotZoneHeight = 36f,
            HotZoneColor = new RgbaColor(0x9A, 0xBC, 0xDE),
            HotZoneOpacity = 0.3f,
            HotZoneHoverOpacity = 0.7f,
        };

        string path = Path.Combine(_dir, "snapshot.toml");
        ThemeTomlFormat.Save(path, original.ToThemeDefinition("Snapshot"));
        var loaded = ThemeTomlFormat.Load(path, ThemeSource.UserFile);
        Assert.That(loaded, Is.Not.Null);

        // Reproduce ApplyTheme's split: colors are assigned by the window, the
        // optional display fields by ApplyThemeOverrides.
        var restored = new TerminalWindowSettings { Colors = loaded!.Colors.Clone() };
        restored.ApplyThemeOverrides(loaded);

        Assert.That(restored.Colors.Foreground, Is.EqualTo(original.Colors.Foreground));
        Assert.That(restored.Colors.Background, Is.EqualTo(original.Colors.Background));
        Assert.That(restored.Colors.Cursor, Is.EqualTo(original.Colors.Cursor));
        Assert.That(restored.Colors.SelectionBackground, Is.EqualTo(original.Colors.SelectionBackground));
        Assert.That(restored.Colors.Ansi, Is.EqualTo(original.Colors.Ansi));
        Assert.That(restored.FontFamily, Is.EqualTo(original.FontFamily));
        Assert.That(restored.FontSize, Is.EqualTo(original.FontSize).Within(0.001f));
        Assert.That(restored.BackgroundOpacity, Is.EqualTo(original.BackgroundOpacity).Within(0.001f));
        Assert.That(restored.ForegroundOpacity, Is.EqualTo(original.ForegroundOpacity).Within(0.001f));
        Assert.That(restored.CellBackgroundOpacity, Is.EqualTo(original.CellBackgroundOpacity).Within(0.001f));
        Assert.That(restored.CursorStyle, Is.EqualTo(original.CursorStyle));
        Assert.That(restored.CursorBlink, Is.EqualTo(original.CursorBlink));
        Assert.That(restored.BorderOnFocus, Is.EqualTo(original.BorderOnFocus));
        Assert.That(restored.BorderOnHover, Is.EqualTo(original.BorderOnHover));
        Assert.That(restored.BorderOpacity, Is.EqualTo(original.BorderOpacity).Within(0.001f));
        Assert.That(restored.LockMode, Is.EqualTo(original.LockMode));
        Assert.That(restored.HotZoneEnabled, Is.EqualTo(original.HotZoneEnabled));
        Assert.That(restored.HotZonePlacement, Is.EqualTo(original.HotZonePlacement));
        Assert.That(restored.HotZoneWidth, Is.EqualTo(original.HotZoneWidth).Within(0.001f));
        Assert.That(restored.HotZoneHeight, Is.EqualTo(original.HotZoneHeight).Within(0.001f));
        Assert.That(restored.HotZoneColor, Is.EqualTo(original.HotZoneColor));
        Assert.That(restored.HotZoneOpacity, Is.EqualTo(original.HotZoneOpacity).Within(0.001f));
        Assert.That(restored.HotZoneHoverOpacity, Is.EqualTo(original.HotZoneHoverOpacity).Within(0.001f));
    }

    [Test]
    public void ApplyThemeOverrides_ClampsOutOfRangeValues()
    {
        var settings = new TerminalWindowSettings();

        settings.ApplyThemeOverrides(new ThemeDefinition
        {
            Name = "Hostile",
            Colors = SampleColors(),
            BackgroundOpacity = 4f,
            BorderOpacity = -1f,
            HotZoneWidth = 99999f,
            HotZoneHeight = 0f,
        });

        Assert.That(settings.BackgroundOpacity, Is.EqualTo(1f));
        Assert.That(settings.BorderOpacity, Is.EqualTo(0f));
        Assert.That(settings.HotZoneWidth, Is.EqualTo(TerminalWindow.MaxHotZoneSize));
        Assert.That(settings.HotZoneHeight, Is.EqualTo(TerminalWindow.MinHotZoneSize));
    }
}
