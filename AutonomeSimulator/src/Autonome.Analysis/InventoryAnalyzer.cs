using Autonome.Core.Model;
using Autonome.Core.Simulation;

namespace Autonome.Analysis;

public static class InventoryAnalyzer
{
    /// <summary>
    /// Analyzes location inventory from simulation snapshots and action events.
    /// Identifies sources (actions that add to location properties) and sinks
    /// (actions that subtract from location properties) by inspecting action definitions.
    /// Includes decay as a calculated sink and per-actor breakdowns.
    /// </summary>
    public static InventoryReport Analyze(
        SimulationResult result,
        IReadOnlyList<ActionDefinition> actions,
        IReadOnlyList<LocationDefinition> locationDefs,
        List<ExternalEvent>? events = null)
    {
        var totalTicks = result.Snapshots.Count > 0
            ? result.Snapshots.Max(s => s.Tick)
            : 0;

        float minutesPerTick = result.MinutesPerTick;

        // Build source/sink map from action definitions:
        // actionId -> property -> amount (positive = source, negative = sink)
        var actionEffects = BuildActionEffects(actions);

        // Count action executions at each location, with per-actor detail
        var actionCountsByLocation = CountActionsAtLocations(result.ActionEvents);

        // Count external event contributions per location+property
        var eventCounts = CountExternalEvents(events, totalTicks);

        // Build decay rate lookup: locationId -> propName -> decayRate
        var decayRates = BuildDecayRateLookup(locationDefs);

        // Build per-location inventory data from snapshots
        var locationIds = new HashSet<string>();
        foreach (var snap in result.Snapshots)
        {
            if (snap.LocationProperties != null)
                foreach (var locId in snap.LocationProperties.Keys)
                    locationIds.Add(locId);
        }

        var locations = new List<LocationInventory>();
        foreach (var locId in locationIds.OrderBy(id => id))
        {
            var propInventories = new Dictionary<string, PropertyInventory>();

            // Collect all property names at this location
            var propNames = new HashSet<string>();
            foreach (var snap in result.Snapshots)
            {
                if (snap.LocationProperties?.TryGetValue(locId, out var props) == true)
                    foreach (var key in props.Keys)
                        propNames.Add(key);
            }

            foreach (var propName in propNames.OrderBy(n => n))
            {
                var timeline = new List<InventorySnapshot>();
                foreach (var snap in result.Snapshots)
                {
                    float val = 0;
                    if (snap.LocationProperties?.TryGetValue(locId, out var props) == true)
                        props.TryGetValue(propName, out val);
                    timeline.Add(new InventorySnapshot(snap.Tick, val));
                }

                if (timeline.Count == 0) continue;

                var values = timeline.Select(s => s.Value).ToList();

                // Decay calculation
                float decayRate = 0;
                if (decayRates.TryGetValue(locId, out var locRates))
                    locRates.TryGetValue(propName, out decayRate);

                float estimatedDecay = EstimateDecay(timeline, decayRate, minutesPerTick);

                // Build sources and sinks for this location+property
                var sources = new List<FlowEntry>();
                var sinks = new List<FlowEntry>();

                if (actionCountsByLocation.TryGetValue(locId, out var actionData))
                {
                    foreach (var (actionId, data) in actionData)
                    {
                        if (!actionEffects.TryGetValue(actionId, out var effects)) continue;
                        if (!effects.TryGetValue(propName, out float amount)) continue;

                        var actors = data.ActorCounts
                            .Select(kv => new ActorCount(
                                kv.Key,
                                kv.Value,
                                data.ActorTicks.GetValueOrDefault(kv.Key)))
                            .OrderByDescending(a => a.Count)
                            .ToList();

                        if (amount > 0)
                            sources.Add(new FlowEntry(actionId, data.Total, amount, actors));
                        else if (amount < 0)
                            sinks.Add(new FlowEntry(actionId, data.Total, amount, actors));
                    }
                }

                // External events as sources/sinks
                int eventSourceCount = 0;
                var eventKey = (locId, propName);
                if (eventCounts.TryGetValue(eventKey, out var evtData))
                {
                    eventSourceCount = evtData.Count;
                    if (evtData.Amount > 0)
                        sources.Add(new FlowEntry("(ship/event)", evtData.Count, evtData.Amount));
                    else if (evtData.Amount < 0)
                        sinks.Add(new FlowEntry("(ship/event)", evtData.Count, evtData.Amount));
                }

                // Decay as a sink
                if (estimatedDecay > 0.1f)
                {
                    sinks.Add(new FlowEntry("(decay)", totalTicks, 0, null) with
                    {
                        AmountPerAction = -estimatedDecay / totalTicks
                    });
                }

                sources.Sort((a, b) => b.Count.CompareTo(a.Count));
                sinks.Sort((a, b) => b.Count.CompareTo(a.Count));

                propInventories[propName] = new PropertyInventory
                {
                    StartValue = values[0],
                    EndValue = values[^1],
                    MinValue = values.Min(),
                    MaxValue = values.Max(),
                    DecayRate = decayRate,
                    EstimatedDecay = estimatedDecay,
                    Timeline = timeline,
                    Sources = sources,
                    Sinks = sinks,
                    EventSourceCount = eventSourceCount
                };
            }

            if (propInventories.Count > 0)
            {
                locations.Add(new LocationInventory
                {
                    Id = locId,
                    Properties = propInventories
                });
            }
        }

        return new InventoryReport
        {
            TotalTicks = totalTicks,
            MinutesPerTick = minutesPerTick,
            SnapshotCount = result.Snapshots.Count,
            Locations = locations
        };
    }

