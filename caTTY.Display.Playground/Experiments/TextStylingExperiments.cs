using Brutal.ImGuiApi;
using caTTY.Core.Types;
using KSA;
using ImGui = Brutal.ImGuiApi.ImGui;
using float2 = Brutal.Numerics.float2;
using float4 = Brutal.Numerics.float4;

namespace caTTY.Playground.Experiments;

/// <summary>
///     Text styling experiments for testing different ImGui text attribute approaches.
///     This class implements the core experiments for task 1.6.
/// </summary>
public static class TextStylingExperiments
{
    // Experiment state
    private static int _selectedExperiment;

    private static readonly string[] _experimentNames =
    [
        "Text Attributes Test",
        "Font Style Variations",
        "Cursor Display Techniques",
        "Interactive Styling Controls",
        "Styling Limitations Analysis"
    ];

    // Text styling state
    private static bool _boldEnabled;
    private static bool _italicEnabled;
    private static bool _underlineEnabled;
    private static bool _strikethroughEnabled;
    private static bool _inverseEnabled;
    private static bool _dimEnabled;
    private static bool _blinkEnabled;

    // Cursor state
    private static int _cursorType; // 0=block, 1=underline, 2=bar, 3=block_hollow
    private static bool _cursorVisible = true;
    private static bool _cursorBlinking;
    private static DateTime _lastBlinkTime = DateTime.Now;
    private static bool _blinkState = true;

    // Color state
    private static int _foregroundColorIndex;
    private static int _backgroundColorIndex = 7; // Transparent by default

    private static readonly float4[] _colorPalette =
    [
        new(1.0f, 1.0f, 1.0f, 1.0f), // 0: White
        new(1.0f, 0.0f, 0.0f, 1.0f), // 1: Red
        new(0.0f, 1.0f, 0.0f, 1.0f), // 2: Green
        new(0.0f, 0.0f, 1.0f, 1.0f), // 3: Blue
        new(1.0f, 1.0f, 0.0f, 1.0f), // 4: Yellow
        new(1.0f, 0.0f, 1.0f, 1.0f), // 5: Magenta
        new(0.0f, 1.0f, 1.0f, 1.0f), // 6: Cyan
        new(0.0f, 0.0f, 0.0f, 0.0f), // 7: Transparent
        new(0.5f, 0.5f, 0.5f, 1.0f), // 8: Gray
        new(0.2f, 0.2f, 0.2f, 1.0f) // 9: Dark Gray
    ];

    // Font metrics
    private static readonly float _fontSize = 32.0f;
    private static float _charWidth;
    private static float _lineHeight;

    // Test content
    private static readonly string[] _testLines =
    [
        "The quick brown fox jumps over the lazy dog",
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
        "abcdefghijklmnopqrstuvwxyz",
        "0123456789!@#$%^&*()_+-=[]{}|;:,.<>?",
        "Bold text should appear heavier and darker",
        "Italic text should appear slanted or oblique",
        "Underlined text should have a line beneath",
        "Strikethrough text should have a line through",
        "Inverse text should swap foreground/background",
        "Dim text should appear lighter or faded"
    ];

    /// <summary>
    ///     Runs the text styling experiments.
    /// </summary>
    public static void Run()
    {
        try
        {
            Console.WriteLine("Starting Text Styling Experiments...");

            // Update cursor blinking
            UpdateCursorBlink();

            DrawExperiments();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in text styling experiments: {ex.Message}");
        }
    }

    private static void UpdateCursorBlink()
    {
        if (_cursorBlinking)
        {
            DateTime currentTime = DateTime.Now;
            if ((currentTime - _lastBlinkTime).TotalMilliseconds > 500) // 500ms blink interval
            {
                _blinkState = !_blinkState;
                _lastBlinkTime = currentTime;
            }
        }
        else
        {
            _blinkState = true; // Always visible when not blinking
        }
    }

