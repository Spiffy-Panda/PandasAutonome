namespace Autonome.Core.Model;

/// <summary>
/// Classification level for a property, controlling UI visibility and behavioral urgency.
/// </summary>
public enum PropertyLevel
{
    /// <summary>If zero, only actions that restore it are allowed.</summary>
    Vital,

    /// <summary>Everyone of a broad class has it; always shown in UI.</summary>
    Essential,

    /// <summary>Only certain entities care; hidden in UI when zero and irrelevant.</summary>
    Optional,

    /// <summary>Catch-all for org/town properties; no special behavior.</summary>
    Any
}

/// <summary>
/// A named set of property-to-level mappings (e.g., "embodied_base", "trade_goods").
/// </summary>
public sealed record PropertyLevelSet(
    string Id,
    Dictionary<string, PropertyLevel> Levels
);

/// <summary>
/// Top-level configuration loaded from property_levels.json.
/// Contains named sets and default set assignments per entity type.
/// </summary>
public sealed record PropertyLevelConfig(
    Dictionary<string, PropertyLevelSet> Sets,
    Dictionary<string, List<string>> EntityTypeDefaults
);
