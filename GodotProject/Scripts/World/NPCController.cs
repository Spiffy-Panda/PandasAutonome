using Godot;

namespace AutonomeSim.World;

/// <summary>
/// Visual representation of a single NPC on the 2D map.
/// A colored circle that moves between locations.
/// </summary>
public partial class NPCController : Node2D
{
    public string EntityId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string CurrentLocationId { get; set; } = "";
    public bool IsPossessed { get; set; }

    private Label _nameLabel = null!;
    private Label _actionLabel = null!;
    private Color _color = Colors.Gray;
    private bool _isMoving;
    private Tween? _moveTween;
    private string? _lastActionId;

    private const float CircleRadius = 8f;

    public override void _Ready()
    {
        ZIndex = 1; // Draw above locations

        _nameLabel = new Label
        {
            Text = DisplayName,
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(-50, -CircleRadius - 16),
            Size = new Vector2(100, 14),
        };
        _nameLabel.AddThemeFontSizeOverride("font_size", 9);
        _nameLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f, 0.8f));
        _nameLabel.Visible = false; // Hidden by default, shown when zoomed in
        AddChild(_nameLabel);

        _actionLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(-40, CircleRadius + 1),
            Size = new Vector2(80, 12),
        };
        _actionLabel.AddThemeFontSizeOverride("font_size", 8);
        _actionLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 0.6f, 0.7f));
        _actionLabel.Visible = false;
        AddChild(_actionLabel);

        QueueRedraw();
    }

    public override void _Draw()
    {
        // Draw circle
        DrawCircle(Vector2.Zero, CircleRadius, _color);

        // Possessed highlight ring
        if (IsPossessed)
        {
            DrawArc(Vector2.Zero, CircleRadius + 3, 0, Mathf.Tau, 32,
                new Color(0.05f, 0.72f, 0.87f), 2f, true);
        }
    }

    public void SetColor(Color color)
    {
        _color = color;
        QueueRedraw();
    }

    public void SetPossessed(bool possessed)
    {
        IsPossessed = possessed;
        _nameLabel.Visible = possessed; // Always show name for possessed NPC
        QueueRedraw();
    }

    public void SetNameVisible(bool visible)
    {
        if (!IsPossessed) // Don't hide possessed NPC's name
            _nameLabel.Visible = visible;
    }

    public void ShowAction(string? actionName)
    {
        _lastActionId = actionName;
        if (actionName != null && _actionLabel != null)
        {
            _actionLabel.Text = actionName;
            _actionLabel.Visible = true;

            // Auto-hide after a moment via tween
            var tween = CreateTween();
            tween.TweenInterval(1.5);
            tween.TweenProperty(_actionLabel, "modulate:a", 0.0f, 0.5f);
            tween.TweenCallback(Callable.From(() =>
            {
                _actionLabel.Visible = false;
                _actionLabel.Modulate = Colors.White;
            }));
        }
    }

    /// <summary>
    /// Animate movement to a new world position.
    /// </summary>
    public void MoveTo(Vector2 targetPosition, float durationSeconds)
    {
        if (_isMoving && _moveTween?.IsValid() == true)
        {
            _moveTween.Kill();
        }

        _isMoving = true;
        _moveTween = CreateTween();
        var tween = _moveTween;
        tween.TweenProperty(this, "global_position", targetPosition,
            Mathf.Max(durationSeconds, 0.05f))
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenCallback(Callable.From(() => _isMoving = false));
    }

    /// <summary>
    /// Instantly set position (no animation).
    /// </summary>
    public void SetLocationInstant(Vector2 worldPosition)
    {
        GlobalPosition = worldPosition;
        _isMoving = false;
    }

    /// <summary>
    /// Determine NPC color from their tags/identity.
    /// </summary>
    public static Color GetColorForTags(IEnumerable<string>? tags)
    {
        if (tags == null) return Colors.Gray;
        var tagSet = new HashSet<string>(tags);

        if (tagSet.Contains("guard") || tagSet.Contains("military"))
            return new Color(0.2f, 0.3f, 0.7f);     // Blue — guards
        if (tagSet.Contains("merchant") || tagSet.Contains("trader"))
            return new Color(0.85f, 0.7f, 0.2f);     // Gold — merchants
        if (tagSet.Contains("fisher") || tagSet.Contains("sailor"))
            return new Color(0.2f, 0.6f, 0.65f);     // Teal — seafolk
        if (tagSet.Contains("farmer") || tagSet.Contains("agriculture"))
            return new Color(0.3f, 0.65f, 0.2f);     // Green — farmers
        if (tagSet.Contains("thief") || tagSet.Contains("criminal"))
            return new Color(0.4f, 0.2f, 0.5f);      // Purple — thieves
        if (tagSet.Contains("priest") || tagSet.Contains("clergy"))
            return new Color(0.85f, 0.85f, 0.85f);   // White — clergy
        if (tagSet.Contains("noble") || tagSet.Contains("official"))
            return new Color(0.7f, 0.2f, 0.2f);      // Dark red — officials
        if (tagSet.Contains("craftsman") || tagSet.Contains("smith"))
            return new Color(0.65f, 0.45f, 0.2f);    // Brown — craftsmen
        if (tagSet.Contains("laborer") || tagSet.Contains("miner"))
            return new Color(0.55f, 0.45f, 0.35f);   // Tan — laborers
        if (tagSet.Contains("ranger") || tagSet.Contains("woodcutter"))
            return new Color(0.2f, 0.5f, 0.3f);      // Forest green — woodsfolk

        return new Color(0.5f, 0.5f, 0.5f);          // Gray — default
    }
}
