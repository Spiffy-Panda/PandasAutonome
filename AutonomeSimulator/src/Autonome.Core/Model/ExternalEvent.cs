namespace Autonome.Core.Model;

/// <summary>
/// A scheduled world event (ship arrivals, supply injections, etc.).
/// Fires at TriggerTick, then every RepeatInterval ticks if set.
/// </summary>
public sealed record ExternalEvent(
    int TriggerTick,
    string Type,
    string? Location = null,
    string? Property = null,
    float? Amount = null,
    int? RepeatInterval = null
);
