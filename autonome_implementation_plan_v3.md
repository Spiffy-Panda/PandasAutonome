# Autonome System — C# Implementation Plan (v3)

## Overview

A pure C# console application that loads Autonome data from JSON, runs the simulation, and outputs a detailed JSON history file. No game engine. No UI. The simulation core proving the system works.

This plan reflects the v3 spec: unified StatefulEntity/Modifier/Relationship abstractions, authority DAG instead of tier enums, piecewise Bezier response curves, and data-driven aggregation.

---

## 1. Solution Structure

```
AutonomeSimulator/
├── AutonomeSimulator.sln
│
├── src/
│   ├── Autonome.Core/
│   │   ├── Autonome.Core.csproj
│   │   │
│   │   ├── Model/                       # Pure data — deserialized from JSON
│   │   │   ├── Property.cs              # The universal state unit
│   │   │   ├── AutonomeProfile.cs       # Unified profile (embodied + disembodied)
│   │   │   ├── ActionDefinition.cs      # Actions with steps and property responses
│   │   │   ├── ActionStep.cs            # Individual step in an action sequence
│   │   │   ├── Modifier.cs              # Unified: memories, directives, passives, traits
│   │   │   ├── Relationship.cs          # Unified: social, authority, affiliation, ownership
│   │   │   ├── ResponseCurve.cs         # Piecewise Hermite curve
│   │   │   ├── Keyframe.cs             # Single keyframe (time, value, tangent slopes)
│   │   │   ├── AggregationExpr.cs       # Declarative aggregation on a property
│   │   │   ├── PassiveEffectRule.cs     # Threshold-triggered modifier emission
│   │   │   ├── DirectiveTarget.cs       # Scope + depth + filter for directive targeting
│   │   │   ├── Reward.cs               # Completion reward on directive-type modifiers
│   │   │   └── LocationDefinition.cs
│   │   │
│   │   ├── Runtime/                     # Stateless processors (operate on world state)
│   │   │   ├── PropertyTicker.cs        # Decay + aggregation + passive emission (one system)
│   │   │   ├── UtilityScorer.cs         # Score actions for an Autonome
│   │   │   ├── ActionExecutor.cs        # Walk step sequences, dispatch to handlers
│   │   │   ├── ModifierManager.cs       # Lifecycle, lookup, claim tracking (one system)
│   │   │   └── CurveEvaluator.cs        # Evaluate piecewise Bezier curves
│   │   │
│   │   ├── Graph/                       # Authority DAG operations
│   │   │   ├── AuthorityGraph.cs        # Build, query, validate the DAG
│   │   │   └── GraphTraversal.cs        # Subordinates, peers, kin traversals
│   │   │
│   │   ├── Simulation/                  # Tick loop orchestration
│   │   │   ├── SimulationClock.cs
│   │   │   ├── SimulationRunner.cs
│   │   │   └── EvaluationScheduler.cs   # Per-Autonome interval scheduling
│   │   │
│   │   └── World/                       # Mutable world state container
│   │       ├── WorldState.cs            # Single source of truth
│   │       ├── EntityRegistry.cs        # All Autonomes + runtime state
│   │       ├── ModifierRegistry.cs      # Active modifiers indexed by target
│   │       ├── RelationshipStore.cs     # All relationships, queryable
│   │       └── LocationGraph.cs         # Spatial index for embodied Autonomes
│   │
│   ├── Autonome.Data/                   # JSON loading + validation
│   │   ├── Autonome.Data.csproj
│   │   ├── DataLoader.cs
│   │   ├── SchemaValidator.cs
│   │   ├── CurvePresetLibrary.cs        # Resolve preset names → control points
│   │   └── VocabularyRegistry.cs
│   │
│   ├── Autonome.History/               # Event recording + export
│   │   ├── Autonome.History.csproj
│   │   ├── HistoryRecorder.cs
│   │   ├── HistoryEntry.cs
│   │   ├── SnapshotBuilder.cs
│   │   └── HistoryExporter.cs
│   │
│   └── Autonome.Cli/                   # Console entry point
│       ├── Autonome.Cli.csproj
│       ├── Program.cs
│       └── CliOptions.cs
│
├── data/                                # JSON content
│   ├── properties/                      # Property archetype definitions
│   ├── curves.json                      # Named curve presets
│   ├── actions/
│   ├── autonomes/
│   ├── relationships/
│   ├── locations/
│   └── vocabulary/
│
├── tests/
│   ├── Autonome.Core.Tests/
│   │   ├── CurveEvaluatorTests.cs
│   │   ├── PropertyTickerTests.cs
│   │   ├── UtilityScorerTests.cs
│   │   ├── ActionExecutorTests.cs
│   │   ├── ModifierManagerTests.cs
│   │   ├── AuthorityGraphTests.cs
│   │   └── AggregationTests.cs
│   │
│   ├── Autonome.Data.Tests/
│   │   ├── DataLoaderTests.cs
│   │   ├── SchemaValidatorTests.cs
│   │   └── CurvePresetTests.cs
│   │
│   └── Autonome.Integration.Tests/
│       ├── SingleAutonomeTests.cs
│       ├── DirectiveFlowTests.cs
│       ├── AggregationFeedbackTests.cs
│       ├── AuthorityCascadeTests.cs
│       └── WorldObjectInteractionTests.cs
│
└── samples/
    ├── minimal/                         # 3 embodied, 0 disembodied
    ├── village/                         # 20 embodied, 1 town, 1 guild
    └── valley/                          # 50+ embodied, 3 towns, 2 guilds, sub-guild
```

