// RULE FOR THIS FILE: no game type.

using System.Text;

namespace purrTTY.Kfs.Lsp.Documents;

/// <summary>
/// Maps <c>file:</c> URIs to workspace-relative paths (forward slashes, as <c>ProjectResolver</c> takes
/// them) and back. Handles percent-encoding (UTF-8, any case of hex digits), an optional
/// <c>localhost</c> authority, Windows drive letters in every spelling clients use
/// (<c>file:///C:/x</c>, <c>file:///c%3A/x</c>, <c>file:///c|/x</c>), UNC paths, a workspace reached
/// through a symbolic link (macOS <c>/tmp</c> → <c>/private/tmp</c>), and case: on a case-insensitive
/// file system a path that matches the root ignoring case is inside it, and each segment is given
/// the casing it has on disk, so an overlay key always matches the file the game would read.
/// </summary>
public sealed class WorkspaceUris
{
    private readonly string[] _roots;
    private readonly bool _windows;
    private readonly StringComparison _comparison;

    /// <summary>A mapper for the workspace folder <paramref name="root"/> (made absolute).</summary>
    /// <param name="root">The workspace folder.</param>
    /// <param name="caseInsensitive">Whether the file system ignores case; default: probed on <paramref name="root"/>.</param>
    /// <param name="windows">Windows path rules (drive letters, <c>\</c>); default: the running OS. Tests pass true to check Windows URIs anywhere.</param>
    public WorkspaceUris(string root, bool? caseInsensitive = null, bool? windows = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _windows = windows ?? OperatingSystem.IsWindows();
        // A root in the other OS's convention (tests) is taken as written: no disk is consulted for it.
        bool native = _windows == OperatingSystem.IsWindows();
        string normalized = _windows ? NormalizeWindows(root)
            : native ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) : root.TrimEnd('/');
        Root = normalized;
        CaseInsensitive = caseInsensitive ?? (_windows || (native && ProbeCaseInsensitive(normalized)));
        _comparison = CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var roots = new List<string> { Slashes(normalized) };
        if (native && !_windows && RealPath(normalized) is { } real && !string.Equals(real, normalized, StringComparison.Ordinal)) roots.Add(Slashes(real));
        _roots = roots.ToArray();
    }

    /// <summary>The workspace folder, absolute, without a trailing separator.</summary>
    public string Root { get; }

    /// <summary>Whether paths are compared ignoring case.</summary>
    public bool CaseInsensitive { get; }

    /// <summary>
    /// The workspace-relative path (forward slashes, on-disk casing where the file exists) of a
    /// <c>file:</c> URI, or null when the URI is not a file URI, is malformed, or lies outside the workspace.
    /// </summary>
    public string? ToPath(string uri)
    {
        if (LocalPath(uri, _windows) is not { } local) return null;
        string full = Slashes(local);
        foreach (string root in _roots)
        {
            if (full.Length <= root.Length + 1) continue;
            if (!full.StartsWith(root, _comparison) || full[root.Length] != '/') continue;
            string relative = full[(root.Length + 1)..];
            if (relative.Split('/').Any(s => s is "" or "." or "..")) return null;
            return CaseInsensitive ? DiskCase(relative) : relative;
        }
        return null;
    }

    /// <summary>
    /// The workspace root's URI exactly as the client spelled it in <paramref name="uri"/>, the URI of
    /// workspace path <paramref name="path"/> (<see cref="ToPath"/> of it); null when it cannot be told
    /// (a segment the client encoded a <c>/</c> into, say).
    /// </summary>
    public string? ClientRoot(string uri, string path)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(path);
        int query = uri.IndexOfAny(['?', '#']);
        string bare = query >= 0 ? uri[..query] : uri;
        int segments = path.Split('/').Length;
        int cut = bare.Length;
        for (int i = 0; i < segments; i++)
        {
            cut = bare.LastIndexOf('/', cut - 1);
            if (cut <= 0) return null;
        }
        string root = bare[..cut];
        return string.Equals(ToPath(Under(root, path)), path, StringComparison.Ordinal) ? root : null;
    }

    /// <summary>The URI of workspace path <paramref name="path"/> under a root URI (<see cref="ClientRoot"/>), each segment percent-encoded.</summary>
    public static string Under(string rootUri, string path)
    {
        ArgumentNullException.ThrowIfNull(rootUri);
        ArgumentNullException.ThrowIfNull(path);
        return rootUri + "/" + string.Join('/', path.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
    }

    /// <summary>The <c>file:</c> URI of a workspace-relative path.</summary>
    public string ToUri(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string full = path.Length == 0 ? Slashes(Root) : Slashes(Root) + "/" + path.Replace('\\', '/');
        return FileUri(full, _windows);
    }

    /// <summary>
    /// The local path of a <c>file:</c> URI (forward slashes on every OS; a Windows drive as
    /// <c>C:/…</c>, a UNC path as <c>//server/share/…</c>), or null when it is not one.
    /// </summary>
    public static string? LocalPath(string uri, bool windows)
    {
        if (string.IsNullOrEmpty(uri) || !uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return null;
        string rest = uri[5..];
        int query = rest.IndexOfAny(['?', '#']);
        if (query >= 0) rest = rest[..query];
        string? authority = null;
        if (rest.StartsWith("//", StringComparison.Ordinal))
        {
            rest = rest[2..];
            int slash = rest.IndexOf('/');
            authority = slash >= 0 ? rest[..slash] : rest;
            rest = slash >= 0 ? rest[slash..] : "/";
        }
        string? path = Decode(rest);
        if (path is null || path.Length == 0 || path[0] != '/') return null;
        if (authority is { Length: > 0 } && !authority.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            // A UNC path: file://server/share/x
            return windows ? "//" + Decode(authority) + path : null;
        }
        if (windows)
        {
            // /C:/x, /c|/x → C:/x
            if (path.Length >= 3 && char.IsAsciiLetter(path[1]) && (path[2] == ':' || path[2] == '|'))
                return char.ToUpperInvariant(path[1]) + ":" + (path.Length > 3 ? path[3..] : "/");
            return path;
        }
        return path;
    }

    /// <summary>The <c>file:</c> URI of an absolute local path (each segment percent-encoded; a drive letter kept as <c>C:</c>).</summary>
    public static string FileUri(string fullPath, bool windows)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        string p = fullPath.Replace('\\', '/');
        string prefix = "file://";
        if (windows && p.StartsWith("//", StringComparison.Ordinal))
        {
            int slash = p.IndexOf('/', 2);
            prefix += Uri.EscapeDataString(slash >= 0 ? p[2..slash] : p[2..]);
            p = slash >= 0 ? p[slash..] : "/";
        }
        else if (windows && p.Length >= 2 && char.IsAsciiLetter(p[0]) && p[1] == ':')
        {
            prefix += "/" + char.ToUpperInvariant(p[0]) + ":";
            p = p[2..];
        }
        if (!p.StartsWith('/')) p = "/" + p;
        return prefix + string.Join('/', p.Split('/').Select(Uri.EscapeDataString));
    }

    // ------------------------------------------------------------------ helpers

    private static string? Decode(string text)
    {
        if (!text.Contains('%')) return text;
        try
        {
            var bytes = new List<byte>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '%')
                {
                    if (i + 2 >= text.Length || !Uri.IsHexDigit(text[i + 1]) || !Uri.IsHexDigit(text[i + 2])) return null;
                    bytes.Add((byte)Convert.ToInt32(text.Substring(i + 1, 2), 16));
                    i += 2;
                }
                else
                {
                    bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
                }
            }
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes.ToArray());
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static string Slashes(string path) => path.Replace('\\', '/').TrimEnd('/');

    private static string NormalizeWindows(string root)
    {
        string p = root.Replace('\\', '/').TrimEnd('/');
        if (p.Length >= 2 && char.IsAsciiLetter(p[0]) && p[1] == ':') p = char.ToUpperInvariant(p[0]) + p[1..];
        if (p.Length == 2 && p[1] == ':') p += "/";
        return OperatingSystem.IsWindows() ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)) : p;
    }

    private string DiskCase(string relative)
    {
        // Gives each segment the case it has on disk (the first exact match wins, else the only
        // case-insensitive one); stops at the first segment that does not exist.
        string current = Root;
        var result = new List<string>();
        string[] segments = relative.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            string? onDisk = null;
            try
            {
                if (Directory.Exists(current))
                {
                    bool last = i == segments.Length - 1;
                    IEnumerable<string> entries = last ? Directory.EnumerateFileSystemEntries(current) : Directory.EnumerateDirectories(current);
                    foreach (string entry in entries)
                    {
                        string name = Path.GetFileName(entry);
                        if (string.Equals(name, segment, StringComparison.Ordinal)) { onDisk = name; break; }
                        if (onDisk is null && string.Equals(name, segment, StringComparison.OrdinalIgnoreCase)) onDisk = name;
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                onDisk = null;
            }
            if (onDisk is null)
            {
                result.AddRange(segments[i..]);
                break;
            }
            result.Add(onDisk);
            current = Path.Combine(current, onDisk);
        }
        return string.Join('/', result);
    }

    private static bool ProbeCaseInsensitive(string root)
    {
        try
        {
            string upper = root.ToUpperInvariant(), lower = root.ToLowerInvariant();
            if (string.Equals(upper, lower, StringComparison.Ordinal)) return OperatingSystem.IsMacOS();
            string other = string.Equals(root, upper, StringComparison.Ordinal) ? lower : upper;
            return Directory.Exists(root) && Directory.Exists(other);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return OperatingSystem.IsMacOS();
        }
    }

    /// <summary>The path with every symbolic link along it resolved (a link's target too, whose own prefix may be a link: <c>/var</c> on macOS), or null when that fails.</summary>
    private static string? RealPath(string full, int depth = 0)
    {
        if (depth > 32) return null;   // a link loop
        try
        {
            string? root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root)) return null;
            string current = root;
            foreach (string segment in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                string next = Path.Combine(current, segment);
                FileSystemInfo? target = new DirectoryInfo(next).ResolveLinkTarget(returnFinalTarget: true);
                current = target is null ? next : RealPath(Path.TrimEndingDirectorySeparator(target.FullName), depth + 1) ?? Path.TrimEndingDirectorySeparator(target.FullName);
            }
            return current;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
