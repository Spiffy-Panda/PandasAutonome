namespace Autonome.Core.Model;

/// <summary>
/// A location in the world graph where embodied Autonomes can be.
/// </summary>
public sealed record LocationDefinition(
    string Id,
    string DisplayName,
    List<string> Tags,
    List<LocationEdge> ConnectedTo,
    Dictionary<string, PropertyDefinition>? Properties = null
);

/// <summary>
/// A weighted edge in the location graph. Cost is in simulation ticks.
/// </summary>
public sealed record LocationEdge(
    string Target,
    int Cost = 1
);
