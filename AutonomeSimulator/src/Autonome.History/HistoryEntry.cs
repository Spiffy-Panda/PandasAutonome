namespace Autonome.History;

/// <summary>
/// A single entry in the simulation history log.
/// </summary>
public sealed class HistoryEntry
{
    public required int Tick { get; init; }
    public required string GameTime { get; init; }
    public required string Type { get; init; }
    public required string AutonomeId { get; init; }
    public bool? Embodied { get; init; }
    public string? ActionId { get; init; }
    public float? Score { get; init; }
    public List<CandidateEntry>? TopCandidates { get; init; }
    public Dictionary<string, float>? PropertySnapshot { get; init; }
    public List<ModifierContribution>? ActiveModifiers { get; init; }
    public string? Message { get; init; }
}

public sealed record CandidateEntry(string ActionId, float Score);
public sealed record ModifierContribution(string ModifierId, string Type, float Intensity, float Contribution);
