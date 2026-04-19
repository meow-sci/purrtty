using System;
using Brutal.ImGuiApi;
using caTTY.Display.Controllers.TerminalUi;

namespace caTTY.Display.Controllers.TerminalUi.Menus;

/// <summary>
/// Handles rendering of the Edit menu with text operations.
/// Provides menu items for copy and paste operations.
/// </summary>
internal class EditMenuRenderer
{
  private readonly TerminalUiSelection _selection;

  public EditMenuRenderer(
    TerminalController controller,
    TerminalUiSelection selection)
  {
    ArgumentNullException.ThrowIfNull(controller);
    _selection = selection ?? throw new ArgumentNullException(nameof(selection));
  }

  /// <summary>
  /// Renders the Edit menu with text operations.
  /// </summary>
  /// <returns>True if the menu is currently open, false otherwise.</returns>
  public bool Render()
  {
    bool isOpen = ImGui.BeginMenu("Edit");
    if (isOpen)
    {
      try
      {
        // Copy - enabled only when selection exists
        bool hasSelection = !_selection.GetCurrentSelection().IsEmpty;
        if (ImGui.MenuItem("Copy", "Ctrl+C", false, hasSelection))
        {
          _selection.CopySelectionToClipboard();
        }

        // Paste - always enabled
        if (ImGui.MenuItem("Paste", "Ctrl+V"))
        {
          _selection.PasteFromClipboard();
        }
      }
      finally
      {
        ImGui.EndMenu();
      }
    }
    return isOpen;
  }
}