    private static void PushHackFont(out bool fontUsed, float? size = null, bool bold = false, bool italic = false)
    {
        // Determine font name based on styling
        string fontName = "JetBrainsMonoNerdFontMono-Regular";
        if (bold && italic)
        {
            fontName = "JetBrainsMonoNerdFontMono-BoldItalic";
        }
        else if (bold)
        {
            fontName = "JetBrainsMonoNerdFontMono-Bold";
        }
        else if (italic)
        {
            fontName = "JetBrainsMonoNerdFontMono-Italic";
        }

        if (FontManager.Fonts.TryGetValue(fontName, out ImFontPtr fontPtr))
        {
            ImGui.PushFont(fontPtr, size ?? _fontSize);
            fontUsed = true;

            // Calculate font metrics
            _charWidth = _fontSize * 0.6f; // Monospace approximation
            _lineHeight = _fontSize + 2.0f; // Good vertical spacing
            return;
        }

        // Fallback to regular font if styled variant not available
        if (FontManager.Fonts.TryGetValue("JetBrainsMonoNerdFontMono-Regular", out fontPtr))
        {
            ImGui.PushFont(fontPtr, size ?? _fontSize);
            fontUsed = true;

            // Calculate font metrics
            _charWidth = _fontSize * 0.6f; // Monospace approximation
            _lineHeight = _fontSize + 2.0f; // Good vertical spacing
            return;
        }

        fontUsed = false;
        _charWidth = _fontSize * 0.6f;
        _lineHeight = _fontSize + 2.0f;
    }

    private static void MaybePopFont(bool wasUsed)
    {
        if (wasUsed)
        {
            ImGui.PopFont();
        }
    }

    /// <summary>
    ///     Draws the text styling experiments UI.
    /// </summary>
    public static void DrawExperiments()
    {
        // Update cursor blinking state
        UpdateCursorBlink();

        PushHackFont(out bool fontUsed);

        // Main experiment window
        ImGui.Begin("Text Styling Experiments");

        ImGui.Text("Text Styling and Cursor Experiments - Task 1.6");
        ImGui.Separator();

        // Experiment selector
        ImGui.Combo("Experiment", ref _selectedExperiment, _experimentNames, _experimentNames.Length);
        ImGui.Separator();

        // Draw the selected experiment
        switch (_selectedExperiment)
        {
            case 0:
                DrawTextAttributesTest();
                break;
            case 1:
                DrawFontStyleVariations();
                break;
            case 2:
                DrawCursorDisplayTechniques();
                break;
            case 3:
                DrawUnderlineStyleVariants();
                break;
            case 4:
                DrawInteractiveStylingControls();
                break;
            case 5:
                DrawStylingLimitationsAnalysis();
                break;
        }

        MaybePopFont(fontUsed);

        ImGui.End();
    }

    private static void DrawTextAttributesTest()
    {
        ImGui.Text("Text Attributes Testing");
        ImGui.Text("Testing different approaches to text styling in ImGui");
        ImGui.Separator();

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float2 windowPos = ImGui.GetCursorScreenPos();

        // Test different text attributes
        (string, bool, bool, bool, bool, bool)[] attributes = new[]
        {
            ("Normal Text", false, false, false, false, false),
            ("Bold Text (True Font)", true, false, false, false, false),
            ("Italic Text (True Font)", false, true, false, false, false),
            ("Bold Italic Text", true, true, false, false, false),
            ("Underlined Text", false, false, true, false, false),
            ("Strikethrough Text", false, false, false, true, false),
            ("Inverse Text", false, false, false, false, true)
        };

        for (int i = 0; i < attributes.Length; i++)
        {
            (string text, bool bold, bool italic, bool underline, bool strikethrough, bool inverse) = attributes[i];
            float y = windowPos.Y + (i * _lineHeight);

            DrawStyledText(drawList, new float2(windowPos.X, y), text,
                bold, italic, underline, strikethrough, inverse, false);
        }

        ImGui.Dummy(new float2(400, attributes.Length * _lineHeight));

        ImGui.Separator();
        ImGui.Text("Styling Approach Analysis:");
        ImGui.BulletText("Bold: True bold font (HackNerdFontMono-Bold) with fallback simulation");
        ImGui.BulletText("Italic: True italic font (HackNerdFontMono-Italic) support");
        ImGui.BulletText("Bold+Italic: Combined font variant (HackNerdFontMono-BoldItalic)");
        ImGui.BulletText("Underline: Custom drawing using DrawList.AddLine()");
        ImGui.BulletText("Strikethrough: Custom drawing using DrawList.AddLine()");
        ImGui.BulletText("Inverse: Swap foreground/background colors with visibility fixes");
        ImGui.BulletText("Dim: Reduce alpha or use darker color variant");
    }

