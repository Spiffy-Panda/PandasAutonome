namespace Autonome.Core.Model;

/// <summary>
/// Declarative expression that computes a property value from subordinates in the authority graph.
/// </summary>
public sealed record AggregationExpr(
    string Source,
    int? Depth,
    TargetFilter? Filter,
    string Property,
    AggregationFunction Function,
    float Weight,
    BlendMode Blend,
    bool PerTick = false,
    string? Note = null
);

public enum AggregationFunction
{
    Avg,
    Sum,
    Min,
    Max,
    Count,
    Ratio
}

public enum BlendMode
{
    Replace,
    Lerp,
    Additive,
    Min,
    Max
}
