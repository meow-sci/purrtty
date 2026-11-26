// RULE FOR THIS FILE: no game type.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace purrTTY.Kfs.Lsp.InGame;

/// <summary>What the server info file says (plan §7.6).</summary>
/// <param name="Address"><c>127.0.0.1:&lt;port&gt;</c>.</param>
/// <param name="Token">The session token.</param>
/// <param name="Pid">The game's process id (a reader ignores the file when that process is gone).</param>
/// <param name="KsaBuild">The game's build (<c>v2026.11.25.5949</c>).</param>
/// <param name="WorkspaceRoot">The kFS workspace folder, absolute.</param>
public sealed record LspServerInfo(string Address, string Token, int Pid, string KsaBuild, string WorkspaceRoot);

/// <summary>
/// The server info file, <c>&lt;config&gt;/.purrTTY/kfs-lsp.json</c> (plan §7.6): a UTF-8 JSON object
/// <c>{ "address", "token", "pid", "ksa_build", "workspace_root" }</c>; readers ignore unknown keys.
/// Written atomically (a sibling temporary file renamed over it) and, on Unix, readable by its owner
/// only (mode 0600, set when the temporary file is created, so the token is never in a file anyone
/// else could read). Never inside the workspace (it would show in <c>kfs ls</c> and the editor's file tree).
/// </summary>
public static class LspServerInfoFile
{
    /// <summary>The file name inside <c>&lt;config&gt;/.purrTTY/</c>.</summary>
    public const string FileName = "kfs-lsp.json";

    /// <summary>The JSON text of <paramref name="info"/>.</summary>
    public static string Serialize(LspServerInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var o = new JsonObject
        {
            ["address"] = info.Address,
            ["token"] = info.Token,
            ["pid"] = info.Pid,
            ["ksa_build"] = info.KsaBuild,
            ["workspace_root"] = info.WorkspaceRoot,
        };
        return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>Reads a file (unknown keys ignored); null when it is missing, unreadable or lacks a key.</summary>
    public static LspServerInfo? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            // Bounded, and never opens a FIFO or device someone put there (this runs on the game's main thread).
            if (JsonNode.Parse(purrTTY.Kfs.Runtime.BoundedFile.ReadText(path, 64 * 1024)) is not JsonObject o) return null;
            string? address = Str(o["address"]), token = Str(o["token"]), build = Str(o["ksa_build"]), root = Str(o["workspace_root"]);
            int? pid = o["pid"] is JsonValue v && v.TryGetValue(out int p) ? p : null;
            if (address is null || token is null || pid is null || build is null || root is null) return null;
            return new LspServerInfo(address, token, pid.Value, build, root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes <paramref name="info"/> to <paramref name="path"/> atomically, replacing any file there (a
    /// stale one of a dead process included). Refuses a path inside <paramref name="info"/>'s
    /// workspace. Throws <see cref="IOException"/> / <see cref="UnauthorizedAccessException"/> on failure.
    /// </summary>
    public static void Write(string path, LspServerInfo info)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(info);
        string full = Path.GetFullPath(path);
        if (IsInside(full, info.WorkspaceRoot))
            throw new IOException($"the server info file {full} would be inside the kFS workspace {info.WorkspaceRoot}; not written");
        string directory = Path.GetDirectoryName(full) ?? throw new IOException($"{full} has no folder");
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{FileName}.{Environment.ProcessId}.tmp");
        if (File.Exists(temporary)) File.Delete(temporary);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            using (var stream = new FileStream(temporary, options))
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(Serialize(info));
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, full, overwrite: true);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(full, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* best effort */ }
        }
    }

    /// <summary>
    /// Deletes the file only when it is provably this server's: it parses and carries
    /// <paramref name="token"/> (and, when given, <paramref name="pid"/>). Another server's file, or a
    /// file that cannot be parsed — someone else's content, a half-written edit — is left alone (the
    /// next start replaces it atomically). True when the file is gone.
    /// </summary>
    public static bool DeleteIfOurs(string path, string token, int? pid = null)
    {
        try
        {
            if (!File.Exists(path)) return true;
            if (Read(path) is not { } info || !string.Equals(info.Token, token, StringComparison.Ordinal)) return false;
            if (pid is { } p && info.Pid != p) return false;
            File.Delete(path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or below it (case-insensitively off Linux).</summary>
    public static bool IsInside(string path, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;
        string p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string f = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        StringComparison c = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return p.Equals(f, c) || p.StartsWith(f + Path.DirectorySeparatorChar, c);
    }

    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue(out string? s) ? s : null;
}
