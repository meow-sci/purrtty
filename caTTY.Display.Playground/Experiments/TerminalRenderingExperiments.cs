using Brutal.ImGuiApi;
using caTTY.Playground.Rendering;
using KSA;
using ImGui = Brutal.ImGuiApi.ImGui;
using float2 = Brutal.Numerics.float2;
using float4 = Brutal.Numerics.float4;

namespace caTTY.Playground.Experiments;

/// <summary>
///     Terminal rendering experiments for testing different ImGui approaches.
///     This class implements the core experiments for task 1.5.
/// </summary>
public static class TerminalRenderingExperiments
{
    // Experiment state
    private static int _selectedExperiment;

    private static readonly string[] _experimentNames =
    [
        "Character Grid Basic",
        "Fixed-Width Font Test",
        "Color Experiments",
        "Grid Alignment Test",
        "Performance Comparison",
        "Text Styling Experiments",
        "Mouse Input - Scrolling Test",
        "Keyboard Focus - Line Discipline Test",
        "Window Resize Detection Test",
        "Terminal Resize Integration Test",
        "Text Selection Experiments"
    ];

    // Terminal simulation data
    private static readonly int TerminalWidth = 80;
    private static readonly int TerminalHeight = 24;
    private static readonly char[,] _terminalBuffer = new char[TerminalHeight, TerminalWidth];
    private static readonly float4[,] _foregroundColors = new float4[TerminalHeight, TerminalWidth];
    private static readonly float4[,] _backgroundColors = new float4[TerminalHeight, TerminalWidth];

    // Color palette for experiments (using Brutal.Numerics.float4)
    private static readonly float4[] _colorPalette =
    [
        new(1.0f, 1.0f, 1.0f, 1.0f), // White
        new(1.0f, 0.0f, 0.0f, 1.0f), // Red
        new(0.0f, 1.0f, 0.0f, 1.0f), // Green
        new(0.0f, 0.0f, 1.0f, 1.0f), // Blue
        new(1.0f, 1.0f, 0.0f, 1.0f), // Yellow
        new(1.0f, 0.0f, 1.0f, 1.0f), // Magenta
        new(0.0f, 1.0f, 1.0f, 1.0f), // Cyan
        new(0.5f, 0.5f, 0.5f, 1.0f) // Gray
    ];

    // Performance tracking
    private static readonly List<float> _renderTimes = new();
    private static DateTime _lastFrameTime = DateTime.Now;

    // Window resize tracking
    private static float2 _lastWindowSize = new(0, 0);
    private static float2 _currentWindowSize = new(0, 0);
    private static readonly List<string> _resizeEvents = new();
    private static DateTime _lastResizeTime = DateTime.MinValue;

    // Font metrics
    private static readonly float _fontSize = 32.0f;
    private static float _charWidth;
    private static float _lineHeight;

    static TerminalRenderingExperiments()
    {
        InitializeTerminalBuffer();
    }

    /// <summary>
    ///     Runs the terminal rendering experiments.
    /// </summary>
    public static void Run()
    {
        try
        {
            StandaloneImGui.Run(DrawExperiments);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize ImGui context: {ex.Message}");
            Console.WriteLine("KSA DLLs not available or graphics initialization failed.");
            Console.WriteLine("To run full experiments, ensure KSA is installed and graphics drivers are available.");
        }
    }

