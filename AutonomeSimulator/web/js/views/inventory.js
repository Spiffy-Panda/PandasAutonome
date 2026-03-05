import { api } from '../api.js';
import { getColor } from '../components/property-bar.js';

const SUPPLY_COLORS = {
  food_supply: '#99cc55',
  ore_supply: '#8899bb',
  tool_supply: '#cc8844',
};

function supplyColor(prop) {
  return SUPPLY_COLORS[prop] || getColor(prop);
}

export async function renderInventory(container, locationId) {
  const runs = await api.analysisRuns();

  if (runs.length === 0) {
    container.innerHTML = '<h2>Inventory</h2><div class="empty-state">No analysis runs found. Run the simulation with --analyze first.</div>';
    return;
  }

  let html = `<h2>Inventory</h2>
    <div class="analysis-layout">
      <div class="analysis-sidebar">
        <div class="detail-section">
          <div class="detail-section-title">Runs</div>
          <select id="inv-run-picker">
            ${runs.map(r => `<option value="${r}">${r}</option>`).join('')}
          </select>
        </div>
        <div class="detail-section">
          <div class="detail-section-title">Locations</div>
          <div id="inv-location-list" class="entity-nav-list"></div>
        </div>
      </div>
      <div class="analysis-content" id="inv-content">
        <div class="empty-state">Loading...</div>
      </div>
    </div>`;

  container.innerHTML = html;

  const picker = container.querySelector('#inv-run-picker');
  const contentEl = container.querySelector('#inv-content');
  const locationListEl = container.querySelector('#inv-location-list');

  let currentData = null;

  async function loadRun(run) {
    contentEl.innerHTML = '<div class="empty-state">Loading...</div>';
    try {
      currentData = await api.analysisInventory(run);
    } catch {
      contentEl.innerHTML = '<div class="empty-state">No inventory data for this run. Re-run simulation with --analyze to generate.</div>';
      return;
    }
    populateLocationList();
    if (locationId) {
      selectLocation(locationId);
    } else {
      showOverview();
    }
  }

  function populateLocationList() {
    if (!currentData) return;

    let listHtml = `<div class="entity-nav-item ${!locationId ? 'active' : ''}" data-id="">Overview</div>`;
    for (const loc of currentData.locations) {
      const shortId = loc.id.split('.').pop();
      const propCount = Object.keys(loc.properties).length;
      listHtml += `<div class="entity-nav-item ${loc.id === locationId ? 'active' : ''}" data-id="${loc.id}">
        ${shortId}
        <span class="entity-nav-count">${propCount}</span>
      </div>`;
    }
    locationListEl.innerHTML = listHtml;

    locationListEl.querySelectorAll('.entity-nav-item').forEach(el => {
      el.addEventListener('click', () => {
        const id = el.dataset.id;
        if (id) {
          location.hash = `#/inventory/${id}`;
        } else {
          location.hash = '#/inventory';
        }
      });
    });
  }

  function selectLocation(id) {
    locationId = id;
    locationListEl.querySelectorAll('.entity-nav-item').forEach(el => {
      el.classList.toggle('active', el.dataset.id === id);
    });
    const loc = currentData.locations.find(l => l.id === id);
    if (loc) {
      renderLocationDetail(contentEl, loc, currentData);
    } else {
      showOverview();
    }
  }

  function showOverview() {
    locationId = '';
    locationListEl.querySelectorAll('.entity-nav-item').forEach(el => {
      el.classList.toggle('active', el.dataset.id === '');
    });
    renderOverview(contentEl, currentData);
  }

  picker.addEventListener('change', () => loadRun(picker.value));
  await loadRun(runs[0]);
}

