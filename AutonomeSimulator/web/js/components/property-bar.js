const PROP_COLORS = {
  // NPC properties
  hunger: '#e07040',
  social: '#4da6ff',
  trade_goods_food: '#99cc55',
  trade_goods_ore: '#8899bb',
  trade_goods_tools: '#cc8844',
  rest: '#a066cc',
  mood: '#66cc88',
  gold: '#ccbb44',
  purpose: '#cc66aa',
  comfort: '#66bbbb',
  entertainment: '#cc8844',
  // Org properties
  territory: '#70c070',
  defense: '#e06080',
  morale: '#60aadd',
  trade_volume: '#ddaa44',
  influence: '#bb77dd',
  ore_supply: '#8899bb',
  food_supply: '#99cc55',
};

// Fallback palette for unknown properties
const FALLBACK_PALETTE = [
  '#e94560', '#0db8de', '#66cc88', '#ccbb44',
  '#a066cc', '#e07040', '#cc66aa', '#66bbbb',
];
const _fallbackMap = {};
let _fallbackIdx = 0;

function getColor(propId) {
  if (PROP_COLORS[propId]) return PROP_COLORS[propId];
  if (!_fallbackMap[propId]) {
    _fallbackMap[propId] = FALLBACK_PALETTE[_fallbackIdx % FALLBACK_PALETTE.length];
    _fallbackIdx++;
  }
  return _fallbackMap[propId];
}

export function renderPropertyBars(properties, container) {
  let html = '';
  for (const [id, prop] of Object.entries(properties)) {
    const val = typeof prop === 'number' ? prop : prop.value;
    const min = typeof prop === 'number' ? 0 : (prop.min ?? 0);
    const max = typeof prop === 'number' ? 1 : (prop.max ?? 1);
    const pct = max > min ? ((val - min) / (max - min)) * 100 : 0;
    const displayVal = max > 100 ? val.toFixed(0) : val.toFixed(2);

    html += `<div class="prop-bar-container">
      <div class="prop-bar-label">
        <span>${id}</span><span>${displayVal}</span>
      </div>
      <div class="prop-bar-track">
        <div class="prop-bar-fill" style="width:${Math.min(pct, 100)}%;background:${getColor(id)}"></div>
      </div>
    </div>`;
  }
  container.innerHTML = html;
}

export function renderPropertyTable(properties) {
  let html = '<table><tr><th>Property</th><th>Value</th><th>Decay</th><th>Critical</th><th>Range</th></tr>';
  for (const [id, p] of Object.entries(properties)) {
    const val = typeof p === 'number' ? p : p.value;
    const decay = p.decayRate ?? 0;
    const crit = p.critical != null ? p.critical.toFixed(2) : '-';
    const range = `${p.min ?? 0} - ${p.max ?? 1}`;
    html += `<tr>
      <td><span style="color:${getColor(id)}">${id}</span></td>
      <td>${typeof val === 'number' && val < 100 ? val.toFixed(3) : val}</td>
      <td>${decay}</td>
      <td>${crit}</td>
      <td>${range}</td>
    </tr>`;
  }
  html += '</table>';
  return html;
}

export { getColor, PROP_COLORS };
