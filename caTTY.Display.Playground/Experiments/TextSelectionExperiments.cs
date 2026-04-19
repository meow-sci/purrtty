using Brutal.ImGuiApi;
using caTTY.Display.Types;
using KSA;
using ImGui = Brutal.ImGuiApi.ImGui;
using float2 = Brutal.Numerics.float2;
using float4 = Brutal.Numerics.float4;

namespace caTTY.Playground.Experiments;

/// <summary>
///     Text selection experiments for testing ImGui mouse input handling.
///     This experiment addresses the issue where clicking and dragging moves the ImGui window
///     instead of selecting text in the terminal.
/// </summary>
public static class TextSelectionExperiments
{
    // Terminal simulation data
    private static readonly int TerminalWidth = 80;
    private static readonly int TerminalHeight = 24;
    private static readonly char[,] _terminalBuffer = new char[TerminalHeight, TerminalWidth];
    private static readonly float4[,] _foregroundColors = new float4[TerminalHeight, TerminalWidth];
    private static readonly float4[,] _backgroundColors = new float4[TerminalHeight, TerminalWidth];

    // Selection state
    private static TextSelection _currentSelection = TextSelection.None;
    private static bool _isSelecting = false;
    private static SelectionPosition _selectionStartPosition;

    // Font metrics
    private static float _charWidth = 9.6f; // Monospace approximation
    private static float _lineHeight = 18.0f;

    // Cached terminal rect for mouse position -> cell coordinate conversion
    private static float2 _lastTerminalOrigin;
    private static float2 _lastTerminalSize;

    // Color palette
    private static readonly float4[] _colorPalette =
    [
        new(1.0f, 1.0f, 1.0f, 1.0f), // White
        new(1.0f, 0.0f, 0.0f, 1.0f), // Red
        new(0.0f, 1.0f, 0.0f, 1.0f), // Green
        new(0.0f, 0.0f, 1.0f, 1.0f), // Blue
        new(1.0f, 1.0f, 0.0f, 1.0f), // Yellow
        new(1.0f, 0.0f, 1.0f, 1.0f), // Magenta
        new(0.0f, 1.0f, 1.0f, 1.0f), // Cyan
        new(0.5f, 0.5f, 0.5f, 1.0f)  // Gray
    ];

    static TextSelectionExperiments()
    {
        InitializeTerminalBuffer();
    }

