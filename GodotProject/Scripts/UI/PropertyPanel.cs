using Godot;
using AutonomeSim.Core;

namespace AutonomeSim.UI;

/// <summary>
/// Shows the possessed entity's properties as horizontal bars.
/// </summary>
public partial class PropertyPanel : PanelContainer
{
    private SimulationBridge _bridge = null!;
    private VBoxContainer _barsContainer = null!;
    private Label _entityNameLabel = null!;
    private Label _locationLabel = null!;
    private string? _boundEntityId;

    private readonly Dictionary<string, (ProgressBar bar, Label valueLabel)> _bars = new();

    // Properties to display in order (vital first)
    private static readonly string[] VitalProps = ["hunger", "rest"];
    private static readonly string[] ImportantProps = ["gold", "mood", "social"];

    public override void _Ready()
    {
        _bridge = GetNode<SimulationBridge>("/root/Main/SimulationBridge");

        var vbox = new VBoxContainer();
        AddChild(vbox);

        _entityNameLabel = new Label { Text = "No entity selected" };
        _entityNameLabel.AddThemeFontSizeOverride("font_size", 14);
        vbox.AddChild(_entityNameLabel);

        _locationLabel = new Label { Text = "" };
        _locationLabel.AddThemeFontSizeOverride("font_size", 11);
        _locationLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        vbox.AddChild(_locationLabel);

        vbox.AddChild(new HSeparator());

        _barsContainer = new VBoxContainer();
        vbox.AddChild(_barsContainer);

        CustomMinimumSize = new Vector2(260, 0);

        _bridge.EntityPossessed += OnPossessed;
        _bridge.EntityReleased += OnReleased;
        _bridge.TickCompleted += OnTick;
    }

    private void OnPossessed(string entityId)
    {
        _boundEntityId = entityId;
        RebuildBars();
        Refresh();
    }

    private void OnReleased(string entityId)
    {
        _boundEntityId = null;
        _entityNameLabel.Text = "No entity selected";
        _locationLabel.Text = "";
        ClearBars();
    }

    private void OnTick(int tick, string gameTime)
    {
        if (_boundEntityId != null)
            Refresh();
    }

    private void RebuildBars()
    {
        ClearBars();
        if (_boundEntityId == null) return;

        var state = _bridge.GetEntityState(_boundEntityId);
        if (state == null) return;

        // Determine display order: vital, important, then remaining
        var ordered = new List<string>();
        foreach (var p in VitalProps)
            if (state.Properties.ContainsKey(p)) ordered.Add(p);
        foreach (var p in ImportantProps)
            if (state.Properties.ContainsKey(p)) ordered.Add(p);
        foreach (var key in state.Properties.Keys.OrderBy(k => k))
            if (!ordered.Contains(key)) ordered.Add(key);

        foreach (var propId in ordered)
        {
            var prop = state.Properties[propId];

            var row = new HBoxContainer();
            var nameLabel = new Label
            {
                Text = propId,
                CustomMinimumSize = new Vector2(70, 0),
            };
            nameLabel.AddThemeFontSizeOverride("font_size", 11);

            var bar = new ProgressBar
            {
                MinValue = prop.Min,
                MaxValue = prop.Max,
                Value = prop.Value,
                CustomMinimumSize = new Vector2(120, 18),
                ShowPercentage = false,
            };

            var valueLabel = new Label
            {
                Text = FormatValue(prop.Value, prop.Max),
                CustomMinimumSize = new Vector2(50, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            valueLabel.AddThemeFontSizeOverride("font_size", 10);

            row.AddChild(nameLabel);
            row.AddChild(bar);
            row.AddChild(valueLabel);
            _barsContainer.AddChild(row);

            _bars[propId] = (bar, valueLabel);
        }
    }

    private void Refresh()
    {
        if (_boundEntityId == null) return;

        var profile = _bridge.GetProfile(_boundEntityId);
        var state = _bridge.GetEntityState(_boundEntityId);
        var location = _bridge.GetEntityLocation(_boundEntityId);

        if (profile != null)
            _entityNameLabel.Text = profile.DisplayName;
        if (location != null)
        {
            var locDef = _bridge.World.Locations.GetDefinition(location);
            _locationLabel.Text = locDef?.DisplayName ?? location;
        }

        if (state == null) return;

        foreach (var (propId, (bar, valueLabel)) in _bars)
        {
            if (!state.Properties.TryGetValue(propId, out var prop)) continue;
            bar.Value = prop.Value;
            valueLabel.Text = FormatValue(prop.Value, prop.Max);

            // Color based on normalized value
            float normalized = prop.Max > prop.Min
                ? (prop.Value - prop.Min) / (prop.Max - prop.Min)
                : 0;

            var styleBox = new StyleBoxFlat();
            if (normalized > 0.6f)
                styleBox.BgColor = new Color(0.2f, 0.6f, 0.2f);
            else if (normalized > 0.3f)
                styleBox.BgColor = new Color(0.7f, 0.6f, 0.1f);
            else
                styleBox.BgColor = new Color(0.7f, 0.2f, 0.2f);
            bar.AddThemeStyleboxOverride("fill", styleBox);
        }
    }

    private void ClearBars()
    {
        foreach (var child in _barsContainer.GetChildren())
            child.QueueFree();
        _bars.Clear();
    }

    private static string FormatValue(float value, float max)
    {
        if (max > 100) return $"{value:F0}";
        return $"{value:F2}";
    }
}
