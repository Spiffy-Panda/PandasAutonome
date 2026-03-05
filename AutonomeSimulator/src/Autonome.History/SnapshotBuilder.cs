using Autonome.Core.Model;
using Autonome.Core.World;

namespace Autonome.History;

/// <summary>
/// Builds periodic full-state snapshots of the simulation.
/// </summary>
public class SnapshotBuilder
{
    public WorldSnapshotFull BuildSnapshot(WorldState world, IReadOnlyList<AutonomeProfile> profiles)
    {
        var entities = new Dictionary<string, EntitySnapshot>();

        foreach (var (id, entity) in world.Entities.All())
        {
            var properties = entity.Properties.ToDictionary(p => p.Key, p => p.Value.Value);
            var modifiers = world.Modifiers.GetModifiers(id)
                .Select(m => new ModifierSnapshot(m.Id, m.Type, m.Source, m.Intensity, m.Duration))
                .ToList();

            entities[id] = new EntitySnapshot(properties, modifiers, entity.BusyUntilTick > world.Clock.Tick);
        }

        return new WorldSnapshotFull(
            world.Clock.Tick,
            world.Clock.FormatGameTime(),
            entities
        );
    }
}

public sealed record WorldSnapshotFull(
    int Tick,
    string GameTime,
    Dictionary<string, EntitySnapshot> Entities
);

public sealed record EntitySnapshot(
    Dictionary<string, float> Properties,
    List<ModifierSnapshot> ActiveModifiers,
    bool IsBusy
);

public sealed record ModifierSnapshot(
    string Id,
    string Type,
    string Source,
    float Intensity,
    float? RemainingDuration
);
