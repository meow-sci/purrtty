using System;
using System.Linq;
using Brutal.ImGuiApi;
using KSA;

namespace caTTY.SkunkworksGameMod.UI;

/// <summary>
/// Small test panel for quick UI experiments (Alex).
/// </summary>
public class AlexsTestPanel
{

    public AlexsTestPanel() { }

    public void Render()
    {
        ImGui.SeparatorText("Alexs Test Panel");

        Thing1();

    }

    private void Thing1()
    {
        //

        if (ImGui.Button("Move Camera to: Hunter"))
        {
            var vehicles = Universe.CurrentSystem?.All.UnsafeAsList().OfType<Vehicle>().ToList() ?? new System.Collections.Generic.List<Vehicle>();
            var hunter = vehicles.First(it => it.Id == "Hunter");
            Universe.MoveCameraTo(hunter);
        }
    }
}
