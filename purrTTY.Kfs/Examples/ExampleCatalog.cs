using System.Reflection;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Commands;

// Not purrTTY.Kfs.Examples: a namespace of that name would shadow the game's
// KSA.Kfs.Host.Workbench.Examples class in code under purrTTY.Kfs (the tests use it).
namespace purrTTY.Kfs.ExamplePrograms;

/// <summary>One example program: a workspace folder, which the game builds as one project.</summary>
/// <param name="Folder">The workspace folder (<c>x_blink</c>), one level deep.</param>
/// <param name="MainFile">The file to open and load (<c>x_blink/blink.c</c>).</param>
/// <param name="Description">One line: what the example shows.</param>
/// <param name="Files">Workspace-relative path → text, every file of the folder.</param>
public sealed record KfsExample(string Folder, string MainFile, string Description, IReadOnlyDictionary<string, string> Files)
{
    /// <summary>True for a kC project (it has a <c>.c</c> file); a pure kASM project otherwise.</summary>
    public bool IsKc => Files.Keys.Any(f => f.EndsWith(".c", StringComparison.OrdinalIgnoreCase));

    /// <summary>The files keyed by their name inside the folder, as the game's <c>BuildProject.Files</c> holds them.</summary>
    public IReadOnlyDictionary<string, string> ProjectFiles =>
        Files.ToDictionary(f => f.Key[(Folder.Length + 1)..], f => f.Value, StringComparer.Ordinal);
}

/// <summary>What <see cref="ExampleCatalog.WriteTo"/> did.</summary>
/// <param name="Written">Files written.</param>
/// <param name="Skipped">Files that existed and were left alone.</param>
/// <param name="Failed">Files that could not be written, with the reason.</param>
public sealed record ExampleWriteResult(IReadOnlyList<string> Written, IReadOnlyList<string> Skipped, IReadOnlyList<(string Path, string Error)> Failed)
{
    /// <summary>The report <c>purr examples</c> prints.</summary>
    public IReadOnlyList<string> Lines(string workspaceFolder)
    {
        var lines = new List<string>
        {
            $"purr examples: {Written.Count} written, {Skipped.Count} kept, {Failed.Count} failed, in {workspaceFolder}",
        };
        foreach (string path in Written) lines.Add($"  wrote   {path}");
        foreach (string path in Skipped) lines.Add($"  kept    {path} (exists; purr examples --force overwrites it)");
        foreach (var (path, error) in Failed) lines.Add($"  FAILED  {path}: {error}");
        lines.Add("each folder is one program:");
        foreach (KfsExample example in ExampleCatalog.All) lines.Add($"  {example.Folder,-14} {example.Description}");
        lines.Add("load one onto the controlled vehicle: kfs on; kfs load x_blink/blink.c --vehicle; kfs start");
        return lines;
    }
}

/// <summary>
/// purrTTY's example kFS programs (plan §4.14), each its own workspace folder — a folder is one
/// project, so two programs in one folder would be compiled together and collide. The sources are
/// embedded in this assembly (<c>Examples/&lt;folder&gt;/…</c>); <c>x_blink_kasm/purrtty.kinc</c>
/// is generated from the catalogue, never stored. Line ends are <c>\r\n</c>, as the game's own
/// examples and headers.
/// </summary>
public static class ExampleCatalog
{
    private const string ResourcePrefix = "purrTTY.Kfs.Examples/";

    /// <summary>Every example, in the order <c>purr examples</c> lists them.</summary>
    public static IReadOnlyList<KfsExample> All { get; } = Build();

    /// <summary>Every file of every example: workspace-relative path → text.</summary>
    public static IReadOnlyDictionary<string, string> Files { get; } =
        All.SelectMany(e => e.Files).ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);

    /// <summary>The example in <paramref name="folder"/>, or null.</summary>
    public static KfsExample? Find(string folder) => All.FirstOrDefault(e => e.Folder == folder);

    /// <summary>
    /// Writes every file into the workspace through <paramref name="host"/>; a file that exists is
    /// kept unless <paramref name="overwrite"/>. A file that cannot be written is reported and the
    /// others are still written. Never throws for a write failure.
    /// </summary>
    public static ExampleWriteResult WriteTo(IKfsConsoleHost host, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(host);
        var written = new List<string>();
        var skipped = new List<string>();
        var failed = new List<(string, string)>();
        foreach (var (path, text) in All.SelectMany(e => e.Files.OrderBy(f => f.Key, StringComparer.Ordinal)))
        {
            try
            {
                if (!overwrite && host.WorkspaceFileExists(path))
                {
                    skipped.Add(path);
                    continue;
                }
                host.WriteWorkspaceFile(path, text);
                written.Add(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                failed.Add((path, e.Message));
            }
        }
        return new ExampleWriteResult(written, skipped, failed);
    }

    private static List<KfsExample> Build()
    {
        string kinc = CatalogEmitter.Kinc(XCatalog.Catalog);
        return
        [
            Example("x_blink", "blink.c", "blink every switchable light once a second (count, PRESENT, HAS_SWITCH, switch groups)"),
            Example("x_light_show", "light_show.c", "colour wheel, breathing cone and intensity ramp on the spot lights; poke stop 1 resets"),
            Example("x_deploy", "deploy_when_safe.c", "deploy the solar arrays above the atmosphere, retract them below (MSG_FLIGHT_DATA, LINKS)"),
            Example("x_actuator", "actuator_sweep.c", "a triangle wave on actuator 0, with position read-back at each turn"),
            Example("x_parts", "parts_list.c", "the index tables in the program log; rescan when the vehicle changes (DIRTY, GENERATION)"),
            Example("x_party_time", "party_time.c", "the light show in every colour at once, with the light parts' actuators moving now and then; poke stop 1 resets"),
            Example("x_blink_kasm", "blink.kasm", "x_blink in kASM, with the generated purrtty.kinc", ("purrtty.kinc", kinc)),
        ];
    }

    private static KfsExample Example(string folder, string main, string description, params (string Name, string Text)[] generated)
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string prefix = ResourcePrefix + folder + "/";
        Assembly assembly = typeof(ExampleCatalog).Assembly;
        foreach (string resource in assembly.GetManifestResourceNames().Where(r => r.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            files[folder + "/" + resource[prefix.Length..]] = Crlf(reader.ReadToEnd());
        }
        foreach (var (name, text) in generated) files[folder + "/" + name] = Crlf(text);
        string mainPath = folder + "/" + main;
        if (!files.ContainsKey(mainPath)) throw new InvalidOperationException($"example {mainPath} is not embedded in {assembly.GetName().Name}");
        return new KfsExample(folder, mainPath, description, new Dictionary<string, string>(files, StringComparer.Ordinal));
    }

    /// <summary>Every line end as <c>\r\n</c> (a checkout may have either).</summary>
    private static string Crlf(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
}
