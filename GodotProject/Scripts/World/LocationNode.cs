using Godot;

namespace AutonomeSim.World;

/// <summary>
/// Visual representation of a single location on the 2D map.
/// Dynamically created by WorldSync — no .tscn required.
/// </summary>
public partial class LocationNode : Node2D
{
    public string LocationId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<string> Tags { get; set; } = [];

    private Label _nameLabel = null!;
    private Label _countLabel = null!;
    private ColorRect _background = null!;
    private int _entityCount;
    private bool _isHighlighted;

    private static readonly Vector2 Size = new(140, 80);

    public override void _Ready()
    {
        // Background rect
        _background = new ColorRect
        {
            Size = Size,
            Position = -Size / 2,
            Color = GetDistrictColor()
        };
        AddChild(_background);

        // Name label
        _nameLabel = new Label
        {
            Text = DisplayName,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(-Size.X / 2, Size.Y / 2 + 2),
            Size = new Vector2(Size.X, 20),
        };
        _nameLabel.AddThemeFontSizeOverride("font_size", 11);
        AddChild(_nameLabel);

        // Count label
        _countLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Right,
            Position = new Vector2(-Size.X / 2 + 4, -Size.Y / 2 + 2),
            Size = new Vector2(Size.X - 8, 16),
        };
        _countLabel.AddThemeFontSizeOverride("font_size", 9);
        _countLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        AddChild(_countLabel);
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
            var baseColor = GetDistrictColor();
            _background.Color = on
                ? baseColor.Lerp(new Color(0.05f, 0.72f, 0.87f), 0.3f)
                : baseColor;
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

    private Color GetDistrictColor()
    {
        if (LocationId.StartsWith("sea."))
            return new Color(0.12f, 0.18f, 0.28f);
        if (LocationId.StartsWith("city.docks"))
            return new Color(0.15f, 0.18f, 0.22f);
        if (LocationId.StartsWith("city.slums"))
            return new Color(0.18f, 0.14f, 0.14f);
        if (LocationId.StartsWith("city.market"))
            return new Color(0.22f, 0.20f, 0.12f);
        if (LocationId.StartsWith("city.civic"))
            return new Color(0.15f, 0.18f, 0.22f);
        if (LocationId.StartsWith("city.manor"))
            return new Color(0.20f, 0.16f, 0.22f);
        if (LocationId.StartsWith("city."))
            return new Color(0.17f, 0.17f, 0.19f);
        if (LocationId.StartsWith("hinterland.farmland"))
            return new Color(0.14f, 0.20f, 0.12f);
        if (LocationId.StartsWith("hinterland.quarry"))
            return new Color(0.20f, 0.18f, 0.14f);
        if (LocationId.StartsWith("hinterland.woodlands"))
            return new Color(0.10f, 0.18f, 0.10f);
        if (LocationId.StartsWith("hinterland."))
            return new Color(0.16f, 0.18f, 0.14f);
        return new Color(0.18f, 0.18f, 0.18f);
    }
}
