// RULE FOR THIS FILE: no game type (KSA.*, Brutal.*, Planet.*) anywhere — it is shared by the
// command-line tool (before and after the game assemblies are resolved), the language server and
// its tests, which run with no game at all.

namespace purrTTY.Kfs.Runtime;

/// <summary>The language a <see cref="KfsProject"/> is built as.</summary>
public enum KfsProjectLanguage
{
    /// <summary>kC: the project has a <c>.c</c> file; built by the kC compiler (which also assembles its <c>.kasm</c> files).</summary>
    Kc,

    /// <summary>Pure kASM: no <c>.c</c> file; every <c>.kasm</c> file is assembled, <c>.include "…"</c> is read through <see cref="KfsProject.ReadInclude"/>.</summary>
    Kasm,
}

/// <summary>One file of a <see cref="KfsProject"/>.</summary>
/// <param name="Name">The name the compiler sees and reports (the bare file name in a folder project; the include text at the root).</param>
/// <param name="Path">The workspace-relative path, forward slashes.</param>
/// <param name="Text">The text that is built: the overlay's when it has one, else the disk's.</param>
/// <param name="FromOverlay">True when <see cref="Text"/> came from the overlay.</param>
public sealed record KfsProjectFile(string Name, string Path, string Text, bool FromOverlay);

/// <summary>
/// Read access to a kFS workspace folder, for <see cref="ProjectResolver"/>. Paths are
/// workspace-relative with forward slashes; <c>""</c> is the workspace root.
/// </summary>
public interface IWorkspaceFiles
{
    /// <summary>
    /// The names of the files directly in <paramref name="folder"/> (no subfolders, no recursion),
    /// in any order; null when the folder does not exist.
    /// </summary>
    IReadOnlyList<string>? FilesIn(string folder);

    /// <summary>True when the file exists (with whatever case rules the store has).</summary>
    bool Exists(string path);

    /// <summary>The file's text.</summary>
    /// <exception cref="IOException">It cannot be read (missing, too large, …); the message says why.</exception>
    string Read(string path);
}

/// <summary>
/// An <see cref="IWorkspaceFiles"/> over a real folder. Case rules are the file system's (as the
/// game's, which calls <see cref="File.Exists(string)"/> too). Reads refuse files over
/// <see cref="ProjectResolver.MaxSourceBytes"/> with the game's message, and refuse any path that
/// leaves the workspace (rooted, or <c>..</c> above the root) — the game would read such a path;
/// this tool does not read outside the folder it was given.
/// </summary>
public sealed class DiskWorkspaceFiles : IWorkspaceFiles
{
    /// <summary>A store over <paramref name="root"/> (made absolute).</summary>
    public DiskWorkspaceFiles(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    /// <summary>The workspace folder, absolute, without a trailing separator.</summary>
    public string Root { get; }

    /// <summary>The absolute path of a workspace-relative path, or null when it would leave the workspace.</summary>
    public string? FullPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length == 0) return Root;
        if (Path.IsPathRooted(path)) return null;
        string full = Path.GetFullPath(Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar)));
        if (string.Equals(full, Root, StringComparison.Ordinal)) return full;
        return full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? full : null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string>? FilesIn(string folder)
    {
        string? full = FullPath(folder);
        if (full is null || !Directory.Exists(full)) return null;
        return Directory.EnumerateFiles(full).Select(f => Path.GetFileName(f)).ToList();
    }

    /// <inheritdoc/>
    public bool Exists(string path)
    {
        // As File.Exists (the game's check): a path the file system refuses (a NUL in a quoted
        // include name, say) does not exist; it never throws.
        try { return FullPath(path) is { } full && File.Exists(full); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) { return false; }
    }

    /// <inheritdoc/>
    public string Read(string path)
    {
        string full = FullPath(path) ?? throw new IOException($"{path} is outside the workspace");
        // Workspace.TooLarge / ReadSource (Workspace.cs:85-103): the same limit and message.
        return BoundedFile.ReadText(full, ProjectResolver.MaxSourceBytes);
    }
}

