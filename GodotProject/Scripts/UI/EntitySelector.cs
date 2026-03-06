using Godot;
using AutonomeSim.Core;
using AutonomeSim.Player;
using AutonomeSim.World;

namespace AutonomeSim.UI;

/// <summary>
/// Dropdown to select and possess/release an NPC.
/// </summary>
public partial class EntitySelector : HBoxContainer
{
    private SimulationBridge _bridge = null!;
    private PlayerController _player = null!;
    private CameraController _camera = null!;
    private WorldSync _worldSync = null!;

    private OptionButton _dropdown = null!;
    private Button _possessBtn = null!;
    private Button _releaseBtn = null!;
    private Label _statusLabel = null!;

    private readonly List<string> _entityIds = [];

    public override void _Ready()
    {
        _bridge = GetNode<SimulationBridge>("/root/Main/SimulationBridge");
        _player = GetNode<PlayerController>("/root/Main/PlayerController");
        _camera = GetNode<CameraController>("/root/Main/WorldMap/Camera");
        _worldSync = GetNode<WorldSync>("/root/Main/WorldMap/WorldSync");

        _dropdown = new OptionButton { CustomMinimumSize = new Vector2(200, 0) };
        _possessBtn = new Button { Text = "Possess" };
        _releaseBtn = new Button { Text = "Release", Disabled = true };
        _statusLabel = new Label { Text = "" };

        AddChild(new Label { Text = "Entity: " });
        AddChild(_dropdown);
        AddChild(_possessBtn);
        AddChild(_releaseBtn);
        AddChild(_statusLabel);

        _possessBtn.Pressed += OnPossess;
        _releaseBtn.Pressed += OnRelease;
        _bridge.SimulationLoaded += PopulateDropdown;

        // Handle case where simulation loaded before we subscribed
        if (_bridge.IsLoaded)
            PopulateDropdown();
    }

    private void PopulateDropdown()
    {
        _dropdown.Clear();
        _entityIds.Clear();

        var sorted = _bridge.Profiles
            .Where(p => p.Embodied)
            .OrderBy(p => p.DisplayName)
            .ToList();

        foreach (var profile in sorted)
        {
            _dropdown.AddItem(profile.DisplayName);
            _entityIds.Add(profile.Id);
        }
    }

    private void OnPossess()
    {
        int idx = _dropdown.Selected;
        if (idx < 0 || idx >= _entityIds.Count) return;

        var entityId = _entityIds[idx];
        _player.Possess(entityId);
        _possessBtn.Disabled = true;
        _releaseBtn.Disabled = false;
        _statusLabel.Text = $"Controlling: {_bridge.GetProfile(entityId)?.DisplayName}";

        // Focus camera on possessed entity
        var loc = _bridge.GetEntityLocation(entityId);
        if (loc != null)
        {
            var pos = _worldSync.GetLocationPosition(loc);
            _camera.FocusOn(pos);
        }
    }

    private void OnRelease()
    {
        _player.Release();
        _possessBtn.Disabled = false;
        _releaseBtn.Disabled = true;
        _statusLabel.Text = "";
    }
}
