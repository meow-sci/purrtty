using System.Text.RegularExpressions;

namespace purrTTY.Kfs.Catalog;

/// <summary>
/// An enumeration of the catalogue: a positional list of value names. In <c>enums.json</c> a
/// value's number is its list position, so gaps are filled with <c>reservedN</c> entries (N = the
/// position) that get no <c>#define</c>. The list is part of the image format: a new value
/// replaces the next filler; nothing is ever inserted or reordered.
/// </summary>
/// <param name="Key">The <c>enums.json</c> key (<c>x_…</c>), or null for a header-only constant group.</param>
/// <param name="CPrefix">The prefix of the header constants (<c>X_LIGHT_</c>); a constant is the prefix + the upper-cased value name.</param>
/// <param name="Values">Value names by position, fillers included.</param>
/// <param name="Doc">One line of documentation for the header and the reference.</param>
/// <param name="FirstFloat">
/// For a property enumeration read by an integer and a float getter: the first float value
/// (integer properties lie below it). Null for any other enumeration.
/// </param>
public sealed record EnumDef(string? Key, string CPrefix, IReadOnlyList<string> Values, string Doc, int? FirstFloat = null)
{
    /// <summary>The name prefix of filler entries.</summary>
    public const string FillerPrefix = "reserved";

    /// <summary>True for a filler entry.</summary>
    public static bool IsFiller(string value) => value.StartsWith(FillerPrefix, StringComparison.Ordinal);

    /// <summary>The filler name for <paramref name="position"/>.</summary>
    public static string Filler(int position) => FillerPrefix + position;

    /// <summary>True when the values go into <c>enums.json</c>; false for a header-only group.</summary>
    public bool InTable => Key is not null;

    /// <summary>Every real (non-filler) value as its header constant name and number.</summary>
    public IEnumerable<(string Name, int Value)> Constants =>
        Values.Select((v, i) => (v, i)).Where(x => !IsFiller(x.v)).Select(x => (CPrefix + x.v.ToUpperInvariant(), x.i));

    /// <summary>The numbers of every real (non-filler) value.</summary>
    public IEnumerable<int> RealValues => Values.Select((v, i) => (v, i)).Where(x => !IsFiller(x.v)).Select(x => x.i);

    /// <summary>
    /// The definition of <typeparamref name="TEnum"/>: each member's snake_cased name at the
    /// position of its numeric value, fillers in the gaps.
    /// </summary>
    public static EnumDef From<TEnum>(string? key, string cPrefix, string doc, int? firstFloat = null)
        where TEnum : struct, Enum
    {
        var byValue = Enum.GetValues<TEnum>().ToDictionary(v => Convert.ToInt32(v), v => Snake(v.ToString()));
        int count = byValue.Count == 0 ? 0 : byValue.Keys.Max() + 1;
        var values = Enumerable.Range(0, count).Select(i => byValue.TryGetValue(i, out var n) ? n : Filler(i)).ToArray();
        return new EnumDef(key, cPrefix, values, doc, firstFloat);
    }

    /// <summary><c>HasSwitch</c> → <c>has_switch</c>.</summary>
    public static string Snake(string pascal) => Regex.Replace(pascal, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
}