function renderOverview(container, data) {
  let html = `<div class="analysis-stats">
    <div class="stat-card"><div class="stat-value">${data.totalTicks}</div><div class="stat-label">Ticks</div></div>
    <div class="stat-card"><div class="stat-value">${data.snapshotCount}</div><div class="stat-label">Snapshots</div></div>
    <div class="stat-card"><div class="stat-value">${data.locations.length}</div><div class="stat-label">Locations</div></div>
  </div>`;

  // All-locations stockpile chart per property type
  const allProps = new Set();
  for (const loc of data.locations) {
    for (const prop of Object.keys(loc.properties)) allProps.add(prop);
  }

  for (const propName of [...allProps].sort()) {
    html += `<div class="detail-section">
      <div class="detail-section-title">${propName}</div>
      <div class="chart-container"><canvas class="overview-chart" data-prop="${propName}" width="800" height="240"></canvas></div>
    </div>`;
  }

  // Summary table
  html += `<div class="detail-section">
    <div class="detail-section-title">Location Summary</div>
    <table class="comparison-table">
      <tr><th>Location</th><th>Property</th><th>Start</th><th>End</th><th>Min</th><th>Max</th><th>Sources</th><th>Sinks</th></tr>`;

  for (const loc of data.locations) {
    const props = Object.entries(loc.properties).sort((a, b) => a[0].localeCompare(b[0]));
    for (const [propName, inv] of props) {
      const sourceCount = inv.sources.reduce((s, e) => s + e.count, 0);
      const sinkCount = inv.sinks.reduce((s, e) => s + e.count, 0);
      html += `<tr data-loc-id="${loc.id}" style="cursor:pointer">
        <td class="entity-id-cell">${loc.id}</td>
        <td style="color:${supplyColor(propName)}">${propName}</td>
        <td>${inv.startValue.toFixed(1)}</td>
        <td>${inv.endValue.toFixed(1)}</td>
        <td>${inv.minValue.toFixed(1)}</td>
        <td>${inv.maxValue.toFixed(1)}</td>
        <td>${sourceCount}</td>
        <td>${sinkCount}</td>
      </tr>`;
    }
  }
  html += '</table></div>';

  container.innerHTML = html;

  // Draw overview charts
  container.querySelectorAll('.overview-chart').forEach(canvas => {
    const propName = canvas.dataset.prop;
    drawOverviewChart(canvas, data, propName);
  });

  // Clickable rows
  container.querySelectorAll('tr[data-loc-id]').forEach(row => {
    row.addEventListener('click', () => {
      location.hash = `#/inventory/${row.dataset.locId}`;
    });
  });
}

function renderLocationDetail(container, loc, data) {
  let html = `<div class="entity-detail">
    <div class="detail-header">
      <h2>${loc.id}</h2>
    </div>`;

  const props = Object.entries(loc.properties).sort((a, b) => a[0].localeCompare(b[0]));

  // Stats cards
  html += '<div class="analysis-stats" style="margin-bottom:16px">';
  for (const [propName, inv] of props) {
    const delta = inv.endValue - inv.startValue;
    const sign = delta >= 0 ? '+' : '';
    html += `<div class="stat-card">
      <div class="stat-value" style="color:${supplyColor(propName)}">${inv.endValue.toFixed(1)}</div>
      <div class="stat-label">${propName} <span style="font-size:10px;color:${delta >= 0 ? '#66cc88' : '#e94560'}">(${sign}${delta.toFixed(1)})</span></div>
    </div>`;
  }
  html += '</div>';

  // Stockpile chart for this location
  html += `<div class="detail-section">
    <div class="detail-section-title">Stockpile Timeline</div>
    <div class="chart-container"><canvas id="loc-stockpile-canvas" width="800" height="240"></canvas></div>
  </div>`;

  // Per-property source/sink breakdown
  for (const [propName, inv] of props) {
    html += `<div class="detail-section">
      <div class="detail-section-title" style="color:${supplyColor(propName)}">${propName}</div>
      <div class="analysis-stats" style="margin-bottom:12px">
        <div class="stat-card"><div class="stat-value">${inv.startValue.toFixed(1)}</div><div class="stat-label">Start</div></div>
        <div class="stat-card"><div class="stat-value">${inv.endValue.toFixed(1)}</div><div class="stat-label">End</div></div>
        <div class="stat-card"><div class="stat-value">${inv.minValue.toFixed(1)}</div><div class="stat-label">Min</div></div>
        <div class="stat-card"><div class="stat-value">${inv.maxValue.toFixed(1)}</div><div class="stat-label">Max</div></div>
        ${inv.decayRate > 0 ? `<div class="stat-card"><div class="stat-value">${inv.decayRate}</div><div class="stat-label">Decay/tick</div></div>` : ''}
      </div>`;

    html += renderFlowTable(inv.sources, 'Sources (inflow)', '#66cc88', true);
    html += renderFlowTable(inv.sinks, 'Sinks (outflow)', '#e94560', false);

    html += '</div>';
  }

  html += '</div>';
  container.innerHTML = html;

  // Draw stockpile chart
  const canvas = container.querySelector('#loc-stockpile-canvas');
  if (canvas) {
    drawLocationChart(canvas, loc);
  }

  // Wire foldout toggles
  wireFlowFoldouts(container);
}