---

## 2. Dependencies

**Target:** .NET 8 (LTS), console application.

| Package                      | Purpose                    |
|------------------------------|----------------------------|
| `System.Text.Json`           | JSON serialization (built-in) |
| `System.CommandLine`         | CLI argument parsing       |
| `xunit` + `FluentAssertions` | Testing                    |

No other dependencies.

---

## 3. Core Abstractions → C# Design

### 3.1 Property (the universal state unit)

```csharp
// Model — deserialized from JSON
public sealed record PropertyDefinition(
    string Id,
    float Value,
    float Min = 0f,
    float Max = 1f,
    float DecayRate = 0f,
    float? Critical = null,
    List<AggregationExpr>? Aggregations = null,
    List<PassiveEffectRule>? PassiveEffects = null
);

// Runtime — mutable state held by EntityRegistry
public sealed class PropertyState
{
    public float Value { get; set; }
    // ... clamping, snapshot helpers
}
```

Everything that was previously "need", "resource", "world object state", or "relationship attribute" is a Property. The PropertyTicker processes them all identically.

### 3.2 ResponseCurve (cubic Hermite, Unity/Godot-style)

```csharp
public sealed record Keyframe(
    float Time,
    float Value,
    float InTangent = 0f,
    float OutTangent = 0f
);

public sealed record ResponseCurve(List<Keyframe> Keys);
```

`CurveEvaluator.Evaluate(ResponseCurve curve, float x) → float`

Implementation:
1. Find the segment where `keys[i].Time ≤ x < keys[i+1].Time`
2. If both tangents are zero → linear interpolation (fast path)
3. Otherwise → cubic Hermite interpolation using tangent slopes scaled by segment width
4. Clamp output to [0, 1]

Tangent slopes (like Unity's `AnimationCurve` and Godot's `Curve`) replace Bezier handle vectors. A slope of -2.0 means "steep downward", 0.0 means "flat". No Vec2 handles, no degenerate curve risk.

Curve presets are resolved at load time by `CurvePresetLibrary`. Actions store the resolved `ResponseCurve`, not the preset name. This means no runtime string lookups during scoring.

### 3.3 Modifier (the universal scoring influence)

```csharp
public sealed class Modifier
{
    public string Id { get; init; }
    public string Source { get; init; }
    public string Type { get; init; }          // "memory", "directive", "passive", "trait"
    public string Target { get; init; }         // Autonome ID

    public Dictionary<string, float>? ActionBonus { get; init; }
    public Dictionary<string, float>? PropertyMod { get; init; }

    public float? Duration { get; set; }        // game-hours, null = permanent
    public float? DecayRate { get; init; }
    public float Intensity { get; set; }        // current strength

    public Reward? CompletionReward { get; init; }
    public int? MaxClaims { get; init; }
    public int CurrentClaims { get; set; }
    public string Priority { get; init; }       // "low", "normal", "urgent", "critical"

    public string? Flavor { get; init; }
}
```

