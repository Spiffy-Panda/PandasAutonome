using Godot;
using AutonomeSim.Core;
using AutonomeSim.World;

namespace AutonomeSim.UI;

/// <summary>
/// Map layout editor: drag districts, change colors, force-directed layout,
/// grouping boxes, save/load.
/// </summary>
public partial class MapEditor : PanelContainer
{
    private SimulationBridge _bridge = null!;
    private WorldSync _worldSync = null!;
    private GroupingBoxRenderer _groupBoxRenderer = null!;

    // UI controls
    private Button _toggleBtn = null!;
    private VBoxContainer _editorPanel = null!;
    private OptionButton _districtDropdown = null!;
    private CheckButton _lockToggle = null!;
    private ColorPickerButton _colorPicker = null!;
    private Button _diffuseBtn = null!;
    private Button _saveBtn = null!;

    // Group editing
    private VBoxContainer _groupList = null!;
    private Button _addGroupBtn = null!;
    private LineEdit _groupLabelEdit = null!;
    private LineEdit _groupPrefixEdit = null!;
    private ColorPickerButton _groupColorPicker = null!;

    // Selection overlay (world-space box-select drawing)
    private SelectionOverlay _selectionOverlay = null!;

    // Editor state
    private bool _editorActive;
    private readonly List<string> _districtKeys = [];

    // Selection + drag state
    private readonly HashSet<string> _selectedLocationIds = new();
    private bool _isDragging;
    private Dictionary<string, Vector2> _dragOffsets = new();
    private bool _isBoxSelecting;
    private Vector2 _boxSelectStartWorld;

    // Force layout state
    private bool _forceLayoutRunning;
    private int _forceIterations;
    private List<(string locA, string locB, int cost)> _allEdges = [];
    private const int MaxForceIterations = 500;
    private const float ConvergenceThreshold = 0.5f;

    public override void _Ready()
    {
        _bridge = GetNode<SimulationBridge>("/root/Main/SimulationBridge");
        _worldSync = GetNode<WorldSync>("/root/Main/WorldMap/WorldSync");

        // Create world-space overlays
        var worldMap = GetNode<Node2D>("/root/Main/WorldMap");

        _groupBoxRenderer = new GroupingBoxRenderer { Name = "GroupingBoxes" };
        worldMap.AddChild(_groupBoxRenderer);

        _selectionOverlay = new SelectionOverlay { Name = "SelectionOverlay" };
        worldMap.AddChild(_selectionOverlay);

        BuildUI();

        _bridge.SimulationLoaded += OnSimulationLoaded;
        _worldSync.LayoutChanged += RefreshGroupBoxes;

        if (_bridge.IsLoaded)
            OnSimulationLoaded();
    }

    private void OnSimulationLoaded()
    {
        PopulateDistrictDropdown();
        RefreshGroupBoxes();
        RefreshGroupList();
    }

    // --- UI Construction ---

    private void BuildUI()
    {
        // Main toggle button sits outside the panel
        var outerBox = new VBoxContainer();
        AddChild(outerBox);

        _toggleBtn = new Button { Text = "Map Editor", ToggleMode = true };
        _toggleBtn.Toggled += OnToggleEditor;
        outerBox.AddChild(_toggleBtn);

        _editorPanel = new VBoxContainer { Visible = false };
        _editorPanel.AddThemeConstantOverride("separation", 6);
        outerBox.AddChild(_editorPanel);

        // --- District section ---
        _editorPanel.AddChild(new Label { Text = "District" });

        _districtDropdown = new OptionButton { CustomMinimumSize = new Vector2(180, 0) };
        _districtDropdown.ItemSelected += OnDistrictSelected;
        _editorPanel.AddChild(_districtDropdown);

        var districtRow = new HBoxContainer();
        _editorPanel.AddChild(districtRow);

        _lockToggle = new CheckButton { Text = "Locked" };
        _lockToggle.Toggled += OnLockToggled;
        districtRow.AddChild(_lockToggle);

        _colorPicker = new ColorPickerButton
        {
            CustomMinimumSize = new Vector2(40, 30),
            Text = "Color",
        };
        _colorPicker.ColorChanged += OnDistrictColorChanged;
        districtRow.AddChild(_colorPicker);

        // --- Layout section ---
        _editorPanel.AddChild(new HSeparator());

        var layoutRow = new HBoxContainer();
        _editorPanel.AddChild(layoutRow);

        _diffuseBtn = new Button { Text = "Diffuse" };
        _diffuseBtn.Pressed += OnDiffusePressed;
        layoutRow.AddChild(_diffuseBtn);

        _saveBtn = new Button { Text = "Save Layout" };
        _saveBtn.Pressed += OnSavePressed;
        layoutRow.AddChild(_saveBtn);

        // --- Groups section ---
        _editorPanel.AddChild(new HSeparator());
        _editorPanel.AddChild(new Label { Text = "Grouping Boxes" });

        _groupList = new VBoxContainer();
        _editorPanel.AddChild(_groupList);

        var addGroupRow = new HBoxContainer();
        _editorPanel.AddChild(addGroupRow);

        _groupLabelEdit = new LineEdit
        {
            PlaceholderText = "Label",
            CustomMinimumSize = new Vector2(70, 0),
        };
        addGroupRow.AddChild(_groupLabelEdit);

        _groupPrefixEdit = new LineEdit
        {
            PlaceholderText = "Prefix (e.g. city)",
            CustomMinimumSize = new Vector2(100, 0),
        };
        addGroupRow.AddChild(_groupPrefixEdit);

        _groupColorPicker = new ColorPickerButton
        {
            CustomMinimumSize = new Vector2(30, 0),
            Color = new Color(0.3f, 0.4f, 0.6f, 0.12f),
        };
        addGroupRow.AddChild(_groupColorPicker);

        _addGroupBtn = new Button { Text = "+" };
        _addGroupBtn.Pressed += OnAddGroup;
        addGroupRow.AddChild(_addGroupBtn);
    }

