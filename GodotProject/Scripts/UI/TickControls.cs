using Godot;
using AutonomeSim.Core;
using AutonomeSim.World;

namespace AutonomeSim.UI;

/// <summary>
/// Tick control bar: step, auto/pause, speed slider, tick counter, game time, time-of-day.
/// </summary>
public partial class TickControls : HBoxContainer
{
    private SimulationBridge _bridge = null!;
    private Button _tickBtn = null!;
    private Button _autoBtn = null!;
    private Button _pauseBtn = null!;
    private HSlider _speedSlider = null!;
    private Label _speedLabel = null!;
    private Label _tickLabel = null!;
    private Label _timeLabel = null!;
    private Label _todLabel = null!;

    public override void _Ready()
    {
        _bridge = GetNode<SimulationBridge>("/root/Main/SimulationBridge");

        _tickBtn = new Button { Text = "Tick", CustomMinimumSize = new Vector2(60, 0) };
        _autoBtn = new Button { Text = "Auto", CustomMinimumSize = new Vector2(60, 0) };
        _pauseBtn = new Button { Text = "Pause", CustomMinimumSize = new Vector2(60, 0) };

        _speedSlider = new HSlider
        {
            MinValue = 0.5, MaxValue = 20, Step = 0.5, Value = 1,
            CustomMinimumSize = new Vector2(120, 0),
        };

        _speedLabel = new Label { Text = "1.0 tps" };
        _tickLabel = new Label { Text = "Tick: 0" };
        _timeLabel = new Label { Text = "" };
        _todLabel = new Label { Text = "" };

        AddChild(_tickBtn);
        AddChild(_autoBtn);
        AddChild(_pauseBtn);
        AddChild(_speedSlider);
        AddChild(_speedLabel);
        AddChild(new VSeparator());
        AddChild(_tickLabel);
        AddChild(_timeLabel);
        AddChild(_todLabel);

        _tickBtn.Pressed += OnTick;
        _autoBtn.Pressed += OnAuto;
        _pauseBtn.Pressed += OnPause;
        _speedSlider.ValueChanged += OnSpeedChanged;
        _bridge.TickCompleted += OnTickCompleted;

        // Set initial time-of-day display
        if (_bridge.IsLoaded)
        {
            _timeLabel.Text = _bridge.CurrentGameTime;
            UpdateTimeOfDay();
        }
    }

    private void UpdateTimeOfDay()
    {
        var gameHour = _bridge.World?.Clock.GameHour ?? 12f;
        var tod = SkyRenderer.GetTimeOfDay(gameHour);
        _todLabel.Text = tod;
        _todLabel.RemoveThemeColorOverride("font_color");
        _todLabel.AddThemeColorOverride("font_color", SkyRenderer.IsNight(gameHour)
            ? new Color(0.6f, 0.65f, 0.9f) // Pale blue for night
            : new Color(0.9f, 0.85f, 0.5f) // Warm yellow for day
        );
    }

    // Keyboard shortcuts moved to InputPlayback (1/2/3 speed, ~ pause, Enter tick, Space screenshot)

    private void OnTick() => _bridge.StepOneTick();
    private void OnAuto() => _bridge.StartAutoAdvance((float)_speedSlider.Value);
    private void OnPause() => _bridge.PauseSimulation();

    private void OnSpeedChanged(double value)
    {
        _speedLabel.Text = $"{value:F1} tps";
        _bridge.SetSpeed((float)value);
    }

    private void OnTickCompleted(int tick, string gameTime)
    {
        _tickLabel.Text = $"Tick: {tick}";
        _timeLabel.Text = gameTime;
        UpdateTimeOfDay();
    }
}
