using Autonome.Core.Model;
using Autonome.Core.Simulation;

namespace Autonome.Analysis;

public static class InventoryAnalyzer
{
    /// <summary>
    /// Analyzes location inventory from simulation snapshots and action events.
    /// Identifies sources (actions that add to location properties) and sinks
    /// (actions that subtract from location properties) by inspecting action definitions.
    /// </summary>
    public static InventoryReport Analyze(
        SimulationResult result,
        IReadOnlyList<ActionDefinition> actions,
        List<ExternalEvent>? events = null)
    {
        var totalTicks = result.Snapshots.Count > 0
            ? result.Snapshots.Max(s => s.Tick)
            : 0;

        // Build source/sink map from action definitions:
        // actionId -> property -> amount (positive = source, negative = sink)
        var actionEffects = BuildActionEffects(actions);

        // Count action executions at each location
        var actionCountsByLocation = CountActionsAtLocations(result.ActionEvents);

        // Count external event contributions per location+property
        var eventCounts = CountExternalEvents(events, totalTicks);

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

                // Build sources and sinks for this location+property
                var sources = new List<FlowEntry>();
                var sinks = new List<FlowEntry>();

                if (actionCountsByLocation.TryGetValue(locId, out var actionCounts))
                {
                    foreach (var (actionId, count) in actionCounts)
                    {
                        if (!actionEffects.TryGetValue(actionId, out var effects)) continue;
                        if (!effects.TryGetValue(propName, out float amount)) continue;

                        if (amount > 0)
                            sources.Add(new FlowEntry(actionId, count, amount));
                        else if (amount < 0)
                            sinks.Add(new FlowEntry(actionId, count, amount));
                    }
                }

                // Count external events as sources
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

                sources.Sort((a, b) => b.Count.CompareTo(a.Count));
                sinks.Sort((a, b) => b.Count.CompareTo(a.Count));

                propInventories[propName] = new PropertyInventory
                {
                    StartValue = values[0],
                    EndValue = values[^1],
                    MinValue = values.Min(),
                    MaxValue = values.Max(),
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
            SnapshotCount = result.Snapshots.Count,
            Locations = locations
        };
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

    /// <summary>
    /// Counts how many times each action was executed at each location.
    /// Returns locationId -> { actionId -> count }.
    /// </summary>
    private static Dictionary<string, Dictionary<string, int>> CountActionsAtLocations(
        List<ActionEvent> events)
    {
        var counts = new Dictionary<string, Dictionary<string, int>>();

        foreach (var ev in events)
        {
            if (ev.Location == null) continue;

            if (!counts.TryGetValue(ev.Location, out var actionCounts))
            {
                actionCounts = new Dictionary<string, int>();
                counts[ev.Location] = actionCounts;
            }

            actionCounts.TryGetValue(ev.ActionId, out int c);
            actionCounts[ev.ActionId] = c + 1;
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