function renderFlowTable(entries, title, color, isSource) {
  if (entries.length === 0) {
    return `<p style="font-size:12px;color:var(--text-dim);margin:4px 0">No ${title.toLowerCase()} identified</p>`;
  }

  let html = `<h4 style="color:${color};margin:8px 0 4px">${title}</h4>
    <table>
      <tr><th>Action</th><th>Count</th><th>Per Action</th><th>Total</th></tr>`;

  for (const entry of entries) {
    const isDecay = entry.actionId === '(decay)';
    const hasActors = entry.actors && entry.actors.length > 0;
    const total = isDecay ? entry.count * entry.amountPerAction : entry.count * entry.amountPerAction;
    const totalStr = isDecay
      ? total.toFixed(1)
      : (isSource ? `+${total.toFixed(1)}` : total.toFixed(1));
    const perStr = isDecay
      ? `${entry.amountPerAction.toFixed(4)}/tick`
      : (isSource ? `+${entry.amountPerAction.toFixed(1)}` : entry.amountPerAction.toFixed(1));

    const rowClass = hasActors ? 'class="inv-foldout-toggle" style="cursor:pointer"' : '';
    const arrow = hasActors ? '<span class="inv-arrow">&#9654;</span> ' : '';

    html += `<tr ${rowClass}>
      <td>${arrow}<span class="tag">${entry.actionId}</span></td>
      <td>${isDecay ? '' : entry.count}</td>
      <td>${perStr}</td>
      <td style="color:${color}">${totalStr}</td>
    </tr>`;

    // Hidden foldout row with per-actor breakdown
    if (hasActors) {
      html += `<tr class="inv-foldout-content" style="display:none">
        <td colspan="4" style="padding:0 0 0 24px">
          <table style="margin:4px 0 8px;font-size:11px">
            <tr><th>Entity</th><th>Count</th></tr>`;
      for (const actor of entry.actors) {
        html += `<tr><td>${actor.entityId}</td><td>${actor.count}</td></tr>`;
      }
      html += '</table></td></tr>';
    }
  }
  html += '</table>';
  return html;
}

function wireFlowFoldouts(container) {
  container.querySelectorAll('.inv-foldout-toggle').forEach(row => {
    row.addEventListener('click', () => {
      const content = row.nextElementSibling;
      if (!content || !content.classList.contains('inv-foldout-content')) return;
      const arrow = row.querySelector('.inv-arrow');
      if (content.style.display === 'none') {
        content.style.display = '';
        if (arrow) arrow.innerHTML = '&#9660;';
      } else {
        content.style.display = 'none';
        if (arrow) arrow.innerHTML = '&#9654;';
      }
    });
  });
}

function drawOverviewChart(canvas, data, propName) {
  const ctx = canvas.getContext('2d');
  const w = canvas.width;
  const h = canvas.height;
  const pad = { left: 50, right: 20, top: 10, bottom: 36 };
  const pw = w - pad.left - pad.right;
  const ph = h - pad.top - pad.bottom;

  ctx.fillStyle = '#1a1a2e';
  ctx.fillRect(0, 0, w, h);

  // Collect all locations that have this property
  const locLines = [];
  let globalMax = 0;
  let globalMinTick = Infinity;
  let globalMaxTick = 0;

  for (const loc of data.locations) {
    const inv = loc.properties[propName];
    if (!inv) continue;
    locLines.push({ id: loc.id, timeline: inv.timeline });
    for (const snap of inv.timeline) {
      if (snap.value > globalMax) globalMax = snap.value;
      if (snap.tick < globalMinTick) globalMinTick = snap.tick;
      if (snap.tick > globalMaxTick) globalMaxTick = snap.tick;
    }
  }

  if (locLines.length === 0) return;
  globalMax = Math.max(globalMax, 1);
  const tickRange = globalMaxTick - globalMinTick || 1;

  // Grid
  ctx.strokeStyle = 'rgba(255,255,255,0.06)';
  ctx.fillStyle = 'rgba(255,255,255,0.3)';
  ctx.font = '10px sans-serif';
  ctx.textAlign = 'right';
  for (let i = 0; i <= 4; i++) {
    const v = (globalMax * i) / 4;
    const y = pad.top + (1 - i / 4) * ph;
    ctx.beginPath(); ctx.moveTo(pad.left, y); ctx.lineTo(pad.left + pw, y); ctx.stroke();
    ctx.fillText(v.toFixed(0), pad.left - 4, y + 3);
  }

  // Tick axis
  ctx.textAlign = 'center';
  const tickStep = Math.max(1, Math.floor(tickRange / 8));
  for (let t = globalMinTick; t <= globalMaxTick; t += tickStep) {
    const x = pad.left + ((t - globalMinTick) / tickRange) * pw;
    ctx.fillText(t.toString(), x, h - pad.bottom + 14);
  }

  // Line palette for locations
  const LINE_PALETTE = [
    '#e94560', '#0db8de', '#66cc88', '#ccbb44',
    '#a066cc', '#e07040', '#cc66aa', '#66bbbb',
    '#4da6ff', '#cc8844', '#88cc66', '#de6080',
  ];

  // Draw lines
  for (let li = 0; li < locLines.length; li++) {
    const { timeline } = locLines[li];
    const color = LINE_PALETTE[li % LINE_PALETTE.length];
    ctx.strokeStyle = color;
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    let first = true;
    for (const snap of timeline) {
      const x = pad.left + ((snap.tick - globalMinTick) / tickRange) * pw;
      const y = pad.top + (1 - snap.value / globalMax) * ph;
      if (first) { ctx.moveTo(x, y); first = false; }
      else ctx.lineTo(x, y);
    }
    ctx.stroke();

    // Dots
    ctx.fillStyle = color;
    for (const snap of timeline) {
      const x = pad.left + ((snap.tick - globalMinTick) / tickRange) * pw;
      const y = pad.top + (1 - snap.value / globalMax) * ph;
      ctx.beginPath(); ctx.arc(x, y, 2.5, 0, Math.PI * 2); ctx.fill();
    }
  }

  // Legend
  ctx.font = '10px sans-serif';
  let lx = pad.left;
  const ly = h - 6;
  for (let li = 0; li < locLines.length; li++) {
    const color = LINE_PALETTE[li % LINE_PALETTE.length];
    const label = locLines[li].id.split('.').pop();
    ctx.fillStyle = color;
    ctx.fillRect(lx, ly - 8, 8, 8);
    ctx.fillStyle = 'rgba(255,255,255,0.6)';
    ctx.textAlign = 'left';
    ctx.fillText(label, lx + 10, ly);
    lx += label.length * 6 + 24;
    if (lx > w - 60) { lx = pad.left; /* would wrap but we'll truncate */ break; }
  }
}

