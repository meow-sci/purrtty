using System.Security.Cryptography;
using System.Text;

namespace KfsLspSpike;

/// Non-LSP modes used to answer the spike's questions directly (latency, overlap, malformed input).
static class SelfTests
{
    public static int Bench(string[] files, TextWriter o)
    {
        foreach (var f in files)
        {
            string name = Path.GetFileName(f), text = File.ReadAllText(f);
            var first = Kfs.Compile(name, text);
            var warm = new List<double>();
            for (int i = 0; i < 30; i++) warm.Add(Kfs.Compile(name, text).Ms);
            warm.Sort();
            o.WriteLine($"bench {name,-22} lines={text.Split('\n').Length,4} ok={first.Result?.Ok} diags={first.Result?.Diagnostics.Count} " +
                        $"first={first.Ms,7:0.0} ms  warm min={warm[0]:0.0} median={warm[warm.Count / 2]:0.0} max={warm[^1]:0.0} ms (n=30)");
        }
        return 0;
    }

    static string Signature(Kfs.Outcome r) => r.Error != null
        ? "THREW " + r.Error.GetType().Name + ": " + r.Error.Message
        : $"ok={r.Result!.Ok} img={(r.Result.Image == null ? "-" : Convert.ToHexString(SHA256.HashData(r.Result.Image))[..12])}\n" +
          string.Join("\n", r.Result.Diagnostics.Select(d => d.ToString()));

    /// Compile the same files serially and then from 8 threads at once; compare every result.
    public static int Stress(string[] files, TextWriter o)
    {
        // --cold: run the parallel phase FIRST (no serial warm-up), so lazy statics initialise under contention.
        bool cold = files.Length > 0 && files[0] == "--cold";
        if (cold) files = files[1..];
        var src = files.Select(f => (Name: Path.GetFileName(f), Text: File.ReadAllText(f))).ToArray();
        int mismatches = 0, threw = 0, total = 400;
        string? firstBad = null;
        var sigs = new string[total];
        string[]? reference = cold ? null : src.Select(s => Signature(Kfs.Compile(s.Name, s.Text))).ToArray();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, total, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            var r = Kfs.Compile(src[i % src.Length].Name, src[i % src.Length].Text);
            if (r.Error != null) Interlocked.Increment(ref threw);
            sigs[i] = Signature(r);
        });
        reference ??= src.Select(s => Signature(Kfs.Compile(s.Name, s.Text))).ToArray();
        for (int i = 0; i < total; i++)
        {
            int k = i % src.Length;
            string sig = sigs[i];
            if (sig != reference[k])
            {
                Interlocked.Increment(ref mismatches);
                firstBad ??= $"{src[k].Name}:\n--- serial\n{reference[k]}\n--- parallel\n{sig}";
            }
        }
        o.WriteLine($"stress{(cold ? " (cold)" : "")}: {total} compiles of {src.Length} files on 8 threads in {sw.ElapsedMilliseconds} ms: mismatches={mismatches} threw={threw}");
        if (firstBad != null) o.WriteLine("first mismatch: " + firstBad);
        return mismatches == 0 ? 0 : 1;
    }

    public static int Nasty(string[] files, TextWriter o)
    {
        var cases = new List<(string Label, string Name, string Text)>();
        foreach (var f in files) cases.Add((Path.GetFileName(f), Path.GetFileName(f), File.ReadAllText(f)));
        string head = "#include <kfs.h>\n#pragma kfs tick_hz 10\n#pragma kfs task t rate_hz=10\n";
        foreach (int depth in new[] { 100, 200, 5000, 100000 })
            cases.Add(($"nested parens x{depth}", "deep.c", head + "uint32_t x;\nvoid t(void) { x = " + new string('(', depth) + "1" + new string(')', depth) + "; }\n"));
        cases.Add(("nested blocks x5000", "blocks.c", head + "void t(void) " + string.Concat(Enumerable.Repeat("{", 5000)) + string.Concat(Enumerable.Repeat("}", 5000)) + "\n"));
        cases.Add(("2000 binary operators", "ops.c", head + "uint32_t x;\nvoid t(void) { x = 1" + string.Concat(Enumerable.Repeat(" + 1", 2000)) + "; }\n"));
        cases.Add(("150 undeclared calls", "many.c", head + "void t(void) {\n" + string.Concat(Enumerable.Range(0, 150).Select(i => $"    nope_{i}();\n")) + "}\n"));
        cases.Add(("self-include", "self.c", "#include \"self.c\"\n"));
        cases.Add(("NUL and emoji", "bytes.c", head + "void t(void) { \0 🐱 }\n"));
        cases.Add(("100k-char line", "long.c", head + "uint32_t x;\nvoid t(void) { x = 0" + string.Concat(Enumerable.Repeat(" ", 100000)) + "; }\n"));
        cases.Add(("no .c file (buffer is a .h)", "lib.h", "int f(void);\n"));
        foreach (var (label, name, text) in cases)
        {
            var r = Kfs.Compile(name, text);
            if (r.Error != null) { o.WriteLine($"nasty [{label}] THREW {r.Error.GetType().FullName}: {r.Error.Message} ({r.Ms:0} ms)"); continue; }
            var ds = r.Result!.Diagnostics;
            o.WriteLine($"nasty [{label}] ok={r.Result.Ok} diags={ds.Count} errors={ds.Count(d => d.Severity == KSA.Kfs.Compiler.Severity.Error)} ({r.Ms:0} ms)");
            foreach (var d in ds.Take(4)) o.WriteLine("    " + Trunc(d.ToString()));
            if (ds.Count > 5) o.WriteLine("    ...");
            if (ds.Count > 4) o.WriteLine("    " + Trunc(ds[^1].ToString()));
        }
        return 0;
    }

    static string Trunc(string s) { s = s.Replace("\n", " | ").Replace("\0", "\\0"); return s.Length > 160 ? s[..160] + "…" : s; }
}
