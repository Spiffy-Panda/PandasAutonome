const BASE = '';

async function fetchJson(url) {
  const res = await fetch(BASE + url);
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json();
}

export const api = {
  datasets: () => fetchJson('/api/datasets'),
  autonomes: (ds) => fetchJson(`/api/data/${ds}/autonomes`),
  actions: (ds) => fetchJson(`/api/data/${ds}/actions`),
  relationships: (ds) => fetchJson(`/api/data/${ds}/relationships`),
  locations: (ds) => fetchJson(`/api/data/${ds}/locations`),
  curves: (ds) => fetchJson(`/api/data/${ds}/curves`),
  propertyLevels: (ds) => fetchJson(`/api/data/${ds}/property_levels`),
  outputs: () => fetchJson('/api/outputs'),
  output: (name) => fetchJson(`/api/output/${name}`),
  analysisRuns: () => fetchJson('/api/analysis-runs'),
  analysisReport: (run) => fetchJson(`/api/analysis/${run}`),
  analysisSimulation: (run) => fetchJson(`/api/analysis/${run}/simulation`),
  analysisInventory: (run) => fetchJson(`/api/analysis/${run}/inventory`),
  analysisMeta: (run) => fetchJson(`/api/analysis/${run}/meta`),
};

// --- Interactive simulation API (C# server on port 3801) ---

const SIM_BASE = 'http://localhost:3801';

async function simFetch(path, opts = {}) {
  const res = await fetch(SIM_BASE + path, {
    ...opts,
    headers: { 'Content-Type': 'application/json', ...opts.headers },
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || `HTTP ${res.status}`);
  }
  return res.json();
}

export const simApi = {
  // Simulation control
  status: () => simFetch('/api/simulation/status'),
  tick: () => simFetch('/api/simulation/tick', { method: 'POST' }),
  auto: (tps) => simFetch('/api/simulation/auto', { method: 'POST', body: JSON.stringify({ ticksPerSecond: tps }) }),
  pause: () => simFetch('/api/simulation/pause', { method: 'POST' }),

  // World state
  worldState: () => simFetch('/api/world/state'),
  worldTick: () => simFetch('/api/world/tick'),
  entities: () => simFetch('/api/world/entities'),

  // Entity
  entityState: (id) => simFetch(`/api/entity/${encodeURIComponent(id)}/state`),
  entityActions: (id) => simFetch(`/api/entity/${encodeURIComponent(id)}/actions`),
  entityAct: (id, actionId, token) => simFetch(`/api/entity/${encodeURIComponent(id)}/act`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}` },
    body: JSON.stringify({ actionId }),
  }),

  // Possession
  possess: (entityId) => simFetch('/api/entity/possess', { method: 'POST', body: JSON.stringify({ entityId }) }),
  release: (entityId) => simFetch('/api/entity/release', { method: 'POST', body: JSON.stringify({ entityId }) }),
  slots: () => simFetch('/api/entity/slots'),

  // WebSocket
  connectWs: () => new WebSocket('ws://localhost:3801/ws/stream'),
};
