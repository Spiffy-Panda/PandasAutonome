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
            AggregationFunction.Ratio => 0f, // TODO: implement sub-filter ratio
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