function drawLocationChart(canvas, loc) {
  const ctx = canvas.getContext('2d');
  const w = canvas.width;
  const h = canvas.height;
  const pad = { left: 50, right: 20, top: 10, bottom: 30 };
  const pw = w - pad.left - pad.right;
  const ph = h - pad.top - pad.bottom;

  ctx.fillStyle = '#1a1a2e';
  ctx.fillRect(0, 0, w, h);

  const props = Object.entries(loc.properties).sort((a, b) => a[0].localeCompare(b[0]));
  if (props.length === 0) return;

  let globalMax = 0;
  let minTick = Infinity;
  let maxTick = 0;
  for (const [, inv] of props) {
    if (inv.maxValue > globalMax) globalMax = inv.maxValue;
    for (const snap of inv.timeline) {
      if (snap.tick < minTick) minTick = snap.tick;
      if (snap.tick > maxTick) maxTick = snap.tick;
    }
  }
  globalMax = Math.max(globalMax, 1);
  const tickRange = maxTick - minTick || 1;

  // Grid
  ctx.strokeStyle = 'rgba(255,255,255,0.06)';
  ctx.fillStyle = 'rgba(255,255,255,0.3)';
  ctx.font = '10px sans-serif';
  ctx.textAlign = 'right';
  for (let i = 0; i <= 4; i++) {
    const v = (globalMax * i) / 4;
    const y = pad.top + (1 - i / 4) * ph;
    ctx.beginPath(); ctx.moveTo(pad.left, y); ctx.lineTo(pad.left + pw, y); ctx.stroke();
    ctx.fillText(v.toFixed(0), pad.left - 4, y + 3);
  }

  // Tick axis
  ctx.textAlign = 'center';
  const tickStep = Math.max(1, Math.floor(tickRange / 8));
  for (let t = minTick; t <= maxTick; t += tickStep) {
    const x = pad.left + ((t - minTick) / tickRange) * pw;
    ctx.fillText(t.toString(), x, h - 4);
  }

  // Draw lines per property
  for (const [propName, inv] of props) {
    const color = supplyColor(propName);
    ctx.strokeStyle = color;
    ctx.lineWidth = 2;
    ctx.beginPath();
    let first = true;
    for (const snap of inv.timeline) {
      const x = pad.left + ((snap.tick - minTick) / tickRange) * pw;
      const y = pad.top + (1 - snap.value / globalMax) * ph;
      if (first) { ctx.moveTo(x, y); first = false; }
      else ctx.lineTo(x, y);
    }
    ctx.stroke();

    // Dots
    ctx.fillStyle = color;
    for (const snap of inv.timeline) {
      const x = pad.left + ((snap.tick - minTick) / tickRange) * pw;
      const y = pad.top + (1 - snap.value / globalMax) * ph;
      ctx.beginPath(); ctx.arc(x, y, 3, 0, Math.PI * 2); ctx.fill();
    }
  }

  // Legend
  ctx.font = '10px sans-serif';
  let lx = pad.left;
  for (const [propName, inv] of props) {
    const color = supplyColor(propName);
    ctx.fillStyle = color;
    ctx.fillRect(lx, h - 18, 8, 8);
    ctx.fillStyle = 'rgba(255,255,255,0.6)';
    ctx.textAlign = 'left';
    const label = `${propName} (${inv.endValue.toFixed(0)})`;
    ctx.fillText(label, lx + 10, h - 10);
    lx += label.length * 6 + 24;
  }
}
