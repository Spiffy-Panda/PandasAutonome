using Autonome.Core.Model;
using Autonome.Core.World;

namespace Autonome.Core.Runtime;

/// <summary>
/// Walks action step sequences and dispatches to handlers.
/// </summary>
public static class ActionExecutor
{
    public static ExecutionResult Execute(
        string autonomeId,
        ActionDefinition action,
        WorldState world)
    {
        var result = new ExecutionResult(autonomeId, action.Id);

        foreach (var step in action.Steps)
        {
            var stepResult = step.Type switch
            {
                "moveTo" => HandleMoveTo(autonomeId, step, world),
                "animate" => HandleAnimate(autonomeId, step, world),
                "wait" => HandleWait(autonomeId, step, world),
                "modifyProperty" => HandleModifyProperty(autonomeId, step, world),
                "emitDirective" => HandleEmitDirective(autonomeId, step, world),
                "emitEvent" => HandleEmitEvent(autonomeId, step, world),
                "socialInteraction" => HandleSocial(autonomeId, step, world),
                _ => StepResult.UnknownType(step.Type)
            };

            result.StepResults.Add(stepResult);
            if (stepResult.Failed) { result.Aborted = true; break; }
        }

        // Post-execution: generate continuity memory + notify ModifierManager
        if (!result.Aborted)
        {
            if (action.MemoryGeneration is { } memGen)
            {
                string memId = $"mem_{autonomeId}_{action.Id}";
                Modifier? existing = null;
                foreach (var m in world.Modifiers.GetModifiers(autonomeId))
                {
                    if (m.Id == memId) { existing = m; break; }
                }

                if (existing != null)
                {
                    if (memGen.StackMode == "accumulate")
                        existing.Intensity = Math.Min(existing.Intensity + memGen.Intensity, memGen.MaxIntensity);
                    else
                        existing.Intensity = memGen.Intensity;

                    if (memGen.Duration.HasValue)
                        existing.Duration = memGen.Duration;
                }
                else
                {
                    var memory = new Modifier
                    {
                        Id = memId,
                        Source = autonomeId,
                        Type = "memory",
                        Target = autonomeId,
                        ActionBonus = memGen.ActionBonus,
                        PropertyMod = memGen.PropertyMod,
                        DecayRate = memGen.DecayRate,
                        Intensity = memGen.Intensity,
                        Duration = memGen.Duration,
                        Flavor = memGen.Flavor
                    };
                    world.Modifiers.Add(memory);
                }
            }

            world.Modifiers.OnActionCompleted(autonomeId, action.Id, world);
        }

        return result;
    }

    private static StepResult HandleMoveTo(string autonomeId, ActionStep step, WorldState world)
    {
        var entity = world.Entities.Get(autonomeId);
        if (entity == null || !entity.Embodied) return StepResult.Failure("Not embodied or not found");

        if (step.Target == null) return StepResult.Success("moveTo");

        // Resolve "home" target from entity's HomeLocation, with fallback
        string? targetLoc;
        if (step.Target == "home")
        {
            targetLoc = entity.HomeLocation;
            if (targetLoc == null)
                targetLoc = world.Locations.ResolveTarget(autonomeId, "nearestTagged:home");
        }
        else
        {
            targetLoc = world.Locations.ResolveTarget(autonomeId, step.Target);
        }
        if (targetLoc == null) return StepResult.Failure($"Cannot resolve target: {step.Target}");

        string? currentLoc = world.Locations.GetLocation(autonomeId);
        if (currentLoc == targetLoc)
            return StepResult.Success("moveTo"); // already there

        // Calculate travel cost, then teleport + add busy time
        int travelCost = currentLoc != null
            ? world.Locations.GetTravelCost(currentLoc, targetLoc)
            : 0;

        world.Locations.SetLocation(autonomeId, targetLoc);

        if (travelCost > 0 && travelCost < int.MaxValue)
        {
            int baseTick = Math.Max(entity.BusyUntilTick, world.Clock.Tick);
            world.Entities.SetBusy(autonomeId, baseTick + travelCost);
        }

        return StepResult.Success("moveTo");
    }

    private static StepResult HandleAnimate(string autonomeId, ActionStep step, WorldState world)
    {
        // No-op in headless sim, recorded for history
        return StepResult.Success("animate");
    }

