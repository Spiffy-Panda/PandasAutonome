import { api } from '../api.js';
import { renderPropertyBars, renderPropertyTable, getColor } from '../components/property-bar.js';

/**
 * Resolve property levels for a single entity (client-side mirror of DataLoader.ResolvePropertyLevels).
 */
function resolvePropertyLevels(profile, levelConfig) {
  if (!levelConfig || !levelConfig.sets) return {};

  // Determine which set IDs to use
  let setIds = profile.propertySets;
  if (!setIds || setIds.length === 0) {
    const typeKey = profile.embodied ? 'embodied' : 'non_embodied';
    setIds = levelConfig.entityTypeDefaults?.[typeKey] || [];
  }

  const merged = {};
  for (const setId of setIds) {
    const set = levelConfig.sets[setId];
    if (!set) continue;
    for (const [levelName, propIds] of Object.entries(set)) {
      for (const propId of propIds) {
        if (!(propId in merged)) merged[propId] = levelName;
      }
    }
  }

  // Unclassified properties default to 'any'
  if (profile.properties) {
    for (const propId of Object.keys(profile.properties)) {
      if (!(propId in merged)) merged[propId] = 'any';
    }
  }
  return merged;
}

/**
 * Filter properties for card display: hide optional properties that are at zero value.
 */
function filterPropertiesForCard(properties, levels) {
  const filtered = {};
  for (const [id, prop] of Object.entries(properties)) {
    const level = levels[id] || 'any';
    const val = typeof prop === 'number' ? prop : prop.value;
    // Hide optional properties at zero
    if (level === 'optional' && val <= 0) continue;
    filtered[id] = prop;
  }
  return filtered;
}

export async function renderAutonomes(container, dataset, detailId) {
  const [data, levelConfig] = await Promise.all([
    api.autonomes(dataset),
    api.propertyLevels(dataset),
  ]);

  if (detailId) {
    const item = data.find(d => d.id === detailId);
    if (!item) { container.innerHTML = '<div class="empty-state">Not found</div>'; return; }
    const levels = resolvePropertyLevels(item, levelConfig);
    renderDetail(container, item, levels);
    return;
  }

  let html = '<h2>Autonomes</h2><div class="card-grid">';
  const cardProps = [];
  for (const a of data) {
    const tags = a.identity?.tags || [];
    const levels = resolvePropertyLevels(a, levelConfig);
    const filteredProps = filterPropertiesForCard(a.properties, levels);
    const idx = cardProps.length;
    cardProps.push(filteredProps);
    html += `<div class="card" data-id="${a.id}">
      <div class="card-title">${a.displayName}</div>
      <div class="card-subtitle">${a.id} ${a.embodied ? '(embodied)' : '(disembodied)'}</div>
      <div>${tags.map(t => `<span class="tag">${t}</span>`).join('')}</div>
      <div class="prop-bars" data-card-idx="${idx}"></div>
    </div>`;
  }
  html += '</div>';
  container.innerHTML = html;

  // Render property bars with filtered properties
  container.querySelectorAll('.prop-bars').forEach(el => {
    const idx = parseInt(el.dataset.cardIdx);
    renderPropertyBars(cardProps[idx], el);
  });

  // Click to navigate
  container.querySelectorAll('.card').forEach(card => {
    card.addEventListener('click', () => {
      location.hash = `#/autonomes/${card.dataset.id}`;
    });
  });
}

