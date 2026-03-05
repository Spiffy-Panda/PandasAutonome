# Autonome Simulator: Long-Term Design Roadmap

## Where We Are

The engine is architecturally mature. We have a unified autonome system that treats individuals and organizations identically, a property/curve-based utility scorer, modifier-driven memory and directive systems, authority graphs, location routing, and a web analysis console. The coastal city world (Part 1) is complete — 44 locations using dot-notation hierarchy, 76 NPCs across city and hinterland, 6 city organizations, ~67 actions, and authority/social relationship graphs. The simulation runs at 15-minute ticks with day/night scoring, dynamic memories, trade loops, and passes a 2000-tick balance verification with no failures.

The power structure (Phase 2) is now in place. Lord Ashworth sits at the top of the authority hierarchy with loyalty edges to the city council and city watch. The noble has 5 governance actions (hold_court, reward_loyalist, issue_decree, levy_tribute, punish_dissent) and 5 political actions exist for entities to challenge him (persuade, bribe, intimidate, spread_rumor, build_alliance). The noble maintains stable authority over 2000 ticks without external interference — legitimacy and influence stay healthy, gold is sustainable, and morale slowly declines creating designed vulnerability for the overthrow scenario. The simulation passes MOSTLY BALANCED (0 failures, 14 warnings).

What we don't have yet is the *external controller interface* that lets a human, bot, or LLM act in the world. The loyalty threshold mechanic (subordinates ignoring directives below a loyalty threshold) uses a soft multiplier — low loyalty reduces directive influence but doesn't block it entirely. A hard threshold could be added in a future phase if needed.

---

## Design Goal

This is **not an RPG**. There are no quests, no scripted events, no storylines to follow.

This is a **political sandbox simulation**. The world runs on its own through utility AI — NPCs eat, work, trade, sleep, socialize, and respond to pressures. Organizations levy taxes, recruit members, run patrols, and compete for influence. The simulation produces emergent behavior without authorial direction.

The design target is a single concrete scenario: **an externally controlled autonome can overthrow the local noble**. The external controller can be:

- A human player making decisions through a UI
- A test script exercising specific strategies
- An LLM agent reasoning about world state
- Any program that reads state and submits actions through the API

The external autonome has **no special powers**. It exists in the world as an entity with the same properties, actions, modifiers, and constraints as any NPC. It can eat, work, trade, chat, bribe, threaten, and scheme — all through the same ActionExecutor pipeline. The difference is that its decisions come from outside the simulation rather than from the utility scorer.

**Overthrow is not a scripted event.** It's an emergent world state. The noble sits at the top of the authority hierarchy with loyalty edges to the city council and key organizations. "Overthrow" means the noble's authority graph has effectively disconnected — loyalty has dropped below functional thresholds, subordinates ignore directives, rival factions hold more influence, and the noble's properties (gold, defense, influence) have collapsed. No flag gets set. No cutscene plays. The world simply reflects that power has shifted.

**Success criteria for the simulation:** an external controller that understands the systems can, through a sequence of ordinary actions over hundreds of ticks, shift the political balance of power. The world must also be resilient enough that a *bad* strategy fails — a naive controller that just spams one action should get nowhere or get caught.

---

## Part 1: The Coastal City — World Design ✅

### Vision

A single walled port city with surrounding hinterland. Not three disconnected towns — one city with districts, social strata, and economic pressure from the sea. NPCs have routines that can be observed, disrupted, and exploited by any entity — human or automated.

### District Layout

```
                    [Deep Sea]
                        |
    [Lighthouse]---[Harbor/Docks]---[Fishmarket]
                        |
        [Warehouse]---[Portside Quarter]---[Shipyard]
                        |
    [Slums/Shanties]---[Market Square]---[Merchant Row]
                        |
        [Temple]---[Civic Center]---[Guard Barracks]
                        |
    [Craftsman Row]---[Residential Quarter]---[Manor District]
                        |
        [Farmland]---[City Gate]---[Forest Road]
                        |
            [Quarry]---[Hinterland]---[Woodlands]
```

