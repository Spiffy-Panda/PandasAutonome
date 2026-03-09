# Phase 1: Core Behavior Fixes (Foundation)

Status: **completed**

**Unblocked**: Phase 2 (Food Pipeline), Phase 4 (Social Overhaul)

---

> Everything downstream depends on NPCs making varied, sensible choices. Fix the decision engine first.

## Population Impact

No new NPCs in this phase. Behavioral changes apply to all existing ~95 embodied NPCs.

---

## 1.1 — Weighted Random Top-K Action Selection

**Problem**: `SimulationRunner.cs:136` always picks `candidates[0]` — the highest-scored action wins every time. With typical decision margins of 0.08-0.11, the same action wins hundreds of ticks in a row.

**Solution**: Replace deterministic top-1 with weighted random selection among the top K candidates, scaled by the entity's impulsiveness trait.

**File**: `SimulationRunner.cs` (lines 130-140) + new helper

**Implementation**:
```
K = 3 + floor(impulsiveness * 4)    // Range: 3 (impulsiveness=0) to 7 (impulsiveness=1)
                                     // Low impulsiveness = routine; high = unpredictable

candidates = top K scored actions (already sorted)

// Softmax-style weighting with temperature
temperature = 0.15 + impulsiveness * 0.35   // Range: 0.15 (sharp) to 0.50 (flat)
weights[i] = exp(candidates[i].Score / temperature)
normalize weights to sum to 1.0

chosen = weighted random sample from candidates using weights
```

**Why softmax**: Raw score differences are small (0.01-0.1). Softmax with tunable temperature controls how "peaked" the distribution is. A farmer with low impulsiveness (temp=0.20) still mostly picks the top action but occasionally varies. A thief with high impulsiveness (temp=0.45) genuinely randomizes among competitive options.

**Vital zero-lock override**: When vital lock is active (hunger=0), bypass randomization and pick the #1 action deterministically. Survival is non-negotiable.

**Analysis impact**: `ActionEvent` should record chosen rank (was it #1, #2, #3?) so analysis can track how much randomization actually fires.

---

## 1.2 — Stronger Night Shift

**Problem**: Current night multiplier is 0.7x for work/trade/leisure (20:00-05:00). Too weak to create distinct daily rhythms.

**Solution**: Strengthen penalties and add positive night bonuses.

**File**: `UtilityScorer.cs` — `GetNightMultiplier` (line 272)

**Changes**:
```
Night (20:00-05:00):
  work    → 0.4x  (was 0.7x)
  trade   → 0.4x  (was 0.7x)
  leisure → 0.7x  (unchanged — tavern socializing is evening activity)

Evening (18:00-20:00) — NEW window:
  social  → 1.3x  (tavern/chat bonus)
  leisure → 1.2x

Night-only bonuses (applied as score additive, not multiplier):
  rest_at_home → +0.3 bonus when gameHour >= 21 or < 5
```

**Implementation note**: The rest bonus should be an additive constant in `ScoreAllCandidates`, not in the multiplier method. Add a `GetTimeBonus(actionId, gameHour)` helper that returns flat bonuses for specific actions at specific hours.

**Expected behavior**: Distinct daily rhythm emerges:
- Morning (5-12): Work peaks
- Afternoon (12-18): Work + trade
- Evening (18-21): Tavern + social
- Night (21-5): Sleep, guards patrol

---

## 1.3 — Nerf eat_scraps

**Problem**: eat_scraps is free, infinite, always available, and restores 0.25 hunger. It's the path of least resistance — NPCs never need the food supply chain because scrounging is costless.

**Solution**: Make eat_scraps the desperate fallback, not the default.

**File**: `worlds/coastal_city/actions/eat_scraps.json`

**Changes**:
```json
{
  "hunger restoration":  "0.25 → 0.15",
  "mood penalty":        "-0.05 → -0.12",
  "add requirement":     { "locationTags": ["outdoor", "slums", "docks"] },
  "add social penalty":  "-0.03 (eating scraps is shameful)"
}
```

**Tuning rationale**:
- At 0.15 hunger restoration, NPCs need to eat scraps ~7 times/day vs ~4 times for market food (0.35). The time cost alone makes market food worth pursuing.
- The -0.12 mood penalty means scraps-dependent NPCs trend toward low mood, which makes mood-boosting actions (tavern, social) more attractive — creating variety.
- Location restriction means NPCs in civic/residential/manor areas can't scrounge — they must buy food or travel to slums. This drives economic demand geographically.

**Safety valve**: eat_scraps remains available under vital zero-lock regardless of location (the AddressesVitalProperty check in UtilityScorer already handles this). No NPC will actually starve.

---

## 1.4 — Clean Sweep of Dead Features

**Remove PropertyMod from modifier model**:
- `Modifier.cs`: Remove `PropertyMod` field entirely (or comment-mark as deprecated)
- `ActionExecutor.cs` (lines 124, 287, 334, 413): Remove PropertyMod copying in memory/directive creation
- All action JSONs referencing `propertyMod`: Remove the fields
- **Rationale**: Stored but never read by PropertyTicker or UtilityScorer. Dead weight that causes confusion.

**Remove `propertyAbove` from bribe.json**:
- `worlds/coastal_city/actions/bribe.json` line 7: Delete `"propertyAbove": { "gold": 80 }`
- Replace with functional requirement: `"propertyMin": { "gold": 80 }`
- **Rationale**: `propertyAbove` is silently ignored. `propertyMin` actually works.

**Remove `AggregationFunction.Ratio` stub**:
- `PropertyTicker.cs:151`: Remove the Ratio case entirely (or throw NotImplementedException)
- Grep confirms zero uses in world data
- **Rationale**: Returning 0f silently is worse than failing loudly. If needed later, implement it then.

**Repurpose dead locations** (add actions targeting them):

| Location | New Purpose | Action |
|---|---|---|
| `sea.lighthouse` (The Beacon) | Lookout post for harbor authority | `watch_horizon` — defense action, grants mood +0.05, detects incoming ships (emits event to harbor authority) |
| `hinterland.woodlands.deep_woods` | Danger zone / rare herb gathering | `forage_rare_herbs` — high risk (mood -0.10, adventurousness 1.5), high reward (trade_goods +2, gold +8) |
| `hinterland.woodlands.whispering_grove` | Meditation / social reflection | `meditate` — restores mood +0.20 and social +0.05, requires empathy > 0.5. Peaceful alternative to tavern. |

**Keep transit-only nodes**: `north_forest_trail`, `south_forest_trail`, `river_road` serve the hop-by-hop travel system. No change needed.

---

## Files Affected

**Engine (C#)**:
- `SimulationRunner.cs` — weighted random selection (1.1)
- `UtilityScorer.cs` — night shift + time bonuses (1.2)
- `PropertyTicker.cs` — remove Ratio stub (1.4)
- `Modifier.cs` — remove PropertyMod (1.4)
- `ActionExecutor.cs` — remove PropertyMod copying (1.4)

**World Data (JSON)**:
- `actions/eat_scraps.json` — nerf (1.3)
- `actions/bribe.json` — fix requirement (1.4)
- `actions/watch_horizon.json` — NEW (1.4)
- `actions/forage_rare_herbs.json` — NEW (1.4)
- `actions/meditate.json` — NEW (1.4)
- `autonomes/*.json` — remove PropertyMod from initialModifiers where present
