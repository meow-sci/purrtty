using Brutal.ImGuiApi;
using Brutal.Numerics;
using purrTTY.Display.Configuration;
using purrTTY.Display.Controllers;
using purrTTY.Display.Rendering;
using purrTTY.Display.Theming;
using purrTTY.Logging;
using PurrTTY.Terminal.Input;
using PurrTTY.Terminal.Rendering;
using PurrTTY.Terminal.Sessions;
using TerminalTheme = PurrTTY.Terminal.TerminalTheme;

namespace purrTTY.Display.Ghostty;

/// <summary>
/// Renders a <b>dedicated</b> terminal session into whatever ImGui draw list is
/// current — used by the in-world (render-to-texture) quad to draw a real shell
/// into an off-screen target. It is the content half of the in-world feature: it
/// owns its own <see cref="SessionManager"/> (one shell, ticked here), resolves
/// fonts/metrics, and draws the grid through the same renderer-neutral
/// <see cref="FrameGridRenderer"/> the 2D <see cref="TerminalWindow"/> uses.
///
/// <para>
/// This is the deliberate counterpart to <see cref="TerminalWindow"/>: it reuses
/// the rendering <em>pieces</em> (<see cref="FrameGridRenderer"/>,
/// <see cref="FrameFonts"/>, the session machinery) without the window's chrome,
/// tabs, geometry, hot-zone, or main-context focus model. Because the in-world
/// terminal is a self-contained dedicated session, there is no visible 2D window.
/// </para>
///
/// <para>
/// Threading: <see cref="BuildUi"/> ticks the surface (<c>BuildFrame</c>) and must
/// be called on the single game/render thread that owns the ImGui contexts — the
/// in-world manager calls it from <c>OnAfterGui</c> with the secondary ImGui
/// context current. Input/focus is wired in a later phase; for now the terminal
/// renders unfocused (steady hollow cursor).
/// </para>
/// </summary>
public sealed class InWorldTerminalRenderer : IDisposable
{
    private readonly TerminalWindowSettings _settings;
    private readonly SessionManager _sessions;
    private TerminalTheme _engineTheme;
    private RgbaColor _selectionColor;

    // Why the shell could not start, shown in the texture instead of a blank
    // panel. Written from the session-creation pool thread.
    private volatile string? _sessionStartError;

    // Lean cached font resolution. Unlike TerminalWindow we skip the ASCII
    // run-batch validation (the in-world grid uses per-cell placement via the
    // simple FrameFonts ctor — always correct, a few more draw calls). Recomputed
    // when the family/size changes or more fonts finish loading.
    private FrameFonts _fonts;
    private bool _hasFonts;
    private string? _cachedFontFamily;
    private float _cachedFontSize;
    private int _cachedLoadedFontCount;
    private float _cellWidth = 8f;
    private float _cellHeight = 16f;

    // App-mouse (Phase 9): the grid's pixel extent (from the last BuildUi) used to
    // map a quad-hit UV to a cell, plus per-button press-gating and last-cell
    // tracking, mirroring TerminalWindow's app-mouse path.
    private float _gridPixelWidth;
    private float _gridPixelHeight;
    private int _appMouseCol = -1;
    private int _appMouseRow = -1;
    private readonly bool[] _appMousePressSent = new bool[3];

    private bool _disposed;

    public InWorldTerminalRenderer(ThemeConfiguration config, ThemeCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(catalog);

        _settings = BuildSettings(config, catalog);
        _engineTheme = _settings.Colors.ToEngineTheme();
        _selectionColor = _settings.Colors.SelectionBackground.WithAlpha(0xAA);

        // A dedicated session manager with one shell. Pass the controller's
        // already-loaded ThemeConfiguration so the default launch options (shell
        // type, grid dims) match the rest of the mod.
        _sessions = GhosttySessionManagerFactory.CreateSessionManager(config);
        // Pre-publication hook: theme/cursor are pushed before the session becomes
        // visible to the tick thread, so the native calls can never race BuildFrame
        // (sessions are created on pool threads).
        _sessions.SessionConfigurator = WireSession;

        StartDefaultSession();
    }

    /// <summary>The dedicated shell session (null until it has started).</summary>
    public TerminalSession? ActiveSession => _sessions.ActiveSession;

    /// <summary>
    ///     Whether the in-world terminal currently has input focus. Drives the
    ///     cursor style (solid block when focused, steady hollow box when not).
    ///     Set by the in-world manager each frame.
    /// </summary>
    public bool HasFocus { get; set; }

