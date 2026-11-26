using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Testing;

/// <summary>
/// Fault injection: wraps a back end and throws from the members switched on, to prove that a
/// failing back end never faults a computer. Every member of <see cref="IVehicleParts"/> can be
/// made to throw independently; everything not switched on is passed through.
/// </summary>
public sealed class FaultyVehicleParts(IVehicleParts inner) : IVehicleParts
{
    /// <summary>The exception every injected fault throws.</summary>
    public sealed class InjectedFault(string what) : Exception("injected fault: " + what);

    /// <summary>Throw from the six getters (<c>LightGetI</c> … <c>ActuatorGetF</c>).</summary>
    public bool ThrowOnRead { get; set; }

    /// <summary>Throw from <see cref="Sets"/>.</summary>
    public bool ThrowOnSets { get; set; }

    /// <summary>Throw from <see cref="Dirty"/>.</summary>
    public bool ThrowOnDirty { get; set; }

    /// <summary>Throw from <see cref="IsPresent"/>.</summary>
    public bool ThrowOnIsPresent { get; set; }

    /// <summary>Throw from <see cref="HasSwitch"/>.</summary>
    public bool ThrowOnHasSwitch { get; set; }

    /// <summary>Throw from <see cref="Apply"/>.</summary>
    public bool ThrowOnApply { get; set; }

    /// <summary>Throw from <see cref="Rebuild"/>.</summary>
    public bool ThrowOnRebuild { get; set; }

    /// <summary>The wrapped back end.</summary>
    public IVehicleParts Inner { get; } = inner;

    /// <inheritdoc/>
    public PartSets Sets => ThrowOnSets ? throw new InjectedFault(nameof(Sets)) : Inner.Sets;

    /// <inheritdoc/>
    public bool Dirty => ThrowOnDirty ? throw new InjectedFault(nameof(Dirty)) : Inner.Dirty;

    /// <inheritdoc/>
    public void Rebuild()
    {
        if (ThrowOnRebuild) throw new InjectedFault(nameof(Rebuild));
        Inner.Rebuild();
    }

    /// <inheritdoc/>
    public bool IsPresent(PartSlot slot) => ThrowOnIsPresent ? throw new InjectedFault(nameof(IsPresent)) : Inner.IsPresent(slot);

    /// <inheritdoc/>
    public bool HasSwitch(LightSlot slot) => ThrowOnHasSwitch ? throw new InjectedFault(nameof(HasSwitch)) : Inner.HasSwitch(slot);

    /// <inheritdoc/>
    public uint LightGetI(int index, LightProp prop) => ThrowOnRead ? throw new InjectedFault("read") : Inner.LightGetI(index, prop);

    /// <inheritdoc/>
    public float LightGetF(int index, LightProp prop) => ThrowOnRead ? throw new InjectedFault("read") : Inner.LightGetF(index, prop);

    /// <inheritdoc/>
    public uint DeployGetI(int index, DeployProp prop) => ThrowOnRead ? throw new InjectedFault("read") : Inner.DeployGetI(index, prop);

    /// <inheritdoc/>
    public float DeployGetF(int index, DeployProp prop) => ThrowOnRead ? throw new InjectedFault("read") : Inner.DeployGetF(index, prop);

    /// <inheritdoc/>
    public uint ActuatorGetI(int index, ActuatorProp prop) => ThrowOnRead ? throw new InjectedFault("read") : Inner.ActuatorGetI(index, prop);

    /// <inheritdoc/>
    public float ActuatorGetF(int index, ActuatorProp prop) => ThrowOnRead ? throw new InjectedFault("read") : Inner.ActuatorGetF(index, prop);

    /// <inheritdoc/>
    public void Apply(in PartCommand command)
    {
        if (ThrowOnApply) throw new InjectedFault(nameof(Apply));
        Inner.Apply(command);
    }
}
