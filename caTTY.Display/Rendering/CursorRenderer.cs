using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using caTTY.Core.Types;
using caTTY.Display.Performance;

namespace caTTY.Display.Rendering;

/// <summary>
///     Handles cursor rendering for the terminal display.
///     Supports all 4 cursor styles: block, bar, underline, and block_hollow.
///     Includes blinking support based on cursor style and theme configuration.
/// </summary>
public class CursorRenderer
{
    private readonly PerformanceStopwatch _perfWatch;
    private DateTime _lastBlinkTime = DateTime.Now;
    private bool _blinkState = true;

    /// <summary>
    ///     Creates a new CursorRenderer with the specified performance stopwatch.
    /// </summary>
    /// <param name="perfWatch">Performance stopwatch for timing measurements</param>
    public CursorRenderer(PerformanceStopwatch perfWatch)
    {
        _perfWatch = perfWatch ?? throw new ArgumentNullException(nameof(perfWatch));
    }

    /// <summary>
    ///     Updates the cursor blink state based on elapsed time.
    /// </summary>
    /// <param name="blinkInterval">Blink interval in milliseconds</param>
    public void UpdateBlinkState(int blinkInterval = 500)
    {
//        _perfWatch.Start("CursorRenderer.UpdateBlinkState");
        try
        {
            DateTime currentTime = DateTime.Now;
            if ((currentTime - _lastBlinkTime).TotalMilliseconds >= blinkInterval)
            {
                _blinkState = !_blinkState;
                _lastBlinkTime = currentTime;
            }
        }
        finally
        {
//            _perfWatch.Stop("CursorRenderer.UpdateBlinkState");
        }
    }

    /// <summary>
    ///     Renders the cursor at the specified position using ImGui DrawList.
    /// </summary>
    /// <param name="drawList">ImGui draw list for rendering</param>
    /// <param name="position">Cursor position in screen coordinates</param>
    /// <param name="characterWidth">Width of a character cell</param>
    /// <param name="lineHeight">Height of a character cell</param>
    /// <param name="cursorStyle">Current cursor style</param>
    /// <param name="cursorVisible">Whether cursor is visible</param>
    /// <param name="cursorColor">Cursor color</param>
    /// <param name="isAtBottom">Whether terminal is at bottom (not scrolled back)</param>
    public void RenderCursor(
        ImDrawListPtr drawList,
        float2 position,
        float characterWidth,
        float lineHeight,
        CursorStyle cursorStyle,
        bool cursorVisible,
        float4 cursorColor,
        bool isAtBottom)
    {
//        _perfWatch.Start("CursorRenderer.RenderCursor");
        try
        {
            // Don't render cursor if not visible or scrolled back in history
            if (!cursorVisible || !isAtBottom)
            {
                return;
            }

            // Handle blinking - if cursor should blink and is in off state, don't render
            bool shouldBlink = cursorStyle.ShouldBlink();
            if (shouldBlink && !_blinkState)
            {
                return;
            }

            // Get cursor shape and render accordingly
            CursorShape shape = cursorStyle.GetShape();
            RenderCursorShape(drawList, position, characterWidth, lineHeight, shape, cursorColor);
        }
        finally
        {
//            _perfWatch.Stop("CursorRenderer.RenderCursor");
        }
    }

