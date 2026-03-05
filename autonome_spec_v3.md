# Autonome System — Hierarchical Utility AI Spec (v3)

## 1. Design Goals

- Support hundreds to low-thousands of concurrent **Autonomes** — any entity that has needs, evaluates actions, and selects the highest utility
- Autonomes form a **directed acyclic authority graph**, not a fixed tier system. A guild can contain sub-guilds. A kingdom contains duchies that contain towns. The graph is data.
- The only hard behavioral split is **embodied vs. disembodied**: embodied Autonomes can physically act; disembodied Autonomes realize goals by issuing **Directives** that reshape the utility landscape of Autonomes they have authority over
- Maximize data-driven content surface so an LLM can mass-produce Autonome profiles, actions, and world flavor without touching engine code
- Keep runtime evaluation cheap: no planning searches, no deep tree traversals

---

## 2. Foundational Abstractions

Before describing the hierarchy, we define the small number of core abstractions that everything in the system is built from. A key design principle: **if two things have the same shape, they are the same thing.**

### 2.1 StatefulEntity

The base abstraction. Anything with named float properties that change over time.

```
StatefulEntity {
    id:         string
    properties: Dictionary<string, Property>
}

Property {
    id:          string
    value:       float         // current value
    min:         float         // floor (default 0.0)
    max:         float         // ceiling (default 1.0)
    decayRate:   float         // per game-minute loss (can be 0)
    critical:    float?        // threshold for passive effects (optional)
    passiveEffects: Effect[]   // applied when value < critical
}
```

**Everything is a StatefulEntity.** An Individual's hunger is a Property. A Town's gold reserve is a Property (with min=0, max=∞). A plant's water level is a Property. A relationship's affinity is a Property. By unifying these, the NeedTicker becomes trivially general — it just iterates Properties and applies decay.

The distinction between "needs" and "resources" is erased. They're both Properties. What makes a "need" special is just that it has a nonzero `decayRate` and a `critical` threshold. Gold doesn't decay (usually), so its decayRate is 0. But you *could* give gold a decay rate to model operating costs, and the engine wouldn't care.

### 2.2 ResponseCurve

A single curve type replaces the previous zoo of sigmoid/exponential/linear/flat.

**A ResponseCurve is a piecewise cubic Hermite spline defined by keyframes in [0,1] → [0,1] space.** Inspired by Unity's `AnimationCurve` and Godot's `Curve` — tangent slopes instead of Bezier handle vectors.

```
ResponseCurve {
    keys: Keyframe[]    // ordered by time, minimum 2 (start and end)
}

Keyframe {
    time:        float   // input position (property value, 0 = empty, 1 = full)
    value:       float   // output (motivation, 0 = no desire, 1 = max desire)
    inTangent:   float   // slope of incoming tangent (default 0 = flat)
    outTangent:  float   // slope of outgoing tangent (default 0 = flat)
}
```

With zero tangents, segments interpolate smoothly between values (ease in/out). With matching tangents at both ends, segments are linear. This subsumes every previous curve type:

- **"Flat"** = two keyframes: `(0, 0.5), (1, 0.5)` — constant output
- **"Linear"** = two keyframes with tangent -1.0: `(0, 1.0, out:-1), (1, 0.0, in:-1)`
- **"Steep near zero"** = keyframe with steep outTangent at low time values
- **"S-curve"** = three keyframes with opposing tangents through the midpoint
- **Any arbitrary shape** = add more keyframes with custom tangent slopes

Evaluation uses cubic Hermite interpolation: find the segment containing the input, compute using Hermite basis functions with tangent slopes scaled by segment width.

**Named presets** are still useful for generation — the LLM references a preset name, which resolves to a keyframe set:

```jsonc
{
  "presets": {
    "linear": {
      "description": "Straight line — more motivation when property is low.",
      "keys": [
        { "time": 0.0, "value": 1.0, "outTangent": -1.0 },
        { "time": 1.0, "value": 0.0, "inTangent": -1.0 }
      ]
    },
    "constant": {
      "description": "Flat bonus regardless of property state.",
      "keys": [
        { "time": 0.0, "value": 0.5 },
        { "time": 1.0, "value": 0.5 }
      ]
    },
    "desperate": {
      "description": "Extreme urgency when empty, negligible when full.",
      "keys": [
        { "time": 0.0, "value": 1.0, "outTangent": 0.0 },
        { "time": 0.2, "value": 0.9, "inTangent": -0.5, "outTangent": -4.0 },
        { "time": 0.5, "value": 0.05, "inTangent": -0.3, "outTangent": 0.0 },
        { "time": 1.0, "value": 0.0, "inTangent": 0.0 }
      ]
    },
    "threshold_low": {
      "description": "Sharp activation below ~0.3, near-zero above.",
      "keys": [
        { "time": 0.0, "value": 1.0, "outTangent": 0.0 },
        { "time": 0.25, "value": 0.9, "inTangent": 0.0, "outTangent": -6.0 },
        { "time": 0.4, "value": 0.0, "inTangent": 0.0 },
        { "time": 1.0, "value": 0.0 }
      ]
    },
    "inverse_linear": {
      "description": "More motivation when property is high.",
      "keys": [
        { "time": 0.0, "value": 0.0, "outTangent": 1.0 },
        { "time": 1.0, "value": 1.0, "inTangent": 1.0 }
      ]
    }
  }
}
```

Actions reference curves either by preset name or inline keyframes. Generated content uses presets; hand-tuned content can use custom keyframes.

### 2.3 Modifier

The third core abstraction. **A Modifier is anything that alters an Autonome's utility scoring.** Previously this was three separate systems (Memories, Directives, Passive Effects). Now it's one.

```
Modifier {
    id:          string
    source:      string         // who/what created this modifier
    type:        string         // freeform tag: "memory", "directive", "passive", "trait", ...
    target:      string         // Autonome ID this applies to

    // What it modifies (all optional — use what applies)
    actionBonus: Dictionary<string, float>?   // action ID → flat bonus
    propertyMod: Dictionary<string, float>?   // property ID → additive modifier to value
    affinityMod: Dictionary<string, float>?   // personality axis → temporary shift

    // Lifecycle
    duration:    float?         // game-hours until expiry (null = permanent)
    decayRate:   float?         // modifier strength decays over time (for memories)
    intensity:   float          // current strength multiplier (1.0 = full effect)

    // Reward on action completion (for directive-type modifiers)
    completionReward: Reward?

    // Metadata
    flavor:      string?        // descriptive text for history/dialogue
}

Reward {
    propertyChanges: Dictionary<string, float>  // property ID → amount
    relationshipMod: RelationshipChange?
}
```

This single structure replaces:
- **Memories**: `type: "memory"`, has `decayRate`, no `completionReward`. "Marta had a good time at the tavern" → `actionBonus: {"eat_at_tavern": 0.3}`, `decayRate: 0.01`.
- **Directives**: `type: "directive"`, has `duration`, `completionReward`, may have `maxClaims`. "Town wants you to farm" → `actionBonus: {"plow_field": 0.35}`, `completionReward: {gold: 8}`.
- **Passive Effects**: `type: "passive"`, `duration: null` (recalculated each aggregation tick). "Town morale is low" → `propertyMod: {"mood": -0.2}`.
- **Trait Effects**: `type: "trait"`, `duration: null`, permanent. A personality quirk that always modifies scoring.

The scorer just iterates all active Modifiers for an Autonome and sums their contributions. One loop, one system.

### 2.4 Relationship (Unified)

Previously there were separate structures for Individual↔Individual relationships, Individual→Organization affiliations, and Town↔Town diplomacy. They're all the same shape:

```
Relationship {
    source:      string         // Autonome ID
    target:      string         // Autonome ID
    properties:  Dictionary<string, Property>  // affinity, loyalty, familiarity, trust, etc.
    tags:        string[]       // "friend", "member", "trade_partner", "authority", etc.
}
```

Relationships are themselves StatefulEntities — their properties can decay, have thresholds, and trigger effects. A friendship's `familiarity` decays if the two Autonomes don't interact. A guild membership's `loyalty` is a Property that can cross a critical threshold and trigger a "consider_leaving" modifier.

