using System;
using Brutal.ImGuiApi;
using caTTY.Core.Terminal;
using caTTY.Core.Types;
using caTTY.Display.Rendering;
using caTTY.Display.Types;
using float2 = Brutal.Numerics.float2;
using float4 = Brutal.Numerics.float4;

namespace caTTY.Display.Controllers.TerminalUi;

/// <summary>
///     Handles rendering of terminal content including cells, cursor, and text decorations.
///     Uses strategy pattern to support both direct and cached rendering.
/// </summary>
internal class TerminalUiRender
{
  private readonly TerminalUiFonts _fonts;
  private readonly CursorRenderer _cursorRenderer;
  private readonly Performance.PerformanceStopwatch _perfWatch;
  private readonly ITerminalRenderStrategy _renderStrategy;
  private readonly TerminalGridRenderer _gridRenderer;

  public TerminalUiRender(
    TerminalUiFonts fonts,
    CursorRenderer cursorRenderer,
    Performance.PerformanceStopwatch perfWatch,
    ITerminalRenderStrategy renderStrategy,
    TerminalGridRenderer gridRenderer)
  {
    _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
    _cursorRenderer = cursorRenderer ?? throw new ArgumentNullException(nameof(cursorRenderer));
    _perfWatch = perfWatch ?? throw new ArgumentNullException(nameof(perfWatch));
    _renderStrategy = renderStrategy ?? throw new ArgumentNullException(nameof(renderStrategy));
    _gridRenderer = gridRenderer ?? throw new ArgumentNullException(nameof(gridRenderer));
  }

  /// <summary>
  ///     Renders the complete terminal content including all cells and cursor.
  /// </summary>
  public void RenderTerminalContent(
    SessionManager sessionManager,
    float currentCharacterWidth,
    float currentLineHeight,
    TextSelection currentSelection,
    out float2 lastTerminalOrigin,
    out float2 lastTerminalSize,
    Action handleMouseInputForTerminal,
    Action handleMouseTrackingForApplications,
    Action renderContextMenu,
    bool drawBackground)
  {
    var activeSession = sessionManager.ActiveSession;
    if (activeSession == null)
    {
      // No active session - show placeholder
      ImGui.Text("No terminal sessions. Click + to create one.");
      lastTerminalOrigin = new float2(0, 0);
      lastTerminalSize = new float2(0, 0);
      return;
    }

    // Push terminal content font for this rendering section
    _fonts.PushTerminalContentFont(out bool terminalFontUsed);

    try
    {
      ImDrawListPtr drawList = ImGui.GetWindowDrawList();
      float2 windowPos = ImGui.GetCursorScreenPos();

      // Calculate terminal area
      float terminalWidth = activeSession.Terminal.Width * currentCharacterWidth;
      float terminalHeight = activeSession.Terminal.Height * currentLineHeight;

      // Cache terminal rect for input encoding (mouse wheel / mouse reporting)
      lastTerminalOrigin = windowPos;
      lastTerminalSize = new float2(terminalWidth, terminalHeight);

      // CRITICAL: Create an invisible button that captures mouse input and prevents window dragging
      // This is the key to preventing ImGui window dragging when selecting text
      ImGui.InvisibleButton("terminal_content", new float2(terminalWidth, terminalHeight));
      bool terminalHovered = ImGui.IsItemHovered();
      bool terminalActive = ImGui.IsItemActive();

      // Open context menu popup on right-click of the invisible button
      // MUST be called immediately after InvisibleButton while it's still the "last item"
      if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
      {
        ImGui.OpenPopup("terminal_context_menu");
      }

      // Get the draw position after the invisible button
      float2 terminalDrawPos = windowPos;

      // Note: Terminal background is now handled manually since window bg is transparent
      // Draw a separate terminal background rectangle for the content area
      if (drawBackground)
      {
        float4 themeBg = ThemeManager.GetDefaultBackground();
        themeBg = OpacityManager.ApplyBackgroundOpacity(themeBg);
        uint themeBgU32 = ImGui.ColorConvertFloat4ToU32(themeBg);

        drawList.AddRectFilled(
            terminalDrawPos,
            terminalDrawPos + new float2(terminalWidth, terminalHeight),
            themeBgU32
        );
      }

      // Compute render key for cache invalidation
      var renderKey = new TerminalRenderKey(
          activeSession.Terminal.ScreenBuffer.Revision,
          activeSession.Terminal.ViewportOffset,
          ThemeManager.Version,
          _fonts.CurrentFontSize,
          currentCharacterWidth,
          currentLineHeight,
          activeSession.Terminal.Width,
          activeSession.Terminal.Height,
          windowPos.X,
          windowPos.Y,
          0);

      // Create render context
      var context = new RenderContext
      {
          DrawList = drawList,
          RenderKey = renderKey,
          DirectTarget = new ImGuiDrawTarget(drawList)
      };

      // Delegate to strategy for grid rendering
      _renderStrategy.RenderGrid(activeSession, terminalDrawPos, currentCharacterWidth,
                                currentLineHeight, currentSelection, context);

      // Render cursor
      RenderCursor(drawList, terminalDrawPos, activeSession, currentCharacterWidth, currentLineHeight);

      // Handle mouse input only when the invisible button is hovered/active
      if (terminalHovered || terminalActive)
      {
        handleMouseInputForTerminal();
      }

      // ALWAYS render the context menu popup (even when not hovering)
      // This is necessary because when the popup is open, we're no longer hovering the terminal
      renderContextMenu();

      // Also handle mouse tracking for applications (this works regardless of hover state)
      handleMouseTrackingForApplications();
    }
    finally
    {
      TerminalUiFonts.MaybePopFont(terminalFontUsed);
    }
  }

  /// <summary>
  ///     Renders the terminal cursor using the new cursor rendering system.
  /// </summary>
  public void RenderCursor(ImDrawListPtr drawList, float2 windowPos, TerminalSession activeSession, float currentCharacterWidth, float currentLineHeight)
  {
    if (activeSession == null) return;

    var terminalState = ((TerminalEmulator)activeSession.Terminal).State;
    ICursor cursor = activeSession.Terminal.Cursor;

    // Ensure cursor position is within bounds
    int cursorCol = Math.Max(0, Math.Min(cursor.Col, activeSession.Terminal.Width - 1));
    int cursorRow = Math.Max(0, Math.Min(cursor.Row, activeSession.Terminal.Height - 1));

    float x = windowPos.X + (cursorCol * currentCharacterWidth);
    float y = windowPos.Y + (cursorRow * currentLineHeight);
    var cursorPos = new float2(x, y);

    // Get cursor color from theme
    float4 cursorColor = ThemeManager.GetCursorColor();

    // Check if terminal is at bottom (not scrolled back)
    var scrollbackManager = activeSession.Terminal.ScrollbackManager;
    bool isAtBottom = scrollbackManager?.IsAtBottom ?? true;

    // Render cursor using the new cursor rendering system
    _cursorRenderer.RenderCursor(
        drawList,
        cursorPos,
        currentCharacterWidth,
        currentLineHeight,
        terminalState.CursorStyle,
        terminalState.CursorVisible,
        cursorColor,
        isAtBottom
    );
  }
}
