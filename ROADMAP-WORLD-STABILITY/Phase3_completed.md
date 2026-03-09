# Phase 3: Gold Circulation

Status: **completed** (2026-03-09)
Unblocked by: Phase 2 (Food Pipeline) — completed 2026-03-09

**Completion Notes**:
- Rent system: rentPerCycle=35, cycle=480 ticks. City rent → org_city_council, manor → noble, hinterland → local towns
- Tax passive: org_city_council emits collect_taxes +0.3 bonus when gold < 500
- collect_taxes: NPC gets 3g commission, council gets 12g
- Merchant guild: gets 1g cut from trade_goods and sell_at_market
- Harbor authority: gets 10g/merchant vessel + 6g/supply barge docking fees (modify_entity_property events)
- Validation: Gini=0.587 (PASS <0.6), no org depleted (PASS), median NPC gold=119 (above 15-50 target, deferred to Phase 6.5)
- Balance improved from 4 failures/17 warnings to 2 failures/7 warnings (both pre-existing)

**Unblocks**: Phase 6 (Equilibrium Tuning — gold equilibrium check)

---

> With food flowing, add gold sinks so wealth circulates instead of accumulating.

## Population Impact

Minimal. May add 1-2 dedicated tax collector NPCs if the existing bureaucrats (Aldren, Nessa, Voss) can't cover it. More likely: existing NPCs absorb the new collect_taxes urgency via passive directives.

---

## 3.1 — Rent System

**Mechanism**: A new periodic property drain applied during PropertyTicker, not via actions.

**Implementation approach**: Add an `upkeep` section to entity profiles (or derive from homeQuality):

```
rent_per_cycle = homeQuality * 10   // manor(0.95)=9.5g, residential(0.80)=8g, slums(0.50)=5g
cycle_length = 480 ticks            // ~1 game-day (480 minutes at 1 min/tick)
drain_per_tick = rent_per_cycle / cycle_length
```

**File**: `PropertyTicker.cs` — add new `TickUpkeep` method called from `TickAll`

**Rules**:
- Rent drains gold from NPC. If gold reaches 0, rent stops (NPCs don't go into debt).
- Drained gold is added to the location's org owner (e.g., city_council gets rent from civic/residential, noble gets rent from manor).
- Creates natural gold flow: NPCs -> orgs -> directives -> back to NPCs (via rewards, hiring).

**Knock-on effect**: NPCs with low gold now have urgency to work/trade for income. This naturally diversifies behavior away from pure eat/rest loops.

---

## 3.2 — Tax Collection

**Simpler approach**: Make tax collection a passive effect on `org_city_council`:
```json
{
  "passiveEffects": [
    {
      "condition": "below",
      "property": "gold",
      "threshold": 500,
      "emit": {
        "targetScope": "subordinates",
        "actionBonus": { "collect_taxes": 0.3 },
        "priority": "urgent"
      }
    }
  ]
}
```

When council gold drops below 500, subordinate NPCs get a bonus to collect_taxes. Self-regulating: council goes broke -> taxes increase -> council recovers -> taxes relax.

---

## 3.3 — Org Revenue Balancing

**Problem**: Org treasuries (city council, ironforge, silver compact) bleed gold with no income.

**Fix**: Ensure orgs receive gold from their economic activities:
- Tavern orgs: already receive gold from eat_at_tavern (+3g) and eat_fine_meal (+6g)
- City council: receives rent (3.1) + taxes (3.2)
- Noble: receives levy_tribute income
- Merchant guild: should receive a cut of trade_goods transactions
- Harbor authority: should receive docking fees from ship arrivals (add to events.json)

**Implementation**: Add `"modifyProperty": { "property": "gold", "amount": X, "entity": "target:ORG_ID" }` steps to relevant actions.

---

## Files Affected

**Engine (C#)**:
- `PropertyTicker.cs` — TickUpkeep method (3.1)
- `SimulationRunner.cs` — call TickUpkeep in TickOnce (3.1)

**World Data (JSON)**:
- `autonomes/org_city_council.json` — passive tax effect (3.2)
- `events.json` — harbor docking fees (3.3)
- Various `actions/*.json` — add org gold steps to trade/work actions (3.3)
