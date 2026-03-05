# Autonome Simulator: Long-Term Design Roadmap

## Where We Are

The engine is architecturally mature. We have a unified autonome system that treats individuals and organizations identically, a property/curve-based utility scorer, modifier-driven memory and directive systems, authority graphs, location routing, and a web analysis console. The "coastal_city" world is currently a reskinned valley — 3 settlements, 52 NPCs, 38 actions — running at 15-minute ticks with day/night scoring, dynamic memories, and trade loops.

What we don't have yet is the *coastal city itself*. The current world is a landlocked network of farming villages. The simulation produces plausible behavior but the world doesn't tell a coherent story. The game integration layer doesn't exist.

---

## Part 1: The Coastal City — World Design

### Vision

A single walled port city with surrounding hinterland. Not three disconnected towns — one city with districts, social strata, and economic pressure from the sea. The player lives here. NPCs have routines the player can observe, disrupt, and exploit.

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
- Player can observe flow by standing in the market square
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

## Part 5: Player Interaction Model

### Design Philosophy

The player exists in the same world as NPCs. They don't have a god-view — they experience the city at street level. Their power comes from *understanding the systems* and *acting within them*, not from UI menus.

### Interaction Categories

**Passive observation:**
- Watch NPCs go about routines
- Notice patterns (who goes where, when)
- Overhear gossip at taverns
- Read market prices on notice boards

**Economic interference:**
- Buy/sell goods (affects local supply/demand)
- Corner a market (buy all the ore, resell at markup)
- Invest in a business (inject gold, get profit share)
- Bribe officials (modify tax rates, get permits)

**Social interference:**
- Talk to NPCs (builds relationship)
- Spread rumors (inject false gossip modifiers)
- Do favors (creates obligation modifiers)
- Threaten (fear-based compliance, temporary)

**Structural interference:**
- Join a guild (access guild actions and information)
- Start a business (become an employer, NPCs work for you)
- Run for office (political power)
- Commission buildings (new locations in the graph)

### Engine Requirements for Player Integration

1. **Player as Autonome** — The player is an entity in the simulation with properties, relationships, and modifiers. But their actions come from input, not utility scoring.

2. **Action injection** — When the player performs an action, it goes through the same ActionExecutor pipeline. Same steps, same property modifications, same event recording.

3. **Pause/resume** — Godot game loop and simulation tick loop need synchronization. Player actions happen between ticks, or the simulation pauses while the player acts.

4. **Visibility model** — NPCs should only react to what they can "see." Player actions in private go unnoticed. Actions in the market square are witnessed by everyone present.

5. **Consequence propagation** — Player buys all the bread → baker has gold → baker buys more flour → miller is busy → miller can't socialize → miller's mood drops. The player sees this over days, not instantly.

---

## Part 6: Player Prototype — Web Interface

### Purpose

Before building the full Godot integration, validate the player-as-autonome concept with a lightweight web prototype. This keeps the feedback loop fast, avoids coupling to Godot early, and — critically — exposes the simulation backend so that external "players" (bots, scripts, other AI agents) can connect and act in the world alongside human players.

### Architecture

```
Browser (React/vanilla)          Autonome Engine (C#)
┌─────────────────────┐          ┌──────────────────────┐
│ World Map View       │◄──WS──►│ WebSocketServer       │
│ NPC Status Panel     │         │   ↕                   │
│ Action Menu          │         │ SimulationRunner      │
│ Property Inspector   │         │ PlayerSlot[]          │
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

**Core endpoints (exposed for external players):**

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/world/state` | GET | Current world snapshot — all locations, NPC positions, property summaries |
| `/api/world/tick` | GET | Current tick number, time-of-day, recent events |
| `/api/player/{id}/state` | GET | Player autonome's properties, inventory, location, relationships |
| `/api/player/{id}/actions` | GET | Available actions at the player's current location (scored but unranked) |
| `/api/player/{id}/act` | POST | Submit an action to execute — goes through the same ActionExecutor pipeline as NPCs |
| `/api/player/register` | POST | Register an external player slot — returns a player ID and auth token |
| `/ws/stream` | WS | Real-time event stream — tick advances, NPC actions, property changes, location arrivals/departures |

**Design constraints:**
- Players submit actions between ticks, not during tick resolution
- One action per player per tick (same as NPCs)
- External players see only what a player at their location could observe (visibility model applies)
- Auth tokens are simple bearer tokens — this is a prototype, not production auth

### Web UI (Human Player)

Minimal interface for a single human player to interact with the simulation:

- **Map view** — Graph visualization of locations, player position highlighted, NPC counts per location
- **Location detail** — NPCs present, available actions, local gossip/events
- **Action picker** — Select from scored actions, see expected property changes before confirming
- **Property panel** — Player's hunger, rest, gold, mood, social as bars
- **Event log** — Scrolling feed of observable events (NPC arrivals, trade, gossip)
- **Tick controls** — Advance manually, auto-advance at configurable speed, pause

### External Player Protocol

External agents interact through the REST API. A typical bot loop:

```
1. GET /api/player/{id}/state     → read own properties
2. GET /api/player/{id}/actions   → see what's available
3. POST /api/player/{id}/act      → submit chosen action
4. WS /stream (or poll /tick)     → wait for tick to resolve
5. Repeat
```

This allows:
- Python/JS bots competing in the same economy as NPCs
- AI agents (LLM-driven) making decisions based on world state
- Multiple external players simultaneously
- Automated testing and balancing — run 100 bot-players and measure economic impact

### What This Validates Before Godot

- Player-as-autonome architecture works end-to-end
- Action injection pipeline handles external input correctly
- Visibility model filters information properly
- Consequence propagation is observable over time
- Multiple simultaneous players don't break the simulation
- Economy survives player interference (or breaks in interesting ways)

---

## Part 7: Godot Integration

### Architecture

