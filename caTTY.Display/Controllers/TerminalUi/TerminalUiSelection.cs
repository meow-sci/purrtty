using System;
using Brutal.ImGuiApi;
using caTTY.Core.Managers;
using caTTY.Core.Terminal;
using caTTY.Core.Types;
using caTTY.Display.Types;
using caTTY.Display.Utils;

namespace caTTY.Display.Controllers.TerminalUi;

/// <summary>
///     Handles text selection, clipboard operations, and selection-related mouse/keyboard input.
///     This class is responsible for managing text selection state and copying text to the clipboard.
/// </summary>
public class TerminalUiSelection
{
  private readonly TerminalController _controller;
  private readonly SessionManager _sessionManager;
  private readonly TerminalUiMouseTracking _mouseTracking;

  // Selection state
  private TextSelection _currentSelection = TextSelection.None;
  private bool _isSelecting = false;
  private SelectionPosition _selectionStartPosition;

  // Context menu
  private const string ContextMenuPopupId = "terminal_context_menu";

  public TerminalUiSelection(
      TerminalController controller,
      SessionManager sessionManager,
      TerminalUiMouseTracking mouseTracking)
  {
    _controller = controller;
    _sessionManager = sessionManager;
    _mouseTracking = mouseTracking;
  }

  /// <summary>
  /// Gets the current text selection.
  /// </summary>
  /// <returns>The current selection</returns>
  public TextSelection GetCurrentSelection()
  {
    return _currentSelection;
  }

  /// <summary>
  /// Sets the current text selection.
  /// </summary>
  /// <param name="selection">The selection to set</param>
  public void SetSelection(TextSelection selection)
  {
    _currentSelection = selection;
    _isSelecting = false;
  }

  /// <summary>
  /// Gets whether a selection is currently in progress.
  /// </summary>
  public bool IsSelecting
  {
    get => _isSelecting;
    set => _isSelecting = value;
  }

  /// <summary>
  /// Handles mouse input only when the invisible button is hovered/active.
  /// This method contains the actual mouse input logic for text selection.
  /// This approach prevents ImGui window dragging when selecting text in the terminal.
  /// </summary>
  public void HandleMouseInputForTerminal()
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

    // Note: Context menu is rendered separately via RenderContextMenu() which is called
    // unconditionally from TerminalController to ensure the popup renders even when
    // the mouse is no longer hovering the terminal area

    // Handle keyboard shortcuts for selection
    if (io.KeyCtrl)
    {
      // Ctrl+A: Select all visible content
      if (ImGui.IsKeyPressed(ImGuiKey.A))
      {
        SelectAllVisibleContent();
      }
      // Ctrl+C: Copy selection (handled above in mouse click, but also handle as pure keyboard shortcut)
      else if (ImGui.IsKeyPressed(ImGuiKey.C) && !_currentSelection.IsEmpty)
      {
        CopySelectionToClipboard();
      }
      // Ctrl+V: Paste from clipboard
      else if (ImGui.IsKeyPressed(ImGuiKey.V))
      {
        PasteFromClipboard();
      }
    }

