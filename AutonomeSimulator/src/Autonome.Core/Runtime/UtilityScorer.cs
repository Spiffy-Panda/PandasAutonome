using Autonome.Core.Model;
using Autonome.Core.World;

namespace Autonome.Core.Runtime;

/// <summary>
/// Scores actions for an Autonome. Pure function — reads world state, never mutates it.
/// </summary>
public static class UtilityScorer
{
    private static readonly Dictionary<string, float> PriorityMultipliers = new()
    {
        ["low"] = 0.5f,
        ["normal"] = 1.0f,
        ["urgent"] = 1.8f,
        ["critical"] = 3.0f
    };

    public static float Score(
        AutonomeProfile profile,
        EntityState state,
        ActionDefinition action,
        WorldState world)
    {
        float score = 0f;

        // Personality multiplier (computed once per action, applied to all property terms)
        float personalityMult = 1f;
        foreach (var (axis, affinity) in action.PersonalityAffinity)
        {
            float traitValue = profile.Personality.GetValueOrDefault(axis, 0.5f);
            personalityMult *= Lerp(1f / affinity, affinity, traitValue);
        }

        // Property responses
        foreach (var (propId, response) in action.PropertyResponses)
        {
            if (!state.Properties.TryGetValue(propId, out var prop)) continue;

            float normalizedValue = NormalizeProperty(prop);
            float curveOutput = CurveEvaluator.Evaluate(response.Curve, normalizedValue);
            score += curveOutput * response.Magnitude * personalityMult;
        }

        // Modifier bonus (unified: memories + directives + passives + traits)
        var modifiers = world.Modifiers.GetModifiers(profile.Id);
        foreach (var mod in modifiers)
        {
            float bonus = mod.ActionBonus?.GetValueOrDefault(action.Id, 0f) ?? 0f;
            if (bonus == 0f) continue;

            float priorityMult = GetPriorityMultiplier(mod.Priority);
            float loyaltyMult = GetLoyaltyMultiplier(profile.Id, mod.Source, world);
            score += bonus * mod.Intensity * priorityMult * loyaltyMult;
        }

        // Noise
        float impulsiveness = profile.Personality.GetValueOrDefault("impulsiveness", 0.5f);
        score += DeterministicNoise(profile.Id, action.Id, world.Clock.Tick) * impulsiveness * 0.1f;

        return score;
    }

