using Godot;
using Autonome.Core.Model;
using AutonomeSim.Core;

namespace AutonomeSim.World;

/// <summary>
/// Synchronizes the Godot scene tree with simulation state.
/// Creates location nodes, spawns NPC tokens, and updates positions each tick.
/// </summary>
public partial class WorldSync : Node
{
    private SimulationBridge _bridge = null!;
    private Node2D _locationsParent = null!;
    private ConnectionLines _connectionLines = null!;

    private readonly Dictionary<string, LocationNode> _locationNodes = new();
    private readonly Dictionary<string, NPCController> _npcNodes = new();
    private readonly Dictionary<string, Vector2> _locationPositions = new();

    // Track NPC-to-location for slot assignment
    private readonly Dictionary<string, List<string>> _entitiesPerLocation = new();

    public override void _Ready()
    {
        _bridge = GetNode<SimulationBridge>("/root/Main/SimulationBridge");
        _locationsParent = GetNode<Node2D>("../Locations");
        _connectionLines = GetNode<ConnectionLines>("../ConnectionLines");

        _bridge.SimulationLoaded += OnSimulationLoaded;
        _bridge.TickCompleted += OnTickCompleted;
        _bridge.EntityMoved += OnEntityMoved;
        _bridge.EntityAction += OnEntityAction;
        _bridge.EntityPossessed += OnEntityPossessed;
        _bridge.EntityReleased += OnEntityReleased;
    }

    private void OnSimulationLoaded()
    {
        BuildMap();
        SpawnAllNPCs();
        RefreshEntityCounts();
    }

    private void BuildMap()
    {
        // Clear existing
        foreach (var child in _locationsParent.GetChildren())
            child.QueueFree();
        _locationNodes.Clear();

        var allLocations = new List<LocationDefinition>();
        foreach (var locId in _bridge.World.Locations.AllLocationIds)
        {
            var def = _bridge.World.Locations.GetDefinition(locId);
            if (def != null) allLocations.Add(def);
        }

        // Compute positions
        var positions = MapLayout.GeneratePositions(allLocations);
        _locationPositions.Clear();

        // Create location nodes
        foreach (var loc in allLocations)
        {
            var node = new LocationNode
            {
                LocationId = loc.Id,
                DisplayName = loc.DisplayName,
                Tags = loc.Tags,
                Name = loc.Id.Replace(".", "_"),
            };

            var pos = positions.GetValueOrDefault(loc.Id, Vector2.Zero);
            node.Position = pos;
            _locationPositions[loc.Id] = pos;

            _locationsParent.AddChild(node);
            _locationNodes[loc.Id] = node;
        }

        // Build connection lines
        var connections = new List<(Vector2, Vector2, int)>();
        var drawn = new HashSet<string>(); // Prevent duplicate bidirectional lines
        foreach (var loc in allLocations)
        {
            var fromPos = _locationPositions.GetValueOrDefault(loc.Id);
            foreach (var edge in loc.ConnectedTo)
            {
                var key = string.Compare(loc.Id, edge.Target) < 0
                    ? $"{loc.Id}|{edge.Target}"
                    : $"{edge.Target}|{loc.Id}";
                if (drawn.Contains(key)) continue;
                drawn.Add(key);

                var toPos = _locationPositions.GetValueOrDefault(edge.Target);
                if (toPos != Vector2.Zero)
                    connections.Add((fromPos, toPos, edge.Cost));
            }
        }
        _connectionLines.SetConnections(connections);
    }