    /// <summary>
    ///     Renders a specific cursor shape.
    /// </summary>
    /// <param name="drawList">ImGui draw list for rendering</param>
    /// <param name="position">Cursor position in screen coordinates</param>
    /// <param name="characterWidth">Width of a character cell</param>
    /// <param name="lineHeight">Height of a character cell</param>
    /// <param name="shape">Cursor shape to render</param>
    /// <param name="cursorColor">Cursor color</param>
    private void RenderCursorShape(
        ImDrawListPtr drawList,
        float2 position,
        float characterWidth,
        float lineHeight,
        CursorShape shape,
        float4 cursorColor)
    {
//        _perfWatch.Start("CursorRenderer.RenderCursorShape");
        try
        {
            uint color = ImGui.ColorConvertFloat4ToU32(cursorColor);

            switch (shape)
            {
                case CursorShape.Block:
                    RenderBlockCursor(drawList, position, characterWidth, lineHeight, color);
                    break;

                case CursorShape.BlockHollow:
                    RenderBlockHollowCursor(drawList, position, characterWidth, lineHeight, color);
                    break;

                case CursorShape.Underline:
                    RenderUnderlineCursor(drawList, position, characterWidth, lineHeight, color);
                    break;

                case CursorShape.Bar:
                    RenderBarCursor(drawList, position, characterWidth, lineHeight, color);
                    break;

                default:
                    // Fallback to block cursor
                    RenderBlockCursor(drawList, position, characterWidth, lineHeight, color);
                    break;
            }
        }
        finally
        {
//            _perfWatch.Stop("CursorRenderer.RenderCursorShape");
        }
    }

    /// <summary>
    ///     Renders a filled block cursor.
    /// </summary>
    private void RenderBlockCursor(
        ImDrawListPtr drawList,
        float2 position,
        float characterWidth,
        float lineHeight,
        uint color)
    {
//        _perfWatch.Start("CursorRenderer.RenderBlockCursor");
        try
        {
            var endPos = new float2(position.X + characterWidth, position.Y + lineHeight);
            drawList.AddRectFilled(position, endPos, color);
        }
        finally
        {
//            _perfWatch.Stop("CursorRenderer.RenderBlockCursor");
        }
    }

    /// <summary>
    ///     Renders a hollow block cursor (outline only).
    /// </summary>
    private void RenderBlockHollowCursor(
        ImDrawListPtr drawList,
        float2 position,
        float characterWidth,
        float lineHeight,
        uint color)
    {
//        _perfWatch.Start("CursorRenderer.RenderBlockHollowCursor");
        try
        {
            var endPos = new float2(position.X + characterWidth, position.Y + lineHeight);
            drawList.AddRect(position, endPos, color, 0.0f, ImDrawFlags.None, 1.0f);
        }
        finally
        {
//            _perfWatch.Stop("CursorRenderer.RenderBlockHollowCursor");
        }
    }

    /// <summary>
    ///     Renders an underline cursor.
    /// </summary>
    private void RenderUnderlineCursor(
        ImDrawListPtr drawList,
        float2 position,
        float characterWidth,
        float lineHeight,
        uint color)
    {
//        _perfWatch.Start("CursorRenderer.RenderUnderlineCursor");
        try
        {
            const float underlineThickness = 2.0f;
            var startPos = new float2(position.X, position.Y + lineHeight - underlineThickness);
            var endPos = new float2(position.X + characterWidth, position.Y + lineHeight - underlineThickness);
            drawList.AddLine(startPos, endPos, color, underlineThickness);
        }
        finally
        {
//            _perfWatch.Stop("CursorRenderer.RenderUnderlineCursor");
        }
    }

    /// <summary>
    ///     Renders a bar cursor (vertical line).
    /// </summary>
    private void RenderBarCursor(
        ImDrawListPtr drawList,
        float2 position,
        float characterWidth,
        float lineHeight,
        uint color)
    {
//        _perfWatch.Start("CursorRenderer.RenderBarCursor");
        try
        {
            const float barThickness = 2.0f;
            var startPos = new float2(position.X, position.Y);
            var endPos = new float2(position.X, position.Y + lineHeight);
            drawList.AddLine(startPos, endPos, color, barThickness);
        }
        finally
        {
//            _perfWatch.Stop("CursorRenderer.RenderBarCursor");
        }
    }

    /// <summary>
    ///     Forces the cursor to be visible (resets blink state).
    ///     Useful when user provides input to ensure cursor is immediately visible.
    /// </summary>
    public void ForceVisible()
    {
        _blinkState = true;
        _lastBlinkTime = DateTime.Now;
    }

    /// <summary>
    ///     Resets the cursor blink state to default (visible).
    ///     Used when cursor style changes or terminal resets.
    /// </summary>
    public void ResetBlinkState()
    {
        _blinkState = true;
        _lastBlinkTime = DateTime.Now;
    }
}