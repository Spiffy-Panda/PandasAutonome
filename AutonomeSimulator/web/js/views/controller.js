import { simApi } from '../api.js';
import { renderPropertyBars } from '../components/property-bar.js';

let ws = null;
let state = {
  connected: false,
  possessedId: null,
  token: null,
  tick: 0,
  gameTime: '',
  isAutoAdvancing: false,
  tps: 1,
  entities: [],
  entityState: null,
  actions: [],
  worldState: null,
  eventLog: [],
};

let container = null;
let destroyed = false;

export function renderController(main) {
  destroyed = false;
  container = main;
  container.innerHTML = '<div class="empty-state">Connecting to simulation server...</div>';

  init();
}

async function init() {
  try {
    const status = await simApi.status();
    state.tick = status.tick;
    state.gameTime = status.gameTime;
    state.isAutoAdvancing = status.isAutoAdvancing;
    state.tps = status.ticksPerSecond;
    state.connected = true;

    const entities = await simApi.entities();
    state.entities = entities.filter(e => e.embodied);

    const world = await simApi.worldState();
    state.worldState = world;

    connectWebSocket();
    render();
  } catch (e) {
    if (destroyed) return;
    container.innerHTML = `<div class="empty-state">
      <p>Cannot connect to simulation server</p>
      <p style="color:var(--text-dim);font-size:12px;margin-top:8px">
        Start the C# server: <code>dotnet run --project src/Autonome.Web -- worlds/coastal_city</code>
      </p>
      <p style="color:var(--text-dim);font-size:12px">Expected at <code>http://localhost:3801</code></p>
    </div>`;
  }
}

function connectWebSocket() {
  if (ws) { ws.close(); ws = null; }
  try {
    ws = simApi.connectWs();
    ws.onmessage = (evt) => {
      if (destroyed) return;
      const msg = JSON.parse(evt.data);
      handleWsMessage(msg);
    };
    ws.onclose = () => { ws = null; };
    ws.onerror = () => { ws = null; };
  } catch { ws = null; }
}

function handleWsMessage(msg) {
  if (msg.type === 'tick') {
    state.tick = msg.tick;
    state.gameTime = msg.gameTime;
    if (msg.events) {
      for (const ev of msg.events) {
        state.eventLog.unshift({ tick: msg.tick, ...ev });
        if (state.eventLog.length > 200) state.eventLog.pop();
      }
    }
    // Refresh entity state if possessed
    if (state.possessedId) {
      refreshPossessedState();
    }
    updateTickDisplay();
    updateEventLog();
    updateMapCounts();
  }
}

async function refreshPossessedState() {
  try {
    const [es, acts] = await Promise.all([
      simApi.entityState(state.possessedId),
      simApi.entityActions(state.possessedId),
    ]);
    state.entityState = es;
    state.actions = acts.actions || [];
    updatePossessedInfo();
    updatePropertyPanel();
    updateActionTable();
  } catch {}
}

function updatePossessedInfo() {
  const el = document.querySelector('.ctrl-possessed-info');
  if (el && state.entityState) {
    el.textContent = `Controlling: ${state.entityState.displayName || state.possessedId} @ ${state.entityState.location || '?'}`;
  }
}

// ===================== RENDER =====================

function render() {
  if (destroyed) return;
  container.innerHTML = `
    <div class="ctrl-layout">
      <div class="ctrl-left">
        <div class="ctrl-panel ctrl-tick-panel">
          <div class="detail-section-title">Simulation</div>
          <div class="ctrl-tick-info">
            <span class="ctrl-tick-num">Tick <strong id="ctrl-tick">${state.tick}</strong></span>
            <span class="ctrl-game-time" id="ctrl-time">${state.gameTime}</span>
          </div>
          <div class="ctrl-tick-buttons">
            <button class="ctrl-btn" id="ctrl-tick-btn">Tick</button>
            <button class="ctrl-btn ${state.isAutoAdvancing ? 'ctrl-btn-active' : ''}" id="ctrl-auto-btn">
              ${state.isAutoAdvancing ? 'Running' : 'Auto'}
            </button>
            <button class="ctrl-btn" id="ctrl-pause-btn">Pause</button>
            <label class="ctrl-speed-label">
              <input type="range" id="ctrl-speed" min="0.5" max="20" step="0.5" value="${state.tps}">
              <span id="ctrl-speed-val">${state.tps}</span> tps
            </label>
          </div>
        </div>

        <div class="ctrl-panel ctrl-entity-panel">
          <div class="detail-section-title">Entity</div>
          <div class="ctrl-entity-row">
            <select id="ctrl-entity-select">
              <option value="">-- Select entity --</option>
              ${state.entities.map(e => `<option value="${e.id}" ${e.id === state.possessedId ? 'selected' : ''}>${e.displayName} (${e.id})</option>`).join('')}
            </select>
            <button class="ctrl-btn ctrl-btn-accent" id="ctrl-possess-btn">Possess</button>
            <button class="ctrl-btn" id="ctrl-release-btn" ${!state.possessedId ? 'disabled' : ''}>Release</button>
          </div>
          ${state.possessedId ? `<div class="ctrl-possessed-info">Controlling: <strong>${state.entityState?.displayName || state.possessedId}</strong> @ ${state.entityState?.location || '?'}</div>` : ''}
        </div>

        <div class="ctrl-panel ctrl-props-panel">
          <div class="detail-section-title">Properties</div>
          <div id="ctrl-props">${state.possessedId ? '' : '<div class="ctrl-hint">Possess an entity to see properties</div>'}</div>
        </div>

        <div class="ctrl-panel ctrl-actions-panel">
          <div class="detail-section-title">Actions</div>
          <div id="ctrl-actions">${state.possessedId ? '' : '<div class="ctrl-hint">Possess an entity to see actions</div>'}</div>
        </div>
      </div>

      <div class="ctrl-right">
        <div class="ctrl-panel ctrl-map-panel">
          <div class="detail-section-title">Map</div>
          <div id="ctrl-map"></div>
        </div>

        <div class="ctrl-panel ctrl-log-panel">
          <div class="detail-section-title">Event Log</div>
          <div id="ctrl-log" class="ctrl-log-scroll"></div>
        </div>
      </div>
    </div>
  `;

  bindEvents();
  if (state.possessedId) refreshPossessedState();
  updateMapView();
  updateEventLog();
}