/// <summary>
/// Reads a text file without ever blocking on, or streaming from, something that is not a regular
/// file, and never more than a limit. .NET cannot tell a FIFO or a device from a file portably, but
/// they report no size: an entry of size 0 (symbolic links followed) is read as empty text without
/// being opened — for an empty file that is its text. A file over the limit is refused with the
/// game's message, and the read itself stops at the limit (a file that grows meanwhile is refused
/// too). Byte-order marks are honoured as <see cref="File.ReadAllText(string)"/> does.
/// </summary>
public static class BoundedFile
{
    /// <summary>The text of <paramref name="fullPath"/>; throws <see cref="IOException"/> (missing, over <paramref name="maxBytes"/>, unreadable) or <see cref="UnauthorizedAccessException"/>.</summary>
    public static string ReadText(string fullPath, long maxBytes)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException($"{info.Name}: no such file", fullPath);
        FileInfo target = info.LinkTarget is null ? info : info.ResolveLinkTarget(returnFinalTarget: true) as FileInfo ?? info;
        if (!target.Exists) throw new FileNotFoundException($"{info.Name}: no such file", fullPath);
        long length = target.Length;
        if (length > maxBytes) throw TooLarge(info.Name, length, maxBytes);
        if (length == 0) return "";   // an empty file — or a FIFO, socket or device: never opened
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        var buffer = new MemoryStream((int)Math.Min(length, maxBytes) + 1);
        byte[] chunk = new byte[64 * 1024];
        while (true)
        {
            int n = stream.Read(chunk, 0, (int)Math.Min(chunk.Length, maxBytes + 1 - buffer.Length));
            if (n <= 0) break;
            buffer.Write(chunk, 0, n);
            if (buffer.Length > maxBytes) throw TooLarge(info.Name, buffer.Length, maxBytes);
        }
        buffer.Position = 0;
        using var reader = new StreamReader(buffer, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static IOException TooLarge(string name, long length, long max) =>
        new($"{name} is {length} bytes; the largest source file kFS reads is {max} bytes");
}

/// <summary>
/// An in-memory <see cref="IWorkspaceFiles"/> (tests, editors with no disk). Paths are
/// case-sensitive (ordinal). A folder exists when a file is in it or below it, or it was added
/// with <see cref="AddFolder"/>.
/// </summary>
public sealed class MemoryWorkspaceFiles : IWorkspaceFiles
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _folders = new(StringComparer.Ordinal) { "" };

    /// <summary>An empty workspace.</summary>
    public MemoryWorkspaceFiles() { }

    /// <summary>A workspace holding <paramref name="files"/> (path → text).</summary>
    public MemoryWorkspaceFiles(IEnumerable<KeyValuePair<string, string>> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        foreach ((string path, string text) in files) Add(path, text);
    }

    /// <summary>Adds or replaces a file (and its folders).</summary>
    public MemoryWorkspaceFiles Add(string path, string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(text);
        path = ProjectResolver.Normalize(path);
        _files[path] = text;
        for (string folder = ProjectResolver.FolderOf(path); folder.Length > 0; folder = ProjectResolver.FolderOf(folder)) _folders.Add(folder);
        return this;
    }

    /// <summary>Adds an empty folder.</summary>
    public MemoryWorkspaceFiles AddFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        for (folder = ProjectResolver.Normalize(folder); folder.Length > 0; folder = ProjectResolver.FolderOf(folder)) _folders.Add(folder);
        return this;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string>? FilesIn(string folder) =>
        _folders.Contains(folder)
            ? _files.Keys.Where(p => string.Equals(ProjectResolver.FolderOf(p), folder, StringComparison.Ordinal)).Select(ProjectResolver.NameOf).ToList()
            : null;

    /// <inheritdoc/>
    public bool Exists(string path) => _files.ContainsKey(path);

    /// <inheritdoc/>
    public string Read(string path) =>
        _files.TryGetValue(path, out string? text) ? text : throw new FileNotFoundException($"{path}: not found in the workspace", path);
}

/// <summary>
/// What <see cref="ProjectResolver.Resolve"/> produced: a <see cref="Project"/>, or null with
/// <see cref="Reason"/> saying why there is nothing to build (the game's <c>why</c>, or the read
/// failure's message, which the game's <c>Workbench.BuildText</c> reports as a <c>kfs</c> error).
/// </summary>
public sealed record ProjectResolution(KfsProject? Project, string? Reason)
{
    /// <summary>True when there is a project.</summary>
    public bool HasProject => Project is not null;
}

