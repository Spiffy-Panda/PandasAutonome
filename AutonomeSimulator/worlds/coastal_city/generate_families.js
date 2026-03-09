// Generates spouse NPC profiles and families.json for Phase 4.
// Run: node generate_families.js
// Outputs: autonomes/spouse_*.json files + relationships/families.json

const fs = require('fs');
const path = require('path');

const autonomesDir = path.join(__dirname, 'autonomes');
const relsDir = path.join(__dirname, 'relationships');

// ── Pairing definitions ──
// [existing_id, new_spouse_id, new_display, home, trade_tags, favorites, backstory, personality_overrides]
const pairs = [
  // ── Hinterland Farmland ──
  { existing: "npc_dalla_furrows", spouse: "npc_ren_furrows", display: "Ren Furrows", home: "hinterland.farmland.homes", quality: 0.60,
    tags: ["farmhand", "laborer"], favorites: ["sell_at_market", "harvest"], backstory: "Ren handles sales at market while Dalla works the fields.",
    personality: { sociability: 0.65, adventurousness: 0.30, frugality: 0.55, diligence: 0.70, impulsiveness: 0.35, empathy: 0.55, volatility: 0.25 }},
  { existing: "npc_colm_harrow", spouse: "npc_sybil_harrow", display: "Sybil Harrow", home: "hinterland.farmland.homes", quality: 0.60,
    tags: ["farmhand", "laborer"], favorites: ["water_plants", "harvest"], backstory: "Sybil tends the garden plots and mends fences.",
    personality: { sociability: 0.50, adventurousness: 0.25, frugality: 0.65, diligence: 0.80, impulsiveness: 0.20, empathy: 0.60, volatility: 0.20 }},
  { existing: "npc_ewan_ploughman", spouse: "npc_nora_ploughman", display: "Nora Ploughman", home: "hinterland.farmland.homes", quality: 0.60,
    tags: ["farmhand", "laborer"], favorites: ["harvest", "sell_at_market"], backstory: "Nora is quick with a sickle and quicker with a joke.",
    personality: { sociability: 0.70, adventurousness: 0.35, frugality: 0.50, diligence: 0.65, impulsiveness: 0.45, empathy: 0.65, volatility: 0.30 }},
  { existing: "npc_bessa_seedworth", spouse: "npc_tarn_seedworth", display: "Tarn Seedworth", home: "hinterland.farmland.homes", quality: 0.60,
    tags: ["farmhand", "laborer"], favorites: ["haul_materials", "plow_field"], backstory: "Tarn does the heavy lifting on the Seedworth plot.",
    personality: { sociability: 0.35, adventurousness: 0.30, frugality: 0.60, diligence: 0.85, impulsiveness: 0.25, empathy: 0.40, volatility: 0.20 }},
  { existing: "npc_hilde_coinsworth", spouse: "npc_marek_coinsworth", display: "Marek Coinsworth", home: "hinterland.farmland.homes", quality: 0.60,
    tags: ["merchant", "trader"], favorites: ["trade_goods", "buy_food"], backstory: "Marek keeps the books while Hilde does the dealing.",
    personality: { sociability: 0.45, adventurousness: 0.25, frugality: 0.75, diligence: 0.70, impulsiveness: 0.20, empathy: 0.50, volatility: 0.15 }},
  { existing: "npc_jon_ditchley", spouse: "npc_blythe_ditchley", display: "Blythe Ditchley", home: "hinterland.farmland.homes", quality: 0.60,
    tags: ["laborer"], favorites: ["haul_materials", "sell_at_market"], backstory: "Blythe mends clothes and sells eggs at the market.",
    personality: { sociability: 0.55, adventurousness: 0.20, frugality: 0.70, diligence: 0.60, impulsiveness: 0.30, empathy: 0.65, volatility: 0.25 }},
  { existing: "npc_wynn_tapper", spouse: "npc_elise_tapper", display: "Elise Tapper", home: "hinterland.farmland.homes", quality: 0.60,
    tags: ["tavern_keeper", "cook"], favorites: ["bake_bread", "chat_with_neighbor"], backstory: "Elise does the cooking at the Millrun. Her stew is legendary.",
    personality: { sociability: 0.75, adventurousness: 0.20, frugality: 0.55, diligence: 0.65, impulsiveness: 0.35, empathy: 0.70, volatility: 0.25 }},
  { existing: "npc_bram_millward", spouse: "npc_thora_millward", display: "Thora Millward", home: "hinterland.farmland.homes", quality: 0.60,
    tags: ["craftsman", "laborer"], favorites: ["haul_materials", "sell_food"], backstory: "Thora loads sacks of flour and delivers to the market.",
    personality: { sociability: 0.40, adventurousness: 0.25, frugality: 0.60, diligence: 0.80, impulsiveness: 0.25, empathy: 0.45, volatility: 0.20 }},

  // ── Hinterland Quarry ──
  { existing: "npc_torben_anvil", spouse: "npc_helga_anvil", display: "Helga Anvil", home: "hinterland.quarry.homes", quality: 0.60,
    tags: ["craftsman", "smith"], favorites: ["forge_metal", "trade_goods"], backstory: "Helga works the bellows and finishes smaller pieces.",
    personality: { sociability: 0.45, adventurousness: 0.20, frugality: 0.65, diligence: 0.85, impulsiveness: 0.20, empathy: 0.50, volatility: 0.25 }},
  { existing: "npc_gundren_pickman", spouse: "npc_minna_pickman", display: "Minna Pickman", home: "hinterland.quarry.homes", quality: 0.60,
    tags: ["miner", "laborer"], favorites: ["haul_materials", "sell_food"], backstory: "Minna sorts ore by grade and keeps the tally sheets.",
    personality: { sociability: 0.55, adventurousness: 0.20, frugality: 0.60, diligence: 0.75, impulsiveness: 0.25, empathy: 0.60, volatility: 0.20 }},
  { existing: "npc_korva_sledge", spouse: "npc_bjorn_sledge", display: "Bjorn Sledge", home: "hinterland.quarry.homes", quality: 0.60,
    tags: ["miner", "laborer"], favorites: ["mine_ore", "haul_materials"], backstory: "Bjorn hauls rubble and sharpens picks for the miners.",
    personality: { sociability: 0.30, adventurousness: 0.35, frugality: 0.55, diligence: 0.80, impulsiveness: 0.30, empathy: 0.35, volatility: 0.35 }},
  { existing: "npc_halden_forge", spouse: "npc_dagny_forge", display: "Dagny Forge", home: "hinterland.quarry.homes", quality: 0.60,
    tags: ["craftsman", "smith"], favorites: ["forge_metal", "buy_ore"], backstory: "Dagny quenches blades and keeps the water troughs full.",
    personality: { sociability: 0.40, adventurousness: 0.15, frugality: 0.70, diligence: 0.80, impulsiveness: 0.20, empathy: 0.55, volatility: 0.20 }},
  { existing: "npc_ingrid_brewster", spouse: "npc_toralf_brewster", display: "Toralf Brewster", home: "hinterland.quarry.homes", quality: 0.60,
    tags: ["tavern_keeper", "cook"], favorites: ["bake_bread", "chat_with_neighbor"], backstory: "Toralf brews the ale and does the heavy lifting at the Smelter's Rest.",
    personality: { sociability: 0.60, adventurousness: 0.25, frugality: 0.50, diligence: 0.65, impulsiveness: 0.40, empathy: 0.55, volatility: 0.30 }},
  { existing: "npc_elda_ledger", spouse: "npc_gareth_ledger", display: "Gareth Ledger", home: "hinterland.quarry.homes", quality: 0.60,
    tags: ["merchant", "trader"], favorites: ["trade_goods", "buy_ore"], backstory: "Gareth handles the books and supply chain logistics.",
    personality: { sociability: 0.35, adventurousness: 0.20, frugality: 0.75, diligence: 0.80, impulsiveness: 0.15, empathy: 0.40, volatility: 0.15 }},
  { existing: "npc_bryn_shieldson", spouse: "npc_ygritte_shieldson", display: "Ygritte Shieldson", home: "hinterland.quarry.homes", quality: 0.60,
    tags: ["laborer"], favorites: ["sell_food", "chat_with_neighbor"], backstory: "Ygritte keeps house and sells preserves at the quarry market.",
    personality: { sociability: 0.65, adventurousness: 0.30, frugality: 0.55, diligence: 0.60, impulsiveness: 0.35, empathy: 0.70, volatility: 0.25 }},
  { existing: "npc_cael_tallyman", spouse: "npc_frida_tallyman", display: "Frida Tallyman", home: "hinterland.quarry.homes", quality: 0.60,
    tags: ["craftsman"], favorites: ["trade_goods", "chat_with_neighbor"], backstory: "Frida tests ore purity and maintains the assay equipment.",
    personality: { sociability: 0.50, adventurousness: 0.15, frugality: 0.65, diligence: 0.75, impulsiveness: 0.20, empathy: 0.55, volatility: 0.20 }},

  // ── Hinterland Woodlands ──
  { existing: "npc_brin_sawyer", spouse: "npc_kenna_sawyer", display: "Kenna Sawyer", home: "hinterland.woodlands.cabins", quality: 0.60,
    tags: ["woodcutter", "laborer"], favorites: ["chop_wood", "sell_at_market"], backstory: "Kenna splits kindling and sells bundled firewood.",
    personality: { sociability: 0.55, adventurousness: 0.35, frugality: 0.50, diligence: 0.70, impulsiveness: 0.40, empathy: 0.55, volatility: 0.30 }},
  { existing: "npc_rowan_hewer", spouse: "npc_tansy_hewer", display: "Tansy Hewer", home: "hinterland.woodlands.cabins", quality: 0.60,
    tags: ["woodcutter", "laborer"], favorites: ["haul_materials", "chop_wood"], backstory: "Tansy drives the timber sled and stacks lumber.",
    personality: { sociability: 0.40, adventurousness: 0.30, frugality: 0.60, diligence: 0.80, impulsiveness: 0.25, empathy: 0.45, volatility: 0.25 }},
  { existing: "npc_ash_bowyer", spouse: "npc_felda_bowyer", display: "Felda Bowyer", home: "hinterland.woodlands.cabins", quality: 0.60,
    tags: ["ranger", "craftsman"], favorites: ["forage_herbs", "trade_goods"], backstory: "Felda fletches arrows and treats bowstrings with pine resin.",
    personality: { sociability: 0.45, adventurousness: 0.55, frugality: 0.50, diligence: 0.75, impulsiveness: 0.30, empathy: 0.50, volatility: 0.25 }},
  { existing: "npc_darren_pikeman", spouse: "npc_myrtle_pikeman", display: "Myrtle Pikeman", home: "hinterland.woodlands.cabins", quality: 0.60,
    tags: ["herbalist"], favorites: ["forage_herbs", "chat_with_neighbor"], backstory: "Myrtle tends a herb garden and patches up militia wounds.",
    personality: { sociability: 0.60, adventurousness: 0.25, frugality: 0.55, diligence: 0.65, impulsiveness: 0.30, empathy: 0.80, volatility: 0.20 }},
  { existing: "npc_hanna_barleycroft", spouse: "npc_einar_barleycroft", display: "Einar Barleycroft", home: "hinterland.woodlands.cabins", quality: 0.60,
    tags: ["farmer", "laborer"], favorites: ["plow_field", "haul_materials"], backstory: "Einar clears stumps and turns the rocky soil.",
    personality: { sociability: 0.35, adventurousness: 0.40, frugality: 0.60, diligence: 0.85, impulsiveness: 0.25, empathy: 0.40, volatility: 0.30 }},
  { existing: "npc_jory_snare", spouse: "npc_willow_snare", display: "Willow Snare", home: "hinterland.woodlands.cabins", quality: 0.60,
    tags: ["trapper", "craftsman"], favorites: ["forage_herbs", "trade_goods"], backstory: "Willow scrapes pelts and sews them into warm cloaks.",
    personality: { sociability: 0.50, adventurousness: 0.40, frugality: 0.55, diligence: 0.70, impulsiveness: 0.30, empathy: 0.55, volatility: 0.25 }},

  // ── City Portside ──
  { existing: "npc_olek_netcaster", spouse: "npc_signe_netcaster", display: "Signe Netcaster", home: "city.portside.quarter", quality: 0.60,
    tags: ["fisherman", "laborer"], favorites: ["fish_harbor", "sell_food"], backstory: "Signe mends nets and sells the morning catch at the fishmarket.",
    personality: { sociability: 0.60, adventurousness: 0.25, frugality: 0.55, diligence: 0.70, impulsiveness: 0.30, empathy: 0.60, volatility: 0.25 }},
  { existing: "npc_tove_scaletrade", spouse: "npc_lars_scaletrade", display: "Lars Scaletrade", home: "city.portside.quarter", quality: 0.60,
    tags: ["merchant", "fishmonger"], favorites: ["tend_shop", "buy_food"], backstory: "Lars mans the stall while Tove negotiates bulk prices.",
    personality: { sociability: 0.55, adventurousness: 0.20, frugality: 0.65, diligence: 0.70, impulsiveness: 0.25, empathy: 0.45, volatility: 0.20 }},
  { existing: "npc_harlan_keelworth", spouse: "npc_astrid_keelworth", display: "Astrid Keelworth", home: "city.portside.quarter", quality: 0.60,
    tags: ["craftsman", "laborer"], favorites: ["trade_goods", "chat_with_neighbor"], backstory: "Astrid makes rope and sells chandlery goods to sailors.",
    personality: { sociability: 0.65, adventurousness: 0.30, frugality: 0.50, diligence: 0.65, impulsiveness: 0.35, empathy: 0.60, volatility: 0.25 }},
  { existing: "npc_bram_cartwright", spouse: "npc_lotta_cartwright", display: "Lotta Cartwright", home: "city.portside.quarter", quality: 0.60,
    tags: ["laborer"], favorites: ["sell_food", "chat_with_neighbor"], backstory: "Lotta sells bread and dried fish from a small portside window.",
    personality: { sociability: 0.70, adventurousness: 0.20, frugality: 0.60, diligence: 0.55, impulsiveness: 0.40, empathy: 0.65, volatility: 0.30 }},

  // ── City Residential ──
  { existing: "npc_agna_crustworth", spouse: "npc_per_crustworth", display: "Per Crustworth", home: "city.residential.quarter", quality: 0.75,
    tags: ["baker", "craftsman"], favorites: ["bake_bread", "buy_food"], backstory: "Per hauls flour from the mill and kneads dough before dawn.",
    personality: { sociability: 0.40, adventurousness: 0.15, frugality: 0.65, diligence: 0.85, impulsiveness: 0.20, empathy: 0.45, volatility: 0.20 }},
  { existing: "npc_sela_hidecraft", spouse: "npc_ulf_hidecraft", display: "Ulf Hidecraft", home: "city.residential.quarter", quality: 0.75,
    tags: ["tanner", "craftsman"], favorites: ["tan_leather", "trade_goods"], backstory: "Ulf stitches leather goods and handles the workshop sales.",
    personality: { sociability: 0.45, adventurousness: 0.25, frugality: 0.60, diligence: 0.75, impulsiveness: 0.25, empathy: 0.50, volatility: 0.25 }},
  { existing: "npc_mira_threadwell", spouse: "npc_brand_threadwell", display: "Brand Threadwell", home: "city.residential.quarter", quality: 0.75,
    tags: ["weaver", "craftsman"], favorites: ["carpenter_work", "trade_goods"], backstory: "Brand dyes cloth and tends the loom when Mira is selling.",
    personality: { sociability: 0.35, adventurousness: 0.20, frugality: 0.70, diligence: 0.80, impulsiveness: 0.15, empathy: 0.55, volatility: 0.15 }},
  { existing: "npc_yara_goodsworth", spouse: "npc_hagen_goodsworth", display: "Hagen Goodsworth", home: "city.residential.quarter", quality: 0.75,
    tags: ["merchant", "shopkeeper"], favorites: ["tend_shop", "buy_food"], backstory: "Hagen manages the back warehouse and inventory counts.",
    personality: { sociability: 0.40, adventurousness: 0.15, frugality: 0.75, diligence: 0.80, impulsiveness: 0.15, empathy: 0.45, volatility: 0.15 }},

  // ── City Manor ──
  { existing: "npc_aldren_stonegate", spouse: "npc_isolde_stonegate", display: "Isolde Stonegate", home: "city.manor_district.estates", quality: 0.95,
    tags: ["civic", "noble"], favorites: ["chat_with_neighbor", "eat_fine_meal"], backstory: "Isolde hosts civic functions and manages the household.",
    personality: { sociability: 0.80, adventurousness: 0.20, frugality: 0.40, diligence: 0.60, impulsiveness: 0.30, empathy: 0.65, volatility: 0.20 }},

  // ── City Slums ──
  { existing: "npc_dace_undertow", spouse: "npc_kaia_undertow", display: "Kaia Undertow", home: "city.slums.shanty_row", quality: 0.50,
    tags: ["smuggler", "underclass"], favorites: ["smuggle_goods", "chat_with_neighbor"], backstory: "Kaia keeps watch and passes signals for Dace's runs.",
    personality: { sociability: 0.45, adventurousness: 0.55, frugality: 0.70, diligence: 0.50, impulsiveness: 0.55, empathy: 0.35, volatility: 0.45 }},

  // ── City Barracks ──
  { existing: "npc_tam_shieldwall", spouse: "npc_brynja_shieldwall", display: "Brynja Shieldwall", home: "city.residential.quarter", quality: 0.75,
    tags: ["laborer"], favorites: ["sell_food", "chat_with_neighbor"], backstory: "Brynja keeps house in the residential quarter while Tam is on duty.",
    personality: { sociability: 0.60, adventurousness: 0.25, frugality: 0.65, diligence: 0.60, impulsiveness: 0.30, empathy: 0.70, volatility: 0.20 }},
];

