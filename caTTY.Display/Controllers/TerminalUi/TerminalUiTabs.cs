using System;
using System.Threading.Tasks;
using Brutal.ImGuiApi;
using caTTY.Core.Terminal;
using caTTY.Display.Controllers;
using caTTY.Display.Utils;
using float2 = Brutal.Numerics.float2;

namespace caTTY.Display.Controllers.TerminalUi;

/// <summary>
///     Handles tab area rendering and tab-related operations for terminal sessions.
///     Provides ImGui tab bar implementation with session management integration.
/// </summary>
internal class TerminalUiTabs
{
  private readonly TerminalController _controller;
  private readonly SessionManager _sessionManager;
  private readonly Action _triggerResizeForAllSessions;
  private Guid? _lastActiveSessionId;
  private int _lastSessionCount = 0;

  /// <summary>
  ///     Gets whether the tab area is currently being hovered or interacted with.
  ///     Used to keep the UI visible when interacting with tabs.
  /// </summary>
  public bool IsTabAreaActive { get; internal set; }

  /// <summary>
  ///     Creates a new tabs subsystem instance.
  /// </summary>
  /// <param name="controller">The parent terminal controller</param>
  /// <param name="sessionManager">The session manager instance</param>
  /// <param name="triggerResizeForAllSessions">Callback to trigger resize for all sessions</param>
  public TerminalUiTabs(TerminalController controller, SessionManager sessionManager, Action triggerResizeForAllSessions)
  {
    _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    _triggerResizeForAllSessions = triggerResizeForAllSessions ?? throw new ArgumentNullException(nameof(triggerResizeForAllSessions));
  }

  /// <summary>
  /// Calculates the current tab area height based on the number of terminal instances.
  /// Uses constrained sizing to prevent excessive height growth.
  /// </summary>
  /// <param name="tabCount">Number of terminal tabs (defaults to 1 for current single terminal)</param>
  /// <returns>Tab area height in pixels</returns>
  public static float CalculateTabAreaHeight(int tabCount = 1) => TerminalUiResize.CalculateTabAreaHeight(tabCount);

