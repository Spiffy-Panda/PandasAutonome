using Godot;
using AutonomeSim.Core;
using AutonomeSim.Player;

namespace AutonomeSim.UI;

/// <summary>
/// Shows available actions for the possessed entity, scored by utility.
/// Player clicks "Act" to enqueue an action for the next tick.
/// </summary>
public partial class ActionPicker : PanelContainer
{
    private SimulationBridge _bridge = null!;
    private PlayerController _player = null!;
    private VBoxContainer _actionList = null!;
    private Label _titleLabel = null!;
    private string? _queuedActionId;

    public override void _Ready()
    {
        _bridge = GetNode<SimulationBridge>("/root/Main/SimulationBridge");
        _player = GetNode<PlayerController>("/root/Main/PlayerController");

        var vbox = new VBoxContainer();
        AddChild(vbox);

        _titleLabel = new Label { Text = "Actions" };
        _titleLabel.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(_titleLabel);
        vbox.AddChild(new HSeparator());

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(260, 200),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        vbox.AddChild(scroll);

        _actionList = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        scroll.AddChild(_actionList);

        CustomMinimumSize = new Vector2(260, 0);

        _bridge.EntityPossessed += _ => Refresh();
        _bridge.EntityReleased += _ => ClearActions();
        _bridge.TickCompleted += (_, _) =>
        {
            _queuedActionId = null;
            if (_player.HasPossession) Refresh();
        };
    }

    private void Refresh()
    {
        ClearActions();
        var entityId = _player.PossessedEntityId;
        if (entityId == null) return;

        var scored = _bridge.GetAvailableActions(entityId);
        if (scored.Count == 0)
        {
            _titleLabel.Text = "Actions (none available)";
            return;
        }

        _titleLabel.Text = $"Actions ({scored.Count})";

        // Show top 15 actions
        foreach (var candidate in scored.Take(15))
        {
            var action = candidate.Action;
            var row = new HBoxContainer();

            var nameLabel = new Label
            {
                Text = action.DisplayName,
                CustomMinimumSize = new Vector2(130, 0),
                ClipText = true,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            nameLabel.AddThemeFontSizeOverride("font_size", 11);

            var catLabel = new Label
            {
                Text = action.Category ?? "",
                CustomMinimumSize = new Vector2(50, 0),
            };
            catLabel.AddThemeFontSizeOverride("font_size", 9);
            catLabel.AddThemeColorOverride("font_color", GetCategoryColor(action.Category));

            var scoreLabel = new Label
            {
                Text = $"{candidate.Score:F2}",
                CustomMinimumSize = new Vector2(40, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            scoreLabel.AddThemeFontSizeOverride("font_size", 10);

            var actBtn = new Button
            {
                Text = _queuedActionId == action.Id ? "Queued" : "Act",
                CustomMinimumSize = new Vector2(55, 0),
                Disabled = _queuedActionId == action.Id,
            };
            actBtn.AddThemeFontSizeOverride("font_size", 10);

            var capturedActionId = action.Id;
            actBtn.Pressed += () => OnActPressed(capturedActionId);

            row.AddChild(nameLabel);
            row.AddChild(catLabel);
            row.AddChild(scoreLabel);
            row.AddChild(actBtn);
            _actionList.AddChild(row);
        }
    }

    private void OnActPressed(string actionId)
    {
        _queuedActionId = actionId;
        _player.PickAction(actionId);
        Refresh(); // Update button states
    }

    private void ClearActions()
    {
        foreach (var child in _actionList.GetChildren())
            child.QueueFree();
    }

    private static Color GetCategoryColor(string? category)
    {
        return category switch
        {
            "work" => new Color(0.9f, 0.6f, 0.2f),
            "sustenance" => new Color(0.3f, 0.8f, 0.3f),
            "social" => new Color(0.3f, 0.6f, 0.9f),
            "trade" => new Color(0.9f, 0.8f, 0.2f),
            "rest" => new Color(0.6f, 0.4f, 0.8f),
            "political" => new Color(0.9f, 0.3f, 0.3f),
            "governance" => new Color(0.8f, 0.2f, 0.2f),
            "movement" => new Color(0.5f, 0.5f, 0.5f),
            _ => new Color(0.6f, 0.6f, 0.6f),
        };
    }
}