The `"authority"` tag is what defines the hierarchy. If a Relationship from A to B has the `"authority"` tag, A can issue Directives to B. This replaces the tier/enum system entirely.

---

## 3. The Authority Graph (Replacing Tiers)

### 3.1 Structure

Autonomes form a **directed acyclic graph** (DAG) through `"authority"` relationships. There is no fixed tier count.

```
Kingdom of Aldara
  ├──authority──→ Duchy of Northmarch
  │                 ├──authority──→ Town of Millhaven
  │                 │                 ├──authority──→ Marta Blackwood (embodied)
  │                 │                 ├──authority──→ Tom Ashworth (embodied)
  │                 │                 └──authority──→ ...
  │                 └──authority──→ Town of Ironforge
  │                                   └──authority──→ ...
  └──authority──→ Duchy of Southvale
                    └──authority──→ ...

The Silver Compact (merchant guild)
  ├──authority──→ Tom Ashworth (embodied, also under Millhaven)
  ├──authority──→ Elena Vas (embodied, also under Ironforge)
  └──authority──→ Compact Ironforge Chapter (sub-guild)
                    └──authority──→ ...
```

Key properties:
- **DAG, not tree.** Tom Ashworth has two authority parents: Millhaven and The Silver Compact. Both can issue him Directives.
- **Acyclic.** Authority cannot loop. Validated at load time.
- **No fixed depth.** Kingdom → Duchy → Town → Individual is four levels. A flat guild with direct individual members is two. Both are valid.
- **Embodied flag** is on the Autonome, not derived from depth. Only embodied Autonomes have physical step types.

### 3.2 The Embodied/Disembodied Split

```jsonc
{
  "id": "town_millhaven",
  "embodied": false,
  // ...
}
```

```jsonc
{
  "id": "npc_marta_blackwood",
  "embodied": true,
  // ...
}
```

This single boolean replaces the Tier 0/1/2 enum. The engine checks it in exactly two places:

1. **Action Executor**: physical step types (`moveTo`, `animate`) only dispatch for embodied Autonomes. Directive steps (`emitDirective`) only dispatch for disembodied Autonomes. (Or more precisely: `emitDirective` can only target Autonomes that the source has authority over.)
2. **Location tracking**: only embodied Autonomes have a position in the `LocationGraph`.

Everything else — scoring, need ticking, modifier application — is identical for all Autonomes regardless of embodiment.

### 3.3 Authority-Scoped Directives

When a disembodied Autonome's action emits a directive, targeting uses the authority graph:

```jsonc
{
  "type": "emitDirective",
  "directive": {
    "target": {
      "scope": "subordinates",          // follow authority edges downward
      "depth": 1,                       // direct subordinates only (1 = children, 2 = grandchildren, null = all)
      "filter": {
        "embodied": true,               // only physical actors
        "tags": ["farmer", "laborer"],
        "propertyMin": { "diligence": 0.3 }
      }
    }
    // ...
  }
}
```

`scope: "subordinates"` walks the authority graph from the source. `depth` controls how far. A kingdom with `depth: null` reaches all the way down to individuals. A town with `depth: 1` reaches only its direct residents.

Additional scopes:
- `"subordinates"` — follow authority edges downward
- `"peers"` — Autonomes sharing at least one authority parent
- `"siblings"` — Autonomes with the exact same set of authority parents
- `"kin"` — any Autonome within N hops in the authority graph (ignoring direction)

These scopes are engine code — they define graph traversal patterns. But which scope an action uses is data.

---

## 4. Data-Driven Aggregation

