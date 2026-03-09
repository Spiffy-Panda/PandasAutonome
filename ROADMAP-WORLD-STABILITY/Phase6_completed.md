# Phase 6: Equilibrium Tuning

Status: **completed** (2026-03-09)
Blocked by: ~~Phase 1~~, ~~Phase 2~~, ~~Phase 3~~, ~~Phase 4~~, ~~Phase 5~~

**Unblocks**: Nothing — this is the final phase.

---

> With all systems online, tune the world toward a fragile equilibrium that cascades when disrupted.

## Population Impact

May adjust NPC counts up or down by 5-10 to hit production/consumption targets. The population should be ~150-190 by the time this phase begins (after Phase 4's family expansion).

---

## 6.1 — Production/Consumption Balance

**Target**: Total food production = 90-95% of total consumption.
- Run 5000-tick simulations with analysis
- Measure: total food produced (harvest + fishing + baking + ship arrivals) vs total food consumed (eat actions + decay + children dependents)
- Adjust: farmer count, harvest output, ship frequency, tavern capacity, decay rates
- The system should sustain itself but have no surplus buffer
- **Children (from Phase 4) increase consumption** without increasing production — this is intentional tension

---

## 6.2 — Decay as the Tension Lever

- Food decay is the clock — the system must constantly produce just to stay level
- Tweak decay rates so that 2 missed ship arrivals = tavern food crisis
- 1 dead farmer = noticeable market supply drop
- This creates natural drama without scripted events

---

## 6.3 — Org Morale Feedback Loops

**Chain**: Low food -> low NPC mood -> low org morale (aggregation) -> fewer directives -> less organized work -> even lower food -> spiral

**Sharpen the loop**:
- Org morale aggregation blend weight: 0.3 -> 0.5 (faster response to subordinate mood)
- Add passive effect on orgs: when morale < 0.3, emit mood penalty to subordinates (-0.05)
- Noble morale decay remains 0.0003 (validated — the overthrow sweet spot)

---

## 6.4 — Validate Weighted Random Top-K

- Run 2000-tick analysis comparing old (deterministic top-1) vs new (weighted random)
- Metrics to compare:
  - Unique actions per NPC (target: 8+ out of available, was 5-10)
  - Max consecutive same-action runs (target: <6, was 11+)
  - Dominant action percentage (target: <25%, was 31-41%)
  - Action category distribution evenness
- Tune K range and temperature if needed

---

## 6.5 — Gold Equilibrium Check

- After rent/taxes, verify NPCs don't go permanently broke
- Target: median NPC gold stays between 15-50 over 2000 ticks
- Poor NPCs (slums) should hover 5-15g; wealthy (manor) should hover 30-80g
- If NPCs trend toward 0, reduce rent rates or increase gold from work
- **Spouse trade assistants** (from Phase 4) contribute household income — factor this into balance

---

## 6.6 — Population Capacity Validation

- With ~150-190 NPCs (doubled from Phase 4), verify:
  - Simulation tick performance stays under 50ms/tick
  - Location crowding doesn't break nearbyRandom (too many candidates)
  - Food production scales with new worker spouses
  - Tavern capacity handles larger clientele
  - Social memory modifiers don't explode (N^2 potential with nearbyRandom)
- If performance degrades, consider: evaluation interval tuning, location splitting, modifier pooling

---

## Files Affected

**World Data (JSON)**:
- Various `autonomes/*.json` — count adjustments
- Various `locations/valley.json` — capacity/decay tuning
- `events.json` — ship frequency tuning
- `autonomes/org_*.json` — aggregation blend weights, passive thresholds

**Engine (C#)**:
- `SimulationRunner.cs` — K/temperature tuning if needed (6.4)
- `PropertyTicker.cs` — aggregation blend weights (6.3)
- Performance profiling (6.6)
