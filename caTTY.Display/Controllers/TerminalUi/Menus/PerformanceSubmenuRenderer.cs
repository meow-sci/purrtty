using System;
using Brutal.ImGuiApi;
using caTTY.Display.Performance;
using float4 = Brutal.Numerics.float4;

namespace caTTY.Display.Controllers.TerminalUi.Menus;

/// <summary>
/// Handles rendering of the Performance submenu with tracing controls.
/// Provides controls for enabling/disabling performance tracing, dumping results,
/// resetting counters, and configuring auto-dump interval.
/// </summary>
internal class PerformanceSubmenuRenderer
{
  private readonly PerformanceStopwatch _perfWatch;

  public PerformanceSubmenuRenderer(PerformanceStopwatch perfWatch)
  {
    _perfWatch = perfWatch ?? throw new ArgumentNullException(nameof(perfWatch));
  }

  /// <summary>
  /// Renders the Performance submenu content with tracing controls.
  /// Note: Parent menu handles BeginMenu/EndMenu calls.
  /// </summary>
  public void RenderContent()
  {
    // Enable/Disable Tracing Checkbox
    bool isEnabled = _perfWatch.Enabled;
    if (ImGui.Checkbox("Enable Tracing", ref isEnabled))
    {
      _perfWatch.Enabled = isEnabled;
      Console.WriteLine($"Performance tracing {(isEnabled ? "enabled" : "disabled")}");
    }

    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip("Toggle performance tracing on/off\nWhen enabled, tracks timing for all rendering operations");
    }

    ImGui.Separator();

    // Dump Now Menu Item
    if (ImGui.MenuItem("Dump Now"))
    {
      _perfWatch.DumpToConsole();
      Console.WriteLine("Performance data dumped to console");
    }

    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip("Immediately output current performance data to console\nDoes not reset counters");
    }

    // Reset Counters Menu Item
    if (ImGui.MenuItem("Reset Counters"))
    {
      _perfWatch.Reset();
      Console.WriteLine("Performance counters reset");
    }

    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip("Clear all performance data and reset frame counter\nUseful for starting a fresh measurement");
    }

    ImGui.Separator();

    // Auto-dump Interval Input Field
    ImGui.Text("Auto-dump Interval:");
    int dumpInterval = _perfWatch.DumpIntervalFrames;
    if (ImGui.InputInt("frames##DumpInterval", ref dumpInterval, 1, 10))
    {
      // Clamp to reasonable range (1-600 frames = ~0.016s to 10s at 60fps)
      dumpInterval = Math.Max(1, Math.Min(600, dumpInterval));
      _perfWatch.DumpIntervalFrames = dumpInterval;
      Console.WriteLine($"Performance auto-dump interval set to {dumpInterval} frames");
    }

    if (ImGui.IsItemHovered())
    {
      var seconds = dumpInterval / 60.0;
      ImGui.SetTooltip($"Auto-dump performance data every N frames\nCurrent: {dumpInterval} frames (~{seconds:F2}s at 60fps)\nRange: 1-600 frames");
    }

    // Display current status
    ImGui.Separator();
    ImGui.Text("Status:");

    var statusColor = _perfWatch.Enabled
      ? new float4(0.0f, 1.0f, 0.0f, 1.0f)  // Green when enabled
      : new float4(0.7f, 0.7f, 0.7f, 1.0f); // Gray when disabled

    var statusText = _perfWatch.Enabled ? "Active" : "Inactive";
    ImGui.TextColored(statusColor, statusText);

    if (_perfWatch.Enabled)
    {
      var intervalSeconds = _perfWatch.DumpIntervalFrames / 60.0;
      ImGui.Text($"Auto-dump: Every {_perfWatch.DumpIntervalFrames} frames (~{intervalSeconds:F1}s)");
    }
  }
}