    // Clear selection on Escape
    if (ImGui.IsKeyPressed(ImGuiKey.Escape))
    {
      ClearSelection();
    }
  }

  /// <summary>
  /// Renders the right-click context menu with Copy and Paste options.
  /// The popup is opened via ImGui.OpenPopup in TerminalUiRender when the terminal invisible button is right-clicked.
  /// Must be called every frame to render the popup when it's open.
  /// </summary>
  public void RenderContextMenu()
  {
    // The popup is opened in TerminalUiRender.RenderTerminalContent immediately after the InvisibleButton
    // We just need to render it here if it's open
    if (ImGui.BeginPopup(ContextMenuPopupId))
    {
      try
      {
        // Copy - enabled only when selection exists
        bool hasSelection = !_currentSelection.IsEmpty;
        if (ImGui.MenuItem("Copy", "", false, hasSelection))
        {
          CopySelectionToClipboard();
        }

        // Paste - always enabled
        if (ImGui.MenuItem("Paste"))
        {
          PasteFromClipboard();
        }
      }
      finally
      {
        ImGui.EndPopup();
      }
    }
  }

  /// <summary>
  /// Pastes text from the clipboard to the terminal.
  /// </summary>
  public void PasteFromClipboard()
  {
    try
    {
      string? clipboardText = ClipboardManager.GetText();
      if (!string.IsNullOrEmpty(clipboardText))
      {
        _controller.SendToProcess(clipboardText);
        Console.WriteLine($"TerminalUiSelection: Pasted {clipboardText.Length} characters from clipboard");
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalUiSelection: Error pasting from clipboard: {ex.Message}");
    }
  }

  /// <summary>
  /// Selects all visible content in the terminal viewport.
  /// </summary>
  public void SelectAllVisibleContent()
  {
    var activeSession = _sessionManager.ActiveSession;
    if (activeSession == null || activeSession.Terminal.Height == 0 || activeSession.Terminal.Width == 0)
    {
      return;
    }

    // Select from top-left to bottom-right of the visible area
    var startPos = new SelectionPosition(0, 0);
    var endPos = new SelectionPosition(activeSession.Terminal.Height - 1, activeSession.Terminal.Width - 1);

    _currentSelection = new TextSelection(startPos, endPos);
    _isSelecting = false;

    Console.WriteLine("TerminalController: Selected all visible content");
  }

  /// <summary>
  /// Handles mouse button press for selection.
  /// </summary>
  private void HandleSelectionMouseDown()
  {
    var mousePos = _mouseTracking.GetMouseCellCoordinates();
    if (!mousePos.HasValue)
    {
      return;
    }

    // Start new selection
    _selectionStartPosition = mousePos.Value;
    _currentSelection = TextSelection.Empty(mousePos.Value.Row, mousePos.Value.Col);
    _isSelecting = true;
  }

  /// <summary>
  /// Handles mouse movement for selection.
  /// </summary>
  private void HandleSelectionMouseMove()
  {
    if (!_isSelecting)
    {
      return;
    }

    var mousePos = _mouseTracking.GetMouseCellCoordinates();
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
  private void HandleSelectionMouseUp()
  {
    if (!_isSelecting)
    {
      return;
    }

    var mousePos = _mouseTracking.GetMouseCellCoordinates();
    if (mousePos.HasValue)
    {
      // Finalize selection
      _currentSelection = new TextSelection(_selectionStartPosition, mousePos.Value);
    }

    _isSelecting = false;
  }

  /// <summary>
  /// Clears the current selection.
  /// </summary>
  public void ClearSelection()
  {
    _currentSelection = TextSelection.None;
    _isSelecting = false;
  }

  /// <summary>
  /// Copies the current selection to the clipboard.
  /// </summary>
  /// <returns>True if text was copied successfully, false otherwise</returns>
  public bool CopySelectionToClipboard()
  {
    if (_currentSelection.IsEmpty)
    {
      return false;
    }

    var activeSession = _sessionManager.ActiveSession;
    if (activeSession == null)
    {
      return false;
    }

    try
    {
      // Get viewport content from ScrollbackManager
      var screenBuffer = new ReadOnlyMemory<Cell>[activeSession.Terminal.Height];
      for (int i = 0; i < activeSession.Terminal.Height; i++)
      {
        var rowSpan = activeSession.Terminal.ScreenBuffer.GetRow(i);
        var rowArray = new Cell[rowSpan.Length];
        rowSpan.CopyTo(rowArray);
        screenBuffer[i] = rowArray.AsMemory();
      }

      var isAlternateScreenActive = ((TerminalEmulator)activeSession.Terminal).State.IsAlternateScreenActive;
      var viewportRows = activeSession.Terminal.ScrollbackManager.GetViewportRows(
          screenBuffer,
          isAlternateScreenActive,
          activeSession.Terminal.Height
      );

      // Extract text from selection
      string selectedText = TextExtractor.ExtractText(
          _currentSelection,
          viewportRows,
          activeSession.Terminal.Width,
          normalizeLineEndings: true,
          trimTrailingSpaces: true
      );

      if (string.IsNullOrEmpty(selectedText))
      {
        return false;
      }

      // Copy to clipboard
      bool success = ClipboardManager.SetText(selectedText);

      if (success)
      {
        Console.WriteLine($"TerminalController: Copied {selectedText.Length} characters to clipboard");
      }
      else
      {
        Console.WriteLine("TerminalController: Failed to copy selection to clipboard");
      }

      return success;
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Error copying selection to clipboard: {ex.Message}");
      return false;
    }
  }
}