    // --- Toggle ---

    private void OnToggleEditor(bool on)
    {
        _editorActive = on;
        _editorPanel.Visible = on;

        if (on)
        {
            ClearSelection();
            RefreshGroupBoxes();
        }
    }

    // --- District dropdown ---

    private void PopulateDistrictDropdown()
    {
        _districtDropdown.Clear();
        _districtKeys.Clear();

        foreach (var district in MapLayout.AllDistricts)
        {
            _districtDropdown.AddItem(district);
            _districtKeys.Add(district);
        }

        if (_districtKeys.Count > 0)
            SyncDistrictUI(0);
    }

    private void OnDistrictSelected(long index)
    {
        SyncDistrictUI((int)index);
    }

    private void SyncDistrictUI(int index)
    {
        if (index < 0 || index >= _districtKeys.Count) return;
        var district = _districtKeys[index];
        _lockToggle.SetPressedNoSignal(MapLayout.IsDistrictLocked(district));
        _colorPicker.Color = MapLayout.GetDistrictColor(district);
    }

    private string? GetSelectedDistrict()
    {
        int idx = _districtDropdown.Selected;
        if (idx < 0 || idx >= _districtKeys.Count) return null;
        return _districtKeys[idx];
    }

    // --- Lock/Color ---

    private void OnLockToggled(bool locked)
    {
        var district = GetSelectedDistrict();
        if (district != null)
            MapLayout.SetDistrictLocked(district, locked);
    }

    private void OnDistrictColorChanged(Color color)
    {
        var district = GetSelectedDistrict();
        if (district == null) return;
        MapLayout.SetDistrictColor(district, color);
        _worldSync.UpdateDistrictColors(district);
    }

    // --- Save ---

    private void OnSavePressed()
    {
        // Snapshot all current positions so every node persists on reload
        foreach (var (locId, pos) in _worldSync.LocationPositions)
            MapLayout.SetLocationPosition(locId, pos);

        MapLayout.SaveToFile(_bridge.ResolvedDataPath);
    }

    // --- Selection + drag (world-space input) ---

    public override void _Input(InputEvent @event)
    {
        if (!_editorActive) return;
        if (_forceLayoutRunning) return;

        if (@event is InputEventMouseButton mb2 && mb2.ButtonIndex == MouseButton.Left)
        {
            // Let HUD controls handle clicks — skip if mouse is over a real UI control
            var hovered = GetViewport().GuiGetHoveredControl();
            if (hovered != null && IsHudControl(hovered))
                return;

            if (mb2.Pressed)
                HandleLeftClickDown();
            else
                HandleLeftClickUp();
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseMotion && (_isDragging || _isBoxSelecting))
        {
            HandleMouseMotion();
        }
    }

    /// <summary>
    /// Returns true if the control is part of the HUD (actual UI), not a world-space label/rect.
    /// </summary>
    private bool IsHudControl(Control control)
    {
        Node? n = control;
        while (n != null)
        {
            if (n is CanvasLayer) return true; // HUD is a CanvasLayer
            n = n.GetParent();
        }
        return false;
    }

