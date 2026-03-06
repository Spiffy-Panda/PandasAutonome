using Godot;
using Autonome.Core.Model;

namespace AutonomeSim.World;

/// <summary>
/// Computes 2D positions for all locations based on district hierarchy.
/// Hardcoded region anchors matching coastal city geography.
/// </summary>
public static class MapLayout
{
    private static readonly Dictionary<string, Vector2> DistrictAnchors = new()
    {
        // Sea — top
        ["sea"] = new(400, 200),

        // City — middle band, left to right by wealth
        ["city.docks"] = new(500, 600),
        ["city.portside"] = new(800, 800),
        ["city.slums"] = new(500, 1000),
        ["city.market"] = new(1100, 700),
        ["city.civic"] = new(1500, 600),
        ["city.residential"] = new(1800, 800),
        ["city.manor_district"] = new(2100, 600),
        ["city.gate"] = new(1300, 1100),

        // Hinterland — bottom band
        ["hinterland.farmland"] = new(600, 1600),
        ["hinterland.trails"] = new(1100, 1400),
        ["hinterland.roads"] = new(1400, 1500),
        ["hinterland.quarry"] = new(1800, 1600),
        ["hinterland.woodlands"] = new(2200, 1500),
    };

    private const float IntraClusterSpacing = 180f;

    public static Dictionary<string, Vector2> GeneratePositions(IEnumerable<LocationDefinition> locations)
    {
        var positions = new Dictionary<string, Vector2>();

        // Group locations by district prefix (first 2 segments)
        var clusters = new Dictionary<string, List<LocationDefinition>>();
        foreach (var loc in locations)
        {
            var district = GetDistrictPrefix(loc.Id);
            if (!clusters.ContainsKey(district))
                clusters[district] = new();
            clusters[district].Add(loc);
        }

        // Position each cluster around its anchor
        foreach (var (district, locs) in clusters)
        {
            var anchor = DistrictAnchors.GetValueOrDefault(district, new Vector2(1200, 1000));

            if (locs.Count == 1)
            {
                positions[locs[0].Id] = anchor;
                continue;
            }

            // Arrange in a compact grid around the anchor
            int cols = Math.Max(2, (int)Math.Ceiling(Math.Sqrt(locs.Count)));
            for (int i = 0; i < locs.Count; i++)
            {
                int row = i / cols;
                int col = i % cols;
                float x = anchor.X + (col - (cols - 1) / 2f) * IntraClusterSpacing;
                float y = anchor.Y + (row * IntraClusterSpacing);
                positions[locs[i].Id] = new Vector2(x, y);
            }
        }

        return positions;
    }

    private static string GetDistrictPrefix(string locationId)
    {
        var parts = locationId.Split('.');
        if (parts.Length >= 2)
            return $"{parts[0]}.{parts[1]}";
        return parts[0];
    }
}
