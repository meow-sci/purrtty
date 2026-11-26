// Game types are allowed here: CliApp reaches this file only after KsaAssemblyResolver is installed.

using System.Globalization;
using KSA.Kfs.Host.Services;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Cli.Commands;

/// <summary><c>purr-kfs run</c>: build, load onto a <see cref="SimVehicle"/>, run in simulated time, report.</summary>
internal static class RunCommand
{
    public static int Run(WorkspaceFile file, VehicleOptions vehicleOptions, RunOptions options, TextWriter stdout, TextWriter stderr)
    {
        RuntimeLease lease;
        try
        {
            lease = RuntimeLease.Acquire();
        }
        catch (InvalidOperationException e)
        {
            stderr.WriteLine("purr-kfs: the kFS runtime did not start: " + e.Message);
            return CliApp.ExitUsage;
        }
        using (lease)
        using (SimVehicle vehicle = lease.Runtime.CreateVehicle(CheckCommand.Spec(vehicleOptions)))
        {
            ProjectBuild build = CheckCommand.Build(lease.Runtime, file, vehicle.Layout);
            BuildOutput.WriteText(stdout, build);
            if (!build.Ok) return CliApp.ExitFailed;

            try
            {
                vehicle.LoadImage(build.Result.Image!);
            }
            catch (InvalidOperationException e)
            {
                stdout.WriteLine("run: " + e.Message);
                return CliApp.ExitFailed;
            }

            // The first frame dispatches at simulated time 0; frames follow until the time reaches --for,
            // so the ticks due at 0 … --for (both included) run.
            double frameDt = 1.0 / options.FrameHz;
            vehicle.Frame(frameDt);
            while (vehicle.TimeS < options.Seconds - 1e-9) vehicle.Frame(frameDt);
            stdout.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"run: 0 to {BuildOutput.Num(vehicle.TimeS)} s simulated, {vehicle.FrameCount} frames at {BuildOutput.Num(options.FrameHz)} Hz, vehicle \"{vehicle.Name}\" ({BuildOutput.Count(vehicleOptions.Lights, "light")}, {BuildOutput.Count(vehicleOptions.Deployables, "deployable")}, {BuildOutput.Count(vehicleOptions.Actuators, "actuator")})"));

            if (options.Log) WriteLog(stdout, vehicle.Log);
            if (options.Requests) WriteRequests(stdout, vehicle);
            string? trap = vehicle.Trap is { } t ? Trap(build, t) : null;
            if (options.State) WriteState(stdout, vehicle, trap);
            if (trap is not null) stdout.WriteLine("trap: " + trap);
            if (vehicle.HostFault is { } fault) stdout.WriteLine("host fault: " + fault);
            return trap is null && vehicle.HostFault is null ? CliApp.ExitOk : CliApp.ExitFailed;
        }
    }

    private static void WriteLog(TextWriter stdout, List<LogLine> log)
    {
        stdout.WriteLine("log:");
        if (log.Count == 0) stdout.WriteLine("  (empty)");
        foreach (LogLine line in log)
        {
            stdout.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  k={line.K} t={line.TimeS:0.000} {line.Text}"));
        }
    }

    private static void WriteRequests(TextWriter stdout, SimVehicle vehicle)
    {
        stdout.WriteLine("requests:");
        if (vehicle.Requests.Count == 0) stdout.WriteLine("  (none)");
        foreach (StockRequest request in vehicle.Requests)
        {
            stdout.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  t={request.TimeS:0.000} {request}"));
        }
    }

    private static void WriteState(TextWriter stdout, SimVehicle vehicle, string? trap)
    {
        stdout.WriteLine("state:");
        stdout.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  machine: {vehicle.State}, {vehicle.TicksRun} ticks at {vehicle.TickHz} Hz, {vehicle.Overruns} overruns, {vehicle.RejectedRequests} rejected requests, {(trap is null ? "no trap" : "trap " + trap)}"));
        SimVehicleParts parts = vehicle.Parts;
        for (int i = 0; i < parts.AllLights.Count; i++)
        {
            SimLight l = parts.AllLights[i];
            var flags = new List<string> { l.On ? "on" : "off", l.Lit ? "lit" : "dark", l.Powered ? "powered" : "unpowered" };
            if (!l.Present) flags.Add("gone");
            if (l.Overridden) flags.Add("overridden");
            stdout.WriteLine(
                $"  light {i} \"{l.Name}\": {string.Join(", ", flags)}; intensity {BuildOutput.Num(l.Intensity)}, range {BuildOutput.Num(l.RangeM)} m, " +
                $"color {BuildOutput.Num(l.R)} {BuildOutput.Num(l.G)} {BuildOutput.Num(l.B)}, cone {BuildOutput.Num(l.InnerRad)}-{BuildOutput.Num(l.OuterRad)} rad");
        }
        WriteAnimations(stdout, "deployable", parts.AllAnimations.Where(a => a.Kind == PartKind.Deployable).ToList());
        WriteAnimations(stdout, "actuator", parts.AllAnimations.Where(a => a.Kind == PartKind.Actuator).ToList());
    }

    private static void WriteAnimations(TextWriter stdout, string noun, List<SimAnimation> animations)
    {
        for (int i = 0; i < animations.Count; i++)
        {
            SimAnimation a = animations[i];
            stdout.WriteLine(
                $"  {noun} {i} \"{a.Name}\": {a.State}{(a.Present ? "" : ", gone")}; position {BuildOutput.Num(a.PositionS)} s of {BuildOutput.Num(a.DurationS)} s, goal {BuildOutput.Num(a.GoalS)} s");
        }
    }

    private static string Trap(ProjectBuild build, SimTrap trap)
    {
        string where = trap.File is null ? "" : string.Create(CultureInfo.InvariantCulture, $" ({BuildOutput.PathOf(build, trap.File) ?? trap.File}:{trap.Line})");
        return $"{trap.Code} in {trap.Function}{where}";
    }
}
