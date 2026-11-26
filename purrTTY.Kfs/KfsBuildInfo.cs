namespace purrTTY.Kfs;

/// <summary>
/// Fixed facts about purrTTY's kFS extension that every other part of it shares.
/// </summary>
public static class KfsBuildInfo
{
    /// <summary>First service id of the block reserved for purrTTY's kFS services.</summary>
    public const ushort ServiceIdFirst = 0x5000;

    /// <summary>Last service id (inclusive) of the block reserved for purrTTY's kFS services.</summary>
    public const ushort ServiceIdLast = 0x50FF;

    /// <summary>
    /// True when <paramref name="id"/> lies in purrTTY's service id block. The extension claims
    /// every id in this block and no id outside it.
    /// </summary>
    public static bool Owns(ushort id) => id >= ServiceIdFirst && id <= ServiceIdLast;
}
