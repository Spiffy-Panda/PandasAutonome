using Godot;

namespace AutonomeSim.World;

/// <summary>
/// Draws connection lines between locations using _Draw().
/// Shows travel cost labels at the midpoint of each line.
/// </summary>
public partial class ConnectionLines : Node2D
{
    private readonly List<(Vector2 from, Vector2 to, int cost, float alpha)> _lines = [];

    public void SetConnections(List<(Vector2 from, Vector2 to, int cost)> connections)
    {
        _lines.Clear();
        foreach (var (from, to, cost) in connections)
        {
            // Higher cost = more transparent, but keep visible
            float alpha = Mathf.Clamp(1.0f / cost, 0.35f, 0.85f);
            _lines.Add((from, to, cost, alpha));
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var lineColor = new Color(0.5f, 0.6f, 0.7f);
        var font = ThemeDB.FallbackFont;

        foreach (var (from, to, cost, alpha) in _lines)
        {
            DrawLine(from, to, new Color(lineColor, alpha), 2f, true);

            // Draw cost label at midpoint
            var mid = (from + to) / 2f;
            var textColor = new Color(0.7f, 0.75f, 0.8f, Mathf.Max(alpha, 0.5f));
            DrawString(font, mid + new Vector2(4, -4), cost.ToString(),
                HorizontalAlignment.Left, -1, 11, textColor);
        }
    }
}
