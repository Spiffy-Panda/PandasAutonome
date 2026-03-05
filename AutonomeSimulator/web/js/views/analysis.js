import { api } from '../api.js';
import { getColor } from '../components/property-bar.js';

// Palette for action breakdown bars
const ACTION_PALETTE = [
  '#e94560', '#0db8de', '#66cc88', '#ccbb44',
  '#a066cc', '#e07040', '#cc66aa', '#66bbbb',
  '#4da6ff', '#cc8844', '#88cc66', '#de6080',
];

// Day/night band rendering helpers
function drawNightBands(ctx, minTick, maxTick, tickRange, pad, pw, bodyTop, bodyH, minutesPerTick) {
  if (!minutesPerTick) return;
  const ticksPerDay = Math.round(24 * 60 / minutesPerTick);
  const nightStart = Math.round(20 * 60 / minutesPerTick);
  const nightEnd = Math.round(5 * 60 / minutesPerTick);
  const firstDay = Math.floor(minTick / ticksPerDay);
  const lastDay = Math.floor(maxTick / ticksPerDay);

  ctx.fillStyle = 'rgba(0, 10, 40, 0.35)';
  for (let d = firstDay; d <= lastDay + 1; d++) {
    const base = d * ticksPerDay;
    drawBand(ctx, base, base + nightEnd, minTick, maxTick, tickRange, pad, pw, bodyTop, bodyH);
    drawBand(ctx, base + nightStart, base + ticksPerDay, minTick, maxTick, tickRange, pad, pw, bodyTop, bodyH);
  }
}

function drawBand(ctx, start, end, minTick, maxTick, tickRange, pad, pw, bodyTop, bodyH) {
  const s = Math.max(start, minTick);
  const e = Math.min(end, maxTick);
  if (s >= e) return;
  const x0 = pad.left + ((s - minTick) / tickRange) * pw;
  const x1 = pad.left + ((e - minTick) / tickRange) * pw;
  ctx.fillRect(x0, bodyTop, x1 - x0, bodyH);
}

// Persistent state across entity selections
let _filter = 'all';
let _scrollTop = 0;
let _swimlaneOn = false;
let _topSection = null;