The scorer's modifier loop:
```
modifierBonus(autonome, action) =
    Σ over modifiers targeting autonome:
        modifier.ActionBonus.GetOrDefault(action.Id, 0) *
        modifier.Intensity *
        PriorityMultiplier(modifier.Priority) *
        LoyaltyMultiplier(autonome, modifier.Source)
```

One loop. Memories, directives, passives, and traits are all just rows in this sum with different lifecycle behaviors.

### 3.4 Relationship (unified edges)

```csharp
public sealed class Relationship
{
    public string Source { get; init; }
    public string Target { get; init; }
    public HashSet<string> Tags { get; init; }
    public Dictionary<string, PropertyState> Properties { get; init; }
}
```

The RelationshipStore indexes by source, by target, and by tag for fast queries. The AuthorityGraph is a view over relationships tagged `"authority"`.

### 3.5 AutonomeProfile (one schema, all scales)

```csharp
public sealed record AutonomeProfile(
    string Id,
    string DisplayName,
    bool Embodied,
    Dictionary<string, PropertyDefinition> Properties,
    Dictionary<string, float> Personality,
    int? EvaluationInterval,              // null = no action evaluation (world objects)
    ActionAccess? ActionAccess,
    SchedulePreferences? SchedulePreferences,
    Identity? Identity,
    List<Modifier>? InitialModifiers,
    Presentation? Presentation             // null for disembodied
);
```

No tier field. No polymorphism needed for different "types" of Autonome — the single class handles everything from a plant (`Embodied=false, EvaluationInterval=null, Properties={water, health, growth}`) to a kingdom (`Embodied=false, EvaluationInterval=300, Properties={influence, treasury, stability}`).

The behavioral differences are entirely driven by the data:
- `Embodied` → determines available step types
- `EvaluationInterval` → determines if/when it evaluates actions (null = passive object)
- `Properties` → determines what it cares about
- Authority graph position → determines who it can direct

---

## 4. Runtime System Details

### 4.1 PropertyTicker

**One system replaces:** NeedTicker + AggregationEngine + PassiveEffectEmitter.

Per tick, for each Autonome:

```
1. DECAY
   For each property with decayRate ≠ 0:
       property.value -= decayRate * delta
       clamp(property.value, property.min, property.max)

2. AGGREGATE (if property has aggregation expressions)
   For each aggregation on the property:
       subordinates = AuthorityGraph.Query(autonome, agg.scope, agg.depth, agg.filter)
       rawValue = AggFunction(subordinates, agg.property, agg.function)
       Blend(property, rawValue, agg.weight, agg.blend)
   clamp(property.value, property.min, property.max)

3. PASSIVE EFFECTS
   For each passiveEffectRule on the property:
       if condition is met (e.g., value < critical):
           ModifierManager.EmitPassive(rule.emit, autonome)
       else:
           ModifierManager.ClearPassive(rule.id, autonome)
```

Aggregation is ordered: decay first, then aggregate, then check thresholds. This prevents stale data in the aggregation query.

**Scheduling:** The PropertyTicker runs for ALL Autonomes (including world objects) at a base rate. Aggregation expressions are only evaluated on the Autonome's `evaluationInterval` ticks, not every tick — a kingdom doesn't re-aggregate every frame.

### 4.2 CurveEvaluator

```csharp
public static class CurveEvaluator
{
    public static float Evaluate(ResponseCurve curve, float x)
    {
        // 1. Clamp x to [0, 1]
        // 2. Linear scan for segment: keys[i].Time ≤ x < keys[i+1].Time
        // 3. Compute t = (x - keys[i].Time) / segmentWidth
        // 4. If both tangents are zero: lerp(keys[i].Value, keys[i+1].Value, t)
        // 5. Otherwise: cubic Hermite interpolation:
        //      m0 = keys[i].OutTangent * segmentWidth
        //      m1 = keys[i+1].InTangent * segmentWidth
        //      y = h00*v0 + h10*m0 + h01*v1 + h11*m1
        // 6. Clamp result to [0, 1]
    }
}
```