function bindEvents() {
  const tickBtn = document.getElementById('ctrl-tick-btn');
  const autoBtn = document.getElementById('ctrl-auto-btn');
  const pauseBtn = document.getElementById('ctrl-pause-btn');
  const speedSlider = document.getElementById('ctrl-speed');
  const speedVal = document.getElementById('ctrl-speed-val');
  const entitySelect = document.getElementById('ctrl-entity-select');
  const possessBtn = document.getElementById('ctrl-possess-btn');
  const releaseBtn = document.getElementById('ctrl-release-btn');

  tickBtn.addEventListener('click', async () => {
    tickBtn.disabled = true;
    try {
      const result = await simApi.tick();
      state.tick = result.tick;
      state.gameTime = result.gameTime;
      if (result.events) {
        for (const ev of result.events) {
          state.eventLog.unshift({ tick: result.tick, ...ev });
        }
        if (state.eventLog.length > 200) state.eventLog.splice(200);
      }
      updateTickDisplay();
      updateEventLog();
      if (state.possessedId) refreshPossessedState();
      refreshWorldState();
    } catch (e) { console.error('Tick failed:', e); }
    tickBtn.disabled = false;
  });

  autoBtn.addEventListener('click', async () => {
    try {
      const result = await simApi.auto(state.tps);
      state.isAutoAdvancing = true;
      autoBtn.classList.add('ctrl-btn-active');
      autoBtn.textContent = 'Running';
    } catch (e) { console.error('Auto failed:', e); }
  });

  pauseBtn.addEventListener('click', async () => {
    try {
      await simApi.pause();
      state.isAutoAdvancing = false;
      autoBtn.classList.remove('ctrl-btn-active');
      autoBtn.textContent = 'Auto';
    } catch (e) { console.error('Pause failed:', e); }
  });

  speedSlider.addEventListener('input', () => {
    state.tps = parseFloat(speedSlider.value);
    speedVal.textContent = state.tps;
  });
  speedSlider.addEventListener('change', () => {
    if (state.isAutoAdvancing) {
      simApi.auto(state.tps).catch(() => {});
    }
  });

  possessBtn.addEventListener('click', async () => {
    const id = entitySelect.value;
    if (!id) return;
    try {
      const result = await simApi.possess(id);
      state.possessedId = result.entityId;
      state.token = result.token;
      // Fetch entity state before rendering so location is available
      const [es, acts] = await Promise.all([
        simApi.entityState(state.possessedId),
        simApi.entityActions(state.possessedId),
      ]);
      state.entityState = es;
      state.actions = acts.actions || [];
      if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify({ token: state.token }));
      }
      render();
    } catch (e) {
      alert('Possess failed: ' + e.message);
    }
  });

  releaseBtn.addEventListener('click', async () => {
    if (!state.possessedId) return;
    try {
      await simApi.release(state.possessedId);
      state.possessedId = null;
      state.token = null;
      state.entityState = null;
      state.actions = [];
      render();
    } catch (e) {
      alert('Release failed: ' + e.message);
    }
  });
}

// ===================== PARTIAL UPDATES =====================

function updateTickDisplay() {
  const el = document.getElementById('ctrl-tick');
  const timeEl = document.getElementById('ctrl-time');
  if (el) el.textContent = state.tick;
  if (timeEl) timeEl.textContent = state.gameTime;
}

function updatePropertyPanel() {
  const el = document.getElementById('ctrl-props');
  if (!el || !state.entityState) return;
  renderPropertyBars(state.entityState.properties, el);
}