    private static StepResult HandleWait(string autonomeId, ActionStep step, WorldState world)
    {
        float duration = step.Duration ?? 0f;
        if (step.DurationMin.HasValue && step.DurationMax.HasValue)
        {
            // Deterministic "random" duration based on tick
            float t = DeterministicRandom(autonomeId, world.Clock.Tick);
            duration = step.DurationMin.Value + (step.DurationMax.Value - step.DurationMin.Value) * t;
        }

        world.Entities.SetBusy(autonomeId, world.Clock.Tick + (int)duration);
        return StepResult.Success("wait");
    }

    private static StepResult HandleModifyProperty(string autonomeId, ActionStep step, WorldState world)
    {
        string entityRef = step.Entity ?? "self";
        string? entityId = ResolveEntityReference(entityRef, autonomeId, world);
        if (entityId == null) return StepResult.Failure($"Target not found: {entityRef}");

        string? propId = step.Property;
        if (propId == null) return StepResult.Failure("No property specified");

        var entity = world.Entities.Get(entityId);
        if (entity == null) return StepResult.Failure($"Entity not found: {entityId}");

        if (!entity.Properties.TryGetValue(propId, out var prop))
            return StepResult.Failure($"Property not found: {entityId}.{propId}");

        float amount = step.Amount ?? 0f;

        // Scale by acting entity's property if specified
        if (step.ScaleByEntityProperty != null)
        {
            var actor = world.Entities.Get(autonomeId);
            if (actor != null && actor.Properties.TryGetValue(step.ScaleByEntityProperty, out var scaleProp))
                amount *= scaleProp.Value;
        }

        float oldValue = prop.Value;
        prop.Value = Math.Clamp(prop.Value + amount, prop.Min, prop.Max);

        return StepResult.PropertyChanged(entityId, propId, oldValue, prop.Value);
    }

    private static StepResult HandleEmitDirective(string autonomeId, ActionStep step, WorldState world)
    {
        var template = step.Modifier;
        if (template == null) return StepResult.Failure("No modifier template");

        // Self-targeting: create a single modifier on the emitter
        if (template.Target?.Scope == "self")
        {
            var modifier = new Modifier
            {
                Id = template.Id,
                Source = autonomeId,
                Type = template.Type,
                Target = autonomeId,
                ActionBonus = template.ActionBonus,
                PropertyMod = template.PropertyMod,
                Duration = template.Duration,
                DecayRate = template.DecayRate,
                Intensity = 1.0f,
                Priority = template.Priority,
                Flavor = template.Flavor,
                Gossip = template.Gossip
            };
            world.Modifiers.Add(modifier);
            return StepResult.DirectiveEmitted(template.Id, 1);
        }

        // Resolve targets via authority graph
        var targetIds = new List<string>();
        if (template.Target != null)
        {
            var filter = template.Target.Filter;
            targetIds = world.AuthorityGraph.GetSubordinates(
                autonomeId,
                template.Target.Depth,
                id =>
                {
                    if (filter == null) return true;
                    var e = world.Entities.Get(id);
                    if (e == null) return false;
                    if (filter.Embodied.HasValue && e.Embodied != filter.Embodied.Value) return false;
                    if (filter.Tags != null && e.Identity?.Tags != null)
                    {
                        foreach (var tag in filter.Tags)
                        {
                            if (!e.Identity.Tags.Contains(tag)) return false;
                        }
                    }
                    return true;
                });
        }

        int created = 0;
        foreach (var targetId in targetIds)
        {
            var modifier = new Modifier
            {
                Id = $"{template.Id}_{world.Clock.Tick}",
                Source = autonomeId,
                Type = template.Type,
                Target = targetId,
                ActionBonus = template.ActionBonus,
                PropertyMod = template.PropertyMod,
                Duration = template.Duration,
                DecayRate = template.DecayRate,
                Intensity = 1.0f,
                CompletionReward = template.CompletionReward,
                MaxClaims = template.MaxClaims,
                Priority = template.Priority,
                Flavor = template.Flavor,
                Gossip = template.Gossip
            };
            world.Modifiers.Add(modifier);
            created++;
        }

        return StepResult.DirectiveEmitted(template.Id, created);
    }

    private static StepResult HandleEmitEvent(string autonomeId, ActionStep step, WorldState world)
    {
        // Record event for history
        if (step.Event != null)
        {
            world.History.RecordEvent(autonomeId, step.Event, world.Clock.Tick);
        }
        return StepResult.Success("emitEvent");
    }

