using System.Text.Json;
using System.Text.Json.Serialization;
using Autonome.Core.Model;
using Autonome.Core.World;

namespace Autonome.Data;

/// <summary>
/// Walks a data directory, deserializes JSON by path convention, and resolves curve presets.
/// </summary>
public class DataLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new LocationEdgeListConverter()
        }
    };

    private readonly CurvePresetLibrary _curvePresets = new();

    public CurvePresetLibrary CurvePresets => _curvePresets;

    public LoadResult Load(string dataPath)
    {
        var result = new LoadResult();

        // Load curve presets first
        string curvesPath = Path.Combine(dataPath, "curves.json");
        _curvePresets.LoadFromFile(curvesPath);

        // Load autonome profiles
        string autonomesPath = Path.Combine(dataPath, "autonomes");
        if (Directory.Exists(autonomesPath))
        {
            foreach (var file in Directory.GetFiles(autonomesPath, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var profile = JsonSerializer.Deserialize<AutonomeProfile>(json, JsonOptions);
                    if (profile != null)
                        result.Profiles.Add(profile);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to load autonome {file}: {ex.Message}");
                }
            }
        }

        // Load action definitions
        string actionsPath = Path.Combine(dataPath, "actions");
        if (Directory.Exists(actionsPath))
        {
            foreach (var file in Directory.GetFiles(actionsPath, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var action = DeserializeAction(json);
                    if (action != null)
                        result.Actions.Add(action);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to load action {file}: {ex.Message}");
                }
            }
        }

        // Load relationships
        string relsPath = Path.Combine(dataPath, "relationships");
        if (Directory.Exists(relsPath))
        {
            foreach (var file in Directory.GetFiles(relsPath, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var rels = JsonSerializer.Deserialize<List<RelationshipData>>(json, JsonOptions);
                    if (rels != null)
                        result.Relationships.AddRange(rels);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to load relationships {file}: {ex.Message}");
                }
            }
        }

        // Load locations
        string locationsPath = Path.Combine(dataPath, "locations");
        if (Directory.Exists(locationsPath))
        {
            foreach (var file in Directory.GetFiles(locationsPath, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var locations = JsonSerializer.Deserialize<List<LocationDefinition>>(json, JsonOptions);
                    if (locations != null)
                        result.Locations.AddRange(locations);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to load locations {file}: {ex.Message}");
                }
            }
        }

        // Load property levels (optional — missing file is fine)
        string propertyLevelsPath = Path.Combine(dataPath, "property_levels.json");
        if (File.Exists(propertyLevelsPath))
        {
            try
            {
                var json = File.ReadAllText(propertyLevelsPath);
                result.PropertyLevels = DeserializePropertyLevels(json);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to load property_levels.json: {ex.Message}");
            }
        }

        // Load external events (optional — missing file is fine)
        string eventsPath = Path.Combine(dataPath, "events.json");
        if (File.Exists(eventsPath))
        {
            try
            {
                var json = File.ReadAllText(eventsPath);
                result.Events = JsonSerializer.Deserialize<List<ExternalEvent>>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to load events.json: {ex.Message}");
            }
        }

        return result;
    }

    private ActionDefinition? DeserializeAction(string json)
    {
        var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        var root = doc.RootElement;

        // Strip propertyResponses before deserializing (they contain curve presets that can't auto-deserialize)
        var strippedJson = StripPropertyResponses(root);
        var action = JsonSerializer.Deserialize<ActionDefinition>(strippedJson, JsonOptions);
        if (action == null) return null;

        // Resolve curve presets in propertyResponses manually
        var resolved = new Dictionary<string, PropertyResponse>();
        if (root.TryGetProperty("propertyResponses", out var responses))
        {
            foreach (var prop in responses.EnumerateObject())
            {
                var responseElem = prop.Value;
                float magnitude = responseElem.TryGetProperty("magnitude", out var mag)
                    ? mag.GetSingle()
                    : 1.0f;

                ResponseCurve? curve = null;
                if (responseElem.TryGetProperty("curve", out var curveElem))
                {
                    if (curveElem.ValueKind == JsonValueKind.String)
                    {
                        curve = _curvePresets.Resolve(curveElem.GetString()!);
                    }
                    else if (curveElem.ValueKind == JsonValueKind.Object)
                    {
                        curve = JsonSerializer.Deserialize<ResponseCurve>(curveElem.GetRawText(), JsonOptions);
                    }
                }

                if (curve != null)
                {
                    resolved[prop.Name] = new PropertyResponse(curve, magnitude);
                }
            }
        }

        return new ActionDefinition
        {
            Id = action.Id,
            DisplayName = action.DisplayName,
            Category = action.Category,
            Requirements = action.Requirements,
            PropertyResponses = resolved,
            PersonalityAffinity = action.PersonalityAffinity,
            Steps = action.Steps,
            Flavor = action.Flavor,
            MemoryGeneration = action.MemoryGeneration
        };
    }

    private static string StripPropertyResponses(JsonElement root)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name == "propertyResponses") continue;
            prop.WriteTo(writer);
        }
        writer.WriteEndObject();
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Parse the compact property_levels.json format where each level is an array of property IDs.
    /// </summary>
    private static PropertyLevelConfig DeserializePropertyLevels(string json)
    {
        var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        var root = doc.RootElement;
        var sets = new Dictionary<string, PropertyLevelSet>();

        if (root.TryGetProperty("sets", out var setsElem))
        {
            foreach (var setProp in setsElem.EnumerateObject())
            {
                var levels = new Dictionary<string, PropertyLevel>();

                foreach (var levelProp in setProp.Value.EnumerateObject())
                {
                    if (!Enum.TryParse<PropertyLevel>(levelProp.Name, ignoreCase: true, out var level))
                        continue;

                    foreach (var propId in levelProp.Value.EnumerateArray())
                    {
                        var id = propId.GetString();
                        if (id != null)
                            levels[id] = level;
                    }
                }

                sets[setProp.Name] = new PropertyLevelSet(setProp.Name, levels);
            }
        }

        var entityTypeDefaults = new Dictionary<string, List<string>>();
        if (root.TryGetProperty("entityTypeDefaults", out var defaultsElem))
        {
            foreach (var defaultProp in defaultsElem.EnumerateObject())
            {
                var setIds = new List<string>();
                foreach (var id in defaultProp.Value.EnumerateArray())
                {
                    var s = id.GetString();
                    if (s != null) setIds.Add(s);
                }
                entityTypeDefaults[defaultProp.Name] = setIds;
            }
        }

        return new PropertyLevelConfig(sets, entityTypeDefaults);
    }

    /// <summary>
    /// Resolve the property levels for a given profile by merging its PropertySets
    /// (or falling back to entityTypeDefaults). Unclassified properties default to Any.
    /// </summary>
    public static Dictionary<string, PropertyLevel> ResolvePropertyLevels(
        AutonomeProfile profile,
        PropertyLevelConfig? config)
    {
        if (config == null)
            return new Dictionary<string, PropertyLevel>();

        // Determine which set IDs to use
        List<string>? setIds = profile.PropertySets;
        if (setIds == null || setIds.Count == 0)
        {
            string typeKey = profile.Embodied ? "embodied" : "non_embodied";
            config.EntityTypeDefaults.TryGetValue(typeKey, out setIds);
        }

        var merged = new Dictionary<string, PropertyLevel>();

        if (setIds != null)
        {
            foreach (var setId in setIds)
            {
                if (!config.Sets.TryGetValue(setId, out var set)) continue;
                foreach (var (propId, level) in set.Levels)
                {
                    // Earlier set wins (first definition takes precedence)
                    if (!merged.ContainsKey(propId))
                        merged[propId] = level;
                }
            }
        }

        // Unclassified properties default to Any
        foreach (var propId in profile.Properties.Keys)
        {
            if (!merged.ContainsKey(propId))
                merged[propId] = PropertyLevel.Any;
        }

        return merged;
    }
}