export async function renderAnalysis(container, entityId, dataset) {
  const runs = await api.analysisRuns();

  if (runs.length === 0) {
    container.innerHTML = '<h2>Analysis</h2><div class="empty-state">No analysis runs found. Run the simulation with --analyze first.</div>';
    return;
  }

  // Load profile/relationship metadata for sorting and memories
  let profileMap = {};
  let orgMemberships = {};
  if (dataset) {
    try {
      const [profiles, relationships] = await Promise.all([
        api.autonomes(dataset),
        api.relationships(dataset),
      ]);
      for (const p of profiles) {
        profileMap[p.id] = p;
      }
      // Build org membership map: entityId -> [orgIds]
      for (const r of relationships) {
        if (r.tags?.includes('authority') && r.source && r.target) {
          if (!orgMemberships[r.target]) orgMemberships[r.target] = [];
          orgMemberships[r.target].push(r.source);
        }
      }
    } catch (e) {
      console.warn('Could not load profile metadata:', e);
    }
  }

  let html = `<h2>Analysis</h2>
    <div class="analysis-layout">
      <div class="analysis-sidebar">
        <div class="detail-section">
          <div class="detail-section-title">Runs</div>
          <select id="run-picker">
            ${runs.map(r => `<option value="${r}">${r}</option>`).join('')}
          </select>
        </div>
        <div class="detail-section">
          <div class="detail-section-title">Entities</div>
          <div id="entity-filter" style="margin-bottom:6px">
            <label style="font-size:11px;margin-right:8px;cursor:pointer">
              <input type="radio" name="entity-type" value="all" ${_filter === 'all' ? 'checked' : ''}> All
            </label>
            <label style="font-size:11px;margin-right:8px;cursor:pointer">
              <input type="radio" name="entity-type" value="unembodied" ${_filter === 'unembodied' ? 'checked' : ''}> Org
            </label>
            <label style="font-size:11px;cursor:pointer">
              <input type="radio" name="entity-type" value="embodied" ${_filter === 'embodied' ? 'checked' : ''}> NPC
            </label>
          </div>
          <div id="entity-list" class="entity-nav-list"></div>
        </div>
      </div>
      <div class="analysis-content" id="analysis-content">
        <div class="empty-state">Loading...</div>
      </div>
    </div>`;

  container.innerHTML = html;

  const picker = container.querySelector('#run-picker');
  const contentEl = container.querySelector('#analysis-content');
  const entityListEl = container.querySelector('#entity-list');
  const filterRadios = container.querySelectorAll('input[name="entity-type"]');

  // Track which section is near the top as user scrolls
  contentEl.addEventListener('scroll', () => {
    _topSection = saveTopSection(contentEl);
  }, { passive: true });

  let currentData = null;
  let currentSimData = null;
  let currentFilter = _filter;

  function getOccupation(id) {
    const profile = profileMap[id];
    if (!profile) return '';
    const tags = profile.identity?.tags || [];
    return tags[0] || '';
  }

  function getOrgKey(id) {
    const orgs = orgMemberships[id];
    if (!orgs || orgs.length === 0) return '';
    return orgs.sort().join('+');
  }

  function getDisplayName(id) {
    const profile = profileMap[id];
    return profile?.displayName || id.replace(/^(npc_|town_|guild_)/, '');
  }

  function sortEntities(entities) {
    return [...entities].sort((a, b) => {
      // 1. Org membership (treat dual membership as separate group)
      const orgA = getOrgKey(a.id);
      const orgB = getOrgKey(b.id);
      if (orgA !== orgB) return orgA.localeCompare(orgB);
      // 2. Occupation
      const occA = getOccupation(a.id);
      const occB = getOccupation(b.id);
      if (occA !== occB) return occA.localeCompare(occB);
      // 3. Name
      return getDisplayName(a.id).localeCompare(getDisplayName(b.id));
    });
  }

  async function loadRun(run) {
    contentEl.innerHTML = '<div class="empty-state">Loading...</div>';
    currentData = await api.analysisReport(run);
    // Try to load simulation result for timeline rendering
    try { currentSimData = await api.analysisSimulation(run); } catch { currentSimData = null; }
    populateEntityList();
    if (entityId) {
      selectEntity(entityId);
    } else {
      showOverview();
    }
  }

  function populateEntityList() {
    if (!currentData) return;
    let entities = currentData.entities;
    if (currentFilter === 'embodied') entities = entities.filter(e => e.embodied);
    else if (currentFilter === 'unembodied') entities = entities.filter(e => !e.embodied);

    entities = sortEntities(entities);

    let html = `<div class="entity-nav-item ${!entityId ? 'active' : ''}" data-id="">Overview</div>`;
    let lastOrg = null;
    for (const e of entities) {
      const orgKey = getOrgKey(e.id);
      // Show org separator for embodied entities
      if (e.embodied && orgKey !== lastOrg) {
        lastOrg = orgKey;
        const orgLabel = orgKey || 'unaffiliated';
        html += `<div class="entity-nav-separator">${orgLabel}</div>`;
      }
      const label = getDisplayName(e.id);
      const badge = e.embodied ? '' : '<span class="entity-org-badge">org</span>';
      html += `<div class="entity-nav-item ${e.id === entityId ? 'active' : ''}" data-id="${e.id}">
        ${badge}${label}
        <span class="entity-nav-count">${e.totalActions}</span>
      </div>`;
    }
    entityListEl.innerHTML = html;

    // Restore scroll position
    entityListEl.scrollTop = _scrollTop;

    entityListEl.querySelectorAll('.entity-nav-item').forEach(el => {
      el.addEventListener('click', () => {
        // Save scroll position before navigation
        _scrollTop = entityListEl.scrollTop;
        const id = el.dataset.id;
        if (id) {
          location.hash = `#/analysis/${id}`;
        } else {
          location.hash = '#/analysis';
        }
      });
    });
  }

  function selectEntity(id) {
    entityId = id;
    entityListEl.querySelectorAll('.entity-nav-item').forEach(el => {
      el.classList.toggle('active', el.dataset.id === id);
    });
    const entity = currentData.entities.find(e => e.id === id);
    if (entity) {
      renderEntityDetail(contentEl, entity, currentData, profileMap, currentSimData);
    } else {
      showOverview();
    }
  }

  function showOverview() {
    entityId = '';
    entityListEl.querySelectorAll('.entity-nav-item').forEach(el => {
      el.classList.toggle('active', el.dataset.id === '');
    });
    renderOverview(contentEl, currentData);
  }

  picker.addEventListener('change', () => loadRun(picker.value));

  filterRadios.forEach(r => {
    r.addEventListener('change', () => {
      currentFilter = r.value;
      _filter = r.value;
      populateEntityList();
    });
  });

  await loadRun(runs[0]);
}

function renderOverview(container, data) {
  let html = `<div class="analysis-stats">
    <div class="stat-card"><div class="stat-value">${data.totalTicks}</div><div class="stat-label">Ticks</div></div>
    <div class="stat-card"><div class="stat-value">${data.totalActionEvents}</div><div class="stat-label">Actions</div></div>
    <div class="stat-card"><div class="stat-value">${data.totalSnapshots}</div><div class="stat-label">Snapshots</div></div>
    <div class="stat-card"><div class="stat-value">${data.embodiedCount}</div><div class="stat-label">Embodied</div></div>
    <div class="stat-card"><div class="stat-value">${data.unembodiedCount}</div><div class="stat-label">Unembodied</div></div>
  </div>`;

  const unembodied = data.entities.filter(e => !e.embodied);
  const embodied = data.entities.filter(e => e.embodied);

  if (unembodied.length > 0) {
    html += '<h3>Unembodied Autonomes</h3>';
    html += renderComparisonTable(unembodied);
  }

  if (embodied.length > 0) {
    html += '<h3>Embodied Autonomes</h3>';
    html += renderComparisonTable(embodied);
  }

  container.innerHTML = html;

  // Make rows clickable
  container.querySelectorAll('tr[data-entity-id]').forEach(row => {
    row.addEventListener('click', () => {
      location.hash = `#/analysis/${row.dataset.entityId}`;
    });
  });
}