    private void SpawnAllNPCs()
    {
        // Clear existing
        foreach (var npc in _npcNodes.Values)
            npc.QueueFree();
        _npcNodes.Clear();
        _entitiesPerLocation.Clear();

        foreach (var profile in _bridge.Profiles)
        {
            if (!profile.Embodied) continue;

            var npc = new NPCController
            {
                EntityId = profile.Id,
                DisplayName = profile.DisplayName,
                Name = profile.Id.Replace(".", "_"),
            };

            // Set color based on tags
            var tags = profile.Identity?.Tags;
            npc.SetColor(NPCController.GetColorForTags(tags));

            // Place at initial location
            var locId = _bridge.GetEntityLocation(profile.Id);
            npc.CurrentLocationId = locId ?? "";
            if (locId != null && _locationPositions.TryGetValue(locId, out var pos))
            {
                var slotIndex = GetSlotIndex(locId, profile.Id);
                var slotOffset = _locationNodes.TryGetValue(locId, out var locNode)
                    ? locNode.GetSlotPosition(slotIndex)
                    : Vector2.Zero;
                npc.SetLocationInstant(pos + slotOffset);
            }

            // Add as child of the map (not individual locations, for smooth movement)
            _locationsParent.AddChild(npc);
            _npcNodes[profile.Id] = npc;
        }
    }

    private void OnTickCompleted(int tick, string gameTime)
    {
        RefreshEntityCounts();
    }

    private void OnEntityMoved(string entityId, string fromLocation, string toLocation)
    {
        if (!_npcNodes.TryGetValue(entityId, out var npc)) return;

        npc.CurrentLocationId = toLocation;
        RemoveFromLocationSlot(fromLocation, entityId);

        if (_locationPositions.TryGetValue(toLocation, out var pos))
        {
            var slotIndex = GetSlotIndex(toLocation, entityId);
            var slotOffset = _locationNodes.TryGetValue(toLocation, out var locNode)
                ? locNode.GetSlotPosition(slotIndex)
                : Vector2.Zero;

            // Animate movement — duration based on tick speed
            float duration = _bridge.TicksPerSecond > 0 ? 1.0f / _bridge.TicksPerSecond : 0.5f;
            npc.MoveTo(pos + slotOffset, Mathf.Clamp(duration, 0.05f, 2f));
        }
    }

    private void OnEntityAction(string entityId, string actionId, string location, float score)
    {
        if (!_npcNodes.TryGetValue(entityId, out var npc)) return;

        var action = _bridge.GetAction(actionId);
        npc.ShowAction(action?.DisplayName ?? actionId);
    }

    private void OnEntityPossessed(string entityId)
    {
        if (_npcNodes.TryGetValue(entityId, out var npc))
            npc.SetPossessed(true);
    }

    private void OnEntityReleased(string entityId)
    {
        if (_npcNodes.TryGetValue(entityId, out var npc))
            npc.SetPossessed(false);
    }

    private void RefreshEntityCounts()
    {
        // Rebuild per-location counts
        var counts = new Dictionary<string, int>();
        foreach (var profile in _bridge.Profiles)
        {
            if (!profile.Embodied) continue;
            var loc = _bridge.GetEntityLocation(profile.Id);
            if (loc == null) continue;
            counts[loc] = counts.GetValueOrDefault(loc) + 1;
        }

        foreach (var (locId, node) in _locationNodes)
            node.UpdateCount(counts.GetValueOrDefault(locId));

        // Highlight possessed entity's location
        if (_bridge.PossessedEntityId != null)
        {
            var possLoc = _bridge.GetEntityLocation(_bridge.PossessedEntityId);
            foreach (var (locId, node) in _locationNodes)
                node.SetHighlight(locId == possLoc);
        }
    }

    /// <summary>
    /// Returns a slot index for the given entity at the given location.
    /// </summary>
    private int GetSlotIndex(string locationId, string entityId)
    {
        if (!_entitiesPerLocation.ContainsKey(locationId))
            _entitiesPerLocation[locationId] = new();

        var list = _entitiesPerLocation[locationId];
        int idx = list.IndexOf(entityId);
        if (idx < 0)
        {
            list.Add(entityId);
            idx = list.Count - 1;
        }
        return idx;
    }

    private void RemoveFromLocationSlot(string locationId, string entityId)
    {
        if (_entitiesPerLocation.TryGetValue(locationId, out var list))
            list.Remove(entityId);
    }

    public LocationNode? GetLocationNode(string locationId)
        => _locationNodes.GetValueOrDefault(locationId);

    public NPCController? GetNPCNode(string entityId)
        => _npcNodes.GetValueOrDefault(entityId);

    public Vector2 GetLocationPosition(string locationId)
        => _locationPositions.GetValueOrDefault(locationId);
}
