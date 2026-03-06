using Godot;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autonome.Core.Model;

namespace AutonomeSim.World;

// --- Layout data model ---

public class LayoutData
{
    [JsonPropertyName("districts")]
    public Dictionary<string, DistrictLayout> Districts { get; set; } = new();

    [JsonPropertyName("locationPositions")]
    public Dictionary<string, float[]> LocationPositions { get; set; } = new();

    [JsonPropertyName("intraClusterSpacing")]
    public float IntraClusterSpacing { get; set; } = 180f;

    [JsonPropertyName("groups")]
    public List<GroupBoxData> Groups { get; set; } = [];
}

public class DistrictLayout
{
    [JsonPropertyName("anchor")]
    public float[] Anchor { get; set; } = [0, 0];

    [JsonPropertyName("color")]
    public float[] Color { get; set; } = [0.18f, 0.18f, 0.18f, 1f];

    [JsonPropertyName("locked")]
    public bool Locked { get; set; }
}

public class GroupBoxData
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("matchPrefix")]
    public string MatchPrefix { get; set; } = "";

    [JsonPropertyName("color")]
    public float[] Color { get; set; } = [0.3f, 0.3f, 0.3f, 0.1f];

    [JsonPropertyName("padding")]
    public float Padding { get; set; } = 50f;
}

// --- Layout engine ---

/// <summary>
/// Computes 2D positions for all locations based on district anchors.
/// Loads/saves layout from layout.json in the world data folder.
/// </summary>
public static class MapLayout
{
    private static LayoutData _layout = CreateDefaultLayout();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static LayoutData Layout => _layout;

    // --- Load / Save ---