/// <summary>
/// A kFS project as the game's workbench would build it (the game's <c>BuildProject</c> plus what
/// an editor needs): its name and folder, the ordered file map, the language, which files came
/// from the overlay, and the mapping between compiler-reported names and workspace paths.
/// </summary>
public sealed class KfsProject
{
    private readonly Func<string, (string Path, string Text, bool FromOverlay)?> _lookup;
    private readonly Dictionary<string, string> _includes = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    internal KfsProject(string file, string folder, string name, IReadOnlyList<KfsProjectFile> files,
        Func<string, (string Path, string Text, bool FromOverlay)?> lookup)
    {
        File = file;
        Folder = folder;
        Name = name;
        Files = files;
        _lookup = lookup;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KfsProjectFile f in files) map[f.Name] = f.Text;
        FileMap = map;
        FromOverlay = files.Where(f => f.FromOverlay).Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        // Workspace.cs:192 (folder: any key ends ".c", OrdinalIgnoreCase); :226 (root: the file's
        // lower-cased extension is ".c").
        Language = folder.Length > 0
            ? files.Any(f => f.Name.EndsWith(".c", StringComparison.OrdinalIgnoreCase)) ? KfsProjectLanguage.Kc : KfsProjectLanguage.Kasm
            : Path.GetExtension(file).Equals(".c", StringComparison.OrdinalIgnoreCase) ? KfsProjectLanguage.Kc : KfsProjectLanguage.Kasm;
    }

    /// <summary>The file the project was resolved for (workspace-relative, forward slashes).</summary>
    public string File { get; }

    /// <summary>The project's folder (<c>""</c> for a root-level project).</summary>
    public string Folder { get; }

    /// <summary>The project name: the folder's last segment, or the root file's name without extension (the compiler's <c>ProjectName</c>).</summary>
    public string Name { get; }

    /// <summary>The language: kC when <see cref="FileMap"/> has a <c>.c</c> file (folder) or the root file is <c>.c</c>.</summary>
    public KfsProjectLanguage Language { get; }

    /// <summary><c>kC</c> or <c>kASM</c>, as the game names them.</summary>
    public string LanguageName => Language == KfsProjectLanguage.Kc ? "kC" : "kASM";

    /// <summary>The project's files, in the game's order (folder: ordinal by name; root: the file, then the include walk's order).</summary>
    public IReadOnlyList<KfsProjectFile> Files { get; }

    /// <summary>Name → text, in <see cref="Files"/> order, ordinal keys: what the compiler gets as <c>CompileInput.Files</c>.</summary>
    public IReadOnlyDictionary<string, string> FileMap { get; }

    /// <summary>The names of <see cref="Files"/> whose text came from the overlay.</summary>
    public IReadOnlySet<string> FromOverlay { get; }

    /// <summary>The <c>.kasm</c> sources a pure-kASM build assembles, in <see cref="Files"/> order (<c>Workbench.cs:530-531</c>).</summary>
    public IEnumerable<KfsProjectFile> KasmSources =>
        Files.Where(f => f.Name.EndsWith(".kasm", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Pure kASM <c>.include "…"</c> names reached so far (by the resolver's scan, and by
    /// <see cref="ReadInclude"/> during a build) → workspace path.
    /// </summary>
    public IReadOnlyDictionary<string, string> Includes
    {
        get { lock (_gate) return new Dictionary<string, string>(_includes, StringComparer.Ordinal); }
    }

    /// <summary>The workspace path of a project file name (<c>BuildProject.WorkspacePath</c>, <c>BuildProject.cs:7-14</c>).</summary>
    public string WorkspacePath(string name) => Folder.Length == 0 ? name : Folder + "/" + name;

    /// <summary>
    /// The text a pure-kASM <c>.include "name"</c> reads, exactly as the workbench's
    /// <c>ProjectFiles</c> callback (<c>Workbench.cs:528</c>): <c>folder/name</c> when the project has a
    /// folder and that file exists, else root-level <c>name</c>; null when neither exists. The
    /// overlay wins over the disk. A name that is found is remembered in <see cref="Includes"/>.
    /// (Inside a kC project the compiler resolves kASM includes from <see cref="FileMap"/> only.)
    /// </summary>
    public string? ReadInclude(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_lookup(name) is not { } hit) return null;
        lock (_gate) _includes[name] = hit.Path;
        return hit.Text;
    }

    internal (string Path, string Text, bool FromOverlay)? LookupInclude(string name) => _lookup(name);

    internal void AddInclude(string name, string path)
    {
        lock (_gate) _includes.TryAdd(name, path);
    }

    /// <summary>
    /// The workspace path of a file name the compiler or assembler reported (a diagnostic's or a
    /// trap's <c>File</c>), or null for a name that is no workspace file: system and generated
    /// headers (<c>&lt;kfs.h&gt;</c>, <c>&lt;vehicle.h&gt;</c>, <c>X.kasm.h</c>), <c>&lt;predefined&gt;</c>, the
    /// project's own name (diagnostics with no position). Exact, ordinal: the compiler reports the
    /// keys it was given.
    /// </summary>
    public string? PathOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (FileMap.ContainsKey(name)) return WorkspacePath(name);
        lock (_gate)
        {
            if (_includes.TryGetValue(name, out string? path)) return path;
        }
        return null;
    }

    /// <summary>The name a workspace path has in this project, or null when it is not one of its files or reached includes.</summary>
    public string? NameOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        path = ProjectResolver.Normalize(path);
        foreach (KfsProjectFile f in Files)
        {
            if (string.Equals(f.Path, path, StringComparison.Ordinal)) return f.Name;
        }
        lock (_gate)
        {
            foreach ((string name, string p) in _includes)
            {
                if (string.Equals(p, path, StringComparison.Ordinal)) return name;
            }
        }
        return null;
    }
}