function renderComparisonTable(entities) {
  let html = `<table class="comparison-table">
    <tr>
      <th>Entity</th>
      <th>Acts</th>
      <th>Uniq</th>
      <th>Avg Score</th>
      <th>StdDev</th>
      <th>Avg Margin</th>
      <th>Close</th>
      <th>Dominant Action</th>
      <th>Dom%</th>
    </tr>`;

  for (const e of entities.sort((a, b) => b.totalActions - a.totalActions)) {
    const dominant = e.actionBreakdown[0];
    const stdDev = e.stdDevScore != null ? e.stdDevScore.toFixed(3) : '-';
    html += `<tr data-entity-id="${e.id}" style="cursor:pointer">
      <td class="entity-id-cell">${e.id}</td>
      <td>${e.totalActions}</td>
      <td>${e.uniqueActions}</td>
      <td>${e.avgScore.toFixed(3)}</td>
      <td>${stdDev}</td>
      <td>${e.avgMargin.toFixed(3)}</td>
      <td>${e.closeCallCount}</td>
      <td><span class="tag">${dominant?.actionId || '-'}</span></td>
      <td>${dominant?.percentage?.toFixed(1) || '0'}%</td>
    </tr>`;
  }
  html += '</table>';
  return html;
}

function renderEntityDetail(container, entity, data, profileMap, simData) {
  const profile = profileMap?.[entity.id];

  // Extract action events for this entity from simulation data
  const entityEvents = simData?.actionEvents?.filter(e => e.autonomeId === entity.id) || [];
  const mpt = simData?.minutesPerTick || 0;

  let html = `<div class="entity-detail">
    <div class="detail-header">
      <h2>${profile?.displayName || entity.id}</h2>
      <span class="tag">${entity.embodied ? 'embodied' : 'unembodied'}</span>
    </div>

    <div class="analysis-stats" style="margin-bottom:16px">
      <div class="stat-card"><div class="stat-value">${entity.totalActions}</div><div class="stat-label">Actions</div></div>
      <div class="stat-card"><div class="stat-value">${entity.uniqueActions}</div><div class="stat-label">Unique</div></div>
      <div class="stat-card"><div class="stat-value">${entity.firstTick}-${entity.lastTick}</div><div class="stat-label">Tick Range</div></div>
      <div class="stat-card"><div class="stat-value">${entity.avgScore.toFixed(3)}</div><div class="stat-label">Avg Score</div></div>
      <div class="stat-card"><div class="stat-value">${entity.stdDevScore != null ? entity.stdDevScore.toFixed(3) : '-'}</div><div class="stat-label">Score StdDev</div></div>
      <div class="stat-card"><div class="stat-value">${entity.closeCallCount}</div><div class="stat-label">Close Calls</div></div>
    </div>`;

  // Action Breakdown
  html += renderActionBreakdown(entity);

  // Action Timeline (needs simulation data)
  const hasLocationData = entityEvents.some(e => e.location);
  if (entityEvents.length > 0) {
    html += `<div class="detail-section">
      <div class="detail-section-title" style="display:flex;align-items:center;gap:12px">
        Action Timeline
        ${hasLocationData ? `<label style="font-size:11px;cursor:pointer;display:flex;align-items:center;gap:4px;font-weight:400;text-transform:none;letter-spacing:0;color:var(--text-dim)">
          <input type="checkbox" id="timeline-swimlane-toggle"> Location swimlanes
        </label>` : ''}
      </div>
      <div style="overflow-x:auto">
        <canvas id="entity-timeline-canvas" width="900" height="60"></canvas>
        <canvas id="entity-swimlane-canvas" width="900" height="0" style="display:none"></canvas>
      </div>
      <div id="entity-timeline-legend" style="margin-top:6px;display:flex;flex-wrap:wrap;gap:6px 12px"></div>
    </div>`;
  }

  // Score Trajectory by Quarter
  html += renderQuarterTrajectory(entity);

  // Property Trajectory chart
  html += `<div class="detail-section">
    <div class="detail-section-title">Property Trajectory</div>
    <div class="chart-container"><canvas id="prop-trajectory-canvas" width="700" height="220"></canvas></div>
  </div>`;

  // Property Changes table
  html += renderPropertyChanges(entity);

  // Memories section
  html += renderMemories(profile);

  // Decision Margins & Close Calls
  html += renderDecisionMargins(entity);

  // Runner-Up Analysis
  html += renderRunnerUps(entity);

  // Consecutive Runs
  html += renderConsecutiveRuns(entity);

  // Critical Alerts
  html += renderCriticalAlerts(entity);

  html += '</div>';
  container.innerHTML = html;

  // Draw property trajectory chart
  const canvas = container.querySelector('#prop-trajectory-canvas');
  if (canvas && entity.propertyTrajectory.length > 0) {
    drawTrajectoryChart(canvas, entity);
  }

  // Draw action timeline
  const timelineCanvas = container.querySelector('#entity-timeline-canvas');
  const swimlaneCanvas = container.querySelector('#entity-swimlane-canvas');
  const timelineLegend = container.querySelector('#entity-timeline-legend');
  if (timelineCanvas && entityEvents.length > 0) {
    drawEntityTimeline(timelineCanvas, timelineLegend, entityEvents, entity, mpt);
  }

  // Wire up swimlane toggle
  const swimToggle = container.querySelector('#timeline-swimlane-toggle');
  if (swimToggle && timelineCanvas && swimlaneCanvas) {
    swimToggle.addEventListener('change', () => {
      _swimlaneOn = swimToggle.checked;
      if (swimToggle.checked) {
        timelineCanvas.style.display = 'none';
        swimlaneCanvas.style.display = 'block';
        drawLocationSwimlane(swimlaneCanvas, timelineLegend, entityEvents, entity, mpt);
      } else {
        swimlaneCanvas.style.display = 'none';
        timelineCanvas.style.display = 'block';
        drawEntityTimeline(timelineCanvas, timelineLegend, entityEvents, entity, mpt);
      }
    });

    // Restore swimlane toggle state
    if (_swimlaneOn) {
      swimToggle.checked = true;
      timelineCanvas.style.display = 'none';
      swimlaneCanvas.style.display = 'block';
      drawLocationSwimlane(swimlaneCanvas, timelineLegend, entityEvents, entity, mpt);
    }
  }

  // Restore scroll to saved section
  restoreTopSection(container, _topSection);
}

