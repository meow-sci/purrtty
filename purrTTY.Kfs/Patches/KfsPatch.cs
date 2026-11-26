using System.Reflection;
using HarmonyLib;

namespace purrTTY.Kfs.Patches;

/// <summary>
/// One entry of the installer's patch registry (<see cref="KfsExtension.Patches"/>): a name, whether
/// the install is all-or-nothing on it, how to find its target, and how to apply and remove it.
/// Resolving is separate from applying so the installer can resolve every required target before
/// patching anything (plan §4.5 step 3).
/// </summary>
/// <param name="Name">Short, stable name (the patch class without <c>Patch</c>): used in the status and in <see cref="KfsExtensionOptions.DisabledOptionalPatches"/>.</param>
/// <param name="Target">The target in words, for messages (<c>VehicleComputer.ApplyRequest</c>).</param>
/// <param name="Required">True when a missing target or a failed apply disables the whole extension.</param>
/// <param name="ResolveTarget">The target method, or null when this game build has none.</param>
/// <param name="Apply">Applies the patch to the resolved target; may throw.</param>
/// <param name="Remove">Removes only this patch from the target; may throw.</param>
public sealed record KfsPatch(
    string Name,
    string Target,
    bool Required,
    Func<MethodBase?> ResolveTarget,
    Action<Harmony, MethodBase> Apply,
    Action<Harmony, MethodBase> Remove);