```
Godot (GDScript/C#)          Autonome Engine (C#)
┌─────────────────┐          ┌──────────────────┐
│ 3D World        │◄────────►│ WorldState        │
│ NPC Scenes      │          │ SimulationRunner  │
│ Player Input    │          │ UtilityScorer     │
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
- Between ticks: animations play out, player moves freely
- On tick: simulation advances, NPCs make decisions, world updates

**NPC scene requirements:**
- Navigation mesh pathfinding (replaces teleport)
- Animation state machine (idle, walk, work, eat, sleep)
- Interaction zone (player can talk when nearby)
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
5. **PlayerController** — Translates player input into ActionDefinition-compatible actions

---

## Part 8: Tuning and Balancing Guide

### Parameters That Need Tuning

**Time Scale (currently 15 min/tick, 96 ticks/day):**
- Works well for headless simulation
- For real-time game: need configurable speed (1 tick/second at normal, faster when sleeping)
- Day length in real minutes affects how much the player can observe

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

**Step 5: Player injection**
- Add player entity, perform economic actions
- Verify: consequences propagate, NPCs react plausibly
- Check: can the player break the economy? Is that fun or catastrophic?

### Known Imbalances to Address

1. **Trade goods domination** — buy_food/sell_food are currently ~30% of all actions. Need higher opportunity cost or longer travel to market.

2. **Eat frequency variance** — Rangers eat 6x/day while merchants eat 1.5x/day. Hunger decay should be more uniform, with activity level adding to it rather than base rate varying 2x.

3. **Work underrepresentation** — mine_ore is 0.3% of actions despite 4 miners existing. Work actions may score too low relative to trade. Investigate personality multiplier interaction.

4. **Rest overshooting** — Average 1.3 sleeps/day is slightly high. Consider reducing base rest decay by 10-15% or reducing work rest penalties further.

5. **No social feedback** — chat_with_neighbor and drink_at_tavern don't modify relationships yet. Social actions are scored but have no lasting effect beyond property restoration.

---

## Part 9: Implementation Priority

### Phase 1: Coastal City World (Data Only)
*No engine changes. Extend the existing valley world into the full coastal city.*

- [ ] Migrate location IDs to structured dot-notation (`city.docks.pier`, `hinterland.farmland.fields`, etc.)
- [ ] Map existing settlements: Millhaven → `hinterland.farmland.*`, Ironforge → `hinterland.quarry.*`, Thornwatch → `hinterland.woodlands.*`
- [ ] Design city district locations (~25 new locations above the hinterland line)
- [ ] Create NPC profiles (60-80 characters with homes, occupations, personalities)
- [ ] Write occupation-specific actions (fishing, baking, carpentry, smuggling)
- [ ] Define organizations (council, guilds, watch, underworld)
- [ ] Set up authority graph and relationships
- [ ] Balance economy with supply chain actions
- [ ] Run 2000+ tick simulations, analyze and tune

### Phase 2: Home System + Social Evolution
*Small engine changes. Big behavior improvement.*

- [ ] Add `home` field to AutonomeProfile
- [ ] Resolve `"home"` target in ActionExecutor moveTo
- [ ] Home quality affects rest restoration
- [ ] Social interaction steps modify relationship properties
- [ ] Gossip modifier propagation during social actions
- [ ] Tune relationship growth/decay rates

### Phase 3: Location Inventory + Price Signals
*Medium engine changes. Economy becomes real.*

- [ ] Add property bags to locations (stock levels)
- [ ] Work actions deposit goods at work location
- [ ] Trade actions move goods between locations
- [ ] Price curve based on local supply (scarce = expensive)
- [ ] Ship arrival events (external economic shocks)
- [ ] Tax/rent as periodic property drains

### Phase 4: Player Prototype — Web Interface
*Validate player-as-autonome before Godot. Expose backend for external players.*

- [ ] REST API: `/api/world/state`, `/api/player/{id}/actions`, `/api/player/{id}/act`
- [ ] WebSocket event stream (`/ws/stream` — tick events, observable actions)
- [ ] Player registration and slot management (`/api/player/register`)
- [ ] Visibility filtering — players only see events at their location
- [ ] Minimal web UI: map view, action picker, property bars, event log
- [ ] External bot protocol documentation and example client (Python/JS)
- [ ] Multi-player stress test — verify economy survives multiple external agents

### Phase 5: Godot Integration
*Major new code. Simulation meets game.*

- [ ] SimulationBridge for tick-by-tick control
- [ ] NPC scene with navigation, animation, interaction zone
- [ ] PlayerController as special Autonome
- [ ] Action-to-animation mapping
- [ ] World synchronization (entity positions, spawn/despawn)
- [ ] Basic UI (property bars, action indicators, dialogue)

### Phase 6: Player Agency
*Game mechanics built on simulation foundation.*

- [ ] Player action menu (context-sensitive, location-aware)
- [ ] Trading interface
- [ ] Conversation system (relationship building, gossip exchange)
- [ ] Reputation and consequence visibility
- [ ] Quest-like emergent objectives (guild tasks, council requests)
- [ ] Save/load simulation state

---

## Appendix: Files That Will Need Changes Per Phase

### Phase 1 (Data)
- `worlds/coastal_city/locations/*.json` — Migrate to dot-notation IDs, extend with city districts
- `worlds/coastal_city/autonomes/*.json` — Complete rewrite
- `worlds/coastal_city/actions/*.json` — Many new actions
- `worlds/coastal_city/relationships/*.json` — New authority + social graphs
- `worlds/coastal_city/property_levels/*.json` — Possible new property sets
- `src/Autonome.Core/World/LocationGraph.cs` — Support dot-notation IDs, hierarchy-aware routing

### Phase 2 (Home + Social)
- `src/Autonome.Core/Model/AutonomeProfile.cs` — Add Home field
- `src/Autonome.Core/Runtime/ActionExecutor.cs` — Resolve "home" target
- `src/Autonome.Core/Runtime/ActionExecutor.cs` — Social interaction relationship mods
- `src/Autonome.Data/DataLoader.cs` — Forward Home field

### Phase 3 (Economy)
- `src/Autonome.Core/World/LocationGraph.cs` — Add location property bags
- `src/Autonome.Core/Runtime/ActionExecutor.cs` — Location inventory transfers
- `src/Autonome.Core/Runtime/UtilityScorer.cs` — Price-aware scoring
- `src/Autonome.Core/Runtime/PropertyTicker.cs` — Location property decay
- New: External event system (ship arrivals, seasons)

### Phase 4 (Web Prototype)
- New: `src/Autonome.Web/WebSocketServer.cs` — Real-time event streaming
- New: `src/Autonome.Web/PlayerApi.cs` — REST endpoints for player state/actions
- New: `src/Autonome.Web/PlayerSlot.cs` — Player registration and auth tokens
- New: `src/Autonome.Web/VisibilityFilter.cs` — Location-scoped event filtering
- New: `web/` — Minimal frontend (map, action picker, event log)
- `src/Autonome.Core/Simulation/SimulationRunner.cs` — Player action injection between ticks

### Phase 5 (Godot)
- New: `src/Autonome.Godot/SimulationBridge.cs`
- New: `src/Autonome.Godot/NPCController.cs`
- New: `src/Autonome.Godot/ActionAnimator.cs`
- New: `src/Autonome.Godot/WorldSync.cs`
- New: Godot scene files (.tscn) for NPCs, locations, UI

### Phase 6 (Player Agency)
- New: `src/Autonome.Godot/PlayerController.cs`
- `src/Autonome.Core/Runtime/ActionExecutor.cs` — Visibility model
- `src/Autonome.Core/Simulation/SimulationRunner.cs` — Pause/inject/resume
- New: Dialogue system, quest system, save/load