    /// <summary>
    ///     Draws the text selection experiments.
    /// </summary>
    public static void DrawExperiments()
    {
        ImGui.Text("Text Selection Experiments");
        ImGui.Text("Testing mouse input handling for text selection without window dragging");
        ImGui.Separator();

        // Display current selection state
        if (!_currentSelection.IsEmpty)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new float4(0.0f, 1.0f, 0.0f, 1.0f)); // Green
            ImGui.Text($"Selection: ({_currentSelection.Start.Row}, {_currentSelection.Start.Col}) to ({_currentSelection.End.Row}, {_currentSelection.End.Col})");
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.Text("Selection: None");
        }

        ImGui.Text($"Selecting: {_isSelecting}");
        ImGui.Separator();

        // Instructions
        ImGui.Text("Instructions:");
        ImGui.BulletText("Click and drag to select text");
        ImGui.BulletText("Right-click to copy selection (simulated)");
        ImGui.BulletText("Ctrl+A to select all");
        ImGui.BulletText("Escape to clear selection");
        ImGui.Separator();

        // Render the terminal content with selection
        RenderTerminalWithSelection();

        // Handle mouse input
        HandleMouseInput();

        // Handle keyboard shortcuts
        HandleKeyboardInput();
    }

    private static void InitializeTerminalBuffer()
    {
        var random = new Random(42); // Fixed seed for consistent results
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

        // Add some lines with different content for selection testing
        string[] testLines = 
        [
            "This is line 4 - perfect for testing text selection functionality",
            "Line 5: Contains various characters !@#$%^&*()_+-={}[]|\\:;\"'<>?,./",
            "Line 6: UPPERCASE and lowercase and 1234567890 numbers",
            "Line 7: Testing word boundaries and spaces between words",
            "Line 8: Short line",
            "Line 9: A much longer line that extends beyond normal terminal width to test horizontal scrolling and selection behavior"
        ];

        for (int lineIndex = 0; lineIndex < testLines.Length && lineIndex + 4 < TerminalHeight - 1; lineIndex++)
        {
            string line = testLines[lineIndex];
            int row = lineIndex + 4;
            for (int i = 0; i < Math.Min(line.Length, TerminalWidth - 2); i++)
            {
                _terminalBuffer[row, i + 1] = line[i];
                _foregroundColors[row, i + 1] = _colorPalette[0]; // White
                _backgroundColors[row, i + 1] = new float4(0, 0, 0, 0);
            }
        }
    }

    private static void RenderTerminalWithSelection()
    {
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        float2 windowPos = ImGui.GetCursorScreenPos();

        // Calculate terminal area
        float terminalWidth = TerminalWidth * _charWidth;
        float terminalHeight = TerminalHeight * _lineHeight;

        // Cache terminal rect for mouse input handling
        _lastTerminalOrigin = windowPos;
        _lastTerminalSize = new float2(terminalWidth, terminalHeight);

        // CRITICAL: Create an invisible button that captures mouse input and prevents window dragging
        // This is the key to preventing ImGui window dragging when selecting text
        ImGui.InvisibleButton("terminal_content", new float2(terminalWidth, terminalHeight));
        bool terminalHovered = ImGui.IsItemHovered();
        bool terminalActive = ImGui.IsItemActive();

        // Get the draw position after the invisible button
        float2 terminalDrawPos = windowPos;

        // Draw terminal background
        float4 terminalBg = new float4(0.1f, 0.1f, 0.1f, 1.0f); // Dark background
        uint bgColor = ImGui.ColorConvertFloat4ToU32(terminalBg);
        var terminalRect = new float2(terminalDrawPos.X + terminalWidth, terminalDrawPos.Y + terminalHeight);
        drawList.AddRectFilled(terminalDrawPos, terminalRect, bgColor);

        // Render each cell
        for (int row = 0; row < TerminalHeight; row++)
        {
            for (int col = 0; col < TerminalWidth; col++)
            {
                RenderCell(drawList, terminalDrawPos, row, col);
            }
        }

        // Handle mouse input only when the invisible button is hovered/active
        if (terminalHovered || terminalActive)
        {
            HandleMouseInputForTerminal();
        }
    }

    /// <summary>
    /// Handles mouse input only when the invisible button is hovered/active.
    /// This method contains the actual mouse input logic for text selection.
    /// </summary>
    private static void HandleMouseInputForTerminal()
    {
        ImGuiIOPtr io = ImGui.GetIO();

        // Handle mouse button press
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            // Check if Ctrl+C is being pressed for copy operation
            if (io.KeyCtrl && !_currentSelection.IsEmpty)
            {
                CopySelectionToClipboard();
                return;
            }

            // Start new selection
            HandleSelectionMouseDown();
        }

        // Handle mouse drag for selection
        if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            HandleSelectionMouseMove();
        }

        // Handle mouse button release
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            HandleSelectionMouseUp();
        }

        // Handle right-click for copy (alternative to Ctrl+C)
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && !_currentSelection.IsEmpty)
        {
            CopySelectionToClipboard();
        }
    }

    private static void RenderCell(ImDrawListPtr drawList, float2 windowPos, int row, int col)
    {
        float x = windowPos.X + (col * _charWidth);
        float y = windowPos.Y + (row * _lineHeight);
        var pos = new float2(x, y);

        // Check if this cell is selected
        bool isSelected = !_currentSelection.IsEmpty && _currentSelection.Contains(row, col);

        // Get cell data
        char character = _terminalBuffer[row, col];
        float4 baseForeground = _foregroundColors[row, col];
        float4 baseBackground = _backgroundColors[row, col];

        // Apply selection highlighting
        float4 fgColor, bgColor;
        if (isSelected)
        {
            // Use selection colors - semi-transparent blue background with white text
            var selectionBg = new float4(0.3f, 0.5f, 0.8f, 0.7f); // Semi-transparent blue
            var selectionFg = new float4(1.0f, 1.0f, 1.0f, 1.0f); // White text
            
            bgColor = selectionBg;
            fgColor = selectionFg;
        }
        else
        {
            fgColor = baseForeground;
            bgColor = baseBackground;
        }

        // Always draw background (either cell background or selection background)
        if (isSelected || bgColor.W > 0) // Draw if selected or has background color
        {
            var bgRect = new float2(x + _charWidth, y + _lineHeight);
            drawList.AddRectFilled(pos, bgRect, ImGui.ColorConvertFloat4ToU32(bgColor));
        }

        // Draw character if not space or null
        if (character != ' ' && character != '\0')
        {
            drawList.AddText(pos, ImGui.ColorConvertFloat4ToU32(fgColor), character.ToString());
        }
    }

    private static void HandleMouseInput()
    {
        ImGuiIOPtr io = ImGui.GetIO();

        // CRITICAL: Check if mouse is over the terminal area first
        // This prevents window dragging when clicking in the terminal area
        var mousePos = ImGui.GetMousePos();
        bool mouseOverTerminal = IsMouseOverTerminal(mousePos);

        if (!mouseOverTerminal)
        {
            return; // Don't handle mouse input if not over terminal
        }

        // Handle mouse button press
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            // Check if Ctrl+C is being pressed for copy operation
            if (io.KeyCtrl && !_currentSelection.IsEmpty)
            {
                CopySelectionToClipboard();
                return;
            }

            // Start new selection
            HandleSelectionMouseDown();
        }

        // Handle mouse drag for selection
        if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            HandleSelectionMouseMove();
        }

        // Handle mouse button release
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            HandleSelectionMouseUp();
        }

        // Handle right-click for copy (alternative to Ctrl+C)
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && !_currentSelection.IsEmpty)
        {
            CopySelectionToClipboard();
        }
    }

    private static void HandleKeyboardInput()
    {
        ImGuiIOPtr io = ImGui.GetIO();

        // Handle keyboard shortcuts for selection
        if (io.KeyCtrl)
        {
            // Ctrl+A: Select all visible content
            if (ImGui.IsKeyPressed(ImGuiKey.A))
            {
                SelectAllVisibleContent();
            }
            // Ctrl+C: Copy selection
            else if (ImGui.IsKeyPressed(ImGuiKey.C) && !_currentSelection.IsEmpty)
            {
                CopySelectionToClipboard();
            }
        }

        // Clear selection on Escape
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            ClearSelection();
        }
    }

    private static bool IsMouseOverTerminal(float2 mousePos)
    {
        return mousePos.X >= _lastTerminalOrigin.X && 
               mousePos.Y >= _lastTerminalOrigin.Y &&
               mousePos.X < _lastTerminalOrigin.X + _lastTerminalSize.X &&
               mousePos.Y < _lastTerminalOrigin.Y + _lastTerminalSize.Y;
    }

    /// <summary>
    /// Converts mouse coordinates to terminal cell coordinates (0-based).
    /// </summary>
    /// <returns>The cell coordinates, or null if the mouse is outside the terminal area</returns>
    private static SelectionPosition? GetMouseCellCoordinates()
    {
        var mouse = ImGui.GetMousePos();

        float relX = mouse.X - _lastTerminalOrigin.X;
        float relY = mouse.Y - _lastTerminalOrigin.Y;

        // Check if mouse is within terminal bounds
        if (relX < 0 || relY < 0 || relX >= _lastTerminalSize.X || relY >= _lastTerminalSize.Y)
        {
            return null;
        }

        int col = (int)Math.Floor(relX / Math.Max(1e-6f, _charWidth));
        int row = (int)Math.Floor(relY / Math.Max(1e-6f, _lineHeight));

        col = Math.Max(0, Math.Min(TerminalWidth - 1, col));
        row = Math.Max(0, Math.Min(TerminalHeight - 1, row));

        return new SelectionPosition(row, col);
    }

    /// <summary>
    /// Handles mouse button press for selection.
    /// </summary>
    private static void HandleSelectionMouseDown()
    {
        var mousePos = GetMouseCellCoordinates();
        if (!mousePos.HasValue)
        {
            return;
        }

        // Start new selection
        _selectionStartPosition = mousePos.Value;
        _currentSelection = TextSelection.Empty(mousePos.Value.Row, mousePos.Value.Col);
        _isSelecting = true;

        Console.WriteLine($"TextSelectionExperiments: Started selection at ({mousePos.Value.Row}, {mousePos.Value.Col})");
    }

    /// <summary>
    /// Handles mouse movement for selection.
    /// </summary>
    private static void HandleSelectionMouseMove()
    {
        if (!_isSelecting)
        {
            return;
        }

        var mousePos = GetMouseCellCoordinates();
        if (!mousePos.HasValue)
        {
            return;
        }

        // Update selection to extend from start position to current mouse position
        _currentSelection = new TextSelection(_selectionStartPosition, mousePos.Value);
    }

    /// <summary>
    /// Handles mouse button release for selection.
    /// </summary>
    private static void HandleSelectionMouseUp()
    {
        if (!_isSelecting)
        {
            return;
        }

        var mousePos = GetMouseCellCoordinates();
        if (mousePos.HasValue)
        {
            // Finalize selection
            _currentSelection = new TextSelection(_selectionStartPosition, mousePos.Value);
            Console.WriteLine($"TextSelectionExperiments: Finalized selection from ({_selectionStartPosition.Row}, {_selectionStartPosition.Col}) to ({mousePos.Value.Row}, {mousePos.Value.Col})");
        }

        _isSelecting = false;
    }

    /// <summary>
    /// Selects all visible content in the terminal viewport.
    /// </summary>
    private static void SelectAllVisibleContent()
    {
        if (TerminalHeight == 0 || TerminalWidth == 0)
        {
            return;
        }

        // Select from top-left to bottom-right of the visible area
        var startPos = new SelectionPosition(0, 0);
        var endPos = new SelectionPosition(TerminalHeight - 1, TerminalWidth - 1);
        
        _currentSelection = new TextSelection(startPos, endPos);
        _isSelecting = false;

        Console.WriteLine("TextSelectionExperiments: Selected all visible content");
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    private static void ClearSelection()
    {
        _currentSelection = TextSelection.None;
        _isSelecting = false;
        Console.WriteLine("TextSelectionExperiments: Cleared selection");
    }

    /// <summary>
    /// Copies the current selection to the clipboard (simulated).
    /// </summary>
    private static void CopySelectionToClipboard()
    {
        if (_currentSelection.IsEmpty)
        {
            Console.WriteLine("TextSelectionExperiments: No selection to copy");
            return;
        }

        try
        {
            // Extract text from selection
            string selectedText = ExtractSelectedText();

            if (string.IsNullOrEmpty(selectedText))
            {
                Console.WriteLine("TextSelectionExperiments: No text in selection");
                return;
            }

            // In a real implementation, this would copy to the system clipboard
            // For the experiment, we'll just log it
            Console.WriteLine($"TextSelectionExperiments: Would copy to clipboard: \"{selectedText}\"");
            Console.WriteLine($"TextSelectionExperiments: Text length: {selectedText.Length} characters");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TextSelectionExperiments: Error copying selection: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts text from the current selection.
    /// </summary>
    private static string ExtractSelectedText()
    {
        if (_currentSelection.IsEmpty)
        {
            return string.Empty;
        }

        var lines = new List<string>();
        
        for (int row = _currentSelection.Start.Row; row <= _currentSelection.End.Row; row++)
        {
            int startCol = (row == _currentSelection.Start.Row) ? _currentSelection.Start.Col : 0;
            int endCol = (row == _currentSelection.End.Row) ? _currentSelection.End.Col : TerminalWidth - 1;
            
            var lineChars = new List<char>();
            for (int col = startCol; col <= endCol; col++)
            {
                if (col < TerminalWidth && row < TerminalHeight)
                {
                    lineChars.Add(_terminalBuffer[row, col]);
                }
            }
            
            // Convert to string and trim trailing spaces
            string line = new string(lineChars.ToArray()).TrimEnd();
            lines.Add(line);
        }
        
        // Join lines with newlines
        return string.Join("\n", lines);
    }
}