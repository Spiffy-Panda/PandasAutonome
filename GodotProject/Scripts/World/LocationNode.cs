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
    private ColorRect? _selectionBorder;
    private int _entityCount;
    private bool _isHighlighted;
    private bool _isSelected;
    private Color _customColor = new(0.18f, 0.18f, 0.18f);

    public static readonly Vector2 Size = new(140, 80);

    public override void _Ready()
    {
        // Background rect — ignore mouse so it doesn't block world-space click detection
        _background = new ColorRect
        {
            Size = Size,
            Position = -Size / 2,
            Color = _customColor,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_background);

        // Name label — below the rect, bright white for readability
        _nameLabel = new Label
        {
            Text = DisplayName,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(-Size.X / 2 - 10, Size.Y / 2 + 4),
            Size = new Vector2(Size.X + 20, 22),
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
            Position = new Vector2(-Size.X / 2 + 4, -Size.Y / 2 + 2),
            Size = new Vector2(Size.X - 8, 16),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _countLabel.AddThemeFontSizeOverride("font_size", 10);
        _countLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
        AddChild(_countLabel);
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
                Size = Size + new Vector2(6, 6),
                Position = -(Size + new Vector2(6, 6)) / 2,
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