**Relationship to existing world data:** The three current valley settlements (Millhaven, Ironforge, Thornwatch) map to the hinterland areas in this diagram. Millhaven becomes the **Farmland** cluster, Ironforge becomes the **Quarry** cluster, and Thornwatch becomes the **Woodlands** cluster. The coastal city is not a replacement — it extends the existing world by adding the city proper above the hinterland line. Existing NPC profiles, actions, and relationships in those settlements carry forward; they gain new connections upward into the city districts.

### Structured Location Naming

Location IDs use dot-separated hierarchical names to encode geographic containment and aid navigation routing. The format is:

```
<region>.<district>.<specific_location>
```

**Examples:**

| Old-style ID | Structured ID | Purpose |
|---|---|---|
| `loc_harbor_docks` | `city.docks.pier` | Specific berth area |
| `loc_mh_farmlands` | `hinterland.farmland.fields` | Millhaven farmland |
| `loc_if_mine` | `hinterland.quarry.mine_shaft` | Ironforge mine |
| `loc_tw_square` | `hinterland.woodlands.camp` | Thornwatch commons |
| `loc_residential_house_3` | `city.residential.house3` | A specific home |
| — | `city.slums.shanty_row` | Dockside slum housing |
| — | `city.manor_district.estate2` | Wealthy estate |

**Routing benefits:**
- Path queries can short-circuit by hierarchy — `city.docks.*` to `city.market.*` stays within `city`, no need to evaluate hinterland nodes
- Home assignments read naturally: `"home": "city.slums.shanty4"` immediately tells you social stratum and district
- District-level queries become trivial: "all locations in `city.docks.*`" vs tag-based filtering
- The hierarchy mirrors the wealth gradient: `city.docks` (poor) → `city.residential` (middle) → `city.manor_district` (rich)

**Top-level regions:**
- `city` — the walled port city and its districts
- `hinterland` — surrounding countryside (farmland, quarry, woodlands)
- `sea` — deep sea, lighthouse, shipping lanes

**Key principles:**
- Wealth gradient from docks (poor) to manors (rich)
- Economic flow: goods arrive at harbor, move inward through markets
- Social stratification reflected in where people live and eat
- Any entity can observe flow by standing in the market square
- ~40 locations, connected with realistic travel costs

### NPC Population (~60-80)

| Stratum | Count | Archetypes |
|---------|-------|------------|
| Dockworkers | 8-10 | Longshoremen, fishermen, net-menders |
| Tradespeople | 10-12 | Smiths, carpenters, tanners, bakers |
| Merchants | 6-8 | Importers, shopkeepers, money-lenders |
| Civic | 6-8 | Mayor, tax collector, clerks, priests |
| Military | 8-10 | City guard, watch captain, militia |
| Service | 6-8 | Tavern keepers, cooks, servants |
| Underclass | 4-6 | Beggars, pickpockets, smugglers |
| Hinterland | 8-10 | Farmers, woodcutters, quarry workers |

### Organizations

- **City Council** — mayor + civic officials, levies taxes, sets laws
- **Merchant Guild** — controls market stalls, sets prices, foreign trade
- **Harbor Authority** — manages docks, collects tariffs, allocates berths
- **City Watch** — patrols, guards gates, reports to council
- **Thieves' Guild** — underground, smuggling, protection rackets
- **Temple** — provides healing, social pressure, charity

### Power Structure — The Noble

*Not yet implemented. Required for the overthrow scenario.*

The city needs a political apex — a noble (lord, baron, or governor) who sits above the city council in the authority hierarchy. This entity is the target of the overthrow scenario.

```
Lord / Noble
    ├── City Council (loyalty edge)
    │       ├── Mayor, Tax Collector, Clerk, Priest
    ├── City Watch (loyalty edge, defense arm)
    │       ├── Watch Captain, Guards
    └── (indirect) Merchant Guild, Harbor Authority, Temple
                    (via council directives and economic pressure)
```

**The noble is an autonome like any other.** Same property system, same utility scoring, same action pipeline. The noble has:

