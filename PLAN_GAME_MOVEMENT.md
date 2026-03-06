# Plan: Hop-by-Hop Movement & Possession Status

## Context

Currently, when an NPC performs an action with a `moveTo` step (e.g., `fish_harbor`), they **teleport** instantly to the destination and are marked "busy" for the travel cost duration. The entity disappears from its origin and appears at the destination in a single tick. This prevents the game engine (Godot) from animating NPCs walking along paths between locations.

The `LocationGraph` already has `GetNextHop(from, to)` returning the next intermediate location on the shortest path, but it's never used — `HandleMoveTo` skips straight to `SetLocation(id, finalDest)`.

Additionally, when possessing an NPC, the API only shows `busyUntilTick` with no detail about **what** the entity is doing (traveling, working, idle).

## Changes

### 1. Add `TravelState` and activity tracking to `EntityState`
**File:** `src/Autonome.Core/World/EntityRegistry.cs`

- Add `TravelState` class with: `Destination` (string), `Action` (ActionDefinition), `PostMoveStepIndex` (int)
- Add to `EntityState`: `Travel` (TravelState?), `LastActionId` (string?)
- Add `EntityActivity` record: `Status` ("idle"/"traveling"/"busy"), `ActionId`, `Destination`
- Add computed `CurrentActivity` property on `EntityState`

### 2. Add `GetEdgeCost` to `LocationGraph`
**File:** `src/Autonome.Core/World/LocationGraph.cs`

- New method: `int? GetEdgeCost(string from, string to)` — looks up cost of a single direct edge from `LocationDefinition.ConnectedTo`
- Needed because `GetNextHop` returns total path cost, but we need per-hop edge cost for `BusyUntilTick`

### 3. Rewrite `HandleMoveTo` for hop-by-hop + add `StepResult.Deferred`
**File:** `src/Autonome.Core/Runtime/ActionExecutor.cs`

**StepResult changes:**
- Add `IsDeferred` bool property
- Add `StepResult.Deferred(stepType)` factory

**ExecutionResult changes:**
- Add `Deferred` bool property

**HandleMoveTo signature change:** Add `ActionDefinition action, int stepIndex` params

**New HandleMoveTo logic:**
1. Resolve final destination (same as now)
2. If already there: return `Success` (same as now)
3. Get `NextHop` from routing table
4. Move entity to next hop only: `SetLocation(id, nextHop)`
5. Set `BusyUntilTick` to current tick + edge cost for this single hop
6. If `nextHop == destination`: return `Success` (single hop, remaining steps execute normally)
7. If multi-hop: set `entity.Travel = new TravelState(destination, action, stepIndex + 1)`, return `Deferred`

**Execute loop changes:**
- Switch from `foreach` to indexed `for` loop (need step index for TravelState)
- Pass `action` and `i` to `HandleMoveTo`
- On `IsDeferred`: set `result.Deferred = true`, break out of loop
- Track `entity.LastActionId = action.Id` at start

**Extract post-execution helper:** The memory generation + `OnActionCompleted` code (lines 37-78) into a private `CompleteAction` method, shared between `Execute` and new `ContinueAction`

**New `ContinueAction` static method:**
- Takes `(string autonomeId, TravelState travel, WorldState world)`
- Runs `action.Steps[postMoveStepIndex..]` with same dispatch logic
- Calls `CompleteAction` on success
- Returns `ExecutionResult`

### 4. Add travel continuation phase to tick loop
**File:** `src/Autonome.Core/Simulation/SimulationRunner.cs`

Insert **phase 3.5** between "3. CLEAR BUSY FLAGS" and "4. EVALUATE + ACT":

```
0. EXTERNAL EVENTS
1. PROPERTY TICK
2. MODIFIER LIFECYCLE
3. CLEAR BUSY FLAGS
3.5. CONTINUE TRAVEL  ← NEW
4. EVALUATE + ACT
```

