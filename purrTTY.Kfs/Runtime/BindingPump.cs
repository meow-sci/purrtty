using purrTTY.Kfs.Catalog;

namespace purrTTY.Kfs.Runtime;

/// <summary>
/// Drain-and-apply for one binding (plan §4.12) — the step the game's main-thread pump and the
/// test runtime's frame both run, so every test exercises the code the game runs.
/// </summary>
public static class BindingPump
{
    /// <summary>
    /// Starts a new run if the computer began one (<see cref="VehicleBinding.SyncRun"/>: fresh
    /// lists and rate limits), then takes the binding's queued commands and applies them in order,
    /// each in its own <c>try</c>/<c>catch</c>: the first <c>x_parts_rescan</c> of the batch
    /// rebuilds the lists and reports <c>parts rescanned: …</c>, later ones in the same batch are
    /// satisfied by that rebuild (commands hold their slots, so where in the batch the one rebuild
    /// happens cannot be observed; the generation steps at most once per drain and a program
    /// issuing hundreds of rescans a tick costs one rebuild a frame); a command whose part has left
    /// is discarded and reported; any other command goes to <see cref="IVehicleParts.Apply"/>, and
    /// a failure there is counted, logged once and reported. Reports reach the program at its next
    /// dispatch, at most one per kind per second of the run. Returns the number of commands
    /// applied (every rescan counts). Main thread only (in the game: where no solver job runs).
    /// </summary>
    public static int DrainAndApply(VehicleBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        binding.SyncRun();
        List<PartCommand> commands = binding.Take();
        int applied = 0;
        bool? rescanned = null;   // null: no rescan in this batch yet; then whether its one rebuild succeeded
        try
        {
            for (int i = 0; i < commands.Count; i++)
            {
                if (commands[i].Service == Svc.PartsRescan)
                {
                    rescanned ??= Rescan(binding, commands[i]);
                    if (rescanned.Value)
                    {
                        binding.CountApplied();
                        applied++;
                    }
                    else
                    {
                        binding.CountApplyFailure();   // the batch's one rebuild failed (reported once): not retried
                    }
                    continue;
                }
                if (ApplyOne(binding, commands[i])) applied++;
            }
        }
        finally
        {
            binding.Recycle(commands);
        }
        return applied;
    }

    private static bool Rescan(VehicleBinding binding, in PartCommand command)
    {
        IVehicleParts parts = binding.Parts;
        try
        {
            parts.Rebuild();
            if (binding.ShouldReport(BindingEvent.Rescanned)) binding.Report(PartsText.Rescanned(parts.Sets));
            return true;
        }
        catch (Exception e)
        {
            Report(binding, command, e);
            return false;
        }
    }

    private static bool ApplyOne(VehicleBinding binding, in PartCommand command)
    {
        IVehicleParts parts = binding.Parts;
        try
        {
            if (command.Slot is { } slot && !parts.IsPresent(slot))
            {
                binding.CountDiscarded();
                if (binding.ShouldReport(BindingEvent.Discarded)) binding.Report(PartsText.LeftBeforeApply(command, slot));
                return false;
            }
            parts.Apply(command);
            binding.CountApplied();
            return true;
        }
        catch (Exception e)
        {
            Failed(binding, command, e);
            return false;
        }
    }

    private static void Failed(VehicleBinding binding, in PartCommand command, Exception e)
    {
        binding.CountApplyFailure();
        Report(binding, command, e);
    }

    /// <summary>Logs a failure once per kind and reports it to the program (rate-limited); never throws.</summary>
    private static void Report(VehicleBinding binding, in PartCommand command, Exception e)
    {
        KfsLog.Once("apply", e);
        try
        {
            if (binding.ShouldReport(BindingEvent.ApplyFailure)) binding.Report(PartsText.NotApplied(command, e));
        }
        catch (Exception reportFailure)
        {
            KfsLog.Once("apply report", reportFailure);
        }
    }
}