    private void HandleLeftClickDown()
    {
        var worldPos = GetWorldMousePos();

        // Check if click hits any location node
        string? hitLocId = null;
        foreach (var (locId, node) in _worldSync.LocationNodes)
        {
            var rect = new Rect2(node.Position - LocationNode.Size / 2, LocationNode.Size);
            if (rect.HasPoint(worldPos))
            {
                hitLocId = locId;
                break;
            }
        }

        if (hitLocId != null)
        {
            if (MapLayout.IsLocationLocked(hitLocId)) return;

            if (!_selectedLocationIds.Contains(hitLocId))
            {
                // Clicked an unselected node — clear selection, select just this one
                ClearSelection();
                _selectedLocationIds.Add(hitLocId);
                UpdateSelectionHighlights();
            }

            // Start dragging all selected nodes
            StartDragSelected(worldPos);
            GetViewport().SetInputAsHandled();
        }
        else
        {
            // Clicked empty space — start box selection
            ClearSelection();
            _isBoxSelecting = true;
            _boxSelectStartWorld = worldPos;
            _selectionOverlay.SelectionRect = null;
            _selectionOverlay.QueueRedraw();
            GetViewport().SetInputAsHandled();
        }
    }

    private void HandleLeftClickUp()
    {
        if (_isBoxSelecting)
        {
            var worldPos = GetWorldMousePos();
            var rect = MakeRect(_boxSelectStartWorld, worldPos);

            // Select all unlocked nodes inside the rect
            foreach (var (locId, node) in _worldSync.LocationNodes)
            {
                if (MapLayout.IsLocationLocked(locId)) continue;
                if (rect.HasPoint(node.Position))
                    _selectedLocationIds.Add(locId);
            }

            _isBoxSelecting = false;
            _selectionOverlay.SelectionRect = null;
            _selectionOverlay.QueueRedraw();
            UpdateSelectionHighlights();
            GetViewport().SetInputAsHandled();
        }
        else if (_isDragging)
        {
            _isDragging = false;
            _dragOffsets.Clear();
            GetViewport().SetInputAsHandled();
        }
    }

    private void HandleMouseMotion()
    {
        if (_isBoxSelecting)
        {
            var worldPos = GetWorldMousePos();
            _selectionOverlay.SelectionRect = MakeRect(_boxSelectStartWorld, worldPos);
            _selectionOverlay.QueueRedraw();
            GetViewport().SetInputAsHandled();
        }
        else if (_isDragging)
        {
            var worldPos = GetWorldMousePos();
            var updates = new Dictionary<string, Vector2>();

            foreach (var (locId, offset) in _dragOffsets)
            {
                var newPos = worldPos + offset;
                MapLayout.SetLocationPosition(locId, newPos);
                updates[locId] = newPos;
            }

            _worldSync.RepositionLocations(updates);
            GetViewport().SetInputAsHandled();
        }
    }

    private void StartDragSelected(Vector2 worldPos)
    {
        _isDragging = true;
        _dragOffsets.Clear();

        foreach (var locId in _selectedLocationIds)
        {
            if (!_worldSync.LocationNodes.TryGetValue(locId, out var node)) continue;
            _dragOffsets[locId] = node.Position - worldPos;
        }
    }

    private void ClearSelection()
    {
        foreach (var locId in _selectedLocationIds)
        {
            if (_worldSync.LocationNodes.TryGetValue(locId, out var node))
                node.SetSelected(false);
        }
        _selectedLocationIds.Clear();
    }

    private void UpdateSelectionHighlights()
    {
        foreach (var (locId, node) in _worldSync.LocationNodes)
            node.SetSelected(_selectedLocationIds.Contains(locId));
    }

