using Godot;

namespace AutonomeSim.World;

/// <summary>
/// Draws connection lines between locations using _Draw().
/// </summary>
public partial class ConnectionLines : Node2D
{
    private readonly List<(Vector2 from, Vector2 to, float alpha)> _lines = [];

    public void SetConnections(List<(Vector2 from, Vector2 to, int cost)> connections)
    {
        _lines.Clear();
        foreach (var (from, to, cost) in connections)
        {
            // Higher cost = more transparent
            float alpha = Mathf.Clamp(1.0f / cost, 0.15f, 0.6f);
            _lines.Add((from, to, alpha));
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var color = new Color(0.3f, 0.4f, 0.5f);
        foreach (var (from, to, alpha) in _lines)
        {
            DrawLine(from, to, new Color(color, alpha), 1.5f, true);
        }
    }
}
