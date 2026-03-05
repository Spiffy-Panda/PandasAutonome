using Autonome.Core.Model;

namespace Autonome.Core.World;

/// <summary>
/// Stores mutable runtime state for location properties (inventory, supply levels).
/// Parallel to EntityRegistry but for locations.
/// </summary>
public sealed class LocationStateStore
{
    private readonly Dictionary<string, Dictionary<string, PropertyState>> _locations = new();

    public void Initialize(string locationId, Dictionary<string, PropertyDefinition> defs)
    {
        var props = new Dictionary<string, PropertyState>();
        foreach (var (id, def) in defs)
            props[id] = new PropertyState(def);
        _locations[locationId] = props;
    }

    public Dictionary<string, PropertyState>? Get(string locationId) =>
        _locations.TryGetValue(locationId, out var props) ? props : null;

    public PropertyState? GetProperty(string locationId, string propId)
    {
        if (_locations.TryGetValue(locationId, out var props) &&
            props.TryGetValue(propId, out var prop))
            return prop;
        return null;
    }

    public IEnumerable<KeyValuePair<string, Dictionary<string, PropertyState>>> All() => _locations;
}
