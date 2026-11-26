// RULE FOR THIS FILE: no game type.

using purrTTY.Kfs.Lsp.Protocol;

namespace purrTTY.Kfs.Lsp.Documents;

/// <summary>
/// One open document, immutable: every change makes a new instance, so a snapshot can be read on
/// any thread.
/// </summary>
public sealed class LspDocument
{
    private LineIndex? _lines;

    internal LspDocument(string uri, string? path, string languageId, int version, string text, long generation)
    {
        Uri = uri;
        Path = path;
        LanguageId = languageId;
        Version = version;
        Text = text;
        Generation = generation;
    }

    /// <summary>The URI exactly as the client sent it (diagnostics are published under it).</summary>
    public string Uri { get; }

    /// <summary>The workspace-relative path, or null for a document outside the workspace (it is never built).</summary>
    public string? Path { get; }

    /// <summary>The client's language id (<c>kc</c>, <c>c</c>, <c>kasm</c>, …).</summary>
    public string LanguageId { get; }

    /// <summary>The client's version of the text.</summary>
    public int Version { get; }

    /// <summary>The text.</summary>
    public string Text { get; }

    /// <summary>A store-wide counter that grows with every open and change: tells two texts apart even when the client reuses a version.</summary>
    public long Generation { get; }

    /// <summary>The text's lines (built on first use).</summary>
    public LineIndex Lines => _lines ??= new LineIndex(Text);
}

/// <summary>
/// The open documents of one connection, keyed by workspace path (and by URI for documents outside
/// the workspace). Thread-safe: the read thread opens, changes and closes; the build scheduler takes
/// snapshots (<see cref="Overlay"/>) from its own thread.
/// </summary>
public sealed class DocumentStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LspDocument> _byUri = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LspDocument> _byPath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _lastUri = new(StringComparer.Ordinal);
    private string? _clientRoot;
    private long _generation;

    /// <summary>The most paths whose last URI is remembered after their document closed.</summary>
    private const int MaxRememberedUris = 4096;

    /// <summary>A store mapping URIs with <paramref name="uris"/>.</summary>
    public DocumentStore(WorkspaceUris uris)
    {
        Uris = uris ?? throw new ArgumentNullException(nameof(uris));
    }

    /// <summary>The URI ↔ path mapping.</summary>
    public WorkspaceUris Uris { get; }

    /// <summary>Opens (or reopens) a document.</summary>
    public LspDocument Open(string uri, string languageId, int version, string text)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(text);
        string? path = Uris.ToPath(uri);
        lock (_gate)
        {
            if (_byUri.Remove(uri, out var old) && old.Path is not null) _byPath.Remove(old.Path);
            if (path is not null && _byPath.Remove(path, out var samePath)) _byUri.Remove(samePath.Uri);
            var doc = new LspDocument(uri, path, languageId ?? "", version, text, ++_generation);
            _byUri[uri] = doc;
            if (path is not null)
            {
                _byPath[path] = doc;
                if (_lastUri.Count < MaxRememberedUris || _lastUri.ContainsKey(path)) _lastUri[path] = uri;
                if (Uris.ClientRoot(uri, path) is { } root) _clientRoot = root;
            }
            return doc;
        }
    }

    /// <summary>Replaces a document's text (full sync); null when it is not open.</summary>
    public LspDocument? Change(string uri, int version, string text)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate)
        {
            if (!_byUri.TryGetValue(uri, out var old)) return null;
            var doc = new LspDocument(old.Uri, old.Path, old.LanguageId, version, text, ++_generation);
            _byUri[uri] = doc;
            if (doc.Path is not null) _byPath[doc.Path] = doc;
            return doc;
        }
    }

    /// <summary>
    /// Applies a <c>didChange</c>'s <c>contentChanges</c> in order: an entry without a range replaces
    /// the text, one with a range edits it (positions in <paramref name="encoding"/>). Null when the
    /// document is not open.
    /// </summary>
    public LspDocument? Apply(string uri, int version, IEnumerable<(LspRange? Range, string Text)> changes, PositionEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(changes);
        string text;
        lock (_gate)
        {
            if (!_byUri.TryGetValue(uri, out var old)) return null;
            text = old.Text;
        }
        foreach ((LspRange? range, string replacement) in changes)
        {
            if (range is not { } r)
            {
                text = replacement;
                continue;
            }
            var lines = new LineIndex(text);
            int start = PositionMapper.Offset(lines, r.Start, encoding);
            int end = Math.Max(start, PositionMapper.Offset(lines, r.End, encoding));
            text = string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(end));
        }
        return Change(uri, version, text);
    }

    /// <summary>Closes a document; the closed instance, or null when it was not open.</summary>
    public LspDocument? Close(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        lock (_gate)
        {
            if (!_byUri.Remove(uri, out var doc)) return null;
            if (doc.Path is not null) _byPath.Remove(doc.Path);
            return doc;
        }
    }

    /// <summary>The open document with this URI, or null.</summary>
    public LspDocument? Get(string uri)
    {
        lock (_gate) return _byUri.GetValueOrDefault(uri);
    }

    /// <summary>The open document at this workspace path, or null.</summary>
    public LspDocument? ByPath(string path)
    {
        lock (_gate) return _byPath.GetValueOrDefault(path);
    }

    /// <summary>Every open document.</summary>
    public IReadOnlyList<LspDocument> All
    {
        get { lock (_gate) return _byUri.Values.ToList(); }
    }

    /// <summary>The open documents inside the workspace, by path: the overlay a build reads instead of the disk.</summary>
    public IReadOnlyDictionary<string, LspDocument> Overlay()
    {
        lock (_gate) return new Dictionary<string, LspDocument>(_byPath, StringComparer.Ordinal);
    }

    /// <summary>
    /// The URI to publish under for a workspace path, in the form the client uses: the open
    /// document's own; else the URI it last opened that path under (a clear after <c>didClose</c>);
    /// else the path under the workspace root as the client spells it (a workspace opened through a
    /// symbolic link: <c>/tmp/…</c> where the server's root is <c>/private/tmp/…</c>); else one made
    /// from the server's root.
    /// </summary>
    public string UriOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string? root;
        lock (_gate)
        {
            if (_byPath.TryGetValue(path, out var doc)) return doc.Uri;
            if (_lastUri.TryGetValue(path, out string? last)) return last;
            root = _clientRoot;
        }
        return root is null ? Uris.ToUri(path) : WorkspaceUris.Under(root, path);
    }
}
