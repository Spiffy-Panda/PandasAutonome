import { api, simApi } from '../api.js';

// Color palette for gossip content types
const GOSSIP_COLORS = {
  food_location: '#4caf50',
  noble_weakness: '#f44336',
  trade_opportunity: '#ff9800',
  danger_warning: '#e91e63',
  social_rumor: '#9c27b0',
  default: '#607d8b',
};

// NPC tag color mapping (matches Godot NPCController)
const TAG_COLORS = {
  guard: '#4488cc', military: '#4488cc',
  merchant: '#ccaa44', trader: '#ccaa44',
  fisher: '#44aaaa', sailor: '#44aaaa',
  farmer: '#44aa44', agriculture: '#44aa44',
  thief: '#9944cc', criminal: '#9944cc',
  priest: '#cccccc', clergy: '#cccccc',
  noble: '#882222', official: '#882222',
  craftsman: '#8b6914', smith: '#8b6914',
  laborer: '#d2b48c', miner: '#d2b48c',
  ranger: '#228b22', woodcutter: '#228b22',
};

function getNodeColor(tags) {
  if (!tags) return '#0db8de';
  for (const tag of tags) {
    if (TAG_COLORS[tag]) return TAG_COLORS[tag];
  }
  return '#0db8de';
}

function trustToColor(trust) {
  if (trust == null) return 'rgba(255,255,255,0.25)';
  // Green (high trust) to Red (low trust)
  const r = Math.round(255 * (1 - trust));
  const g = Math.round(200 * trust);
  const b = 50;
  return `rgba(${r},${g},${b},0.6)`;
}