function renderMemories(profile) {
  if (!profile) return '';
  const modifiers = profile.initialModifiers || [];
  if (modifiers.length === 0) return '';

  let html = `<div class="detail-section">
    <div class="detail-section-title">Memories &amp; Modifiers</div>`;

  for (const mod of modifiers) {
    const typeColor = mod.type === 'memory' ? '#ccbb44'
      : mod.type === 'directive' ? '#e94560'
      : mod.type === 'trait' ? '#66cc88'
      : '#0db8de';
    html += `<div class="modifier-card">
      <span class="type-badge" style="background:${typeColor}">${mod.type || 'modifier'}</span>`;
    if (mod.flavor) {
      html += `<span class="flavor">${mod.flavor}</span>`;
    }
    if (mod.actionBonus && Object.keys(mod.actionBonus).length > 0) {
      html += '<div style="margin-top:4px;font-size:11px;color:var(--text-dim)">';
      for (const [action, bonus] of Object.entries(mod.actionBonus)) {
        const sign = bonus >= 0 ? '+' : '';
        html += `<span class="tag" style="font-size:10px">${action} ${sign}${bonus}</span> `;
      }
      html += '</div>';
    }
    if (mod.propertyMod && Object.keys(mod.propertyMod).length > 0) {
      html += '<div style="margin-top:4px;font-size:11px;color:var(--text-dim)">';
      for (const [prop, val] of Object.entries(mod.propertyMod)) {
        const sign = val >= 0 ? '+' : '';
        html += `<span class="tag" style="font-size:10px;color:${getColor(prop)}">${prop} ${sign}${val}</span> `;
      }
      html += '</div>';
    }
    const details = [];
    if (mod.intensity != null && mod.intensity !== 1) details.push(`intensity: ${mod.intensity}`);
    if (mod.decayRate != null && mod.decayRate > 0) details.push(`decay: ${mod.decayRate}/tick`);
    if (mod.duration != null && mod.duration > 0) details.push(`duration: ${mod.duration} ticks`);
    if (details.length > 0) {
      html += `<div style="margin-top:4px;font-size:10px;color:var(--text-dim)">${details.join(' | ')}</div>`;
    }
    html += '</div>';
  }
  html += '</div>';
  return html;
}

function renderActionBreakdown(entity) {
  let html = `<div class="detail-section">
    <div class="detail-section-title">Action Breakdown</div>`;

  for (let i = 0; i < entity.actionBreakdown.length; i++) {
    const a = entity.actionBreakdown[i];
    const color = ACTION_PALETTE[i % ACTION_PALETTE.length];
    html += `<div class="score-bar-row">
      <span class="score-bar-name">${a.actionId}</span>
      <div class="score-bar-track">
        <div class="score-bar-fill" style="width:${a.percentage}%;background:${color}"></div>
      </div>
      <span class="score-bar-value">${a.count} (${a.percentage.toFixed(1)}%)</span>
    </div>`;
  }
  html += '</div>';
  return html;
}

