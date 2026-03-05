import { api } from '../api.js';
import { ForceGraph } from '../components/graph.js';

export async function renderLocations(container, dataset) {
  const data = await api.locations(dataset);

  // Build graph
  const nodes = data.map(loc => ({
    id: loc.id,
    label: loc.displayName,
    shortLabel: loc.displayName.substring(0, 6),
    tags: loc.tags,
    color: '#66cc88',
  }));

  const edgeSet = new Set();
  const edges = [];
  for (const loc of data) {
    for (const conn of loc.connectedTo || []) {
      const key = [loc.id, conn].sort().join('|');
      if (!edgeSet.has(key)) {
        edgeSet.add(key);
        edges.push({ source: loc.id, target: conn, weight: 1.5 });
      }
    }
  }

  let html = `<h2>Locations</h2>
    <div id="loc-graph"></div>
    <div id="loc-detail" style="margin-top:16px"></div>
    <div style="margin-top:16px">
      <h3>All Locations</h3>
      <table><tr><th>ID</th><th>Name</th><th>Tags</th><th>Connected To</th></tr>`;
  for (const loc of data) {
    html += `<tr>
      <td style="font-family:monospace;font-size:12px">${loc.id}</td>
      <td>${loc.displayName}</td>
      <td>${(loc.tags || []).map(t => `<span class="tag">${t}</span>`).join(' ')}</td>
      <td style="font-size:12px">${(loc.connectedTo || []).join(', ')}</td>
    </tr>`;
  }
  html += '</table></div>';
  container.innerHTML = html;

  const graphEl = container.querySelector('#loc-graph');
  const detailEl = container.querySelector('#loc-detail');

  const graph = new ForceGraph(graphEl, {
    width: 600,
    height: 350,
    onNodeClick: (nodeId) => {
      const loc = data.find(l => l.id === nodeId);
      if (!loc) return;
      detailEl.innerHTML = `<div class="detail-section">
        <div class="detail-section-title">${loc.displayName}</div>
        <div style="font-size:12px;font-family:monospace;color:var(--text-dim);margin-bottom:8px">${loc.id}</div>
        <div>Tags: ${(loc.tags || []).map(t => `<span class="tag">${t}</span>`).join(' ')}</div>
        <div style="margin-top:6px;font-size:12px">Connected to: ${(loc.connectedTo || []).join(', ')}</div>
      </div>`;
    }
  });

  graph.setData(nodes, edges);
}
