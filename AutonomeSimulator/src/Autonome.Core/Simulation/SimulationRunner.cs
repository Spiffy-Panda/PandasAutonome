using Autonome.Core.Model;
using Autonome.Core.Runtime;
using Autonome.Core.World;

namespace Autonome.Core.Simulation;

/// <summary>
/// Main simulation tick loop. Supports both batch mode (Run) and
/// interactive tick-by-tick mode (TickOnce) for external controller API.
/// </summary>
public class SimulationRunner
{
    /// <summary>
    /// Advance the simulation by one tick. Returns events from that tick.
    /// External entities check ExternalActionQueue instead of UtilityScorer.
    /// </summary>
    public TickResult TickOnce(
        WorldState world,
        IReadOnlyList<AutonomeProfile> profiles,
        IReadOnlyList<ActionDefinition> actions,
        SimulationConfig config,
        ExternalActionQueue? externalActions = null)
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

        var tickResult = new TickResult(world.Clock.Tick, world.Clock.FormatGameTime());

        // 4. EVALUATE + ACT (only Autonomes due for evaluation this tick)
        foreach (var (profile, state) in EvaluationScheduler.GetDue(world, profiles))
        {
            ActionDefinition? chosenAction = null;
            float chosenScore = 0f;
            List<CandidateScore>? topCandidates = null;

            if (externalActions != null && externalActions.IsExternalEntity(profile.Id))
            {
                // External entity: use submitted action or idle
                if (externalActions.TryDequeue(profile.Id, out var extAction) && extAction != null)
                {
                    chosenAction = extAction;
                    chosenScore = -1f; // Sentinel: externally chosen
                    topCandidates = [new CandidateScore(extAction.Id, -1f)];
                }
                else
                {
                    continue; // No action submitted — idle
                }
            }
            else
            {
                // AI-controlled: run utility scorer
                var candidates = UtilityScorer.ScoreAllCandidates(profile, state, actions, world);
                if (candidates.Count == 0) continue;

                chosenAction = candidates[0].Action;
                chosenScore = candidates[0].Score;
                topCandidates = candidates.Take(5)
                    .Select(c => new CandidateScore(c.Action.Id, c.Score)).ToList();
            }

            ActionExecutor.Execute(profile.Id, chosenAction, world);

            tickResult.Events.Add(new ActionEvent(
                world.Clock.Tick,
                world.Clock.FormatGameTime(),
                profile.Id,
                profile.Embodied,
                chosenAction.Id,
                chosenScore,
                topCandidates!,
                state.Properties.ToDictionary(p => p.Key, p => p.Value.Value),
                world.Locations.GetLocation(profile.Id)
            ));
        }

        return tickResult;
    }

    /// <summary>
    /// Batch mode: run simulation to completion. Delegates to TickOnce per tick.
    /// </summary>
    public SimulationResult Run(
        WorldState world,
        IReadOnlyList<AutonomeProfile> profiles,
        IReadOnlyList<ActionDefinition> actions,
        SimulationConfig config)
    {
        var result = new SimulationResult { MinutesPerTick = world.Clock.MinutesPerTick };

        while (world.Clock.Tick < config.TotalTicks)
        {
            var tickResult = TickOnce(world, profiles, actions, config);
            result.ActionEvents.AddRange(tickResult.Events);

            // SNAPSHOT (periodic)
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
