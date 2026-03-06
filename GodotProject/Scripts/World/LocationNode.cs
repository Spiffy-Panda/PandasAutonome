using Godot;

namespace AutonomeSim.World;

/// <summary>
/// Visual representation of a single location on the 2D map.
/// Dynamically created by WorldSync — no .tscn required.
/// Supports dynamic sizing for inventory locations and residential areas.
/// </summary>
public partial class LocationNode : Node2D
{
    public string LocationId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> Tags { get; set; } = [];

    private Label _nameLabel = null!;
    private Label _countLabel = null!;
    private ColorRect _background = null!;
    private ColorRect? _selectionBorder;
    private int _entityCount;
    private bool _isHighlighted;
    private bool _isSelected;
    private Color _customColor = new(0.18f, 0.18f, 0.18f);

    // Dynamic sizing
    public static readonly Vector2 DefaultSize = new(140, 80);
    public Vector2 NodeSize { get; private set; } = DefaultSize;

    // Inventory bars — built as child nodes so they draw ON TOP of background
    private readonly List<InventoryBarDef> _inventoryDefs = [];
    private readonly List<InventoryBarNodes> _inventoryBarNodes = [];
    private bool _hasInventory;

    // Residential
    private int _residentCount;

    private record InventoryBarDef(string PropertyId, string Label, Color BarColor, float Max);

    private class InventoryBarNodes
    {
        public ColorRect Background = null!;
        public ColorRect Fill = null!;
        public Label Label = null!;
    }

    private static readonly Dictionary<string, (string label, Color color)> InventoryVisuals = new()
    {
        ["food_supply"] = ("Fod", new Color(0.3f, 0.7f, 0.2f)),
        ["ore_supply"] = ("Ore", new Color(0.5f, 0.5f, 0.55f)),
        ["tool_supply"] = ("Tol", new Color(0.6f, 0.4f, 0.2f)),
        ["metal_supply"] = ("Met", new Color(0.7f, 0.7f, 0.75f)),
        ["gold"] = ("Gld", new Color(0.85f, 0.7f, 0.2f)),
    };

