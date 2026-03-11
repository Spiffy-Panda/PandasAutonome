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
            ${runs.map(r => {
              const label = typeof r === 'object' ? `${r.name} [${r.source}]` : r;
              const value = typeof r === 'object' ? r.name : r;
              return `<option value="${value}">${label}</option>`;
            }).join('')}
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

    // Group locations by resource type
    const resourceGroups = {};
    for (const loc of currentData.locations) {
      for (const prop of Object.keys(loc.properties)) {
        if (!resourceGroups[prop]) resourceGroups[prop] = [];
        resourceGroups[prop].push(loc);
      }
    }

    for (const resource of Object.keys(resourceGroups).sort()) {
      const color = supplyColor(resource);
      const displayName = resource.replace(/_/g, ' ');

      // Build display names: "tavern" + abbreviated parent path "ci.do" in light grey
      const locs = [...resourceGroups[resource]].map(loc => {
        const parts = loc.id.split('.');
        const shortName = parts.pop();
        const abbrevPath = parts.map(p => p.substring(0, 2)).join('.');
        const sortKey = shortName + '.' + abbrevPath;
        return { loc, shortName, abbrevPath, sortKey };
      });

      // Sort alphabetically so taverns group together
      locs.sort((a, b) => a.sortKey.localeCompare(b.sortKey));

      listHtml += `<div class="inv-resource-group" style="color:${color}">${displayName}</div>`;

      for (const { loc, shortName, abbrevPath } of locs) {
        const inv = loc.properties[resource];
        const endVal = inv ? inv.endValue.toFixed(0) : '?';
        const delta = inv ? inv.endValue - inv.startValue : 0;
        const deltaColor = delta >= 0 ? '#66cc88' : '#e94560';
        const deltaChar = delta >= 0 ? '▲' : '▼';
        listHtml += `<div class="entity-nav-item ${loc.id === locationId ? 'active' : ''}" data-id="${loc.id}">
          ${shortName} <span class="inv-path-hint">${abbrevPath}</span>
          <span class="entity-nav-count" style="background:${color}20;color:${color}">${endVal} <span style="font-size:8px;color:${deltaColor}">${deltaChar}</span></span>
        </div>`;
      }
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
  await loadRun(typeof runs[0] === 'object' ? runs[0].name : runs[0]);
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
      <div class="chart-container"><canvas class="overview-chart" data-prop="${propName}" width="800" height="280"></canvas></div>
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
        ${inv.decayRate > 0 ? `<div class="stat-card"><div class="stat-value">${inv.decayRate}</div><div class="stat-label">Rate/min</div></div>` : ''}
      </div>`;

    html += renderFlowTable(inv.sources, 'Sources (inflow)', '#66cc88', true, data.totalTicks);
    html += renderFlowTable(inv.sinks, 'Sinks (outflow)', '#e94560', false, data.totalTicks);

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

let _swimlaneId = 0;
const _swimlaneData = {};

function renderFlowTable(entries, title, color, isSource, totalTicks) {
  if (entries.length === 0) {
    return `<p style="font-size:12px;color:var(--text-dim);margin:4px 0">No ${title.toLowerCase()} identified</p>`;
  }

  let html = `<h4 style="color:${color};margin:8px 0 4px">${title}</h4>
    <table>
      <tr><th>Action</th><th>Count</th><th>Per Action</th><th>Total</th></tr>`;

  for (const entry of entries) {
    const isDecay = entry.actionId === '(decay)';
    const hasActors = entry.actors && entry.actors.length > 0;
    const hasTicks = hasActors && entry.actors.some(a => a.ticks && a.ticks.length > 0);
    const total = entry.count * entry.amountPerAction;
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

    // Swimlane foldout
    if (hasActors) {
      const sid = _swimlaneId++;
      _swimlaneData[sid] = { actors: entry.actors, totalTicks, color };

      const laneH = 20;
      const canvasH = Math.max(60, entry.actors.length * laneH + 30);
      html += `<tr class="inv-foldout-content" style="display:none">
        <td colspan="4" style="padding:0">
          <canvas class="inv-swimlane" data-sid="${sid}" width="760" height="${canvasH}"></canvas>
        </td>
      </tr>`;
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
        // Draw swimlane on first open
        const canvas = content.querySelector('.inv-swimlane');
        if (canvas && !canvas.dataset.drawn) {
          drawSwimlane(canvas);
          canvas.dataset.drawn = '1';
        }
      } else {
        content.style.display = 'none';
        if (arrow) arrow.innerHTML = '&#9654;';
      }
    });
  });
}

function drawSwimlane(canvas) {
  const sid = parseInt(canvas.dataset.sid);
  const data = _swimlaneData[sid];
  if (!data) return;

  const { actors, totalTicks, color } = data;
  const ctx = canvas.getContext('2d');
  const w = canvas.width;
  const h = canvas.height;

  const labelW = 150;
  const padR = 12;
  const padTop = 4;
  const laneH = 20;
  const plotW = w - labelW - padR;

  // Background
  ctx.fillStyle = '#16213e';
  ctx.fillRect(0, 0, w, h);

  // Draw lanes
  for (let i = 0; i < actors.length; i++) {
    const actor = actors[i];
    const y = padTop + i * laneH;
    const cy = y + laneH / 2;

    // Alternating lane background
    if (i % 2 === 0) {
      ctx.fillStyle = 'rgba(255,255,255,0.02)';
      ctx.fillRect(labelW, y, plotW, laneH);
    }

    // Lane separator
    ctx.strokeStyle = 'rgba(255,255,255,0.06)';
    ctx.beginPath();
    ctx.moveTo(labelW, y + laneH);
    ctx.lineTo(w - padR, y + laneH);
    ctx.stroke();

    // Entity label
    const shortName = actor.entityId.replace(/^npc_/, '');
    ctx.fillStyle = 'rgba(255,255,255,0.6)';
    ctx.font = '10px monospace';
    ctx.textAlign = 'right';
    ctx.textBaseline = 'middle';
    ctx.fillText(shortName, labelW - 6, cy);

    // Count badge
    ctx.fillStyle = 'rgba(255,255,255,0.25)';
    ctx.font = '9px sans-serif';
    ctx.textAlign = 'left';
    const countStr = `${actor.count}`;
    ctx.fillText(countStr, labelW - 4 - ctx.measureText(shortName).width - ctx.measureText(countStr).width - 6, cy);

    // Draw tick marks
    if (actor.ticks && actor.ticks.length > 0) {
      ctx.fillStyle = color;
      for (const tick of actor.ticks) {
        const x = labelW + (tick / totalTicks) * plotW;
        ctx.beginPath();
        ctx.arc(x, cy, 2.5, 0, Math.PI * 2);
        ctx.fill();
      }
    }
  }

  // Tick axis at bottom
  const axisY = padTop + actors.length * laneH + 2;
  ctx.strokeStyle = 'rgba(255,255,255,0.15)';
  ctx.beginPath();
  ctx.moveTo(labelW, axisY);
  ctx.lineTo(w - padR, axisY);
  ctx.stroke();

  ctx.fillStyle = 'rgba(255,255,255,0.3)';
  ctx.font = '9px sans-serif';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'top';
  const tickStep = Math.max(1, Math.floor(totalTicks / 8));
  for (let t = 0; t <= totalTicks; t += tickStep) {
    const x = labelW + (t / totalTicks) * plotW;
    ctx.fillText(t.toString(), x, axisY + 2);
    // Grid line
    ctx.strokeStyle = 'rgba(255,255,255,0.04)';
    ctx.beginPath();
    ctx.moveTo(x, padTop);
    ctx.lineTo(x, axisY);
    ctx.stroke();
  }
}

// --- Interactive Chart ---

const LINE_PALETTE = [
  '#e94560', '#0db8de', '#66cc88', '#ccbb44',
  '#a066cc', '#e07040', '#cc66aa', '#66bbbb',
  '#4da6ff', '#cc8844', '#88cc66', '#de6080',
];

class InteractiveChart {
  constructor(canvas, lines, pad) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.lines = lines; // [{id, label, color, timeline[{tick, value}]}]
    this.pad = pad || { left: 50, right: 20, top: 10, bottom: 36 };
    this.hiddenIds = new Set();
    this.legendHitboxes = [];

    // Compute full data ranges
    this.dataXRange = this._computeXRange(lines);
    this.dataYRange = this._computeYRange(lines, this.dataXRange);
    this.xRange = { ...this.dataXRange };
    this.yRange = { ...this.dataYRange };

    canvas._chart = this;
    this._bindEvents();
    this.draw();
  }

  _computeXRange(lines) {
    let min = Infinity, max = -Infinity;
    for (const line of lines) {
      for (const s of line.timeline) {
        if (s.tick < min) min = s.tick;
        if (s.tick > max) max = s.tick;
      }
    }
    if (!isFinite(min)) { min = 0; max = 1; }
    return { min, max: Math.max(max, min + 1) };
  }

  _computeYRange(lines, xRange) {
    let max = 0;
    for (const line of lines) {
      if (this.hiddenIds.has(line.id)) continue;
      for (const s of line.timeline) {
        if (s.tick >= xRange.min && s.tick <= xRange.max && s.value > max) max = s.value;
      }
    }
    return { min: 0, max: Math.max(max, 1) };
  }

  _visibleLines() {
    return this.lines.filter(l => !this.hiddenIds.has(l.id));
  }

  _recomputeYFromVisible() {
    this.yRange = this._computeYRange(this.lines, this.xRange);
  }

  _canvasCoords(e) {
    const rect = this.canvas.getBoundingClientRect();
    const scaleX = this.canvas.width / rect.width;
    const scaleY = this.canvas.height / rect.height;
    return {
      x: (e.clientX - rect.left) * scaleX,
      y: (e.clientY - rect.top) * scaleY,
    };
  }

  _bindEvents() {
    this.canvas.style.cursor = 'default';

    this.canvas.addEventListener('click', (e) => {
      const { x, y } = this._canvasCoords(e);
      for (const hb of this.legendHitboxes) {
        if (x >= hb.x && x <= hb.x + hb.w && y >= hb.y && y <= hb.y + hb.h) {
          if (this.hiddenIds.has(hb.id)) {
            this.hiddenIds.delete(hb.id);
          } else {
            // Don't hide the last visible line
            if (this._visibleLines().length > 1) {
              this.hiddenIds.add(hb.id);
            }
          }
          this._recomputeYFromVisible();
          this.draw();
          return;
        }
      }
    });

    this.canvas.addEventListener('mousemove', (e) => {
      const { x, y } = this._canvasCoords(e);
      const pad = this._effectivePad || this.pad;
      const w = this.canvas.width;
      const h = this.canvas.height;
      let cursor = 'default';

      // Over legend items (check first — legend is inside bottom padding)
      for (const hb of this.legendHitboxes) {
        if (x >= hb.x && x <= hb.x + hb.w && y >= hb.y && y <= hb.y + hb.h) {
          cursor = 'pointer';
          break;
        }
      }
      // Over Y-axis area
      if (cursor === 'default' && x < pad.left && y >= pad.top && y <= h - pad.bottom) cursor = 'ns-resize';
      // Over X-axis label strip (just below plot, above legend)
      if (cursor === 'default' && y > h - pad.bottom && y < h - pad.bottom + 18 && x >= pad.left && x <= w - pad.right) cursor = 'ew-resize';

      this.canvas.style.cursor = cursor;
    });

    this.canvas.addEventListener('wheel', (e) => {
      const { x, y } = this._canvasCoords(e);
      const pad = this._effectivePad || this.pad;
      const w = this.canvas.width;
      const h = this.canvas.height;
      const zoomFactor = e.deltaY > 0 ? 1.15 : 1 / 1.15;

      // Y-axis zoom: cursor on Y-axis
      if (x < pad.left && y >= pad.top && y <= h - pad.bottom) {
        e.preventDefault();
        const range = this.yRange.max - this.yRange.min;
        const newRange = range * zoomFactor;
        // Zoom from bottom (min stays 0)
        this.yRange.max = Math.max(this.yRange.min + newRange, 1);
        this.draw();
        return;
      }

      // X-axis zoom: cursor on X-axis label strip
      if (y > h - pad.bottom && y < h - pad.bottom + 18 && x >= pad.left && x <= w - pad.right) {
        e.preventDefault();
        const pw = w - pad.left - pad.right;
        const frac = (x - pad.left) / pw;
        const range = this.xRange.max - this.xRange.min;
        const center = this.xRange.min + frac * range;
        const newRange = range * zoomFactor;
        // Clamp to data bounds
        let newMin = center - frac * newRange;
        let newMax = center + (1 - frac) * newRange;
        if (newMin < this.dataXRange.min) { newMin = this.dataXRange.min; newMax = newMin + newRange; }
        if (newMax > this.dataXRange.max) { newMax = this.dataXRange.max; newMin = newMax - newRange; }
        newMin = Math.max(newMin, this.dataXRange.min);
        newMax = Math.min(newMax, this.dataXRange.max);
        if (newMax - newMin < 10) return; // prevent over-zoom
        this.xRange.min = newMin;
        this.xRange.max = newMax;
        this._recomputeYFromVisible();
        this.draw();
        return;
      }
    }, { passive: false });

    this.canvas.addEventListener('dblclick', () => {
      this.hiddenIds.clear();
      this.xRange = { ...this.dataXRange };
      this.yRange = this._computeYRange(this.lines, this.xRange);
      this.draw();
    });
  }

  draw() {
    const { ctx, canvas } = this;
    const w = canvas.width;
    const h = canvas.height;

    // Pre-compute legend rows to determine effective bottom padding
    ctx.font = '10px sans-serif';
    const legendRowH = 14;
    const legendGap = 14;
    const maxLegendW = w - this.pad.right;
    let legendRows = 1;
    let testX = this.pad.left;
    for (const line of this.lines) {
      const itemW = 10 + ctx.measureText(line.label).width + 4;
      if (testX + itemW > maxLegendW && testX > this.pad.left) {
        legendRows++;
        testX = this.pad.left;
      }
      testX += itemW + legendGap;
    }
    // Bottom padding: base 18px for x-axis labels + legend space
    const legendH = legendRows * legendRowH + 6;
    const pad = { ...this.pad, bottom: Math.max(this.pad.bottom, 18 + legendH) };
    this._effectivePad = pad; // store for event handlers

    const pw = w - pad.left - pad.right;
    const ph = h - pad.top - pad.bottom;
    const { xRange, yRange } = this;
    const xSpan = xRange.max - xRange.min || 1;
    const ySpan = yRange.max - yRange.min || 1;

    // Background
    ctx.fillStyle = '#1a1a2e';
    ctx.fillRect(0, 0, w, h);

    // Y-axis grid
    ctx.strokeStyle = 'rgba(255,255,255,0.06)';
    ctx.fillStyle = 'rgba(255,255,255,0.3)';
    ctx.font = '10px sans-serif';
    ctx.textAlign = 'right';
    ctx.textBaseline = 'middle';
    for (let i = 0; i <= 4; i++) {
      const v = yRange.min + (ySpan * i) / 4;
      const y = pad.top + (1 - i / 4) * ph;
      ctx.beginPath(); ctx.moveTo(pad.left, y); ctx.lineTo(pad.left + pw, y); ctx.stroke();
      ctx.fillText(v.toFixed(0), pad.left - 4, y);
    }

    // X-axis labels
    ctx.textAlign = 'center';
    ctx.textBaseline = 'top';
    const tickStep = Math.max(1, Math.floor(xSpan / 8));
    const startTick = Math.ceil(xRange.min / tickStep) * tickStep;
    for (let t = startTick; t <= xRange.max; t += tickStep) {
      const x = pad.left + ((t - xRange.min) / xSpan) * pw;
      ctx.fillText(t.toFixed(0), x, h - pad.bottom + 4);
    }

    // Clip plot area for lines
    ctx.save();
    ctx.beginPath();
    ctx.rect(pad.left, pad.top, pw, ph);
    ctx.clip();

    // Draw lines
    for (const line of this.lines) {
      const hidden = this.hiddenIds.has(line.id);
      if (hidden) continue;

      ctx.strokeStyle = line.color;
      ctx.lineWidth = 1.5;
      ctx.beginPath();
      let first = true;
      for (const snap of line.timeline) {
        const x = pad.left + ((snap.tick - xRange.min) / xSpan) * pw;
        const y = pad.top + (1 - (snap.value - yRange.min) / ySpan) * ph;
        if (first) { ctx.moveTo(x, y); first = false; }
        else ctx.lineTo(x, y);
      }
      ctx.stroke();

      // Dots
      ctx.fillStyle = line.color;
      for (const snap of line.timeline) {
        const x = pad.left + ((snap.tick - xRange.min) / xSpan) * pw;
        const y = pad.top + (1 - (snap.value - yRange.min) / ySpan) * ph;
        if (x < pad.left - 4 || x > pad.left + pw + 4) continue;
        ctx.beginPath(); ctx.arc(x, y, 2.5, 0, Math.PI * 2); ctx.fill();
      }
    }

    ctx.restore(); // unclip

    // Legend — draw with multi-row wrapping
    this.legendHitboxes = [];
    ctx.font = '10px sans-serif';
    ctx.textBaseline = 'alphabetic';

    let lx = pad.left;
    let currentRow = 0;
    const legendBaseY = h - 6;
    const legendTopY = legendBaseY - (legendRows - 1) * legendRowH;

    for (const line of this.lines) {
      const hidden = this.hiddenIds.has(line.id);
      const label = line.label;
      const textW = ctx.measureText(label).width;
      const itemW = 10 + textW + 4;

      // Wrap to next row
      if (lx + itemW > w - pad.right && lx > pad.left) {
        currentRow++;
        lx = pad.left;
      }

      const ly = legendTopY + currentRow * legendRowH;

      // Color swatch
      ctx.globalAlpha = hidden ? 0.3 : 1;
      ctx.fillStyle = line.color;
      ctx.fillRect(lx, ly - 8, 8, 8);

      // Label
      ctx.fillStyle = 'rgba(255,255,255,0.6)';
      ctx.textAlign = 'left';
      ctx.fillText(label, lx + 10, ly);

      // Strikethrough for hidden
      if (hidden) {
        ctx.strokeStyle = 'rgba(255,255,255,0.4)';
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(lx + 10, ly - 4);
        ctx.lineTo(lx + 10 + textW, ly - 4);
        ctx.stroke();
      }

      ctx.globalAlpha = 1;

      this.legendHitboxes.push({ id: line.id, x: lx, y: ly - legendRowH, w: itemW, h: legendRowH + 4 });
      lx += itemW + 14;
    }
  }
}

function drawOverviewChart(canvas, data, propName) {
  // Collect lines and detect duplicate short names
  const raw = [];
  for (const loc of data.locations) {
    const inv = loc.properties[propName];
    if (!inv) continue;
    const parts = loc.id.split('.');
    const shortName = parts.pop();
    const parent = parts.length > 0 ? parts[parts.length - 1].substring(0, 2) : '';
    raw.push({ id: loc.id, shortName, parent, timeline: inv.timeline });
  }
  // Find names that appear more than once
  const nameCounts = {};
  for (const r of raw) nameCounts[r.shortName] = (nameCounts[r.shortName] || 0) + 1;
  // Build lines with abbreviated parent for duplicates
  const lines = raw.map((r, i) => ({
    id: r.id,
    label: nameCounts[r.shortName] > 1 ? `${r.shortName}·${r.parent}` : r.shortName,
    color: LINE_PALETTE[i % LINE_PALETTE.length],
    timeline: r.timeline,
  }));
  if (lines.length === 0) return;
  new InteractiveChart(canvas, lines);
}

function drawLocationChart(canvas, loc) {
  const props = Object.entries(loc.properties).sort((a, b) => a[0].localeCompare(b[0]));
  if (props.length === 0) return;
  const lines = props.map(([propName, inv]) => ({
    id: propName,
    label: `${propName} (${inv.endValue.toFixed(0)})`,
    color: supplyColor(propName),
    timeline: inv.timeline,
  }));
  new InteractiveChart(canvas, lines, { left: 50, right: 20, top: 10, bottom: 30 });
}