For curves with only 2 keyframes and zero tangents (the common case — flat or constant), this reduces to a single lerp. The Hermite path only activates when tangent slopes are nonzero.

**Performance note:** Curve evaluation is the innermost hot loop (called per-property per-action per-Autonome per-tick). N is typically 2-4 keyframes, so a linear scan with early exit is faster than binary search.

### 4.3 UtilityScorer

```csharp
public static class UtilityScorer
{
    public static float Score(
        AutonomeProfile profile,
        EntityState state,
        ActionDefinition action,
        WorldState world)
    {
        float score = 0f;

        // Property responses
        foreach (var (propId, response) in action.PropertyResponses)
        {
            float currentValue = state.Properties[propId].Value;
            float curveOutput = CurveEvaluator.Evaluate(response.Curve, currentValue);
            float magnitude = response.Magnitude;

            // Personality affinity (per-action, not per-property)
            // Applied once as a multiplier to the whole property term
            float personalityMult = 1f;
            foreach (var (axis, affinity) in action.PersonalityAffinity)
            {
                float traitValue = profile.Personality.GetValueOrDefault(axis, 0.5f);
                personalityMult *= Lerp(1f / affinity, affinity, traitValue);
            }

            score += curveOutput * magnitude * personalityMult;
        }

        // Modifier bonus (unified: memories + directives + passives)
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
        score += DeterministicNoise(profile.Id, world.Clock.Tick) * impulsiveness * 0.1f;

        return score;
    }
}
```

`ScoreAllCandidates` pre-filters by requirements (embodied check, nearby tags, property minimums, time-of-day, blocked states, schedule preferences) then scores the remaining candidates and returns them sorted descending.

**The scorer is a pure function.** It reads world state but never mutates it.

### 4.4 ActionExecutor

```csharp
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
                "moveTo"           => HandleMoveTo(autonomeId, step, world),
                "animate"          => HandleAnimate(autonomeId, step, world),
                "wait"             => HandleWait(autonomeId, step, world),
                "modifyProperty"   => HandleModifyProperty(step, world),
                "emitDirective"    => HandleEmitDirective(autonomeId, step, world),
                "emitEvent"        => HandleEmitEvent(autonomeId, step, world),
                "socialInteraction"=> HandleSocial(autonomeId, step, world),
                "createEntity"     => HandleCreateEntity(step, world),
                "destroyEntity"    => HandleDestroyEntity(step, world),
                _ => StepResult.UnknownType(step.Type)
            };

            result.StepResults.Add(stepResult);
            if (stepResult.Failed) { result.Aborted = true; break; }
        }

        // Post-execution: notify ModifierManager of completed action
        world.Modifiers.OnActionCompleted(autonomeId, action.Id, world);

        return result;
    }
}
```

`HandleModifyProperty` is the unified step that replaces modifyNeed, spendGold, modifyObjectState, and modifyResource:

```csharp
static StepResult HandleModifyProperty(ActionStep step, WorldState world)
{
    // step.Entity: "self", "nearest:plant", "nearest:tagged:tavern", "target:npc_tom"
    string entityId = ResolveEntityReference(step.Entity, world);
    if (entityId == null) return StepResult.TargetNotFound(step.Entity);

    string propId = step.Property;
    float amount = step.Amount;

    var prop = world.Entities.GetProperty(entityId, propId);
    if (prop == null) return StepResult.PropertyNotFound(entityId, propId);

    float oldValue = prop.Value;
    prop.Value = Math.Clamp(prop.Value + amount, prop.Min, prop.Max);

    return StepResult.Success(entityId, propId, oldValue, prop.Value);
}
```

### 4.5 ModifierManager

Replaces DirectiveRouter + MemoryManager:

```csharp
public class ModifierManager
{
    // Primary index: target Autonome ID → active modifiers
    private Dictionary<string, List<Modifier>> _byTarget;

    // Secondary index: source → modifiers (for cleanup when source is destroyed)
    private Dictionary<string, List<Modifier>> _bySource;

    public void Add(Modifier mod) { ... }
    public void Remove(string modifierId) { ... }
    public List<Modifier> GetModifiers(string targetId) => _byTarget.GetOrEmpty(targetId);

    // Called every tick
    public void Tick(float delta)
    {
        foreach (var mod in AllModifiers())
        {
            // Duration countdown
            if (mod.Duration.HasValue)
            {
                mod.Duration -= delta;
                if (mod.Duration <= 0) { MarkForRemoval(mod); continue; }
            }

            // Intensity decay (for memory-type modifiers)
            if (mod.DecayRate.HasValue)
            {
                mod.Intensity -= mod.DecayRate.Value * delta;
                if (mod.Intensity <= 0) { MarkForRemoval(mod); }
            }
        }
        PurgeMarked();
    }

    // Called when an Autonome completes an action
    public void OnActionCompleted(string autonomeId, string actionId, WorldState world)
    {
        foreach (var mod in GetModifiers(autonomeId))
        {
            if (mod.ActionBonus == null) continue;
            if (!mod.ActionBonus.ContainsKey(actionId)) continue;
            if (mod.CompletionReward == null) continue;

            // Deliver reward
            DeliverReward(autonomeId, mod, world);

            // Increment claims
            mod.CurrentClaims++;
            if (mod.MaxClaims.HasValue && mod.CurrentClaims >= mod.MaxClaims)
                MarkForRemoval(mod);

            // Feed back to source's properties (need relief)
            FeedbackToSource(mod, world);
        }
    }

    // Passive modifier management (called by PropertyTicker)
    public void EmitPassive(PassiveEffectRule rule, string sourceAutonomeId, WorldState world) { ... }
    public void ClearPassive(string ruleId, string sourceAutonomeId) { ... }
}
```

### 4.6 AuthorityGraph

```csharp
public class AuthorityGraph
{
    // Built from relationships tagged "authority"
    private Dictionary<string, List<string>> _subordinates;  // parent → children
    private Dictionary<string, List<string>> _superiors;     // child → parents

    public void Build(RelationshipStore relationships) { ... }
    public void ValidateAcyclic() { ... }  // Throws if cycle detected (Kahn's algorithm)

    public List<string> GetSubordinates(string id, int? maxDepth, Func<string, bool>? filter)
    {
        // BFS/DFS from id following _subordinates edges
        // Respect maxDepth (null = unlimited)
        // Apply filter to each candidate
    }

    public List<string> GetSuperiors(string id) => _superiors.GetOrEmpty(id);

    public List<string> GetPeers(string id)
    {
        // All Autonomes sharing at least one authority parent with id
        return GetSuperiors(id)
            .SelectMany(parent => GetSubordinates(parent, maxDepth: 1))
            .Where(peer => peer != id)
            .Distinct()
            .ToList();
    }
}
```

---

## 5. Simulation Loop

```csharp
public class SimulationRunner
{
    public void Run(WorldState world, SimulationConfig config)
    {
        while (world.Clock.Tick < config.TotalTicks)
        {
            world.Clock.Advance(config.TickDelta);
            float delta = config.TickDelta;

            // 1. PROPERTY TICK (all entities — decay, aggregation, passives)
            PropertyTicker.TickAll(world, delta);

            // 2. MODIFIER LIFECYCLE
            world.Modifiers.Tick(delta);

            // 3. EVALUATE + ACT (only Autonomes due for evaluation this tick)
            foreach (var autonome in EvaluationScheduler.GetDue(world))
            {
                if (world.Entities.IsBusy(autonome.Id)) continue;

                var candidates = UtilityScorer.ScoreAllCandidates(autonome, world);
                if (candidates.Count == 0) continue;

                var chosen = candidates[0];
                var result = ActionExecutor.Execute(autonome.Id, chosen.Action, world);

                // Record memory-type modifier for completed action
                RecordActionMemory(autonome, chosen, world);

                // Record to history
                HistoryRecorder.RecordAction(autonome, chosen, candidates, world);
            }

            // 4. SNAPSHOT (periodic)
            if (world.Clock.Tick % config.SnapshotInterval == 0)
                HistoryRecorder.Snapshot(world);
        }
    }
}
```

