import { api } from '../api.js';
import { getColor } from '../components/property-bar.js';

const ACTION_COLORS = {
  sustenance: '#e07040',
  social: '#4da6ff',
  rest: '#a066cc',
  work: '#66cc88',
  food_policy: '#ccbb44',
};

function actionColor(actionId) {
  // Try to guess category from common patterns
  if (actionId.includes('eat') || actionId.includes('food')) return ACTION_COLORS.sustenance;
  if (actionId.includes('chat') || actionId.includes('social')) return ACTION_COLORS.social;
  if (actionId.includes('rest') || actionId.includes('sleep')) return ACTION_COLORS.rest;
  if (actionId.includes('forage') || actionId.includes('trade') || actionId.includes('farm') || actionId.includes('plow')) return ACTION_COLORS.work;
  return '#0db8de';
}

export async function renderSimulation(container, dataset) {
  const outputs = await api.outputs();

  if (outputs.length === 0) {
    container.innerHTML = '<h2>Simulation Results</h2><div class="empty-state">No output files found in output/. Run the simulation first.</div>';
    return;
  }

  let html = `<h2>Simulation Results</h2>
    <div style="display:flex;gap:20px">
      <div style="min-width:200px">
        <div class="detail-section">
          <div class="detail-section-title">Output Files</div>
          <ul class="output-list" id="output-list">
            ${outputs.map(f => `<li data-file="${f}">${f}</li>`).join('')}
          </ul>
        </div>
      </div>
      <div style="flex:1" id="sim-content">
        <div class="empty-state">Select an output file to view</div>
      </div>
    </div>`;

  container.innerHTML = html;

  container.querySelectorAll('#output-list li').forEach(li => {
    li.addEventListener('click', async () => {
      container.querySelectorAll('#output-list li').forEach(l => l.classList.remove('active'));
      li.classList.add('active');
      await loadOutput(li.dataset.file, container.querySelector('#sim-content'));
    });
  });

  // Auto-load first
  if (outputs.length > 0) {
    container.querySelector('#output-list li').click();
  }
}

async function loadOutput(filename, contentEl) {
  contentEl.innerHTML = '<div class="empty-state">Loading...</div>';
  const data = await api.output(filename);

  const events = data.actionEvents || [];
  const snapshots = data.snapshots || [];

  // Get unique autonomes and actions
  const autonomeIds = [...new Set(events.map(e => e.autonomeId))];
  const actionIds = [...new Set(events.map(e => e.actionId))];

  let html = `<div class="tabs" id="sim-tabs">
    <button class="tab-btn active" data-tab="timeline">Timeline</button>
    <button class="tab-btn" data-tab="properties">Properties</button>
    <button class="tab-btn" data-tab="scoring">Scoring</button>
    <button class="tab-btn" data-tab="snapshots">Snapshots</button>
  </div>
  <div id="sim-tab-content"></div>`;

  contentEl.innerHTML = html;

  const tabContent = contentEl.querySelector('#sim-tab-content');
  const tabs = contentEl.querySelectorAll('.tab-btn');

  const views = {
    timeline: () => renderTimeline(tabContent, events, autonomeIds),
    properties: () => renderPropertyCharts(tabContent, events, autonomeIds),
    scoring: () => renderScoring(tabContent, events),
    snapshots: () => renderSnapshots(tabContent, snapshots),
  };

  tabs.forEach(btn => {
    btn.addEventListener('click', () => {
      tabs.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      views[btn.dataset.tab]();
    });
  });

  // Default view
  views.timeline();
}