- **Properties:** gold, influence, legitimacy, defense — all subject to decay and action-driven replenishment
- **Authority edges:** loyalty connections to the council and watch, with loyalty values that decay if unattended
- **Actions:** levy taxes, issue decrees, hold court, reward loyalists, punish dissent — all standard action definitions
- **Personality:** drives how the noble responds to threats (cautious noble hoards gold, aggressive noble cracks down)

**What makes the noble vulnerable:**

- Loyalty on authority edges decays naturally — the noble must actively maintain it through rewards, appearances, and directives
- Subordinate NPCs with low loyalty ignore or resist directives, weakening the noble's ability to act through others
- Influence and legitimacy are properties that can be eroded by rival actions (spreading rumors, bribing officials, running rackets)
- Gold depletion prevents the noble from taking actions that cost money (rewarding loyalists, funding the watch)
- If enough loyalty edges drop below threshold simultaneously, the noble's authority graph disconnects — overthrow

**What protects the noble:**

- The city watch enforces order (patrols detect crime, guards respond to threats)
- Economic momentum — taxes flow upward, the noble starts wealthy
- Social inertia — NPCs default to following authority unless given reason not to
- The noble's own utility scoring — a well-tuned noble AI fights back, reallocating resources to shore up weak loyalty edges

---

## Part 2: The Home System

### Why It Matters

Right now NPCs teleport to `rest_at_home` and the location is wherever they happen to be. In the game, players will see NPCs walk home at night, lock their doors, and emerge in the morning. Homes are the anchor that makes NPCs feel like *people*.

### Implementation

**Per-NPC home assignment:**
```json
// In autonome profile
"home": "city.residential.house3"
```

**Engine change:** `rest_at_home` action's `moveTo` step uses `target: "home"` instead of `nearestTagged:rest`. The ActionExecutor resolves `"home"` from the profile.

**Home properties to track:**
- Quality (affects rest restoration amount)
- Rent cost (periodic gold drain)
- Location (determines travel time to work)
- Capacity (future: families, roommates)

**Tuning needed:**
- Travel time home vs rest urgency — NPCs far from home may collapse before arriving
- Rest restoration should scale with home quality (shanty +0.5, manor +0.9)
- Morning departure time should vary by occupation (fishermen leave at 4am, merchants at 8am)

---

## Part 3: Economy

### Current State

Gold flows in circles: NPCs work → earn gold → buy goods → sell goods → earn gold. There's no scarcity, no supply chain, no external pressure. Every NPC has access to infinite food at farms and infinite ore at mines.

### Target Economy

**Supply chains with real bottlenecks:**

```
Fish (harbor) ──→ Fishmarket ──→ Taverns/Homes
Grain (farms) ──→ Mill ──→ Baker ──→ Market
Ore (quarry) ──→ Smith ──→ Tools/Weapons ──→ Market
Timber (forest) ──→ Carpenter ──→ Buildings/Ships
Imports (ships) ──→ Harbor ──→ Warehouse ──→ Merchants
```

**What needs to change:**

1. **Location-bound resources** — Farms produce food *at the farm*. It doesn't exist until someone harvests it. The market doesn't have food until someone carries it there.

2. **Inventory on locations** — Locations need property bags (stock levels). A tavern runs out of food if nobody supplies it. A smithy runs out of ore if nobody delivers it.

3. **Price signals** — When tavern food stock is low, the buy price rises. NPCs with food to sell are drawn there by higher profit. This creates emergent supply chains without scripting.

4. **Ship arrivals** — Periodic external events: a trade ship docks, offloading goods at the harbor. Creates a burst of economic activity. Missing ships cause shortages.

5. **Tax and rent** — Periodic gold drains force NPCs to work. Without taxes, wealthy NPCs retire and the simulation stagnates.

### Tuning Concerns

| Parameter | Risk if too high | Risk if too low |
|-----------|-----------------|-----------------|
| Food decay at locations | Constant shortage, NPCs starve | No urgency to resupply |
| Gold from work actions | Inflation, everyone rich | Can't afford food, death spiral |
| Tax rate | NPCs can't save, no investment | No gold sink, economy inflates |
| Import frequency | Locals can't compete | Shortages, price spikes |
| Travel cost (ticks) | NPCs stuck commuting | Distance meaningless |
| Rest restoration at home | NPCs never tired | NPCs always sleeping |

