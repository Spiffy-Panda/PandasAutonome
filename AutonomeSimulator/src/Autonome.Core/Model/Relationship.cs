namespace Autonome.Core.Model;

/// <summary>
/// Unified relationship between two Autonomes. Covers social, authority, affiliation, and ownership.
/// Relationships are themselves stateful — their properties can decay and trigger effects.
/// </summary>
public sealed class Relationship
{
    public required string Source { get; init; }
    public required string Target { get; init; }
    public required HashSet<string> Tags { get; init; }
    public Dictionary<string, PropertyState> Properties { get; init; } = new();
}
