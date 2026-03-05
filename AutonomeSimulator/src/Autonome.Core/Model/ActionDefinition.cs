namespace Autonome.Core.Model;

/// <summary>
/// A complete action definition — who can use it, how it scores, and what it does.
/// </summary>
public sealed class ActionDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Category { get; init; }

    public ActionRequirements? Requirements { get; init; }
    public Dictionary<string, PropertyResponse> PropertyResponses { get; init; } = new();
    public Dictionary<string, float> PersonalityAffinity { get; init; } = new();
    public List<ActionStep> Steps { get; init; } = [];

    public ActionFlavor? Flavor { get; init; }
    public MemoryGeneration? MemoryGeneration { get; init; }
}

/// <summary>
/// Preconditions that must be met for an action to be a candidate.
/// </summary>
public sealed record ActionRequirements(
    bool? Embodied = null,
    List<string>? NearbyTags = null,
    TimeRange? TimeOfDay = null,
    Dictionary<string, float>? PropertyMin = null,
    Dictionary<string, float>? PropertyBelow = null,
    Dictionary<string, float>? PropertyMinAny = null,
    List<string>? BlockedByStates = null,
    List<string>? NoActiveModifier = null,
    Dictionary<string, float>? LocationPropertyMin = null,
    Dictionary<string, float>? LocationPropertyBelow = null
);

public sealed record TimeRange(float Min, float Max);

public sealed record ActionFlavor(
    List<string>? OnStart = null,
    List<string>? OnComplete = null
);

/// <summary>
/// Template for creating a continuity memory when an action completes.
/// Uses ID "mem_{autonomeId}_{actionId}" for deduplication.
/// StackMode: "refresh" resets intensity, "accumulate" adds intensity up to MaxIntensity.
/// </summary>
public sealed record MemoryGeneration(
    Dictionary<string, float>? ActionBonus = null,
    Dictionary<string, float>? PropertyMod = null,
    float DecayRate = 0.005f,
    float Intensity = 0.5f,
    float? Duration = null,
    string? Flavor = null,
    string StackMode = "refresh",
    float MaxIntensity = 1.0f
);