// Existing pairs (no new NPC needed, just family relationship)
const existingPairs = [
  { a: "npc_aldric_thresher", b: "npc_greta_windrow", home: "hinterland.farmland.homes", trade: "farming" },
  { a: "npc_olav_copperfield", b: "npc_tilda_kettle", home: "hinterland.quarry.homes", trade: "quarry" },
];

// Young singles (new NPCs, no spouse)
const singles = [
  { id: "npc_fynn_driftson", display: "Fynn Driftson", home: "city.portside.quarter", quality: 0.60,
    tags: ["dockworker", "laborer"], favorites: ["load_cargo", "drink_at_tavern"], backstory: "Fynn arrived on a merchant ship and never left. Works the docks for coin.",
    personality: { sociability: 0.65, adventurousness: 0.70, frugality: 0.35, diligence: 0.50, impulsiveness: 0.65, empathy: 0.45, volatility: 0.40 }},
  { id: "npc_vara_quickstep", display: "Vara Quickstep", home: "hinterland.farmland.homes", quality: 0.60,
    tags: ["laborer", "drifter"], favorites: ["haul_materials", "sell_at_market"], backstory: "Vara picks up work wherever she can find it. Fast on her feet.",
    personality: { sociability: 0.55, adventurousness: 0.60, frugality: 0.45, diligence: 0.55, impulsiveness: 0.55, empathy: 0.50, volatility: 0.35 }},
  { id: "npc_beck_ashfall", display: "Beck Ashfall", home: "hinterland.quarry.homes", quality: 0.60,
    tags: ["miner", "laborer"], favorites: ["mine_ore", "drink_at_tavern"], backstory: "Beck came from a collapsed mine up north seeking steady work.",
    personality: { sociability: 0.40, adventurousness: 0.50, frugality: 0.50, diligence: 0.65, impulsiveness: 0.45, empathy: 0.35, volatility: 0.40 }},
  { id: "npc_lark_greenhollow", display: "Lark Greenhollow", home: "hinterland.woodlands.cabins", quality: 0.60,
    tags: ["herbalist", "loner"], favorites: ["forage_herbs", "chat_with_neighbor"], backstory: "Lark studies medicinal plants under Neve Yarrow's guidance.",
    personality: { sociability: 0.45, adventurousness: 0.55, frugality: 0.55, diligence: 0.60, impulsiveness: 0.35, empathy: 0.70, volatility: 0.25 }},
  { id: "npc_reed_copperworth", display: "Reed Copperworth", home: "city.market.merchant_row", quality: 0.75,
    tags: ["merchant", "trader"], favorites: ["trade_goods", "tend_shop"], backstory: "Reed apprentices under Yara Goodsworth, learning the trade.",
    personality: { sociability: 0.60, adventurousness: 0.35, frugality: 0.55, diligence: 0.70, impulsiveness: 0.40, empathy: 0.50, volatility: 0.25 }},
];

