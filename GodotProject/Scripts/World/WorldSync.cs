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

    // Home resident tracking — stable slot assignment for residents
    private readonly Dictionary<string, List<string>> _homeResidents = new();

    // Inventory locations — avoid checking all 40 locations each tick
    private readonly HashSet<string> _inventoryLocations = new();

    // Track entities currently performing delivery/hauling actions for food flow viz
    private readonly HashSet<string> _activeHaulers = new();

    // Action IDs/categories that represent food/resource delivery (pulse connection lines)
    private static bool IsDeliveryAction(string actionId) =>
        actionId.StartsWith("deliver_food") || actionId.StartsWith("deliver_metal") ||
        actionId.StartsWith("deliver_tools") || actionId.StartsWith("pickup_harbor") ||
        actionId.StartsWith("pickup_market") || actionId.StartsWith("pickup_quarry") ||
        actionId == "sell_at_market" || actionId == "sell_food" ||
        actionId == "stock_millhaven" || actionId == "haul_materials";

    // Cache location definitions for editor reposition
    private List<LocationDefinition> _allLocations = [];

    // Sky renderer — created dynamically
    private SkyRenderer? _skyRenderer;

    [Signal] public delegate void LayoutChangedEventHandler();

    public IReadOnlyDictionary<string, Vector2> LocationPositions => _locationPositions;
    public IReadOnlyDictionary<string, LocationNode> LocationNodes => _locationNodes;

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
        _bridge.ShipArrived += OnShipArrived;

        // SimulationBridge._Ready() runs before WorldSync._Ready() in the tree,
        // so the signal may have already fired — handle it now if so.
        if (_bridge.IsLoaded)
            OnSimulationLoaded();
    }

    private void OnSimulationLoaded()
    {
        MapLayout.LoadFromFile(_bridge.ResolvedDataPath);
        BuildHomeResidentMap();
        BuildMap();
        SpawnAllNPCs();
        RefreshEntityCounts();
        RefreshInventoryBars();
        CreateSkyRenderer();
        CenterCamera();
    }

    /// <summary>
    /// Build stable home resident mapping from profiles.
    /// </summary>
    private void BuildHomeResidentMap()
    {
        _homeResidents.Clear();
        foreach (var profile in _bridge.Profiles)
        {
            if (!profile.Embodied) continue;
            if (string.IsNullOrEmpty(profile.HomeLocation)) continue;

            if (!_homeResidents.ContainsKey(profile.HomeLocation))
                _homeResidents[profile.HomeLocation] = new();
            _homeResidents[profile.HomeLocation].Add(profile.Id);
        }
        // Sort for stable ordering
        foreach (var list in _homeResidents.Values)
            list.Sort(StringComparer.Ordinal);
    }

    private void CreateSkyRenderer()
    {
        if (_skyRenderer != null) return;
        _skyRenderer = new SkyRenderer { Name = "SkyRenderer" };
        // Use CallDeferred since this may run during _Ready when parent is busy
        var worldMap = _locationsParent.GetParent<Node2D>();
        worldMap.CallDeferred(Node.MethodName.AddChild, _skyRenderer);
        worldMap.CallDeferred(Node.MethodName.MoveChild, _skyRenderer, 0);
    }

    private void CenterCamera()
    {
        if (_locationPositions.Count == 0) return;

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        foreach (var pos in _locationPositions.Values)
        {
            min.X = Mathf.Min(min.X, pos.X);
            min.Y = Mathf.Min(min.Y, pos.Y);
            max.X = Mathf.Max(max.X, pos.X);
            max.Y = Mathf.Max(max.Y, pos.Y);
        }

        var center = (min + max) / 2f;
        var camera = GetNode<Camera2D>("../Camera");
        camera.Position = center;

        // Zoom out enough to see the whole map with padding
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var mapSize = max - min + new Vector2(200, 200); // padding
        float zoomFactor = Mathf.Min(viewportSize.X / mapSize.X, viewportSize.Y / mapSize.Y);
        zoomFactor = Mathf.Clamp(zoomFactor, 0.2f, 1.5f);
        camera.Zoom = new Vector2(zoomFactor, zoomFactor);
    }

    private void BuildMap()
    {
        // Clear existing
        foreach (var child in _locationsParent.GetChildren())
            child.QueueFree();
        _locationNodes.Clear();
        _inventoryLocations.Clear();

        _allLocations = new List<LocationDefinition>();
        foreach (var locId in _bridge.World.Locations.AllLocationIds)
        {
            var def = _bridge.World.Locations.GetDefinition(locId);
            if (def != null) _allLocations.Add(def);
        }

        // Pre-configure location nodes (sizing must happen before _Ready)
        var nodeConfigs = new Dictionary<string, LocationNode>();
        foreach (var loc in _allLocations)
        {
            var node = new LocationNode
            {
                LocationId = loc.Id,
                DisplayName = loc.DisplayName,
                Tags = loc.Tags,
                Name = loc.Id.Replace(".", "_"),
            };

            // Configure inventory properties (must be before AddChild/_Ready)
            var locProps = _bridge.World.LocationStates.Get(loc.Id);
            if (locProps != null && locProps.Count > 0)
            {
                var propList = new List<(string id, float max)>();
                foreach (var (propId, propState) in locProps)
                    propList.Add((propId, propState.Max));
                node.SetInventoryProperties(propList);
                _inventoryLocations.Add(loc.Id);
            }

            // Configure residential sizing
            if (_homeResidents.TryGetValue(loc.Id, out var residents) && residents.Count > 0)
                node.SetResidentCount(residents.Count);

            nodeConfigs[loc.Id] = node;
        }

        // Compute positions
        var positions = MapLayout.GeneratePositions(_allLocations);
        _locationPositions.Clear();

        // Create location nodes
        foreach (var loc in _allLocations)
        {
            var node = nodeConfigs[loc.Id];
            var pos = positions.GetValueOrDefault(loc.Id, Vector2.Zero);
            node.Position = pos;
            _locationPositions[loc.Id] = pos;

            // Set color from layout data
            var district = MapLayout.GetDistrictPrefix(loc.Id);
            node.SetBackgroundColor(MapLayout.GetDistrictColor(district));

            _locationsParent.AddChild(node);
            _locationNodes[loc.Id] = node;
        }

        // Build connection lines
        RebuildConnectionLines();
    }

    private void RebuildConnectionLines()
    {
        var connections = new List<(Vector2, Vector2, int, string?, string?)>();
        var drawn = new HashSet<string>();
        foreach (var loc in _allLocations)
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
                    connections.Add((fromPos, toPos, edge.Cost, loc.Id, edge.Target));
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

    // --- Editor methods ---

    /// <summary>
    /// Reposition all locations within a single district after its anchor changed.
    /// </summary>
    public void RepositionDistrict(string districtPrefix)
    {
        var positions = MapLayout.GeneratePositions(_allLocations);

        foreach (var (locId, node) in _locationNodes)
        {
            if (MapLayout.GetDistrictPrefix(locId) != districtPrefix) continue;
            var newPos = positions.GetValueOrDefault(locId, node.Position);
            node.Position = newPos;
            _locationPositions[locId] = newPos;
        }

        RebuildConnectionLines();
        SnapNPCsToLocations();
        EmitSignal(SignalName.LayoutChanged);
    }

    /// <summary>
    /// Reposition all districts (used after force layout steps).
    /// </summary>
    public void RepositionAllDistricts()
    {
        var positions = MapLayout.GeneratePositions(_allLocations);

        foreach (var (locId, node) in _locationNodes)
        {
            var newPos = positions.GetValueOrDefault(locId, node.Position);
            node.Position = newPos;
            _locationPositions[locId] = newPos;
        }

        RebuildConnectionLines();
        SnapNPCsToLocations();
        EmitSignal(SignalName.LayoutChanged);
    }

    /// <summary>
    /// Reposition a single location node to a new position.
    /// </summary>
    public void RepositionLocation(string locationId, Vector2 newPos)
    {
        if (_locationNodes.TryGetValue(locationId, out var node))
        {
            node.Position = newPos;
            _locationPositions[locationId] = newPos;
        }

        RebuildConnectionLines();
        SnapNPCsToLocations();
        EmitSignal(SignalName.LayoutChanged);
    }

    /// <summary>
    /// Reposition all locations from their saved positions (used after force layout steps).
    /// </summary>
    public void RepositionAllLocations()
    {
        var positions = MapLayout.GeneratePositions(_allLocations);

        foreach (var (locId, node) in _locationNodes)
        {
            var newPos = positions.GetValueOrDefault(locId, node.Position);
            node.Position = newPos;
            _locationPositions[locId] = newPos;
        }

        RebuildConnectionLines();
        SnapNPCsToLocations();
        EmitSignal(SignalName.LayoutChanged);
    }

    /// <summary>
    /// Reposition multiple locations at once (batch, single rebuild).
    /// </summary>
    public void RepositionLocations(IReadOnlyDictionary<string, Vector2> updates)
    {
        foreach (var (locId, newPos) in updates)
        {
            if (_locationNodes.TryGetValue(locId, out var node))
            {
                node.Position = newPos;
                _locationPositions[locId] = newPos;
            }
        }

        RebuildConnectionLines();
        SnapNPCsToLocations();
        EmitSignal(SignalName.LayoutChanged);
    }

    /// <summary>
    /// Update background colors for all locations in a district.
    /// </summary>
    public void UpdateDistrictColors(string districtPrefix)
    {
        var color = MapLayout.GetDistrictColor(districtPrefix);
        foreach (var (locId, node) in _locationNodes)
        {
            if (MapLayout.GetDistrictPrefix(locId) == districtPrefix)
                node.SetBackgroundColor(color);
        }
    }

    /// <summary>
    /// Snap all NPCs to their current location's position (editor reposition, no animation).
    /// </summary>
    private void SnapNPCsToLocations()
    {
        foreach (var (entityId, npc) in _npcNodes)
        {
            var locId = npc.CurrentLocationId;
            if (string.IsNullOrEmpty(locId)) continue;
            if (!_locationPositions.TryGetValue(locId, out var pos)) continue;

            var slotIndex = GetSlotIndex(locId, entityId);
            var slotOffset = _locationNodes.TryGetValue(locId, out var locNode)
                ? locNode.GetSlotPosition(slotIndex)
                : Vector2.Zero;
            npc.SetLocationInstant(pos + slotOffset);
        }
    }

    /// <summary>
    /// Get cross-district edges for force layout. Returns (districtA, districtB, minCost).
    /// </summary>
    public List<(string districtA, string districtB, int cost)> GetCrossDistrictEdges()
    {
        var edges = new Dictionary<string, (string a, string b, int cost)>();
        foreach (var loc in _allLocations)
        {
            var distA = MapLayout.GetDistrictPrefix(loc.Id);
            foreach (var edge in loc.ConnectedTo)
            {
                var distB = MapLayout.GetDistrictPrefix(edge.Target);
                if (distA == distB) continue;

                var key = string.Compare(distA, distB) < 0
                    ? $"{distA}|{distB}" : $"{distB}|{distA}";
                var (a, b) = string.Compare(distA, distB) < 0
                    ? (distA, distB) : (distB, distA);

                if (!edges.ContainsKey(key) || edge.Cost < edges[key].cost)
                    edges[key] = (a, b, edge.Cost);
            }
        }
        return edges.Values.ToList();
    }

    /// <summary>
    /// Get all location-to-location edges with travel costs (deduplicated).
    /// </summary>
    public List<(string locA, string locB, int cost)> GetAllEdges()
    {
        var edges = new List<(string, string, int)>();
        var drawn = new HashSet<string>();
        foreach (var loc in _allLocations)
        {
            foreach (var edge in loc.ConnectedTo)
            {
                var key = string.Compare(loc.Id, edge.Target) < 0
                    ? $"{loc.Id}|{edge.Target}"
                    : $"{edge.Target}|{loc.Id}";
                if (drawn.Contains(key)) continue;
                drawn.Add(key);

                var (a, b) = string.Compare(loc.Id, edge.Target) < 0
                    ? (loc.Id, edge.Target) : (edge.Target, loc.Id);
                edges.Add((a, b, edge.Cost));
            }
        }
        return edges;
    }

    // --- Simulation event handlers ---

    private void OnTickCompleted(int tick, string gameTime)
    {
        RefreshEntityCounts();
        RefreshInventoryBars();
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

        // Pulse connection line when a hauler moves (food flow visualization)
        if (_activeHaulers.Contains(entityId))
            _connectionLines.PulseEdge(fromLocation, toLocation);
    }

    private void OnEntityAction(string entityId, string actionId, string location, float score)
    {
        if (!_npcNodes.TryGetValue(entityId, out var npc)) return;

        var action = _bridge.GetAction(actionId);
        npc.ShowAction(action?.DisplayName ?? actionId);

        // Track haulers for food flow visualization
        if (IsDeliveryAction(actionId))
            _activeHaulers.Add(entityId);
        else
            _activeHaulers.Remove(entityId);
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

    private void OnShipArrived(string vesselName, string cargo, string locationId)
    {
        // Flash the harbor location
        if (_locationNodes.TryGetValue(locationId, out var locNode))
            locNode.Flash(new Color(0.85f, 0.7f, 0.2f, 1f)); // Gold flash

        // Animate a ship marker arriving from off-screen toward the harbor
        if (_locationPositions.TryGetValue(locationId, out var harborPos))
        {
            var ship = new ShipMarker { VesselName = vesselName };
            _locationsParent.AddChild(ship);
            ship.AnimateArrival(harborPos, _bridge.TicksPerSecond);
        }
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
    /// Update inventory bar fill ratios for all inventory locations.
    /// </summary>
    private void RefreshInventoryBars()
    {
        foreach (var locId in _inventoryLocations)
        {
            if (!_locationNodes.TryGetValue(locId, out var node)) continue;
            var props = _bridge.World.LocationStates.Get(locId);
            if (props == null) continue;

            var ratios = new Dictionary<string, float>();
            var rawValues = new Dictionary<string, float>();
            foreach (var (propId, propState) in props)
            {
                float range = propState.Max - propState.Min;
                ratios[propId] = range > 0 ? (propState.Value - propState.Min) / range : 0f;
                rawValues[propId] = propState.Value;
            }
            node.UpdateInventory(ratios, rawValues);
        }
    }

    private int GetSlotIndex(string locationId, string entityId)
    {
        // Home residents get stable fixed slots
        if (_homeResidents.TryGetValue(locationId, out var residents))
        {
            int homeIdx = residents.IndexOf(entityId);
            if (homeIdx >= 0)
                return homeIdx; // Stable home slot

            // Visitor: dynamic slot after all home slots
            if (!_entitiesPerLocation.ContainsKey(locationId))
                _entitiesPerLocation[locationId] = new();

            var visitors = _entitiesPerLocation[locationId];
            int visitorIdx = visitors.IndexOf(entityId);
            if (visitorIdx < 0)
            {
                visitors.Add(entityId);
                visitorIdx = visitors.Count - 1;
            }
            return residents.Count + visitorIdx;
        }

        // Non-residential: dynamic slots as before
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