function renderQuarterTrajectory(entity) {
  if (entity.scoreByQuarter.length === 0) return '';

  let html = `<div class="detail-section">
    <div class="detail-section-title">Score Trajectory by Quarter</div>
    <table>
      <tr><th>Quarter</th><th>n</th><th>Avg</th><th>Min</th><th>Max</th><th>Trend</th></tr>`;

  for (const q of entity.scoreByQuarter) {
    let trendClass = 'trend-stable';
    let trendText = `${q.trendDelta >= 0 ? '+' : ''}${q.trendDelta.toFixed(3)}`;
    if (q.trendDelta > 0.02) { trendClass = 'trend-rising'; trendText = 'RISING ' + trendText; }
    else if (q.trendDelta < -0.02) { trendClass = 'trend-falling'; trendText = 'FALLING ' + trendText; }
    else { trendText = 'STABLE ' + trendText; }

    html += `<tr>
      <td style="font-size:12px">${q.label}</td>
      <td>${q.count}</td>
      <td>${q.avgScore.toFixed(3)}</td>
      <td>${q.minScore.toFixed(3)}</td>
      <td>${q.maxScore.toFixed(3)}</td>
      <td><span class="${trendClass}">${trendText}</span></td>
    </tr>`;
  }
  html += '</table></div>';
  return html;
}

function renderPropertyChanges(entity) {
  const props = Object.keys(entity.propertyDeltas).sort();
  if (props.length === 0) return '';

  let html = `<div class="detail-section">
    <div class="detail-section-title">Property Changes (first &rarr; last)</div>
    <table>
      <tr><th>Property</th><th>First</th><th>Last</th><th>Delta</th></tr>`;

  for (const prop of props) {
    const first = entity.firstProperties[prop] ?? 0;
    const last = entity.lastProperties[prop] ?? 0;
    const delta = entity.propertyDeltas[prop];
    const deltaClass = delta > 0 ? 'delta-positive' : delta < 0 ? 'delta-negative' : '';

    html += `<tr>
      <td style="color:${getColor(prop)}">${prop}</td>
      <td>${fmtProp(first)}</td>
      <td>${fmtProp(last)}</td>
      <td class="${deltaClass}">${delta >= 0 ? '+' : ''}${fmtProp(delta)}</td>
    </tr>`;
  }
  html += '</table></div>';
  return html;
}

function renderDecisionMargins(entity) {
  let html = `<div class="detail-section">
    <div class="detail-section-title">Decision Margins</div>
    <div class="analysis-stats" style="margin-bottom:12px">
      <div class="stat-card"><div class="stat-value">${entity.avgMargin.toFixed(4)}</div><div class="stat-label">Avg Margin</div></div>
      <div class="stat-card"><div class="stat-value">${entity.minMargin.toFixed(4)}</div><div class="stat-label">Min</div></div>
      <div class="stat-card"><div class="stat-value">${entity.maxMargin.toFixed(4)}</div><div class="stat-label">Max</div></div>
      <div class="stat-card"><div class="stat-value">${entity.closeCallCount}</div><div class="stat-label">Close Calls</div></div>
    </div>`;

  if (entity.closeCalls.length > 0) {
    html += `<table>
      <tr><th>Tick</th><th>Winner</th><th>Score</th><th>Runner-Up</th><th>Score</th><th>Margin</th></tr>`;
    for (const cc of entity.closeCalls.slice(0, 10)) {
      html += `<tr>
        <td>${cc.tick}</td>
        <td><span class="tag">${cc.winnerAction}</span></td>
        <td>${cc.winnerScore.toFixed(4)}</td>
        <td>${cc.runnerUpAction}</td>
        <td>${cc.runnerUpScore.toFixed(4)}</td>
        <td style="color:var(--accent)">${cc.margin.toFixed(6)}</td>
      </tr>`;
    }
    html += '</table>';
  }
  html += '</div>';
  return html;
}

function renderRunnerUps(entity) {
  if (entity.runnerUps.length === 0) return '';

  let html = `<div class="detail-section">
    <div class="detail-section-title">Runner-Up Analysis</div>
    <table>
      <tr><th>Action</th><th>As Candidate</th><th>Times Chosen</th><th>Win Rate</th></tr>`;

  for (const ru of entity.runnerUps.slice(0, 10)) {
    const total = ru.timesAsCandidate + ru.timesChosen;
    const winRate = total > 0 ? (ru.timesChosen / total * 100).toFixed(1) : '0.0';
    html += `<tr>
      <td>${ru.actionId}</td>
      <td>${ru.timesAsCandidate}</td>
      <td>${ru.timesChosen}</td>
      <td>${winRate}%</td>
    </tr>`;
  }
  html += '</table></div>';
  return html;
}

function renderConsecutiveRuns(entity) {
  if (entity.consecutiveRuns.length === 0) return '';

  let html = `<div class="detail-section">
    <div class="detail-section-title">Consecutive Action Runs (2+)</div>
    <table>
      <tr><th>Action</th><th>Length</th><th>Ticks</th></tr>`;

  for (const run of entity.consecutiveRuns.slice(0, 10)) {
    html += `<tr>
      <td>${run.actionId}</td>
      <td>${run.length}x</td>
      <td>${run.startTick}-${run.endTick}</td>
    </tr>`;
  }
  html += '</table></div>';
  return html;
}

