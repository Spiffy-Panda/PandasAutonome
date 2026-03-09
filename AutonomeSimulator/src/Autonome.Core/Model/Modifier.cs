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
    public bool Gossip { get; init; }

    /// <summary>Semantic gossip type: food_location, noble_weakness, danger_warning, tavern_quality.</summary>
    public string? GossipType { get; init; }
    /// <summary>Location ID relevant to this gossip (e.g., where food is, where danger is).</summary>
    public string? GossipLocation { get; init; }
    /// <summary>Entity ID of the social interaction target (for social memory).</summary>
    public string? SocialTarget { get; init; }
}
