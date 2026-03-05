using Autonome.Core.Model;
using Autonome.Core.Runtime;
using Autonome.Core.World;

namespace Autonome.Core.Simulation;

/// <summary>
/// Main simulation tick loop.
/// </summary>
public class SimulationRunner
{
    public SimulationResult Run(
        WorldState world,
        IReadOnlyList<AutonomeProfile> profiles,
        IReadOnlyList<ActionDefinition> actions,
        SimulationConfig config)
    {
        var result = new SimulationResult { MinutesPerTick = world.Clock.MinutesPerTick };

        while (world.Clock.Tick < config.TotalTicks)
        {
            world.Clock.Advance();

            // 0. EXTERNAL EVENTS (ship arrivals, supply injections)
            if (config.Events != null)
                ProcessEvents(world, config.Events);

            // 1. PROPERTY TICK (all entities + locations — decay, aggregation, passives)
            PropertyTicker.TickAll(world, 1f);

            // 2. MODIFIER LIFECYCLE
            world.Modifiers.Tick(1f);

            // 3. CLEAR BUSY FLAGS
            world.Entities.TickBusy(world.Clock.Tick);

            // 4. EVALUATE + ACT (only Autonomes due for evaluation this tick)
            foreach (var (profile, state) in EvaluationScheduler.GetDue(world, profiles))
            {
                var candidates = UtilityScorer.ScoreAllCandidates(profile, state, actions, world);
                if (candidates.Count == 0) continue;

                var chosen = candidates[0];
                var execResult = ActionExecutor.Execute(profile.Id, chosen.Action, world);

                // Record to result (capture location after action execution, which may have moved the entity)
                result.ActionEvents.Add(new ActionEvent(
                    world.Clock.Tick,
                    world.Clock.FormatGameTime(),
                    profile.Id,
                    profile.Embodied,
                    chosen.Action.Id,
                    chosen.Score,
                    candidates.Take(5).Select(c => new CandidateScore(c.Action.Id, c.Score)).ToList(),
                    state.Properties.ToDictionary(p => p.Key, p => p.Value.Value),
                    world.Locations.GetLocation(profile.Id)
                ));
            }

            // 5. SNAPSHOT (periodic)
            if (config.SnapshotInterval > 0 && world.Clock.Tick % config.SnapshotInterval == 0)
            {
                result.Snapshots.Add(TakeSnapshot(world, profiles));
            }

            // Progress
            if (config.OnProgress != null && world.Clock.Tick % 100 == 0)
            {
                config.OnProgress(world.Clock.Tick, config.TotalTicks);
            }
        }

        return result;
    }

    private static void ProcessEvents(WorldState world, List<ExternalEvent> events)
    {
        int tick = world.Clock.Tick;
        foreach (var evt in events)
        {
            if (tick < evt.TriggerTick) continue;

            bool shouldFire = tick == evt.TriggerTick ||
                (evt.RepeatInterval.HasValue &&
                 evt.RepeatInterval.Value > 0 &&
                 (tick - evt.TriggerTick) % evt.RepeatInterval.Value == 0);

            if (!shouldFire) continue;

            switch (evt.Type)
            {
                case "modify_location_property":
                    if (evt.Location != null && evt.Property != null && evt.Amount.HasValue)
                    {
                        var prop = world.LocationStates.GetProperty(evt.Location, evt.Property);
                        if (prop != null)
                        {
                            prop.Value += evt.Amount.Value;
                            prop.Clamp();
                        }
                    }
                    break;
            }
        }
    }

    private static WorldSnapshot TakeSnapshot(WorldState world, IReadOnlyList<AutonomeProfile> profiles)
    {
        var entities = new Dictionary<string, Dictionary<string, float>>();
        foreach (var (id, entity) in world.Entities.All())
        {
            entities[id] = entity.Properties.ToDictionary(p => p.Key, p => p.Value.Value);
        }

        var locationProperties = new Dictionary<string, Dictionary<string, float>>();
        foreach (var (locId, props) in world.LocationStates.All())
        {
            locationProperties[locId] = props.ToDictionary(p => p.Key, p => p.Value.Value);
        }

        return new WorldSnapshot(world.Clock.Tick, world.Clock.FormatGameTime(), entities, locationProperties);
    }
}

public sealed record SimulationConfig(
    int TotalTicks,
    int SnapshotInterval = 100,
    Action<int, int>? OnProgress = null,
    List<ExternalEvent>? Events = null
);

public sealed class SimulationResult
{
    public float MinutesPerTick { get; set; }
    public List<ActionEvent> ActionEvents { get; } = [];
    public List<WorldSnapshot> Snapshots { get; } = [];
}

public sealed record ActionEvent(
    int Tick,
    string GameTime,
    string AutonomeId,
    bool Embodied,
    string ActionId,
    float Score,
    List<CandidateScore> TopCandidates,
    Dictionary<string, float> PropertySnapshot,
    string? Location = null
);

public sealed record CandidateScore(string ActionId, float Score);

public sealed record WorldSnapshot(
    int Tick,
    string GameTime,
    Dictionary<string, Dictionary<string, float>> EntityProperties,
    Dictionary<string, Dictionary<string, float>>? LocationProperties = null
);
