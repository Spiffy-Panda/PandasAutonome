using Godot;

namespace AutonomeSim.Core;

/// <summary>
/// Takes a screenshot of the viewport after a 1-second delay and saves it
/// to a debug folder in the project's parent directory.
/// Takes two shots: one zoomed-out overview, one zoomed-in on an inventory location.
/// </summary>
public partial class ScreenGrab : Node
{
    private double _elapsed;
    private int _shotsTaken;

    public override void _Process(double delta)
    {
        if (_shotsTaken >= 2) return;

        _elapsed += delta;

        // Shot 1: overview at 1 second
        if (_shotsTaken == 0 && _elapsed >= 1.0)
        {
            CaptureScreenshot("overview");
            ZoomToInventoryLocation();
            _shotsTaken = 1;
        }
        // Shot 2: zoomed-in at 1.5 seconds (after camera has moved)
        else if (_shotsTaken == 1 && _elapsed >= 1.5)
        {
            CaptureScreenshot("inventory_closeup");
            _shotsTaken = 2;
        }
    }

    private void ZoomToInventoryLocation()
    {
        // Zoom camera in on Harbor Docks area to verify inventory bars
        var camera = GetNode<Camera2D>("/root/Main/WorldMap/Camera");
        var worldSync = GetNode<Node>("/root/Main/WorldMap/WorldSync");

        // Try to find Harbor Docks position via WorldSync
        if (worldSync is AutonomeSim.World.WorldSync ws)
        {
            var pos = ws.GetLocationPosition("city.docks.harbor");
            if (pos != Vector2.Zero)
            {
                camera.Position = pos;
                camera.Zoom = new Vector2(2.0f, 2.0f);
                GD.Print($"ScreenGrab: zoomed to Harbor Docks at {pos}");
                return;
            }
        }

        // Fallback: just zoom in where we are
        camera.Zoom = new Vector2(2.0f, 2.0f);
    }

    private void CaptureScreenshot(string label)
    {
        var image = GetViewport().GetTexture().GetImage();

        // Build path: project root's parent / debug
        var projectDir = ProjectSettings.GlobalizePath("res://");
        var parentDir = System.IO.Path.GetDirectoryName(projectDir.TrimEnd('/', '\\')) ?? projectDir;
        var debugDir = System.IO.Path.Combine(parentDir, "debug");

        if (!System.IO.Directory.Exists(debugDir))
            System.IO.Directory.CreateDirectory(debugDir);

        var timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var filePath = System.IO.Path.Combine(debugDir, $"screenshot_{label}_{timestamp}.png");

        var err = image.SavePng(filePath);
        if (err == Error.Ok)
            GD.Print($"ScreenGrab: saved {label} to {filePath}");
        else
            GD.PrintErr($"ScreenGrab: failed to save {label} — {err}");
    }
}
