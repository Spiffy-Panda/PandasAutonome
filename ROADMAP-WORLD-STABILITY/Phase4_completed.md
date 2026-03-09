# Phase 4: Full Social Overhaul

Status: **completed**
Unblocked by: Phase 1 (Core Behavior Fixes)

**Unblocks**: Phase 5 (Social Graph Viz), Phase 6 (Equilibrium Tuning)

---

> Make relationships, gossip, and family connections mechanically meaningful.

## Population Impact — THIS IS THE BIG EXPANSION

**The population is expected to nearly double in this phase.**

### Family Pairs & Spouse Trade Assistants

Designate ~30-40 NPC pairs as families. **Spouses of tradesmen should be assistants to those trades.** This means:

- A farmer's spouse is a **farmhand** — allowed actions include `plow_field`, `harvest`, `sell_at_market`, plus domestic actions
- A smith's spouse is a **smith's assistant** — allowed actions include `work_metal`, `forge_metal`, plus buying ore/selling tools
- A fisher's spouse is a **net mender / fish seller** — allowed actions include `fish_harbor`, selling fish at market
- A guard's spouse is a **shopkeeper or homemaker** — different trade, complementary income
- A miner's spouse is a **ore sorter / smelter assistant** — allowed actions include `mine_ore` variants, hauling
- A tavern keeper's spouse is a **cook or server** — allowed actions include `serve_drinks`, `bake_bread`, tavern supply runs

This creates **household economic units** where both partners contribute to the family trade, making the economy more resilient and realistic. A farming household produces more than a solo farmer, but both members need to eat — so the production/consumption ratio stays tight.

### New NPC Estimates

| Source | New NPCs | Notes |
|---|---|---|
| Spouses of existing tradesmen | ~30-40 | Every major NPC gets a partner with trade-assistant role |
| Children (non-acting dependents) | ~15-20 | Increase food consumption at home, no actions of their own |
| New singles (social mobility) | ~5-10 | Young NPCs, newcomers, wanderers — not paired |
| **Total new** | **~50-70** | |
| **Total population** | **~150-165** | Up from ~95 |

### Spouse Design Pattern

Each spouse NPC profile should follow this pattern:
```json
{
  "id": "npc_dalla_furrows",
  "displayName": "Dalla Furrows",
  "embodied": true,
  "homeLocation": "hinterland.farmland.homes",
  "family": "npc_aldric_thresher",
  "role": "trade_assistant",
  "primaryTrade": "farming",
  "actionAccess": {
    "allowed": ["*"],
    "forbidden": ["smuggle_goods", "pickpocket", ...],
    "favorites": ["harvest", "sell_at_market"]
  }
}
```

The `role: "trade_assistant"` and `primaryTrade` fields make it explicit that this NPC is an extension of their spouse's economic function. Their personality should complement the spouse (e.g., if the farmer is high diligence/low sociability, the spouse might be moderate diligence/high sociability — they handle the selling).

---

## 4.1 — Relationship Strengthening from Social Actions

**Problem**: `chat_with_neighbor` gives +0.05 affinity and `drink_at_tavern` gives +0.03, but these are so small relative to decay (0.0005/tick) that relationships never meaningfully grow. 100 ticks of decay erases one chat.

**Fix**:
- Increase chat affinity: 0.05 -> 0.12
- Increase drink affinity: 0.03 -> 0.08
- Add familiarity growth: +0.05 per social interaction
- Reduce affinity decay: 0.0005 -> 0.0003 (relationships persist longer)

**New property — `trust`**: Added alongside affinity and familiarity.
- Grows slowly from repeated interactions (+0.02 per social action)
- Decays very slowly (0.0001/tick)
- Used in gossip propagation (4.3) and friend preference (4.2)

---

## 4.2 — Weighted Friend Preference in nearbyRandom

**Problem**: `HandleSocial` uses `nearbyRandom` which picks any embodied entity at the same location with equal probability. NPCs don't prefer friends.

**Fix**: Replace uniform random with affinity-weighted selection.

**File**: `ActionExecutor.cs` — `HandleSocial` (line 370-380)

```
candidates = all embodied entities at location (excluding self)
for each candidate:
    relationship = world.Relationships.Get(actor, candidate)
    affinity = relationship?.Properties["affinity"]?.Value ?? 0.5
    weight = 0.3 + affinity * 1.4    // Strangers: 1.0, friends(0.7): 1.28, best friends(1.0): 1.7

chosen = weighted random sample from candidates
```

**Effect**: NPCs naturally cluster into friend groups over time. Strangers can still interact (weight never reaches 0) but friends are preferred. Family members (high initial affinity) strongly prefer each other.

---

## 4.3 — Gossip Content Types

**Problem**: Gossip is generic modifier copying — no content, no information asymmetry.

**Solution**: Add a `gossipType` field to modifiers that carries semantic meaning.

**New gossip types**:

| Type | Created By | Effect When Heard | Gameplay Impact |
|---|---|---|---|
| `food_location` | sell_at_market, deliver_food_* | ActionBonus: +0.15 to eat_at_market at the gossip's source location | NPCs learn where food is |
| `noble_weakness` | spread_rumor | ActionBonus: +0.10 to persuade, +0.08 to intimidate | Political information spreads |
| `danger_warning` | patrol (when encountering crime) | ActionBonus: -0.10 to visit the dangerous location | NPCs avoid dangerous areas |
| `tavern_quality` | eat_at_tavern, drink_at_tavern | ActionBonus: +0.08 to eat/drink at that tavern | Word-of-mouth reputation |

