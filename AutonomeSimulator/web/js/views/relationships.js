import { api } from '../api.js';
import { ForceGraph } from '../components/graph.js';

export async function renderRelationships(container, dataset) {
  const [rels, autonomes] = await Promise.all([
    api.relationships(dataset),
    api.autonomes(dataset),
  ]);

  const nameMap = {};
  autonomes.forEach(a => nameMap[a.id] = a.displayName);

  // Build node/edge sets
  const nodeIds = new Set();
  for (const r of rels) { nodeIds.add(r.source); nodeIds.add(r.target); }

  const nodes = [...nodeIds].map(id => ({
    id,
    label: nameMap[id] || id,
    shortLabel: (nameMap[id] || id).substring(0, 5),
    color: '#0db8de',
  }));

  const edges = rels.map(r => ({
    source: r.source,
    target: r.target,
    label: (r.tags || []).join(', '),
    weight: r.properties?.affinity?.value ? 1 + r.properties.affinity.value * 3 : 1.5,
  }));

  let html = `<h2>Relationships</h2>
    <div id="rel-graph"></div>
    <div id="rel-detail" style="margin-top:16px"></div>
    <div style="margin-top:16px">
      <h3>All Relationships</h3>
      <table><tr><th>Source</th><th>Target</th><th>Tags</th><th>Properties</th></tr>`;
  for (const r of rels) {
    const props = r.properties ? Object.entries(r.properties).map(([k, v]) => `${k}: ${v.value ?? v}`).join(', ') : '-';
    html += `<tr>
      <td>${nameMap[r.source] || r.source}</td>
      <td>${nameMap[r.target] || r.target}</td>
      <td>${(r.tags || []).map(t => `<span class="tag">${t}</span>`).join(' ')}</td>
      <td style="font-size:12px">${props}</td>
    </tr>`;
  }
  html += '</table></div>';
  container.innerHTML = html;

  const graphEl = container.querySelector('#rel-graph');
  const detailEl = container.querySelector('#rel-detail');

  const graph = new ForceGraph(graphEl, {
    width: 600,
    height: 400,
    onEdgeClick: ({ source, target }) => {
      const rel = rels.find(r => r.source === source && r.target === target);
      if (!rel) return;
      let d = `<div class="detail-section">
        <div class="detail-section-title">${nameMap[source] || source} &rarr; ${nameMap[target] || target}</div>
        <div>Tags: ${(rel.tags || []).map(t => `<span class="tag">${t}</span>`).join(' ')}</div>`;
      if (rel.properties) {
        d += '<table><tr><th>Property</th><th>Value</th><th>Decay Rate</th></tr>';
        for (const [k, v] of Object.entries(rel.properties)) {
          d += `<tr><td>${k}</td><td>${v.value ?? v}</td><td>${v.decayRate ?? '-'}</td></tr>`;
        }
        d += '</table>';
      }
      d += '</div>';
      detailEl.innerHTML = d;
    }
  });

  graph.setData(nodes, edges);
}
