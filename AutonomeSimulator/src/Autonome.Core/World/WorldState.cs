using Autonome.Core.Graph;
using Autonome.Core.Runtime;
using Autonome.Core.Simulation;

namespace Autonome.Core.World;

/// <summary>
/// Single source of truth for all mutable world state.
/// </summary>
public sealed class WorldState
{
    public EntityRegistry Entities { get; } = new();
    public ModifierManager Modifiers { get; } = new();
    public RelationshipStore Relationships { get; } = new();
    public LocationGraph Locations { get; } = new();
    public AuthorityGraph AuthorityGraph { get; } = new();
    public SimulationClock Clock { get; } = new() { MinutesPerTick = 15f };
    public HistoryBuffer History { get; } = new();
}

/// <summary>
/// Simple event buffer for recording events during simulation.
/// </summary>
public sealed class HistoryBuffer
{
    private readonly List<HistoryEvent> _events = [];

    public void RecordEvent(string autonomeId, string eventName, int tick)
    {
        _events.Add(new HistoryEvent(tick, autonomeId, eventName));
    }

    public IReadOnlyList<HistoryEvent> Events => _events;
    public void Clear() => _events.Clear();
}

public sealed record HistoryEvent(int Tick, string AutonomeId, string EventName);