function renderCriticalAlerts(entity) {
  if (entity.criticalAlertTicks === 0) return '';

  let html = `<div class="detail-section">
    <div class="detail-section-title">Critical Property Alerts</div>
    <p style="font-size:12px;color:var(--text-dim);margin-bottom:8px">${entity.criticalAlertTicks} ticks with zero-value properties</p>`;

  if (entity.criticalAlerts.length > 0) {
    html += `<table>
      <tr><th>Tick</th><th>Zero Properties</th><th>Action Chosen</th></tr>`;
    for (const alert of entity.criticalAlerts.slice(0, 10)) {
      const alerts = alert.alerts.map(a => `<span class="alert-badge">${a.replace('_ZERO', '')}</span>`).join(' ');
      html += `<tr>
        <td>${alert.tick}</td>
        <td>${alerts}</td>
        <td>${alert.actionChosen}</td>
      </tr>`;
    }
    html += '</table>';
    if (entity.criticalAlertTicks > 10) {
      html += `<p style="font-size:11px;color:var(--text-dim);margin-top:4px">...and ${entity.criticalAlertTicks - 10} more</p>`;
    }
  }
  html += '</div>';
  return html;
}

function drawTrajectoryChart(canvas, entity) {
  const trajectory = entity.propertyTrajectory;
  if (trajectory.length === 0) return;

  const ctx = canvas.getContext('2d');
  const w = canvas.width;
  const h = canvas.height;
  const pad = { left: 50, right: 20, top: 10, bottom: 30 };
  const pw = w - pad.left - pad.right;
  const ph = h - pad.top - pad.bottom;

  ctx.fillStyle = '#1a1a2e';
  ctx.fillRect(0, 0, w, h);

  const allProps = [...new Set(trajectory.flatMap(s => Object.keys(s.properties)))].sort();

  // Separate 0-1 range and large value properties
  const normalProps = [];
  const largeProps = [];
  for (const p of allProps) {
    const maxVal = Math.max(...trajectory.map(s => s.properties[p] ?? 0));
    if (maxVal > 1.5) largeProps.push(p);
    else normalProps.push(p);
  }

  const maxTick = Math.max(...trajectory.map(s => s.tick));
  const minTick = Math.min(...trajectory.map(s => s.tick));

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

  // Tick axis
  ctx.fillStyle = 'rgba(255,255,255,0.3)';
  ctx.font = '10px sans-serif';
  ctx.textAlign = 'center';
  for (const snap of trajectory) {
    const x = pad.left + ((snap.tick - minTick) / (maxTick - minTick || 1)) * pw;
    ctx.fillText(snap.tick.toString(), x, h - 4);
  }

  // Lines for normal (0-1) properties
  for (const propName of normalProps) {
    const color = getColor(propName);
    ctx.strokeStyle = color;
    ctx.lineWidth = 2;
    ctx.beginPath();
    let first = true;
    for (const snap of trajectory) {
      const val = snap.properties[propName] ?? 0;
      const x = pad.left + ((snap.tick - minTick) / (maxTick - minTick || 1)) * pw;
      const y = pad.top + (1 - Math.min(val, 1)) * ph;
      if (first) { ctx.moveTo(x, y); first = false; }
      else ctx.lineTo(x, y);
    }
    ctx.stroke();

    // Dots
    ctx.fillStyle = color;
    for (const snap of trajectory) {
      const val = snap.properties[propName] ?? 0;
      const x = pad.left + ((snap.tick - minTick) / (maxTick - minTick || 1)) * pw;
      const y = pad.top + (1 - Math.min(val, 1)) * ph;
      ctx.beginPath(); ctx.arc(x, y, 3, 0, Math.PI * 2); ctx.fill();
    }
  }

  // Lines for large-value properties (scaled to their own max)
  for (const propName of largeProps) {
    const maxVal = Math.max(...trajectory.map(s => s.properties[propName] ?? 0), 1);
    const color = getColor(propName);
    ctx.strokeStyle = color;
    ctx.lineWidth = 2;
    ctx.setLineDash([6, 3]);
    ctx.beginPath();
    let first = true;
    for (const snap of trajectory) {
      const val = snap.properties[propName] ?? 0;
      const x = pad.left + ((snap.tick - minTick) / (maxTick - minTick || 1)) * pw;
      const y = pad.top + (1 - val / maxVal) * ph;
      if (first) { ctx.moveTo(x, y); first = false; }
      else ctx.lineTo(x, y);
    }
    ctx.stroke();
    ctx.setLineDash([]);

    // Dots
    ctx.fillStyle = color;
    for (const snap of trajectory) {
      const val = snap.properties[propName] ?? 0;
      const x = pad.left + ((snap.tick - minTick) / (maxTick - minTick || 1)) * pw;
      const y = pad.top + (1 - val / maxVal) * ph;
      ctx.beginPath(); ctx.arc(x, y, 3, 0, Math.PI * 2); ctx.fill();
    }
  }

  // Legend
  ctx.font = '10px sans-serif';
  let lx = pad.left;
  const allDrawn = [...normalProps, ...largeProps];
  for (const propName of allDrawn) {
    const isLarge = largeProps.includes(propName);
    const maxVal = isLarge ? Math.max(...trajectory.map(s => s.properties[propName] ?? 0), 1) : 1;
    ctx.fillStyle = getColor(propName);
    ctx.fillRect(lx, h - 18, 8, 8);
    ctx.fillStyle = 'rgba(255,255,255,0.6)';
    ctx.textAlign = 'left';
    const label = isLarge ? `${propName} (0-${Math.round(maxVal)})` : propName;
    ctx.fillText(label, lx + 10, h - 10);
    lx += label.length * 6 + 24;
  }
}