    private static StepResult HandleSocial(string autonomeId, ActionStep step, WorldState world)
    {
        string? targetId = step.TargetEntity;

        // Resolve "nearbyRandom" — pick a random embodied entity at the same location
        if (targetId == "nearbyRandom")
        {
            var currentLoc = world.Locations.GetLocation(autonomeId);
            if (currentLoc == null)
                return StepResult.Success("socialInteraction"); // no location, skip gracefully

            var nearby = world.Locations.GetEntitiesAtLocation(currentLoc)
                .Where(id => id != autonomeId && (world.Entities.Get(id)?.Embodied ?? false))
                .ToList();

            if (nearby.Count == 0)
                return StepResult.Success("socialInteraction"); // no one nearby, skip gracefully

            int index = Math.Abs(HashCode.Combine(autonomeId, world.Clock.Tick)) % nearby.Count;
            targetId = nearby[index];
        }

        if (targetId == null) return StepResult.Failure("No target for social interaction");

        string? relProp = step.RelationshipProperty;
        float amount = step.RelationshipAmount ?? 0f;

        if (relProp != null)
        {
            world.Relationships.ModifyProperty(autonomeId, targetId, relProp, amount);
        }

        // Gossip propagation: copy gossip-flagged modifiers from actor to target
        if (step.PropagateModifiers == true)
        {
            foreach (var mod in world.Modifiers.GetModifiers(autonomeId))
            {
                if (!mod.Gossip) continue;
                if (mod.Duration is null or <= 0) continue;

                // Don't propagate if target already has this gossip
                bool alreadyHas = world.Modifiers.GetModifiers(targetId)
                    .Any(m => m.Id == mod.Id && m.Gossip);
                if (alreadyHas) continue;

                var copy = new Modifier
                {
                    Id = mod.Id,
                    Source = mod.Source,
                    Type = mod.Type,
                    Target = targetId,
                    ActionBonus = mod.ActionBonus,
                    PropertyMod = mod.PropertyMod,
                    AffinityMod = mod.AffinityMod,
                    Duration = mod.Duration / 2f,
                    DecayRate = mod.DecayRate,
                    Intensity = mod.Intensity * 0.5f,
                    Priority = mod.Priority,
                    Flavor = mod.Flavor,
                    Gossip = true
                };
                world.Modifiers.Add(copy);
            }
        }

        return StepResult.Success("socialInteraction");
    }

    private static string? ResolveEntityReference(string reference, string selfId, WorldState world)
    {
        if (reference == "self") return selfId;

        if (reference.StartsWith("target:"))
            return reference["target:".Length..];

        if (reference.StartsWith("nearest:tagged:"))
        {
            string tag = reference["nearest:tagged:".Length..];
            return world.Locations.FindNearestWithTag(selfId, tag);
        }

        if (reference.StartsWith("nearest:"))
        {
            string tag = reference["nearest:".Length..];
            return world.Locations.FindNearestWithTag(selfId, tag);
        }

        return reference;
    }

    private static float DeterministicRandom(string seed, int tick)
    {
        int hash = HashCode.Combine(seed, tick);
        return (hash & 0x7FFFFFFF) / (float)int.MaxValue;
    }
}

public sealed class ExecutionResult
{
    public string AutonomeId { get; }
    public string ActionId { get; }
    public List<StepResult> StepResults { get; } = [];
    public bool Aborted { get; set; }

    public ExecutionResult(string autonomeId, string actionId)
    {
        AutonomeId = autonomeId;
        ActionId = actionId;
    }
}

public sealed class StepResult
{
    public string StepType { get; init; } = "";
    public bool Failed { get; init; }
    public string? Message { get; init; }
    public string? EntityId { get; init; }
    public string? PropertyId { get; init; }
    public float? OldValue { get; init; }
    public float? NewValue { get; init; }
    public int? DirectiveTargetCount { get; init; }

    public static StepResult Success(string stepType) => new() { StepType = stepType };
    public static StepResult Failure(string message) => new() { Failed = true, Message = message };
    public static StepResult UnknownType(string type) => new() { Failed = true, Message = $"Unknown step type: {type}" };

    public static StepResult PropertyChanged(string entityId, string propertyId, float oldValue, float newValue)
        => new() { StepType = "modifyProperty", EntityId = entityId, PropertyId = propertyId, OldValue = oldValue, NewValue = newValue };

    public static StepResult DirectiveEmitted(string directiveId, int targetCount)
        => new() { StepType = "emitDirective", Message = directiveId, DirectiveTargetCount = targetCount };
}