    private static void DrawFontStyleVariations()
    {
        ImGui.Text("Font Style Variations");
        ImGui.Text("Comparing different font rendering techniques");
        ImGui.Separator();

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float2 windowPos = ImGui.GetCursorScreenPos();

        // Font size variations
        ImGui.Text("Font Size Variations:");
        float[] sizes = new[] { 16.0f, 24.0f, 32.0f, 48.0f };
        float currentY = windowPos.Y + _lineHeight;

        for (int i = 0; i < sizes.Length; i++)
        {
            PushHackFont(out bool sizedFontUsed, sizes[i]);

            var pos = new float2(windowPos.X, currentY);
            string text = $"Size {sizes[i]:F0}: The quick brown fox";
            drawList.AddText(pos, ImGui.ColorConvertFloat4ToU32(_colorPalette[0]), text);

            MaybePopFont(sizedFontUsed);

            currentY += sizes[i] + 4.0f; // Add some spacing
        }

        ImGui.Dummy(new float2(400, currentY - windowPos.Y));

        ImGui.Separator();
        ImGui.Text("Font Variant Testing:");

        float fontY = ImGui.GetCursorScreenPos().Y;
        (string, bool, bool)[] fontTests = new[]
        {
            ("Regular Font", false, false), ("Bold Font", true, false), ("Italic Font", false, true),
            ("Bold Italic Font", true, true)
        };

        for (int i = 0; i < fontTests.Length; i++)
        {
            (string label, bool bold, bool italic) = fontTests[i];
            var pos = new float2(windowPos.X, fontY + (i * _lineHeight));

            PushHackFont(out bool variantFontUsed, _fontSize, bold, italic);
            drawList.AddText(pos, ImGui.ColorConvertFloat4ToU32(_colorPalette[0]), label);
            MaybePopFont(variantFontUsed);
        }

        ImGui.Dummy(new float2(400, fontTests.Length * _lineHeight));

        ImGui.Separator();
        ImGui.Text("Bold Simulation Techniques (fallback when no bold font):");

        float boldY = ImGui.GetCursorScreenPos().Y;
        string boldText = "Bold Text Simulation";

        // Technique 1: Multiple draws with offsets
        ImGui.Text("1. Multiple draws with pixel offsets:");
        DrawBoldText(drawList, new float2(windowPos.X + 20, boldY + _lineHeight), boldText,
            BoldTechnique.MultipleDraws);

        // Technique 2: Outline effect
        ImGui.Text("2. Outline effect:");
        DrawBoldText(drawList, new float2(windowPos.X + 20, boldY + (_lineHeight * 3)), boldText,
            BoldTechnique.Outline);

        // Technique 3: Color intensity
        ImGui.Text("3. Color intensity variation:");
        DrawBoldText(drawList, new float2(windowPos.X + 20, boldY + (_lineHeight * 5)), boldText,
            BoldTechnique.ColorIntensity);

        ImGui.Dummy(new float2(400, _lineHeight * 6));
    }

