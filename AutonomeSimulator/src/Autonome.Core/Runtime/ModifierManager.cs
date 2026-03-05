using Autonome.Core.Model;
using Autonome.Core.World;

namespace Autonome.Core.Runtime;

/// <summary>
/// Unified modifier lifecycle manager. Replaces DirectiveRouter + MemoryManager + passive effect system.
/// Stores all active modifiers indexed by target Autonome ID.
/// </summary>
public class ModifierManager
{
    private readonly Dictionary<string, List<Modifier>> _byTarget = new();
    private readonly Dictionary<string, List<Modifier>> _bySource = new();
    private readonly List<Modifier> _toRemove = [];

    public void Add(Modifier mod)
    {
        if (!_byTarget.TryGetValue(mod.Target, out var targetList))
        {
            targetList = [];
            _byTarget[mod.Target] = targetList;
        }
        targetList.Add(mod);

        if (!_bySource.TryGetValue(mod.Source, out var sourceList))
        {
            sourceList = [];
            _bySource[mod.Source] = sourceList;
        }
        sourceList.Add(mod);
    }

    public void Remove(Modifier mod)
    {
        if (_byTarget.TryGetValue(mod.Target, out var targetList))
            targetList.Remove(mod);
        if (_bySource.TryGetValue(mod.Source, out var sourceList))
            sourceList.Remove(mod);
    }

    public List<Modifier> GetModifiers(string targetId)
    {
        return _byTarget.TryGetValue(targetId, out var list) ? list : [];
    }

    public IEnumerable<Modifier> AllModifiers()
    {
        return _byTarget.Values.SelectMany(list => list);
    }

    /// <summary>
    /// Tick modifier lifecycles: decrement duration, apply intensity decay, remove expired.
    /// </summary>
    public void Tick(float delta)
    {
        _toRemove.Clear();

        foreach (var mod in AllModifiers())
        {
            // Duration countdown
            if (mod.Duration.HasValue)
            {
                mod.Duration -= delta;
                if (mod.Duration <= 0)
                {
                    _toRemove.Add(mod);
                    continue;
                }
            }

            // Intensity decay (for memory-type modifiers)
            if (mod.DecayRate.HasValue)
            {
                mod.Intensity -= mod.DecayRate.Value * delta;
                if (mod.Intensity <= 0)
                {
                    _toRemove.Add(mod);
                }
            }
        }

        foreach (var mod in _toRemove)
        {
            Remove(mod);
        }
    }

    /// <summary>
    /// Called when an Autonome completes an action — delivers rewards and increments claims.
    /// </summary>
    public void OnActionCompleted(string autonomeId, string actionId, WorldState world)
    {
        var modifiers = GetModifiers(autonomeId);
        _toRemove.Clear();

        foreach (var mod in modifiers)
        {
            if (mod.ActionBonus == null) continue;
            if (!mod.ActionBonus.ContainsKey(actionId)) continue;
            if (mod.CompletionReward == null) continue;

            DeliverReward(autonomeId, mod, world);

            mod.CurrentClaims++;
            if (mod.MaxClaims.HasValue && mod.CurrentClaims >= mod.MaxClaims)
            {
                _toRemove.Add(mod);
            }
        }

        foreach (var mod in _toRemove)
        {
            Remove(mod);
        }
    }

    /// <summary>
    /// Emit a passive modifier. Passives are set, not accumulated — replaces any existing passive from same rule.
    /// </summary>
    public void EmitPassive(PassiveEffectRule rule, string sourceAutonomeId, WorldState world)
    {
        string passiveId = $"passive_{sourceAutonomeId}_{rule.Condition}_{rule.Threshold}";
        var emission = rule.Emit;

        // Check if already emitted
        if (_bySource.TryGetValue(sourceAutonomeId, out var existing))
        {
            if (existing.Any(m => m.Id == passiveId && m.Type == "passive"))
                return; // Already active
        }

        // Resolve targets
        var targetIds = emission.Target switch
        {
            "self" => new List<string> { sourceAutonomeId },
            "subordinates" => world.AuthorityGraph.GetSubordinates(sourceAutonomeId, emission.Depth, null),
            _ => new List<string>()
        };

        foreach (var targetId in targetIds)
        {
            var modifier = new Modifier
            {
                Id = passiveId,
                Source = sourceAutonomeId,
                Type = "passive",
                Target = targetId,
                PropertyMod = emission.PropertyMod,
                ActionBonus = emission.ActionBonus,
                Duration = null, // Passives are recalculated each tick
                Intensity = 1.0f,
                Flavor = emission.Flavor
            };
            Add(modifier);
        }
    }

    /// <summary>
    /// Clear passive modifiers from a specific rule when the condition is no longer met.
    /// </summary>
    public void ClearPassive(PassiveEffectRule rule, string sourceAutonomeId)
    {
        string passiveId = $"passive_{sourceAutonomeId}_{rule.Condition}_{rule.Threshold}";

        if (!_bySource.TryGetValue(sourceAutonomeId, out var existing)) return;

        var toRemove = existing.Where(m => m.Id == passiveId && m.Type == "passive").ToList();
        foreach (var mod in toRemove)
        {
            Remove(mod);
        }
    }

    private static void DeliverReward(string autonomeId, Modifier mod, WorldState world)
    {
        var reward = mod.CompletionReward!;

        if (reward.PropertyChanges != null)
        {
            var entity = world.Entities.Get(autonomeId);
            if (entity != null)
            {
                foreach (var (propId, amount) in reward.PropertyChanges)
                {
                    if (entity.Properties.TryGetValue(propId, out var prop))
                    {
                        prop.Value = Math.Clamp(prop.Value + amount, prop.Min, prop.Max);
                    }
                }
            }
        }

        if (reward.RelationshipMod != null)
        {
            string targetId = reward.RelationshipMod.Target == "source" ? mod.Source : reward.RelationshipMod.Target;
            world.Relationships.ModifyProperty(autonomeId, targetId, reward.RelationshipMod.Property, reward.RelationshipMod.Amount);
        }
    }
}