    private static void InitializeTerminalBuffer()
    {
        var random = new Random();
        string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()_+-=[]{}|;:,.<>? ";

        for (int row = 0; row < TerminalHeight; row++)
        {
            for (int col = 0; col < TerminalWidth; col++)
            {
                // Create some pattern for testing
                if (row == 0 || row == TerminalHeight - 1 || col == 0 || col == TerminalWidth - 1)
                {
                    // Border
                    _terminalBuffer[row, col] = '#';
                    _foregroundColors[row, col] = _colorPalette[2]; // Green
                    _backgroundColors[row, col] = new float4(0, 0, 0, 0);
                }
                else if (row % 3 == 0 && col % 10 == 0)
                {
                    // Markers
                    _terminalBuffer[row, col] = '+';
                    _foregroundColors[row, col] = _colorPalette[4]; // Yellow
                    _backgroundColors[row, col] = _colorPalette[7]; // Gray background
                }
                else if (random.NextDouble() < 0.7)
                {
                    // Random content
                    _terminalBuffer[row, col] = chars[random.Next(chars.Length)];
                    _foregroundColors[row, col] = _colorPalette[random.Next(_colorPalette.Length)];
                    _backgroundColors[row, col] = random.NextDouble() < 0.1
                        ? _colorPalette[random.Next(_colorPalette.Length)]
                        : new float4(0, 0, 0, 0);
                }
                else
                {
                    // Empty space
                    _terminalBuffer[row, col] = ' ';
                    _foregroundColors[row, col] = _colorPalette[0]; // White
                    _backgroundColors[row, col] = new float4(0, 0, 0, 0);
                }
            }
        }

        // Add some test patterns
        string testLine = "The quick brown fox jumps over the lazy dog 1234567890";
        for (int i = 0; i < Math.Min(testLine.Length, TerminalWidth - 2); i++)
        {
            _terminalBuffer[2, i + 1] = testLine[i];
            _foregroundColors[2, i + 1] = _colorPalette[0]; // White
            _backgroundColors[2, i + 1] = new float4(0, 0, 0, 0);
        }

        // Color test line
        string colorTestLine = "Color Test: ";
        for (int i = 0; i < colorTestLine.Length && i < TerminalWidth - 10; i++)
        {
            _terminalBuffer[4, i + 1] = colorTestLine[i];
            _foregroundColors[4, i + 1] = _colorPalette[i % _colorPalette.Length];
            _backgroundColors[4, i + 1] = new float4(0, 0, 0, 0);
        }
    }

    private static void PushHackFont(out bool fontUsed, float? size = null)
    {
        if (FontManager.Fonts.TryGetValue("JetBrainsMonoNerdFontMono-Regular", out ImFontPtr fontPtr))
        {
            ImGui.PushFont(fontPtr, size ?? _fontSize);
            fontUsed = true;
            return;
        }

        fontUsed = false;
    }

    private static void MaybePopFont(bool wasUsed)
    {
        if (wasUsed)
        {
            ImGui.PopFont();
        }
    }

    private static void DrawExperiments()
    {
        PushHackFont(out bool fontUsed);

        // Track frame time for performance analysis
        DateTime currentTime = DateTime.Now;
        float frameTime = (float)(currentTime - _lastFrameTime).TotalMilliseconds;
        _lastFrameTime = currentTime;
        _renderTimes.Add(frameTime);
        if (_renderTimes.Count > 100)
        {
            _renderTimes.RemoveAt(0);
        }

        // Calculate font metrics
        _charWidth = _fontSize * 0.6f; // Monospace approximation
        _lineHeight = _fontSize + 2.0f; // Good vertical spacing

        // Main experiment window
        ImGui.Begin("Terminal Rendering Experiments");

        ImGui.Text("Terminal Rendering Experiments - Full Implementation");
        ImGui.Separator();

        // Experiment selector
        ImGui.Combo("Experiment", ref _selectedExperiment, _experimentNames, _experimentNames.Length);
        ImGui.Separator();

        // Draw the selected experiment
        switch (_selectedExperiment)
        {
            case 0:
                DrawCharacterGridBasic();
                break;
            case 1:
                DrawFixedWidthFontTest();
                break;
            case 2:
                DrawColorExperiments();
                break;
            case 3:
                DrawGridAlignmentTest();
                break;
            case 4:
                DrawPerformanceComparison();
                break;
            case 5:
                TextStylingExperiments.DrawExperiments();
                break;
            case 6:
                MouseInputExperiments.DrawExperiments();
                break;
            case 7:
                TerminalLineDisciplineExperiments.DrawExperiments();
                break;
            case 8:
                DrawWindowResizeDetectionTest();
                break;
            case 9:
                DrawTerminalResizeIntegrationTest();
                break;
            case 10:
                TextSelectionExperiments.DrawExperiments();
                break;
        }

        MaybePopFont(fontUsed);

        ImGui.End();
    }

