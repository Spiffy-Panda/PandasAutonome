# Phase 5: UI Polish & Visibility

Status: **ready-to-start** (partially — social graph viz still needs Phase 4)
Unblocked by: Phase 2 (Food Pipeline) — completed 2026-03-09

**Unblocks**: Phase 6 (Equilibrium Tuning — visual validation)

---

> Make all the new systems observable in both Godot and Web console.

## Population Impact

None. This phase is purely visual/UI.

---

## 5.1 — Ship Arrival Indicator (Godot)

- When `ProcessEvents` fires a ship arrival event, emit a signal that the Godot bridge can catch
- Animate a ship sprite arriving at harbor node
- Flash the harbor location node briefly
- Show cargo manifest in the event log: "Merchant vessel arrived: +40 food, +8 metal"

---

## 5.2 — Gold Display Fix (Godot)

- Change LocationNode gold display from fill bar to text label (e.g., "42g")
- Gold is unbounded — bar representation with min/max is misleading
- NPC gold in entity inspector should also show as number, not bar

---

## 5.3 — Food Flow Visualization (Godot)

- Color-code connection lines between locations based on food movement direction
- When a delivery action completes, briefly animate a particle or line pulse from source to destination
- Teamster routes glow when active hauling is happening

---

## 5.4 — Gossip & Social Visualization (Web Console)

**New web console view**: `social_graph.js`
- Show NPC nodes connected by relationship edges
- Edge thickness = affinity strength
- Edge color = trust level (green=high, red=low)
- Animate gossip propagation: when a modifier spreads, flash the edge between source and target
- Show family pairs as connected clusters

**Modifier inspector enhancement**:
- In entity detail view, show active gossip modifiers with their type, source, and remaining duration
- Show gossip content type (food_location, noble_weakness, etc.) as colored badges

---

## 5.5 — Daily Rhythm Visualization (Web Console)

- Add a 24-hour timeline showing aggregate action category distribution per hour
- Bars showing % work / % social / % rest / % sustenance per hour across all NPCs
- Should clearly show the day -> evening -> night rhythm from Phase 1.2

---

## Files Affected

**Godot**:
- Ship arrival animation script + sprite (5.1)
- LocationNode gold label refactor (5.2)
- Connection line shader / particle effect (5.3)

**Web Console**:
- `web/social_graph.js` — NEW (5.4)
- `web/daily_rhythm.js` — NEW (5.5)
- `web/autonomes.js` — modifier inspector enhancement (5.4)
