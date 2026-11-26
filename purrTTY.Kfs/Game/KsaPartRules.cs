using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Game;

/// <summary>
/// The decisions <see cref="KsaVehicleParts"/> makes about live game objects, as pure functions
/// over plain values — no game types — so they are unit-tested outside the game. Each one names
/// the game code it mirrors (KSA build 2026.11.25.5949).
/// </summary>
public static class KsaPartRules
{
    /// <summary>
    /// A light's <i>effective switch</i> (plan §4.10): the switch a program may read and write for
    /// it, or null when it has none. Null for an interior fill light (its template has
    /// <c>DisableInIva</c>: <c>LightModule.IsActive</c> is then true whatever the switch says), for
    /// a part without a switch (<c>Part.LightSwitch</c> null), and for a switch whose part is not on
    /// this vehicle's part tree (the game ignores such a switch, <c>Part.IsLightSwitchedOff</c>, and
    /// a program must never write another vehicle's switch). Trees are compared by reference.
    /// </summary>
    /// <param name="disableInIva">The light template's <c>DisableInIva</c>.</param>
    /// <param name="partSwitch">The light's full part's <c>LightSwitch</c>.</param>
    /// <param name="switchTree">The part tree the switch's part is on (<c>LightSwitch.Parent.Tree</c>).</param>
    /// <param name="vehicleTree">This vehicle's part tree (<c>Vehicle.Parts</c>).</param>
    public static TSwitch? EffectiveSwitch<TSwitch>(bool disableInIva, TSwitch? partSwitch, object? switchTree, object? vehicleTree)
        where TSwitch : class
    {
        if (disableInIva || partSwitch is null || vehicleTree is null) return null;
        return ReferenceEquals(switchTree, vehicleTree) ? partSwitch : null;
    }

    /// <summary>The <c>X_*_LINKS</c> bit flags of an animation (<see cref="AnimSlot.LinkSolarPanel"/> and its siblings).</summary>
    /// <param name="drivesSolarPanel">A solar panel's power is gated by this animation (<c>SolarPanel.KeyframeAnimationModule</c> is it).</param>
    /// <param name="parachuteBayDoor">A parachute's state machine also writes this animation's goal (<c>Parachute.SetBayDoorsOpen</c>).</param>
    /// <param name="carriesLight">The animated part carries a light emitter (an aimable light).</param>
    public static uint Links(bool drivesSolarPanel, bool parachuteBayDoor, bool carriesLight) =>
        (drivesSolarPanel ? AnimSlot.LinkSolarPanel : 0u)
        | (parachuteBayDoor ? AnimSlot.LinkParachute : 0u)
        | (carriesLight ? AnimSlot.LinkLight : 0u);

    /// <summary>
    /// True when an animation of this authored length can be addressed: finite and above zero.
    /// The game's own filter is <c>Shared.Duration &gt; 0</c> (an asset that failed to load or has
    /// no keyframes reads 0 and would divide by zero in fraction maths); an infinite length is
    /// refused too, since no goal could ever be reached.
    /// </summary>
    public static bool HasTravel(float durationS) => float.IsFinite(durationS) && durationS > 0f;

    /// <summary>The list an animation belongs to: the stock part window shows Deploy / Retract buttons for <c>ShowDeployRetract</c>, an Actuate slider otherwise.</summary>
    public static PartKind AnimationKind(bool showDeployRetract) => showDeployRetract ? PartKind.Deployable : PartKind.Actuator;

    /// <summary>
    /// <c>KeyframeAnimationModule.DeriveDeploymentState(timeGoal, timeCurrent)</c>, numbered as the
    /// game numbers it (Deployed 0, Retracted 1, Deploying 2, Retracting 3): goal below position
    /// retracting, above deploying, at it deployed when the position is above zero and retracted
    /// otherwise. Comparisons with NaN fall through exactly as in the game.
    /// </summary>
    public static int DeriveGameDeploymentState(float timeGoal, float timeCurrent)
    {
        if (timeGoal < timeCurrent) return GameRetracting;
        if (timeGoal > timeCurrent) return GameDeploying;
        if (!(timeCurrent <= 0f)) return GameDeployed;
        return GameRetracted;
    }

    /// <summary>
    /// The deployment state when the committed state of an animation cannot be read (its tree has no
    /// state list for animations, or the module's state index is out of range): the state of an
    /// animation resting at its goal — gatOS's fallback.
    /// </summary>
    public static int FallbackGameDeploymentState(float timeGoal) => DeriveGameDeploymentState(timeGoal, timeGoal);

    /// <summary>
    /// A light's type as programs read it from the game's <c>LightModule.TemplateData.LightType</c>
    /// value (Spot 0, Point 1). A value this build does not define reads <see cref="LightType.Point"/>:
    /// the conservative choice, because no cone is ever written to a point light.
    /// </summary>
    public static LightType LightTypeOf(int gameLightType) => gameLightType == 0 ? LightType.Spot : LightType.Point;

    /// <summary>
    /// The stable key of a part module as the console and <c>x_parts_describe</c> show it:
    /// <c>&lt;part template id&gt;/&lt;module key&gt;</c>, or whichever of the two is not empty.
    /// The module key tells several emitters (or animations) of one part kind apart.
    /// </summary>
    public static string Key(string? partTemplateId, string? moduleKey)
    {
        bool part = !string.IsNullOrEmpty(partTemplateId), module = !string.IsNullOrEmpty(moduleKey);
        return part && module ? partTemplateId + "/" + moduleKey
            : part ? partTemplateId!
            : module ? moduleKey!
            : string.Empty;
    }

    /// <summary>
    /// True when <paramref name="item"/> is the first element of <paramref name="items"/> (by
    /// reference). The game binds a part's solar panel and parachute to the <b>first</b>
    /// animation of the full part's subtree (<c>SolarPanel.OnFullPartCreated</c>,
    /// <c>Parachute.OnFullPartCreated</c>).
    /// </summary>
    public static bool IsFirst<T>(ReadOnlySpan<T> items, T item) where T : class =>
        items.Length > 0 && ReferenceEquals(items[0], item);

    /// <summary>
    /// The <c>Dirty</c> rule of the game back end: the live part counts differ from the frozen
    /// lists' lengths, or (equal counts can hide a swap, such as one light undocked and another
    /// docked) at least one frozen slot is no longer on the vehicle.
    /// </summary>
    public static bool Dirty(int liveLights, int liveDeployables, int liveActuators,
        int frozenLights, int frozenDeployables, int frozenActuators, bool anyFrozenDeparted) =>
        liveLights != frozenLights || liveDeployables != frozenDeployables || liveActuators != frozenActuators || anyFrozenDeparted;

    // KeyframeAnimationModule.DeploymentState values (KSA/KeyframeAnimationModule.cs:51-58).
    internal const int GameDeployed = 0;
    internal const int GameRetracted = 1;
    internal const int GameDeploying = 2;
    internal const int GameRetracting = 3;
}
