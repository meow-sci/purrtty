using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using caTTY.Core.Types;
using caTTY.Display.Controllers.TerminalUi;

namespace caTTY.Display.Rendering;

public class CommandBufferBackingStore : ITerminalBackingStore
{
    private readonly List<DrawCommand> _commands = new();
    private bool _isCapturing;
    private CommandBufferDrawTarget? _recorder;

    // We need a way to delegate complex drawing back to TerminalUiRender helpers during playback
    // or we implement them here. 
    // Since RenderCurlyUnderline etc are in TerminalUiRender, we should probably expose them 
    // or move them to a utility class. 
    // For now, we will rely on a callback or interface during Draw.
    // Actually, ITerminalBackingStore.Draw takes ImDrawListPtr.
    // We need the helper logic. 
    // To solve this cleanly, we will make TerminalUiRender static helpers public or duplicate logic?
    // Duplicating logic is bad. 
    // Best approach: terminal render helpers should be in a static class or accessible.
    // For this implementation, I will assume we can access them or implementing them inline (if simple).
    // Start with basic text/rects. Decorations might need special handling.
    
    // Actually, we can pass an Action<ITerminalDrawTarget> to a 'Replay' method?
    // But ITerminalBackingStore.Draw just does the work.
    
    // Let's create the Recorder.
    
    public bool IsReady => _commands.Count > 0;

    public ITerminalDrawTarget GetDrawTarget()
    {
        if (_recorder == null)
        {
            throw new InvalidOperationException("Cannot get draw target before BeginCapture() is called");
        }
        return _recorder;
    }

    public bool BeginCapture(int width, int height)
    {
        if (_isCapturing) return false;
        _commands.Clear();
        _isCapturing = true;
        _recorder = new CommandBufferDrawTarget(_commands);
        return true;
    }

    public void EndCapture()
    {
        _isCapturing = false;
        _recorder = null;
    }

    public void Draw(ImDrawListPtr drawList, float2 position, float2 size)
    {
        // Replay commands
        foreach (var cmd in _commands)
        {
            switch (cmd.Type)
            {
                case DrawCommandType.RectFilled:
                    drawList.AddRectFilled(cmd.P1, cmd.P2, cmd.Color);
                    break;
                case DrawCommandType.Text:
                    ImGui.PushFont(cmd.Font, cmd.FontSize);
                    drawList.AddText(cmd.P1, cmd.Color, cmd.Text ?? string.Empty);
                    ImGui.PopFont();
                    break;
                case DrawCommandType.Line:
                    drawList.AddLine(cmd.P1, cmd.P2, cmd.Color, cmd.Thickness);
                    break;
                // For complex underlines, we'll need to link back to the renderer or implement logic here.
                // For now, let's implement placeholders or move logic if needed.
                case DrawCommandType.CurlyUnderline:
                    // TODO: Re-implement curly logic or call helper
                    TerminalDecorationRenderers.RenderCurlyUnderline(drawList, cmd.P1, cmd.FloatColor, cmd.Thickness, cmd.Width, cmd.Height);
                    break;
                case DrawCommandType.DottedUnderline:
                    TerminalDecorationRenderers.RenderDottedUnderline(drawList, cmd.P1, cmd.FloatColor, cmd.Thickness, cmd.Width, cmd.Height);
                    break;
                case DrawCommandType.DashedUnderline:
                    TerminalDecorationRenderers.RenderDashedUnderline(drawList, cmd.P1, cmd.FloatColor, cmd.Thickness, cmd.Width, cmd.Height);
                    break;
            }
        }
    }

    public void Dispose()
    {
        _commands.Clear();
    }
}

internal enum DrawCommandType
{
    RectFilled,
    Text,
    Line,
    CurlyUnderline,
    DottedUnderline,
    DashedUnderline
}

internal struct DrawCommand
{
    public DrawCommandType Type;
    public float2 P1;
    public float2 P2; // Used for PMax or P2
    public uint Color;
    public float4 FloatColor; // For decorations/opacity if needed
    public string? Text;
    public ImFontPtr Font;
    public float FontSize;
    public float Thickness;
    public float Width;  // For decorations
    public float Height; // For decorations
}

internal class CommandBufferDrawTarget : ITerminalDrawTarget
{
    private readonly List<DrawCommand> _commands;

    public CommandBufferDrawTarget(List<DrawCommand> commands)
    {
        _commands = commands;
    }

    public void AddRectFilled(float2 pMin, float2 pMax, uint col)
    {
        _commands.Add(new DrawCommand
        {
            Type = DrawCommandType.RectFilled,
            P1 = pMin,
            P2 = pMax,
            Color = col
        });
    }

    public void AddText(float2 pos, uint col, string text, ImFontPtr font, float fontSize)
    {
        _commands.Add(new DrawCommand
        {
            Type = DrawCommandType.Text,
            P1 = pos,
            Color = col,
            Text = text,
            Font = font,
            FontSize = fontSize
        });
    }

    public void DrawLine(float2 p1, float2 p2, uint col, float thickness)
    {
        _commands.Add(new DrawCommand
        {
            Type = DrawCommandType.Line,
            P1 = p1,
            P2 = p2,
            Color = col,
            Thickness = thickness
        });
    }

    public void DrawCurlyUnderline(float2 pos, float4 color, float thickness, float width, float height)
    {
        _commands.Add(new DrawCommand
        {
            Type = DrawCommandType.CurlyUnderline,
            P1 = pos,
            FloatColor = color,
            Thickness = thickness,
            Width = width,
            Height = height
        });
    }

    public void DrawDottedUnderline(float2 pos, float4 color, float thickness, float width, float height)
    {
        _commands.Add(new DrawCommand
        {
            Type = DrawCommandType.DottedUnderline,
            P1 = pos,
            FloatColor = color,
            Thickness = thickness,
            Width = width,
            Height = height
        });
    }

    public void DrawDashedUnderline(float2 pos, float4 color, float thickness, float width, float height)
    {
        _commands.Add(new DrawCommand
        {
            Type = DrawCommandType.DashedUnderline,
            P1 = pos,
            FloatColor = color,
            Thickness = thickness,
            Width = width,
            Height = height
        });
    }
}
