using System.Reflection;
using KSA.Kfs;

namespace purrTTY.Kfs.Tables;

/// <summary>
/// Whether the stock kFS editor is building in the background (its build reads the game's
/// <c>SystemHeaders</c> on another thread: <c>Workbench.cs</c> builds through <c>EditorModel</c>,
/// whose <c>Building</c> is true while a background build is pending). Reads the private
/// <c>KfsIntegration._workbench</c> field, because the public <c>KfsIntegration.Workbench</c> getter
/// creates the workbench — which must never happen at unload. The field, the workbench's
/// <c>Editor</c> and the editor's <c>Building</c> are all looked up by name, once.
/// </summary>
/// <remarks>
/// <para>When a member is not found (a game update renamed it) or reading fails, the probe answers
/// <b>building</b>: the uninstall then leaves <c>&lt;purrtty.h&gt;</c> registered, which the next install
/// takes over and the status reports. The other answer would be worse: a wrong "not building" lets the
/// uninstall change the game's header <c>Dictionary</c>/<c>HashSet</c> while a background compile may be
/// reading them, which can corrupt them or fault that compile.</para>
/// <para>A failed lookup is logged once (naming the missing member) and is kept in <see cref="Problem"/>
/// for the extension's status (<see cref="KfsExtensionStatus.EditorProbe"/>).</para>
/// </remarks>
internal static class StockEditorProbe
{
    /// <summary>The answer while the stock editor builds.</summary>
    public const string BuildingReason = "the stock kFS editor is building in the background";

    private static readonly Lazy<EditorBuildProbe> Real = new(
        () => EditorBuildProbe.Resolve(typeof(KfsIntegration), "_workbench", KfsLog.Info),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Why the real probe cannot read the editor's build state (the missing member), or null when it can.</summary>
    public static string? Problem => Real.Value.Problem;

    /// <summary><see cref="BuildingReason"/>, the reason the state cannot be read, or null (no workbench, or not building). Never throws.</summary>
    public static string? Building()
    {
        try { return Real.Value.Read(); }
        catch (Exception e)
        {
            KfsLog.Once("read the stock editor's build state", e);
            return EditorBuildProbe.UnreadableReason($"{e.GetType().Name}");
        }
    }
}

/// <summary>
/// The lookup behind <see cref="StockEditorProbe"/>: a static field holding the workbench (or null),
/// its <c>Editor</c> and the editor's <c>Building</c>, each a property or field, resolved by name.
/// Separate from the game types so tests can point it at fakes.
/// </summary>
internal sealed class EditorBuildProbe
{
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private readonly FieldInfo? _workbench;
    private readonly Func<object, object?>? _editor;
    private readonly Func<object, object?>? _building;

    private EditorBuildProbe(FieldInfo? workbench, Func<object, object?>? editor, Func<object, object?>? building, string? problem)
    {
        _workbench = workbench;
        _editor = editor;
        _building = building;
        Problem = problem;
    }

    /// <summary>The member that was not found, in words, or null when the lookup is complete.</summary>
    public string? Problem { get; }

    /// <summary>The conservative answer when the state cannot be read.</summary>
    public static string UnreadableReason(string why) =>
        $"the stock kFS editor's build state cannot be read ({why}), so it may be building";

    /// <summary>
    /// Looks up <paramref name="owner"/>'s static field <paramref name="fieldName"/>, then <c>Editor</c> on
    /// the field's type and <c>Building</c> (a bool) on the editor's type. A missing member is logged
    /// once through <paramref name="log"/>. Never throws.
    /// </summary>
    public static EditorBuildProbe Resolve(Type owner, string fieldName, Action<string>? log)
    {
        string? problem;
        FieldInfo? field = null;
        Func<object, object?>? editor = null, building = null;
        try
        {
            field = owner.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (field is null) problem = $"{owner.FullName}.{fieldName} not found";
            else if (!Member(field.FieldType, "Editor", out editor, out Type? editorType)) problem = $"{field.FieldType.FullName}.Editor not found";
            else if (!Member(editorType!, "Building", out building, out Type? buildingType) || buildingType != typeof(bool))
                problem = $"{editorType!.FullName}.Building (bool) not found";
            else problem = null;
        }
        catch (Exception e)
        {
            problem = $"lookup failed: {e.GetType().Name}";
        }
        if (problem is not null)
        {
            try
            {
                log?.Invoke($"{KfsExtensionStatus.LinePrefix}cannot read whether the stock kFS editor is building: {problem}. " +
                            "<purrtty.h> will stay registered at every uninstall (the safe answer); the next install takes it over");
            }
            catch { /* never throw from logging */ }
            return new EditorBuildProbe(null, null, null, problem);
        }
        return new EditorBuildProbe(field, editor, building, null);
    }

    /// <summary>
    /// <see cref="StockEditorProbe.BuildingReason"/> while building; the conservative
    /// <see cref="UnreadableReason"/> when the lookup failed; null with no workbench or not building.
    /// Throws what the reads throw.
    /// </summary>
    public string? Read()
    {
        if (Problem is not null || _workbench is null) return UnreadableReason(Problem ?? "no lookup");
        object? workbench = _workbench.GetValue(null);
        if (workbench is null) return null;
        object? editor = _editor!(workbench);
        if (editor is null) return null;
        return _building!(editor) is true ? StockEditorProbe.BuildingReason : null;
    }

    private static bool Member(Type type, string name, out Func<object, object?>? read, out Type? memberType)
    {
        if (type.GetProperty(name, Instance) is { CanRead: true } property && property.GetIndexParameters().Length == 0)
        {
            read = property.GetValue;
            memberType = property.PropertyType;
            return true;
        }
        if (type.GetField(name, Instance) is { } field)
        {
            read = field.GetValue;
            memberType = field.FieldType;
            return true;
        }
        read = null;
        memberType = null;
        return false;
    }
}
