// RULE FOR THIS FILE (and CliApp.cs, CliOptions.cs, HelpText.cs): no game type (KSA.*, Brutal.*,
// Planet.*) — not in a signature, a field, a local or a generic argument. The game's assemblies are
// resolved by KsaAssemblyResolver, which CliApp installs before it dispatches to a command; the
// runtime loads every type a method mentions when it JIT-compiles that method, so a game type here
// would ask for KSA.dll before the handler that supplies it exists. Game types live in Commands/.

using System.Runtime.InteropServices;
using System.Text;

namespace purrTTY.Kfs.Cli;

/// <summary>The <c>purr-kfs</c> entry point: a thin wrapper over <see cref="CliApp.Run(string[], TextWriter, TextWriter)"/>.</summary>
public static class Program
{
    /// <summary>Runs the tool with the process's console and environment; returns the exit code.</summary>
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] != "lsp") return CliApp.Run(args, Console.Out, Console.Error);

        // purr-kfs lsp: stdout carries the protocol (--stdio) or the one LISTENING line (--listen).
        // Everything else that might print — the game's code included — goes to stderr.
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        Console.SetOut(Console.Error);
        using var stop = new CancellationTokenSource();
        // The handlers cancel the command, which then returns promptly in both modes (--stdio cuts
        // its server off even while stdin stays open). They never throw (a signal that comes in as
        // the process exits finds the source disposed).
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            TryCancel(stop);
        };
        using PosixSignalRegistration term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            TryCancel(stop);
        });
        return CliApp.Run(args, stdout, Console.Error, CliEnvironment.Process with { Cancellation = stop.Token });
    }

    private static void TryCancel(CancellationTokenSource source)
    {
        try { source.Cancel(); }
        catch (Exception) { /* exiting */ }
    }
}