function drawEntityTimeline(canvas, legendEl, events, entity, minutesPerTick) {
  if (events.length === 0) return;

  const ctx = canvas.getContext('2d');
  const w = canvas.width;
  const h = canvas.height;
  const pad = { left: 40, right: 10, top: 4, bottom: 20 };
  const pw = w - pad.left - pad.right;
  const ph = h - pad.top - pad.bottom;

  ctx.fillStyle = '#1a1a2e';
  ctx.fillRect(0, 0, w, h);

  // Build action → color mapping using same palette as action breakdown
  const actionIds = [...new Set(events.map(e => e.actionId))];
  // Sort to match breakdown order (most frequent first)
  const actionCounts = {};
  for (const e of events) actionCounts[e.actionId] = (actionCounts[e.actionId] || 0) + 1;
  actionIds.sort((a, b) => (actionCounts[b] || 0) - (actionCounts[a] || 0));
  const actionColorMap = {};
  for (let i = 0; i < actionIds.length; i++) {
    actionColorMap[actionIds[i]] = ACTION_PALETTE[i % ACTION_PALETTE.length];
  }

  const minTick = entity.firstTick;
  const maxTick = entity.lastTick;
  const tickRange = maxTick - minTick || 1;

  // Day/night background bands
  drawNightBands(ctx, minTick, maxTick, tickRange, pad, pw, pad.top, ph, minutesPerTick);

  // Draw colored blocks for each action event
  for (const ev of events) {
    const x = pad.left + ((ev.tick - minTick) / tickRange) * pw;
    const color = actionColorMap[ev.actionId] || '#888';
    const blockW = Math.max(pw / tickRange, 1); // At least 1px wide
    ctx.fillStyle = color;
    ctx.fillRect(x, pad.top, blockW, ph);
  }

  // Tick axis labels
  ctx.fillStyle = 'rgba(255,255,255,0.4)';
  ctx.font = '10px sans-serif';
  ctx.textAlign = 'center';
  const labelCount = Math.min(10, events.length);
  for (let i = 0; i <= labelCount; i++) {
    const tick = Math.round(minTick + (i / labelCount) * tickRange);
    const x = pad.left + (i / labelCount) * pw;
    ctx.fillText(tick.toString(), x, h - 4);
  }

  // Left label
  ctx.save();
  ctx.fillStyle = 'rgba(255,255,255,0.3)';
  ctx.font = '10px sans-serif';
  ctx.textAlign = 'center';
  ctx.translate(12, pad.top + ph / 2);
  ctx.rotate(-Math.PI / 2);
  ctx.fillText('actions', 0, 0);
  ctx.restore();

  // Render legend as HTML
  if (legendEl) {
    let legendHtml = '';
    for (const actionId of actionIds) {
      const color = actionColorMap[actionId];
      const count = actionCounts[actionId] || 0;
      legendHtml += `<span style="font-size:11px;display:flex;align-items:center;gap:3px">
        <span style="display:inline-block;width:10px;height:10px;background:${color};border-radius:2px"></span>
        ${actionId} <span style="color:var(--text-dim)">(${count})</span>
      </span>`;
    }
    legendEl.innerHTML = legendHtml;
  }
}

