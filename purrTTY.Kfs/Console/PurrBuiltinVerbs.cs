using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.ExamplePrograms;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Commands;

/// <summary>The built-in verbs of <c>purr</c> (plan §4.13, §4.14).</summary>
internal static class PurrBuiltinVerbs
{
    /// <summary>A registry holding the built-in verbs, in <c>purr help</c> order.</summary>
    internal static PurrVerbRegistry Create()
    {
        var verbs = new PurrVerbRegistry();
        verbs.Add("help", "this list", Help);
        verbs.Add("status", "the extension's status: hooks, tables, console, counters", Status);
        verbs.Add("parts", "which x_ index is which part of the controlled vehicle [lights | deploy | actuators]", Parts, needsExtension: true);
        verbs.Add("lights", "restore the stock look of lights a program changed: reset [all] (the controlled vehicle, or every vehicle)", Lights, needsExtension: true);
        verbs.Add("examples", "write the purrTTY example programs into the kFS workspace [--force overwrites]", Examples, needsExtension: true);
        verbs.Add("header", "print <purrtty.h>, the kC header of the x_ services", Header);
        verbs.Add("kinc", "print purrtty.kinc, the x_ constants for kASM", Kinc);
        verbs.Add("services", "one line per x_ service: id, prototype, summary", Services);
        return verbs;
    }

    private static void Help(PurrContext c)
    {
        c.Print($"purr: {(c.ExtensionActive ? "the purrTTY kFS extension is active" : "the purrTTY kFS extension is not active (" + KfsExtension.StatusSummary + ")")}; verbs (purr <verb> [arguments]):");
        foreach (PurrVerb verb in PurrConsole.Verbs.All) c.Print($"  {verb.Name,-9} {verb.Help}");
    }

    private static void Status(PurrContext c)
    {
        if (!c.NoArguments()) return;
        c.Print(KfsExtension.Status.Describe());
    }

    private static void Parts(PurrContext c)
    {
        PartKind kind;
        switch (c.Args.ToLowerInvariant())
        {
            case "": kind = PartKind.All; break;
            case "lights" or "light": kind = PartKind.Light; break;
            case "deploy" or "deployables" or "deployable": kind = PartKind.Deployable; break;
            case "actuators" or "actuator": kind = PartKind.Actuator; break;
            default:
                c.Print($"purr parts: unknown kind '{c.Args}' (usage: purr parts [lights | deploy | actuators])");
                return;
        }
        ControlledParts? vehicle = c.Host!.ControlledVehicle();
        if (vehicle is null)
        {
            c.Print("purr parts: no vehicle is controlled (take control of one and try again)");
            return;
        }
        c.Print(PartsListing.Lines(vehicle, kind));
    }

    private const string LightsUsage = "purr lights: usage: purr lights reset [all] (the controlled vehicle's lights, or every vehicle's)";

    /// <summary>
    /// <c>purr lights reset [all]</c>: a program's appearance overrides outlive it (stop, trap, another
    /// program); this puts the stock look back — never automatically, since a program may set lights
    /// and stop on purpose. Main thread (a light's template is read by the renderer on it).
    /// </summary>
    private static void Lights(PurrContext c)
    {
        IReadOnlyList<string> words = c.Words;
        bool reset = words.Count is 1 or 2 && words[0].Equals("reset", StringComparison.OrdinalIgnoreCase);
        bool all = words.Count == 2 && words[1].Equals("all", StringComparison.OrdinalIgnoreCase);
        if (!reset || (words.Count == 2 && !all))
        {
            c.Print(LightsUsage);
            return;
        }
        if (all)
        {
            int n = c.Host!.ResetAllLightOverrides();
            c.Print(n == 0
                ? "purr lights reset all: no light has a program's look"
                : $"purr lights reset all: {Lights(n)} back to {(n == 1 ? "its" : "their")} stock look, on every vehicle");
            return;
        }
        ControlledParts? vehicle = c.Host!.ControlledVehicle();
        if (vehicle is null)
        {
            c.Print("purr lights reset: no vehicle is controlled (purr lights reset all restores every vehicle's lights)");
            return;
        }
        int restored = ResetOverridden(vehicle.Parts);
        string running = vehicle.Source == PartsListingSource.RunningProgram && restored > 0 ? "; its running program may change them again" : "";
        c.Print(restored == 0
            ? $"purr lights reset: no light of \"{vehicle.VehicleName}\" has a program's look"
            : $"purr lights reset: {Lights(restored)} of \"{vehicle.VehicleName}\" back to {(restored == 1 ? "its" : "their")} stock look{running}");
    }

    private static string Lights(int n) => n == 1 ? "1 light" : $"{n} lights";

    /// <summary>Applies <c>x_light_reset</c> to every present, overridden light of <paramref name="parts"/>' lists (main thread); returns how many.</summary>
    private static int ResetOverridden(IVehicleParts parts)
    {
        int n = 0;
        foreach (LightSlot slot in parts.Sets.Lights)
        {
            if (!parts.IsPresent(slot) || parts.LightGetI(slot.Index, LightProp.Overridden) == 0) continue;
            parts.Apply(new PartCommand(Svc.LightReset, slot));
            n++;
        }
        return n;
    }

    private static void Examples(PurrContext c)
    {
        bool force = false;
        foreach (string word in c.Words)
        {
            if (word is "--force" or "-f") force = true;
            else
            {
                c.Print($"purr examples: unexpected argument '{word}' (usage: purr examples [--force])");
                return;
            }
        }
        ExampleWriteResult result = ExampleCatalog.WriteTo(c.Host!, force);
        c.Print(result.Lines(c.Host!.WorkspaceFolder));
    }

    private static void Header(PurrContext c)
    {
        if (!c.NoArguments()) return;
        c.Print(CatalogEmitter.Header(XCatalog.Catalog).TrimEnd('\r', '\n'));
    }

    private static void Kinc(PurrContext c)
    {
        if (!c.NoArguments()) return;
        c.Print(CatalogEmitter.Kinc(XCatalog.Catalog).TrimEnd('\r', '\n'));
    }

    private static void Services(PurrContext c)
    {
        if (!c.NoArguments()) return;
        c.Print(CatalogEmitter.ServiceLines(XCatalog.Catalog));
    }
}