    /// <summary>
    /// Draws the terminal into the current ImGui context's window draw list. Call
    /// with the secondary (off-screen) context current; the in-world manager wraps
    /// this in <c>PerFrameRenderer</c>'s NewFrame/Render.
    /// </summary>
    public void BuildUi()
    {
        if (_disposed)
        {
            return;
        }

        var io = ImGui.GetIO();
        var displaySize = io.DisplaySize;
        float fontSize = _settings.FontSize;
        var fonts = ResolveFontsCached(fontSize);

        // Borderless, padding-less window filling the whole off-screen texture. We
        // paint our own background (the render pass clear is black, but the theme
        // background may not be), so the window itself is NoBackground.
        ImGui.SetNextWindowPos(new float2(0f, 0f));
        ImGui.SetNextWindowSize(displaySize);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(0f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBackground |
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus;

        bool visible = ImGui.Begin("##purrtty-inworld-content", flags);
        ImGui.PopStyleVar(2);

        if (visible)
        {
            DrawContent(fonts, fontSize);
        }

        // ImGui requires a matching End() for every Begin(), regardless of return.
        ImGui.End();
    }

    private void DrawContent(FrameFonts fonts, float fontSize)
    {
        var canvasPos = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();
        var drawList = ImGui.GetWindowDrawList();

        if (avail.X <= 0f || avail.Y <= 0f)
        {
            return;
        }

        // Paint the terminal background (the render-pass clear is black; the theme
        // background usually is not). Opaque — the quad applies its own opacity.
        var bg = _settings.Colors.Background.WithAlpha(0xFF);
        drawList.AddRectFilled(canvasPos, canvasPos + avail, FrameGridRenderer.ToU32(bg));

        var session = _sessions.ActiveSession;
        if (session == null)
        {
            // Still starting (CreateSessionAsync runs on a pool thread) or failed.
            string message = _sessionStartError is { } err
                ? $"Failed to start shell session: {err}"
                : "Starting shell session…";
            drawList.AddText(new float2(canvasPos.X + 8f, canvasPos.Y + 8f), 0xFFCCCCCCu, message);
            return;
        }

        int cols = Math.Max(1, (int)(avail.X / _cellWidth));
        int rows = Math.Max(1, (int)(avail.Y / _cellHeight));

        if (cols != session.Surface.Cols || rows != session.Surface.Rows)
        {
            session.Surface.Resize(cols, rows, (int)_cellWidth, (int)_cellHeight);
            session.UpdateTerminalDimensions(cols, rows);
            session.ProcessManager.Resize(cols, rows);
            _sessions.UpdateLastKnownTerminalDimensions(cols, rows);
        }

        // Mouse geometry for app-mouse cell mapping: the engine divides pixels by
        // these INTEGER cell metrics, so a quad-hit cell must be synthesized in them
        // too (Phase 9). The grid pixel extent feeds ProcessMouse's UV→cell map.
        int mouseCellW = Math.Max(1, (int)_cellWidth);
        int mouseCellH = Math.Max(1, (int)_cellHeight);
        session.Surface.SetMouseGeometry(cols * mouseCellW, rows * mouseCellH, mouseCellW, mouseCellH);
        _gridPixelWidth = avail.X;
        _gridPixelHeight = avail.Y;

        // BuildFrame is the tick: it drains the PTY inbox and advances the engine.
        // It must run every frame (gotcha 18 — an unticked surface grows its inbox
        // unbounded), which it does because the in-world manager calls BuildUi each
        // frame.
        var frame = session.Surface.BuildFrame();

        // Solid block cursor when focused; the renderer forces a steady hollow box
        // when windowFocused is false (input is going elsewhere).
        FrameGridRenderer.Render(
            frame, drawList, canvasPos,
            _cellWidth, _cellHeight, fonts, fontSize, _selectionColor, cursorOn: true,
            _settings.ForegroundOpacity, _settings.CellBackgroundOpacity, windowFocused: HasFocus);
    }

    private void StartDefaultSession()
    {
        // Launch the configured default shell. Mirrors the controller's
        // fire-and-forget start on a pool thread, recording any failure so the
        // texture can show it instead of staying blank.
        _ = Task.Run(async () =>
        {
            try
            {
                await _sessions.CreateSessionAsync(null, null);
            }
            catch (Exception ex)
            {
                _sessionStartError = ex.Message;
                ModLog.Log.Debug($"InWorldTerminalRenderer: failed to start session: {ex.Message}");
            }
        });
    }

    private void WireSession(TerminalSession session)
    {
        session.Surface.SetTheme(_engineTheme);
        session.Surface.SetCursorStyle(_settings.CursorStyle, _settings.CursorBlink);
    }

    private static TerminalWindowSettings BuildSettings(ThemeConfiguration config, ThemeCatalog catalog)
    {
        var theme = catalog.Find(config.SelectedThemeName) ?? catalog.Default;
        var settings = new TerminalWindowSettings
        {
            ThemeName = theme.Name,
            Colors = theme.Colors.Clone(),
            FontFamily = config.FontFamily ?? "Hack",
            FontSize = Math.Clamp(config.FontSize ?? 32f, LayoutConstants.MIN_FONT_SIZE, LayoutConstants.MAX_FONT_SIZE),
            BackgroundOpacity = Math.Clamp(config.BackgroundOpacity, 0f, 1f),
            ForegroundOpacity = Math.Clamp(config.ForegroundOpacity, 0f, 1f),
            CellBackgroundOpacity = Math.Clamp(config.CellBackgroundOpacity, 0f, 1f),
            CursorStyle = config.CursorStyle,
            CursorBlink = config.CursorBlink,
        };

        // A user-saved theme may carry display settings (font/opacity/cursor) that
        // override the loose defaults; bundled themes are colors-only.
        settings.ApplyThemeOverrides(theme);
        return settings;
    }

    private FrameFonts ResolveFontsCached(float fontSize)
    {
        int loadedCount = PurrTTYFontManager.LoadedFonts.Count;
        if (_hasFonts
            && _cachedFontFamily == _settings.FontFamily
            && _cachedFontSize == fontSize
            && _cachedLoadedFontCount == loadedCount)
        {
            return _fonts;
        }

        var config = PurrTTYFontManager.CreateFontConfigForFamily(_settings.FontFamily);
        var regular = ResolveFontByName(config.RegularFontName) ?? ImGui.GetFont();
        var bold = ResolveFontByName(config.BoldFontName) ?? regular;
        var italic = ResolveFontByName(config.ItalicFontName) ?? regular;
        var boldItalic = ResolveFontByName(config.BoldItalicFontName) ?? regular;

        ComputeCellMetrics(regular, fontSize);

        // Simple ctor: ASCII run batching disabled (per-cell placement). Correct
        // for any font; the in-world texture only re-renders on change.
        _fonts = new FrameFonts(regular, bold, italic, boldItalic);
        _hasFonts = true;
        _cachedFontFamily = _settings.FontFamily;
        _cachedFontSize = fontSize;
        _cachedLoadedFontCount = loadedCount;
        return _fonts;
    }

    private void ComputeCellMetrics(ImFontPtr font, float fontSize)
        => (_cellWidth, _cellHeight) = MeasureCell(font, fontSize);

    private static (float Width, float Height) MeasureCell(ImFontPtr font, float fontSize)
    {
        ImGui.PushFont(font, fontSize);
        var size = ImGui.CalcTextSize("M");
        ImGui.PopFont();

        return (size.X > 0.5f ? size.X : fontSize * 0.6f, size.Y > 0.5f ? size.Y : fontSize * 1.2f);
    }

    /// <summary>
    ///     Measures the cell size (pixels) for the in-world terminal's configured font
    ///     — family + size resolved from the theme config exactly as a live renderer
    ///     would — using the current ImGui context's shared font atlas. Lets a
    ///     fixed-grid instance derive its off-screen texture extent (cols×Width,
    ///     rows×Height) at build time. Must be called with an ImGui frame active (the
    ///     in-world manager builds instances from <c>OnAfterGui</c>).
    /// </summary>
    public static (float Width, float Height) MeasureCell(ThemeConfiguration config, ThemeCatalog catalog)
    {
        var settings = BuildSettings(config, catalog);
        var fontConfig = PurrTTYFontManager.CreateFontConfigForFamily(settings.FontFamily);
        var regular = ResolveFontByName(fontConfig.RegularFontName) ?? ImGui.GetFont();
        return MeasureCell(regular, settings.FontSize);
    }

    private static ImFontPtr? ResolveFontByName(string? name)
        => !string.IsNullOrEmpty(name) && PurrTTYFontManager.LoadedFonts.TryGetValue(name, out var font)
            ? font
            : null;

    /// <summary>
    ///     Maps an in-world quad hit (texture UV, or null when the cursor is off the
    ///     quad) to a terminal cell and forwards app-mouse events (press / release /
    ///     drag-motion / wheel) to the dedicated shell — so in-world TUIs (vim,
    ///     htop, ...) respond to clicks. No-op unless the app requested mouse
    ///     tracking. Mirrors <see cref="TerminalWindow"/>'s app-mouse gating. Call on
    ///     the main thread (the in-world manager calls it from <c>OnAfterGui</c>).
    /// </summary>
    public void ProcessMouse(float2? hitUv, ImGuiIOPtr io)
    {
        if (_disposed)
        {
            return;
        }

        var session = _sessions.ActiveSession;
        // Only when the app requests mouse tracking; otherwise clicks just focus.
        if (session == null || !session.Surface.IsMouseTrackingEnabled)
        {
            return;
        }

        int cols = session.Surface.Cols;
        int rows = session.Surface.Rows;
        if (cols <= 0 || rows <= 0 || _gridPixelWidth <= 0f || _gridPixelHeight <= 0f)
        {
            return;
        }

        int cellW = Math.Max(1, (int)_cellWidth);
        int cellH = Math.Max(1, (int)_cellHeight);

        bool over = hitUv.HasValue;
        int col = _appMouseCol < 0 ? 0 : _appMouseCol;
        int row = _appMouseRow < 0 ? 0 : _appMouseRow;
        if (hitUv is { } uvHit)
        {
            // UV → texture pixel → cell, using the same fractional metrics the grid
            // was drawn with so the cell matches the glyph under the cursor.
            float px = uvHit.X * _gridPixelWidth;
            float py = uvHit.Y * _gridPixelHeight;
            col = Math.Clamp((int)(px / _cellWidth), 0, cols - 1);
            row = Math.Clamp((int)(py / _cellHeight), 0, rows - 1);
        }

        bool cellChanged = col != _appMouseCol || row != _appMouseRow;
        _appMouseCol = col;
        _appMouseRow = row;

        var mods = TerminalInputEncoder.ReadModifiers(io);
        // Synthesize the cell-center in INTEGER metrics so the engine's integer
        // pixel→cell division recovers the same cell (gotcha 11).
        var pos = new float2(col * cellW + cellW * 0.5f, row * cellH + cellH * 0.5f);

        // Press is gated on being over the quad; release fires ungated (but only
        // when the matching press was forwarded), mirroring TerminalWindow.
        if (over && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _appMousePressSent[(int)MouseButton.Left] = true;
            EncodeMouseAndSend(session, MouseAction.Press, MouseButton.Left, mods, pos);
        }
        else if (_appMousePressSent[(int)MouseButton.Left] && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            _appMousePressSent[(int)MouseButton.Left] = false;
            EncodeMouseAndSend(session, MouseAction.Release, MouseButton.Left, mods, pos);
        }
        else if (over && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            _appMousePressSent[(int)MouseButton.Right] = true;
            EncodeMouseAndSend(session, MouseAction.Press, MouseButton.Right, mods, pos);
        }
        else if (_appMousePressSent[(int)MouseButton.Right] && ImGui.IsMouseReleased(ImGuiMouseButton.Right))
        {
            _appMousePressSent[(int)MouseButton.Right] = false;
            EncodeMouseAndSend(session, MouseAction.Release, MouseButton.Right, mods, pos);
        }
        else if (over && ImGui.IsMouseClicked(ImGuiMouseButton.Middle))
        {
            _appMousePressSent[(int)MouseButton.Middle] = true;
            EncodeMouseAndSend(session, MouseAction.Press, MouseButton.Middle, mods, pos);
        }
        else if (_appMousePressSent[(int)MouseButton.Middle] && ImGui.IsMouseReleased(ImGuiMouseButton.Middle))
        {
            _appMousePressSent[(int)MouseButton.Middle] = false;
            EncodeMouseAndSend(session, MouseAction.Release, MouseButton.Middle, mods, pos);
        }
        else if (cellChanged)
        {
            // Drag / hover motion: the engine's encoder is mode-aware, so just offer
            // each cell crossing (a held button = drag, over-quad = any-event hover).
            var held = HeldMouseButton();
            if (held != MouseButton.None || over)
            {
                EncodeMouseAndSend(session, MouseAction.Motion, held, mods, pos);
            }
        }

        // Wheel reports as scroll-button presses, one per notch.
        if (over && io.MouseWheel != 0f)
        {
            var button = io.MouseWheel > 0f ? MouseButton.ScrollUp : MouseButton.ScrollDown;
            int notches = Math.Clamp((int)Math.Round(Math.Abs(io.MouseWheel)), 1, 10);
            for (int i = 0; i < notches; i++)
            {
                EncodeMouseAndSend(session, MouseAction.Press, button, mods, pos);
            }
        }
    }

    private static MouseButton HeldMouseButton()
    {
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) return MouseButton.Left;
        if (ImGui.IsMouseDown(ImGuiMouseButton.Middle)) return MouseButton.Middle;
        if (ImGui.IsMouseDown(ImGuiMouseButton.Right)) return MouseButton.Right;
        return MouseButton.None;
    }

    private static void EncodeMouseAndSend(
        TerminalSession session, MouseAction action, MouseButton button, KeyModifiers mods, float2 pos)
    {
        var ev = new TerminalMouseEvent
        {
            Action = action,
            Button = button,
            Modifiers = mods,
            X = pos.X,
            Y = pos.Y,
        };
        Span<byte> buf = stackalloc byte[64];
        int n = session.Surface.EncodeMouse(ev, buf);
        if (n > 0)
        {
            session.SendInput(buf[..n]);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // Disposes the session manager: closes the shell + its native surface.
        // Must run on the tick thread (the in-world manager disposes on Unload).
        _sessions.Dispose();
    }
}