    public static List<ScoredAction> ScoreAllCandidates(
        AutonomeProfile profile,
        EntityState state,
        IReadOnlyList<ActionDefinition> allActions,
        WorldState world)
    {
        var results = new List<ScoredAction>();

        // Check for vital zero-lock: if any vital property is at zero, restrict to actions that address it
        var zeroedVitals = state.GetZeroedVitalProperties();
        bool vitalLockActive = zeroedVitals.Count > 0;

        foreach (var action in allActions)
        {
            if (!MeetsRequirements(profile, state, action, world)) continue;
            if (!IsAccessAllowed(profile, action)) continue;

            // Vital zero-lock filter: skip actions that don't address a zeroed vital
            if (vitalLockActive && !AddressesVitalProperty(action, zeroedVitals))
                continue;

            float score = Score(profile, state, action, world);
            score *= GetNightMultiplier(action.Category, world.Clock.GameHour);

            // Favorite multiplier
            if (profile.ActionAccess?.Favorites?.Contains(action.Id) == true)
            {
                score *= profile.ActionAccess.FavoriteMultiplier;
            }

            results.Add(new ScoredAction(action, score));
        }

        // Fallback: if vital lock produced zero candidates, re-run without the filter
        // to prevent permanent deadlock (e.g., no eat action available)
        if (vitalLockActive && results.Count == 0)
        {
            foreach (var action in allActions)
            {
                if (!MeetsRequirements(profile, state, action, world)) continue;
                if (!IsAccessAllowed(profile, action)) continue;

                float score = Score(profile, state, action, world);
                score *= GetNightMultiplier(action.Category, world.Clock.GameHour);
                if (profile.ActionAccess?.Favorites?.Contains(action.Id) == true)
                    score *= profile.ActionAccess.FavoriteMultiplier;

                results.Add(new ScoredAction(action, score));
            }
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));
        return results;
    }

    /// <summary>
    /// Returns true if the action actually restores at least one zeroed vital property
    /// via a modifyProperty step with a positive amount. PropertyResponses (scoring curves)
    /// are NOT checked — having hunger in the scoring curve doesn't mean the action feeds you.
    /// </summary>
    private static bool AddressesVitalProperty(ActionDefinition action, List<string> zeroedVitals)
    {
        foreach (var step in action.Steps)
        {
            if (step.Type == "modifyProperty"
                && step.Property != null
                && step.Amount is > 0f
                && zeroedVitals.Contains(step.Property))
            {
                return true;
            }
        }

        return false;
    }

    public static bool MeetsRequirements(
        AutonomeProfile profile,
        EntityState state,
        ActionDefinition action,
        WorldState world)
    {
        var req = action.Requirements;
        if (req == null) return true;

        if (req.Embodied.HasValue && profile.Embodied != req.Embodied.Value) return false;

        if (req.PropertyMin != null)
        {
            foreach (var (propId, minVal) in req.PropertyMin)
            {
                if (!state.Properties.TryGetValue(propId, out var prop)) return false;
                if (prop.Value < minVal) return false;
            }
        }

        if (req.PropertyBelow != null)
        {
            foreach (var (propId, maxVal) in req.PropertyBelow)
            {
                if (!state.Properties.TryGetValue(propId, out var prop)) return false;
                if (prop.Value >= maxVal) return false;
            }
        }

        // OR-based: at least ONE of the listed properties must meet its minimum
        if (req.PropertyMinAny != null)
        {
            bool anyMet = false;
            foreach (var (propId, minVal) in req.PropertyMinAny)
            {
                if (state.Properties.TryGetValue(propId, out var prop) && prop.Value >= minVal)
                {
                    anyMet = true;
                    break;
                }
            }
            if (!anyMet) return false;
        }

        if (req.TimeOfDay != null)
        {
            float hour = world.Clock.GameHour;
            if (hour < req.TimeOfDay.Min || hour > req.TimeOfDay.Max) return false;
        }

        if (req.NearbyTags != null)
        {
            var location = world.Locations.GetLocation(profile.Id);
            if (location == null) return false;
            foreach (var tag in req.NearbyTags)
            {
                if (!world.Locations.HasNearbyTag(location, tag)) return false;
            }
        }

        if (req.NoActiveModifier != null)
        {
            var modifiers = world.Modifiers.GetModifiers(profile.Id);
            foreach (var blockedId in req.NoActiveModifier)
            {
                if (modifiers.Any(m => m.Id == blockedId)) return false;
            }
        }

        // Location property requirements — check supply at entity's current location
        if (req.LocationPropertyMin != null || req.LocationPropertyBelow != null)
        {
            var loc = world.Locations.GetLocation(profile.Id);
            if (loc == null) return false;

            if (req.LocationPropertyMin != null)
            {
                foreach (var (propId, minVal) in req.LocationPropertyMin)
                {
                    var locProp = world.LocationStates.GetProperty(loc, propId);
                    if (locProp == null || locProp.Value < minVal) return false;
                }
            }

            if (req.LocationPropertyBelow != null)
            {
                foreach (var (propId, maxVal) in req.LocationPropertyBelow)
                {
                    var locProp = world.LocationStates.GetProperty(loc, propId);
                    if (locProp == null || locProp.Value >= maxVal) return false;
                }
            }
        }

        return true;
    }

    public static bool IsAccessAllowed(AutonomeProfile profile, ActionDefinition action)
    {
        var access = profile.ActionAccess;
        if (access == null) return true;

        if (access.Forbidden?.Contains(action.Id) == true) return false;

        if (access.Allowed.Count == 1 && access.Allowed[0] == "*") return true;
        if (access.Allowed.Count == 0) return false;

        return access.Allowed.Contains(action.Id);
    }

    private static float NormalizeProperty(PropertyState prop)
    {
        if (prop.Max <= prop.Min) return 0f;
        return (prop.Value - prop.Min) / (prop.Max - prop.Min);
    }

    private static float GetPriorityMultiplier(string priority)
    {
        return PriorityMultipliers.GetValueOrDefault(priority, 1.0f);
    }

    private static float GetLoyaltyMultiplier(string autonomeId, string sourceId, WorldState world)
    {
        var rel = world.Relationships.Get(sourceId, autonomeId);
        if (rel == null) return 1.0f;
        if (rel.Properties.TryGetValue("loyalty", out var loyalty))
        {
            return 0.5f + loyalty.Value * 0.5f; // Range [0.5, 1.0]
        }
        return 1.0f;
    }

    private static float GetNightMultiplier(string? category, float gameHour)
    {
        bool isNight = gameHour >= 20f || gameHour < 5f;
        if (!isNight) return 1f;

        return category switch
        {
            "work" or "trade" or "leisure" => 0.7f,
            _ => 1f
        };
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float DeterministicNoise(string autonomeId, string actionId, int tick)
    {
        int hash = HashCode.Combine(autonomeId, actionId, tick);
        return (hash & 0x7FFFFFFF) / (float)int.MaxValue * 2f - 1f; // Range [-1, 1]
    }
}

public sealed record ScoredAction(ActionDefinition Action, float Score);
