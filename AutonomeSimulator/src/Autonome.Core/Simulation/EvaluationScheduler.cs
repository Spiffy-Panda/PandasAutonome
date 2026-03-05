using Autonome.Core.Model;
using Autonome.Core.World;

namespace Autonome.Core.Simulation;

/// <summary>
/// Determines which Autonomes are due for action evaluation on a given tick.
/// Respects per-Autonome evaluation intervals.
/// </summary>
public static class EvaluationScheduler
{
    public static IEnumerable<(AutonomeProfile Profile, EntityState State)> GetDue(
        WorldState world,
        IReadOnlyList<AutonomeProfile> profiles)
    {
        int tick = world.Clock.Tick;

        foreach (var profile in profiles)
        {
            if (profile.EvaluationInterval == null) continue; // World objects never evaluate

            if (tick % profile.EvaluationInterval.Value != 0) continue;

            var state = world.Entities.Get(profile.Id);
            if (state == null) continue;

            if (state.BusyUntilTick > tick) continue;

            yield return (profile, state);
        }
    }
}