function renderTimeline(container, events, autonomeIds) {
  if (events.length === 0) {
    container.innerHTML = '<div class="empty-state">No events</div>';
    return;
  }

  const maxTick = Math.max(...events.map(e => e.tick));
  const tickScale = Math.max(600, maxTick * 3);
  const laneH = 36;
  const headerH = 24;
  const totalH = headerH + autonomeIds.length * laneH + 20;

  let html = `<div class="timeline-container" style="overflow-x:auto">
    <canvas id="timeline-canvas" width="${tickScale}" height="${totalH}"></canvas>
  </div>
  <div id="event-detail" style="margin-top:12px"></div>`;

  container.innerHTML = html;

  const canvas = container.querySelector('#timeline-canvas');
  const ctx = canvas.getContext('2d');

  // Background
  ctx.fillStyle = '#1a1a2e';
  ctx.fillRect(0, 0, tickScale, totalH);

  // Tick markers
  const tickStep = Math.max(1, Math.floor(maxTick / 20));
  ctx.fillStyle = 'rgba(255,255,255,0.3)';
  ctx.font = '10px sans-serif';
  ctx.textAlign = 'center';
  for (let t = 0; t <= maxTick; t += tickStep) {
    const x = 80 + (t / maxTick) * (tickScale - 100);
    ctx.fillText(t.toString(), x, 14);
    ctx.strokeStyle = 'rgba(255,255,255,0.05)';
    ctx.beginPath(); ctx.moveTo(x, headerH); ctx.lineTo(x, totalH); ctx.stroke();
  }

  // Swim lanes
  for (let i = 0; i < autonomeIds.length; i++) {
    const y = headerH + i * laneH;
    ctx.fillStyle = i % 2 === 0 ? 'rgba(255,255,255,0.02)' : 'rgba(0,0,0,0.1)';
    ctx.fillRect(0, y, tickScale, laneH);

    // Label
    ctx.fillStyle = 'rgba(255,255,255,0.5)';
    ctx.font = '11px sans-serif';
    ctx.textAlign = 'left';
    ctx.fillText(autonomeIds[i].replace('npc_', ''), 4, y + laneH / 2 + 4);
  }

  // Events
  for (const ev of events) {
    const laneIdx = autonomeIds.indexOf(ev.autonomeId);
    if (laneIdx < 0) continue;

    const x = 80 + (ev.tick / maxTick) * (tickScale - 100);
    const y = headerH + laneIdx * laneH + 4;
    const color = actionColor(ev.actionId);

    ctx.fillStyle = color;
    ctx.fillRect(x - 3, y, 6, laneH - 8);

    // Tiny label on hover area
    ctx.fillStyle = 'rgba(255,255,255,0.6)';
    ctx.font = '8px sans-serif';
    ctx.textAlign = 'left';
    ctx.fillText(ev.actionId.substring(0, 8), x + 5, y + laneH / 2);
  }

  // Click detection
  canvas.addEventListener('click', (e) => {
    const rect = canvas.getBoundingClientRect();
    const cx = (e.clientX - rect.left) * (canvas.width / rect.width);
    const cy = (e.clientY - rect.top) * (canvas.height / rect.height);

    // Find closest event
    let closest = null;
    let closestDist = Infinity;
    for (const ev of events) {
      const laneIdx = autonomeIds.indexOf(ev.autonomeId);
      const ex = 80 + (ev.tick / maxTick) * (tickScale - 100);
      const ey = headerH + laneIdx * laneH + laneH / 2;
      const dist = Math.abs(cx - ex) + Math.abs(cy - ey);
      if (dist < closestDist && dist < 30) {
        closest = ev;
        closestDist = dist;
      }
    }

    const detailEl = container.querySelector('#event-detail');
    if (closest) {
      showEventDetail(detailEl, closest);
    }
  });
}