/// <summary>
/// Decides which files make up a kFS project — the game workbench's rules
/// (<c>KSA.Kfs.Host.Workbench.Workspace.ProjectOf</c>, decompiled <c>Workspace.cs:176-235</c>) — over
/// a workspace folder (<see cref="IWorkspaceFiles"/>) plus an <b>overlay</b> of unsaved editor
/// buffers (workspace path → text). Shared by the command-line tool and the language server.
/// </summary>
/// <remarks>
/// <para>The rules, each as the game has it (file:line in the decompiled <c>KSA.Kfs.Host.Workbench</c>):</para>
/// <list type="bullet">
/// <item><b>Paths.</b> The file is workspace-relative; <c>\</c> becomes <c>/</c> (<c>Workspace.cs:179</c>).
/// Its folder is everything before the last <c>/</c> (<c>FolderOf</c>, <c>Workspace.cs:123-132</c>), so a
/// folder may be nested at any depth.</item>
/// <item><b>Folder project</b> (the file has a folder, <c>Workspace.cs:182-193</c>): every file
/// <b>directly</b> in that folder (one level, no recursion) whose extension is <c>.kasm</c>, <c>.kinc</c>,
/// <c>.c</c> or <c>.h</c> — extension compared <b>case-insensitively</b> (<c>IsSource</c>, <c>Workspace.cs:15, 61-64</c>)
/// — except files named exactly <c>vehicle.h</c> or <c>vehicle.kinc</c> (ordinal, <b>case-sensitive</b>:
/// <c>Vehicle.h</c> is a project file; <c>Workspace.cs:187</c>), in <b>ordinal</b> order (<c>Workspace.cs:184</c>),
/// keyed by bare file name in an ordinal dictionary (<c>Workspace.cs:181</c>). Hidden (dot) files are
/// not skipped (only <c>List</c> skips them). The target file need not be one of them. Name = the
/// folder's last segment; kC when any key ends with <c>.c</c> (case-insensitive).
/// <b>Overlay:</b> the union of the folder's files on disk and the overlay's entries in that folder
/// (same filters); an overlay entry can add a file that was never saved, never delete one. The game
/// overlays one buffer only and never enumerates an unsaved file.</item>
/// <item><b>Root header</b>: a root-level file whose lower-cased extension is <c>.h</c> or <c>.kinc</c>
/// is no project; the reason is the game's text (<c>Workspace.cs:194-199</c>).</item>
/// <item><b>Root file</b> (<c>Workspace.cs:200-226</c>): the file itself, keyed by its name; name = file name
/// without extension; kC when its extension is <c>.c</c> (case-insensitive), otherwise pure kASM —
/// for any other extension too, as in the game. A <c>.c</c> file's text is walked breadth-first for
/// quoted includes with the game's multiline regex <c>^\s*#\s*include\s*"([^"]+)"\s*</c>
/// (<c>Workspace.cs:169</c>; it matches inside block comments and inactive <c>#if</c> blocks too, and
/// its trailing <c>\s*</c> eats the next line's newline and indentation, so an <b>indented</b> include
/// directly after another include line is missed — kept, to match the game):
/// <c>"X.kasm.h"</c> (suffix compared case-insensitively) adds <c>X.kasm</c> — the include text minus its
/// last two characters, a <c>/</c> allowed — when it exists, without scanning it
/// (<c>Workspace.cs:209-217</c>); any other name with no <c>/</c> that exists is added and scanned in
/// turn (<c>Workspace.cs:218-222</c>). Root <c>vehicle.h</c> is <b>not</b> excluded on this path. Existence
/// and lookup are exact ordinal hits on the overlay, then the store's <see cref="IWorkspaceFiles.Exists"/>
/// (the game: <c>File.Exists</c>, so on a case-insensitive file system <c>"UTIL.h"</c> finds <c>util.h</c>
/// and is keyed <c>UTIL.h</c>). <b>Overlay:</b> the walk reads the overlaid text, so an unsaved
/// <c>#include "x.h"</c> is followed, and an overlay-only file exists.</item>
/// <item><b>Text</b>: the overlay's text when the path is an overlay key (ordinal, after <c>\</c> → <c>/</c>),
/// else the store's (<c>Text</c>, <c>Workspace.cs:227-234</c>; the game honours one open file).</item>
/// <item><b>Pure-kASM includes</b> (<c>Workbench.cs:526-529</c>): <c>.include "name"</c> reads
/// <c>folder/name</c>, then root-level <c>name</c>; see <see cref="KfsProject.ReadInclude"/>. The resolver
/// also scans the kASM sources (and what they reach) for <c>.include "…"</c> lines to fill
/// <see cref="KfsProject.Includes"/> up front; that scan is informational, the build reads through
/// <see cref="KfsProject.ReadInclude"/>.</item>
/// <item><b>Read failures</b>: a missing root file, a missing folder or a file over
/// <see cref="MaxSourceBytes"/> make the game's <c>ProjectOf</c> throw an <c>IOException</c>, which
/// <c>Workbench.BuildText</c> turns into its <c>kfs</c> error (<c>Workbench.cs:488-499</c>); here they give
/// no project with the reason.</item>
/// </list>
/// </remarks>
public sealed class ProjectResolver
{
    /// <summary>The largest source file kFS reads (<c>Workspace.MAX_SOURCE_BYTES</c>, <c>Workspace.cs:19</c>).</summary>
    public const long MaxSourceBytes = 4194304L;

