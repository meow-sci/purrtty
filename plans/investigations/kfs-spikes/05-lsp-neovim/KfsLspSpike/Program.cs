using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace KfsLspSpike;

/// Keep KSA types out of this class: the resolver must be registered before the JIT sees any of them.
static class Entry
{
    /// The process's real stdout. Console.Out is redirected to stderr so that nothing the game code
    /// (or we) print by accident can corrupt the LSP stdio stream.
    public static Stream RealStdout = Stream.Null;

    static int Main(string[] args)
    {
        RealStdout = Console.OpenStandardOutput();
        Console.SetOut(Console.Error);

        string? dir = Environment.GetEnvironmentVariable("KSA_DLL_DIR");
        if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "KSA.dll")))
        {
            Console.Error.WriteLine("set KSA_DLL_DIR to the folder holding KSA.dll");
            return 2;
        }
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            string path = Path.Combine(dir, name.Name + ".dll");
            if (!File.Exists(path)) return null;
            byte[] bytes = File.ReadAllBytes(path);
            int pe = BitConverter.ToInt32(bytes, 0x3C);
            ushort machine = BitConverter.ToUInt16(bytes, pe + 4);
            if (machine == 0x8664 && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                bytes[pe + 4] = 0x64; bytes[pe + 5] = 0xAA;      // IMAGE_FILE_MACHINE_ARM64, in memory only
                Log.W($"[resolver] {name.Name}: AMD64 -> ARM64 in memory");
            }
            return ctx.LoadFromStream(new MemoryStream(bytes));
        };
        return App.Run(args);
    }
}

static class Log
{
    static readonly long T0 = Environment.TickCount64;
    public static void W(string s) => Console.Error.WriteLine($"[{Environment.TickCount64 - T0,6}ms] {s}");
}

static class App
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run(string[] args)
    {
        string? forceEncoding = null;
        int debounceMs = 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--encoding") forceEncoding = args[++i];
            else if (args[i] == "--debounce-ms") debounceMs = int.Parse(args[++i]);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Kfs.Init();
        Log.W($"kfs init: {sw.ElapsedMilliseconds} ms; tables={Kfs.Tables.Origin} services={Kfs.Tables.ServicesData.Services.Count} kc-names={Kfs.ServicesByName.Count}");

        var stdout = new StreamWriter(Entry.RealStdout) { AutoFlush = true };
        if (args.Length > 0 && args[0] == "--bench") return SelfTests.Bench(args.Skip(1).ToArray(), stdout);
        if (args.Length > 0 && args[0] == "--stress") return SelfTests.Stress(args.Skip(1).ToArray(), stdout);
        if (args.Length > 0 && args[0] == "--nasty") return SelfTests.Nasty(args.Skip(1).ToArray(), stdout);

        if (args.Contains("--stdio"))
        {
            var s = new Session("stdio", Console.OpenStandardInput(), Entry.RealStdout, forceEncoding, debounceMs, exitProcessOnExit: true);
            s.Run();
            return s.ExitCode;
        }
        int li = Array.IndexOf(args, "--listen");
        if (li >= 0)
        {
            string ep = args[li + 1];
            int colon = ep.LastIndexOf(':');
            var listener = new TcpListener(IPAddress.Parse(ep[..colon]), int.Parse(ep[(colon + 1)..]));
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            stdout.WriteLine($"LISTENING {port}");
            Log.W($"listening on {ep[..colon]}:{port}");
            int n = 0;
            while (true)
            {
                TcpClient c = listener.AcceptTcpClient();
                c.NoDelay = true;
                int id = ++n;
                Log.W($"client #{id} connected from {c.Client.RemoteEndPoint}");
                var t = new Thread(() =>
                {
                    using (c)
                    {
                        var ns = c.GetStream();
                        // In the game the server must never exit the process: 'exit' only closes this connection.
                        var s = new Session($"tcp#{id}", ns, ns, forceEncoding, debounceMs, exitProcessOnExit: false);
                        try { s.Run(); } catch (Exception e) { Log.W($"client #{id} session died: {e.GetType().Name}: {e.Message}"); }
                    }
                    Log.W($"client #{id} disconnected");
                }) { IsBackground = true, Name = $"lsp-client-{id}" };
                t.Start();
            }
        }
        Console.Error.WriteLine("usage: KfsLspSpike (--stdio | --listen HOST:PORT) [--encoding utf-8|utf-16|utf-32] [--debounce-ms N]\n" +
                                "       KfsLspSpike --bench FILE... | --stress FILE... | --nasty [FILE...]");
        return 2;
    }
}
