using Godot;
using AutonomeSim.Core;

namespace AutonomeSim.World;

/// <summary>
/// Draws a sky-colored background that cycles through time-of-day colors.
/// Created dynamically by WorldSync as the first child of WorldMap.
/// </summary>
public partial class SkyRenderer : Node2D
{
    private SimulationBridge _bridge = null!;
    private Color _currentSkyColor;

    // Sky color keyframes: (hour, color)
    private static readonly (float hour, Color color)[] SkyKeyframes =
    [
        (0f,   new Color(0.04f, 0.04f, 0.12f)),  // Deep night
        (5f,   new Color(0.04f, 0.04f, 0.12f)),  // Still night
        (6.5f, new Color(0.25f, 0.15f, 0.20f)),  // Pre-dawn
        (8f,   new Color(0.45f, 0.58f, 0.75f)),  // Morning
        (10f,  new Color(0.50f, 0.65f, 0.82f)),  // Daytime
        (16f,  new Color(0.50f, 0.65f, 0.82f)),  // Still day
        (18f,  new Color(0.55f, 0.35f, 0.18f)),  // Golden hour
        (20f,  new Color(0.15f, 0.10f, 0.20f)),  // Dusk
        (21f,  new Color(0.04f, 0.04f, 0.12f)),  // Night
        (24f,  new Color(0.04f, 0.04f, 0.12f)),  // Wrap
    ];

    public override void _Ready()
    {
        ZIndex = -10; // Behind everything
        _bridge = GetNode<SimulationBridge>("/root/Main/SimulationBridge");
        _bridge.TickCompleted += OnTickCompleted;

        // Initialize to current time
        _currentSkyColor = ComputeSkyColor(_bridge.World?.Clock.GameHour ?? 12f);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        // Redraw every frame so the sky rect tracks camera movement during panning/zooming
        QueueRedraw();
    }

    private void OnTickCompleted(int tick, string gameTime)
    {
        _currentSkyColor = ComputeSkyColor(_bridge.World.Clock.GameHour);
    }

    public override void _Draw()
    {
        // Draw a large rect covering the camera's visible area
        var canvas = GetCanvasTransform();
        var viewportSize = GetViewportRect().Size;

        // Inverse of canvas transform gives us world-space bounds
        var invTransform = canvas.AffineInverse();
        var topLeft = invTransform * Vector2.Zero;
        var bottomRight = invTransform * viewportSize;

        // Add padding to avoid edge artifacts during panning
        var padding = new Vector2(500, 500);
        var rect = new Rect2(topLeft - padding, (bottomRight - topLeft) + padding * 2);

        DrawRect(rect, _currentSkyColor);
    }

    /// <summary>
    /// Compute sky color by interpolating between keyframes.
    /// </summary>
    private static Color ComputeSkyColor(float gameHour)
    {
        gameHour = Mathf.Clamp(gameHour, 0f, 23.999f);

        for (int i = 0; i < SkyKeyframes.Length - 1; i++)
        {
            var (h0, c0) = SkyKeyframes[i];
            var (h1, c1) = SkyKeyframes[i + 1];

            if (gameHour >= h0 && gameHour <= h1)
            {
                float t = (h1 - h0) > 0 ? (gameHour - h0) / (h1 - h0) : 0f;
                return c0.Lerp(c1, t);
            }
        }

        return SkyKeyframes[^1].color;
    }

    /// <summary>
    /// Get the current time-of-day period name.
    /// </summary>
    public static string GetTimeOfDay(float gameHour)
    {
        return gameHour switch
        {
            < 6f => "Night",
            < 8f => "Dawn",
            < 18f => "Day",
            < 20f => "Dusk",
            _ => "Night",
        };
    }

    /// <summary>
    /// Returns true if the given hour is considered nighttime.
    /// </summary>
    public static bool IsNight(float gameHour) => gameHour < 6f || gameHour >= 20f;
}
