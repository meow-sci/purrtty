// A small test-facing API: a simulated vehicle (FakeVehicle + SimVehicleParts) running real kC/kASM
// on the game's own compiler and VM.
//
//   var sim = KfsSim.Create("v", lights: 3, deployables: 1, actuators: 1).LoadKc(src);
//   sim.Run(seconds: 2, frameDt: 0.1);
//   Assert(sim.Parts.Lights[0].On); Assert(sim.ProgramLog.Any(...));
//
// Frame model (mirrors the game): Pump() (main-thread hook applies queued commands), Parts.Step(dt)
// (the world advances), then one kFS dispatch at the frame's time, which runs the ticks that are due.

using KSA.Kfs.Compiler;
using KSA.Kfs.Host;
using KSA.Kfs.Host.Services;
using KSA.Kfs.Vm.Tables;

namespace PurrKfs;

public sealed class KcCompileException(string message) : Exception(message);

public sealed class KfsSim
{
    public static GameTables Tables => KSA.Kfs.KfsIntegration.Tables;     // the game's own (patched) accessor

    public FakeVehicle Fake { get; }
    public SimVehicleParts Parts { get; }
    public VehicleComputer Computer => Fake.Computer;
    public CompileResult? LastCompile { get; private set; }
    public long TimeNs { get; private set; }
    public int Frame { get; private set; }

    public KfsSim(string name, SimVehicleParts parts)
    {
        Parts = parts;
        Fake = new FakeVehicle(Tables, thrusters: 1, name: name);
        PurrRouter.Attach(Fake.Computer, parts);
    }

    public static KfsSim Create(string name, int lights = 0, int deployables = 0, int actuators = 0, Action<SimVehicleParts>? configure = null)
    {
        var parts = new SimVehicleParts(lights, deployables, actuators);
        configure?.Invoke(parts);
        return new KfsSim(name, parts);
    }

    public static CompileResult Compile(string source, IoLayout layout, GameTables? tables = null, string file = "main.c", CompileOptions? options = null)
    {
        tables ??= Tables;
        return Compiler.Compile(new CompileInput
        {
            Files = new Dictionary<string, string> { [file] = source },
            Part = tables.BenchPart,
            Game = tables,
            Layout = layout,
            Options = options ?? new CompileOptions(),
        });
    }

    public CompileResult Compile(string source) => Compile(source, Fake.Layout);

    public KfsSim LoadKc(string source)
    {
        var r = LastCompile = Compile(source);
        if (!r.Ok || r.Image == null)
            throw new KcCompileException("kC compile failed:\n  " + string.Join("\n  ", r.Diagnostics));
        return LoadImage(r.Image);
    }

    public KfsSim LoadKasm(string source)
    {
        var r = Fake.Assemble(source);
        if (!r.Ok || r.Image == null)
            throw new KcCompileException("kASM assemble failed:\n  " + string.Join("\n  ", r.Diagnostics));
        return LoadImage(r.Image);
    }

    public KfsSim LoadImage(byte[] image)
    {
        var lr = Computer.Load(image, Fake.Layout);
        if (!lr.Ok) throw new InvalidOperationException($"image rejected: {lr.Rule} {lr.Message}");
        Computer.Start(TimeNs);
        return this;
    }

    /// Runs whole frames for <paramref name="seconds"/>. <paramref name="beforeDispatch"/> sees the
    /// world exactly as this frame's ticks will (after Pump and Step).
    public KfsSim Run(double seconds, double frameDt = 0.1, Action<KfsSim>? beforeDispatch = null, Action<KfsSim, DispatchResult>? afterDispatch = null)
    {
        RunTogether(new[] { this }, seconds, frameDt, beforeDispatch, afterDispatch);
        return this;
    }

    public static void RunTogether(IReadOnlyList<KfsSim> sims, double seconds, double frameDt = 0.1,
        Action<KfsSim>? beforeDispatch = null, Action<KfsSim, DispatchResult>? afterDispatch = null)
    {
        int frames = (int)Math.Round(seconds / frameDt);
        for (int f = 0; f < frames; f++)
        {
            PurrRouter.Pump();                                        // main thread: apply last frame's requests
            foreach (var s in sims)
            {
                if (f > 0 || s.Frame > 0) s.Parts.Step(frameDt);      // world advances to this frame's time
                beforeDispatch?.Invoke(s);
                var dr = s.Fake.Dispatch(s.TimeNs, default(TruthSample));
                afterDispatch?.Invoke(s, dr);
                s.Frame++;
                s.TimeNs += (long)Math.Round(frameDt * 1e9);
            }
        }
    }

    public List<LogLine> Log => Computer.Console.Snapshot();
    public IEnumerable<string> LogText => Log.Select(l => l.Text);
    public IEnumerable<LogLine> Lines(string prefix) => Log.Where(l => l.Text.StartsWith(prefix, StringComparison.Ordinal));
    public string Dump() => string.Join("\n", Log.Select(l => $"    k={l.K} {l.Text}"));
}
