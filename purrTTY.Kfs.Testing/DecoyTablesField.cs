using System.Reflection;
using KSA.Kfs.Vm.Tables;

namespace purrTTY.Kfs.Testing;

/// <summary>
/// A static <see cref="GameTables"/> field that is not the one behind <c>KfsIntegration.Tables</c>:
/// handed to the installer as "the" tables field, it makes the publication's read-back fail, which
/// is the publisher's <c>NotVisible</c> outcome (a game build whose accessor stopped reading
/// <c>_tables</c>). Testing only.
/// </summary>
public static class DecoyTablesField
{
    /// <summary>The decoy's value; null unless a publication left something (it must not).</summary>
    public static GameTables? Value;

    /// <summary>The decoy field.</summary>
    public static FieldInfo Find() => typeof(DecoyTablesField).GetField(nameof(Value), BindingFlags.Public | BindingFlags.Static)!;
}
