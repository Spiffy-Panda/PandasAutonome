namespace Autonome.Analysis;

/// <summary>
/// Inventory analysis for location stockpiles — timelines, sources, and sinks.
/// </summary>
public sealed class InventoryReport
{
    public required int TotalTicks { get; init; }
    public required int SnapshotCount { get; init; }
    public required List<LocationInventory> Locations { get; init; }
}

public sealed class LocationInventory
{
    public required string Id { get; init; }
    public required Dictionary<string, PropertyInventory> Properties { get; init; }
}

public sealed class PropertyInventory
{
    public required float StartValue { get; init; }
    public required float EndValue { get; init; }
    public required float MinValue { get; init; }
    public required float MaxValue { get; init; }
    public required float DecayRate { get; init; }
    public required float EstimatedDecay { get; init; }
    public required List<InventorySnapshot> Timeline { get; init; }
    public required List<FlowEntry> Sources { get; init; }
    public required List<FlowEntry> Sinks { get; init; }
    public required int EventSourceCount { get; init; }
}

public sealed record InventorySnapshot(int Tick, float Value);

public sealed record FlowEntry(string ActionId, int Count, float AmountPerAction, List<ActorCount>? Actors = null);

public sealed record ActorCount(string EntityId, int Count);