**No tier-specific loops.** The `EvaluationScheduler` returns all Autonomes whose `evaluationInterval` aligns with the current tick. Embodied Autonomes with interval=1 evaluate every tick. Towns with interval=30 evaluate every 30th tick. World objects with interval=null never appear.

---

## 6. History Output Format

Unchanged from previous plan in structure. Key updates to reflect unified abstractions:

### Event Types

```jsonc
// Action chosen (same for all Autonomes, embodied or not)
{
  "tick": 142,
  "gameTime": "Day 1, 11:30",
  "type": "action_chosen",
  "autonomeId": "npc_marta_blackwood",
  "embodied": true,
  "actionId": "forage_herbs",
  "score": 0.847,
  "topCandidates": [
    { "actionId": "forage_herbs", "score": 0.847 },
    { "actionId": "eat_at_tavern", "score": 0.612 }
  ],
  "propertySnapshot": { "hunger": 0.55, "purpose": 0.31, "mood": 0.62 },
  "activeModifiers": [
    { "id": "mem_forage_positive", "type": "memory", "intensity": 0.75, "contribution": 0.12 }
  ]
}

// Modifier emitted (covers directives, passives, memories)
{
  "tick": 585,
  "type": "modifier_emitted",
  "modifierId": "dir_expand_farmland_001",
  "modifierType": "directive",
  "sourceId": "town_millhaven",
  "targetCount": 8,
  "targetIds": ["npc_farmer_joe", "npc_laborer_ann"],
  "boostedActions": ["plow_field", "clear_land"],
  "priority": "normal",
  "duration": 72
}

// Modifier influenced a decision
{
  "tick": 590,
  "type": "action_chosen",
  "autonomeId": "npc_farmer_joe",
  "actionId": "plow_field",
  "score": 0.923,
  "modifierContributions": [
    { "modifierId": "dir_expand_farmland_001", "contribution": 0.35 }
  ]
}

// Modifier claim completed
{
  "tick": 602,
  "type": "modifier_claim_completed",
  "modifierId": "dir_expand_farmland_001",
  "claimantId": "npc_farmer_joe",
  "claimNumber": 1,
  "maxClaims": 5,
  "rewardDelivered": { "gold": 8, "purpose": 0.2 },
  "sourcePropertyRelief": { "food_supply": 0.04 }
}

// Property change (covers needs, resources, world object state, relationship attrs)
{
  "tick": 800,
  "type": "property_changed",
  "entityId": "obj_herb_planter_01",
  "property": "water",
  "oldValue": 0.3,
  "newValue": 1.0,
  "cause": "action:water_plants",
  "actorId": "npc_marta_blackwood"
}

// Aggregation result
{
  "tick": 960,
  "type": "aggregation_computed",
  "autonomeId": "town_millhaven",
  "property": "morale",
  "previousValue": 0.52,
  "aggregatedInput": 0.48,
  "newValue": 0.50,
  "function": "avg",
  "sourceCount": 20
}
```

---

## 7. Build Order + Milestones

### M1 — Core Model + Data Loading (~2 days)

**Build:**
- All `Model/` classes: Property, AutonomeProfile, ActionDefinition, ActionStep, Modifier, Relationship, ResponseCurve, ControlPoint, AggregationExpr, PassiveEffectRule
- `DataLoader` — walks data directory, deserializes JSON by path convention
- `CurvePresetLibrary` — loads `curves.json`, resolves preset names in action definitions to `ResponseCurve` objects at load time
- `SchemaValidator` — validates ID references, range constraints, required fields
- `VocabularyRegistry` — valid tags, step types, personality axes
- `samples/minimal/` — 3 embodied Autonomes, 1 location, 5 actions, no disembodied, no authority graph

**Test gate:** `dotnet run --validate-only --data ./samples/minimal` passes.

### M2 — Property Ticker + Curve Evaluator (~2 days)

**Build:**
- `CurveEvaluator` — cubic Hermite evaluation with linear fallback
- `PropertyTicker` — decay only (no aggregation yet, no passives yet)
- `EntityRegistry` — stores runtime PropertyState per Autonome
- `WorldState` — ties together the registries
- `SimulationClock`

