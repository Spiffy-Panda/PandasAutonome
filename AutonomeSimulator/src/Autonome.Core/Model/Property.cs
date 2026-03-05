namespace Autonome.Core.Model;

/// <summary>
/// Definition of a property — the universal state unit. Deserialized from JSON.
/// Everything that was previously "need", "resource", or "object state" is a Property.
/// </summary>
public sealed record PropertyDefinition(
    string Id,
    float Value,
    float Min = 0f,
    float Max = 1f,
    float DecayRate = 0f,
    float? Critical = null,
    List<AggregationExpr>? Aggregations = null,
    List<PassiveEffectRule>? PassiveEffects = null
);

/// <summary>
/// Mutable runtime state for a property, held by the EntityRegistry.
/// </summary>
public sealed class PropertyState
{
    public float Value { get; set; }
    public float Min { get; }
    public float Max { get; }
    public float DecayRate { get; }
    public float? Critical { get; }
    public List<AggregationExpr>? Aggregations { get; }
    public List<PassiveEffectRule>? PassiveEffects { get; }

    public PropertyState(PropertyDefinition def)
    {
        Value = def.Value;
        Min = def.Min;
        Max = def.Max;
        DecayRate = def.DecayRate;
        Critical = def.Critical;
        Aggregations = def.Aggregations;
        PassiveEffects = def.PassiveEffects;
    }

    public void Clamp()
    {
        Value = Math.Clamp(Value, Min, Max);
    }
}