  /// <summary>
  /// Renders the tab area using real ImGui tabs for session management.
  /// Includes add button and context menus for tab operations.
  /// When there is only one session, the tab bar is not rendered.
  /// </summary>
  public void RenderTabArea()
  {
    try
    {
      var sessions = _sessionManager.Sessions;
      var activeSession = _sessionManager.ActiveSession;
      int currentSessionCount = sessions.Count;

      // Detect if tab bar visibility changed (crossing 1/2 session boundary)
      // This happens when going from 1 session (no tab bar) to 2+ (tab bar shown)
      // or vice versa. We need to trigger a resize because the available terminal
      // height changes by ~50px when the tab bar appears/disappears.
      bool tabBarVisibilityChanged =
        (_lastSessionCount <= 1 && currentSessionCount >= 2) ||
        (_lastSessionCount >= 2 && currentSessionCount <= 1);

      if (tabBarVisibilityChanged)
      {
        _triggerResizeForAllSessions();
      }

      _lastSessionCount = currentSessionCount;

      // Don't render tab bar when there's only one session
      if (sessions.Count <= 1)
      {
        IsTabAreaActive = false;
        return;
      }

      // Detect if active session changed (for tab synchronization)
      bool activeSessionChanged = activeSession?.Id != _lastActiveSessionId;
      if (activeSession != null)
      {
        _lastActiveSessionId = activeSession.Id;
      }

      // Get available width for tab area
      float availableWidth = ImGui.GetContentRegionAvail().X;
      float tabHeight = LayoutConstants.TAB_AREA_HEIGHT;

      // Create a child region for the tab area to maintain consistent height
      bool childBegun = ImGui.BeginChild("TabArea", new float2(availableWidth, tabHeight), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);

      try
      {
        if (childBegun)
        {
          // Track if tab area child window is hovered (must check while inside child)
          bool isChildHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows);

          // Add button on the left with fixed width
          float addButtonWidth = LayoutConstants.ADD_BUTTON_WIDTH;
          if (ImGui.Button("+##add_terminal", new float2(addButtonWidth, tabHeight - 5.0f)))
          {
            ImGui.OpenPopup("new_terminal_popup");
          }

          // Render popup menu for shell selection
          if (ImGui.BeginPopup("new_terminal_popup"))
          {
            RenderNewTerminalPopup();
            ImGui.EndPopup();
          }

          if (ImGui.IsItemHovered())
          {
            ImGui.SetTooltip("Add new terminal session");
          }

          // Only show tabs if we have sessions
          if (sessions.Count > 0)
          {
            ImGui.SameLine();

            // Calculate remaining width for tab bar
            float remainingWidth = availableWidth - addButtonWidth - LayoutConstants.ELEMENT_SPACING;

            // Begin tab bar with remaining width
            if (ImGui.BeginTabBar("SessionTabs", ImGuiTabBarFlags.Reorderable | ImGuiTabBarFlags.AutoSelectNewTabs | ImGuiTabBarFlags.FittingPolicyScroll))
            {
              try
              {
                // Render each session as a tab
                foreach (var session in sessions)
                {
                  bool isActive = session == activeSession;

                  // Create tab label with session title and optional exit code
                  string tabLabel = session.Title;
                  if (session.ProcessManager.ExitCode.HasValue)
                  {
                    tabLabel += $" (Exit: {session.ProcessManager.ExitCode})";
                  }

                  // Use unique ID for each tab
                  string tabId = $"{tabLabel}##tab_{session.Id}";

                  // Use SetSelected flag only when active session just changed (to sync ImGui with SessionManager)
                  // This ensures programmatic session switches (File menu, Sessions menu) update the tab UI
                  // Without causing infinite loops from using SetSelected every frame
                  ImGuiTabItemFlags tabFlags = (isActive && activeSessionChanged)
                    ? ImGuiTabItemFlags.SetSelected
                    : ImGuiTabItemFlags.None;

                  bool tabOpen = true;
                  if (ImGui.BeginTabItem(tabId, ref tabOpen, tabFlags))
                  {
                    try
                    {
                      // If this tab is being rendered and it's not the current active session, switch to it
                      // This happens when user clicks the tab directly
                      // BUT: Don't switch if we just did a programmatic switch (activeSessionChanged),
                      // because ImGui's tab selection lags one frame behind our SessionManager state
                      if (!isActive && !activeSessionChanged)
                      {
                        _controller.SwitchToSessionAndFocus(session.Id);
                      }

                      // Tab content is handled by the terminal canvas, so we don't render content here
                      // The tab item just needs to exist to show the tab
                    }
                    finally
                    {
                      ImGui.EndTabItem();
                    }
                  }

                  // Handle tab close button (when tabOpen becomes false)
                  if (!tabOpen && sessions.Count > 1)
                  {
                    _ = Task.Run(async () => await _sessionManager.CloseSessionAsync(session.Id));
                  }

                  // Context menu for tab (right-click)
                  if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                  {
                    ImGui.OpenPopup($"tab_context_{session.Id}");
                  }

                  if (ImGui.BeginPopup($"tab_context_{session.Id}"))
                  {
                    if (ImGui.MenuItem("Close Tab") && sessions.Count > 1)
                    {
                      _ = Task.Run(async () => await _sessionManager.CloseSessionAsync(session.Id));
                    }

                    // Add restart option for terminated sessions
                    if (!session.ProcessManager.IsRunning && session.ProcessManager.ExitCode.HasValue)
                    {
                      if (ImGui.MenuItem("Restart Session"))
                      {
                        _ = Task.Run(async () =>
                        {
                          try
                          {
                            await _sessionManager.RestartSessionAsync(session.Id);
                          }
                          catch (Exception ex)
                          {
                            Console.WriteLine($"TerminalController: Failed to restart session {session.Id}: {ex.Message}");
                          }
                        });
                      }
                    }

                    if (ImGui.MenuItem("Rename Tab"))
                    {
                      // TODO: Implement tab renaming in future
                      ShowNotImplementedMessage("Tab renaming");
                    }
                    ImGui.EndPopup();
                  }
                }
              }
              finally
              {
                ImGui.EndTabBar();
              }
            }
          }

          // Update interaction state before ending child window
          // This captures hover/active state while we're still in the child context
          bool isAnyItemHoveredInChild = ImGui.IsAnyItemHovered();
          bool isAnyItemActiveInChild = ImGui.IsAnyItemActive();
          IsTabAreaActive = isChildHovered || isAnyItemHoveredInChild || isAnyItemActiveInChild;
        }
        else
        {
          IsTabAreaActive = false;
        }
      }
      finally
      {
        if (childBegun)
        {
          ImGui.EndChild();
        }
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Error rendering tab area: {ex.Message}");

      // Fallback: render a simple text indicator if tab rendering fails
      ImGui.Text("No sessions");
      ImGui.SameLine();
      if (ImGui.Button("+##fallback_add"))
      {
        _ = Task.Run(async () => await _sessionManager.CreateSessionAsync());
      }
    }
  }

  /// <summary>
  /// Renders the popup menu for creating new terminal sessions with shell selection.
  /// </summary>
  private void RenderNewTerminalPopup()
  {
    // Default option (uses persisted config)
    if (ImGui.MenuItem("New Terminal (Default)"))
    {
      _ = Task.Run(async () => await _sessionManager.CreateSessionAsync());
      _controller.ForceFocus();
    }

    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip("Create terminal with default shell from settings");
    }

    ImGui.Separator();

    // Shell-specific options
    var shellOptions = ShellSelectionHelper.GetAvailableShellOptions();

    if (shellOptions.Count == 0)
    {
      ImGui.TextDisabled("No shells available");
    }
    else
    {
      foreach (var option in shellOptions)
      {
        if (ImGui.MenuItem(option.DisplayName))
        {
          _ = Task.Run(async () =>
            await ShellSelectionHelper.CreateSessionWithShell(_sessionManager, option));
          _controller.ForceFocus();
        }

        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(option.Tooltip))
        {
          ImGui.SetTooltip(option.Tooltip);
        }
      }
    }
  }

  /// <summary>
  /// Shows a message for not-yet-implemented features.
  /// </summary>
  /// <param name="feature">The feature name to display</param>
  private void ShowNotImplementedMessage(string feature)
  {
    Console.WriteLine($"TerminalController: {feature} not implemented in this phase");
    // Future: Could show ImGui popup
  }
}