**Test gate:** Load 3 Autonomes, tick 100 times, verify each property decayed by expected amount. Verify negative decay rates increase value. Verify clamping at min/max.

**Curve tests:** Verify 2-keyframe linear, 2-keyframe flat, 4-keyframe Hermite all produce expected outputs for a spread of inputs. Verify evaluation at x=0, x=1, and segment boundaries.

### M3 — Utility Scorer + Evaluation Loop (~3 days)

**Build:**
- `UtilityScorer.Score` and `ScoreAllCandidates`
- Context filtering (requirements checking)
- `EvaluationScheduler` — per-Autonome intervals
- `ActionExecutor` — embodied step handlers only (moveTo, animate, wait, modifyProperty, emitEvent)
- `SimulationRunner` — main tick loop without modifiers or aggregation
- `HistoryRecorder` — action_chosen events + initial/final state
- `LocationGraph` — basic spatial tracking

**Test gate:** Run 10-day sim with 3 embodied Autonomes. Output JSON shows:
- Different Autonomes pick different actions (personality + property state divergence)
- Properties oscillate as actions satisfy them and decay drains them
- Routines emerge (visible in action sequence per Autonome)

### M4 — Unified Modifier System (~3 days)

**Build:**
- `ModifierManager` — add, remove, tick lifecycle, lookup by target, claim tracking
- Memory creation: after an Autonome completes an action, create a memory-type Modifier biasing future evaluations
- Initial modifier loading from profiles (`initialModifiers`)
- Wire `modifierBonus` into UtilityScorer
- Intensity decay and duration expiry

**Test gate:** Two Autonomes in identical property states choose differently because:
- One has a seeded memory modifier favoring a specific action
- Completing an action creates a new memory that biases the next evaluation
- Memories decay over time and eventually stop influencing

### M5 — Authority Graph + Directives (~3 days)

**Build:**
- `AuthorityGraph` — build from "authority"-tagged relationships, validate acyclic
- `RelationshipStore` — indexed by source, target, and tags
- `GraphTraversal` — subordinates (with depth), peers
- `emitDirective` step handler — resolves targets via graph, creates directive-type Modifier
- Directive completion rewards + claim counting + source feedback
- Disembodied action evaluation (no physical steps, only emitDirective/modifyProperty/emitEvent)
- `samples/village/` — 20 embodied, 1 town, 1 guild, authority relationships

**Test gate:** Town's food_supply drops → Town evaluates and picks "expand_farmland" → Directive modifier created targeting resident farmers → Farmers' plow_field score increases → Farmer completes plowing → Reward delivered, town's food_supply gets relief. Full cycle visible in event stream.

### M6 — Aggregation + Passive Effects (~2 days)

**Build:**
- Aggregation expression evaluation in PropertyTicker (avg, sum, count, min, max, ratio)
- Blend modes (replace, lerp, additive, min, max)
- Passive effect emission/clearing based on threshold conditions
- Compound aggregations (multiple sources per property)

**Test gate:** Town's morale property tracks average resident mood via aggregation. When morale drops below critical, passive Modifier emitted to all residents applying mood penalty. Penalty clears when morale recovers. Verify feedback loop stabilizes (damped oscillation).

### M7 — World Objects + Full Integration (~2 days)

**Build:**
- World objects as non-evaluating Autonomes (evaluationInterval: null)
- Ownership via relationship tags
- `objectPropertyBelow` context filter in action requirements
- `samples/valley/` — 50+ embodied, 3 towns, 2 guilds (one with a sub-chapter), world objects

**Test gate:** Plant water decays → becomes eligible for "water_plants" action → owner NPC picks it up → water restored. If owner is busy, another NPC with sufficient empathy waters it. Sub-guild receives directives from parent guild correctly. Full three-level cascade visible in event stream.

### M8 — Summary Statistics + CLI Polish (~2 days)