Previously, aggregation formulas (how a Town's morale depends on its residents) were declared engine code. Now they're data.

### 4.1 Aggregation Expressions

Each Property on a disembodied Autonome can declare an `aggregation` that computes its value from subordinates:

```jsonc
{
  "id": "morale",
  "value": 0.5,
  "decayRate": 0.004,
  "aggregation": {
    "source": "subordinates",
    "depth": 1,
    "filter": { "embodied": true },
    "property": "mood",
    "function": "avg",
    "weight": 0.6,
    "blend": "lerp"
  },
  "critical": 0.20,
  "passiveEffects": [
    {
      "condition": "below_critical",
      "emit": {
        "type": "passive",
        "target": "subordinates",
        "propertyMod": { "mood": -0.2 }
      }
    }
  ]
}
```

This reads as: "morale is 60% derived from the average mood of direct embodied subordinates, blended with its own decayed value."

### 4.2 Aggregation Primitives

The engine implements a small set of aggregation functions. These are engine code — they're the "instruction set" of the aggregation system:

| Function  | Behavior                                            |
|-----------|-----------------------------------------------------|
| `avg`     | Mean of the queried property across matching subordinates |
| `sum`     | Sum (useful for stockpile-like properties)          |
| `min`     | Minimum value found                                 |
| `max`     | Maximum value found                                 |
| `count`   | Number of matching subordinates (normalized by some reference) |
| `ratio`   | Count matching a sub-filter / total count           |

### 4.3 Blend Modes

How the aggregated value combines with the Property's own decayed value:

| Blend     | Formula                                              |
|-----------|------------------------------------------------------|
| `replace` | `property.value = aggregatedValue`                   |
| `lerp`    | `property.value = lerp(property.value, aggregatedValue, weight)` |
| `additive`| `property.value += (aggregatedValue - 0.5) * weight` |
| `min`     | `property.value = min(property.value, aggregatedValue)` |
| `max`     | `property.value = max(property.value, aggregatedValue)` |

### 4.4 Compound Aggregations

A Property can have multiple aggregation sources that combine:

```jsonc
{
  "id": "food_supply",
  "decayRate": 0.005,
  "aggregations": [
    {
      "note": "Consumption: more residents = faster drain",
      "source": "subordinates",
      "depth": 1,
      "filter": { "embodied": true },
      "property": "_count",
      "function": "count",
      "weight": -0.002,
      "blend": "additive",
      "perTick": true
    },
    {
      "note": "Production: farmers offset drain",
      "source": "subordinates",
      "depth": 1,
      "filter": { "tags": ["farmer"] },
      "property": "diligence",
      "function": "avg",
      "weight": 0.001,
      "blend": "additive",
      "perTick": true
    }
  ]
}
```

Food supply drains proportional to population count but is offset by the average diligence of farmers. Both terms are data.

### 4.5 Passive Effect Emission

When a Property crosses its critical threshold, it can automatically emit Modifiers to subordinates. This replaces the hard-coded "passive effects" from the previous spec:

```jsonc
"passiveEffects": [
  {
    "condition": "below_critical",
    "emit": {
      "type": "passive",
      "target": "subordinates",
      "depth": 1,
      "propertyMod": { "mood": -0.2 },
      "flavor": "The town's low morale weighs on everyone."
    }
  },
  {
    "condition": "below",
    "threshold": 0.10,
    "emit": {
      "type": "passive",
      "target": "subordinates",
      "depth": 1,
      "actionBonus": { "emigrate": 0.5 },
      "flavor": "Residents are seriously considering leaving."
    }
  }
]
```

Passive modifiers are **recalculated each aggregation tick** — cleared and re-emitted based on current state. They don't accumulate.

---

## 5. Autonome Profile Schema (Unified)

There is now **one profile schema** for all Autonomes. The `embodied` flag and the authority graph determine behavior, not a tier enum.

```jsonc
{
  "id": "town_millhaven",
  "displayName": "Millhaven",
  "embodied": false,

  // --- PROPERTIES (unified needs + resources) ---
  "properties": {
    "food_supply": {
      "value": 0.6,
      "decayRate": 0.005,
      "critical": 0.20,
      "aggregations": [ ... ],
      "passiveEffects": [ ... ]
    },
    "defense": {
      "value": 0.5,
      "decayRate": 0.003,
      "critical": 0.25
    },
    "gold": {
      "value": 500,
      "min": 0,
      "max": 100000,
      "decayRate": 0.0
    },
    "lumber": {
      "value": 120,
      "min": 0,
      "max": 10000,
      "decayRate": 0.0
    }
  },

  // --- PERSONALITY ---
  "personality": {
    "caution": 0.65,
    "expansionism": 0.40,
    "militarism": 0.25,
    "generosity": 0.60,
    "diplomacy": 0.70
  },

  // --- EVALUATION SCHEDULE ---
  "evaluationInterval": 30,   // seconds between scoring cycles (real-time)

  // --- ACTION ACCESS ---
  "actionAccess": {
    "allowed": ["*"],
    "forbidden": [],
    "favorites": [],
    "favoriteMultiplier": 1.4
  },

  // --- IDENTITY (flavor, generatable) ---
  "identity": {
    "description": "A quiet farming town on the river Millrun.",
    "culturalTraits": ["self-reliant", "suspicious of outsiders"],
    "motto": "By our own hands.",
    "tags": ["settlement", "farming", "river"]
  },

  // --- SEEDED MODIFIERS (replaces memorySeed) ---
  "initialModifiers": [
    {
      "type": "memory",
      "actionBonus": { "fortify_walls": -0.2 },
      "decayRate": 0.0005,
      "intensity": 0.7,
      "flavor": "Millhaven remembers the great flood — walls didn't help."
    },
    {
      "type": "memory",
      "actionBonus": { "lower_taxes": 0.3 },
      "intensity": 0.5,
      "flavor": "Low taxes have kept the peace for years."
    }
  ],

  // --- PRESENTATION (for embodied Autonomes) ---
  "presentation": null
}
```

An Individual profile has the same shape — just different properties, an `embodied: true` flag, and a `presentation` block:

```jsonc
{
  "id": "npc_marta_blackwood",
  "displayName": "Marta Blackwood",
  "embodied": true,

  "properties": {
    "hunger":        { "value": 0.7, "decayRate": 0.018, "critical": 0.15 },
    "social":        { "value": 0.7, "decayRate": 0.004, "critical": 0.10 },
    "rest":          { "value": 0.7, "decayRate": 0.012, "critical": 0.10 },
    "entertainment": { "value": 0.7, "decayRate": 0.010, "critical": 0.05 },
    "comfort":       { "value": 0.7, "decayRate": 0.006, "critical": 0.10 },
    "purpose":       { "value": 0.7, "decayRate": 0.009, "critical": 0.08 },
    "mood":          { "value": 0.6, "decayRate": 0.002, "critical": 0.15 },
    "gold":          { "value": 25, "min": 0, "max": 100000, "decayRate": 0.0 }
  },

  "personality": {
    "sociability": 0.35,
    "adventurousness": 0.70,
    "frugality": 0.80,
    "diligence": 0.60,
    "impulsiveness": 0.45,
    "empathy": 0.55,
    "volatility": 0.30
  },

  "evaluationInterval": 1,    // every tick (frequent — she's a physical actor)

  "actionAccess": {
    "allowed": ["*"],
    "forbidden": ["pray_at_temple"],
    "favorites": ["forage_herbs", "read_book"],
    "favoriteMultiplier": 1.4
  },

  "schedulePreferences": {
    "wakeHour": 5, "sleepHour": 21,
    "workHours": { "start": 6, "end": 14 }
  },

  "identity": {
    "backstory": "Marta moved to Millhaven after her husband died. She tends an herb garden and trades remedies.",
    "quirks": ["Hums while foraging", "Avoids eye contact with strangers"],
    "greetingLines": ["Hm? Oh. What is it."],
    "tags": ["herbalist", "widow", "loner"]
  },

  "initialModifiers": [
    {
      "type": "memory",
      "actionBonus": { "forage_herbs": 0.3 },
      "decayRate": 0.0,
      "intensity": 0.8,
      "flavor": "Finds peace in herbwork"
    },
    {
      "type": "memory",
      "propertyMod": { "mood": -0.1 },
      "decayRate": 0.001,
      "intensity": 0.9,
      "flavor": "Still grieving"
    }
  ],

  "presentation": {
    "bodyType": "average_female",
    "ageRange": "middle",
    "clothingTags": ["practical", "worn", "earth_tones"],
    "voiceTag": "female_reserved_alto"
  }
}
```

### Relationships Are Separate Files

Relationships reference two Autonome IDs and carry their own Properties:

```jsonc
// relationships/millhaven_residents.json
[
  {
    "source": "town_millhaven",
    "target": "npc_marta_blackwood",
    "tags": ["authority", "resident"],
    "properties": {
      "loyalty": { "value": 0.6, "decayRate": 0.001 },
      "reputation": { "value": 0.4, "decayRate": 0.0 }
    }
  },
  {
    "source": "guild_herbalists",
    "target": "npc_marta_blackwood",
    "tags": ["authority", "member"],
    "properties": {
      "loyalty": { "value": 0.7, "decayRate": 0.001 }
    }
  },
  {
    "source": "npc_marta_blackwood",
    "target": "npc_tom_ashworth",
    "tags": ["friend", "neighbor"],
    "properties": {
      "affinity":    { "value": 0.6, "decayRate": 0.0005 },
      "familiarity": { "value": 0.8, "decayRate": 0.0002 }
    }
  }
]
```

The authority graph is just the set of all Relationships tagged `"authority"`.

---

## 6. Action Definition Schema (Updated)

```jsonc
{
  "id": "eat_at_tavern",
  "displayName": "Eat at Tavern",
  "category": "sustenance",

  // --- WHO CAN USE THIS ACTION ---
  "requirements": {
    "embodied": true,                      // only physical actors
    "nearbyTags": ["tavern"],
    "timeOfDay": { "min": 6, "max": 22 },
    "propertyMin": { "gold": 5 },
    "blockedByStates": ["sleeping"]
  },

  // --- PROPERTY RESPONSE CURVES ---
  // Keys are property IDs. Curve evaluates the property's current value
  // and outputs a motivation score. Multiplied by magnitude.
  "propertyResponses": {
    "hunger": {
      "curve": "desperate",              // preset name, or inline keyframes
      "magnitude": 0.7
    },
    "social": {
      "curve": {                         // inline custom curve
        "keys": [
          { "time": 0.0, "value": 0.8, "outTangent": -1.5 },
          { "time": 0.4, "value": 0.3, "inTangent": -1.0, "outTangent": -0.5 },
          { "time": 1.0, "value": 0.0, "inTangent": -0.2 }
        ]
      },
      "magnitude": 0.3
    },
    "comfort": {
      "curve": "constant",
      "magnitude": 0.15
    }
  },

  // --- PERSONALITY AFFINITY ---
  "personalityAffinity": {
    "sociability": 1.3,
    "frugality": 0.6
  },

  // --- EXECUTION STEPS ---
  "steps": [
    { "type": "moveTo",          "target": "nearestTagged:tavern" },
    { "type": "animate",         "animation": "sit_down", "duration": 1.0 },
    { "type": "wait",            "duration": { "min": 8, "max": 15 }, "animation": "eating" },
    { "type": "modifyProperty",  "entity": "self", "property": "hunger",  "amount": 0.6 },
    { "type": "modifyProperty",  "entity": "self", "property": "social",  "amount": 0.2 },
    { "type": "modifyProperty",  "entity": "self", "property": "gold",    "amount": -5 },
    { "type": "emitEvent",       "event": "ate_at_tavern" },
    { "type": "animate",         "animation": "stand_up", "duration": 0.5 }
  ],

  "flavor": {
    "onStart":    ["{name} settles into a chair at the tavern."],
    "onComplete": ["{name} pushes back from the table, satisfied."]
  }
}
```

Note: `modifyNeed`, `spendGold`, and `modifyObjectState` are all collapsed into `modifyProperty`. The step just names an entity and a property. Entities are resolved by reference: `"self"`, `"nearest:plant"`, `"target:npc_tom_ashworth"`, etc.

### Disembodied Actions (Directives)

```jsonc
{
  "id": "town_expand_farmland",
  "displayName": "Expand Farmland",
  "category": "food_policy",

  "requirements": {
    "embodied": false,
    "propertyBelow": { "food_supply": 0.5 },
    "propertyMin": { "gold": 50, "lumber": 20 },
    "noActiveModifier": ["dir_expand_farmland"]
  },

  "propertyResponses": {
    "food_supply":    { "curve": "desperate", "magnitude": 0.8 },
    "gold":           { "curve": "constant",  "magnitude": -0.05 }
  },

  "personalityAffinity": {
    "caution": 1.3,
    "expansionism": 1.2,
    "militarism": 0.7
  },

  "steps": [
    { "type": "modifyProperty",  "entity": "self", "property": "gold",   "amount": -50 },
    { "type": "modifyProperty",  "entity": "self", "property": "lumber", "amount": -20 },
    {
      "type": "emitDirective",
      "modifier": {
        "id": "dir_expand_farmland",
        "type": "directive",
        "target": {
          "scope": "subordinates",
          "depth": 1,
          "filter": { "embodied": true, "tags": ["farmer", "laborer"] }
        },
        "actionBonus": {
          "plow_field": 0.35,
          "clear_land": 0.30,
          "haul_materials": 0.20
        },
        "completionReward": {
          "propertyChanges": { "gold": 8, "purpose": 0.2 },
          "relationshipMod": { "target": "source", "property": "reputation", "amount": 0.02 }
        },
        "duration": 72,
        "priority": "normal",
        "maxClaims": 5,
        "flavor": "Work orders posted: clear the north field for planting."
      }
    },
    { "type": "emitEvent", "event": "town_expanding_farmland" }
  ],

  "flavor": {
    "onStart": ["The council of {name} votes to expand farmland."]
  }
}
```

`emitDirective` creates a Modifier. The Modifier goes into the global modifier registry. The scorer picks it up for matching Autonomes. It's the same system as memories and passive effects — just with different lifecycle parameters and a completionReward.

---

## 7. World Objects as Autonomes

A plant, a furnace, a well — these are just **non-embodied Autonomes with no decision-making.** They have Properties that decay. They don't evaluate actions (their `actionAccess` is empty). They exist so that other Autonomes' actions can reference them.

```jsonc
{
  "id": "obj_herb_planter_01",
  "displayName": "Herb Planter",
  "embodied": false,

  "properties": {
    "water":  { "value": 0.85, "decayRate": 0.02, "critical": 0.15,
      "passiveEffects": [
        { "condition": "below_critical",
          "emit": { "type": "passive", "target": "self", "propertyMod": { "health": -0.01 } } }
      ]
    },
    "health": { "value": 0.95, "decayRate": 0.0, "min": 0.0, "critical": 0.0 },
    "growth": { "value": 0.4,  "decayRate": -0.005, "max": 1.0 }
  },

  "personality": {},
  "evaluationInterval": null,     // null = never evaluates actions
  "actionAccess": { "allowed": [] },

  "identity": {
    "tags": ["plant", "herb_garden"]
  }
}
```

Authority relationships from an Autonome to a world object represent ownership:

```jsonc
{
  "source": "npc_marta_blackwood",
  "target": "obj_herb_planter_01",
  "tags": ["ownership"],
  "properties": {}
}
```

This means the "water plants" action can filter by `ownership` relationship tags, and the context filter `objectPropertyBelow: { "water": 0.6 }` checks properties on nearby Autonomes tagged `plant`.

Note `growth` has a **negative decay rate** — it increases over time (the plant grows). The engine doesn't care about the sign; it just applies the rate. Growth capped at `max: 1.0`.

---

## 8. Runtime Systems (Engine Code)

With the unified abstractions, the engine is smaller.

### 8.1 PropertyTicker

Iterates all Properties on all Autonomes, applies decay. Applies aggregation formulas for Properties that have them. Emits/clears passive Modifiers based on threshold conditions. One system replaces NeedTicker + AggregationEngine + passive effects.

### 8.2 UtilityScorer

```
score(action, autonome) =
    Σ over each propertyResponse in action:
        responseCurve.Evaluate(autonome.properties[propId].value) *
        response.magnitude *
        personalityAffinity.GetOrDefault(axis, 1.0) * autonome.personality[axis]
    + Σ over active modifiers targeting this autonome:
        modifier.actionBonus.GetOrDefault(action.id, 0.0) * modifier.intensity * priorityMult
    + noise(autonome.personality.GetOrDefault("impulsiveness", 0.5))
```

One modifier loop replaces memoryBias + directiveBonus + passiveEffects.

### 8.3 ActionExecutor

Walks steps. Step types:
- `moveTo` — embodied only. Update location.
- `animate` — embodied only. No-op in headless sim. Recorded for history.
- `wait` — sets `busyUntil` timestamp.
- `modifyProperty` — adjust a Property on any referenced entity.
- `emitDirective` — create a Modifier in the global registry, resolve targets via authority graph.
- `emitEvent` — push to history + trigger listeners.
- `socialInteraction` — modify Relationship Properties between two Autonomes.
- `createEntity` / `destroyEntity` — add/remove Autonomes from the world.

### 8.4 ModifierManager

Replaces DirectiveRouter + MemoryManager + passive effect system:
- Stores all active Modifiers indexed by target Autonome ID
- Ticks modifier lifecycles: decrement duration, apply intensity decay
- Removes expired modifiers
- Handles directive claim counting and completion rewards
- Provides fast lookup for scorer: `GetModifiers(autonomeId)` → `List<Modifier>`

### 8.5 AuthorityGraph

Maintains the DAG of authority relationships:
- `GetSubordinates(autonomeId, depth?, filter?)` — used by directive targeting
- `GetSuperiors(autonomeId)` — used by loyalty lookups
- `GetPeers(autonomeId)` — used by peer-scoped directives
- Validates acyclicity at load time and on any runtime mutation

---

## 9. AI Generation Strategy

### 9.1 Isolation Boundaries (Updated)

| Layer                   | AI-Generated? | Volume    | Notes                                                   |
|-------------------------|---------------|-----------|----------------------------------------------------------|
| Property definitions    | Seed only     | ~10-20    | Core design. Which properties exist per archetype.       |
| Curve presets           | Partial       | ~8-15     | Hand-tune critical ones, LLM can propose new presets     |
| Actions (embodied)      | **Yes**       | 50-500+   | Physical actions. Highest volume.                        |
| Actions (disembodied)   | **Yes**       | 20-150+   | Policy/strategy actions with directive modifiers          |
| Autonome profiles       | **Yes**       | 100-5000+ | All scales. The prime generation target.                 |
| Relationships           | **Yes**       | 200-10000+| Authority graph + social graph + affiliations            |
| Aggregation expressions | Partial       | Per-property | LLM picks from primitive vocabulary, hand-validate     |
| Modifier templates      | **Yes**       | 30-200+   | Reusable modifier skeletons for directives               |
| Flavor text             | **Yes**       | Unbounded | Barks, descriptions, mottos, announcements               |

### 9.2 Generation Order

```
 1. Property definitions (per archetype)      ← hand-authored
 2. Curve presets                             ← hand-authored
 3. Embodied actions                          ← AI-generated
 4. Embodied Autonome profiles                ← AI-generated
 5. Relationships (peer, social)              ← AI-generated
 6. Modifier templates                        ← AI-generated
 7. Disembodied actions                       ← AI-generated, references modifier templates
 8. Disembodied Autonome profiles             ← AI-generated
 9. Authority relationships                   ← AI-generated, defines the hierarchy
10. Aggregation expressions                   ← AI-generated, validated by simulation
11. Cross-graph relationships + final pass    ← AI-generated
```

---

## 10. Validation Metrics (Updated)

### Per-Autonome (regardless of embodiment)
- **Action diversity**: ≥5 different actions per 100 game-days (for evaluating Autonomes)
- **Property balance**: No property >60% of time above 0.8 or below 0.2
- **Uniqueness**: <85% action-sequence overlap between any two comparable Autonomes

### Authority Graph Health
- **Directive responsiveness**: >50% of directives get ≥1 claim within 24 game-hours
- **Graph utilization**: >80% of authority edges should carry at least 1 directive per 100 game-days
- **No orphans**: Every embodied Autonome should be reachable from at least one disembodied Autonome

### Feedback Loop Stability
- **Oscillation check**: Properties with aggregations should show damped oscillation, not divergence
- **Cascade detection**: ≥1 multi-level cascade per 200 game-days
- **Modifier starvation**: No Autonome should have >3 active directive-type modifiers with 0 claims

### Aggregate
- **Deadlock detection**: No evaluating Autonome goes >48 game-hours without selecting an action
- **DAG validity**: Authority graph remains acyclic at all times
