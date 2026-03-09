using Godot;

namespace AutonomeSim.World;

/// <summary>
/// Draws connection lines between locations using _Draw().
/// Shows travel cost labels at the midpoint of each line.
/// Supports pulse animations for delivery routes.
/// </summary>
public partial class ConnectionLines : Node2D
{
    private readonly List<ConnectionLine> _lines = [];
    private readonly Dictionary<string, float> _pulses = new(); // edge key -> pulse intensity (0-1)
    private bool _hasPulses;

    private class ConnectionLine
    {
        public Vector2 From, To;
        public int Cost;
        public float Alpha;
        public string Key = ""; // "locA|locB" for pulse lookup
    }

    public void SetConnections(List<(Vector2 from, Vector2 to, int cost, string? fromId, string? toId)> connections)
    {
        _lines.Clear();
        foreach (var (from, to, cost, fromId, toId) in connections)
        {
            float alpha = Mathf.Clamp(1.0f / cost, 0.35f, 0.85f);
            var key = fromId != null && toId != null
                ? (string.Compare(fromId, toId) < 0 ? $"{fromId}|{toId}" : $"{toId}|{fromId}")
                : "";
            _lines.Add(new ConnectionLine { From = from, To = to, Cost = cost, Alpha = alpha, Key = key });
        }
        QueueRedraw();
    }

    /// <summary>
    /// Trigger a pulse on the edge between two locations.
    /// The pulse fades over time via _Process.
    /// </summary>
    public void PulseEdge(string locA, string locB, Color? color = null)
    {
        var key = string.Compare(locA, locB) < 0 ? $"{locA}|{locB}" : $"{locB}|{locA}";
        _pulses[key] = 1.0f;
        _hasPulses = true;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (!_hasPulses) return;

        bool any = false;
        var keys = new List<string>(_pulses.Keys);
        foreach (var key in keys)
        {
            var val = _pulses[key] - (float)delta * 0.8f; // Fade over ~1.2 seconds
            if (val <= 0)
                _pulses.Remove(key);
            else
            {
                _pulses[key] = val;
                any = true;
            }
        }
        _hasPulses = any;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var lineColor = new Color(0.5f, 0.6f, 0.7f);
        var foodPulseColor = new Color(0.3f, 0.85f, 0.2f); // Green for food flow
        var font = ThemeDB.FallbackFont;

        foreach (var line in _lines)
        {
            // Check for active pulse
            float pulseIntensity = 0f;
            if (!string.IsNullOrEmpty(line.Key) && _pulses.TryGetValue(line.Key, out var pulse))
                pulseIntensity = pulse;

            Color drawColor;
            float width;
            if (pulseIntensity > 0)
            {
                // Blend toward green, increase thickness
                drawColor = lineColor.Lerp(foodPulseColor, pulseIntensity);
                drawColor.A = Mathf.Max(line.Alpha, pulseIntensity);
                width = 2f + pulseIntensity * 3f; // Thicker during pulse
            }
            else
            {
                drawColor = new Color(lineColor, line.Alpha);
                width = 2f;
            }

            DrawLine(line.From, line.To, drawColor, width, true);

            // Draw cost label at midpoint
            var mid = (line.From + line.To) / 2f;
            var textColor = new Color(0.7f, 0.75f, 0.8f, Mathf.Max(line.Alpha, 0.5f));
            DrawString(font, mid + new Vector2(4, -4), line.Cost.ToString(),
                HorizontalAlignment.Left, -1, 11, textColor);
        }
    }
}