**Build:**
- `SnapshotBuilder` — periodic full-state dumps
- Summary computation: action diversity, property balance, uniqueness overlap, directive responsiveness, graph utilization, oscillation checks, cascade detection, deadlock detection
- Finalize CLI with `System.CommandLine`
- Console progress output
- `README.md`

**Test gate:** Clean run from `git clone` to output JSON with only .NET SDK. Summary statistics all within expected ranges for `samples/valley/`.

---

## 8. What Unified vs. What Remains Distinct

### Successfully Unified

| Before (v2)                     | After (v3)                          |
|---------------------------------|-------------------------------------|
| Need + Resource + ObjectState   | **Property** (one type)             |
| NeedTicker + AggregationEngine  | **PropertyTicker** (one system)     |
| Memory + Directive + Passive    | **Modifier** (one type)             |
| MemoryManager + DirectiveRouter | **ModifierManager** (one system)    |
| Relationship + Affiliation      | **Relationship** (one type)         |
| Tier enum (0, 1, 2)            | **Authority DAG** (data-driven)     |
| Sigmoid/Exponential/Linear/Flat | **Piecewise Hermite** (one evaluator)|
| modifyNeed/spendGold/modifyObj  | **modifyProperty** (one step type)  |
| Hard-coded aggregation formulas | **AggregationExpr** (data-driven)   |

### Intentionally Distinct

| Thing | Why it stays separate |
|-------|----------------------|
| `embodied` flag | Fundamental behavioral split: can or cannot physically act. One boolean, checked in two places. |
| Step type dispatch | `moveTo` and `emitDirective` have genuinely different semantics. The step type vocabulary is engine code; which steps an action uses is data. |
| Hermite curve evaluation vs. Aggregation primitives | Curves map a single input to a single output. Aggregation queries many entities and reduces them. Different computational shapes. |
| AuthorityGraph vs. RelationshipStore | The authority graph is a derived, cached view over relationships. It exists for performance (BFS/DFS traversal) and validation (acyclicity). The RelationshipStore is the source of truth. |

---

## 9. Critical Technical Risks

### Aggregation Query Performance

A kingdom with `depth: null` and 500 subordinates means a graph traversal of 500 nodes per aggregation per tick. If the kingdom has 5 aggregated properties evaluated every 60 seconds, this is 2500 traversals per minute. Each traversal is O(V) where V = subordinate count.

**Mitigation:** Cache subordinate lists on the AuthorityGraph. Invalidate only when relationships change (rare — relationship mutations are events). Most ticks hit the cache.

### Modifier Lookup During Scoring

The hot path: for each Autonome evaluation, for each candidate action, iterate all modifiers on that Autonome. If an Autonome has 15 active modifiers (3 memories + 2 directives + 5 passives + 5 trait effects) and 20 candidate actions, that's 300 modifier lookups per evaluation.

**Mitigation:** Pre-index modifiers by action ID as well as by target. Use `Dictionary<string, Dictionary<string, float>>` — target → action → total bonus. Rebuild this index when modifiers change (add/remove), not on every scorer call.

### Passive Effect Oscillation

Property below critical → emit passive modifier → modifier hurts mood → aggregation drops morale further → emit stronger passive → spiral.

**Mitigation:** Passive modifiers are **set, not accumulated**. Each PassiveEffectRule emits exactly one modifier that gets replaced each tick, not stacked. The modifier's strength is fixed by the rule definition, not proportional to how far below the threshold the property is (unless explicitly configured with a formula).

### Curve Authoring

Tangent slopes (Unity/Godot-style Hermite) are more intuitive than Bezier handles but extreme slopes can still produce overshooting curves.

**Mitigation:** Presets cover 90% of cases. The LLM references preset names, not raw keyframes. For the 10% that need custom curves, the SchemaValidator checks: Time values are monotonically increasing, Value stays in [0, 1]. The evaluator clamps output to [0, 1] regardless.

---

## 10. Out of Scope

- **Godot integration** — standalone console app
- **Visualization** — output JSON is the visualization contract
- **AI content generation pipeline** — assumes pre-authored sample data
- **Pathfinding** — `moveTo` is instant in headless sim
- **Save/load mid-simulation** — snapshots are for analysis, not resumption
- **Networking** — single-process, single-threaded