// Forbidden actions for wildcard NPCs
const baseForbidden = [
  "buy_ore_for_smithy", "stock_smithy_ore", "pickup_quarry_tools",
  "deliver_tools_city_market", "deliver_metal_craftsman", "smith_silverware",
  "buy_food_for_golden_keg", "stock_golden_keg",
  "buy_food_for_portside", "stock_portside",
  "buy_food_for_shanty_row", "stock_shanty_row",
  "buy_food_for_millhaven", "stock_millhaven",
  "buy_food_for_slag_tankard", "stock_slag_tankard",
  "buy_food_for_stump_antler", "stock_stump_antler",
  "pickup_harbor_food", "deliver_food_tavern_docks",
  "deliver_food_market", "deliver_food_tavern_portside",
  "deliver_food_tavern_slums", "pickup_harbor_metal",
  "deliver_metal_market",
  "hold_court", "reward_loyalist", "issue_decree", "levy_tribute", "punish_dissent",
  "collect_taxes",
  "hold_service"
];

function makeProfile(id, display, home, quality, tags, favorites, backstory, personality, hasChildren) {
  const hungerDecay = hasChildren ? 0.016 : 0.012;  // parents consume more to feed kids
  return {
    id,
    displayName: display,
    embodied: true,
    properties: {
      hunger: { value: 0.6, decayRate: hungerDecay, critical: 0.15 },
      social: { value: 0.4, decayRate: 0.003, critical: 0.1 },
      rest: { value: 0.6, decayRate: 0.007, critical: 0.1 },
      mood: { value: 0.55, decayRate: 0.002, critical: 0.15 },
      gold: { value: 8, min: 0, max: 100000, decayRate: 0 },
      trade_goods_food: { value: 0, min: 0, max: 5, decayRate: 0 },
      trade_goods_ore: { value: 0, min: 0, max: 5, decayRate: 0 },
      trade_goods_tools: { value: 0, min: 0, max: 5, decayRate: 0 },
      homeQuality: { value: quality, decayRate: 0.0 }
    },
    personality,
    evaluationInterval: 1,
    actionAccess: {
      allowed: ["*"],
      forbidden: baseForbidden.slice(),
      favorites,
      favoriteMultiplier: 1.3
    },
    identity: {
      backstory,
      tags
    },
    initialModifiers: [{
      type: "memory",
      actionBonus: Object.fromEntries(favorites.map(f => [f, 0.15])),
      decayRate: 0,
      intensity: 0.6,
      flavor: "Trade knowledge"
    }],
    homeLocation: home
  };
}

