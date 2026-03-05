namespace Autonome.Core.Model;

/// <summary>
/// Rule for emitting passive modifiers when a property crosses a threshold.
/// </summary>
public sealed record PassiveEffectRule(
    string Condition,
    float? Threshold,
    PassiveEmission Emit
);

/// <summary>
/// Describes the passive modifier to emit when a threshold condition is met.
/// </summary>
public sealed record PassiveEmission(
    string Type,
    string Target,
    int? Depth = null,
    Dictionary<string, float>? PropertyMod = null,
    Dictionary<string, float>? ActionBonus = null,
    string? Flavor = null
);
