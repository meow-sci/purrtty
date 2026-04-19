using System;
using System.Threading.Tasks;
using Brutal.ImGuiApi;
using caTTY.Core.Terminal;
using caTTY.Display.Controllers;
using caTTY.Display.Utils;

namespace caTTY.Display.Controllers.TerminalUi.Menus;

/// <summary>
/// Handles rendering of the Sessions menu with session creation and management operations.
/// Provides menu items for creating new sessions, switching between sessions, and session navigation.
/// </summary>
internal class SessionsMenuRenderer
{
  private readonly TerminalController _controller;
  private readonly SessionManager _sessionManager;

  public SessionsMenuRenderer(
    TerminalController controller,
    SessionManager sessionManager)
  {
    _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
  }

  /// <summary>
  /// Renders the Sessions menu with session creation, management, and navigation options.
  /// </summary>
  /// <returns>True if the menu is currently open, false otherwise.</returns>
  public bool Render()
  {
    bool isOpen = ImGui.BeginMenu("Sessions");
    if (isOpen)
    {
      try
      {
        // New Session (Default)
        if (ImGui.MenuItem("New Session (Default)"))
        {
          _ = Task.Run(async () => await _sessionManager.CreateSessionAsync());
        }

        // New Session with Shell submenu
        ImGui.Separator();
        ImGui.Text("New Session with Shell:");

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
              {
                try
                {
                  await ShellSelectionHelper.CreateSessionWithShell(_sessionManager, option);
                }
                catch (Exception ex)
                {
                  Console.WriteLine($"Failed to create shell session '{option.DisplayName}': {ex.Message}");
                  Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
              });
            }

            if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(option.Tooltip))
            {
              ImGui.SetTooltip(option.Tooltip);
            }
          }
        }

        // Sessions submenu (active session list)
        ImGui.Separator();

        if (ImGui.BeginMenu("Sessions"))
        {
          var sessions = _sessionManager.Sessions;
          var activeSession = _sessionManager.ActiveSession;

          if (sessions.Count == 0)
          {
            ImGui.Text("No sessions available");
          }
          else
          {
            foreach (var session in sessions)
            {
              bool isActive = session == activeSession;
              string sessionLabel = session.Title;

              // Add process exit code to label if process has exited
              if (session.ProcessManager.ExitCode.HasValue)
              {
                sessionLabel += $" (Exit: {session.ProcessManager.ExitCode})";
              }

              // Create unique ImGui ID using session GUID to avoid conflicts
              string menuItemId = $"{sessionLabel}##session_menu_item_{session.Id}";

              if (ImGui.MenuItem(menuItemId, "", isActive))
              {
                if (!isActive)
                {
                  _controller.SwitchToSessionAndFocus(session.Id);
                }
              }

              // Show tooltip with session information
              if (ImGui.IsItemHovered())
              {
                var tooltip = $"Session: {session.Title}\nCreated: {session.CreatedAt:HH:mm:ss}";
                if (session.LastActiveAt.HasValue)
                {
                  tooltip += $"\nLast Active: {session.LastActiveAt.Value:HH:mm:ss}";
                }
                tooltip += $"\nState: {session.State}";
                if (session.ProcessManager.IsRunning)
                {
                  tooltip += "\nProcess: Running";
                }
                else if (session.ProcessManager.ExitCode.HasValue)
                {
                  tooltip += $"\nProcess: Exited ({session.ProcessManager.ExitCode})";
                }
                ImGui.SetTooltip(tooltip);
              }
            }
          }

          ImGui.EndMenu();
        }

        // Session management actions
        ImGui.Separator();

        // Close Session - enabled when more than one session exists
        bool canCloseSession = _sessionManager.SessionCount > 1;
        if (ImGui.MenuItem("Close Session", "", false, canCloseSession))
        {
          var activeSession = _sessionManager.ActiveSession;
          if (activeSession != null)
          {
            _ = Task.Run(async () => await _sessionManager.CloseSessionAsync(activeSession.Id));
          }
        }

        // Next Session - enabled when more than one session exists
        bool canNavigateSessions = _sessionManager.SessionCount > 1;
        if (ImGui.MenuItem("Next Session", "", false, canNavigateSessions))
        {
          _controller.SwitchToNextSessionAndFocus();
        }

        // Previous Session - enabled when more than one session exists
        if (ImGui.MenuItem("Previous Session", "", false, canNavigateSessions))
        {
          _controller.SwitchToPreviousSessionAndFocus();
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