// ── Generate spouse NPC files ──
let generated = 0;
const childPairs = new Set(); // indices of pairs that get children (every other pair roughly)

pairs.forEach((p, i) => {
  if (i % 2 === 0) childPairs.add(i); // ~half have children
});

pairs.forEach((p, i) => {
  const hasChildren = childPairs.has(i);
  const profile = makeProfile(p.spouse, p.display, p.home, p.quality, p.tags, p.favorites, p.backstory, p.personality, hasChildren);
  const filename = `spouse_${p.spouse.replace('npc_', '')}.json`;
  fs.writeFileSync(path.join(autonomesDir, filename), JSON.stringify(profile, null, 2) + '\n');
  generated++;
});

// Generate single NPC files
singles.forEach(s => {
  const profile = makeProfile(s.id, s.display, s.home, s.quality, s.tags, s.favorites, s.backstory, s.personality, false);
  const filename = `single_${s.id.replace('npc_', '')}.json`;
  fs.writeFileSync(path.join(autonomesDir, filename), JSON.stringify(profile, null, 2) + '\n');
  generated++;
});

console.log(`Generated ${generated} NPC profiles.`);

// ── Update existing parent NPCs: increase hunger decay for those with children ──
// We'll also output a list for manual verification
const parentsWithChildren = [];
pairs.forEach((p, i) => {
  if (childPairs.has(i)) {
    parentsWithChildren.push(p.existing);
  }
});
console.log(`Parents with children (need hunger decay bump): ${parentsWithChildren.join(', ')}`);

// ── Generate families.json in standard relationship format ──
// Each pair becomes two directed relationships (A→B and B→A)
const familyRels = [];

function addSpousePair(a, b) {
  const relProps = {
    affinity: { value: 0.85, decayRate: 0.0002 },
    familiarity: { value: 0.90, decayRate: 0.0001 },
    trust: { value: 0.70, decayRate: 0.0001 }
  };
  familyRels.push({ source: a, target: b, tags: ["spouse", "family"], properties: relProps });
  familyRels.push({ source: b, target: a, tags: ["spouse", "family"], properties: relProps });
}

// New spouse pairs
pairs.forEach(p => addSpousePair(p.existing, p.spouse));

// Existing pairs
existingPairs.forEach(ep => addSpousePair(ep.a, ep.b));

fs.writeFileSync(path.join(relsDir, 'families.json'), JSON.stringify(familyRels, null, 2) + '\n');
console.log(`Generated families.json with ${familyRels.length} relationship entries (${familyRels.length / 2} pairs).`);