function renderDetail(container, a, levels) {
  levels = levels || {};
  let html = `<div class="detail-panel">
    <span class="back-link" onclick="location.hash='#/autonomes'">&larr; Back to list</span>
    <div class="detail-header">
      <h2>${a.displayName}</h2>
      <span class="id-label">${a.id}</span>
      <span class="tag">${a.embodied ? 'embodied' : 'disembodied'}</span>
    </div>`;

  // Identity
  if (a.identity) {
    html += `<div class="detail-section">
      <div class="detail-section-title">Identity</div>`;
    if (a.identity.backstory) html += `<p class="flavor">${a.identity.backstory}</p>`;
    if (a.identity.description) html += `<p class="flavor">${a.identity.description}</p>`;
    if (a.identity.motto) html += `<p class="flavor">"${a.identity.motto}"</p>`;
    if (a.identity.tags) html += `<div>${a.identity.tags.map(t => `<span class="tag">${t}</span>`).join('')}</div>`;
    if (a.identity.quirks) html += `<div style="margin-top:6px">${a.identity.quirks.map(q => `<div class="flavor">- ${q}</div>`).join('')}</div>`;
    html += `</div>`;
  }

  // Properties (with level badges in table)
  html += `<div class="detail-section">
    <div class="detail-section-title">Properties</div>
    <div id="detail-prop-bars"></div>
    ${renderPropertyTableWithLevels(a.properties, levels)}
  </div>`;

  // Personality
  if (a.personality && Object.keys(a.personality).length > 0) {
    html += `<div class="detail-section">
      <div class="detail-section-title">Personality</div>`;
    for (const [axis, val] of Object.entries(a.personality)) {
      html += `<div class="personality-bar">
        <span class="label">${axis}</span>
        <div class="track"><div class="fill" style="width:${val * 100}%"></div></div>
        <span class="value">${val.toFixed(2)}</span>
      </div>`;
    }
    html += `</div>`;
  }

  // Action Access
  if (a.actionAccess) {
    const aa = a.actionAccess;
    html += `<div class="detail-section">
      <div class="detail-section-title">Action Access</div>
      <p style="font-size:12px">Allowed: ${aa.allowed?.join(', ') || '-'}</p>
      ${aa.forbidden?.length ? `<p style="font-size:12px">Forbidden: ${aa.forbidden.join(', ')}</p>` : ''}
      ${aa.favorites?.length ? `<p style="font-size:12px">Favorites: ${aa.favorites.join(', ')} (x${aa.favoriteMultiplier})</p>` : ''}
    </div>`;
  }

  // Schedule
  if (a.schedulePreferences) {
    const sp = a.schedulePreferences;
    html += `<div class="detail-section">
      <div class="detail-section-title">Schedule</div>
      <p style="font-size:12px">Wake: ${sp.wakeHour}:00, Sleep: ${sp.sleepHour}:00</p>
      ${sp.workHours ? `<p style="font-size:12px">Work: ${sp.workHours.start}:00 - ${sp.workHours.end}:00</p>` : ''}
    </div>`;
  }

  // Initial Modifiers
  if (a.initialModifiers?.length > 0) {
    html += `<div class="detail-section">
      <div class="detail-section-title">Initial Modifiers</div>`;
    for (const mod of a.initialModifiers) {
      html += `<div class="modifier-card">
        <span class="type-badge">${mod.type}</span>
        ${mod.flavor ? `<span class="flavor">${mod.flavor}</span>` : ''}
        ${mod.actionBonus ? `<div style="font-size:11px;margin-top:4px">Action bonus: ${JSON.stringify(mod.actionBonus)}</div>` : ''}
        ${mod.propertyMod ? `<div style="font-size:11px">Property mod: ${JSON.stringify(mod.propertyMod)}</div>` : ''}
        <div style="font-size:11px;color:var(--text-dim)">Intensity: ${mod.intensity}, Decay: ${mod.decayRate ?? 'none'}</div>
      </div>`;
    }
    html += `</div>`;
  }

  // Eval interval
  html += `<div class="detail-section">
    <div class="detail-section-title">Evaluation</div>
    <p style="font-size:12px">Interval: ${a.evaluationInterval ?? 'none (passive object)'} tick(s)</p>
  </div>`;

  html += `</div>`;
  container.innerHTML = html;

  // Render bars
  const barsEl = container.querySelector('#detail-prop-bars');
  if (barsEl) renderPropertyBars(a.properties, barsEl);
}

const LEVEL_COLORS = {
  vital: '#e06080',
  essential: '#60aadd',
  optional: '#888',
  any: '#666',
};

function renderPropertyTableWithLevels(properties, levels) {
  let html = '<table><tr><th>Property</th><th>Value</th><th>Decay</th><th>Critical</th><th>Range</th><th>Level</th></tr>';
  for (const [id, p] of Object.entries(properties)) {
    const val = typeof p === 'number' ? p : p.value;
    const decay = p.decayRate ?? 0;
    const crit = p.critical != null ? p.critical.toFixed(2) : '-';
    const range = `${p.min ?? 0} - ${p.max ?? 1}`;
    const level = levels[id] || 'any';
    const levelColor = LEVEL_COLORS[level] || '#666';
    html += `<tr>
      <td><span style="color:${getColor(id)}">${id}</span></td>
      <td>${typeof val === 'number' && val < 100 ? val.toFixed(3) : val}</td>
      <td>${decay}</td>
      <td>${crit}</td>
      <td>${range}</td>
      <td><span style="color:${levelColor};font-size:11px;font-weight:600;text-transform:uppercase">${level}</span></td>
    </tr>`;
  }
  html += '</table>';
  return html;
}
