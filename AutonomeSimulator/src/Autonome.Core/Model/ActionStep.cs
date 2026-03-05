namespace Autonome.Core.Model;

/// <summary>
/// A single step in an action's execution sequence.
/// Step type determines dispatch; fields are interpreted per type.
/// </summary>
public sealed class ActionStep
{
    public required string Type { get; init; }

    // moveTo
    public string? Target { get; init; }

    // animate
    public string? Animation { get; init; }

    // wait / animate duration
    public float? Duration { get; init; }
    public float? DurationMin { get; init; }
    public float? DurationMax { get; init; }

    // modifyProperty
    public string? Entity { get; init; }
    public string? Property { get; init; }
    public float? Amount { get; init; }

    // emitDirective
    public ModifierTemplate? Modifier { get; init; }

    // emitEvent
    public string? Event { get; init; }

    // socialInteraction
    public string? TargetEntity { get; init; }
    public string? RelationshipProperty { get; init; }
    public float? RelationshipAmount { get; init; }
}

/// <summary>
/// Template for creating a modifier via emitDirective step.
/// </summary>
public sealed class ModifierTemplate
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public DirectiveTarget? Target { get; init; }
    public Dictionary<string, float>? ActionBonus { get; init; }
    public Dictionary<string, float>? PropertyMod { get; init; }
    public Reward? CompletionReward { get; init; }
    public float? Duration { get; init; }
    public float? DecayRate { get; init; }
    public string Priority { get; init; } = "normal";
    public int? MaxClaims { get; init; }
    public string? Flavor { get; init; }
}
