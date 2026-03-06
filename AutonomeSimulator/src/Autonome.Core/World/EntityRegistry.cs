using Autonome.Core.Model;

namespace Autonome.Core.World;

/// <summary>
/// Stores all Autonome runtime state indexed by ID.
/// </summary>
public class EntityRegistry
{
    private readonly Dictionary<string, EntityState> _entities = new();

    public void Register(AutonomeProfile profile, Dictionary<string, PropertyLevel>? resolvedLevels = null)
    {
        var state = new EntityState(profile, resolvedLevels);
        _entities[profile.Id] = state;
    }

    public EntityState? Get(string id) =>
        _entities.TryGetValue(id, out var entity) ? entity : null;

    public PropertyState? GetProperty(string entityId, string propertyId)
    {
        var entity = Get(entityId);
        if (entity == null) return null;
        return entity.Properties.TryGetValue(propertyId, out var prop) ? prop : null;
    }

    public IEnumerable<KeyValuePair<string, EntityState>> All() => _entities;

    public bool IsBusy(string id)
    {
        var entity = Get(id);
        return entity?.BusyUntilTick > 0;
    }

    public void SetBusy(string id, int untilTick)
    {
        var entity = Get(id);
        if (entity != null) entity.BusyUntilTick = untilTick;
    }

    public void TickBusy(int currentTick)
    {
        foreach (var (_, entity) in _entities)
        {
            if (entity.BusyUntilTick > 0 && currentTick >= entity.BusyUntilTick)
                entity.BusyUntilTick = 0;
        }
    }
}

/// <summary>
/// Runtime state for a single Autonome.
/// </summary>
public sealed class EntityState
{
    public string Id { get; }
    public bool Embodied { get; }
    public int? EvaluationInterval { get; }
    public Dictionary<string, PropertyState> Properties { get; }
    public Dictionary<string, float> Personality { get; }
    public Identity? Identity { get; }
    public Dictionary<string, PropertyLevel> PropertyLevels { get; }
    public string? HomeLocation { get; }
    public int BusyUntilTick { get; set; }

    /// <summary>Non-null when entity is mid-journey between locations.</summary>
    public TravelState? Travel { get; set; }

    /// <summary>ID of the last action executed or in progress.</summary>
    public string? LastActionId { get; set; }

    /// <summary>Current activity status for API consumers and Godot visualization.</summary>
    public EntityActivity CurrentActivity
    {
        get
        {
            if (Travel != null)
                return new EntityActivity("traveling", Travel.Action.Id, Travel.Destination);
            if (BusyUntilTick > 0)
                return new EntityActivity("busy", LastActionId, null);
            return new EntityActivity("idle", null, null);
        }
    }

    public EntityState(AutonomeProfile profile, Dictionary<string, PropertyLevel>? resolvedLevels = null)
    {
        Id = profile.Id;
        Embodied = profile.Embodied;
        EvaluationInterval = profile.EvaluationInterval;
        Personality = new Dictionary<string, float>(profile.Personality);
        Identity = profile.Identity;
        PropertyLevels = resolvedLevels ?? new Dictionary<string, PropertyLevel>();
        HomeLocation = profile.HomeLocation;

        Properties = new Dictionary<string, PropertyState>();
        foreach (var (propId, def) in profile.Properties)
        {
            Properties[propId] = new PropertyState(def);
        }
    }

    /// <summary>
    /// Returns IDs of vital properties currently at their minimum value.
    /// </summary>
    public List<string> GetZeroedVitalProperties()
    {
        var zeroed = new List<string>();
        foreach (var (propId, level) in PropertyLevels)
        {
            if (level == PropertyLevel.Vital
                && Properties.TryGetValue(propId, out var prop)
                && prop.Value <= prop.Min)
            {
                zeroed.Add(propId);
            }
        }
        return zeroed;
    }
}

/// <summary>
/// Tracks an entity's in-progress travel. When non-null on EntityState,
/// the entity is mid-journey and the tick loop auto-advances it hop by hop.
/// </summary>
public sealed class TravelState
{
    /// <summary>Final destination location ID.</summary>
    public string Destination { get; }

    /// <summary>The action whose remaining steps execute after arrival.</summary>
    public ActionDefinition Action { get; }

    /// <summary>
    /// Index into Action.Steps of the first step AFTER the moveTo.
    /// Steps [PostMoveStepIndex..] execute once the entity reaches Destination.
    /// </summary>
    public int PostMoveStepIndex { get; }

    public TravelState(string destination, ActionDefinition action, int postMoveStepIndex)
    {
        Destination = destination;
        Action = action;
        PostMoveStepIndex = postMoveStepIndex;
    }
}

/// <summary>
/// Lightweight status snapshot for API consumers and Godot visualization.
/// </summary>
public sealed record EntityActivity(
    string Status,       // "idle", "traveling", "busy"
    string? ActionId,    // what action is in progress (null if idle)
    string? Destination  // where the entity is heading (null if not traveling)
);