    /// <summary>The project source extensions (<c>Workspace.Extensions</c>, <c>Workspace.cs:15</c>), compared case-insensitively.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".kasm", ".kinc", ".c", ".h"];

    /// <summary>The generated headers a folder project never includes (<c>Workspace.cs:187</c>), compared ordinally.</summary>
    public static IReadOnlyList<string> GeneratedHeaders { get; } = ["vehicle.h", "vehicle.kinc"];

    private readonly IWorkspaceFiles _store;

    /// <summary>A resolver over <paramref name="store"/>.</summary>
    public ProjectResolver(IWorkspaceFiles store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>A resolver over the folder <paramref name="workspaceRoot"/> on disk.</summary>
    public static ProjectResolver ForDisk(string workspaceRoot) => new(new DiskWorkspaceFiles(workspaceRoot));

    /// <summary>The store this resolver reads.</summary>
    public IWorkspaceFiles Store => _store;

    /// <summary>
    /// The project <paramref name="file"/> belongs to, with <paramref name="overlay"/> (workspace
    /// path → unsaved text; null or empty for none) over the store. See the class remarks for the rules.
    /// </summary>
    /// <param name="file">The file being edited or built, workspace-relative (<c>proj/main.c</c>, <c>main.c</c>).</param>
    /// <param name="overlay">Open buffers by workspace path; their text wins over the store's.</param>
    public ProjectResolution Resolve(string file, IReadOnlyDictionary<string, string>? overlay = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        file = Normalize(file);
        if (file.EndsWith('/')) throw new ArgumentException($"'{file}' names a folder, not a file", nameof(file));
        var buffers = new Dictionary<string, string>(StringComparer.Ordinal);
        if (overlay is not null)
        {
            foreach ((string path, string text) in overlay)
            {
                if (path is null || text is null) continue;
                buffers[Normalize(path)] = text;
            }
        }

        try
        {
            // An unsaved buffer is held to the game's source limit too (it would refuse the saved file).
            foreach ((string path, string text) in buffers)
            {
                if (text.Length > MaxSourceBytes / 4 && System.Text.Encoding.UTF8.GetByteCount(text) is int bytes && bytes > MaxSourceBytes)
                    throw new NoProject($"{NameOf(path)} is {bytes} bytes; the largest source file kFS reads is {MaxSourceBytes} bytes");
            }
            string folder = FolderOf(file);
            KfsProject project = folder.Length > 0 ? FolderProject(file, folder, buffers) : RootProject(file, buffers, out string? why) ?? throw new NoProject(why!);
            ScanKasmIncludes(project);
            return new ProjectResolution(project, null);
        }
        catch (NoProject e)
        {
            return new ProjectResolution(null, e.Message);
        }
        catch (IOException e)
        {
            return new ProjectResolution(null, e.Message);
        }
        catch (UnauthorizedAccessException e)
        {
            return new ProjectResolution(null, e.Message);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            // A name the file system refuses (a NUL in a quoted include, an illegal character): the
            // game's own walk would throw here; a tool reports it as the reason instead of dying.
            return new ProjectResolution(null, $"{file}: a file name in the project cannot be used ({e.Message})");
        }
    }

    // ------------------------------------------------------------------ the two shapes

    private KfsProject FolderProject(string file, string folder, Dictionary<string, string> buffers)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        IReadOnlyList<string>? onDisk = _store.FilesIn(folder);
        if (onDisk is not null)
        {
            foreach (string name in onDisk)
            {
                if (IsProjectSource(name)) names.Add(name);
            }
        }
        bool overlaid = false;
        foreach (string path in buffers.Keys)
        {
            if (!string.Equals(FolderOf(path), folder, StringComparison.Ordinal)) continue;
            overlaid = true;
            string name = NameOf(path);
            if (IsProjectSource(name)) names.Add(name);
        }
        if (onDisk is null && !overlaid) throw new NoProject($"{folder}: no such folder in the workspace");

        var files = new List<KfsProjectFile>(names.Count);
        foreach (string name in names)
        {
            string path = folder + "/" + name;
            (string text, bool fromOverlay) = Text(path, buffers);
            files.Add(new KfsProjectFile(name, path, text, fromOverlay));
        }
        int slash = folder.LastIndexOf('/');
        return new KfsProject(file, folder, slash >= 0 ? folder[(slash + 1)..] : folder, files, IncludeLookup(folder, buffers));
    }