    private static void DrawBoldText(ImDrawListPtr drawList, float2 pos, string text, BoldTechnique technique)
    {
        uint color = ImGui.ColorConvertFloat4ToU32(_colorPalette[0]);

        switch (technique)
        {
            case BoldTechnique.MultipleDraws:
                // Draw text multiple times with slight offsets
                drawList.AddText(pos, color, text);
                drawList.AddText(new float2(pos.X + 0.5f, pos.Y), color, text);
                drawList.AddText(new float2(pos.X, pos.Y + 0.5f), color, text);
                drawList.AddText(new float2(pos.X + 0.5f, pos.Y + 0.5f), color, text);
                break;

            case BoldTechnique.Outline:
                // Draw outline first, then main text
                uint outlineColor = ImGui.ColorConvertFloat4ToU32(new float4(0.3f, 0.3f, 0.3f, 1.0f));
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx != 0 || dy != 0)
                        {
                            drawList.AddText(new float2(pos.X + dx, pos.Y + dy), outlineColor, text);
                        }
                    }
                }

                drawList.AddText(pos, color, text);
                break;

            case BoldTechnique.ColorIntensity:
                // Use brighter/more intense color
                uint brightColor = ImGui.ColorConvertFloat4ToU32(new float4(1.2f, 1.2f, 1.2f, 1.0f));
                drawList.AddText(pos, brightColor, text);
                break;
        }
    }

    private static void DrawCursorDisplayTechniques()
    {
        ImGui.Text("Cursor Display Techniques");
        ImGui.Text("Different cursor styles and blinking behavior");
        ImGui.Separator();

        // Cursor controls
        ImGui.Text("Cursor Controls:");
        string[] cursorTypes = new[] { "Block Cursor", "Underline Cursor", "Bar Cursor", "Block Hollow Cursor" };
        ImGui.Combo("Cursor Type", ref _cursorType, cursorTypes, cursorTypes.Length);
        ImGui.Checkbox("Cursor Visible", ref _cursorVisible);
        ImGui.Checkbox("Cursor Blinking", ref _cursorBlinking);

        ImGui.Separator();

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float2 windowPos = ImGui.GetCursorScreenPos();

        // Demo text with cursor
        string demoText = "Sample text with cursor here: ";
        var cursorPos = new float2(windowPos.X + (demoText.Length * _charWidth), windowPos.Y);

        // Draw the demo text
        drawList.AddText(windowPos, ImGui.ColorConvertFloat4ToU32(_colorPalette[0]), demoText);

        // Draw cursor if visible and not blinking or blink state is on
        if (_cursorVisible && (!_cursorBlinking || _blinkState))
        {
            DrawCursor(drawList, cursorPos, _cursorType);
        }

        ImGui.Dummy(new float2(400, _lineHeight * 2));

        ImGui.Separator();
        ImGui.Text("Cursor Style Demonstrations:");

        // Show all cursor types
        float cursorDemoY = ImGui.GetCursorScreenPos().Y;
        string[] cursorNames = new[] { "Block", "Underline", "Bar", "Block Hollow" };

        for (int i = 0; i < 4; i++)
        {
            var pos = new float2(windowPos.X, cursorDemoY + (i * _lineHeight));
            var textPos = new float2(pos.X + (_charWidth * 2), pos.Y);

            // Draw cursor
            DrawCursor(drawList, pos, i);

            // Draw label
            drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(_colorPalette[0]),
                $"{cursorNames[i]} cursor");
        }

        ImGui.Dummy(new float2(400, _lineHeight * 5));

        ImGui.Separator();
        ImGui.Text("Cursor Implementation Notes:");
        ImGui.BulletText("Block: Filled rectangle covering entire character cell");
        ImGui.BulletText("Underline: Horizontal line at bottom of character cell");
        ImGui.BulletText("Bar: Vertical line at left edge of character cell");
        ImGui.BulletText("Block Hollow: Outline rectangle of character cell");
        ImGui.BulletText("Blinking: Toggle visibility every 500ms when enabled");
        ImGui.BulletText("All cursors use ImGui DrawList for custom rendering");
    }

    private static void DrawCursor(ImDrawListPtr drawList, float2 pos, int cursorType)
    {
        uint cursorColor = ImGui.ColorConvertFloat4ToU32(_colorPalette[0]); // White cursor

        switch (cursorType)
        {
            case 0: // Block cursor
                var blockEnd = new float2(pos.X + _charWidth, pos.Y + _lineHeight);
                drawList.AddRectFilled(pos, blockEnd, cursorColor);
                break;

            case 1: // Underline cursor
                var underlineStart = new float2(pos.X, pos.Y + _lineHeight - 2);
                var underlineEnd = new float2(pos.X + _charWidth, pos.Y + _lineHeight - 2);
                drawList.AddLine(underlineStart, underlineEnd, cursorColor, 2.0f);
                break;

            case 2: // Bar cursor
                var barStart = new float2(pos.X, pos.Y);
                var barEnd = new float2(pos.X, pos.Y + _lineHeight);
                drawList.AddLine(barStart, barEnd, cursorColor, 2.0f);
                break;

            case 3: // Block hollow cursor
                var hollowEnd = new float2(pos.X + _charWidth, pos.Y + _lineHeight);
                drawList.AddRect(pos, hollowEnd, cursorColor, 0.0f, ImDrawFlags.None, 1.0f);
                break;
        }
    }

    private static void DrawUnderlineStyleVariants()
    {
        ImGui.Text("Underline Style Variants");
        ImGui.Text("Testing different underline styles with colors (copied from TerminalController)");
        ImGui.Separator();

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float2 windowPos = ImGui.GetCursorScreenPos();

        // Test different underline styles with colors
        (string, UnderlineStyle, float4)[] underlineTests = new[]
        {
            ("Single Red Underline", UnderlineStyle.Single, new float4(1.0f, 0.0f, 0.0f, 1.0f)), // Red
            ("Double Green Underline", UnderlineStyle.Double, new float4(0.0f, 1.0f, 0.0f, 1.0f)), // Green
            ("Curly Yellow Underline", UnderlineStyle.Curly, new float4(1.0f, 1.0f, 0.0f, 1.0f)), // Yellow
            ("Dotted Yellow Underline", UnderlineStyle.Dotted, new float4(1.0f, 1.0f, 0.0f, 1.0f)), // Yellow
            ("Dashed Magenta Underline", UnderlineStyle.Dashed, new float4(1.0f, 0.0f, 1.0f, 1.0f)), // Magenta
        };

        for (int i = 0; i < underlineTests.Length; i++)
        {
            (string text, UnderlineStyle style, float4 color) = underlineTests[i];
            float y = windowPos.Y + (i * _lineHeight);
            var pos = new float2(windowPos.X, y);

            // Draw the text
            drawList.AddText(pos, ImGui.ColorConvertFloat4ToU32(_colorPalette[0]), text);

            // Draw the underline with the specified style and color
            DrawUnderlineStyle(drawList, pos, text.Length, style, color);
        }

        ImGui.Dummy(new float2(500, underlineTests.Length * _lineHeight));

        ImGui.Separator();
        ImGui.Text("Style Implementation Details:");
        ImGui.BulletText("Single: Straight line with 3px thickness");
        ImGui.BulletText("Double: Two parallel lines with 4px spacing");
        ImGui.BulletText("Curly: Bezier curves with 4.0f amplitude for wavy effect");
        ImGui.BulletText("Dotted: 3px dots with 3px spacing");
        ImGui.BulletText("Dashed: 6px dashes with 4px spacing");
    }

    private static void DrawUnderlineStyle(ImDrawListPtr drawList, float2 pos, int textLength, UnderlineStyle style, float4 color)
    {
        float underlineY = pos.Y + _lineHeight - 2;
        uint underlineColor = ImGui.ColorConvertFloat4ToU32(color);
        float thickness = 3.0f;

        switch (style)
        {
            case UnderlineStyle.Single:
                var singleStart = new float2(pos.X, underlineY);
                var singleEnd = new float2(pos.X + (textLength * _charWidth), underlineY);
                drawList.AddLine(singleStart, singleEnd, underlineColor, thickness);
                break;

            case UnderlineStyle.Double:
                // First line (bottom)
                var doubleStart1 = new float2(pos.X, underlineY);
                var doubleEnd1 = new float2(pos.X + (textLength * _charWidth), underlineY);
                drawList.AddLine(doubleStart1, doubleEnd1, underlineColor, thickness);

                // Second line (top) - spaced 4 pixels above
                var doubleStart2 = new float2(pos.X, underlineY - 4);
                var doubleEnd2 = new float2(pos.X + (textLength * _charWidth), underlineY - 4);
                drawList.AddLine(doubleStart2, doubleEnd2, underlineColor, thickness);
                break;

            case UnderlineStyle.Curly:
                RenderCurlyUnderline(drawList, pos, textLength, color, thickness);
                break;

            case UnderlineStyle.Dotted:
                RenderDottedUnderline(drawList, pos, textLength, color, thickness);
                break;

            case UnderlineStyle.Dashed:
                RenderDashedUnderline(drawList, pos, textLength, color, thickness);
                break;
        }
    }

    private static void RenderCurlyUnderline(ImDrawListPtr drawList, float2 pos, int textLength, float4 underlineColor, float thickness)
    {
        float underlineY = pos.Y + _lineHeight - 2;
        uint color = ImGui.ColorConvertFloat4ToU32(underlineColor);
        float curlyThickness = Math.Max(3.0f, thickness);

        // Create a wavy line using multiple bezier curve segments
        float waveHeight = 4.0f; // Amplitude for visible waves
        float totalWidth = textLength * _charWidth;
        float segmentWidth = _charWidth / 2.0f; // 2 wave segments per character
        int numSegments = (int)(totalWidth / segmentWidth);

        for (int i = 0; i < numSegments; i++)
        {
            float startX = pos.X + (i * segmentWidth);
            float endX = pos.X + ((i + 1) * segmentWidth);

            // Alternate wave direction for each segment
            float controlOffset = (i % 2 == 0) ? -waveHeight : waveHeight;

            var p1 = new float2(startX, underlineY);
            var p2 = new float2(startX + segmentWidth * 0.3f, underlineY + controlOffset);
            var p3 = new float2(startX + segmentWidth * 0.7f, underlineY - controlOffset);
            var p4 = new float2(endX, underlineY);

            drawList.AddBezierCubic(p1, p2, p3, p4, color, curlyThickness);
        }
    }

    private static void RenderDottedUnderline(ImDrawListPtr drawList, float2 pos, int textLength, float4 underlineColor, float thickness)
    {
        float underlineY = pos.Y + _lineHeight - 2;
        uint color = ImGui.ColorConvertFloat4ToU32(underlineColor);
        float dottedThickness = Math.Max(3.0f, thickness);

        float dotSize = 3.0f;
        float spacing = 3.0f;
        float totalStep = dotSize + spacing;
        float totalWidth = textLength * _charWidth;

        for (float x = pos.X; x < pos.X + totalWidth - dotSize; x += totalStep)
        {
            float dotEnd = Math.Min(x + dotSize, pos.X + totalWidth);
            var dotStart = new float2(x, underlineY);
            var dotEndPos = new float2(dotEnd, underlineY);
            drawList.AddLine(dotStart, dotEndPos, color, dottedThickness);
        }
    }

    private static void RenderDashedUnderline(ImDrawListPtr drawList, float2 pos, int textLength, float4 underlineColor, float thickness)
    {
        float underlineY = pos.Y + _lineHeight - 2;
        uint color = ImGui.ColorConvertFloat4ToU32(underlineColor);
        float dashedThickness = Math.Max(3.0f, thickness);

        float dashSize = 6.0f;
        float spacing = 4.0f;
        float totalStep = dashSize + spacing;
        float totalWidth = textLength * _charWidth;

        for (float x = pos.X; x < pos.X + totalWidth - dashSize; x += totalStep)
        {
            float dashEnd = Math.Min(x + dashSize, pos.X + totalWidth);
            var dashStart = new float2(x, underlineY);
            var dashEndPos = new float2(dashEnd, underlineY);
            drawList.AddLine(dashStart, dashEndPos, color, dashedThickness);
        }
    }

    private static void DrawInteractiveStylingControls()
    {
        ImGui.Text("Interactive Styling Controls");
        ImGui.Text("Real-time text styling with interactive controls");
        ImGui.Separator();

        // Styling controls
        ImGui.Text("Text Attributes:");
        ImGui.Checkbox("Bold", ref _boldEnabled);
        ImGui.SameLine();
        ImGui.Checkbox("Italic", ref _italicEnabled);
        ImGui.SameLine();
        ImGui.Checkbox("Underline", ref _underlineEnabled);

        ImGui.Checkbox("Strikethrough", ref _strikethroughEnabled);
        ImGui.SameLine();
        ImGui.Checkbox("Inverse", ref _inverseEnabled);
        ImGui.SameLine();
        ImGui.Checkbox("Dim", ref _dimEnabled);

        ImGui.Checkbox("Blink", ref _blinkEnabled);

        ImGui.Separator();

        // Color controls
        ImGui.Text("Colors:");
        string[] colorNames = new[]
        {
            "White", "Red", "Green", "Blue", "Yellow", "Magenta", "Cyan", "Transparent", "Gray", "Dark Gray"
        };
        ImGui.Combo("Foreground", ref _foregroundColorIndex, colorNames, colorNames.Length);
        ImGui.Combo("Background", ref _backgroundColorIndex, colorNames, colorNames.Length);

        // Debug color information
        if (_inverseEnabled)
        {
            ImGui.Separator();
            ImGui.Text("Debug - Color Values:");
            float4 fg = _colorPalette[_foregroundColorIndex];
            float4 bg = _colorPalette[_backgroundColorIndex];
            ImGui.Text($"Original FG: R={fg.X:F2} G={fg.Y:F2} B={fg.Z:F2} A={fg.W:F2}");
            ImGui.Text($"Original BG: R={bg.X:F2} G={bg.Y:F2} B={bg.Z:F2} A={bg.W:F2}");
            ImGui.Text($"After swap: FG=BG, BG=FG");
        }

        ImGui.Separator();

        // Live preview
        ImGui.Text("Live Preview:");
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float2 windowPos = ImGui.GetCursorScreenPos();

        // Draw sample text with current styling
        string sampleText = "Sample text with current styling applied";
        DrawStyledText(drawList, windowPos, sampleText,
            _boldEnabled, _italicEnabled, _underlineEnabled, _strikethroughEnabled,
            _inverseEnabled, _dimEnabled, _blinkEnabled);

        ImGui.Dummy(new float2(400, _lineHeight * 2));

        ImGui.Separator();

        // Multiple lines with different combinations
        ImGui.Text("Style Combination Examples:");
        float exampleY = ImGui.GetCursorScreenPos().Y;

        (string, bool, bool, bool, bool, bool, bool)[] examples = new[]
        {
            ("Normal text", false, false, false, false, false, false),
            ("Bold + Underline", true, false, true, false, false, false),
            ("Italic + Strikethrough", false, true, false, true, false, false),
            ("Inverse + Bold", true, false, false, false, true, false),
            ("All attributes", true, true, true, true, false, true)
        };

        for (int i = 0; i < examples.Length; i++)
        {
            (string text, bool bold, bool italic, bool underline, bool strikethrough, bool inverse, bool dim) =
                examples[i];
            var pos = new float2(windowPos.X, exampleY + (i * _lineHeight));

            DrawStyledText(drawList, pos, text, bold, italic, underline, strikethrough, inverse, dim);
        }

        ImGui.Dummy(new float2(400, examples.Length * _lineHeight));
    }

    private static void DrawStyledText(ImDrawListPtr drawList, float2 pos, string text,
        bool bold, bool italic, bool underline, bool strikethrough, bool inverse, bool dim, bool blink = false)
    {
        // Handle blinking
        if (blink && _blinkEnabled && !_blinkState)
        {
            return; // Don't draw if blinking and in off state
        }

        // Determine colors
        float4 fgColor = _colorPalette[_foregroundColorIndex];
        float4 bgColor = _colorPalette[_backgroundColorIndex];

        if (inverse)
        {
            // Simple inverse: swap colors and ensure visibility
            (fgColor, bgColor) = (bgColor, fgColor);

            // If background is now transparent (was foreground), make it black
            if (bgColor.W < 0.1f)
            {
                bgColor = new float4(0.0f, 0.0f, 0.0f, 1.0f); // Black background
            }

            // If foreground is now transparent (was background), make it white
            if (fgColor.W < 0.1f)
            {
                fgColor = new float4(1.0f, 1.0f, 1.0f, 1.0f); // White foreground
            }

            // Simple contrast check: if both colors are very similar, force contrast
            float fgSum = fgColor.X + fgColor.Y + fgColor.Z;
            float bgSum = bgColor.X + bgColor.Y + bgColor.Z;

            if (Math.Abs(fgSum - bgSum) < 0.5f) // Colors too similar
            {
                if (bgSum < 1.5f) // Dark background
                {
                    fgColor = new float4(1.0f, 1.0f, 1.0f, 1.0f); // White text
                }
                else // Light background
                {
                    fgColor = new float4(0.0f, 0.0f, 0.0f, 1.0f); // Black text
                }
            }
        }

        if (dim)
        {
            fgColor = new float4(fgColor.X * 0.6f, fgColor.Y * 0.6f, fgColor.Z * 0.6f, fgColor.W);
        }

        // Draw background if not transparent
        if (bgColor.W > 0)
        {
            var bgEnd = new float2(pos.X + (text.Length * _charWidth), pos.Y + _lineHeight);
            drawList.AddRectFilled(pos, bgEnd, ImGui.ColorConvertFloat4ToU32(bgColor));
        }

        // Use appropriate font for bold/italic
        PushHackFont(out bool styledFontUsed, _fontSize, bold, italic);

        // Draw text
        uint textColor = ImGui.ColorConvertFloat4ToU32(fgColor);
        if (bold && !styledFontUsed)
        {
            // Fallback to bold simulation if no bold font available
            DrawBoldText(drawList, pos, text, BoldTechnique.MultipleDraws);
        }
        else
        {
            drawList.AddText(pos, textColor, text);
        }

        MaybePopFont(styledFontUsed);

        // Draw underline if enabled
        if (underline)
        {
            float underlineY = pos.Y + _lineHeight - 2;
            var underlineStart = new float2(pos.X, underlineY);
            var underlineEnd = new float2(pos.X + (text.Length * _charWidth), underlineY);
            drawList.AddLine(underlineStart, underlineEnd, textColor);
        }

        // Draw strikethrough if enabled
        if (strikethrough)
        {
            float strikeY = pos.Y + (_lineHeight * 0.5f);
            var strikeStart = new float2(pos.X, strikeY);
            var strikeEnd = new float2(pos.X + (text.Length * _charWidth), strikeY);
            drawList.AddLine(strikeStart, strikeEnd, textColor);
        }
    }

    private static void DrawStylingLimitationsAnalysis()
    {
        ImGui.Text("Styling Capabilities and Limitations Analysis");
        ImGui.Text("Comprehensive analysis of ImGui text styling capabilities");
        ImGui.Separator();

        ImGui.Text("✅ Supported Features:");
        ImGui.BulletText("Bold text (true font + simulation fallback)");
        ImGui.BulletText("Italic text (true font support)");
        ImGui.BulletText("Bold+Italic combination (true font support)");
        ImGui.BulletText("Underline (custom line drawing)");
        ImGui.BulletText("Advanced underline styles (double, curly, dotted, dashed)");
        ImGui.BulletText("Strikethrough (custom line drawing)");
        ImGui.BulletText("Color variations (foreground/background)");
        ImGui.BulletText("Inverse video (color swapping with visibility fixes)");
        ImGui.BulletText("Dim text (alpha/color reduction)");
        ImGui.BulletText("Cursor variations (block, underline, beam)");
        ImGui.BulletText("Cursor blinking (visibility toggling)");
        ImGui.BulletText("Custom drawing via DrawList");

        ImGui.Separator();
        ImGui.Text("❌ Limitations:");
        ImGui.BulletText("Font weight variations beyond bold (no semi-bold, extra-bold)");
        ImGui.BulletText("Complex text shaping (ligatures, kerning)");
        ImGui.BulletText("Proportional font support (monospace assumption)");

        ImGui.Separator();
        ImGui.Text("🔧 Workarounds and Solutions:");
        ImGui.BulletText("Bold: True font variants with simulation fallback");
        ImGui.BulletText("Italic: True font variants (HackNerdFontMono-Italic)");
        ImGui.BulletText("Underline styles: Custom line patterns with DrawList");
        ImGui.BulletText("Blinking: Timer-based visibility toggling");
        ImGui.BulletText("Inverse: Color swapping with visibility validation");
        ImGui.BulletText("Advanced effects: Combine multiple DrawList operations");

        ImGui.Separator();
        ImGui.Text("📊 Performance Considerations:");
        ImGui.BulletText("Bold simulation adds 4x draw calls per character");
        ImGui.BulletText("Custom decorations require additional DrawList calls");
        ImGui.BulletText("Blinking requires continuous frame updates");
        ImGui.BulletText("Background colors add rectangle draw calls");
        ImGui.BulletText("Consider batching for large terminal grids");

        ImGui.Separator();
        ImGui.Text("🎯 Recommendations for Terminal Implementation:");
        ImGui.BulletText("Use DrawList for all custom styling effects");
        ImGui.BulletText("Implement bold as configurable (quality vs performance)");
        ImGui.BulletText("Cache styled text measurements for positioning");
        ImGui.BulletText("Batch similar styling operations together");
        ImGui.BulletText("Consider LOD for distant or small text");
        ImGui.BulletText("Use texture atlases for repeated decorative elements");

        ImGui.Separator();
        ImGui.Text("📋 Implementation Priority for Terminal:");
        ImGui.Text("1. High Priority: Colors, bold simulation, underline, cursor");
        ImGui.Text("2. Medium Priority: Strikethrough, inverse, dim, blinking");
        ImGui.Text("3. Low Priority: Advanced underline styles, italic alternatives");
    }

    private enum BoldTechnique
    {
        MultipleDraws,
        Outline,
        ColorIntensity
    }
}
