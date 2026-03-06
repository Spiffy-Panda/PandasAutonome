using Godot;

namespace AutonomeSim.Player;

/// <summary>
/// Camera2D with WASD/arrow pan, scroll wheel zoom, and middle-mouse drag.
/// </summary>
public partial class CameraController : Camera2D
{
    [Export] public float ZoomMin { get; set; } = 0.2f;
    [Export] public float ZoomMax { get; set; } = 3.0f;
    [Export] public float ZoomSpeed { get; set; } = 0.1f;
    [Export] public float PanSpeed { get; set; } = 800f;

    private bool _isDragging;
    private Vector2 _dragStart;

    public override void _Process(double delta)
    {
        var input = Vector2.Zero;
        if (Input.IsActionPressed("ui_left") || Input.IsKeyPressed(Key.A))
            input.X -= 1;
        if (Input.IsActionPressed("ui_right") || Input.IsKeyPressed(Key.D))
            input.X += 1;
        if (Input.IsActionPressed("ui_up") || Input.IsKeyPressed(Key.W))
            input.Y -= 1;
        if (Input.IsActionPressed("ui_down") || Input.IsKeyPressed(Key.S))
            input.Y += 1;

        if (input != Vector2.Zero)
        {
            // Invert zoom so panning speed feels consistent
            Position += input.Normalized() * PanSpeed * (float)delta / Zoom.X;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp)
            {
                ZoomAt(mb.GlobalPosition, ZoomSpeed);
                GetViewport().SetInputAsHandled();
            }
            else if (mb.ButtonIndex == MouseButton.WheelDown)
            {
                ZoomAt(mb.GlobalPosition, -ZoomSpeed);
                GetViewport().SetInputAsHandled();
            }
            else if (mb.ButtonIndex == MouseButton.Middle)
            {
                _isDragging = mb.Pressed;
                _dragStart = mb.GlobalPosition;
                GetViewport().SetInputAsHandled();
            }
        }
        else if (@event is InputEventMouseMotion mm && _isDragging)
        {
            Position -= mm.Relative / Zoom;
            GetViewport().SetInputAsHandled();
        }
    }

    private void ZoomAt(Vector2 mousePos, float factor)
    {
        float newZoom = Mathf.Clamp(Zoom.X + factor, ZoomMin, ZoomMax);
        Zoom = new Vector2(newZoom, newZoom);
    }

    public void FocusOn(Vector2 worldPosition)
    {
        Position = worldPosition;
    }
}
