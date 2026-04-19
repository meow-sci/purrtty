using Brutal.ImGuiApi;
using KSA;
using ImGui = Brutal.ImGuiApi.ImGui;
using float2 = Brutal.Numerics.float2;
using float4 = Brutal.Numerics.float4;

namespace caTTY.Playground.Experiments;

public static class TerminalLineDisciplineExperiments
{
    private static bool _activeCapture = false;
    private static string _lineBuffer = string.Empty;
    private static string _lastGlobal = "<none>";

    public static void DrawExperiments()
    {
        ImGui.Begin("Keyboard Focus - Line Discipline Test");

        ImGui.Text("Demonstrates a simple terminal line discipline that can capture keyboard input.");
        ImGui.Separator();

        var io = ImGui.GetIO();
        ImGui.Text($"ImGui.io.WantCaptureKeyboard: {io.WantCaptureKeyboard}");

        ImGui.Spacing();
        ImGui.Text("Global Key Detector (shows input when ImGui does NOT want keyboard):");
        ImGui.BeginChild("global_detector", new float2(420, 80));

        // When ImGui is not capturing keyboard input and our experiment is NOT actively capturing,
        // consider input as "global". If our line discipline has focus, suppress global updates.
        if (!_activeCapture && !io.WantCaptureKeyboard)
        {
            if (io.InputQueueCharacters.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < io.InputQueueCharacters.Count; i++)
                {
                    sb.Append((char)io.InputQueueCharacters[i]);
                }
                _lastGlobal = sb.ToString();
            }

            // Also detect some special keys for demonstration
            if (ImGui.IsKeyPressed(ImGuiKey.Enter)) _lastGlobal = "<Enter>";
            if (ImGui.IsKeyPressed(ImGuiKey.Backspace)) _lastGlobal = "<Backspace>";
        }
        else if (_activeCapture)
        {
            // Suppress global detector while line discipline is active
            _lastGlobal = "<suppressed by capture>";
        }

        ImGui.Text($"Last global input: {_lastGlobal}");
        ImGui.EndChild();

        ImGui.Separator();
        ImGui.Text("Terminal Line Discipline (when active, it captures keyboard and appends to the buffer):");

        if (ImGui.Button(_activeCapture ? "Release Focus" : "Grab Focus"))
        {
            _activeCapture = !_activeCapture;
        }
        ImGui.SameLine();
        ImGui.Text(_activeCapture ? "Captured: this experiment is handling keyboard" : "Not captured: global input is visible");

        ImGui.Spacing();

        if (_activeCapture)
        {
            // Mark ImGui as wanting the keyboard so other widgets/parts can detect capture
            io.WantCaptureKeyboard = true;

            // Consume input characters into the line buffer
            if (io.InputQueueCharacters.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < io.InputQueueCharacters.Count; i++)
                {
                    sb.Append((char)io.InputQueueCharacters[i]);
                }
                _lineBuffer += sb.ToString();
            }

            // Handle some special keys
            if (ImGui.IsKeyPressed(ImGuiKey.Backspace) && _lineBuffer.Length > 0)
            {
                _lineBuffer = _lineBuffer.Substring(0, _lineBuffer.Length - 1);
            }
            if (ImGui.IsKeyPressed(ImGuiKey.Enter))
            {
                _lineBuffer += "\n";
            }
        }

        ImGui.TextWrapped("Captured buffer:");
        ImGui.BeginChild("linebuf", new float2(420, 160));
        ImGui.Text(string.IsNullOrEmpty(_lineBuffer) ? "<empty>" : _lineBuffer);
        ImGui.EndChild();

        ImGui.Separator();
        ImGui.TextWrapped("Usage: Click 'Grab Focus' to make the line discipline capture keyboard input. When captured, the global detector will stop updating.");

        ImGui.End();
    }
}
