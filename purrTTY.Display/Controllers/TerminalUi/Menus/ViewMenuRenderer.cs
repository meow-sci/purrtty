using System;
using Brutal.ImGuiApi;

namespace purrTTY.Display.Controllers.TerminalUi.Menus;

/// <summary>
/// Handles rendering of the View menu for terminal window visibility actions.
/// </summary>
internal class ViewMenuRenderer
{
  private readonly TerminalController _controller;

  public ViewMenuRenderer(TerminalController controller)
  {
    _controller = controller ?? throw new ArgumentNullException(nameof(controller));
  }

  /// <summary>
  /// Renders the View menu.
  /// </summary>
  /// <returns>True if the menu is currently open, false otherwise.</returns>
  public bool Render()
  {
    bool isOpen = ImGui.BeginMenu("View");
    if (isOpen)
    {
      try
      {
        if (ImGui.MenuItem("Hide Terminal"))
        {
          _controller.IsVisible = !_controller.IsVisible;
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