    private static void DrawCharacterGridBasic()
    {
        ImGui.Text("Character Grid Basic Rendering");
        ImGui.Text("Approach: Character-by-character positioning using ImGui DrawList");
        ImGui.Separator();

        // Get the draw list for custom drawing
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float2 windowPos = ImGui.GetCursorScreenPos();

        // Draw terminal grid
        for (int row = 0; row < TerminalHeight; row++)
        {
            for (int col = 0; col < TerminalWidth; col++)
            {
                float x = windowPos.X + (col * _charWidth);
                float y = windowPos.Y + (row * _lineHeight);
                var pos = new float2(x, y);

                // Draw background if not transparent
                float4 bgColor = _backgroundColors[row, col];
                if (bgColor.W > 0) // Alpha > 0
                {
                    var bgRect = new float2(x + _charWidth, y + _lineHeight);
                    drawList.AddRectFilled(pos, bgRect, ImGui.ColorConvertFloat4ToU32(bgColor));
                }

                // Draw character
                char ch = _terminalBuffer[row, col];
                if (ch != ' ')
                {
                    float4 fgColor = _foregroundColors[row, col];
                    drawList.AddText(pos, ImGui.ColorConvertFloat4ToU32(fgColor), ch.ToString());
                }
            }
        }

        // Reserve space for the terminal
        ImGui.Dummy(new float2(TerminalWidth * _charWidth, TerminalHeight * _lineHeight));
    }

