using Autonome.Core.Model;

namespace Autonome.Core.World;

/// <summary>
/// Spatial index for embodied Autonomes. Tracks locations, provides proximity queries,
/// and precomputes all-pairs shortest paths for travel cost lookups.
/// </summary>
public class LocationGraph
{
    private readonly Dictionary<string, LocationDefinition> _locations = new();
    private readonly Dictionary<string, string> _entityLocations = new(); // entityId → locationId
    private readonly Dictionary<string, HashSet<string>> _locationEntities = new(); // locationId → entityIds

    // Precomputed routing: _routingTable[from][to] = (nextHop, totalCost)
    private Dictionary<string, Dictionary<string, (string NextHop, int TotalCost)>>? _routingTable;

    public void AddLocation(LocationDefinition location)
    {
        _locations[location.Id] = location;
        _locationEntities[location.Id] = new HashSet<string>();
    }

    /// <summary>
    /// Precomputes all-pairs shortest paths using Dijkstra from each source.
    /// Must be called after all locations are added.
    /// </summary>
    public void BuildRoutingTable()
    {
        _routingTable = new();

        foreach (var sourceId in _locations.Keys)
        {
            _routingTable[sourceId] = DijkstraFrom(sourceId);
        }
    }

    private Dictionary<string, (string NextHop, int TotalCost)> DijkstraFrom(string source)
    {
        var dist = new Dictionary<string, int>();
        var prev = new Dictionary<string, string?>(); // prev[node] = predecessor
        var visited = new HashSet<string>();
        var pq = new PriorityQueue<string, int>();

        foreach (var id in _locations.Keys)
        {
            dist[id] = int.MaxValue;
            prev[id] = null;
        }

        dist[source] = 0;
        pq.Enqueue(source, 0);

        while (pq.Count > 0)
        {
            var current = pq.Dequeue();
            if (!visited.Add(current)) continue;

            if (!_locations.TryGetValue(current, out var loc)) continue;

            foreach (var edge in loc.ConnectedTo)
            {
                if (!_locations.ContainsKey(edge.Target)) continue;
                int newDist = dist[current] + edge.Cost;
                if (newDist < dist[edge.Target])
                {
                    dist[edge.Target] = newDist;
                    prev[edge.Target] = current;
                    pq.Enqueue(edge.Target, newDist);
                }
            }
        }

        // Build result: for each reachable destination, trace back to find the first hop from source
        var result = new Dictionary<string, (string NextHop, int TotalCost)>();
        foreach (var destId in _locations.Keys)
        {
            if (destId == source) continue;
            if (dist[destId] == int.MaxValue) continue; // unreachable

            // Trace back from dest to source to find the first hop
            string? hop = destId;
            while (hop != null && prev[hop] != source)
            {
                hop = prev[hop];
            }

            if (hop != null)
            {
                result[destId] = (hop, dist[destId]);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the total travel cost (in ticks) between two locations.
    /// Returns 0 if same location, int.MaxValue if unreachable.
    /// </summary>
    public int GetTravelCost(string from, string to)
    {
        if (from == to) return 0;
        if (_routingTable == null) return 0; // no routing table = instant travel (backward compat)

        if (_routingTable.TryGetValue(from, out var routes) &&
            routes.TryGetValue(to, out var route))
        {
            return route.TotalCost;
        }

        return int.MaxValue; // unreachable
    }

    /// <summary>
    /// Returns the edge cost for a direct connection from 'from' to 'to'.
    /// Returns null if there is no direct edge.
    /// </summary>
    public int? GetEdgeCost(string from, string to)
    {
        if (!_locations.TryGetValue(from, out var loc)) return null;
        foreach (var edge in loc.ConnectedTo)
        {
            if (edge.Target == to) return edge.Cost;
        }
        return null;
    }

    /// <summary>
    /// Gets the next hop on the shortest path from 'from' to 'to'.
    /// </summary>
    public (string NextHop, int TotalCost)? GetNextHop(string from, string to)
    {
        if (from == to) return null;
        if (_routingTable == null) return null;

        if (_routingTable.TryGetValue(from, out var routes) &&
            routes.TryGetValue(to, out var route))
        {
            return route;
        }

        return null;
    }

    /// <summary>
    /// Finds the nearest location (by travel cost) that has the given tag.
    /// Uses the precomputed routing table for efficient lookup.
    /// </summary>
    public string? FindNearestLocationWithTag(string from, string tag)
    {
        // Check current location first
        if (_locations.TryGetValue(from, out var fromLoc) && fromLoc.Tags.Contains(tag))
            return from;

        if (_routingTable == null)
        {
            // Fallback: check adjacent only (pre-routing-table behavior)
            if (_locations.TryGetValue(from, out var loc))
            {
                foreach (var edge in loc.ConnectedTo)
                {
                    if (_locations.TryGetValue(edge.Target, out var connLoc) && connLoc.Tags.Contains(tag))
                        return edge.Target;
                }
            }
            return null;
        }

        // Use routing table: find the closest location with the tag
        string? nearest = null;
        int nearestCost = int.MaxValue;

        if (_routingTable.TryGetValue(from, out var routes))
        {
            foreach (var (destId, route) in routes)
            {
                if (route.TotalCost < nearestCost &&
                    _locations.TryGetValue(destId, out var destLoc) &&
                    destLoc.Tags.Contains(tag))
                {
                    nearest = destId;
                    nearestCost = route.TotalCost;
                }
            }
        }

        return nearest;
    }

    public void SetLocation(string entityId, string locationId)
    {
        // Remove from old location
        if (_entityLocations.TryGetValue(entityId, out var oldLocation))
        {
            if (_locationEntities.TryGetValue(oldLocation, out var oldSet))
                oldSet.Remove(entityId);
        }

        _entityLocations[entityId] = locationId;

        if (!_locationEntities.TryGetValue(locationId, out var newSet))
        {
            newSet = new HashSet<string>();
            _locationEntities[locationId] = newSet;
        }
        newSet.Add(entityId);
    }

    public string? GetLocation(string entityId) =>
        _entityLocations.TryGetValue(entityId, out var loc) ? loc : null;

    /// <summary>
    /// Returns all entity IDs currently at the given location.
    /// </summary>
    public IReadOnlyCollection<string> GetEntitiesAtLocation(string locationId) =>
        _locationEntities.TryGetValue(locationId, out var set) ? set : (IReadOnlyCollection<string>)Array.Empty<string>();

    /// <summary>
    /// Returns true if the location itself has at least one of the specified tags.
    /// Unlike HasNearbyTag, does NOT check connected locations.
    /// </summary>
    public bool LocationHasAnyTag(string locationId, IEnumerable<string> tags)
    {
        if (!_locations.TryGetValue(locationId, out var loc)) return false;
        foreach (var tag in tags)
        {
            if (loc.Tags.Contains(tag)) return true;
        }
        return false;
    }

    public bool HasNearbyTag(string locationId, string tag)
    {
        // Check if the location itself has the tag
        if (_locations.TryGetValue(locationId, out var loc))
        {
            if (loc.Tags.Contains(tag)) return true;

            // Check connected locations
            foreach (var edge in loc.ConnectedTo)
            {
                if (_locations.TryGetValue(edge.Target, out var connLoc) && connLoc.Tags.Contains(tag))
                    return true;
            }
        }
        return false;
    }

    public string? FindNearestWithTag(string entityId, string tag)
    {
        var location = GetLocation(entityId);
        if (location == null) return null;

        // Check entities at current location first
        if (_locationEntities.TryGetValue(location, out var entities))
        {
            foreach (var id in entities)
            {
                if (id != entityId && HasEntityTag(id, tag)) return id;
            }
        }

        // Check connected locations
        if (_locations.TryGetValue(location, out var loc))
        {
            foreach (var edge in loc.ConnectedTo)
            {
                if (_locationEntities.TryGetValue(edge.Target, out var connEntities))
                {
                    foreach (var id in connEntities)
                    {
                        if (HasEntityTag(id, tag)) return id;
                    }
                }
            }
        }

        return null;
    }

    public string? ResolveTarget(string entityId, string targetSpec)
    {
        if (targetSpec.StartsWith("nearestTagged:"))
        {
            string tag = targetSpec["nearestTagged:".Length..];
            var currentLoc = GetLocation(entityId);
            if (currentLoc == null) return null;

            // Use global search via routing table (falls back to adjacent-only if no routing table)
            return FindNearestLocationWithTag(currentLoc, tag);
        }

        return targetSpec;
    }

    /// <summary>
    /// Returns the LocationDefinition for a given location ID.
    /// </summary>
    public LocationDefinition? GetDefinition(string locationId) =>
        _locations.TryGetValue(locationId, out var loc) ? loc : null;

    /// <summary>
    /// Returns all location IDs.
    /// </summary>
    public IEnumerable<string> AllLocationIds => _locations.Keys;

    /// <summary>
    /// Returns the total number of locations.
    /// </summary>
    public int LocationCount => _locations.Count;

    /// <summary>
    /// Returns the total number of edges across all locations.
    /// </summary>
    public int EdgeCount => _locations.Values.Sum(l => l.ConnectedTo.Count);

    /// <summary>
    /// Returns all location IDs whose ID starts with the given prefix.
    /// Supports dot-notation hierarchy queries (e.g., "city.docks" matches "city.docks.harbor").
    /// </summary>
    public IEnumerable<string> FindLocationsByPrefix(string prefix)
    {
        string prefixDot = prefix.EndsWith('.') ? prefix : prefix + '.';
        foreach (var id in _locations.Keys)
        {
            if (id.StartsWith(prefixDot, StringComparison.Ordinal) || id == prefix)
                yield return id;
        }
    }

    /// <summary>
    /// Extracts the top-level region from a dot-notation location ID.
    /// e.g., "city.docks.harbor" → "city", "hinterland.farmland.fields" → "hinterland"
    /// </summary>
    public static string GetRegion(string locationId)
    {
        int dot = locationId.IndexOf('.');
        return dot >= 0 ? locationId[..dot] : locationId;
    }

    /// <summary>
    /// Extracts the district (second segment) from a dot-notation location ID.
    /// e.g., "city.docks.harbor" → "docks", "sea.lighthouse" → null (only 2 segments)
    /// </summary>
    public static string? GetDistrict(string locationId)
    {
        int first = locationId.IndexOf('.');
        if (first < 0) return null;
        int second = locationId.IndexOf('.', first + 1);
        if (second < 0) return null;
        return locationId[(first + 1)..second];
    }

    // Placeholder — entity tag lookup requires access to EntityRegistry
    private Func<string, string, bool>? _entityTagChecker;
    public void SetEntityTagChecker(Func<string, string, bool> checker) => _entityTagChecker = checker;

    private bool HasEntityTag(string entityId, string tag)
    {
        return _entityTagChecker?.Invoke(entityId, tag) ?? false;
    }
}