---

## Part 4: Social Dynamics

### Current State

Relationships exist in data but don't meaningfully drive behavior. An NPC's "friend" doesn't affect what they do. Loyalty decays but nobody notices.

### Target Social System

**Relationship evolution through shared experience:**
- NPCs who work at the same location build familiarity
- NPCs who eat at the same tavern build affinity
- NPCs who compete for the same resources build rivalry
- Relationship changes happen in `socialInteraction` steps, triggered by proximity

**Gossip and reputation:**
- When NPCs socialize (`chat_with_neighbor`, `drink_at_tavern`), they exchange information
- "Heard that Merchant Elda raised prices" — creates a temporary modifier on nearby NPCs affecting their trade actions
- Reputation emerges from observed behavior, not a single number

**Faction pressure:**
- Guild membership creates obligations (dues, duties)
- Failing obligations lowers loyalty, which reduces directive effectiveness
- Low loyalty NPCs might defect, leave the guild, or become informants

### Tuning Concerns

- Affinity growth rate — too fast and everyone's friends, too slow and relationships are static
- Gossip propagation — too fast and information is instant, too slow and it's irrelevant
- Loyalty decay vs reinforcement — guilds need to actively maintain loyalty through rewards and events

---

## Part 5: External Autonome Model

### Design Philosophy

An externally controlled autonome exists in the same world as NPCs. It doesn't have a god-view — it experiences the city at street level. Its power comes from *understanding the systems* and *acting within them*, not from special privileges. Whether controlled by a human, a test script, or an LLM, the external autonome is mechanically identical to any NPC.

### Action Categories Available to External Autonomes

**Observation (reading world state):**
- See NPCs present at current location
- See available actions and their expected effects
- See own properties, relationships, and active modifiers
- See public information (market prices, posted decrees, visible events)

**Economic actions:**
- Buy/sell goods (affects local supply/demand)
- Work at occupations (earn gold, build reputation)
- Bribe officials (inject gold to modify loyalty/behavior)
- Undercut competitors (price manipulation)

**Social actions:**
- Talk to NPCs (builds relationship, gathers information)
- Spread rumors (inject gossip modifiers that propagate through social actions)
- Do favors (creates obligation modifiers on targets)
- Threaten (fear-based compliance, temporary, risks detection)

**Political actions:**
- Build faction loyalty (repeated positive interactions with organization members)
- Erode rival loyalty (bribe, intimidate, or persuade subordinates away from their authority)
- Gain guild membership (access guild-level actions and information)
- Accumulate influence (property that gates political actions)

### Engine Requirements

1. **External autonome as entity** — The external autonome is an autonome in the simulation with properties, relationships, and modifiers. Its actions come from external input rather than utility scoring. The engine doesn't know or care what's controlling it.

2. **Action injection** — When the external autonome performs an action, it goes through the same ActionExecutor pipeline. Same steps, same property modifications, same event recording, same modifiers and cooldowns.

3. **Tick synchronization** — External autonomes submit actions between ticks, same as NPCs make decisions between ticks. One action per tick. The external controller waits for tick resolution before acting again.

4. **Visibility model** — External autonomes only see what an entity at their location could observe. Actions in private go unnoticed. Actions in the market square are witnessed by everyone present. Information is location-scoped.

5. **Consequence propagation** — External autonome bribes a guard → guard's loyalty to watch captain drops → watch captain's org morale drops → city watch patrols less effectively → thieves' guild runs more rackets → noble's influence erodes. This plays out over ticks, not instantly.

---

## Part 6: External Controller Interface

### Purpose

Expose the simulation backend so that external controllers (humans, bots, scripts, LLM agents) can connect and act in the world. A lightweight web prototype validates the external-autonome-as-entity concept before building Godot integration. This keeps the feedback loop fast and — critically — makes the simulation testable: run an LLM agent against the overthrow scenario and measure whether the power structure is appropriately resilient or fragile.

### Architecture