    /// <summary>
    /// Estimates total decay over the simulation by interpolating between snapshots.
    /// Matches engine formula: loss/tick = value * decayRate * minutesPerTick.
    /// Uses trapezoidal approximation between snapshot pairs.
    /// </summary>
    private static float EstimateDecay(List<InventorySnapshot> timeline, float decayRate, float minutesPerTick)
    {
        if (decayRate <= 0 || timeline.Count < 2) return 0;

        float totalDecay = 0;
        for (int i = 0; i < timeline.Count - 1; i++)
        {
            int tickSpan = timeline[i + 1].Tick - timeline[i].Tick;
            float avgValue = (timeline[i].Value + timeline[i + 1].Value) / 2f;
            totalDecay += avgValue * decayRate * minutesPerTick * tickSpan;
        }

        return totalDecay;
    }

    /// <summary>
    /// Builds a lookup of locationId -> propName -> decayRate from location definitions.
    /// </summary>
    private static Dictionary<string, Dictionary<string, float>> BuildDecayRateLookup(
        IReadOnlyList<LocationDefinition> locationDefs)
    {
        var lookup = new Dictionary<string, Dictionary<string, float>>();
        foreach (var loc in locationDefs)
        {
            if (loc.Properties == null) continue;
            var rates = new Dictionary<string, float>();
            foreach (var (propName, propDef) in loc.Properties)
            {
                if (propDef.DecayRate > 0)
                    rates[propName] = propDef.DecayRate;
            }
            if (rates.Count > 0)
                lookup[loc.Id] = rates;
        }
        return lookup;
    }

    /// <summary>
    /// Scans action definitions for modifyProperty steps targeting location properties.
    /// Returns actionId -> { propertyName -> totalAmount }.
    /// </summary>
    private static Dictionary<string, Dictionary<string, float>> BuildActionEffects(
        IReadOnlyList<ActionDefinition> actions)
    {
        var effects = new Dictionary<string, Dictionary<string, float>>();

        foreach (var action in actions)
        {
            foreach (var step in action.Steps)
            {
                if (step.Type != "modifyProperty") continue;
                if (step.Entity == null || !step.Entity.StartsWith("location:")) continue;
                if (step.Property == null || step.Amount == null) continue;

                if (!effects.TryGetValue(action.Id, out var propEffects))
                {
                    propEffects = new Dictionary<string, float>();
                    effects[action.Id] = propEffects;
                }

                propEffects.TryGetValue(step.Property, out float existing);
                propEffects[step.Property] = existing + step.Amount.Value;
            }
        }

        return effects;
    }

    private sealed class ActionLocationData
    {
        public int Total { get; set; }
        public Dictionary<string, int> ActorCounts { get; } = new();
        public Dictionary<string, List<int>> ActorTicks { get; } = new();
    }

    /// <summary>
    /// Counts action executions at each location with per-actor breakdown.
    /// Returns locationId -> { actionId -> (total, { actorId -> count }) }.
    /// </summary>
    private static Dictionary<string, Dictionary<string, ActionLocationData>> CountActionsAtLocations(
        List<ActionEvent> events)
    {
        var counts = new Dictionary<string, Dictionary<string, ActionLocationData>>();

        foreach (var ev in events)
        {
            if (ev.Location == null) continue;

            if (!counts.TryGetValue(ev.Location, out var actionCounts))
            {
                actionCounts = new Dictionary<string, ActionLocationData>();
                counts[ev.Location] = actionCounts;
            }

            if (!actionCounts.TryGetValue(ev.ActionId, out var data))
            {
                data = new ActionLocationData();
                actionCounts[ev.ActionId] = data;
            }

            data.Total++;
            data.ActorCounts.TryGetValue(ev.AutonomeId, out int c);
            data.ActorCounts[ev.AutonomeId] = c + 1;

            if (!data.ActorTicks.TryGetValue(ev.AutonomeId, out var ticks))
            {
                ticks = new List<int>();
                data.ActorTicks[ev.AutonomeId] = ticks;
            }
            ticks.Add(ev.Tick);
        }

        return counts;
    }

    /// <summary>
    /// Counts how many times external events fire for each location+property.
    /// </summary>
    private static Dictionary<(string Location, string Property), (int Count, float Amount)>
        CountExternalEvents(List<ExternalEvent>? events, int totalTicks)
    {
        var counts = new Dictionary<(string, string), (int Count, float Amount)>();
        if (events == null) return counts;

        foreach (var evt in events)
        {
            if (evt.Type != "modify_location_property") continue;
            if (evt.Location == null || evt.Property == null || !evt.Amount.HasValue) continue;

            int fireCount = 0;
            if (evt.RepeatInterval.HasValue && evt.RepeatInterval.Value > 0)
            {
                for (int tick = evt.TriggerTick; tick <= totalTicks; tick += evt.RepeatInterval.Value)
                    fireCount++;
            }
            else if (evt.TriggerTick <= totalTicks)
            {
                fireCount = 1;
            }

            var key = (evt.Location, evt.Property);
            if (counts.TryGetValue(key, out var existing))
                counts[key] = (existing.Count + fireCount, evt.Amount.Value);
            else
                counts[key] = (fireCount, evt.Amount.Value);
        }

        return counts;
    }
}
