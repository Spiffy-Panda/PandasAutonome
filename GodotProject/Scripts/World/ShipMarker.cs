using Godot;

namespace AutonomeSim.World;

/// <summary>
/// Animated ship marker that slides toward harbor and fades out.
/// Self-destructs after animation completes.
/// </summary>
public partial class ShipMarker : Node2D
{
    public string VesselName { get; set; } = "Ship";

    private static readonly Color ShipColor = new(0.85f, 0.7f, 0.2f);
    private float _alpha = 1f;

    public void AnimateArrival(Vector2 harborPos, float ticksPerSecond)
    {
        // Start off-screen to the left of harbor
        var startPos = harborPos + new Vector2(-200, -40);
        Position = startPos;

        float duration = ticksPerSecond > 0 ? Mathf.Clamp(2f / ticksPerSecond, 0.5f, 3f) : 1.5f;

        // Slide in toward harbor
        var tween = CreateTween();
        tween.TweenProperty(this, "position", harborPos + new Vector2(-20, -40), duration * 0.6f)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Sine);
        // Pause briefly at harbor
        tween.TweenInterval(duration * 0.2f);
        // Fade out
        tween.TweenMethod(Callable.From<float>(SetAlpha), 1f, 0f, duration * 0.2f);
        tween.TweenCallback(Callable.From(QueueFree));
    }

    private void SetAlpha(float a)
    {
        _alpha = a;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var color = new Color(ShipColor, _alpha);
        var darkColor = new Color(ShipColor.R * 0.6f, ShipColor.G * 0.6f, ShipColor.B * 0.6f, _alpha);

        // Simple ship shape: hull (polygon) + mast (line) + sail (triangle)
        // Hull
        var hull = new Vector2[]
        {
            new(-12, 4), new(-8, 8), new(10, 8), new(14, 4), new(10, 0), new(-8, 0)
        };
        DrawColoredPolygon(hull, darkColor);

        // Mast
        DrawLine(new Vector2(2, 0), new Vector2(2, -14), color, 2f);

        // Sail
        var sail = new Vector2[] { new(2, -12), new(2, -2), new(10, -4) };
        DrawColoredPolygon(sail, color);

        // Name label
        var font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(-20, -18), VesselName,
            HorizontalAlignment.Center, 60, 9, new Color(1f, 1f, 1f, _alpha));
    }
}
