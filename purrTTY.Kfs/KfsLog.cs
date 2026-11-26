using System.Collections.Concurrent;
using purrTTY.Logging;

namespace purrTTY.Kfs;

/// <summary>
/// Logging for the kFS extension: each distinct failure kind is logged once through
/// <c>purrTTY.Logging</c> and only counted afterwards, so a failure that repeats every tick on the
/// dispatch thread cannot flood the game log. Never throws.
/// </summary>
public static class KfsLog
{
    private static readonly ConcurrentDictionary<string, long> Counts = new(StringComparer.Ordinal);
    private static long _total;

    /// <summary>Records a failure of kind <paramref name="key"/>; logs the first occurrence of each kind, counts the rest.</summary>
    public static void Once(string key, Exception error)
    {
        try
        {
            Interlocked.Increment(ref _total);
            long n = Counts.AddOrUpdate(key, 1, static (_, v) => v + 1);
            if (n == 1) Error($"purrTTY kFS: {key} failed (logged once, later failures of this kind are only counted): {error}");
        }
        catch
        {
            // Logging must never take the caller down; the dispatch thread is the caller.
        }
    }

    /// <summary>How many failures of kind <paramref name="key"/> were recorded.</summary>
    public static long Count(string key) => Counts.TryGetValue(key, out long n) ? n : 0;

    /// <summary>How many failures of any kind were recorded.</summary>
    public static long Total => Interlocked.Read(ref _total);

    /// <summary>Every kind recorded so far with its count.</summary>
    public static IReadOnlyDictionary<string, long> Snapshot() => new Dictionary<string, long>(Counts, StringComparer.Ordinal);

    /// <summary>Logs an informational line (the extension's status line).</summary>
    public static void Info(string message)
    {
        try { ModLog.Log.Info($"{message}"); }
        catch { /* never throw from logging */ }
    }

    private static void Error(string message)
    {
        try { ModLog.Log.Error($"{message}"); }
        catch { /* never throw from logging */ }
    }
}