function drawLocationSwimlane(canvas, legendEl, events, entity, minutesPerTick) {
  if (events.length === 0) return;

  // Build location visit order (first occurrence = new row, spawn = row 0)
  const locationOrder = [];
  const locationSet = new Set();
  for (const ev of events) {
    if (ev.location && !locationSet.has(ev.location)) {
      locationSet.add(ev.location);
      locationOrder.push(ev.location);
    }
  }
  if (locationOrder.length === 0) return;

  const rowHeight = 24;
  const pad = { left: 110, right: 10, top: 4, bottom: 22 };
  const bodyH = rowHeight * locationOrder.length;
  const totalH = bodyH + pad.top + pad.bottom;
  const w = canvas.width;

  // Resize canvas height
  canvas.height = totalH;
  const pw = w - pad.left - pad.right;

  const ctx = canvas.getContext('2d');
  ctx.fillStyle = '#1a1a2e';
  ctx.fillRect(0, 0, w, totalH);

  // Build action → color mapping (same as flat timeline)
  const actionIds = [...new Set(events.map(e => e.actionId))];
  const actionCounts = {};
  for (const e of events) actionCounts[e.actionId] = (actionCounts[e.actionId] || 0) + 1;
  actionIds.sort((a, b) => (actionCounts[b] || 0) - (actionCounts[a] || 0));
  const actionColorMap = {};
  for (let i = 0; i < actionIds.length; i++) {
    actionColorMap[actionIds[i]] = ACTION_PALETTE[i % ACTION_PALETTE.length];
  }

  const minTick = entity.firstTick;
  const maxTick = entity.lastTick;
  const tickRange = maxTick - minTick || 1;

  // Day/night background bands
  drawNightBands(ctx, minTick, maxTick, tickRange, pad, pw, pad.top, bodyH, minutesPerTick);

  // Draw row labels and separator lines
  ctx.font = '11px sans-serif';
  ctx.textAlign = 'right';
  for (let i = 0; i < locationOrder.length; i++) {
    const y = pad.top + i * rowHeight;
    const label = locationOrder[i].length > 14
      ? locationOrder[i].slice(0, 13) + '…'
      : locationOrder[i];

    // Row background (alternate subtle shading)
    if (i % 2 === 0) {
      ctx.fillStyle = 'rgba(255,255,255,0.02)';
      ctx.fillRect(pad.left, y, pw, rowHeight);
    }

    // Row separator line
    ctx.strokeStyle = 'rgba(255,255,255,0.08)';
    ctx.beginPath();
    ctx.moveTo(pad.left, y);
    ctx.lineTo(pad.left + pw, y);
    ctx.stroke();

    // Label
    ctx.fillStyle = 'rgba(255,255,255,0.55)';
    ctx.fillText(label, pad.left - 6, y + rowHeight * 0.65);
  }

  // Bottom separator
  ctx.strokeStyle = 'rgba(255,255,255,0.08)';
  ctx.beginPath();
  ctx.moveTo(pad.left, pad.top + bodyH);
  ctx.lineTo(pad.left + pw, pad.top + bodyH);
  ctx.stroke();

  // Build location → row index map
  const locRowMap = {};
  for (let i = 0; i < locationOrder.length; i++) locRowMap[locationOrder[i]] = i;

  // Draw action lines
  for (const ev of events) {
    if (!ev.location) continue;
    const row = locRowMap[ev.location];
    if (row == null) continue;

    const x = pad.left + ((ev.tick - minTick) / tickRange) * pw;
    const y = pad.top + row * rowHeight + 2;
    const lineH = rowHeight - 4;
    const color = actionColorMap[ev.actionId] || '#888';

    ctx.fillStyle = color;
    ctx.fillRect(Math.round(x), y, 1.5, lineH);
  }

  // Tick axis labels
  ctx.fillStyle = 'rgba(255,255,255,0.4)';
  ctx.font = '10px sans-serif';
  ctx.textAlign = 'center';
  const labelCount = Math.min(10, events.length);
  for (let i = 0; i <= labelCount; i++) {
    const tick = Math.round(minTick + (i / labelCount) * tickRange);
    const x = pad.left + (i / labelCount) * pw;
    ctx.fillText(tick.toString(), x, totalH - 4);
  }

  // Reuse legend (same as flat timeline)
  if (legendEl) {
    let legendHtml = '';
    for (const actionId of actionIds) {
      const color = actionColorMap[actionId];
      const count = actionCounts[actionId] || 0;
      legendHtml += `<span style="font-size:11px;display:flex;align-items:center;gap:3px">
        <span style="display:inline-block;width:10px;height:10px;background:${color};border-radius:2px"></span>
        ${actionId} <span style="color:var(--text-dim)">(${count})</span>
      </span>`;
    }
    legendEl.innerHTML = legendHtml;
  }
}

/** Extract section key from a detail-section-title element (first direct text node only). */
function sectionKey(el) {
  for (const node of el.childNodes) {
    if (node.nodeType === 3) {
      const text = node.textContent.trim();
      if (text) return text;
    }
  }
  return el.textContent.trim().substring(0, 30);
}

/** Find which detail section is closest to the top of the scroll container. */
function saveTopSection(container) {
  const titles = container.querySelectorAll('.detail-section-title');
  if (titles.length === 0) return null;
  const containerRect = container.getBoundingClientRect();
  let best = null;
  let bestDist = Infinity;
  for (const t of titles) {
    const rect = t.getBoundingClientRect();
    const dist = Math.abs(rect.top - containerRect.top);
    if (dist < bestDist) {
      bestDist = dist;
      best = sectionKey(t);
    }
  }
  return best;
}

/** Scroll to a saved section by key. */
function restoreTopSection(container, key) {
  if (!key) return;
  const titles = container.querySelectorAll('.detail-section-title');
  for (const t of titles) {
    if (sectionKey(t) === key) {
      requestAnimationFrame(() => {
        const containerRect = container.getBoundingClientRect();
        const titleRect = t.getBoundingClientRect();
        container.scrollTop += titleRect.top - containerRect.top;
      });
      return;
    }
  }
}

function fmtProp(val) {
  if (val === 0) return '0';
  if (Math.abs(val) >= 2) return val.toFixed(0);
  return val.toFixed(3);
}
