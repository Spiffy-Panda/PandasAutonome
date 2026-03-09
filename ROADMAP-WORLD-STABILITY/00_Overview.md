# Roadmap: World Stability & Self-Correction

Status: **Approved** | Created: 2026-03-09

---

## Design Goal

The world should self-correct to a stable (but fragile) equilibrium without player intervention. NPCs should exhibit varied, believable daily rhythms. The economy should function end-to-end. Social connections should matter. Information should spread meaningfully. Periodic events (night, ships) should visibly reshape behavior.

---

## Population Growth Expectation

The current population is ~95 embodied NPCs and ~21 orgs. **The population is expected to roughly double across these phases** as new systems require new roles and actors:

- **Phase 2** adds a handful of new NPCs (dedicated teamster haulers, a second baker).
- **Phase 3** may add tax collectors or rent agents if existing NPCs can't absorb the load.
- **Phase 4 is the big population expansion.** Family pairs require spouses, and many of those spouses should be trade assistants to their partner's profession (see Phase 4 notes). This alone adds 30-40+ new NPCs. Additional children (non-acting dependents) further increase the headcount.
- **Phase 6** tuning may adjust NPC counts up or down to hit production/consumption targets.

Target by Phase 6 completion: **~150-190 embodied NPCs**.

---

## Decisions Made

| Problem | Approach |
|---|---|
| Action monotony (50+ NPCs in 2-3 loops) | Weighted Random Top-K selection scaled by impulsiveness |
| Broken food pipeline | Fix distribution (sell_at_market, fix routing, reduce tavern decay) |
| Social system decorative | Full overhaul — all at once (family, gossip content, trust networks) |
| Dead features (PropertyMod, dead locations, etc.) | Clean sweep — remove inert code, repurpose locations |
| eat_scraps undermines economy | Nerf (lower restoration, mood penalty, location restriction) |
| Gold hoarding, no sinks | Rent + taxes system |
| Weak night cycle | Stronger night shift (0.4x work, rest/tavern bonuses) |

---

## Phase Dependency Graph

```
Phase 1 (Behavior Fixes)  [ready-to-start]
  ├── 1.1 Weighted Random Top-K ──┐
  ├── 1.2 Stronger Night Shift    ├──→ Phase 2 (Food Pipeline)  [blocked by Phase 1]
  ├── 1.3 Nerf eat_scraps         │       ├── 2.1 Farm-to-City
  └── 1.4 Clean Sweep             │       ├── 2.2 Fix Bake Bread
                                   │       ├── 2.3 Tavern Decay
                                   │       ├── 2.4 Teamster Urgency
                                   │       └── 2.5 Mill Routing
                                   │              │
                                   │              ▼
                                   │     Phase 3 (Gold Circulation)  [blocked by Phase 2]
                                   │       ├── 3.1 Rent System
                                   │       ├── 3.2 Tax Collection
                                   │       └── 3.3 Org Revenue
                                   │
                                   ▼
                              Phase 4 (Social Overhaul)  [blocked by Phase 1]
                                ├── 4.1 Relationship Strengthening
                                ├── 4.2 Weighted Friend Preference
                                ├── 4.3 Gossip Content Types
                                ├── 4.4 Family Pairs + Trade Assistants
                                ├── 4.5 Eat-Together
                                ├── 4.6 Social Memory
                                └── 4.7 Trust Networks

                              Phase 5 (UI Polish)  [blocked by Phase 2]
                                ├── 5.1 Ship Indicator
                                ├── 5.2 Gold Display
                                ├── 5.3 Food Flow Viz
                                ├── 5.4 Social Graph Viz
                                └── 5.5 Daily Rhythm Viz

                              Phase 6 (Equilibrium Tuning)  [blocked by Phase 1-5]
                                ├── 6.1 Production/Consumption
                                ├── 6.2 Decay Tension
                                ├── 6.3 Org Morale Loops
                                ├── 6.4 Validate Top-K
                                └── 6.5 Gold Equilibrium
```

**Parallelism**: Phase 4 (Social) can begin as soon as Phase 1 is complete — it doesn't depend on the food pipeline. Phase 5 (UI) can start after Phase 2. Phase 6 must wait for everything else.

---

## Validation Checkpoints

After each phase, run a 2000-tick analysis and verify:

| Checkpoint | Pass Criteria |
|---|---|
| Phase 1 done | Max consecutive same-action < 8. Dominant action % < 30%. Night rest_at_home > 60% of night actions. |
| Phase 2 done | Tavern food_supply never hits 0 for > 50 ticks. Market food_supply stays above 20. 0 NPCs eating scraps when gold > 5. |
| Phase 3 done | Median NPC gold between 15-50 at tick 2000. Org gold doesn't deplete to 0. Gold Gini coefficient < 0.6. |
| Phase 4 done | Average unique social partners per NPC > 4. Gossip modifiers propagate to 3+ NPCs per rumor. Family pairs have affinity > 0.7. Population ~150+. |
| Phase 5 done | Visual inspection: ship arrivals visible, gold shown as number, food flow lines animate, social graph renders. |
| Phase 6 done | Overall verdict: BALANCED or MOSTLY BALANCED. 0 failures. Warnings < 8. |

---

## File Index

| File | Status | Contents |
|---|---|---|
| `Phase1_ready-to-start.md` | Ready | Core Behavior Fixes (Top-K, Night, eat_scraps nerf, Clean Sweep) |
| `Phase2_blocked.md` | Blocked by Phase 1 | Food Pipeline Repair |
| `Phase3_blocked.md` | Blocked by Phase 2 | Gold Circulation (Rent, Taxes, Org Revenue) |
| `Phase4_blocked.md` | Blocked by Phase 1 | Full Social Overhaul (Families, Gossip, Trust) |
| `Phase5_blocked.md` | Blocked by Phase 2 | UI Polish (Godot + Web Console) |
| `Phase6_blocked.md` | Blocked by All | Equilibrium Tuning |