**Implementation**:
- Add `GossipType` enum/string to Modifier
- In HandleSocial gossip propagation, copy the type along with the modifier
- In UtilityScorer, gossip-type modifiers can carry location-specific bonuses (new field: `gossipLocation`)

---

## 4.4 — Family Pairs

**Data format** (in `relationships/families.json`):
```json
[
  {
    "members": ["npc_aldric_thresher", "npc_dalla_furrows"],
    "type": "spouse",
    "home": "hinterland.farmland.homes",
    "trade": "farming",
    "initialAffinity": 0.85,
    "initialTrust": 0.70
  },
  {
    "members": ["npc_torben_anvil", "npc_yrsa_steelhand"],
    "type": "spouse",
    "home": "hinterland.quarry.residences",
    "trade": "smithing",
    "initialAffinity": 0.80,
    "initialTrust": 0.65
  }
]
```

**Behavioral effects**:
- Family members get a mood bonus when at the same location (+0.02/tick)
- Family members prefer social actions with each other (affinity-weighted, 4.2)
- Family members share gold (highest earner's gold is accessible to the family unit)
- If a family member's mood drops below 0.2, the other gets an anxiety modifier (mood -0.05, social -0.03)
- **Trade assistant spouses** have favorite actions aligned with their partner's trade, so they naturally work together

**Selection criteria**:
- Pair NPCs at the same home location with complementary jobs
- Spouse personality should complement (not duplicate) the tradesman's
- Don't pair ALL NPCs — leave singles for social mobility and narrative variety
- Children are non-acting dependents that increase household food consumption

---

## 4.5 — Eat-Together Bonus

**New action**: `eat_together`

```json
{
  "id": "eat_together",
  "displayName": "Eat Together",
  "category": "sustenance",
  "requirements": {
    "embodied": true,
    "propertyMin": { "gold": 2 },
    "nearbyFamily": true
  },
  "propertyResponses": {
    "hunger": { "curve": "desperate", "magnitude": 0.5 },
    "social": { "curve": "linear", "magnitude": 0.3 },
    "mood": { "curve": "smooth_step", "magnitude": 0.2 }
  },
  "steps": [
    { "type": "moveTo", "target": "nearestTagged:tavern" },
    { "type": "wait", "duration": 6 },
    { "type": "modifyProperty", "property": "hunger", "amount": 0.45 },
    { "type": "modifyProperty", "property": "social", "amount": 0.20 },
    { "type": "modifyProperty", "property": "mood", "amount": 0.15 },
    { "type": "modifyProperty", "property": "gold", "amount": -4 },
    { "type": "modifyProperty", "property": "food_supply", "amount": -2, "entity": "target:location" },
    { "type": "socialInteraction", "targetEntity": "nearbyFamily", "relationshipProperty": "affinity", "relationshipAmount": 0.08, "propagateModifiers": true }
  ]
}
```

**New requirement**: `nearbyFamily: true` — checks if any family member is at the same location. Needs new requirement handler in `UtilityScorer.MeetsRequirements`.

**New target type**: `nearbyFamily` — resolves to a family member at the same location (similar to nearbyRandom but filtered).

---

## 4.6 — Social Memory

**Problem**: NPCs don't remember who they talked to. They can chat with the same NPC 10 times in a row.

**Solution**: When a social interaction occurs, create a memory modifier that penalizes socializing with the same target.

**Implementation** (in `HandleSocial`):
```
After socialInteraction completes:
  Create modifier on actor:
    id: "social_mem_{actorId}_{targetId}"
    type: "social_memory"
    actionBonus: { "chat_with_neighbor": -0.05 }
    duration: 100 ticks
    intensity: 0.8
    decayRate: 0.005
    socialTarget: targetId
```

**In nearbyRandom selection** (4.2 weighted selection): Check for active `social_mem_*` modifiers. If actor has a social memory of a candidate, reduce that candidate's selection weight by 50%. Naturally spreads social interactions across available NPCs.

---

## 4.7 — Friend-of-Friend Trust Networks

**Mechanism**: When NPC A gossips to NPC B, and A has high trust with B, then B receives the information at greater strength.

**Implementation**:
- When gossip propagates in HandleSocial, multiply the copied modifier's intensity by the relationship trust value:
  ```
  copiedIntensity = original.Intensity * 0.5 * trust(actor, target)
  // High trust (0.8): intensity = original * 0.4 (strong pass-through)
  // Low trust (0.2): intensity = original * 0.1 (barely registers)
  ```
- Replaces the current flat 0.5 halving with trust-weighted halving.
- Information from trusted friends carries more weight than rumors from strangers.

**Effect**: Social networks form information channels. A tight-knit group of friends shares information effectively. An outsider's gossip barely penetrates.

---

## Files Affected

**Engine (C#)**:
- `ActionExecutor.cs` — friend-weighted nearbyRandom (4.2), social memory creation (4.6), trust-weighted gossip (4.7)
- `UtilityScorer.cs` — nearbyFamily requirement (4.5)
- `Modifier.cs` — add GossipType, GossipLocation, SocialTarget fields (4.3, 4.6)
- `ModifierManager.cs` — gossip content type handling (4.3)
- `RelationshipStore.cs` — trust property support (4.1)
- Family loading in world loader (4.4)

**World Data (JSON)**:
- `actions/chat_with_neighbor.json` — increase affinity amounts (4.1)
- `actions/drink_at_tavern.json` — increase affinity amounts (4.1)
- `actions/eat_together.json` — NEW (4.5)
- `relationships/families.json` — NEW (4.4)
- `relationships/social.json` — reduce decay rates (4.1)
- `autonomes/*.json` — ~50-70 NEW spouse/child/single NPC profiles (4.4)
- Various actions — add gossipType to modifier-emitting steps (4.3)