```
Browser (React/vanilla)          Autonome Engine (C#)
┌─────────────────────┐          ┌──────────────────────┐
│ World Map View       │◄──WS──►│ WebSocketServer       │
│ NPC Status Panel     │         │   ↕                   │
│ Action Menu          │         │ SimulationRunner      │
│ Property Inspector   │         │ ExternalSlot[]        │
│ Event Log            │         │ ActionExecutor        │
└─────────────────────┘          └──────────────────────┘
         ▲                                ▲
         │            REST API            │
External Bots/Agents ────────────────────►│
  (Python, JS, etc.)     POST /act        │
                         GET /state       │
                         WS /stream       │
```

### REST + WebSocket API

**Core endpoints:**

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/world/state` | GET | Current world snapshot — all locations, NPC positions, property summaries |
| `/api/world/tick` | GET | Current tick number, time-of-day, recent events |
| `/api/entity/{id}/state` | GET | Entity's properties, location, relationships, active modifiers |
| `/api/entity/{id}/actions` | GET | Available actions at entity's current location (filtered by requirements) |
| `/api/entity/{id}/act` | POST | Submit an action — goes through the same ActionExecutor pipeline as NPCs |
| `/api/entity/register` | POST | Register an external autonome slot — returns entity ID and auth token |
| `/ws/stream` | WS | Real-time event stream — tick advances, NPC actions, property changes, location arrivals/departures |

**Design constraints:**
- External autonomes submit actions between ticks, not during tick resolution
- One action per entity per tick (same as NPCs)
- External autonomes see only what an entity at their location could observe (visibility model applies)
- Auth tokens are simple bearer tokens — this is a prototype, not production auth

### Web UI (Human Controller)

Minimal interface for a human to control an external autonome:

- **Map view** — Graph visualization of locations, entity position highlighted, NPC counts per location
- **Location detail** — NPCs present, available actions, local events
- **Action picker** — Select from available actions, see expected property changes before confirming
- **Property panel** — Entity's hunger, rest, gold, mood, social, influence as bars
- **Event log** — Scrolling feed of observable events (NPC arrivals, trade, gossip)
- **Tick controls** — Advance manually, auto-advance at configurable speed, pause

### External Controller Protocol

External controllers interact through the REST API. A typical bot loop:

```
1. GET /api/entity/{id}/state     → read own properties
2. GET /api/entity/{id}/actions   → see what's available
3. POST /api/entity/{id}/act      → submit chosen action
4. WS /stream (or poll /tick)     → wait for tick to resolve
5. Repeat
```

This allows:
- Python/JS bots competing in the same economy as NPCs
- LLM agents reasoning about world state and choosing political strategies
- Multiple external autonomes simultaneously
- Automated testing — run an overthrow strategy and measure how long it takes, whether the noble's AI fights back effectively, whether the world destabilizes

### What This Validates

- External-autonome-as-entity architecture works end-to-end
- Action injection pipeline handles external input correctly
- Visibility model filters information properly
- Consequence propagation is observable over time
- The noble's power structure is appropriately resilient (not trivially overthrown, not impossibly defended)
- Multiple simultaneous external autonomes don't break the simulation

---

## Part 7: Godot Integration

### Architecture

```
Godot (GDScript/C#)          Autonome Engine (C#)
┌─────────────────┐          ┌──────────────────┐
│ 3D World        │◄────────►│ WorldState        │
│ NPC Scenes      │          │ SimulationRunner  │
│ External Input  │          │ UtilityScorer     │
│ UI/HUD          │          │ ActionExecutor    │
│ Animation       │          │ LocationGraph     │
└─────────────────┘          └──────────────────┘
         ▲                            ▲
         │         Shared             │
         └────── EntityRegistry ──────┘
```

**Tick synchronization:**
- Godot process loop runs at 60fps
- Simulation ticks at configurable rate (1 tick per N seconds of real time)
- Between ticks: animations play out, external autonome moves freely
- On tick: simulation advances, NPCs make decisions, world updates

**NPC scene requirements:**
- Navigation mesh pathfinding (replaces teleport)
- Animation state machine (idle, walk, work, eat, sleep)
- Interaction zone (entities can interact when nearby)
- Home node reference (despawn to interior scene at night)
- Speech bubble / thought indicator (shows current action)

**Data flow:**
- Simulation decides "Aldric will plow_field"
- Godot receives action event, translates to: navigate to farm → play plow animation → wait N seconds
- While animating, simulation is paused or running in parallel
- On completion, Godot signals back → ActionExecutor finalizes → property changes apply

### What Needs Building

1. **SimulationBridge** — C# class that wraps SimulationRunner for Godot, providing tick-by-tick control instead of batch execution
2. **ActionAnimator** — Maps action step types to Godot animation sequences
3. **NPCController** — Godot node that receives actions and executes them visually
4. **WorldSync** — Keeps Godot scene tree in sync with EntityRegistry (spawn/despawn NPCs, update positions)
5. **ExternalAutonomeController** — Translates external input (human, bot, LLM) into ActionDefinition-compatible actions via the same API used in the web prototype

---

## Part 8: Tuning and Balancing Guide

### Parameters That Need Tuning

**Time Scale (currently 15 min/tick, 96 ticks/day):**
- Works well for headless simulation
- For real-time game: need configurable speed (1 tick/second at normal, faster when sleeping)
- Day length in real minutes affects how much can be observed per cycle

**Property Decay Rates:**
| Property | Current Range | Balancing Goal |
|----------|--------------|----------------|
| hunger | 0.008-0.015 | 3 meals/day, desperation below 0.25 |
| rest | 0.005-0.009 | 1 sleep/day, work penalties additive |
| social | 0.002-0.008 | Socializes every 1-2 days |
| mood | 0.001-0.003 | Slow drift, responds to events |
| gold | 0 (no decay) | Consider rent/tax as pseudo-decay |

**Action Durations (ticks):**
| Category | Current Range | Notes |
|----------|--------------|-------|
| Eating | 1-3 | Fast, interruptible feel |
| Social | 2-3 | Short conversations |
| Work | 10-16 | 2.5-4 hour shifts, core of the day |
| Trade | 2-4 | Quick transactions |
| Rest | 28 | 7 hours, anchors night cycle |

**Night Debuff (currently 0.7x for work/trade/leisure):**
- May need per-action granularity (night fishing is a thing, night farming is not)
- Consider seasonal variation (shorter nights in summer)
- Guard patrol should get a slight night *bonus* to represent need

**Memory Generation:**
| Parameter | Current | Watch For |
|-----------|---------|-----------|
| Eat memory decay | 0.003 | If too fast, NPCs don't develop preferences |
| Trade memory duration | 48 ticks | If too short, buy-sell loop breaks |
| Accumulate max intensity | 0.8 | If too high, memories dominate personality |
| Refresh intensity | 0.5 | If too low, memories are irrelevant noise |

### Balancing Methodology

**Step 1: Solo NPC sandbox**
- Run a single NPC in isolation with full action access
- Verify: eats 3x/day, sleeps 1x/day, works 4-6 hours, socializes occasionally
- Adjust decay rates until rhythm feels right

**Step 2: Pair interaction**
- Two NPCs, one workplace
- Verify: both develop routines, don't deadlock, relationships evolve
- Check: do they compete for resources? Does one starve the other?

**Step 3: Small group (8-10)**
- One district, mixed occupations
- Verify: economic loop functions (producer → market → consumer)
- Check: wealth distribution, action diversity, no single dominant strategy

**Step 4: Full city (60-80)**
- All districts, all supply chains
- Verify: emergent specialization, district character, social stratification
- Check: performance (scoring 80 NPCs × 38 actions per tick), stability over 1000+ ticks

**Step 5: External autonome injection**
- Add external autonome entity, perform economic and political actions
- Verify: consequences propagate, NPCs react plausibly, noble AI responds to threats
- Check: can the external autonome overthrow the noble through a sequence of ordinary actions?
- Check: does a naive strategy (spamming one action) fail appropriately?
- Check: does the noble's AI shore up loyalty when it erodes, creating genuine resistance?

### Known Imbalances to Address

1. **Trade goods domination** — buy_food/sell_food are currently ~30% of all actions. Need higher opportunity cost or longer travel to market.

2. **Eat frequency variance** — Rangers eat 6x/day while merchants eat 1.5x/day. Hunger decay should be more uniform, with activity level adding to it rather than base rate varying 2x.

3. **Work underrepresentation** — mine_ore is 0.3% of actions despite 4 miners existing. Work actions may score too low relative to trade. Investigate personality multiplier interaction.

4. **Rest overshooting** — Average 1.3 sleeps/day is slightly high. Consider reducing base rest decay by 10-15% or reducing work rest penalties further.

5. **No social feedback** — chat_with_neighbor and drink_at_tavern don't modify relationships yet. Social actions are scored but have no lasting effect beyond property restoration.

---

## Part 9: Implementation Priority

### Phase 1: Coastal City World (Data Only) ✅
*No engine changes. Extend the existing valley world into the full coastal city.*

- [x] Migrate location IDs to structured dot-notation (`city.docks.pier`, `hinterland.farmland.fields`, etc.)
- [x] Map existing settlements: Millhaven → `hinterland.farmland.*`, Ironforge → `hinterland.quarry.*`, Thornwatch → `hinterland.woodlands.*`
- [x] Design city district locations (~19 new locations above the hinterland line)
- [x] Create NPC profiles (76 characters with homes, occupations, personalities)
- [x] Write occupation-specific actions (fishing, baking, carpentry, smuggling, etc.)
- [x] Define organizations (council, guilds, watch, underworld, temple)
- [x] Set up authority graph and relationships
- [x] Balance economy with supply chain actions
- [x] Run 2000+ tick simulations, analyze and tune — MOSTLY BALANCED (0 failures, 9 warnings)

### Phase 2: Power Structure + Political Actions ✅
*Add the noble entity and actions that enable the overthrow scenario. Mostly data, minimal engine changes.*

- [x] Create noble/lord autonome profile — `noble_lord_ashworth` with gold, influence, legitimacy, defense, morale
- [x] Add authority edges: noble → city council (loyalty 0.75), noble → city watch (loyalty 0.80)
- [x] Create noble actions: hold_court, reward_loyalist, issue_decree, levy_tribute, punish_dissent
- [x] Create political actions available to all entities: persuade, bribe, intimidate, spread_rumor, build_alliance
- [x] Add legitimacy property to noble — decays at 0.0003/tick, replenished by hold_court and reward_loyalist
- [x] Loyalty threshold uses existing soft multiplier (0.5–1.0x based on loyalty) — hard threshold deferred
- [x] Tune noble AI personality — balanced action distribution (~22% each), actively uses all 5 actions
- [x] Run 2000+ tick simulations — MOSTLY BALANCED (0 failures, 14 warnings), noble holds power with slow morale decline as designed vulnerability
- [ ] Test overthrow scenario — manually script a sequence of political actions and verify the noble's authority can be eroded

### Phase 3: Home System + Social Evolution
*Small engine changes. Big behavior improvement.*

- [ ] Add `home` field to AutonomeProfile
- [ ] Resolve `"home"` target in ActionExecutor moveTo
- [ ] Home quality affects rest restoration
- [ ] Social interaction steps modify relationship properties
- [ ] Gossip modifier propagation during social actions
- [ ] Tune relationship growth/decay rates

### Phase 4: Location Inventory + Price Signals
*Medium engine changes. Economy becomes real.*

- [ ] Add property bags to locations (stock levels)
- [ ] Work actions deposit goods at work location
- [ ] Trade actions move goods between locations
- [ ] Price curve based on local supply (scarce = expensive)
- [ ] Ship arrival events (external economic shocks)
- [ ] Tax/rent as periodic property drains

### Phase 5: External Controller Interface
*Expose the simulation for external autonomes. Validate the overthrow scenario end-to-end.*

- [ ] REST API: `/api/world/state`, `/api/entity/{id}/actions`, `/api/entity/{id}/act`
- [ ] WebSocket event stream (`/ws/stream` — tick events, observable actions)
- [ ] External autonome registration and slot management (`/api/entity/register`)
- [ ] Visibility filtering — external autonomes only see events at their location
- [ ] Minimal web UI: map view, action picker, property bars, event log
- [ ] External controller protocol documentation and example client (Python/JS)
- [ ] LLM agent test — connect an LLM to the API and attempt the overthrow scenario
- [ ] Stress test — verify simulation stability with multiple external autonomes

### Phase 6: Godot Integration
*Major new code. Simulation meets visual representation.*

- [ ] SimulationBridge for tick-by-tick control
- [ ] NPC scene with navigation, animation, interaction zone
- [ ] External autonome controller as entity input source
- [ ] Action-to-animation mapping
- [ ] World synchronization (entity positions, spawn/despawn)
- [ ] Basic UI (property bars, action indicators, event feed)
- [ ] Save/load simulation state

---

## Appendix: Files That Will Need Changes Per Phase

### Phase 1 (Coastal City World) ✅
- `worlds/coastal_city/locations/*.json` — Migrated to dot-notation IDs, extended with city districts
- `worlds/coastal_city/autonomes/*.json` — 76 NPCs + 6 organizations
- `worlds/coastal_city/actions/*.json` — ~50 actions including city occupations
- `worlds/coastal_city/relationships/*.json` — Authority + social graphs
- `src/Autonome.Core/World/LocationGraph.cs` — Dot-notation hierarchy query methods

### Phase 2 (Power Structure + Political Actions) ✅
- New: `worlds/coastal_city/autonomes/noble_lord_ashworth.json` — Noble entity profile
- New: `worlds/coastal_city/actions/hold_court.json`, `reward_loyalist.json`, `issue_decree.json`, `levy_tribute.json`, `punish_dissent.json` — Noble actions
- New: `worlds/coastal_city/actions/persuade.json`, `bribe.json`, `intimidate.json`, `spread_rumor.json`, `build_alliance.json` — Political actions
- `worlds/coastal_city/relationships/authority_city.json` — Added noble → council, noble → watch edges
- 6 wildcard entities (guilds, towns) — Added noble actions to forbidden lists
- `worlds/coastal_city/autonomes/org_thieves_guild.json` — Added build_alliance to allowed list
- Loyalty threshold uses existing soft multiplier in UtilityScorer (no engine changes needed)

### Phase 3 (Home + Social)
- `src/Autonome.Core/Model/AutonomeProfile.cs` — Add Home field
- `src/Autonome.Core/Runtime/ActionExecutor.cs` — Resolve "home" target
- `src/Autonome.Core/Runtime/ActionExecutor.cs` — Social interaction relationship mods
- `src/Autonome.Data/DataLoader.cs` — Forward Home field

### Phase 4 (Economy)
- `src/Autonome.Core/World/LocationGraph.cs` — Add location property bags
- `src/Autonome.Core/Runtime/ActionExecutor.cs` — Location inventory transfers
- `src/Autonome.Core/Runtime/UtilityScorer.cs` — Price-aware scoring
- `src/Autonome.Core/Runtime/PropertyTicker.cs` — Location property decay
- New: External event system (ship arrivals, seasons)

### Phase 5 (External Controller Interface)
- New: `src/Autonome.Web/WebSocketServer.cs` — Real-time event streaming
- New: `src/Autonome.Web/EntityApi.cs` — REST endpoints for entity state/actions
- New: `src/Autonome.Web/ExternalSlot.cs` — External autonome registration and auth tokens
- New: `src/Autonome.Web/VisibilityFilter.cs` — Location-scoped event filtering
- New: `web/` — Minimal frontend (map, action picker, event log)
- `src/Autonome.Core/Simulation/SimulationRunner.cs` — External action injection between ticks

### Phase 6 (Godot)
- New: `src/Autonome.Godot/SimulationBridge.cs`
- New: `src/Autonome.Godot/NPCController.cs`
- New: `src/Autonome.Godot/ActionAnimator.cs`
- New: `src/Autonome.Godot/WorldSync.cs`
- New: Godot scene files (.tscn) for NPCs, locations, UI
- `src/Autonome.Core/Runtime/ActionExecutor.cs` — Visibility model
- `src/Autonome.Core/Simulation/SimulationRunner.cs` — Tick synchronization with game loop
