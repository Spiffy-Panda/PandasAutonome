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
