namespace Autonome.Core.Model;

/// <summary>
/// Targeting specification for directive emission, resolved via the authority graph.
/// </summary>
public sealed record DirectiveTarget(
    string Scope,
    int? Depth = null,
    TargetFilter? Filter = null
);

/// <summary>
/// Filter criteria for selecting target Autonomes.
/// </summary>
public sealed record TargetFilter(
    bool? Embodied = null,
    List<string>? Tags = null,
    Dictionary<string, float>? PropertyMin = null,
    Dictionary<string, float>? PropertyMax = null
);
