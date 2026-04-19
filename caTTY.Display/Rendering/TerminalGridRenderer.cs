using System;
using System.Buffers;
using System.Collections.Generic;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using caTTY.Core.Terminal;
using caTTY.Core.Types;
using caTTY.Display.Controllers.TerminalUi;
using caTTY.Display.Types;

namespace caTTY.Display.Rendering;

/// <summary>
///     Handles rendering of terminal grid content (cells, text, backgrounds, decorations).
///     Separated from TerminalUiRender to enable strategy pattern for caching.
/// </summary>
internal class TerminalGridRenderer
{
    private readonly TerminalUiFonts _fonts;
    private readonly CachedColorResolver _colorResolver;
    private readonly StyleManager _styleManager;
    private readonly Performance.PerformanceStopwatch _perfWatch;

    // Reusable buffers to avoid per-frame allocations
    private ReadOnlyMemory<Cell>[] _screenBufferCache = [];
    private readonly List<ReadOnlyMemory<Cell>> _viewportRowsCache = new(256);

    public TerminalGridRenderer(
        TerminalUiFonts fonts,
        CachedColorResolver colorResolver,
        StyleManager styleManager,
        Performance.PerformanceStopwatch perfWatch)
    {
        _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
        _colorResolver = colorResolver ?? throw new ArgumentNullException(nameof(colorResolver));
        _styleManager = styleManager ?? throw new ArgumentNullException(nameof(styleManager));
        _perfWatch = perfWatch ?? throw new ArgumentNullException(nameof(perfWatch));
    }

    /// <summary>
    ///     Populates the viewport cache with the current terminal content.
    ///     This must be called before Render() or RenderSelectionOverlay().
    /// </summary>
    public void PopulateViewportCache(TerminalSession activeSession)
    {
        // Ensure screen buffer cache is the right size
        int terminalRowCount = activeSession.Terminal.Height;
        if (_screenBufferCache.Length != terminalRowCount)
        {
            _screenBufferCache = new ReadOnlyMemory<Cell>[terminalRowCount];
        }

        // Get row memory references directly from ScreenBuffer - no allocation!
        for (int i = 0; i < terminalRowCount; i++)
        {
            _screenBufferCache[i] = activeSession.Terminal.ScreenBuffer.GetRowMemory(i);
        }

        // Get the viewport rows that should be displayed (combines scrollback + screen buffer)
        var isAlternateScreenActive = ((TerminalEmulator)activeSession.Terminal).State.IsAlternateScreenActive;
        activeSession.Terminal.ScrollbackManager.GetViewportRowsNonAlloc(
            _screenBufferCache,
            isAlternateScreenActive,
            terminalRowCount,
            _viewportRowsCache
        );
    }