    private static void DrawFixedWidthFontTest()
    {
        ImGui.Text("Fixed-Width Font Testing");
        ImGui.Text("Comparing different font rendering approaches");
        ImGui.Separator();

        ImGui.Text("Approach 1: ImGui.Text() with monospace assumption");
        ImGui.Text("ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        ImGui.Text("0123456789!@#$%^&*()_+-=");
        ImGui.Text("||||||||||||||||||||||||||||");

        ImGui.Separator();
        ImGui.Text("Approach 2: Character-by-character positioning");

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float2 windowPos = ImGui.GetCursorScreenPos();
        string testText = "Character-by-character: ABCD1234!@#$";

        for (int i = 0; i < testText.Length; i++)
        {
            float x = windowPos.X + (i * _charWidth);
            float y = windowPos.Y;
            var pos = new float2(x, y);
            drawList.AddText(pos, ImGui.ColorConvertFloat4ToU32(_colorPalette[0]), testText[i].ToString());
        }

        ImGui.Dummy(new float2(testText.Length * _charWidth, _lineHeight));
    }

    private static void DrawColorExperiments()
    {
        ImGui.Text("Color Experiments");
        ImGui.Text("Testing foreground and background colors");
        ImGui.Separator();

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float2 windowPos = ImGui.GetCursorScreenPos();

        // Color palette display
        ImGui.Text("Color Palette:");
        for (int i = 0; i < _colorPalette.Length; i++)
        {
            float x = windowPos.X + (i * _charWidth * 3);
            float y = windowPos.Y + _lineHeight;
            var pos = new float2(x, y);
            var bgRect = new float2(x + (_charWidth * 2), y + _lineHeight);

            // Background color
            drawList.AddRectFilled(pos, bgRect, ImGui.ColorConvertFloat4ToU32(_colorPalette[i]));
            // Text with contrasting color
            float4 textColor = i == 0 ? _colorPalette[1] : _colorPalette[0]; // Use red on white, white on others
            drawList.AddText(pos, ImGui.ColorConvertFloat4ToU32(textColor), $"{i}");
        }

        ImGui.Dummy(new float2(_colorPalette.Length * _charWidth * 3, _lineHeight * 3));

        // Color combinations test
        ImGui.Text("Color Combinations:");
        string[] testColors = new[] { "Red/Black", "Green/Black", "Blue/White", "Yellow/Blue" };
        var combinations = new (float4 fg, float4 bg)[]
        {
            (_colorPalette[1], new float4(0, 0, 0, 1)), // Red on Black
            (_colorPalette[2], new float4(0, 0, 0, 1)), // Green on Black
            (_colorPalette[3], _colorPalette[0]), // Blue on White
            (_colorPalette[4], _colorPalette[3]) // Yellow on Blue
        };

        float startY = ImGui.GetCursorScreenPos().Y;
        for (int i = 0; i < combinations.Length; i++)
        {
            float x = windowPos.X;
            float y = startY + (i * _lineHeight);
            var pos = new float2(x, y);
            var bgRect = new float2(x + (testColors[i].Length * _charWidth), y + _lineHeight);

            // Background
            drawList.AddRectFilled(pos, bgRect, ImGui.ColorConvertFloat4ToU32(combinations[i].bg));
            // Text
            drawList.AddText(pos, ImGui.ColorConvertFloat4ToU32(combinations[i].fg), testColors[i]);
        }

        ImGui.Dummy(new float2(200, combinations.Length * _lineHeight));
    }

    private static void DrawGridAlignmentTest()
    {
        ImGui.Text("Grid Alignment Testing");
        ImGui.Text("Verifying character positioning and grid consistency");
        ImGui.Separator();

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float2 windowPos = ImGui.GetCursorScreenPos();

        // Draw grid lines for alignment verification
        uint gridColor = ImGui.ColorConvertFloat4ToU32(new float4(0.3f, 0.3f, 0.3f, 1.0f));

        // Vertical lines
        for (int col = 0; col <= 20; col++)
        {
            float x = windowPos.X + (col * _charWidth);
            var startPos = new float2(x, windowPos.Y);
            var endPos = new float2(x, windowPos.Y + (10 * _lineHeight));
            drawList.AddLine(startPos, endPos, gridColor);
        }

        // Horizontal lines
        for (int row = 0; row <= 10; row++)
        {
            float y = windowPos.Y + (row * _lineHeight);
            var startPos = new float2(windowPos.X, y);
            var endPos = new float2(windowPos.X + (20 * _charWidth), y);
            drawList.AddLine(startPos, endPos, gridColor);
        }

        // Draw characters on grid
        string testPattern = "ABCDEFGHIJKLMNOPQRST";
        for (int row = 0; row < 10; row++)
        {
            for (int col = 0; col < 20; col++)
            {
                float x = windowPos.X + (col * _charWidth);
                float y = windowPos.Y + (row * _lineHeight);
                var pos = new float2(x, y);
                char ch = testPattern[col % testPattern.Length];
                float4 color = _colorPalette[(row + col) % _colorPalette.Length];
                drawList.AddText(pos, ImGui.ColorConvertFloat4ToU32(color), ch.ToString());
            }
        }

        ImGui.Dummy(new float2(20 * _charWidth, 10 * _lineHeight));

        // Display metrics
        ImGui.Separator();
        ImGui.Text($"Font Size: {_fontSize:F1}");
        ImGui.Text($"Character Width: {_charWidth:F2}");
        ImGui.Text($"Line Height: {_lineHeight:F2}");
    }

    private static void DrawPerformanceComparison()
    {
        ImGui.Text("Performance Comparison");
        ImGui.Text("Frame time tracking and rendering performance analysis");
        ImGui.Separator();

        // Performance metrics
        if (_renderTimes.Count > 0)
        {
            float currentFrameTime = _renderTimes[_renderTimes.Count - 1];
            float avgFrameTime = _renderTimes.Count > 0 ? _renderTimes.Sum() / _renderTimes.Count : 0;
            float currentFps = 1000.0f / currentFrameTime;
            float avgFps = 1000.0f / avgFrameTime;

            ImGui.Text($"Current frame time: {currentFrameTime:F2}ms");
            ImGui.Text($"Average frame time: {avgFrameTime:F2}ms");
            ImGui.Text($"Current FPS: {currentFps:F1}");
            ImGui.Text($"Average FPS: {avgFps:F1}");

            // Frame time graph (simplified)
            ImGui.Separator();
            ImGui.Text("Recent frame times:");
            float[] recentTimes = _renderTimes.TakeLast(10).ToArray();
            for (int i = 0; i < recentTimes.Length; i++)
            {
                ImGui.Text($"  {i + 1}: {recentTimes[i]:F2}ms");
            }
        }

        ImGui.Separator();
        ImGui.Text("Terminal Rendering Test (80x24):");

        // Render a small version of the full terminal for performance testing
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float2 windowPos = ImGui.GetCursorScreenPos();
        float smallCharWidth = _charWidth * 0.5f;
        float smallLineHeight = _lineHeight * 0.5f;

        PushHackFont(out bool fontUsed, _fontSize * 0.5f);

        for (int row = 0; row < TerminalHeight; row++)
        {
            for (int col = 0; col < TerminalWidth; col++)
            {
                float x = windowPos.X + (col * smallCharWidth);
                float y = windowPos.Y + (row * smallLineHeight);
                var pos = new float2(x, y);

                // Draw background if not transparent
                float4 bgColor = _backgroundColors[row, col];
                if (bgColor.W > 0)
                {
                    var bgRect = new float2(x + smallCharWidth, y + smallLineHeight);
                    drawList.AddRectFilled(pos, bgRect, ImGui.ColorConvertFloat4ToU32(bgColor));
                }

                // Draw character
                char ch = _terminalBuffer[row, col];
                if (ch != ' ')
                {
                    float4 fgColor = _foregroundColors[row, col];
                    drawList.AddText(pos, ImGui.ColorConvertFloat4ToU32(fgColor), ch.ToString());
                }
            }
        }

        MaybePopFont(fontUsed);

        ImGui.Dummy(new float2(TerminalWidth * smallCharWidth, TerminalHeight * smallLineHeight));
    }

    private static void DrawWindowResizeDetectionTest()
    {
        ImGui.Text("Window Resize Detection Test");
        ImGui.Text("This experiment detects ImGui window resizing and reacts to it");
        ImGui.Separator();

        // Get current window size
        _currentWindowSize = ImGui.GetWindowSize();

        // Check if window was resized
        bool wasResized = false;
        if (_lastWindowSize.X != 0 && _lastWindowSize.Y != 0) // Skip first frame
        {
            float deltaX = Math.Abs(_currentWindowSize.X - _lastWindowSize.X);
            float deltaY = Math.Abs(_currentWindowSize.Y - _lastWindowSize.Y);

            // Consider it a resize if change is more than 1 pixel (to avoid floating point precision issues)
            if (deltaX > 1.0f || deltaY > 1.0f)
            {
                wasResized = true;
                _lastResizeTime = DateTime.Now;

                // Add resize event to history
                string resizeEvent = $"[{_lastResizeTime:HH:mm:ss.fff}] Resize: {_lastWindowSize.X:F0}x{_lastWindowSize.Y:F0} → {_currentWindowSize.X:F0}x{_currentWindowSize.Y:F0}";
                _resizeEvents.Add(resizeEvent);

                // Keep only last 10 resize events
                if (_resizeEvents.Count > 10)
                {
                    _resizeEvents.RemoveAt(0);
                }
            }
        }

        // Display current window information
        ImGui.Text($"Current Window Size: {_currentWindowSize.X:F0} x {_currentWindowSize.Y:F0}");
        ImGui.Text($"Previous Window Size: {_lastWindowSize.X:F0} x {_lastWindowSize.Y:F0}");

        // Show resize status
        if (wasResized)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new float4(0.0f, 1.0f, 0.0f, 1.0f)); // Green
            ImGui.Text("WINDOW RESIZED!");
            ImGui.PopStyleColor();
        }
        else
        {
            // Show time since last resize
            if (_lastResizeTime != DateTime.MinValue)
            {
                var timeSinceResize = DateTime.Now - _lastResizeTime;
                ImGui.Text($"Time since last resize: {timeSinceResize.TotalSeconds:F1}s");
            }
            else
            {
                ImGui.Text("No resize detected yet");
            }
        }