function updateActionTable() {
  const el = document.getElementById('ctrl-actions');
  if (!el) return;
  if (!state.actions.length) {
    el.innerHTML = '<div class="ctrl-hint">No actions available</div>';
    return;
  }

  let html = '<table class="ctrl-action-table"><tr><th>Action</th><th>Category</th><th>Score</th><th></th></tr>';
  for (const a of state.actions) {
    const scoreColor = a.score > 0.5 ? '#66cc88' : a.score > 0.2 ? '#ccbb44' : '#e07040';
    html += `<tr>
      <td>${a.displayName}</td>
      <td><span class="tag">${a.category || '-'}</span></td>
      <td style="color:${scoreColor};font-weight:600">${a.score.toFixed(3)}</td>
      <td><button class="ctrl-btn ctrl-btn-sm" data-action="${a.actionId}">Act</button></td>
    </tr>`;
  }
  html += '</table>';
  el.innerHTML = html;

  // Bind act buttons
  el.querySelectorAll('[data-action]').forEach(btn => {
    btn.addEventListener('click', async () => {
      const actionId = btn.dataset.action;
      if (!state.token) { alert('No possession token'); return; }
      btn.disabled = true;
      btn.textContent = '...';
      try {
        await simApi.entityAct(state.possessedId, actionId, state.token);
        btn.textContent = 'Queued';
        btn.classList.add('ctrl-btn-active');
      } catch (e) {
        btn.textContent = 'Fail';
        alert('Act failed: ' + e.message);
      }
    });
  });
}

function updateEventLog() {
  const el = document.getElementById('ctrl-log');
  if (!el) return;
  if (!state.eventLog.length) {
    el.innerHTML = '<div class="ctrl-hint">No events yet. Advance ticks to see activity.</div>';
    return;
  }

  let html = '';
  for (const ev of state.eventLog.slice(0, 100)) {
    const isOwn = ev.autonomeId === state.possessedId || ev.entityId === state.possessedId;
    html += `<div class="ctrl-log-entry ${isOwn ? 'ctrl-log-own' : ''}">
      <span class="ctrl-log-tick">[${ev.tick}]</span>
      <span class="ctrl-log-entity">${ev.autonomeId || ev.entityId || '?'}</span>
      <span class="ctrl-log-action">${ev.actionId || '?'}</span>
      ${ev.score != null ? `<span class="ctrl-log-score">${ev.score.toFixed(3)}</span>` : ''}
      ${ev.location ? `<span class="ctrl-log-loc">@ ${ev.location}</span>` : ''}
    </div>`;
  }
  el.innerHTML = html;
}

async function refreshWorldState() {
  try {
    const world = await simApi.worldState();
    state.worldState = world;
    updateMapCounts();
  } catch {}
}

function updateMapView() {
  const el = document.getElementById('ctrl-map');
  if (!el || !state.worldState) return;

  const locs = state.worldState.locations;
  let html = '<div class="ctrl-map-grid">';

  // Group by parent (dot notation: valley.hinterland.millhaven → group under valley.hinterland)
  const groups = {};
  for (const [locId, loc] of Object.entries(locs)) {
    const parts = locId.split('.');
    const group = parts.length > 1 ? parts.slice(0, -1).join('.') : '_root';
    if (!groups[group]) groups[group] = [];
    groups[group].push({ id: locId, ...loc });
  }

  for (const [group, locations] of Object.entries(groups)) {
    if (group !== '_root') {
      html += `<div class="ctrl-map-group-label">${group}</div>`;
    }
    for (const loc of locations) {
      const shortName = loc.displayName || loc.id.split('.').pop();
      const isHere = state.entityState && state.entityState.location === loc.id;
      html += `<div class="ctrl-map-loc ${isHere ? 'ctrl-map-here' : ''}" data-loc="${loc.id}">
        <div class="ctrl-map-loc-name">${shortName}</div>
        <div class="ctrl-map-loc-count" id="map-count-${loc.id.replace(/\./g, '-')}">${loc.entityCount} NPCs</div>
        ${loc.tags ? `<div class="ctrl-map-loc-tags">${loc.tags.map(t => `<span class="tag">${t}</span>`).join('')}</div>` : ''}
      </div>`;
    }
  }
  html += '</div>';
  el.innerHTML = html;
}

function updateMapCounts() {
  if (!state.worldState) return;
  for (const [locId, loc] of Object.entries(state.worldState.locations)) {
    const el = document.getElementById(`map-count-${locId.replace(/\./g, '-')}`);
    if (el) el.textContent = `${loc.entityCount} NPCs`;
  }
  // Update highlight
  document.querySelectorAll('.ctrl-map-loc').forEach(el => {
    el.classList.toggle('ctrl-map-here', state.entityState && state.entityState.location === el.dataset.loc);
  });
}

// Cleanup when navigating away
window.addEventListener('hashchange', function onHashChange() {
  if (!location.hash.startsWith('#/controller')) {
    destroyed = true;
    if (ws) { ws.close(); ws = null; }
    window.removeEventListener('hashchange', onHashChange);
  }
});