    /// <summary>
    ///     Renders the terminal grid content to the specified draw target.
    /// </summary>
    public void Render(
        TerminalSession activeSession,
        ITerminalDrawTarget target,
        float2 terminalDrawPos,
        float currentCharacterWidth,
        float currentLineHeight,
        TextSelection currentSelection)
    {
        PopulateViewportCache(activeSession);
        var viewportRows = _viewportRowsCache;

        // Check if we're viewing the live screen buffer (not scrolled into scrollback history)
        // This enables dirty row tracking optimization
        // - In alternate screen mode: always viewing live buffer (no scrollback)
        // - In primary screen mode: only when auto-scroll is enabled (not scrolled up)
        var isAlternateScreenActive = ((TerminalEmulator)activeSession.Terminal).State.IsAlternateScreenActive;
        bool isViewingLiveScreen = isAlternateScreenActive || activeSession.Terminal.IsAutoScrollEnabled;
        bool canUseDirtyTracking = isViewingLiveScreen;

        // Render each cell from the viewport content
        int terminalWidthCells = activeSession.Terminal.Width;
        char[] runChars = ArrayPool<char>.Shared.Rent(Math.Max(terminalWidthCells, 1));
        float4[] foregroundColors = ArrayPool<float4>.Shared.Rent(Math.Max(terminalWidthCells, 1));
        SgrAttributes[] cellAttributes = ArrayPool<SgrAttributes>.Shared.Rent(Math.Max(terminalWidthCells, 1));
        bool[] isSelectedByCol = ArrayPool<bool>.Shared.Rent(Math.Max(terminalWidthCells, 1));

        try
        {
            for (int row = 0; row < Math.Min(viewportRows.Count, activeSession.Terminal.Height); row++)
            {
                var rowMemory = viewportRows[row];
                var rowSpan = rowMemory.Span;
                int colsToRender = Math.Min(rowSpan.Length, terminalWidthCells);

                int runStartCol = 0;
                int runLength = 0;
                uint runColorU32 = 0;
                ImFontPtr runFont = default;

                // Background run tracking for batching contiguous same-color backgrounds
                int bgRunStartCol = -1;
                int bgRunLength = 0;
                uint bgRunColorU32 = 0;

                void FlushBackgroundRun()
                {
                    if (bgRunLength <= 0)
                        return;

                    float bgX = terminalDrawPos.X + (bgRunStartCol * currentCharacterWidth);
                    float bgY = terminalDrawPos.Y + (row * currentLineHeight);
                    float bgWidth = bgRunLength * currentCharacterWidth;
                    float bgHeight = currentLineHeight;

                    target.AddRectFilled(
                        new float2(bgX, bgY),
                        new float2(bgX + bgWidth, bgY + bgHeight),
                        bgRunColorU32
                    );

                    bgRunLength = 0;
                    bgRunStartCol = -1;
                }

                void FlushRun()
                {
                    if (runLength <= 0)
                        return;

                    // IMPORTANT: Flush backgrounds BEFORE text to ensure correct draw order
                    FlushBackgroundRun();

                    float runY = terminalDrawPos.Y + (row * currentLineHeight);

                    try
                    {
                        // Render each character at its exact grid position to prevent drift
                        // caused by font glyph advance widths not matching currentCharacterWidth.
                        // This fixes character shifting when selection changes run boundaries.
                        for (int i = 0; i < runLength; i++)
                        {
                            float charX = terminalDrawPos.X + ((runStartCol + i) * currentCharacterWidth);
                            var charPos = new float2(charX, runY);
                            target.AddText(charPos, runColorU32, runChars[i].ToString(), runFont, _fonts.CurrentFontConfig.FontSize);
                        }
                    }
                    finally
                    {
                        // ImGui.PopFont handled by target
                    }

                    // Decorations must be drawn after text to preserve existing draw order.
                    for (int i = 0; i < runLength; i++)
                    {
                        int col = runStartCol + i;
                        if (col < 0 || col >= colsToRender)
                            continue;

                        if (isSelectedByCol[col])
                            continue;

                        var attrs = cellAttributes[col];
                        var fgColor = foregroundColors[col];
                        float x = terminalDrawPos.X + (col * currentCharacterWidth);
                        float y = terminalDrawPos.Y + (row * currentLineHeight);
                        var pos = new float2(x, y);

                        if (_styleManager.ShouldRenderUnderline(attrs))
                        {
                            RenderUnderline(target, pos, attrs, fgColor, currentCharacterWidth, currentLineHeight);
                        }

                        if (_styleManager.ShouldRenderStrikethrough(attrs))
                        {
                            RenderStrikethrough(target, pos, fgColor, currentCharacterWidth, currentLineHeight);
                        }
                    }

                    runLength = 0;
                }

                // Pre-compute whether this row might have any selection overlap
                bool rowMightHaveSelection = currentSelection.RowMightBeSelected(row);

                // EARLY EXIT: Skip rows with no content and no selection
                if (!rowMightHaveSelection && !RowHasContent(rowSpan))
                {
                    continue;
                }

                // DIRTY ROW OPTIMIZATION: Skip clean rows that have no content requiring rendering
                // This only applies when viewing the live screen buffer (not scrollback)
                // Clean rows with no content can be safely skipped because nothing needs to be drawn
                if (canUseDirtyTracking && !activeSession.Terminal.ScreenBuffer.IsRowDirty(row))
                {
                    // Row hasn't changed since last render and has already been skipped
                    // (if it had content, it wouldn't pass the RowHasContent check above on future frames)
                    // For truly empty rows that haven't been modified, we can skip processing
                    if (!RowHasContent(rowSpan) && !rowMightHaveSelection)
                    {
                        continue;
                    }
                }

                for (int col = 0; col < colsToRender; col++)
                {
                    try
                    {
                        Cell cell = rowSpan[col];

                        // EARLY EXIT: Skip completely default empty cells
                        // This is the most common case in typical terminal output
                        bool isEmptyChar = cell.Character == ' ' || cell.Character == '\0';
                        if (isEmptyChar && cell.Attributes.IsDefault)
                        {
                            // Quick selection check - only do full Contains() if row overlaps selection
                            if (!rowMightHaveSelection || !currentSelection.Contains(row, col))
                            {
                                // Flush any pending text run before skipping
                                FlushRun();
                                continue;
                            }
                        }

                        float x = terminalDrawPos.X + (col * currentCharacterWidth);
                        float y = terminalDrawPos.Y + (row * currentLineHeight);
                        var pos = new float2(x, y);

                        // Check if this cell is selected
                        bool isSelected;
                        if (!rowMightHaveSelection)
                        {
                            isSelected = false;
                        }
                        else
                        {
                            isSelected = currentSelection.Contains(row, col);
                        }

                        isSelectedByCol[col] = isSelected;
                        cellAttributes[col] = cell.Attributes;

                        // Resolve and process all colors in one fused call
                        _colorResolver.ResolveCellColors(
                            cell.Attributes,
                            out uint fgColorU32,
                            out uint cellBgColorU32,
                            out bool needsBackground,
                            out float4 fgColor);

                        foregroundColors[col] = fgColor;

                        // Handle selection - override colors if selected
                        if (isSelected)
                        {
                            // Use selection colors - invert foreground and background for selected text
                            var selectionBg = new float4(0.3f, 0.5f, 0.8f, 0.7f); // Semi-transparent blue
                            var selectionFg = new float4(1.0f, 1.0f, 1.0f, 1.0f); // White text

                            // Apply foreground opacity to selection foreground and cell background opacity to selection background
                            var bgColor = OpacityManager.ApplyCellBackgroundOpacity(selectionBg);
                            fgColor = OpacityManager.ApplyForegroundOpacity(selectionFg);
                            foregroundColors[col] = fgColor;

                            cellBgColorU32 = ImGui.ColorConvertFloat4ToU32(bgColor);
                            needsBackground = true;
                        }

                        // Batch background drawing
                        if (needsBackground)
                        {
                            bool canExtendRun = bgRunLength > 0
                                && col == bgRunStartCol + bgRunLength
                                && cellBgColorU32 == bgRunColorU32;

                            if (canExtendRun)
                            {
                                bgRunLength++;
                            }
                            else
                            {
                                FlushBackgroundRun();
                                bgRunStartCol = col;
                                bgRunLength = 1;
                                bgRunColorU32 = cellBgColorU32;
                            }
                        }
                        else
                        {
                            FlushBackgroundRun();
                        }

                        // Draw character if not space or null (batched into runs)
                        if (cell.Character != ' ' && cell.Character != '\0')
                        {
                            var font = _fonts.SelectFont(cell.Attributes);
                            uint fgU32 = ImGui.ColorConvertFloat4ToU32(foregroundColors[col]);

                            if (runLength == 0)
                            {
                                runStartCol = col;
                                runFont = font;
                                runColorU32 = fgU32;
                                runChars[0] = cell.Character;
                                runLength = 1;
                            }
                            else
                            {
                                bool isContiguous = col == runStartCol + runLength;
                                bool sameFont = runFont.Equals(font);
                                bool sameColor = runColorU32 == fgU32;

                                if (isContiguous && sameFont && sameColor)
                                {
                                    runChars[runLength] = cell.Character;
                                    runLength++;
                                }
                                else
                                {
                                    FlushRun();
                                    runStartCol = col;
                                    runFont = font;
                                    runColorU32 = fgU32;
                                    runChars[0] = cell.Character;
                                    runLength = 1;
                                }
                            }
                        }
                        else
                        {
                            FlushRun();
                        }
                    }
                    finally
                    {
                        // Column processing complete
                    }
                }

                // FlushRun internally calls FlushBackgroundRun first to ensure correct draw order
                FlushRun();
                // Final background flush in case there's a trailing background with no text
                FlushBackgroundRun();
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(runChars);
            ArrayPool<float4>.Shared.Return(foregroundColors);
            ArrayPool<SgrAttributes>.Shared.Return(cellAttributes);
            ArrayPool<bool>.Shared.Return(isSelectedByCol);
        }

        // Clear dirty flags after rendering (only when viewing current screen, not scrollback)
        // This marks all rows as "clean" so the next frame can detect new changes
        if (canUseDirtyTracking)
        {
            // Only clear when viewing current screen buffer content (not scrolled into history)
            activeSession.Terminal.ScreenBuffer.ClearDirtyFlags();
        }
    }

    /// <summary>
    ///     Renders selection overlay on top of cached content.
    ///     Assumes PopulateViewportCache() has already been called.
    /// </summary>
    public void RenderSelectionOverlay(
        TerminalSession activeSession,
        ITerminalDrawTarget target,
        float2 terminalDrawPos,
        float currentCharacterWidth,
        float currentLineHeight,
        TextSelection currentSelection)
    {
        var viewportRows = _viewportRowsCache;
        int terminalWidthCells = activeSession.Terminal.Width;

        // Selection Colors
        var selectionBg = new float4(0.3f, 0.5f, 0.8f, 0.7f); // Semi-transparent blue
        var selectionFg = new float4(1.0f, 1.0f, 1.0f, 1.0f); // White text
        var finalBg = OpacityManager.ApplyCellBackgroundOpacity(selectionBg);
        var finalFg = OpacityManager.ApplyForegroundOpacity(selectionFg);
        uint bgColU32 = ImGui.ColorConvertFloat4ToU32(finalBg);
        uint fgColU32 = ImGui.ColorConvertFloat4ToU32(finalFg);

        var fontSize = _fonts.CurrentFontConfig.FontSize;

        for (int row = 0; row < Math.Min(viewportRows.Count, activeSession.Terminal.Height); row++)
        {
            if (!currentSelection.RowMightBeSelected(row))
                continue;

            var rowMemory = viewportRows[row];
            var rowSpan = rowMemory.Span;
            int colsToRender = Math.Min(rowSpan.Length, terminalWidthCells);

            for (int col = 0; col < colsToRender; col++)
            {
                if (!currentSelection.Contains(row, col))
                    continue;

                Cell cell = rowSpan[col];

                float x = terminalDrawPos.X + (col * currentCharacterWidth);
                float y = terminalDrawPos.Y + (row * currentLineHeight);
                var pos = new float2(x, y);

                // Draw Background
                target.AddRectFilled(
                    pos,
                    new float2(x + currentCharacterWidth, y + currentLineHeight),
                    bgColU32
                );

                // Draw Text
                if (cell.Character != ' ' && cell.Character != '\0')
                {
                    var font = _fonts.SelectFont(cell.Attributes);
                    target.AddText(pos, fgColU32, cell.Character.ToString(), font, fontSize);
                }
            }
        }
    }

    /// <summary>
    ///     Renders underline decoration for a cell.
    /// </summary>
    private void RenderUnderline(ITerminalDrawTarget target, float2 pos, SgrAttributes attributes, float4 foregroundColor, float currentCharacterWidth, float currentLineHeight)
    {
        float4 underlineColor = _styleManager.GetUnderlineColor(attributes, foregroundColor);
        underlineColor = OpacityManager.ApplyForegroundOpacity(underlineColor);
        float thickness = _styleManager.GetUnderlineThickness(attributes.UnderlineStyle);

        float underlineY = pos.Y + currentLineHeight - 2;
        var underlineStart = new float2(pos.X, underlineY);
        var underlineEnd = new float2(pos.X + currentCharacterWidth, underlineY);

        switch (attributes.UnderlineStyle)
        {
            case UnderlineStyle.Single:
                uint singleColor = ImGui.ColorConvertFloat4ToU32(underlineColor);
                float singleThickness = Math.Max(3.0f, thickness);
                target.DrawLine(underlineStart, underlineEnd, singleColor, singleThickness);
                break;

            case UnderlineStyle.Double:
                // Draw two lines for double underline with proper spacing
                uint doubleColor = ImGui.ColorConvertFloat4ToU32(underlineColor);
                float doubleThickness = Math.Max(3.0f, thickness);

                // First line (bottom) - same position as single underline
                target.DrawLine(underlineStart, underlineEnd, doubleColor, doubleThickness);

                // Second line (top) - spaced 4 pixels above the first for better visibility
                var doubleStart = new float2(pos.X, underlineY - 4);
                var doubleEnd = new float2(pos.X + currentCharacterWidth, underlineY - 4);
                target.DrawLine(doubleStart, doubleEnd, doubleColor, doubleThickness);
                break;

            case UnderlineStyle.Curly:
                target.DrawCurlyUnderline(pos, underlineColor, thickness, currentCharacterWidth, currentLineHeight);
                break;

            case UnderlineStyle.Dotted:
                target.DrawDottedUnderline(pos, underlineColor, thickness, currentCharacterWidth, currentLineHeight);
                break;

            case UnderlineStyle.Dashed:
                target.DrawDashedUnderline(pos, underlineColor, thickness, currentCharacterWidth, currentLineHeight);
                break;
        }
    }

    /// <summary>
    ///     Renders strikethrough for a cell.
    /// </summary>
    private void RenderStrikethrough(ITerminalDrawTarget target, float2 pos, float4 foregroundColor, float currentCharacterWidth, float currentLineHeight)
    {
        // Apply foreground opacity to strikethrough color
        foregroundColor = OpacityManager.ApplyForegroundOpacity(foregroundColor);

        float strikeY = pos.Y + (currentLineHeight / 2);
        var strikeStart = new float2(pos.X, strikeY);
        var strikeEnd = new float2(pos.X + currentCharacterWidth, strikeY);
        target.DrawLine(strikeStart, strikeEnd, ImGui.ColorConvertFloat4ToU32(foregroundColor), 1.0f);
    }

    /// <summary>
    ///     Checks if a row has any content that requires rendering.
    ///     Returns true if any cell has a non-space character or non-default attributes.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static bool RowHasContent(ReadOnlySpan<Cell> rowSpan)
    {
        for (int i = 0; i < rowSpan.Length; i++)
        {
            ref readonly var cell = ref rowSpan[i];

            // Non-empty character?
            if (cell.Character != ' ' && cell.Character != '\0')
                return true;

            // Has explicit background color? (needs to draw background)
            if (cell.Attributes.BackgroundColor.HasValue)
                return true;

            // Has attributes that affect empty cells? (inverse makes spaces visible)
            if (cell.Attributes.Inverse)
                return true;
        }
        return false;
    }
}