    public static void LoadFromFile(string dataPath)
    {
        GD.Print($"MapLayout: dataPath = {dataPath}");
        string path = System.IO.Path.Combine(dataPath, "layout.json");
        GD.Print($"MapLayout: looking for {path}");

        if (System.IO.File.Exists(path))
        {
            try
            {
                var json = System.IO.File.ReadAllText(path);
                _layout = JsonSerializer.Deserialize<LayoutData>(json, JsonOptions)
                          ?? CreateDefaultLayout();
                GD.Print($"MapLayout: loaded layout.json ({_layout.Districts.Count} districts, {_layout.LocationPositions.Count} locations, {_layout.Groups.Count} groups)");
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"MapLayout: failed to parse layout.json: {ex.Message}");
                _layout = CreateDefaultLayout();
            }
        }
        else
        {
            GD.Print($"MapLayout: no layout.json found, creating default at {path}");
            _layout = CreateDefaultLayout();
            SaveToFile(dataPath);
        }
    }

    public static void SaveToFile(string dataPath)
    {
        string path = System.IO.Path.Combine(dataPath, "layout.json");
        var json = JsonSerializer.Serialize(_layout, JsonOptions);
        System.IO.File.WriteAllText(path, json);
        GD.Print($"MapLayout: saved layout.json to {path}");
    }

    // --- District accessors ---

    public static Vector2 GetDistrictAnchor(string district)
    {
        if (_layout.Districts.TryGetValue(district, out var d))
            return new Vector2(d.Anchor[0], d.Anchor[1]);
        return new Vector2(1200, 1000);
    }

    public static void SetDistrictAnchor(string district, Vector2 pos)
    {
        if (!_layout.Districts.ContainsKey(district))
            _layout.Districts[district] = new DistrictLayout();
        _layout.Districts[district].Anchor = [pos.X, pos.Y];
    }

    public static Color GetDistrictColor(string district)
    {
        if (_layout.Districts.TryGetValue(district, out var d) && d.Color.Length >= 4)
            return new Color(d.Color[0], d.Color[1], d.Color[2], d.Color[3]);
        if (_layout.Districts.TryGetValue(district, out d) && d.Color.Length >= 3)
            return new Color(d.Color[0], d.Color[1], d.Color[2]);
        return new Color(0.18f, 0.18f, 0.18f);
    }

    public static void SetDistrictColor(string district, Color color)
    {
        if (!_layout.Districts.ContainsKey(district))
            _layout.Districts[district] = new DistrictLayout();
        _layout.Districts[district].Color = [color.R, color.G, color.B, color.A];
    }

    public static bool IsDistrictLocked(string district)
    {
        return _layout.Districts.TryGetValue(district, out var d) && d.Locked;
    }

    public static void SetDistrictLocked(string district, bool locked)
    {
        if (!_layout.Districts.ContainsKey(district))
            _layout.Districts[district] = new DistrictLayout();
        _layout.Districts[district].Locked = locked;
    }

    public static IEnumerable<string> AllDistricts => _layout.Districts.Keys;

    // --- Per-location position accessors ---

    public static Vector2? GetLocationPosition(string locationId)
    {
        if (_layout.LocationPositions.TryGetValue(locationId, out var pos) && pos.Length >= 2)
            return new Vector2(pos[0], pos[1]);
        return null;
    }

    public static void SetLocationPosition(string locationId, Vector2 pos)
    {
        _layout.LocationPositions[locationId] = [pos.X, pos.Y];
    }

    public static bool IsLocationLocked(string locationId)
    {
        return IsDistrictLocked(GetDistrictPrefix(locationId));
    }

    // --- Position generation ---

    /// <summary>
    /// Returns positions for all locations. Uses saved per-location positions if available,
    /// otherwise generates from district anchors using grid layout.
    /// </summary>
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

        float spacing = _layout.IntraClusterSpacing;

        // Position each cluster around its anchor
        foreach (var (district, locs) in clusters)
        {
            var anchor = GetDistrictAnchor(district);

            int cols = Math.Max(2, (int)Math.Ceiling(Math.Sqrt(Math.Max(locs.Count, 2))));
            for (int i = 0; i < locs.Count; i++)
            {
                // Use saved position if available
                var saved = GetLocationPosition(locs[i].Id);
                if (saved.HasValue)
                {
                    positions[locs[i].Id] = saved.Value;
                    continue;
                }

                // Otherwise generate from grid
                if (locs.Count == 1)
                {
                    positions[locs[i].Id] = anchor;
                }
                else
                {
                    int row = i / cols;
                    int col = i % cols;
                    float x = anchor.X + (col - (cols - 1) / 2f) * spacing;
                    float y = anchor.Y + (row * spacing);
                    positions[locs[i].Id] = new Vector2(x, y);
                }
            }
        }

        return positions;
    }

    public static string GetDistrictPrefix(string locationId)
    {
        var parts = locationId.Split('.');
        if (parts.Length >= 2)
            return $"{parts[0]}.{parts[1]}";
        return parts[0];
    }

    // --- Defaults ---

    private static LayoutData CreateDefaultLayout()
    {
        return new LayoutData
        {
            IntraClusterSpacing = 180f,
            Districts = new()
            {
                ["sea"] = new() { Anchor = [400, 200], Color = [0.12f, 0.18f, 0.28f, 1f] },
                ["city.docks"] = new() { Anchor = [500, 600], Color = [0.15f, 0.18f, 0.22f, 1f] },
                ["city.portside"] = new() { Anchor = [800, 800], Color = [0.17f, 0.17f, 0.19f, 1f] },
                ["city.slums"] = new() { Anchor = [500, 1000], Color = [0.18f, 0.14f, 0.14f, 1f] },
                ["city.market"] = new() { Anchor = [1100, 700], Color = [0.22f, 0.20f, 0.12f, 1f] },
                ["city.civic"] = new() { Anchor = [1500, 600], Color = [0.15f, 0.18f, 0.22f, 1f] },
                ["city.residential"] = new() { Anchor = [1800, 800], Color = [0.17f, 0.17f, 0.19f, 1f] },
                ["city.manor_district"] = new() { Anchor = [2100, 600], Color = [0.20f, 0.16f, 0.22f, 1f] },
                ["city.gate"] = new() { Anchor = [1300, 1100], Color = [0.17f, 0.17f, 0.19f, 1f] },
                ["hinterland.farmland"] = new() { Anchor = [600, 1600], Color = [0.14f, 0.20f, 0.12f, 1f] },
                ["hinterland.trails"] = new() { Anchor = [1100, 1400], Color = [0.16f, 0.18f, 0.14f, 1f] },
                ["hinterland.roads"] = new() { Anchor = [1400, 1500], Color = [0.16f, 0.18f, 0.14f, 1f] },
                ["hinterland.quarry"] = new() { Anchor = [1800, 1600], Color = [0.20f, 0.18f, 0.14f, 1f] },
                ["hinterland.woodlands"] = new() { Anchor = [2200, 1500], Color = [0.10f, 0.18f, 0.10f, 1f] },
            },
            Groups =
            [
                new() { Label = "City", MatchPrefix = "city", Color = [0.2f, 0.3f, 0.5f, 0.15f], Padding = 60f },
                new() { Label = "Hinterland", MatchPrefix = "hinterland", Color = [0.2f, 0.4f, 0.2f, 0.15f], Padding = 60f },
                new() { Label = "Sea", MatchPrefix = "sea", Color = [0.15f, 0.25f, 0.4f, 0.15f], Padding = 60f },
            ],
        };
    }
}
