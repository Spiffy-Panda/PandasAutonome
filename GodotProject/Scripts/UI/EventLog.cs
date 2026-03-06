using Godot;
using AutonomeSim.Core;

namespace AutonomeSim.UI;

/// <summary>
/// Scrolling event log showing recent simulation actions.
/// </summary>
public partial class EventLog : PanelContainer
{
    private SimulationBridge _bridge = null!;
    private VBoxContainer _entries = null!;
    private ScrollContainer _scroll = null!;
    private const int MaxEntries = 200;
    private int _entryCount;

    public override void _Ready()
    {
        _bridge = GetNode<SimulationBridge>("/root/Main/SimulationBridge");

        var vbox = new VBoxContainer();
        AddChild(vbox);

        var title = new Label { Text = "Event Log" };
        title.AddThemeFontSizeOverride("font_size", 13);
        vbox.AddChild(title);
        vbox.AddChild(new HSeparator());

        _scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(280, 200),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        vbox.AddChild(_scroll);

        _entries = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _scroll.AddChild(_entries);

        CustomMinimumSize = new Vector2(280, 0);

        _bridge.EntityAction += OnEntityAction;
    }

    private void OnEntityAction(string entityId, string actionId, string location, float score)
    {
        var profile = _bridge.GetProfile(entityId);
        var action = _bridge.GetAction(actionId);
        var name = profile?.DisplayName ?? entityId;
        var actionName = action?.DisplayName ?? actionId;
        var locDef = string.IsNullOrEmpty(location) ? null : _bridge.World.Locations.GetDefinition(location);
        var locName = locDef?.DisplayName ?? location;

        var label = new Label
        {
            Text = $"[{_bridge.CurrentTick}] {name} — {actionName} @ {locName}",
            AutowrapMode = TextServer.AutowrapMode.Off,
            ClipText = true,
        };
        label.AddThemeFontSizeOverride("font_size", 10);

        // Highlight possessed entity's events
        bool isOwn = entityId == _bridge.PossessedEntityId;
        label.AddThemeColorOverride("font_color", isOwn
            ? new Color(0.05f, 0.72f, 0.87f)
            : new Color(0.65f, 0.65f, 0.65f));

        _entries.AddChild(label);
        _entryCount++;

        // Trim oldest
        while (_entryCount > MaxEntries)
        {
            var oldest = _entries.GetChild(0);
            _entries.RemoveChild(oldest);
            oldest.QueueFree();
            _entryCount--;
        }

        // Auto-scroll to bottom
        CallDeferred(nameof(ScrollToBottom));
    }

    private void ScrollToBottom()
    {
        _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
    }
}