    private KfsProject? RootProject(string file, Dictionary<string, string> buffers, out string? why)
    {
        why = null;
        string extension = Path.GetExtension(file).ToLowerInvariant();
        if (extension is ".h" or ".kinc")
        {
            why = file + " is a header: build a .c or .kasm file that includes it (a subfolder builds as one project)";
            return null;
        }
        if (!buffers.ContainsKey(file) && !_store.Exists(file)) throw new NoProject($"{file}: not found in the workspace");

        var files = new List<KfsProjectFile>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        (string rootText, bool rootOverlaid) = Text(file, buffers);
        files.Add(new KfsProjectFile(file, file, rootText, rootOverlaid));
        keys.Add(file);

        if (extension == ".c")
        {
            var queue = new Queue<string>();
            queue.Enqueue(rootText);
            while (queue.Count > 0)
            {
                foreach (string value in QuotedIncludes(queue.Dequeue()))
                {
                    if (value.EndsWith(".kasm.h", StringComparison.OrdinalIgnoreCase))
                    {
                        string kasm = value[..^2];
                        if (!keys.Contains(kasm) && Exists(kasm, buffers))
                        {
                            (string text, bool overlaid) = Text(kasm, buffers);
                            files.Add(new KfsProjectFile(kasm, kasm, text, overlaid));
                            keys.Add(kasm);
                        }
                    }
                    else if (!keys.Contains(value) && !value.Contains('/') && Exists(value, buffers))
                    {
                        (string text, bool overlaid) = Text(value, buffers);
                        files.Add(new KfsProjectFile(value, value, text, overlaid));
                        keys.Add(value);
                        queue.Enqueue(text);
                    }
                }
            }
        }
        return new KfsProject(file, "", Path.GetFileNameWithoutExtension(file), files, IncludeLookup("", buffers));
    }

