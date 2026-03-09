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

        // 1.5. UPKEEP (rent drain: NPCs -> org landlords)
        PropertyTicker.TickUpkeep(world, 1f);

        // 2. MODIFIER LIFECYCLE
        world.Modifiers.Tick(1f);

        // 3. CLEAR BUSY FLAGS
        world.Entities.TickBusy(world.Clock.Tick);

        var tickResult = new TickResult(world.Clock.Tick, world.Clock.FormatGameTime());

        // 3.5. CONTINUE TRAVEL — advance traveling entities that just finished a hop
        foreach (var (id, state) in world.Entities.All())
        {
            if (state.Travel == null) continue;       // not traveling
            if (state.BusyUntilTick > 0) continue;    // still mid-hop

            string? currentLoc = world.Locations.GetLocation(id);
            if (currentLoc == null) { state.Travel = null; continue; }

            if (currentLoc == state.Travel.Destination)
            {
                // Arrived at final destination — execute remaining action steps
                var travel = state.Travel;
                state.Travel = null;

                var contResult = ActionExecutor.ContinueAction(id, travel, world);

                if (!contResult.Deferred)
                {
                    tickResult.Events.Add(new ActionEvent(
                        world.Clock.Tick,
                        world.Clock.FormatGameTime(),
                        id,
                        state.Embodied,
                        travel.Action.Id,
                        -2f,
                        [new CandidateScore(travel.Action.Id, -2f)],
                        state.Properties.ToDictionary(p => p.Key, p => p.Value.Value),
                        currentLoc,
                        "action_complete"
                    ));
                }
            }
            else
            {
                // Not at destination yet — advance to next hop
                var hopInfo = world.Locations.GetNextHop(currentLoc, state.Travel.Destination);
                if (hopInfo == null)
                {
                    // Route became unreachable mid-travel — abort
                    state.Travel = null;
                    continue;
                }

                string nextHop = hopInfo.Value.NextHop;
                int hopCost = world.Locations.GetEdgeCost(currentLoc, nextHop) ?? 1;

                world.Locations.SetLocation(id, nextHop);
                world.Entities.SetBusy(id, world.Clock.Tick + hopCost);

                tickResult.Events.Add(new ActionEvent(
                    world.Clock.Tick,
                    world.Clock.FormatGameTime(),
                    id,
                    state.Embodied,
                    state.Travel.Action.Id,
                    -3f,
                    [new CandidateScore(state.Travel.Action.Id, -3f)],
                    state.Properties.ToDictionary(p => p.Key, p => p.Value.Value),
                    nextHop,
                    "travel_hop"
                ));

                // If this hop reaches the destination, travel continues next tick
                // when BusyUntilTick clears and phase 3.5 runs again
            }
        }

        // 4. EVALUATE + ACT (only Autonomes due for evaluation this tick)
        foreach (var (profile, state) in EvaluationScheduler.GetDue(world, profiles))
        {
            ActionDefinition? chosenAction = null;
            float chosenScore = 0f;
            List<CandidateScore>? topCandidates = null;
            int chosenRank = 1;

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

                // Check for vital zero-lock — if active, pick deterministically (survival is non-negotiable)
                var zeroedVitals = state.GetZeroedVitalProperties();
                bool vitalLockActive = zeroedVitals.Count > 0;

                int chosenIndex;
                if (vitalLockActive || candidates.Count == 1)
                {
                    chosenIndex = 0;
                }
                else
                {
                    chosenIndex = WeightedRandomSelect(profile, candidates, world.Clock.Tick);
                }

                chosenAction = candidates[chosenIndex].Action;
                chosenScore = candidates[chosenIndex].Score;
                topCandidates = candidates.Take(5)
                    .Select(c => new CandidateScore(c.Action.Id, c.Score)).ToList();
                chosenRank = chosenIndex + 1;
            }

            var execResult = ActionExecutor.Execute(profile.Id, chosenAction, world);

            tickResult.Events.Add(new ActionEvent(
                world.Clock.Tick,
                world.Clock.FormatGameTime(),
                profile.Id,
                profile.Embodied,
                chosenAction.Id,
                chosenScore,
                topCandidates!,
                state.Properties.ToDictionary(p => p.Key, p => p.Value.Value),
                world.Locations.GetLocation(profile.Id),
                execResult.Deferred ? "travel_start" : "action_start",
                chosenRank
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

    /// <summary>
    /// Weighted random selection among top-K candidates using softmax with temperature
    /// scaled by entity impulsiveness. Low impulsiveness → sharp distribution (mostly top action).
    /// High impulsiveness → flat distribution (genuine randomization).
    /// </summary>
    private static int WeightedRandomSelect(
        AutonomeProfile profile,
        List<ScoredAction> candidates,
        int tick)
    {
        float impulsiveness = profile.Personality.GetValueOrDefault("impulsiveness", 0.5f);
        int k = Math.Min(3 + (int)(impulsiveness * 4), candidates.Count);
        float temperature = 0.15f + impulsiveness * 0.35f;

        // Compute softmax weights for top-K candidates
        double[] weights = new double[k];
        double maxScore = candidates[0].Score; // For numerical stability
        double sumWeights = 0;

        for (int i = 0; i < k; i++)
        {
            weights[i] = Math.Exp((candidates[i].Score - maxScore) / temperature);
            sumWeights += weights[i];
        }

        // Deterministic random roll based on entity ID and tick
        int hash = HashCode.Combine(profile.Id, tick, "topk");
        double roll = (hash & 0x7FFFFFFF) / (double)int.MaxValue * sumWeights;

        double cumulative = 0;
        for (int i = 0; i < k; i++)
        {
            cumulative += weights[i];
            if (roll <= cumulative) return i;
        }

        return 0; // Fallback to top action
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

                case "modify_entity_property":
                    if (evt.EntityId != null && evt.Property != null && evt.Amount.HasValue)
                    {
                        var entity = world.Entities.Get(evt.EntityId);
                        if (entity != null && entity.Properties.TryGetValue(evt.Property, out var entProp))
                        {
                            entProp.Value += evt.Amount.Value;
                            entProp.Clamp();
                        }
                    }
                    break;
            }
        }
    }

    public static WorldSnapshot TakeSnapshot(WorldState world, IReadOnlyList<AutonomeProfile> profiles)
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
    string? Location = null,
    string? EventType = "action_start",
    int ChosenRank = 1
);

public sealed record CandidateScore(string ActionId, float Score);

public sealed record WorldSnapshot(
    int Tick,
    string GameTime,
    Dictionary<string, Dictionary<string, float>> EntityProperties,
    Dictionary<string, Dictionary<string, float>>? LocationProperties = null
);