/// <summary>
/// Intermediate type for relationship JSON deserialization.
/// </summary>
public sealed record RelationshipData(
    string Source,
    string Target,
    List<string> Tags,
    Dictionary<string, PropertyDefData>? Properties = null
);

public sealed record PropertyDefData(
    float Value = 0.5f,
    float DecayRate = 0f,
    float Min = 0f,
    float Max = 1f
);

public sealed class LoadResult
{
    public List<AutonomeProfile> Profiles { get; } = [];
    public List<ActionDefinition> Actions { get; } = [];
    public List<RelationshipData> Relationships { get; } = [];
    public List<LocationDefinition> Locations { get; } = [];
    public PropertyLevelConfig? PropertyLevels { get; set; }
    public List<ExternalEvent>? Events { get; set; }
    public List<string> Errors { get; } = [];

    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// Handles both string ("loc_id") and object ({ "target": "loc_id", "cost": 3 }) formats
/// for location edges in connectedTo arrays.
/// </summary>
public sealed class LocationEdgeListConverter : JsonConverter<List<LocationEdge>>
{
    public override List<LocationEdge> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var edges = new List<LocationEdge>();

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected array for connectedTo");

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) break;

            if (reader.TokenType == JsonTokenType.String)
            {
                // Plain string: "loc_id" with default cost of 1
                edges.Add(new LocationEdge(reader.GetString()!, 1));
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                // Object: { "target": "loc_id", "cost": 3 }
                string? target = null;
                int cost = 1;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject) break;

                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        string propName = reader.GetString()!;
                        reader.Read();

                        if (propName.Equals("target", StringComparison.OrdinalIgnoreCase))
                            target = reader.GetString();
                        else if (propName.Equals("cost", StringComparison.OrdinalIgnoreCase))
                            cost = reader.GetInt32();
                    }
                }

                if (target != null)
                    edges.Add(new LocationEdge(target, cost));
            }
        }

        return edges;
    }

    public override void Write(Utf8JsonWriter writer, List<LocationEdge> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var edge in value)
        {
            writer.WriteStartObject();
            writer.WriteString("target", edge.Target);
            writer.WriteNumber("cost", edge.Cost);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
