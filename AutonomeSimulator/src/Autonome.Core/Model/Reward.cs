namespace Autonome.Core.Model;

/// <summary>
/// Reward delivered when a directive-type modifier's action is completed.
/// </summary>
public sealed record Reward(
    Dictionary<string, float>? PropertyChanges = null,
    RelationshipChange? RelationshipMod = null
);

/// <summary>
/// A change to apply to a relationship property.
/// </summary>
public sealed record RelationshipChange(
    string Target,
    string Property,
    float Amount
);
