namespace Autonome.Core.Model;

/// <summary>
/// Unified profile for all Autonomes — embodied individuals, disembodied organizations,
/// and passive world objects. One schema, all scales.
/// </summary>
public sealed record AutonomeProfile(
    string Id,
    string DisplayName,
    bool Embodied,
    Dictionary<string, PropertyDefinition> Properties,
    Dictionary<string, float> Personality,
    int? EvaluationInterval,
    ActionAccess? ActionAccess = null,
    SchedulePreferences? SchedulePreferences = null,
    Identity? Identity = null,
    List<InitialModifier>? InitialModifiers = null,
    Presentation? Presentation = null,
    string? HomeLocation = null,
    List<string>? PropertySets = null
);

public sealed record ActionAccess(
    List<string> Allowed,
    List<string>? Forbidden = null,
    List<string>? Favorites = null,
    float FavoriteMultiplier = 1.4f
);

public sealed record SchedulePreferences(
    int WakeHour = 5,
    int SleepHour = 21,
    WorkHours? WorkHours = null
);

public sealed record WorkHours(int Start, int End);

public sealed record Identity(
    string? Description = null,
    string? Backstory = null,
    List<string>? CulturalTraits = null,
    List<string>? Quirks = null,
    string? Motto = null,
    List<string>? GreetingLines = null,
    List<string>? Tags = null
);

public sealed record Presentation(
    string? BodyType = null,
    string? AgeRange = null,
    List<string>? ClothingTags = null,
    string? VoiceTag = null
);

/// <summary>
/// Modifier seed in a profile — source and target are resolved at load time.
/// </summary>
public sealed record InitialModifier(
    string Type,
    Dictionary<string, float>? ActionBonus = null,
    Dictionary<string, float>? PropertyMod = null,
    float? DecayRate = null,
    float Intensity = 1.0f,
    float? Duration = null,
    string? Flavor = null
);