    // ------------------------------------------------------------------ pure kASM includes

    private Func<string, (string Path, string Text, bool FromOverlay)?> IncludeLookup(string folder, Dictionary<string, string> buffers) =>
        name =>
        {
            // Workbench.cs:528: (folder.Length > 0 ? ProjectFile(folder + "/" + name) : null) ?? ProjectFile(name).
            if (folder.Length > 0)
            {
                string inFolder = folder + "/" + name;
                if (TryText(inFolder, buffers) is { } a) return (inFolder, a.Text, a.FromOverlay);
            }
            return TryText(name, buffers) is { } b ? (name, b.Text, b.FromOverlay) : null;
        };

    private static void ScanKasmIncludes(KfsProject project)
    {
        if (project.Language != KfsProjectLanguage.Kasm) return;
        var queue = new Queue<string>(project.KasmSources.Select(f => f.Text));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            foreach (string name in KasmQuotedIncludes(queue.Dequeue()))
            {
                if (!seen.Add(name)) continue;
                (string Path, string Text, bool FromOverlay)? hit;
                try { hit = project.LookupInclude(name); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { continue; }
                if (hit is not { } h) continue;
                project.AddInclude(name, h.Path);
                queue.Enqueue(h.Text);
            }
        }
    }

    // ------------------------------------------------------------------ helpers

    private bool Exists(string path, Dictionary<string, string> buffers) => buffers.ContainsKey(path) || _store.Exists(path);

    private (string Text, bool FromOverlay) Text(string path, Dictionary<string, string> buffers) =>
        buffers.TryGetValue(path, out string? text) ? (text, true) : (_store.Read(path), false);

    private (string Text, bool FromOverlay)? TryText(string path, Dictionary<string, string> buffers)
    {
        if (buffers.TryGetValue(path, out string? text)) return (text, true);
        return _store.Exists(path) ? (_store.Read(path), false) : null;
    }