        ImGui.Separator();

        // Display resize history
        ImGui.Text("Resize Event History:");
        if (_resizeEvents.Count == 0)
        {
            ImGui.Text("  (No resize events yet)");
        }
        else
        {
            for (int i = _resizeEvents.Count - 1; i >= 0; i--) // Show most recent first
            {
                ImGui.Text($"  {_resizeEvents[i]}");
            }
        }

        ImGui.Separator();

        // Calculate terminal dimensions based on current window size
        ImGui.Text("Terminal Dimension Calculations:");

        // Estimate available space for terminal (subtract some padding for UI elements)
        float availableWidth = _currentWindowSize.X - 40; // Account for padding and scrollbars
        float availableHeight = _currentWindowSize.Y - 200; // Account for header, separator, and other UI

        if (availableWidth > 0 && availableHeight > 0)
        {
            int terminalCols = (int)(availableWidth / _charWidth);
            int terminalRows = (int)(availableHeight / _lineHeight);

            ImGui.Text($"Character Width: {_charWidth:F2}px");
            ImGui.Text($"Line Height: {_lineHeight:F2}px");
            ImGui.Text($"Available Space: {availableWidth:F0} x {availableHeight:F0}px");
            ImGui.Text($"Calculated Terminal Size: {terminalCols} cols x {terminalRows} rows");

            // Show if this would be a valid terminal size
            if (terminalCols >= 10 && terminalRows >= 3)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new float4(0.0f, 1.0f, 0.0f, 1.0f)); // Green
                ImGui.Text("✓ Valid terminal dimensions");
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new float4(1.0f, 0.0f, 0.0f, 1.0f)); // Red
                ImGui.Text("✗ Window too small for terminal");
                ImGui.PopStyleColor();
            }
        }
        else
        {
            ImGui.Text("Window too small to calculate terminal dimensions");
        }

        ImGui.Separator();

        // Instructions for testing
        ImGui.Text("Testing Instructions:");
        ImGui.Text("• Drag the window edges to resize");
        ImGui.Text("• Drag the window corners to resize both dimensions");
        ImGui.Text("• Watch the resize events appear in real-time");
        ImGui.Text("• Observe how terminal dimensions would change");

        // Update last window size for next frame
        _lastWindowSize = _currentWindowSize;
    }

    private static void DrawTerminalResizeIntegrationTest()
    {
        ImGui.Text("Terminal Resize Integration Test");
        ImGui.Text("This experiment demonstrates the complete terminal resize flow");
        ImGui.Separator();

        // Get current window size
        _currentWindowSize = ImGui.GetWindowSize();

        // Calculate what terminal dimensions would be with current window size
        const float UI_OVERHEAD_HEIGHT = 200.0f; // More overhead for this demo
        const float PADDING_WIDTH = 40.0f;

        float availableWidth = _currentWindowSize.X - PADDING_WIDTH;
        float availableHeight = _currentWindowSize.Y - UI_OVERHEAD_HEIGHT;

        int calculatedCols = 0;
        int calculatedRows = 0;
        bool validDimensions = false;

        if (availableWidth > 0 && availableHeight > 0 && _charWidth > 0 && _lineHeight > 0)
        {
            calculatedCols = (int)Math.Floor(availableWidth / _charWidth);
            calculatedRows = (int)Math.Floor(availableHeight / _lineHeight);

            // Apply bounds like the real controller
            calculatedCols = Math.Max(10, Math.Min(1000, calculatedCols));
            calculatedRows = Math.Max(3, Math.Min(1000, calculatedRows));

            validDimensions = calculatedCols >= 10 && calculatedRows >= 3;
        }

        // Display current state
        ImGui.Text($"Current Window Size: {_currentWindowSize.X:F0} x {_currentWindowSize.Y:F0}");
        ImGui.Text($"Available Space: {availableWidth:F0} x {availableHeight:F0}");
        ImGui.Text($"Character Metrics: {_charWidth:F1} x {_lineHeight:F1}");

        ImGui.Separator();

        if (validDimensions)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new float4(0.0f, 1.0f, 0.0f, 1.0f)); // Green
            ImGui.Text($"✓ Calculated Terminal Size: {calculatedCols} cols x {calculatedRows} rows");
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new float4(1.0f, 0.0f, 0.0f, 1.0f)); // Red
            ImGui.Text("✗ Window too small for valid terminal dimensions");
            ImGui.PopStyleColor();
        }

        ImGui.Separator();

        // Show the resize flow explanation
        ImGui.Text("Terminal Resize Flow (matches TypeScript implementation):");
        ImGui.BulletText("1. ImGui window size change detected");
        ImGui.BulletText("2. Calculate new terminal dimensions from available space");
        ImGui.BulletText("3. Validate dimensions (10x3 minimum, 1000x1000 maximum)");
        ImGui.BulletText("4. Call ITerminalEmulator.Resize(cols, rows) - headless logic");
        ImGui.BulletText("5. Call IProcessManager.Resize(cols, rows) - PTY process");
        ImGui.BulletText("6. Terminal content reflows to new dimensions");

        ImGui.Separator();

        // Show comparison with TypeScript
        ImGui.Text("TypeScript Equivalent:");
        ImGui.BulletText("TerminalController sends JSON: { type: 'resize', cols, rows }");
        ImGui.BulletText("BackendServer calls pty.resize(cols, rows)");
        ImGui.BulletText("StatefulTerminal handles dimension changes");

        ImGui.Separator();

        // Visual demonstration of terminal grid at calculated size
        if (validDimensions)
        {
            ImGui.Text("Terminal Grid Preview:");

            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            float2 windowPos = ImGui.GetCursorScreenPos();

            // Draw a mini version of what the terminal would look like
            float miniCharWidth = _charWidth * 0.3f;
            float miniLineHeight = _lineHeight * 0.3f;

            // Draw grid outline
            float gridWidth = calculatedCols * miniCharWidth;
            float gridHeight = calculatedRows * miniLineHeight;

            uint gridColor = ImGui.ColorConvertFloat4ToU32(new float4(0.5f, 0.5f, 0.5f, 1.0f));
            var gridRect = new float2(windowPos.X + gridWidth, windowPos.Y + gridHeight);
            drawList.AddRect(windowPos, gridRect, gridColor);

            // Draw some sample grid lines
            for (int col = 0; col <= Math.Min(calculatedCols, 20); col += 10)
            {
                float x = windowPos.X + (col * miniCharWidth);
                var lineStart = new float2(x, windowPos.Y);
                var lineEnd = new float2(x, windowPos.Y + gridHeight);
                drawList.AddLine(lineStart, lineEnd, gridColor);
            }

            for (int row = 0; row <= Math.Min(calculatedRows, 10); row += 5)
            {
                float y = windowPos.Y + (row * miniLineHeight);
                var lineStart = new float2(windowPos.X, y);
                var lineEnd = new float2(windowPos.X + gridWidth, y);
                drawList.AddLine(lineStart, lineEnd, gridColor);
            }

            // Add some sample text
            drawList.AddText(new float2(windowPos.X + 2, windowPos.Y + 2),
                ImGui.ColorConvertFloat4ToU32(_colorPalette[0]),
                $"{calculatedCols}x{calculatedRows}");

            ImGui.Dummy(new float2(gridWidth, gridHeight + 10));
        }

        ImGui.Separator();
        ImGui.Text("Testing Instructions:");
        ImGui.BulletText("Resize this window and watch the terminal dimensions update");
        ImGui.BulletText("The grid preview shows what the terminal would look like");
        ImGui.BulletText("Green text = valid dimensions, Red text = too small");
    }
}
