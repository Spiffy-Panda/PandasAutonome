using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutonomeSim.Core;

/// <summary>
/// Loads a JSON file of recorded input events (clicks, key presses) and replays them.
/// Also handles global hotkeys:
///   1/2/3 = sim speed 1x/5x/20x tps, ~ = pause sim
///   Space = screenshot to debug folder, Enter = single sim tick
///   Ctrl+S = export analysis (same as Esc but without quitting)
/// </summary>
public partial class InputPlayback : Node
{
    [Export] public string RecordingPath { get; set; } = "res://recordings/input.json";

    private SimulationBridge _bridge = null!;

    private List<InputEvent> _events = new();
    private int _currentIndex;
    private double _elapsed;
    private float _playbackSpeed = 1f;
    private bool _paused = true;
    private bool _finished;
    private int _screenshotCounter;

    [Signal] public delegate void PlaybackStartedEventHandler();
    [Signal] public delegate void PlaybackPausedEventHandler();
    [Signal] public delegate void PlaybackFinishedEventHandler();
    [Signal] public delegate void EventPlayedEventHandler(int index, string type);

    public override void _Ready()
    {
        _bridge = GetNode<SimulationBridge>("/root/Main/SimulationBridge");
        LoadRecording(RecordingPath);
    }

    public override void _UnhandledKeyInput(Godot.InputEvent ev)
    {
        if (ev is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
            return;

        // Ctrl+S: export analysis without quitting
        if (keyEvent.Keycode == Key.S && keyEvent.CtrlPressed)
        {
            ExportAnalysis();
            GetViewport().SetInputAsHandled();
            return;
        }

        switch (keyEvent.Keycode)
        {
            case Key.Key1:
                _bridge.StartAutoAdvance(1f);
                _playbackSpeed = 1f;
                _paused = false;
                GD.Print("[InputPlayback] Sim 1 tps");
                GetViewport().SetInputAsHandled();
                break;
            case Key.Key2:
                _bridge.StartAutoAdvance(5f);
                _playbackSpeed = 2f;
                _paused = false;
                GD.Print("[InputPlayback] Sim 5 tps");
                GetViewport().SetInputAsHandled();
                break;
            case Key.Key3:
                _bridge.StartAutoAdvance(20f);
                _playbackSpeed = 3f;
                _paused = false;
                GD.Print("[InputPlayback] Sim 20 tps");
                GetViewport().SetInputAsHandled();
                break;
            case Key.Quoteleft: // ~ key
                _bridge.PauseSimulation();
                _paused = true;
                GD.Print("[InputPlayback] Sim paused");
                GetViewport().SetInputAsHandled();
                break;
            case Key.Space:
                CaptureScreenshot();
                GetViewport().SetInputAsHandled();
                break;
            case Key.Enter:
                _bridge.StepOneTick();
                GD.Print($"[InputPlayback] Single tick → {_bridge.CurrentTick}");
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_paused || _finished || _currentIndex >= _events.Count)
            return;

        _elapsed += delta * _playbackSpeed;

        // Fire all events whose timestamp has been reached
        while (_currentIndex < _events.Count && _elapsed >= _events[_currentIndex].Time)
        {
            ExecuteEvent(_events[_currentIndex]);
            EmitSignal(SignalName.EventPlayed, _currentIndex, _events[_currentIndex].Type);
            _currentIndex++;
        }

        if (_currentIndex >= _events.Count)
        {
            _finished = true;
            _paused = true;
            GD.Print("[InputPlayback] Finished all events");
            EmitSignal(SignalName.PlaybackFinished);
        }
    }

    public void LoadRecording(string path)
    {
        _events.Clear();
        _currentIndex = 0;
        _elapsed = 0;
        _finished = false;
        _paused = true;

        if (!Godot.FileAccess.FileExists(path))
        {
            GD.PrintErr($"[InputPlayback] File not found: {path}");
            return;
        }

        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        var json = file.GetAsText();

        try
        {
            var recording = JsonSerializer.Deserialize<Recording>(json, _jsonOpts);
            if (recording?.Events != null)
            {
                _events = recording.Events;
                _events.Sort((a, b) => a.Time.CompareTo(b.Time));
                GD.Print($"[InputPlayback] Loaded {_events.Count} events from {path}");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[InputPlayback] Failed to parse JSON: {ex.Message}");
        }
    }

    public void Restart()
    {
        _currentIndex = 0;
        _elapsed = 0;
        _finished = false;
        GD.Print("[InputPlayback] Restarted");
    }

    private void ExportAnalysis()
    {
        if (!_bridge.IsLoaded)
        {
            GD.Print("[InputPlayback] Simulation not loaded, skipping export.");
            return;
        }

        var result = _bridge.GetSimulationResult();
        var loadResult = _bridge.GetLoadResult();

        if (result.ActionEvents.Count == 0)
        {
            GD.Print("[InputPlayback] No ticks recorded, skipping export.");
            return;
        }

        // Take a snapshot
        result.Snapshots.Add(
            Autonome.Core.Simulation.SimulationRunner.TakeSnapshot(
                _bridge.World, _bridge.Profiles));

        try
        {
            var projectDir = ProjectSettings.GlobalizePath("res://");
            var parentDir = System.IO.Path.GetDirectoryName(projectDir.TrimEnd('/', '\\')) ?? projectDir;
            var analysisDir = System.IO.Path.Combine(parentDir, "output", "analysis");
            var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var runDir = System.IO.Path.Combine(analysisDir, stamp);
            System.IO.Directory.CreateDirectory(runDir);

            var metaJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                dataPath = _bridge.ResolvedDataPath,
                source = "godot_ctrl_s",
                ticks = _bridge.CurrentTick,
                gameTime = _bridge.CurrentGameTime,
            });
            System.IO.File.WriteAllText(System.IO.Path.Combine(runDir, "meta.json"), metaJson);

            var simOutputPath = System.IO.Path.Combine(runDir, "simulation_result.json");
            Autonome.History.HistoryExporter.Export(result, simOutputPath);

            var analysisResult = Autonome.Analysis.SimulationAnalyzer.Analyze(result);
            Autonome.Analysis.ReportWriter.WriteToDir(analysisResult, runDir);

            if (loadResult != null)
            {
                var inventoryResult = Autonome.Analysis.InventoryAnalyzer.Analyze(
                    result, loadResult.Actions, loadResult.Locations, loadResult.Events);
                Autonome.Analysis.ReportWriter.WriteInventory(inventoryResult, runDir);
            }

            GD.Print($"[InputPlayback] Analysis exported to {runDir}/");
            GD.Print($"  {result.ActionEvents.Count} action events, {result.Snapshots.Count} snapshots");
            GD.Print($"  Tick {_bridge.CurrentTick} ({_bridge.CurrentGameTime})");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[InputPlayback] Export failed: {ex.Message}");
            GD.PrintErr(ex.StackTrace);
        }
    }

