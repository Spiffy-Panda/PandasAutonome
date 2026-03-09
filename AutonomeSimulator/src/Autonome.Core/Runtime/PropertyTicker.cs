using Autonome.Core.Model;
using Autonome.Core.World;
using Autonome.Core.Graph;

namespace Autonome.Core.Runtime;

/// <summary>
/// Unified tick system: decay + aggregation + passive effect emission.
/// Replaces NeedTicker + AggregationEngine + passive effect system.
/// </summary>
public static class PropertyTicker
{
    public static void TickAll(WorldState world, float delta)
    {
        foreach (var (autonomeId, entity) in world.Entities.All())
        {
            TickEntity(autonomeId, entity, world, delta);
        }

        TickRelationships(world, delta);
        TickLocations(world, delta);
    }

    /// <summary>
    /// Periodic rent drain: embodied NPCs pay rent based on homeQuality.
    /// Gold flows from NPCs to the org that owns their home district.
    /// rent_per_tick = homeQuality * 30 / 480 (one game-day cycle).
    /// </summary>
    public static void TickUpkeep(WorldState world, float delta)
    {
        const float rentPerCycle = 35f;
        const float cycleLength = 480f;

        foreach (var (_, entity) in world.Entities.All())
        {
            if (!entity.Embodied) continue;
            if (entity.HomeLocation == null) continue;
            if (!entity.Properties.TryGetValue("homeQuality", out var hqProp)) continue;
            if (hqProp.Value <= 0f) continue;
            if (!entity.Properties.TryGetValue("gold", out var goldProp)) continue;
            if (goldProp.Value <= 0f) continue;

            float rentDrain = hqProp.Value * rentPerCycle / cycleLength * delta;
            float actualDrain = Math.Min(rentDrain, goldProp.Value);
            goldProp.Value -= actualDrain;

            string? ownerId = GetLocationOwner(entity.HomeLocation);
            if (ownerId != null)
            {
                var owner = world.Entities.Get(ownerId);
                if (owner != null && owner.Properties.TryGetValue("gold", out var ownerGold))
                {
                    ownerGold.Value += actualDrain;
                }
            }
        }
    }

    private static string? GetLocationOwner(string homeLocation)
    {
        if (homeLocation.StartsWith("city.manor_district."))
            return "noble_lord_ashworth";
        if (homeLocation.StartsWith("city."))
            return "org_city_council";
        if (homeLocation.StartsWith("hinterland.farmland."))
            return "town_millhaven";
        if (homeLocation.StartsWith("hinterland.quarry."))
            return "town_ironforge";
        if (homeLocation.StartsWith("hinterland.woodlands."))
            return "town_thornwatch";
        return null;
    }

    /// <summary>
    /// Decay relationship properties toward neutral (0.5). Loyalty drifts to indifferent, not hostile.
    /// </summary>
    public static void TickRelationships(WorldState world, float delta)
    {
        const float neutral = 0.5f;
        foreach (var rel in world.Relationships.All())
        {
            foreach (var (_, prop) in rel.Properties)
            {
                if (prop.DecayRate == 0f) continue;

                float decay = prop.DecayRate * delta;
                if (prop.Value > neutral)
                    prop.Value = Math.Max(neutral, prop.Value - decay);
                else if (prop.Value < neutral)
                    prop.Value = Math.Min(neutral, prop.Value + decay);

                prop.Clamp();
            }
        }
    }

    /// <summary>
    /// Decay location properties (food spoils, supplies deplete).
    /// Stock-proportional and time-scaled: loss = value * decayRate * minutesPerTick * delta.
    /// decayRate is fractional loss per game-minute; independent of tick granularity.
    /// </summary>
    public static void TickLocations(WorldState world, float delta)
    {
        float mpt = world.Clock.MinutesPerTick;
        foreach (var (_, props) in world.LocationStates.All())
        {
            foreach (var (_, prop) in props)
            {
                if (prop.DecayRate != 0f)
                {
                    prop.Value -= prop.Value * prop.DecayRate * mpt * delta;
                    prop.Clamp();
                }
            }
        }
    }