    public override void _Ready()
    {
        // Background rect — ignore mouse so it doesn't block world-space click detection
        _background = new ColorRect
        {
            Size = NodeSize,
            Position = -NodeSize / 2,
            Color = _customColor,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_background);

        // Build inventory bar child nodes ON TOP of background
        if (_hasInventory)
            BuildInventoryBars();

        // Name label — below the rect, bright white for readability
        _nameLabel = new Label
        {
            Text = DisplayName,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(-NodeSize.X / 2 - 10, NodeSize.Y / 2 + 4),
            Size = new Vector2(NodeSize.X + 20, 22),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _nameLabel.AddThemeFontSizeOverride("font_size", 13);
        _nameLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        AddChild(_nameLabel);

        // Count label — top-right corner, brighter
        _countLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Right,
            Position = new Vector2(-NodeSize.X / 2 + 4, -NodeSize.Y / 2 + 2),
            Size = new Vector2(NodeSize.X - 8, 16),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _countLabel.AddThemeFontSizeOverride("font_size", 10);
        _countLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
        AddChild(_countLabel);
    }

    private void BuildInventoryBars()
    {
        float barWidth = NodeSize.X - 40f;
        float barHeight = 6f;
        float gap = 2f;
        float totalBarHeight = _inventoryDefs.Count * (barHeight + gap) - gap;
        float startY = NodeSize.Y / 2 - totalBarHeight - 6f;
        float barX = -NodeSize.X / 2 + 30f;

        for (int i = 0; i < _inventoryDefs.Count; i++)
        {
            var def = _inventoryDefs[i];
            float y = startY + i * (barHeight + gap);

            // Bar background (dark track)
            var bg = new ColorRect
            {
                Position = new Vector2(barX, y),
                Size = new Vector2(barWidth, barHeight),
                Color = new Color(0.08f, 0.08f, 0.08f, 0.8f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            AddChild(bg);

            // Fill rect (colored, starts at 0 width)
            var fill = new ColorRect
            {
                Position = new Vector2(barX, y),
                Size = new Vector2(0, barHeight),
                Color = def.BarColor,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            AddChild(fill);

            // Label
            var label = new Label
            {
                Text = def.Label,
                Position = new Vector2(-NodeSize.X / 2 + 2f, y - 2f),
                Size = new Vector2(28, 12),
                HorizontalAlignment = HorizontalAlignment.Left,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            label.AddThemeFontSizeOverride("font_size", 8);
            label.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f, 0.9f));
            AddChild(label);

            _inventoryBarNodes.Add(new InventoryBarNodes
            {
                Background = bg,
                Fill = fill,
                Label = label,
            });
        }
    }

    /// <summary>
    /// Configure this location as an inventory location with resource bars.
    /// Must be called before _Ready (before AddChild in WorldSync.BuildMap).
    /// </summary>
    public void SetInventoryProperties(IEnumerable<(string id, float max)> properties)
    {
        _inventoryDefs.Clear();
        foreach (var (id, max) in properties)
        {
            if (!InventoryVisuals.TryGetValue(id, out var vis)) continue;
            _inventoryDefs.Add(new InventoryBarDef(id, vis.label, vis.color, max));
        }

        _hasInventory = _inventoryDefs.Count > 0;
        RecalculateSize();
    }

    /// <summary>
    /// Configure this location as residential with a fixed resident count.
    /// Must be called before _Ready.
    /// </summary>
    public void SetResidentCount(int count)
    {
        _residentCount = count;
        RecalculateSize();
    }

    private void RecalculateSize()
    {
        float width = DefaultSize.X;
        float height = DefaultSize.Y;

        // Inventory: wider + taller for bars
        if (_hasInventory)
        {
            width = Mathf.Max(width, 200);
            height = Mathf.Max(height, 60 + _inventoryDefs.Count * 10 + 20);
        }

        // Residential: scale to fit resident slots
        if (_residentCount > 0)
        {
            int cols = 5;
            int rows = (_residentCount + cols - 1) / cols;
            float neededWidth = cols * 22f + 20f;
            float neededHeight = rows * 22f + 40f;
            width = Mathf.Max(width, neededWidth);
            height = Mathf.Max(height, neededHeight);
        }

        // Cap at reasonable max
        width = Mathf.Min(width, 320);
        height = Mathf.Min(height, 200);

        NodeSize = new Vector2(width, height);
    }

    /// <summary>
    /// Update inventory bar fill ratios. Called each tick by WorldSync.
    /// </summary>
    public void UpdateInventory(Dictionary<string, float> fillRatios)
    {
        if (_inventoryBarNodes.Count == 0) return;

        float barWidth = NodeSize.X - 40f;
        for (int i = 0; i < _inventoryDefs.Count && i < _inventoryBarNodes.Count; i++)
        {
            var def = _inventoryDefs[i];
            var nodes = _inventoryBarNodes[i];

            if (fillRatios.TryGetValue(def.PropertyId, out var ratio))
            {
                ratio = Mathf.Clamp(ratio, 0f, 1f);
                nodes.Fill.Size = new Vector2(barWidth * ratio, nodes.Fill.Size.Y);
            }
        }
    }

    public void SetBackgroundColor(Color color)
    {
        _customColor = color;
        if (_background != null)
            _background.Color = _isHighlighted
                ? color.Lerp(new Color(0.05f, 0.72f, 0.87f), 0.3f)
                : color;
    }

    public void UpdateCount(int count)
    {
        _entityCount = count;
        if (_countLabel != null)
            _countLabel.Text = count > 0 ? $"{count}" : "";
    }

    public void SetHighlight(bool on)
    {
        _isHighlighted = on;
        if (_background != null)
        {
            _background.Color = on
                ? _customColor.Lerp(new Color(0.05f, 0.72f, 0.87f), 0.3f)
                : _customColor;
        }
    }

    public void SetSelected(bool on)
    {
        _isSelected = on;
        if (on && _selectionBorder == null)
        {
            _selectionBorder = new ColorRect
            {
                Size = NodeSize + new Vector2(6, 6),
                Position = -(NodeSize + new Vector2(6, 6)) / 2,
                Color = new Color(0.3f, 0.7f, 1.0f, 0.4f),
                ZIndex = -1,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            AddChild(_selectionBorder);
        }
        else if (!on && _selectionBorder != null)
        {
            _selectionBorder.QueueFree();
            _selectionBorder = null;
        }
    }

    /// <summary>
    /// Returns a position offset for the Nth NPC at this location.
    /// Arranged in a grid within the location bounds.
    /// </summary>
    public Vector2 GetSlotPosition(int index)
    {
        int cols = 5;
        int row = index / cols;
        int col = index % cols;
        float spacing = 22f;
        float startX = -(cols - 1) * spacing / 2f;
        float startY = -20f;
        return new Vector2(startX + col * spacing, startY + row * spacing);
    }
}