    /// <summary>
    /// True for a name a folder project takes: extension in <see cref="Extensions"/>
    /// (case-insensitive) and not exactly <c>vehicle.h</c> / <c>vehicle.kinc</c>.
    /// </summary>
    public static bool IsProjectSource(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string extension = Path.GetExtension(name);
        return Extensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase))
            && !GeneratedHeaders.Contains(name, StringComparer.Ordinal);
    }

    /// <summary><c>\</c> → <c>/</c>, as <c>Workspace.ProjectOf</c> does with the file it is given.</summary>
    public static string Normalize(string path) => path.Replace('\\', '/');

    /// <summary>Everything before the last <c>/</c>, or <c>""</c> (<c>Workspace.FolderOf</c>, <c>Workspace.cs:123-132</c>).</summary>
    public static string FolderOf(string path)
    {
        string p = Normalize(path);
        int slash = p.LastIndexOf('/');
        return slash >= 0 ? p[..slash] : "";
    }

    /// <summary>Everything after the last <c>/</c>.</summary>
    public static string NameOf(string path)
    {
        string p = Normalize(path);
        int slash = p.LastIndexOf('/');
        return slash >= 0 ? p[(slash + 1)..] : p;
    }

    /// <summary>
    /// The names the game's quoted-include regex <c>^\s*#\s*include\s*"([^"]+)"\s*</c> (multiline,
    /// <c>Workspace.cs:169</c>) matches in <paramref name="text"/>, in order — the same matches, found by
    /// a hand-written scan that is linear in the text. The regex is quadratic on a run of blank lines
    /// (every line start re-scans the run); <c>\s</c> is <see cref="char.IsWhiteSpace(char)"/>.
    /// Semantics kept: a match starts at a line start; its trailing <c>\s*</c> eats the following line
    /// breaks and indentation, so the scan resumes after them (an indented include right after an
    /// include line is missed, as in the game); <c>[^"]+</c> may span lines.
    /// </summary>
    public static IEnumerable<string> QuotedIncludes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var results = new List<string>();
        int n = text.Length;
        // Memo of the last whitespace run and the last quote search: each is extended monotonically.
        int runFrom = -1, runEnd = -1;
        int quoteFrom = -1, quoteAt = -1;
        int WhitespaceEnd(int i)
        {
            if (i >= runFrom && i <= runEnd && runFrom >= 0) return runEnd;
            int j = i;
            while (j < n && char.IsWhiteSpace(text[j])) j++;
            runFrom = i;
            runEnd = j;
            return j;
        }
        int NextQuote(int i)
        {
            if (quoteFrom >= 0 && i >= quoteFrom && (quoteAt < 0 || i <= quoteAt)) return quoteAt;
            quoteFrom = i;
            quoteAt = text.IndexOf('"', i);
            return quoteAt;
        }

        // The first line start (0, or a position right after '\n') at or after i; -1 when there is none.
        int LineStartAtOrAfter(int i)
        {
            if (i == 0 || (i <= n && text[i - 1] == '\n')) return i;
            int newline = text.IndexOf('\n', i);
            return newline < 0 ? -1 : newline + 1;
        }

        int p = 0;
        while (p >= 0 && p < n)
        {
            int q = WhitespaceEnd(p);
            if (q >= n || text[q] != '#')
            {
                // Every line start inside the run p..q reaches the same character: none of them matches.
                p = q >= n ? -1 : LineStartAtOrAfter(q + 1);
                continue;
            }
            int r = WhitespaceEnd(q + 1);
            if (r + 7 <= n && string.CompareOrdinal(text, r, "include", 0, 7) == 0)
            {
                int t = WhitespaceEnd(r + 7);
                if (t < n && text[t] == '"')
                {
                    int close = NextQuote(t + 1);
                    if (close > t + 1)
                    {
                        results.Add(text.Substring(t + 1, close - t - 1));
                        p = LineStartAtOrAfter(WhitespaceEnd(close + 1));   // the trailing \s*, then the next line start
                        continue;
                    }
                }
            }
            p = LineStartAtOrAfter(p + 1);
        }
        return results;
    }

    /// <summary>
    /// Informational only (<see cref="KfsProject.Includes"/>): the names of the assembler's own
    /// <c>.include "name"</c> lines (<c>Assembler.cs:294, 346-356</c>) — what the multiline regex
    /// <c>^[ \t]*\.include[ \t]+"([^"]+)"</c> matches, found by a scan linear in the text (the regex
    /// re-scans to the end of the text from every line whose quote is never closed).
    /// </summary>
    public static IEnumerable<string> KasmQuotedIncludes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var results = new List<string>();
        int n = text.Length;
        int quoteFrom = -1, quoteAt = -1;
        int p = 0;
        while (p < n)
        {
            int i = p;
            while (i < n && text[i] is ' ' or '\t') i++;
            int resume = p;
            if (i + 8 <= n && string.CompareOrdinal(text, i, ".include", 0, 8) == 0)
            {
                int j = i + 8, k = j;
                while (k < n && text[k] is ' ' or '\t') k++;
                if (k > j && k < n && text[k] == '"')
                {
                    int close;
                    if (quoteFrom >= 0 && k + 1 >= quoteFrom && (quoteAt < 0 || k + 1 <= quoteAt)) close = quoteAt;
                    else
                    {
                        quoteFrom = k + 1;
                        quoteAt = close = text.IndexOf('"', k + 1);
                    }
                    if (close > k + 1)
                    {
                        results.Add(text.Substring(k + 1, close - k - 1));
                        resume = close;
                    }
                }
            }
            int newline = text.IndexOf('\n', resume);
            if (newline < 0) break;
            p = newline + 1;
        }
        return results;
    }

    private sealed class NoProject(string message) : Exception(message);
}