export async function renderSocialGraph(container, dataset, detailId) {
  const [rels, autonomes] = await Promise.all([
    api.relationships(dataset),
    api.autonomes(dataset),
  ]);

  const nameMap = {};
  const tagMap = {};
  const embodiedIds = new Set();
  autonomes.forEach(a => {
    nameMap[a.id] = a.displayName;
    tagMap[a.id] = a.identity?.tags || [];
    if (a.embodied) embodiedIds.add(a.id);
  });

  // Filter to only social relationships between embodied NPCs
  const socialRels = rels.filter(r =>
    embodiedIds.has(r.source) && embodiedIds.has(r.target)
  );

  // Deduplicate edges (keep the one with higher affinity)
  const edgeMap = new Map();
  for (const r of socialRels) {
    const key = [r.source, r.target].sort().join('|');
    const affinity = r.properties?.affinity?.value ?? 0.5;
    const existing = edgeMap.get(key);
    if (!existing || affinity > (existing.properties?.affinity?.value ?? 0)) {
      edgeMap.set(key, r);
    }
  }

  // Build node set from edges
  const nodeIds = new Set();
  for (const r of edgeMap.values()) {
    nodeIds.add(r.source);
    nodeIds.add(r.target);
  }

  // Identify family pairs
  const familyPairs = new Set();
  for (const r of socialRels) {
    if ((r.tags || []).includes('spouse') || (r.tags || []).includes('family')) {
      familyPairs.add([r.source, r.target].sort().join('|'));
    }
  }

  // Render
  const width = 900;
  const height = 600;

  let html = `<h2>Social Graph</h2>
    <div class="social-controls" style="margin-bottom:12px;display:flex;gap:12px;align-items:center">
      <label style="font-size:12px;color:var(--text-secondary)">
        <input type="checkbox" id="show-families-only"> Families only
      </label>
      <label style="font-size:12px;color:var(--text-secondary)">
        <input type="checkbox" id="show-trust-colors" checked> Color by trust
      </label>
      <span style="font-size:11px;color:var(--text-muted)">
        ${nodeIds.size} NPCs, ${edgeMap.size} relationships, ${familyPairs.size} family pairs
      </span>
    </div>
    <div id="social-graph-svg" style="background:var(--bg-primary);border-radius:8px;overflow:hidden"></div>
    <div id="social-detail" style="margin-top:16px"></div>
    <div id="modifier-inspector" style="margin-top:16px"></div>`;

  container.innerHTML = html;

  const graphEl = container.querySelector('#social-graph-svg');
  const detailEl = container.querySelector('#social-detail');
  const modInspectorEl = container.querySelector('#modifier-inspector');
  const familyCheckbox = container.querySelector('#show-families-only');
  const trustCheckbox = container.querySelector('#show-trust-colors');

  function renderGraph(familiesOnly, showTrust) {
    const filteredEdges = familiesOnly
      ? [...edgeMap.entries()].filter(([key]) => familyPairs.has(key)).map(([, v]) => v)
      : [...edgeMap.values()];

    const filteredNodeIds = new Set();
    for (const r of filteredEdges) {
      filteredNodeIds.add(r.source);
      filteredNodeIds.add(r.target);
    }

    const nodes = [...filteredNodeIds].map((id, i) => ({
      id,
      label: nameMap[id] || id,
      x: width / 2 + Math.cos(i * 2 * Math.PI / filteredNodeIds.size) * 200,
      y: height / 2 + Math.sin(i * 2 * Math.PI / filteredNodeIds.size) * 200,
      vx: 0, vy: 0,
      color: getNodeColor(tagMap[id]),
      isFamily: [...familyPairs].some(k => k.includes(id)),
    }));

    const nodeMap = {};
    nodes.forEach(n => nodeMap[n.id] = n);

    const edges = filteredEdges.map(r => {
      const affinity = r.properties?.affinity?.value ?? 0.5;
      const trust = r.properties?.trust?.value ?? null;
      const isSpouse = (r.tags || []).includes('spouse');
      return {
        source: r.source,
        target: r.target,
        weight: 1 + affinity * 4,
        color: showTrust ? trustToColor(trust) : (isSpouse ? 'rgba(255,180,80,0.6)' : 'rgba(255,255,255,0.2)'),
        tags: r.tags || [],
        affinity, trust,
      };
    });

    // Force simulation
    for (let iter = 0; iter < 300; iter++) {
      for (let i = 0; i < nodes.length; i++) {
        for (let j = i + 1; j < nodes.length; j++) {
          let dx = nodes[j].x - nodes[i].x;
          let dy = nodes[j].y - nodes[i].y;
          let dist = Math.sqrt(dx * dx + dy * dy) || 1;
          let force = 6000 / (dist * dist);
          let fx = (dx / dist) * force;
          let fy = (dy / dist) * force;
          nodes[i].vx -= fx; nodes[i].vy -= fy;
          nodes[j].vx += fx; nodes[j].vy += fy;
        }
      }

      for (const e of edges) {
        const a = nodeMap[e.source];
        const b = nodeMap[e.target];
        if (!a || !b) continue;
        let dx = b.x - a.x;
        let dy = b.y - a.y;
        let dist = Math.sqrt(dx * dx + dy * dy) || 1;
        // Family pairs attract more strongly
        const idealDist = e.tags.includes('spouse') ? 60 : 120;
        let force = (dist - idealDist) * 0.03;
        let fx = (dx / dist) * force;
        let fy = (dy / dist) * force;
        a.vx += fx; a.vy += fy;
        b.vx -= fx; b.vy -= fy;
      }

      for (const n of nodes) {
        n.vx += (width / 2 - n.x) * 0.004;
        n.vy += (height / 2 - n.y) * 0.004;
        n.vx *= 0.85; n.vy *= 0.85;
        n.x += n.vx; n.y += n.vy;
        n.x = Math.max(30, Math.min(width - 30, n.x));
        n.y = Math.max(30, Math.min(height - 30, n.y));
      }
    }

    // Render SVG
    let svg = `<svg width="${width}" height="${height}" xmlns="http://www.w3.org/2000/svg">`;

    // Legend
    svg += `<text x="10" y="18" fill="rgba(255,255,255,0.5)" font-size="10">Edge: thickness=affinity, color=trust (green=high, red=low)</text>`;

    // Edges
    for (const e of edges) {
      const a = nodeMap[e.source];
      const b = nodeMap[e.target];
      if (!a || !b) continue;
      const dashArray = e.tags.includes('spouse') ? '' : 'stroke-dasharray="4,2"';
      svg += `<line x1="${a.x}" y1="${a.y}" x2="${b.x}" y2="${b.y}"
        stroke="${e.color}" stroke-width="${e.weight}" ${dashArray}
        data-edge="${e.source}|${e.target}" style="cursor:pointer"/>`;
    }

    // Nodes
    for (const n of nodes) {
      const r = n.isFamily ? 14 : 10;
      svg += `<circle cx="${n.x}" cy="${n.y}" r="${r}" fill="${n.color}" opacity="0.85"
        stroke="${n.isFamily ? 'rgba(255,180,80,0.6)' : 'none'}" stroke-width="2"
        data-node="${n.id}" style="cursor:pointer"/>`;
      const shortName = (n.label || '').split(' ')[0].substring(0, 6);
      svg += `<text x="${n.x}" y="${n.y + r + 12}" text-anchor="middle"
        fill="rgba(255,255,255,0.7)" font-size="9" pointer-events="none">${shortName}</text>`;
    }

    svg += '</svg>';
    graphEl.innerHTML = svg;

    // Event listeners
    graphEl.querySelectorAll('[data-node]').forEach(el => {
      el.addEventListener('click', () => showNodeDetail(el.dataset.node));
    });
    graphEl.querySelectorAll('[data-edge]').forEach(el => {
      el.addEventListener('click', () => {
        const [s, t] = el.dataset.edge.split('|');
        showEdgeDetail(s, t);
      });
    });
  }

  function showNodeDetail(entityId) {
    const rels = socialRels.filter(r => r.source === entityId || r.target === entityId);
    const name = nameMap[entityId] || entityId;

    let html = `<div class="detail-section">
      <div class="detail-section-title">${name}</div>
      <div style="font-size:11px;color:var(--text-muted);margin-bottom:8px">${tagMap[entityId]?.join(', ') || ''}</div>
      <table><tr><th>Connection</th><th>Tags</th><th>Affinity</th><th>Trust</th></tr>`;

    for (const r of rels) {
      const otherId = r.source === entityId ? r.target : r.source;
      const otherName = nameMap[otherId] || otherId;
      const affinity = r.properties?.affinity?.value ?? '-';
      const trust = r.properties?.trust?.value ?? '-';
      html += `<tr>
        <td>${otherName}</td>
        <td>${(r.tags || []).map(t => `<span class="tag">${t}</span>`).join(' ')}</td>
        <td>${typeof affinity === 'number' ? affinity.toFixed(2) : affinity}</td>
        <td style="color:${typeof trust === 'number' ? trustToColor(trust) : 'inherit'}">${typeof trust === 'number' ? trust.toFixed(2) : trust}</td>
      </tr>`;
    }
    html += '</table></div>';
    detailEl.innerHTML = html;

    // Try to load live modifiers
    loadModifierInspector(entityId);
  }

  function showEdgeDetail(sourceId, targetId) {
    const rel = socialRels.find(r =>
      (r.source === sourceId && r.target === targetId) ||
      (r.source === targetId && r.target === sourceId)
    );
    if (!rel) return;

    let html = `<div class="detail-section">
      <div class="detail-section-title">${nameMap[sourceId] || sourceId} &harr; ${nameMap[targetId] || targetId}</div>
      <div style="margin:8px 0">${(rel.tags || []).map(t => `<span class="tag">${t}</span>`).join(' ')}</div>`;

    if (rel.properties) {
      html += '<table><tr><th>Property</th><th>Value</th><th>Decay Rate</th></tr>';
      for (const [k, v] of Object.entries(rel.properties)) {
        html += `<tr><td>${k}</td><td>${(v.value ?? v).toFixed?.(3) ?? v.value ?? v}</td><td>${v.decayRate ?? '-'}</td></tr>`;
      }
      html += '</table>';
    }
    html += '</div>';
    detailEl.innerHTML = html;
  }

  async function loadModifierInspector(entityId) {
    try {
      const state = await simApi.entityState(entityId);
      if (!state.modifiers || state.modifiers.length === 0) {
        modInspectorEl.innerHTML = `<div class="detail-section">
          <div class="detail-section-title">Active Modifiers</div>
          <div style="color:var(--text-muted);font-size:12px">No active modifiers</div>
        </div>`;
        return;
      }

      // Filter for gossip and social modifiers
      const gossipMods = state.modifiers.filter(m => m.gossip || m.type === 'gossip' || m.type === 'social_memory');
      const otherMods = state.modifiers.filter(m => !m.gossip && m.type !== 'gossip' && m.type !== 'social_memory');

      let html = `<div class="detail-section">
        <div class="detail-section-title">Active Modifiers (${state.modifiers.length})</div>`;

      if (gossipMods.length > 0) {
        html += `<div style="margin:8px 0"><strong style="font-size:12px">Gossip & Social (${gossipMods.length})</strong></div>`;
        html += renderModifierCards(gossipMods, true);
      }

      if (otherMods.length > 0) {
        html += `<div style="margin:8px 0"><strong style="font-size:12px">Other (${otherMods.length})</strong></div>`;
        html += renderModifierCards(otherMods, false);
      }

      html += '</div>';
      modInspectorEl.innerHTML = html;
    } catch {
      modInspectorEl.innerHTML = `<div class="detail-section">
        <div class="detail-section-title">Modifier Inspector</div>
        <div style="color:var(--text-muted);font-size:12px">Connect to simulation server (port 3801) for live modifier data</div>
      </div>`;
    }
  }

  function renderModifierCards(mods, isGossip) {
    let html = '<div style="display:flex;flex-wrap:wrap;gap:8px">';
    for (const m of mods) {
      const source = nameMap[m.source] || m.source || '?';
      const contentType = m.contentType || m.type || 'unknown';
      const badgeColor = GOSSIP_COLORS[contentType] || GOSSIP_COLORS.default;

      html += `<div style="background:var(--bg-secondary);border-radius:6px;padding:8px 12px;font-size:11px;min-width:180px;border-left:3px solid ${badgeColor}">
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:4px">
          <span style="background:${badgeColor};color:white;padding:1px 6px;border-radius:8px;font-size:9px">${contentType}</span>
          ${m.gossip ? '<span style="color:#ff9800;font-size:9px">gossip</span>' : ''}
        </div>
        <div style="color:var(--text-primary)">From: ${source}</div>
        ${m.flavor ? `<div style="color:var(--text-muted);font-style:italic;margin-top:2px">${m.flavor}</div>` : ''}
        <div style="color:var(--text-secondary);margin-top:4px">
          ${m.intensity != null ? `Intensity: ${m.intensity.toFixed(2)}` : ''}
          ${m.duration != null ? ` | Duration: ${m.duration}` : ''}
        </div>
        ${m.actionBonus ? `<div style="color:var(--text-secondary);margin-top:2px">Bonuses: ${Object.entries(m.actionBonus).map(([k,v]) => `${k}: ${v > 0 ? '+' : ''}${v.toFixed(2)}`).join(', ')}</div>` : ''}
      </div>`;
    }
    html += '</div>';
    return html;
  }

  // Initial render
  renderGraph(false, true);

  // Filter controls
  familyCheckbox.addEventListener('change', () => {
    renderGraph(familyCheckbox.checked, trustCheckbox.checked);
  });
  trustCheckbox.addEventListener('change', () => {
    renderGraph(familyCheckbox.checked, trustCheckbox.checked);
  });

  // If detail requested, show it
  if (detailId) {
    showNodeDetail(detailId);
  }
}
