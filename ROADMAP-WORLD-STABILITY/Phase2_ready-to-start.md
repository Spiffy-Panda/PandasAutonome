# Phase 2: Food Pipeline Repair

Status: **ready-to-start**
Unblocked by: Phase 1 (Core Behavior Fixes)

**Unblocks**: Phase 3 (Gold Circulation), Phase 5 (UI Polish)

---

> With behavior fixes in place, make the supply chain actually deliver food to consumers.

## Population Impact

Small growth. May add 2-4 new NPCs:
- 1-2 dedicated teamster haulers if existing teamsters can't keep up
- 1 additional baker if bake_bread throughput is insufficient
- These NPCs go into existing hinterland/docks homes

---

## 2.1 — Farm-to-City Distribution

**New action**: `sell_at_market`

```json
{
  "id": "sell_at_market",
  "displayName": "Sell Produce at Market",
  "category": "trade",
  "requirements": {
    "embodied": true,
    "propertyMin": { "trade_goods_food": 2 }
  },
  "personalityAffinity": { "frugality": 1.3, "diligence": 1.2 },
  "propertyResponses": {
    "trade_goods_food": { "curve": "inverse_linear", "magnitude": 0.4 },
    "gold": { "curve": "linear", "magnitude": 0.15 }
  },
  "steps": [
    { "type": "moveTo", "target": "nearestTagged:market" },
    { "type": "wait", "duration": 3 },
    { "type": "modifyProperty", "property": "trade_goods_food", "amount": -2 },
    { "type": "modifyProperty", "property": "gold", "amount": 8 },
    { "type": "modifyProperty", "property": "food_supply", "amount": 2, "entity": "target:location" }
  ],
  "memoryGeneration": {
    "actionBonus": { "sell_at_market": -0.15, "harvest": 0.10, "plow_field": 0.08 },
    "decayRate": 0.005,
    "intensity": 0.4,
    "duration": 60,
    "flavor": "Sold produce at the market..."
  }
}
```

**Key design**: The memory bonus encourages a harvest -> sell -> harvest cycle. The inverse_linear curve on trade_goods_food means the more food NPCs carry, the more motivated they are to sell it.

**Add to allowed lists**: All farmer NPCs (6-8 in hinterland) need `sell_at_market` in their allowed actions. Farmers use wildcard `"*"` so this works automatically as long as it's not in their forbidden list.

---

## 2.2 — Fix Bake Bread Routing

**Problem**: `bake_bread` deposits food_supply at `city.residential.craftsman_row` — not a food-tagged location, so nobody eats there.

**Fix**: Change `bake_bread` moveTo target to `city.market.square` and deposit food_supply there:
```json
"steps": [
  { "type": "moveTo", "target": "city.market.square" },
  { "type": "wait", "duration": 8 },
  { "type": "modifyProperty", "property": "trade_goods_food", "amount": -1 },
  { "type": "modifyProperty", "property": "food_supply", "amount": 1, "entity": "target:location" }
]
```

---

## 2.3 — Reduce Tavern Food Decay

**Problem**: All 6 taverns decay food at 0.0008 (proportional). A full tavern (100 units) loses 0.08 units/tick. Over 200 ticks between ship arrivals, that's 16 units lost to pure decay.

**Fix**: Reduce tavern food decay from 0.0008 to 0.0003 (matching markets).

**File**: `worlds/coastal_city/locations/valley.json` — all tavern food_supply properties

**Rationale**: Taverns are indoor, preserved environments. The 0.0003 rate means a full tavern loses ~6 units per 200 ticks instead of 16 — restocking can now outpace spoilage.

---

## 2.4 — Teamster Urgency Bonuses

**Problem**: Teamster Union (`org_teamster_union`) has only one action (`teamster_dispatch_haulers`) and runs it 100% of the time. Individual teamster NPCs don't respond dynamically to supply shortages.

**Solution**: Add reactive utility bonuses to delivery actions based on destination food_supply level.

**New action variants** (or modify existing `deliver_food_*`):
```json
{
  "propertyResponses": {
    "trade_goods_food": { "curve": "inverse_linear", "magnitude": 0.35 }
  },
  "requirements": {
    "propertyMin": { "trade_goods_food": 4 }
  }
}
```

**Passive urgency from Teamster Union**: When harbor food_supply > 100 (ships just arrived), emit directive with actionBonus to `pickup_harbor_food` (+0.4, urgent priority). Creates a surge of pickup activity after ship arrivals.

```json
{
  "passiveEffects": [
    {
      "condition": "above",
      "property": "food_supply",
      "threshold": 100,
      "location": "city.docks.harbor",
      "emit": {
        "targetScope": "subordinates",
        "actionBonus": { "pickup_harbor_food": 0.4 },
        "priority": "urgent",
        "duration": 30
      }
    }
  ]
}
```

---

## 2.5 — Verify Mill Output Routing

**Check**: Confirm `mill_grain` deposits food_supply at `hinterland.farmland.mill` and that this location is reachable from market/tavern nodes. If mill is a dead end, add a `haul_from_mill` action or change mill's moveTo destination.

---

## Files Affected

**World Data (JSON)**:
- `actions/sell_at_market.json` — NEW (2.1)
- `actions/bake_bread.json` — fix routing (2.2)
- `locations/valley.json` — tavern decay rates (2.3)
- `autonomes/org_teamster_union.json` — passive urgency (2.4)
- `actions/deliver_food_*.json` — property response tuning (2.4)
- Possibly `actions/haul_from_mill.json` — NEW if mill routing broken (2.5)
