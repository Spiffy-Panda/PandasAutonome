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

    // Inventory bars
    private readonly List<InventoryBar> _inventoryBars = [];
    private bool _hasInventory;

    // Residential
    private int _residentCount;

    private record InventoryBar(string PropertyId, string Label, Color BarColor, float Max)
    {
        public float FillRatio { get; set; }
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

        if (_hasInventory)
            QueueRedraw();
    }

    /// <summary>
    /// Configure this location as an inventory location with resource bars.
    /// Must be called before _Ready (before AddChild in WorldSync.BuildMap).
    /// </summary>
    public void SetInventoryProperties(IEnumerable<(string id, float max)> properties)
    {
        _inventoryBars.Clear();
        foreach (var (id, max) in properties)
        {
            if (!InventoryVisuals.TryGetValue(id, out var vis)) continue;
            _inventoryBars.Add(new InventoryBar(id, vis.label, vis.color, max));
        }

        _hasInventory = _inventoryBars.Count > 0;
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
            height = Mathf.Max(height, 60 + _inventoryBars.Count * 10 + 20);
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
        foreach (var bar in _inventoryBars)
        {
            if (fillRatios.TryGetValue(bar.PropertyId, out var ratio))
                bar.FillRatio = Mathf.Clamp(ratio, 0f, 1f);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_hasInventory) return;

        // Draw inventory bars in the lower portion of the tile
        float barWidth = NodeSize.X - 40f;
        float barHeight = 6f;
        float gap = 2f;
        float totalBarHeight = _inventoryBars.Count * (barHeight + gap) - gap;
        float startY = NodeSize.Y / 2 - totalBarHeight - 6f;
        float barX = -NodeSize.X / 2 + 30f;

        for (int i = 0; i < _inventoryBars.Count; i++)
        {
            var bar = _inventoryBars[i];
            float y = startY + i * (barHeight + gap);

            // Background (empty bar)
            var bgRect = new Rect2(barX, y, barWidth, barHeight);
            DrawRect(bgRect, new Color(0.1f, 0.1f, 0.1f, 0.6f));

            // Filled portion
            if (bar.FillRatio > 0f)
            {
                var fillRect = new Rect2(barX, y, barWidth * bar.FillRatio, barHeight);
                DrawRect(fillRect, bar.BarColor);
            }

            // Label
            DrawString(ThemeDB.FallbackFont,
                new Vector2(-NodeSize.X / 2 + 4f, y + barHeight),
                bar.Label, HorizontalAlignment.Left, 26, 8,
                new Color(0.7f, 0.7f, 0.7f, 0.8f));
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