**Phase 3.5 logic** — iterate all entities:
- Skip if `entity.Travel == null` (not traveling)
- Skip if `entity.BusyUntilTick > 0` (still mid-hop, hasn't finished yet)
- If `currentLoc == travel.Destination`: arrived! Clear travel, call `ContinueAction` for remaining steps, emit action event
- Else: advance to next hop via `GetNextHop`, `SetLocation`, set `BusyUntilTick` for edge cost. Emit a `travel_hop` event for Godot visualization. If route is unreachable (`GetNextHop` returns null), abort travel.

Move `tickResult` creation above phase 3.5 (currently at line 39, after phase 3 — just needs to include travel events).

### 5. Skip traveling entities in evaluation
**File:** `src/Autonome.Core/Simulation/EvaluationScheduler.cs`

Add after the `BusyUntilTick` check (line 27):
```csharp
if (state.Travel != null) continue;
```

### 6. Update NPC spawn to use homeLocation
**File:** `src/Autonome.Data/WorldBuilder.cs`

Change spawn priority (lines 66-121):
1. **First:** If NPC has `homeLocation` in profile, spawn there
2. **Fallback:** Existing org-linked location logic
3. **Last resort:** First location in graph

This makes NPCs start at home and commute to work, matching the visual expectation.

### 7. Enhance API responses with activity status
**File:** `src/Autonome.Web/Program.cs`

**`GET /api/entity/{id}/state`** (line 154) — add:
```json
{
  "activity": { "status": "traveling|busy|idle", "actionId": "...", "destination": "..." },
  "travel": { "destination": "...", "actionId": "...", "remainingSteps": 3 }
}
```

**`GET /api/world/entities`** (line 133) — add `activity` status per entity

**`POST /api/entity/{id}/act`** (line 233) — enhance response note:
- If traveling: "Entity is traveling to X — action will execute after arrival"
- If busy: "Entity busy until tick X"
- If idle: "Action will execute on next tick"

**`POST /api/entity/possess`** (line 277) — add activity status to response

**New endpoint: `POST /api/entity/{id}/cancel-travel`** — lets possessed entity abort mid-travel (clears `Travel` and `BusyUntilTick`)

### 8. Update MCP bridge with activity info
**File:** `autonome-mcp/index.js`

- Update `entity_state` description to mention activity status
- Add `entity_cancel_travel` tool for possessed entities
- Update `entity_act` description to mention travel/busy status visibility

### 9. Add ActionEvent type field
**File:** `src/Autonome.Core/Simulation/SimulationRunner.cs` (ActionEvent record)

Add optional `EventType` field to `ActionEvent` record: `"action_start"`, `"travel_hop"`, `"action_complete"`. This lets Godot and the web UI distinguish between regular actions, mid-travel hops, and deferred action completions without parsing sentinel scores.

## Implementation Order

1. `EntityRegistry.cs` — TravelState, EntityActivity, new properties (foundation, no breaking changes)
2. `LocationGraph.cs` — GetEdgeCost (small addition)
3. `ActionExecutor.cs` — StepResult.Deferred, rewrite HandleMoveTo, Execute loop, ContinueAction, CompleteAction helper (core change)
4. `EvaluationScheduler.cs` — skip traveling entities (one line)
5. `SimulationRunner.cs` — travel continuation phase 3.5, ActionEvent type field
6. `WorldBuilder.cs` — spawn at homeLocation
7. `Program.cs` — API response enhancements, cancel-travel endpoint
8. `index.js` — MCP bridge updates

Steps 1-5 are the core engine (test together). Step 6 is independent. Steps 7-8 are API layer.

## Verification

1. `dotnet build` — confirm compilation
2. `dotnet run --project src/Autonome.Cli -- --data worlds/coastal_city --ticks 100` — confirm no crashes, entities move
3. Grep output events for `travel_hop` — verify entities visit intermediate locations
4. Run 2000-tick analysis — compare action counts and balance against previous baseline (total busy time per entity should be unchanged)
5. Start web server, possess an entity, check `entity_state` shows activity status
6. Submit action while entity is traveling — verify response shows travel status
7. Test `cancel-travel` endpoint

## Edge Cases Handled

- **Route becomes unreachable mid-travel**: `GetNextHop` returns null → abort travel, entity stays at current intermediate location, becomes idle
- **Possessed mid-travel**: Travel continues to completion, then entity waits for external commands
- **Cancel travel**: New endpoint clears Travel + BusyUntilTick, entity becomes idle at current hop
- **Adjacent destination (1 hop)**: No TravelState created, action completes normally in one tick
- **Already at destination**: No movement, no busy time (unchanged)
- **Non-embodied entities**: HandleMoveTo returns Failure for non-embodied (unchanged, they never move)
- **Actions without moveTo** (e.g., wander): No travel state, execute as before

## Balance Impact

Total busy time per entity is unchanged (sum of hop costs = old total travel cost). The difference is **spatial**: entities now physically occupy intermediate locations during transit, enabling social interactions at waypoints and correct location-based effects. No action magnitude rebalancing should be needed.
