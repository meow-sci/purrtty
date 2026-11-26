using System.Runtime.CompilerServices;

namespace purrTTY.Kfs.Lsp;

/// <summary>How important an <see cref="LspLogSink"/> line is.</summary>
public enum LspLogLevel
{
    /// <summary>Method names, timings, dropped results.</summary>
    Debug,
    /// <summary>Connections, the build target, start and stop.</summary>
    Info,
    /// <summary>Something the client or the toolchain did wrong; the server carries on.</summary>
    Warning,
    /// <summary>A failure inside the server.</summary>
    Error,
}

/// <summary>Where the language server writes its log. Must not throw (the server catches anyway) and must never write to a stdio protocol stream.</summary>
public delegate void LspLogSink(LspLogLevel level, string message);

/// <summary>Ready-made <see cref="LspLogSink"/>s.</summary>
public static class LspLog
{
    /// <summary>Discards everything (tests).</summary>
    public static LspLogSink None { get; } = static (_, _) => { };

    /// <summary>
    /// purrTTY's game log (<c>purrTTY.Logging</c>, plan §6.2): debug lines at debug level, the rest at
    /// their own level. Outside the game the log category is not initialised and the lines go nowhere.
    /// </summary>
    public static LspLogSink ModLog { get; } = WriteModLog;

    /// <summary>Lines at <paramref name="minimum"/> or above to <paramref name="writer"/> (purr-kfs writes to stderr).</summary>
    public static LspLogSink Writer(TextWriter writer, LspLogLevel minimum = LspLogLevel.Info)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var gate = new object();
        return (level, message) =>
        {
            if (level < minimum) return;
            lock (gate)
            {
                writer.WriteLine($"purr-kfs lsp: {Name(level)}{message}");
                writer.Flush();
            }
        };
    }

    /// <summary>Calls <paramref name="sink"/> and swallows anything it throws.</summary>
    public static void Safe(this LspLogSink sink, LspLogLevel level, string message)
    {
        try { sink(level, message); }
        catch { /* logging must never take the server down */ }
    }

    private static string Name(LspLogLevel level) => level switch
    {
        LspLogLevel.Debug => "debug: ",
        LspLogLevel.Warning => "warning: ",
        LspLogLevel.Error => "error: ",
        _ => "",
    };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteModLog(LspLogLevel level, string message)
    {
        try
        {
            string line = "purrTTY kFS LSP: " + message;
            switch (level)
            {
                case LspLogLevel.Debug: Logging.ModLog.Log.Debug($"{line}"); break;
                case LspLogLevel.Info: Logging.ModLog.Log.Info($"{line}"); break;
                case LspLogLevel.Warning: Logging.ModLog.Log.Warning($"{line}"); break;
                default: Logging.ModLog.Log.Error($"{line}"); break;
            }
        }
        catch
        {
            // never throw from logging
        }
    }
}