    public static void TickEntity(string autonomeId, EntityState entity, WorldState world, float delta)
    {
        foreach (var (propId, prop) in entity.Properties)
        {
            // 1. DECAY
            if (prop.DecayRate != 0f)
            {
                prop.Value -= prop.DecayRate * delta;
                prop.Clamp();
            }

            // 2. AGGREGATE (if property has aggregation expressions)
            if (prop.Aggregations != null && ShouldAggregate(entity, world))
            {
                foreach (var agg in prop.Aggregations)
                {
                    float rawValue = ComputeAggregation(autonomeId, agg, world);
                    ApplyBlend(prop, rawValue, agg.Weight, agg.Blend);
                }
                prop.Clamp();
            }

            // 3. PASSIVE EFFECTS
            if (prop.PassiveEffects != null)
            {
                foreach (var rule in prop.PassiveEffects)
                {
                    if (IsConditionMet(prop, rule))
                    {
                        world.Modifiers.EmitPassive(rule, autonomeId, world);
                    }
                    else
                    {
                        world.Modifiers.ClearPassive(rule, autonomeId);
                    }
                }
            }
        }
    }

    private static bool ShouldAggregate(EntityState entity, WorldState world)
    {
        if (entity.EvaluationInterval == null) return false;
        return world.Clock.Tick % entity.EvaluationInterval.Value == 0;
    }

    private static float ComputeAggregation(string autonomeId, AggregationExpr agg, WorldState world)
    {
        var subordinates = world.AuthorityGraph.GetSubordinates(autonomeId, agg.Depth, id =>
        {
            if (agg.Filter == null) return true;
            var e = world.Entities.Get(id);
            if (e == null) return false;
            if (agg.Filter.Embodied.HasValue && e.Embodied != agg.Filter.Embodied.Value) return false;
            if (agg.Filter.Tags != null)
            {
                var identity = e.Identity;
                if (identity?.Tags == null) return false;
                foreach (var tag in agg.Filter.Tags)
                {
                    if (!identity.Tags.Contains(tag)) return false;
                }
            }
            return true;
        });

        if (subordinates.Count == 0) return 0f;

        return agg.Function switch
        {
            AggregationFunction.Avg => subordinates
                .Select(id => GetPropertyValue(id, agg.Property, world))
                .Average(),
            AggregationFunction.Sum => subordinates
                .Select(id => GetPropertyValue(id, agg.Property, world))
                .Sum(),
            AggregationFunction.Min => subordinates
                .Select(id => GetPropertyValue(id, agg.Property, world))
                .Min(),
            AggregationFunction.Max => subordinates
                .Select(id => GetPropertyValue(id, agg.Property, world))
                .Max(),
            AggregationFunction.Count => subordinates.Count,
            AggregationFunction.Ratio => throw new NotImplementedException("AggregationFunction.Ratio is not implemented"),
            _ => 0f
        };
    }

    private static float GetPropertyValue(string entityId, string propertyId, WorldState world)
    {
        if (propertyId == "_count") return 1f;
        var entity = world.Entities.Get(entityId);
        if (entity == null) return 0f;
        return entity.Properties.TryGetValue(propertyId, out var prop) ? prop.Value : 0f;
    }

    private static void ApplyBlend(PropertyState prop, float aggregatedValue, float weight, BlendMode blend)
    {
        switch (blend)
        {
            case BlendMode.Replace:
                prop.Value = aggregatedValue;
                break;
            case BlendMode.Lerp:
                prop.Value = prop.Value + (aggregatedValue - prop.Value) * weight;
                break;
            case BlendMode.Additive:
                prop.Value += (aggregatedValue - 0.5f) * weight;
                break;
            case BlendMode.Min:
                prop.Value = Math.Min(prop.Value, aggregatedValue);
                break;
            case BlendMode.Max:
                prop.Value = Math.Max(prop.Value, aggregatedValue);
                break;
        }
    }

    private static bool IsConditionMet(PropertyState prop, PassiveEffectRule rule)
    {
        float threshold = rule.Threshold ?? prop.Critical ?? 0f;
        return rule.Condition switch
        {
            "below_critical" => prop.Critical.HasValue && prop.Value < prop.Critical.Value,
            "below" => prop.Value < threshold,
            "above" => prop.Value > threshold,
            _ => false
        };
    }
}
