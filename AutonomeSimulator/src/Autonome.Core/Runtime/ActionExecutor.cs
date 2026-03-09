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

        // Track what the entity is doing for status visibility
        var entity = world.Entities.Get(autonomeId);
        if (entity != null) entity.LastActionId = action.Id;

        for (int i = 0; i < action.Steps.Count; i++)
        {
            var step = action.Steps[i];
            var stepResult = step.Type switch
            {
                "moveTo" => HandleMoveTo(autonomeId, step, world, action, i),
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
            if (stepResult.IsDeferred) { result.Deferred = true; break; }
        }

        // Post-execution: only when action fully completed (not deferred or aborted)
        if (!result.Aborted && !result.Deferred)
            CompleteAction(autonomeId, action, world);

        return result;
    }

    /// <summary>
    /// Executes remaining action steps after a deferred moveTo arrives at destination.
    /// Called by the travel continuation phase in SimulationRunner.
    /// </summary>
    public static ExecutionResult ContinueAction(
        string autonomeId,
        TravelState travel,
        WorldState world)
    {
        var action = travel.Action;
        var result = new ExecutionResult(autonomeId, action.Id);

        var entity = world.Entities.Get(autonomeId);
        if (entity != null) entity.LastActionId = action.Id;

        for (int i = travel.PostMoveStepIndex; i < action.Steps.Count; i++)
        {
            var step = action.Steps[i];
            var stepResult = step.Type switch
            {
                "moveTo" => HandleMoveTo(autonomeId, step, world, action, i),
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
            if (stepResult.IsDeferred) { result.Deferred = true; break; }
        }

        if (!result.Aborted && !result.Deferred)
            CompleteAction(autonomeId, action, world);

        return result;
    }

    /// <summary>
    /// Post-execution: generate continuity memory + notify ModifierManager.
    /// Shared by Execute and ContinueAction.
    /// </summary>
    private static void CompleteAction(string autonomeId, ActionDefinition action, WorldState world)
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

    private static StepResult HandleMoveTo(
        string autonomeId, ActionStep step, WorldState world,
        ActionDefinition action, int stepIndex)
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

        if (currentLoc == null)
            return StepResult.Failure("Entity has no current location");

        // Get next hop on shortest path
        var hopInfo = world.Locations.GetNextHop(currentLoc, targetLoc);
        if (hopInfo == null)
            return StepResult.Failure($"No route from {currentLoc} to {targetLoc}");

        string nextHop = hopInfo.Value.NextHop;
        int hopCost = world.Locations.GetEdgeCost(currentLoc, nextHop) ?? 1;

        // Move to the next hop (physically)
        world.Locations.SetLocation(autonomeId, nextHop);

        // Set busy for this hop's travel duration
        int baseTick = Math.Max(entity.BusyUntilTick, world.Clock.Tick);
        world.Entities.SetBusy(autonomeId, baseTick + hopCost);

        // Single hop to destination — remaining steps execute normally
        if (nextHop == targetLoc)
            return StepResult.Success("moveTo");

        // Multi-hop: store travel state for continuation by SimulationRunner
        entity.Travel = new TravelState(targetLoc, action, stepIndex + 1);
        return StepResult.Deferred("moveTo");
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
        string? propId = step.Property;
        if (propId == null) return StepResult.Failure("No property specified");

        float amount = step.Amount ?? 0f;

        // Scale by acting entity's property if specified
        if (step.ScaleByEntityProperty != null)
        {
            var actor = world.Entities.Get(autonomeId);
            if (actor != null && actor.Properties.TryGetValue(step.ScaleByEntityProperty, out var scaleProp))
                amount *= scaleProp.Value;
        }

        // Scale by current location's property if specified
        if (step.ScaleByLocationProperty != null)
        {
            var currentLoc = world.Locations.GetLocation(autonomeId);
            if (currentLoc != null)
            {
                var locProp = world.LocationStates.GetProperty(currentLoc, step.ScaleByLocationProperty);
                if (locProp != null)
                    amount *= locProp.Value;
            }
        }

        // Location targeting: entity ref starts with "location:"
        if (entityRef.StartsWith("location:"))
        {
            string locRef = entityRef["location:".Length..];
            string? locationId = locRef == "current"
                ? world.Locations.GetLocation(autonomeId)
                : locRef;
            if (locationId == null) return StepResult.Failure("No current location");

            var locProp = world.LocationStates.GetProperty(locationId, propId);
            if (locProp == null) return StepResult.Success("modifyProperty"); // location has no such property, skip

            float oldLocVal = locProp.Value;
            locProp.Value = Math.Clamp(locProp.Value + amount, locProp.Min, locProp.Max);
            return StepResult.PropertyChanged(locationId, propId, oldLocVal, locProp.Value);
        }

        // Entity targeting (existing logic)
        string? entityId = ResolveEntityReference(entityRef, autonomeId, world);
        if (entityId == null) return StepResult.Failure($"Target not found: {entityRef}");

        var entity = world.Entities.Get(entityId);
        if (entity == null) return StepResult.Failure($"Entity not found: {entityId}");

        if (!entity.Properties.TryGetValue(propId, out var prop))
            return StepResult.Failure($"Property not found: {entityId}.{propId}");

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

        // Resolve "nearbyRandom" — affinity-weighted random pick (4.2)
        if (targetId == "nearbyRandom")
        {
            targetId = ResolveNearbyRandom(autonomeId, world);
            if (targetId == null)
                return StepResult.Success("socialInteraction"); // no one nearby, skip gracefully
        }

        // Resolve "nearbyFamily" — pick a family member at the same location (4.5)
        if (targetId == "nearbyFamily")
        {
            targetId = ResolveNearbyFamily(autonomeId, world);
            if (targetId == null)
                return StepResult.Success("socialInteraction"); // no family nearby, skip gracefully
        }

        if (targetId == null) return StepResult.Failure("No target for social interaction");

        // Single relationship property (backward compat)
        string? relProp = step.RelationshipProperty;
        float amount = step.RelationshipAmount ?? 0f;
        if (relProp != null)
        {
            world.Relationships.ModifyProperty(autonomeId, targetId, relProp, amount);
        }

        // Multiple relationship properties (4.1)
        if (step.RelationshipProperties != null)
        {
            foreach (var (propId, propAmount) in step.RelationshipProperties)
            {
                world.Relationships.ModifyProperty(autonomeId, targetId, propId, propAmount);
            }
        }

        // Social memory: penalize re-interacting with same target (4.6)
        CreateSocialMemory(autonomeId, targetId, world);

        // Gossip propagation: trust-weighted (4.7) with content types (4.3)
        if (step.PropagateModifiers == true)
        {
            PropagateGossip(autonomeId, targetId, world);
        }

        return StepResult.Success("socialInteraction");
    }

    /// <summary>
    /// Affinity-weighted random pick from nearby embodied entities.
    /// Friends are preferred but strangers can still be chosen. (4.2)
    /// Social memory reduces weight for recently-interacted targets. (4.6)
    /// </summary>
    private static string? ResolveNearbyRandom(string autonomeId, WorldState world)
    {
        var currentLoc = world.Locations.GetLocation(autonomeId);
        if (currentLoc == null) return null;

        var nearby = world.Locations.GetEntitiesAtLocation(currentLoc)
            .Where(id => id != autonomeId && (world.Entities.Get(id)?.Embodied ?? false))
            .ToList();

        if (nearby.Count == 0) return null;

        // Build affinity-weighted selection
        var weights = new float[nearby.Count];
        float totalWeight = 0f;

        // Check for social memory modifiers (4.6)
        var actorModifiers = world.Modifiers.GetModifiers(autonomeId);

        for (int i = 0; i < nearby.Count; i++)
        {
            var rel = world.Relationships.Get(autonomeId, nearby[i]);
            float affinity = rel?.Properties.TryGetValue("affinity", out var afProp) == true
                ? afProp.Value : 0.5f;

            // Weight formula: strangers(0.5)=1.0, friends(0.7)=1.28, best friends(1.0)=1.7
            float weight = 0.3f + affinity * 1.4f;

            // Social memory penalty: if recently interacted, halve weight (4.6)
            string memId = $"social_mem_{autonomeId}_{nearby[i]}";
            if (actorModifiers.Any(m => m.Id == memId))
                weight *= 0.5f;

            weights[i] = weight;
            totalWeight += weight;
        }

        // Deterministic weighted random selection
        float roll = DeterministicRandom(autonomeId, world.Clock.Tick) * totalWeight;
        float cumulative = 0f;
        for (int i = 0; i < nearby.Count; i++)
        {
            cumulative += weights[i];
            if (roll <= cumulative)
                return nearby[i];
        }

        return nearby[nearby.Count - 1]; // fallback
    }

    /// <summary>
    /// Pick a family member (spouse/family tagged relationship) at the same location. (4.5)
    /// </summary>
    private static string? ResolveNearbyFamily(string autonomeId, WorldState world)
    {
        var currentLoc = world.Locations.GetLocation(autonomeId);
        if (currentLoc == null) return null;

        var familyIds = world.Relationships.GetBySource(autonomeId)
            .Where(r => r.Tags.Contains("spouse") || r.Tags.Contains("family"))
            .Select(r => r.Target)
            .ToList();

        // Also check reverse direction (target looking up source)
        foreach (var r in world.Relationships.GetByTarget(autonomeId))
        {
            if ((r.Tags.Contains("spouse") || r.Tags.Contains("family")) && !familyIds.Contains(r.Source))
                familyIds.Add(r.Source);
        }

        foreach (var fId in familyIds)
        {
            var fLoc = world.Locations.GetLocation(fId);
            if (fLoc == currentLoc && (world.Entities.Get(fId)?.Embodied ?? false))
                return fId;
        }

        return null;
    }

    /// <summary>
    /// Create a short-lived social memory modifier that discourages repeat interactions. (4.6)
    /// </summary>
    private static void CreateSocialMemory(string autonomeId, string targetId, WorldState world)
    {
        string memId = $"social_mem_{autonomeId}_{targetId}";

        // Refresh if already exists
        foreach (var m in world.Modifiers.GetModifiers(autonomeId))
        {
            if (m.Id == memId) { m.Duration = 100f; m.Intensity = 0.8f; return; }
        }

        var memory = new Modifier
        {
            Id = memId,
            Source = autonomeId,
            Type = "social_memory",
            Target = autonomeId,
            Duration = 100f,
            DecayRate = 0.005f,
            Intensity = 0.8f,
            SocialTarget = targetId
        };
        world.Modifiers.Add(memory);
    }

    /// <summary>
    /// Trust-weighted gossip propagation with content types. (4.3 + 4.7)
    /// Higher trust between actor and target = stronger gossip pass-through.
    /// </summary>
    private static void PropagateGossip(string autonomeId, string targetId, WorldState world)
    {
        // Get trust between actor and target for intensity scaling (4.7)
        var rel = world.Relationships.Get(autonomeId, targetId);
        float trust = rel?.Properties.TryGetValue("trust", out var trustProp) == true
            ? trustProp.Value : 0.5f;

        foreach (var mod in world.Modifiers.GetModifiers(autonomeId))
        {
            if (!mod.Gossip) continue;
            if (mod.Duration is null or <= 0) continue;

            // Don't propagate if target already has this gossip
            bool alreadyHas = world.Modifiers.GetModifiers(targetId)
                .Any(m => m.Id == mod.Id && m.Gossip);
            if (alreadyHas) continue;

            // Trust-weighted intensity: high trust(0.8) = 0.4x, low trust(0.2) = 0.1x (4.7)
            float copiedIntensity = mod.Intensity * 0.5f * trust;

            var copy = new Modifier
            {
                Id = mod.Id,
                Source = mod.Source,
                Type = mod.Type,
                Target = targetId,
                ActionBonus = mod.ActionBonus,
                AffinityMod = mod.AffinityMod,
                Duration = mod.Duration / 2f,
                DecayRate = mod.DecayRate,
                Intensity = copiedIntensity,
                Priority = mod.Priority,
                Flavor = mod.Flavor,
                Gossip = true,
                GossipType = mod.GossipType,
                GossipLocation = mod.GossipLocation
            };
            world.Modifiers.Add(copy);
        }
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
    public bool Deferred { get; set; }

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
    public bool IsDeferred { get; init; }
    public string? Message { get; init; }
    public string? EntityId { get; init; }
    public string? PropertyId { get; init; }
    public float? OldValue { get; init; }
    public float? NewValue { get; init; }
    public int? DirectiveTargetCount { get; init; }

    public static StepResult Success(string stepType) => new() { StepType = stepType };
    public static StepResult Failure(string message) => new() { Failed = true, Message = message };
    public static StepResult Deferred(string stepType) => new() { StepType = stepType, IsDeferred = true };
    public static StepResult UnknownType(string type) => new() { Failed = true, Message = $"Unknown step type: {type}" };

    public static StepResult PropertyChanged(string entityId, string propertyId, float oldValue, float newValue)
        => new() { StepType = "modifyProperty", EntityId = entityId, PropertyId = propertyId, OldValue = oldValue, NewValue = newValue };

    public static StepResult DirectiveEmitted(string directiveId, int targetCount)
        => new() { StepType = "emitDirective", Message = directiveId, DirectiveTargetCount = targetCount };
}
