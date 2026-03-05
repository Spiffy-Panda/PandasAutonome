namespace Autonome.Core.Model;

/// <summary>
/// Unified modifier: memories, directives, passives, and traits are all Modifiers
/// with different lifecycle parameters.
/// </summary>
public sealed class Modifier
{
    public required string Id { get; init; }
    public required string Source { get; init; }
    public required string Type { get; init; }
    public required string Target { get; init; }

    public Dictionary<string, float>? ActionBonus { get; init; }
    public Dictionary<string, float>? PropertyMod { get; init; }
    public Dictionary<string, float>? AffinityMod { get; init; }

    public float? Duration { get; set; }
    public float? DecayRate { get; init; }
    public float Intensity { get; set; } = 1.0f;

    public Reward? CompletionReward { get; init; }
    public int? MaxClaims { get; init; }
    public int CurrentClaims { get; set; }
    public string Priority { get; init; } = "normal";

    public string? Flavor { get; init; }
}
