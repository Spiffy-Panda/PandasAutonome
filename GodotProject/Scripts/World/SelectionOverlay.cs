using Godot;

namespace AutonomeSim.World;

/// <summary>
/// Draws a selection rectangle in world space during box-select.
/// </summary>
public partial class SelectionOverlay : Node2D
{
    public Rect2? SelectionRect { get; set; }

    public override void _Ready()
    {
        ZIndex = 10; // Draw above everything
    }

    public override void _Draw()
    {
        if (!SelectionRect.HasValue) return;

        var rect = SelectionRect.Value;
        DrawRect(rect, new Color(0.3f, 0.6f, 1.0f, 0.12f), filled: true);
        DrawRect(rect, new Color(0.4f, 0.7f, 1.0f, 0.6f), filled: false, width: 1.5f);
    }
}