    private static Rect2 MakeRect(Vector2 a, Vector2 b)
    {
        var min = new Vector2(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y));
        var max = new Vector2(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y));
        return new Rect2(min, max - min);
    }

    private Vector2 GetWorldMousePos()
    {
        var camera = GetNode<Camera2D>("/root/Main/WorldMap/Camera");
        var viewport = GetViewport();
        var screenPos = viewport.GetMousePosition();
        // Convert screen position to world position accounting for camera
        return camera.GetScreenCenterPosition()
            + (screenPos - viewport.GetVisibleRect().Size / 2) / camera.Zoom;
    }

    // --- Force-directed layout ---

    private void OnDiffusePressed()
    {
        if (_forceLayoutRunning)
        {
            StopForceLayout();
        }
        else
        {
            StartForceLayout();
        }
    }

    private void StartForceLayout()
    {
        _allEdges = _worldSync.GetAllEdges();
        _forceLayoutRunning = true;
        _forceIterations = 0;
        _diffuseBtn.Text = "Stop";
    }

    private void StopForceLayout()
    {
        _forceLayoutRunning = false;
        _diffuseBtn.Text = "Diffuse";
    }

    public override void _Process(double delta)
    {
        if (!_forceLayoutRunning) return;

        ForceLayoutStep();
        _forceIterations++;

        if (_forceIterations >= MaxForceIterations)
            StopForceLayout();
    }

    private void ForceLayoutStep()
    {
        const float repulsionK = 30000f;
        const float springK = 0.002f;
        const float damping = 0.85f;
        const float minDist = 80f;
        const float maxDisplacement = 30f;
        const float costToPixelScale = 120f;

        var locationIds = _worldSync.LocationPositions.Keys.ToList();
        var forces = new Dictionary<string, Vector2>();

        foreach (var id in locationIds)
            forces[id] = Vector2.Zero;

        // Repulsion between all pairs
        for (int i = 0; i < locationIds.Count; i++)
        {
            if (MapLayout.IsLocationLocked(locationIds[i])) continue;
            for (int j = i + 1; j < locationIds.Count; j++)
            {
                var posA = _worldSync.LocationPositions[locationIds[i]];
                var posB = _worldSync.LocationPositions[locationIds[j]];
                var diff = posA - posB;
                float dist = diff.Length();
                if (dist < minDist) dist = minDist;
                var force = diff.Normalized() * repulsionK / (dist * dist);

                if (!MapLayout.IsLocationLocked(locationIds[i]))
                    forces[locationIds[i]] += force;
                if (!MapLayout.IsLocationLocked(locationIds[j]))
                    forces[locationIds[j]] -= force;
            }
        }

        // Spring attraction along edges (proportional to travel cost)
        foreach (var (locA, locB, cost) in _allEdges)
        {
            if (!forces.ContainsKey(locA) || !forces.ContainsKey(locB)) continue;

            var posA = _worldSync.LocationPositions[locA];
            var posB = _worldSync.LocationPositions[locB];
            var diff = posB - posA;
            float dist = diff.Length();
            float idealDist = cost * costToPixelScale;
            float displacement = dist - idealDist;
            var springForce = diff.Normalized() * displacement * springK;

            if (!MapLayout.IsLocationLocked(locA))
                forces[locA] += springForce;
            if (!MapLayout.IsLocationLocked(locB))
                forces[locB] -= springForce;
        }

        // Apply forces
        float totalDisplacement = 0;
        foreach (var locId in locationIds)
        {
            if (MapLayout.IsLocationLocked(locId)) continue;
            var clamped = forces[locId].LimitLength(maxDisplacement) * damping;
            var newPos = _worldSync.LocationPositions[locId] + clamped;
            MapLayout.SetLocationPosition(locId, newPos);
            totalDisplacement += clamped.Length();
        }

        _worldSync.RepositionAllLocations();

        // Auto-stop on convergence
        if (totalDisplacement < ConvergenceThreshold)
            StopForceLayout();
    }

    // --- Grouping boxes ---

    private void RefreshGroupBoxes()
    {
        if (_groupBoxRenderer != null)
            _groupBoxRenderer.UpdateBoxes(_worldSync.LocationPositions, MapLayout.Layout.Groups);
    }

    private void RefreshGroupList()
    {
        foreach (var child in _groupList.GetChildren())
            child.QueueFree();

        for (int i = 0; i < MapLayout.Layout.Groups.Count; i++)
        {
            var group = MapLayout.Layout.Groups[i];
            var row = new HBoxContainer();

            var label = new Label
            {
                Text = $"{group.Label} [{group.MatchPrefix}]",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            label.AddThemeFontSizeOverride("font_size", 11);
            row.AddChild(label);

            int capturedIndex = i;
            var removeBtn = new Button { Text = "x" };
            removeBtn.Pressed += () => OnRemoveGroup(capturedIndex);
            row.AddChild(removeBtn);

            _groupList.AddChild(row);
        }
    }

    private void OnAddGroup()
    {
        var label = _groupLabelEdit.Text.Trim();
        var prefix = _groupPrefixEdit.Text.Trim();
        if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(prefix)) return;

        var color = _groupColorPicker.Color;
        MapLayout.Layout.Groups.Add(new GroupBoxData
        {
            Label = label,
            MatchPrefix = prefix,
            Color = [color.R, color.G, color.B, color.A],
            Padding = 50f,
        });

        _groupLabelEdit.Text = "";
        _groupPrefixEdit.Text = "";
        RefreshGroupList();
        RefreshGroupBoxes();
    }

    private void OnRemoveGroup(int index)
    {
        if (index >= 0 && index < MapLayout.Layout.Groups.Count)
        {
            MapLayout.Layout.Groups.RemoveAt(index);
            RefreshGroupList();
            RefreshGroupBoxes();
        }
    }
}
