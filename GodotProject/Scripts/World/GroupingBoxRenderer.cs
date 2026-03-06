using Godot;

namespace AutonomeSim.World;

/// <summary>
/// Draws dotted-border grouping boxes behind location clusters.
/// Added as a child of WorldMap with ZIndex = -1.
/// </summary>
public partial class GroupingBoxRenderer : Node2D
{
    private readonly List<(Rect2 bounds, Color color, string label)> _boxes = [];

    private const float DashLength = 10f;
    private const float GapLength = 6f;

    public override void _Ready()
    {
        ZIndex = -1; // Draw behind locations and NPCs
    }

    public void UpdateBoxes(
        IReadOnlyDictionary<string, Vector2> locationPositions,
        List<GroupBoxData> groups)
    {
        _boxes.Clear();

        foreach (var group in groups)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            bool found = false;

            foreach (var (locId, pos) in locationPositions)
            {
                if (!locId.StartsWith(group.MatchPrefix)) continue;
                found = true;
                // Account for LocationNode.Size (140x80) centered on position
                min.X = Mathf.Min(min.X, pos.X - LocationNode.Size.X / 2);
                min.Y = Mathf.Min(min.Y, pos.Y - LocationNode.Size.Y / 2);
                max.X = Mathf.Max(max.X, pos.X + LocationNode.Size.X / 2);
                max.Y = Mathf.Max(max.Y, pos.Y + LocationNode.Size.Y / 2);
            }

            if (!found) continue;

            var padding = group.Padding;
            var bounds = new Rect2(
                min - new Vector2(padding, padding),
                (max - min) + new Vector2(padding * 2, padding * 2));

            var color = group.Color.Length >= 4
                ? new Color(group.Color[0], group.Color[1], group.Color[2], group.Color[3])
                : new Color(0.3f, 0.3f, 0.3f, 0.1f);

            _boxes.Add((bounds, color, group.Label));
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var (bounds, color, label) in _boxes)
        {
            // Semi-transparent fill
            DrawRect(bounds, color, filled: true);

            // Dotted border — brighter version of fill color
            var borderColor = new Color(color.R, color.G, color.B, Mathf.Min(color.A * 4, 0.6f));
            DrawDashedRect(bounds, borderColor, 2f);

            // Label at top-left
            var labelPos = bounds.Position + new Vector2(8, 18);
            DrawString(ThemeDB.FallbackFont, labelPos, label,
                HorizontalAlignment.Left, -1, 14, borderColor);
        }
    }

    private void DrawDashedRect(Rect2 rect, Color color, float width)
    {
        var tl = rect.Position;
        var tr = tl + new Vector2(rect.Size.X, 0);
        var br = tl + rect.Size;
        var bl = tl + new Vector2(0, rect.Size.Y);

        DrawDashedLine(tl, tr, color, width);
        DrawDashedLine(tr, br, color, width);
        DrawDashedLine(br, bl, color, width);
        DrawDashedLine(bl, tl, color, width);
    }

    private void DrawDashedLine(Vector2 from, Vector2 to, Color color, float width)
    {
        var dir = (to - from);
        float totalLen = dir.Length();
        if (totalLen < 1f) return;
        dir /= totalLen;

        float t = 0;
        bool drawing = true;
        while (t < totalLen)
        {
            float segLen = drawing ? DashLength : GapLength;
            float end = Mathf.Min(t + segLen, totalLen);
            if (drawing)
                DrawLine(from + dir * t, from + dir * end, color, width, true);
            t = end;
            drawing = !drawing;
        }
    }
}
