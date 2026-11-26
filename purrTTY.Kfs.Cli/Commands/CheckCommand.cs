// Game types are allowed here: CliApp reaches this file only after KsaAssemblyResolver is installed.

using KSA.Kfs.Host;
using KSA.Kfs.Vm.Tables;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Cli.Commands;

/// <summary>The process's <see cref="KfsRuntime"/> for one command: the host's when it has one, else started and disposed here.</summary>
internal sealed class RuntimeLease : IDisposable
{
    private readonly bool _owned;

    private RuntimeLease(KfsRuntime runtime, bool owned)
    {
        Runtime = runtime;
        _owned = owned;
    }

    public KfsRuntime Runtime { get; }

    public static RuntimeLease Acquire() =>
        KfsRuntime.Current is { IsDisposed: false } current ? new RuntimeLease(current, owned: false) : new RuntimeLease(KfsRuntime.Start(), owned: true);

    public void Dispose()
    {
        if (_owned) Runtime.Dispose();
    }
}

/// <summary><c>purr-kfs check</c>: resolve the file's project and build it as the workbench would.</summary>
internal static class CheckCommand
{
    public static int Run(WorkspaceFile file, VehicleOptions vehicle, bool json, TextWriter stdout, TextWriter stderr)
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
        {
            ProjectBuild build = Build(lease.Runtime, file, Layout(vehicle));
            if (json) CliApp.Lines(stdout, BuildOutput.Json(build));
            else BuildOutput.WriteText(stdout, build);
            return build.Ok ? CliApp.ExitOk : CliApp.ExitFailed;
        }
    }

    /// <summary>Resolves and builds <paramref name="file"/> against <paramref name="layout"/> on the runtime's augmented tables, for the bench part.</summary>
    public static ProjectBuild Build(KfsRuntime runtime, WorkspaceFile file, IoLayout layout)
    {
        ProjectResolution resolution = ProjectResolver.ForDisk(file.Workspace).Resolve(file.Path);
        return ProjectBuilder.Build(resolution, file.Path, layout, runtime.Tables, runtime.Tables.BenchPart);
    }

    /// <summary>The layout of the simulated vehicle <c>run</c> would create (the game's <c>FakeVehicle</c> layout).</summary>
    public static IoLayout Layout(VehicleOptions vehicle) =>
        LayoutGenerator.Vehicle(VehicleOptions.Name, vehicle.Thrusters, vehicle.Engines, vehicle.Gimbals);

    /// <summary>The simulated vehicle's spec.</summary>
    public static SimVehicleSpec Spec(VehicleOptions vehicle) =>
        SimVehicleSpec.With(vehicle.Lights, vehicle.Deployables, vehicle.Actuators, VehicleOptions.Name, vehicle.Engines, vehicle.Gimbals, vehicle.Thrusters);
}