function showEventDetail(el, ev) {
  let html = `<div class="detail-section">
    <div class="detail-section-title">Tick ${ev.tick} - ${ev.gameTime}</div>
    <div style="font-size:13px;margin-bottom:8px">
      <strong>${ev.autonomeId}</strong> chose <strong>${ev.actionId}</strong> (score: ${ev.score.toFixed(3)})
    </div>`;

  // Top candidates
  if (ev.topCandidates?.length > 0) {
    const maxScore = Math.max(...ev.topCandidates.map(c => c.score), 0.001);
    for (const c of ev.topCandidates) {
      const pct = (c.score / maxScore) * 100;
      const isChosen = c.actionId === ev.actionId;
      html += `<div class="score-bar-row">
        <span class="score-bar-name" style="${isChosen ? 'font-weight:600;color:var(--accent2)' : ''}">${c.actionId}</span>
        <div class="score-bar-track">
          <div class="score-bar-fill" style="width:${pct}%;background:${isChosen ? 'var(--accent2)' : actionColor(c.actionId)}"></div>
        </div>
        <span class="score-bar-value">${c.score.toFixed(3)}</span>
      </div>`;
    }
  }

  // Property snapshot
  if (ev.propertySnapshot) {
    html += '<div style="margin-top:10px"><strong style="font-size:11px;color:var(--text-dim)">PROPERTIES AT DECISION</strong></div>';
    for (const [id, val] of Object.entries(ev.propertySnapshot)) {
      const pct = val > 1 ? 100 : val * 100;
      html += `<div class="score-bar-row">
        <span class="score-bar-name" style="color:${getColor(id)}">${id}</span>
        <div class="score-bar-track">
          <div class="score-bar-fill" style="width:${Math.min(pct, 100)}%;background:${getColor(id)}"></div>
        </div>
        <span class="score-bar-value">${typeof val === 'number' && val < 100 ? val.toFixed(3) : val}</span>
      </div>`;
    }
  }

  html += '</div>';
  el.innerHTML = html;
}

function renderPropertyCharts(container, events, autonomeIds) {
  if (events.length === 0) {
    container.innerHTML = '<div class="empty-state">No events</div>';
    return;
  }

  // One chart per autonome
  let html = '';
  for (const aId of autonomeIds) {
    html += `<h3>${aId}</h3><div class="chart-container"><canvas data-autonome="${aId}" width="700" height="200"></canvas></div>`;
  }
  container.innerHTML = html;

  for (const aId of autonomeIds) {
    const canvas = container.querySelector(`canvas[data-autonome="${aId}"]`);
    const aEvents = events.filter(e => e.autonomeId === aId);
    drawPropertyChart(canvas, aEvents);
  }
}

function drawPropertyChart(canvas, events) {
  if (events.length === 0) return;

  const ctx = canvas.getContext('2d');
  const w = canvas.width;
  const h = canvas.height;
  const pad = { left: 50, right: 20, top: 10, bottom: 24 };
  const pw = w - pad.left - pad.right;
  const ph = h - pad.top - pad.bottom;

  ctx.fillStyle = '#1a1a2e';
  ctx.fillRect(0, 0, w, h);

  const maxTick = Math.max(...events.map(e => e.tick));
  const propNames = Object.keys(events[0].propertySnapshot || {}).filter(p => {
    // Only plot 0-1 range properties
    const val = events[0].propertySnapshot[p];
    return val <= 1;
  });

  // Grid
  ctx.strokeStyle = 'rgba(255,255,255,0.06)';
  for (let v = 0; v <= 1; v += 0.25) {
    const y = pad.top + (1 - v) * ph;
    ctx.beginPath(); ctx.moveTo(pad.left, y); ctx.lineTo(pad.left + pw, y); ctx.stroke();
    ctx.fillStyle = 'rgba(255,255,255,0.3)';
    ctx.font = '10px sans-serif';
    ctx.textAlign = 'right';
    ctx.fillText(v.toFixed(1), pad.left - 4, y + 3);
  }

  // Axis labels
  ctx.fillStyle = 'rgba(255,255,255,0.3)';
  ctx.font = '10px sans-serif';
  ctx.textAlign = 'center';
  ctx.fillText('0', pad.left, h - 4);
  ctx.fillText(maxTick.toString(), pad.left + pw, h - 4);

  // Draw lines with dots
  for (const propName of propNames) {
    const color = getColor(propName);
    ctx.strokeStyle = color;
    ctx.lineWidth = 2;
    ctx.beginPath();

    const points = [];
    let first = true;
    for (const ev of events) {
      const val = ev.propertySnapshot[propName];
      if (val == null) continue;
      const x = pad.left + (ev.tick / maxTick) * pw;
      const y = pad.top + (1 - Math.min(val, 1)) * ph;
      points.push({ x, y });
      if (first) { ctx.moveTo(x, y); first = false; }
      else ctx.lineTo(x, y);
    }
    ctx.stroke();

    // Draw dots at data points (sample every N to avoid clutter)
    const step = Math.max(1, Math.floor(points.length / 20));
    ctx.fillStyle = color;
    for (let i = 0; i < points.length; i += step) {
      ctx.beginPath();
      ctx.arc(points[i].x, points[i].y, 3, 0, Math.PI * 2);
      ctx.fill();
    }
    // Always draw last point
    if (points.length > 0) {
      const last = points[points.length - 1];
      ctx.beginPath();
      ctx.arc(last.x, last.y, 3, 0, Math.PI * 2);
      ctx.fill();
    }
  }

  // Legend
  let lx = pad.left;
  ctx.font = '10px sans-serif';
  for (const propName of propNames) {
    ctx.fillStyle = getColor(propName);
    ctx.fillRect(lx, h - 12, 8, 8);
    ctx.fillStyle = 'rgba(255,255,255,0.6)';
    ctx.textAlign = 'left';
    ctx.fillText(propName, lx + 10, h - 4);
    lx += propName.length * 6 + 24;
  }
}