    private void CaptureScreenshot()
    {
        var image = GetViewport().GetTexture().GetImage();

        var projectDir = ProjectSettings.GlobalizePath("res://");
        var parentDir = System.IO.Path.GetDirectoryName(projectDir.TrimEnd('/', '\\')) ?? projectDir;
        var debugDir = System.IO.Path.Combine(parentDir, "debug");

        if (!System.IO.Directory.Exists(debugDir))
            System.IO.Directory.CreateDirectory(debugDir);

        _screenshotCounter++;
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var filePath = System.IO.Path.Combine(debugDir, $"playback_{timestamp}_{_screenshotCounter:D3}.png");

        var err = image.SavePng(filePath);
        if (err == Error.Ok)
            GD.Print($"[InputPlayback] Screenshot saved: {filePath}");
        else
            GD.PrintErr($"[InputPlayback] Screenshot failed: {err}");
    }

    private void StepOneEvent()
    {
        if (_finished || _currentIndex >= _events.Count)
        {
            GD.Print("[InputPlayback] No more events to step through");
            return;
        }

        _paused = true;
        var evt = _events[_currentIndex];
        _elapsed = evt.Time; // jump timeline to this event
        ExecuteEvent(evt);
        EmitSignal(SignalName.EventPlayed, _currentIndex, evt.Type);
        _currentIndex++;
        GD.Print($"[InputPlayback] Stepped to event {_currentIndex}/{_events.Count}");

        if (_currentIndex >= _events.Count)
        {
            _finished = true;
            GD.Print("[InputPlayback] Finished all events");
            EmitSignal(SignalName.PlaybackFinished);
        }
    }

