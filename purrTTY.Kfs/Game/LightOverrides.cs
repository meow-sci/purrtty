using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using KSA;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Game;

/// <summary>
/// Per-light private templates (plan §4.10). A <see cref="LightModule.TemplateData"/> is shared by
/// every light created from the same part template, on every vehicle, so the first appearance
/// write to a light gives that one module its own copy. Kept here: module → (the template it had,
/// our copy), in a <see cref="ConditionalWeakTable{TKey, TValue}"/> so that an entry lives exactly
/// as long as its module (staging and docking keep the module; a load or a system change does not).
/// </summary>
/// <remarks>
/// <para><b>Threads.</b> <see cref="EnsurePrivate"/>, <see cref="Reset"/> and
/// <see cref="RestoreAll"/> run on the main thread only (the pump; uninstall). The reads
/// (<see cref="IsOverridden"/>, <see cref="StockTemplate"/>) run on dispatch threads: a lock-free,
/// allocation-free table lookup plus one field read. A reader sees either the old or the new
/// template, both complete: the copy is fully built before the one reference write
/// <c>m.Template = copy</c>.</para>
/// <para><b>Other mods</b> (zippo, gatOS) replace templates the same way. When the module's
/// template is no longer ours, the next write copies whatever template is there now, and a reset
/// restores the original only while the template is still ours (plan §9 R8).</para>
/// </remarks>
public static class LightOverrides
{
    private sealed class Entry(LightModule.TemplateData original, LightModule.TemplateData mine)
    {
        public readonly LightModule.TemplateData Original = original;
        public readonly LightModule.TemplateData Mine = mine;
    }

    private static readonly ConditionalWeakTable<LightModule, Entry> Table = new();
    private static readonly ConcurrentDictionary<Type, FieldInfo[]> FieldsByType = new();

    /// <summary>
    /// The module's private template, created on first use (main thread): a field-by-field copy of
    /// the <b>current</b> template over its whole type hierarchy — so a field the game adds later is
    /// carried along — with its own <see cref="FloatReference"/> for range, intensity and both cone
    /// angles and its own <see cref="ColorRgbReference"/> (copied the same way, so the rendered
    /// colour value comes along), assigned to <c>m.Template</c> once, complete. Copies again when
    /// another mod replaced the template since.
    /// </summary>
    public static LightModule.TemplateData EnsurePrivate(LightModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        LightModule.TemplateData current = module.Template
            ?? throw new PartCommandException("the light has no template");
        if (Table.TryGetValue(module, out Entry? entry) && ReferenceEquals(current, entry.Mine)) return entry.Mine;

        LightModule.TemplateData mine = ShallowCopy(current);
        mine.Range = CopyOrNew(current.Range);
        mine.Intensity = CopyOrNew(current.Intensity);
        mine.InnerAngle = CopyOrNew(current.InnerAngle);
        mine.OuterAngle = CopyOrNew(current.OuterAngle);
        mine.ColorRgb = current.ColorRgb is { } color ? ShallowCopy(color) : NewColor();

        // The table first, the template last: a dispatch-thread read in between sees an entry
        // whose copy is not installed yet, which reads "not overridden" — true until the next line.
        Table.AddOrUpdate(module, new Entry(current, mine));
        module.Template = mine;
        return mine;
    }

    /// <summary>
    /// Restores the template the module had before its first appearance write — only while the
    /// module's template is still ours — and forgets the override (main thread). Returns true when
    /// a template was put back.
    /// </summary>
    public static bool Reset(LightModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (!Table.TryGetValue(module, out Entry? entry)) return false;
        bool restored = false;
        if (ReferenceEquals(module.Template, entry.Mine))
        {
            module.Template = entry.Original;
            restored = true;
        }
        Table.Remove(module);
        return restored;
    }

    /// <summary>True while the module renders with our private template (dispatch thread; allocation-free).</summary>
    public static bool IsOverridden(LightModule module) =>
        Table.TryGetValue(module, out Entry? entry) && ReferenceEquals(module.Template, entry.Mine);

    /// <summary>
    /// The template the module had before a program changed it — its authored appearance — or its
    /// current template when not overridden (dispatch thread; allocation-free).
    /// </summary>
    public static LightModule.TemplateData StockTemplate(LightModule module)
    {
        LightModule.TemplateData current = module.Template;
        return Table.TryGetValue(module, out Entry? entry) && ReferenceEquals(current, entry.Mine) ? entry.Original : current;
    }

    /// <summary>
    /// Restores every overridden light still alive whose template is still ours, and forgets all
    /// overrides (main thread; uninstall). Returns the number restored. Never throws.
    /// </summary>
    public static int RestoreAll()
    {
        int restored = 0;
        List<KeyValuePair<LightModule, Entry>> entries;
        try
        {
            entries = [.. (IEnumerable<KeyValuePair<LightModule, Entry>>)Table];
        }
        catch (Exception e)
        {
            KfsLog.Once("restore light templates", e);
            return 0;
        }
        foreach (var (module, entry) in entries)
        {
            try
            {
                if (ReferenceEquals(module.Template, entry.Mine))
                {
                    module.Template = entry.Original;
                    restored++;
                }
            }
            catch (Exception e)
            {
                KfsLog.Once("restore light template", e);
            }
        }
        try { Table.Clear(); }
        catch (Exception e) { KfsLog.Once("restore light templates", e); }
        return restored;
    }

    /// <summary>Overrides currently recorded (alive modules only; for status and tests).</summary>
    public static int Count
    {
        get
        {
            int n = 0;
            foreach (var _ in (IEnumerable<KeyValuePair<LightModule, Entry>>)Table) n++;
            return n;
        }
    }

    // ------------------------------------------------------------------ copying

    private static FloatReference CopyOrNew(FloatReference? source) => source is null ? new FloatReference() : ShallowCopy(source);

    private static ColorRgbReference NewColor()
    {
        var color = new ColorRgbReference();
        color.OnDataLoad(null!);   // the rendered Value from R/G/B; the mod argument is unused
        return color;
    }

    /// <summary>
    /// A shallow copy of <paramref name="source"/>: an uninitialised instance of its exact runtime
    /// type (no constructor runs, so no template registration and no field initialisers) with every
    /// instance field of every type in its hierarchy copied — gatOS's <c>LightActuator.ShallowClone</c>.
    /// </summary>
    private static T ShallowCopy<T>(T source) where T : class
    {
        Type type = source.GetType();
        object copy = RuntimeHelpers.GetUninitializedObject(type);
        foreach (FieldInfo field in FieldsByType.GetOrAdd(type, InstanceFields)) field.SetValue(copy, field.GetValue(source));
        return (T)copy;
    }

    private static FieldInfo[] InstanceFields(Type type)
    {
        var fields = new List<FieldInfo>();
        for (Type? t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            fields.AddRange(t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        }
        return [.. fields];
    }
}