function renderScoring(container, events) {
  if (events.length === 0) {
    container.innerHTML = '<div class="empty-state">No events</div>';
    return;
  }

  let html = '<p style="font-size:12px;color:var(--text-dim);margin-bottom:12px">Click an event to view scoring breakdown</p>';
  html += '<table><tr><th>Tick</th><th>Time</th><th>Autonome</th><th>Action</th><th>Score</th></tr>';
  for (const ev of events.slice(0, 100)) {
    html += `<tr data-idx="${events.indexOf(ev)}" style="cursor:pointer">
      <td>${ev.tick}</td>
      <td>${ev.gameTime}</td>
      <td>${ev.autonomeId}</td>
      <td>${ev.actionId}</td>
      <td>${ev.score.toFixed(3)}</td>
    </tr>`;
  }
  html += '</table>';
  if (events.length > 100) html += `<p style="font-size:12px;color:var(--text-dim)">Showing first 100 of ${events.length} events</p>`;
  html += '<div id="scoring-detail" style="margin-top:12px"></div>';
  container.innerHTML = html;

  container.querySelectorAll('tr[data-idx]').forEach(row => {
    row.addEventListener('click', () => {
      const ev = events[parseInt(row.dataset.idx)];
      showEventDetail(container.querySelector('#scoring-detail'), ev);
    });
  });
}

function renderSnapshots(container, snapshots) {
  if (snapshots.length === 0) {
    container.innerHTML = '<div class="empty-state">No snapshots</div>';
    return;
  }

  let html = '<div style="display:flex;gap:8px;margin-bottom:12px;flex-wrap:wrap">';
  for (let i = 0; i < snapshots.length; i++) {
    html += `<button class="tab-btn ${i === 0 ? 'active' : ''}" data-snap="${i}">Tick ${snapshots[i].tick}</button>`;
  }
  html += '</div><div id="snap-content"></div>';
  container.innerHTML = html;

  const showSnap = (idx) => {
    const snap = snapshots[idx];
    const entities = snap.entityProperties;
    const allProps = new Set();
    Object.values(entities).forEach(props => Object.keys(props).forEach(k => allProps.add(k)));

    let t = `<p style="font-size:12px;color:var(--text-dim);margin-bottom:8px">${snap.gameTime}</p>`;
    t += '<table><tr><th>Entity</th>';
    for (const p of allProps) t += `<th style="color:${getColor(p)}">${p}</th>`;
    t += '</tr>';
    for (const [entityId, props] of Object.entries(entities)) {
      t += `<tr><td style="font-family:monospace;font-size:12px">${entityId}</td>`;
      for (const p of allProps) {
        const val = props[p];
        if (val != null) {
          const display = val < 100 ? val.toFixed(3) : val.toFixed(0);
          t += `<td>${display}</td>`;
        } else {
          t += '<td>-</td>';
        }
      }
      t += '</tr>';
    }
    t += '</table>';
    container.querySelector('#snap-content').innerHTML = t;
  };

  container.querySelectorAll('button[data-snap]').forEach(btn => {
    btn.addEventListener('click', () => {
      container.querySelectorAll('button[data-snap]').forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      showSnap(parseInt(btn.dataset.snap));
    });
  });

  showSnap(0);
}