    private void ExecuteEvent(InputEvent evt)
    {
        switch (evt.Type)
        {
            case "click":
                SimulateClick(evt);
                break;
            case "key":
                SimulateKey(evt);
                break;
            case "mouse_move":
                SimulateMouseMove(evt);
                break;
            default:
                GD.PrintErr($"[InputPlayback] Unknown event type: {evt.Type}");
                break;
        }
    }

    private void SimulateClick(InputEvent evt)
    {
        var button = evt.Button switch
        {
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => MouseButton.Left
        };

        var pos = new Vector2(evt.X, evt.Y);

        // Press
        var press = new InputEventMouseButton
        {
            ButtonIndex = button,
            Pressed = true,
            Position = pos,
            GlobalPosition = pos
        };
        Input.ParseInputEvent(press);

        // Release
        var release = new InputEventMouseButton
        {
            ButtonIndex = button,
            Pressed = false,
            Position = pos,
            GlobalPosition = pos
        };
        Input.ParseInputEvent(release);

        GD.Print($"[InputPlayback] Click {button} at ({evt.X}, {evt.Y})");
    }

    private void SimulateKey(InputEvent evt)
    {
        var keycode = OS.FindKeycodeFromString(evt.Key ?? "");
        if (keycode == Key.None)
        {
            GD.PrintErr($"[InputPlayback] Unknown key: {evt.Key}");
            return;
        }

        // Press
        var press = new InputEventKey
        {
            Keycode = keycode,
            PhysicalKeycode = keycode,
            Pressed = true,
            ShiftPressed = evt.Shift,
            CtrlPressed = evt.Ctrl,
            AltPressed = evt.Alt
        };
        Input.ParseInputEvent(press);

        // Release
        var release = new InputEventKey
        {
            Keycode = keycode,
            PhysicalKeycode = keycode,
            Pressed = false,
            ShiftPressed = evt.Shift,
            CtrlPressed = evt.Ctrl,
            AltPressed = evt.Alt
        };
        Input.ParseInputEvent(release);

        GD.Print($"[InputPlayback] Key '{evt.Key}'{(evt.Shift ? " +Shift" : "")}{(evt.Ctrl ? " +Ctrl" : "")}{(evt.Alt ? " +Alt" : "")}");
    }

    private void SimulateMouseMove(InputEvent evt)
    {
        var pos = new Vector2(evt.X, evt.Y);
        var motion = new InputEventMouseMotion
        {
            Position = pos,
            GlobalPosition = pos
        };
        Input.ParseInputEvent(motion);
    }

    // --- JSON model ---

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private class Recording
    {
        [JsonPropertyName("events")]
        public List<InputEvent> Events { get; set; } = new();
    }

    private class InputEvent
    {
        [JsonPropertyName("time")]
        public double Time { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "click";

        // Click fields
        [JsonPropertyName("x")]
        public float X { get; set; }

        [JsonPropertyName("y")]
        public float Y { get; set; }

        [JsonPropertyName("button")]
        public string Button { get; set; } = "left";

        // Key fields
        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonPropertyName("shift")]
        public bool Shift { get; set; }

        [JsonPropertyName("ctrl")]
        public bool Ctrl { get; set; }

        [JsonPropertyName("alt")]
        public bool Alt { get; set; }
    }
}
