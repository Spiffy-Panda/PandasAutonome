import { api } from '../api.js';
import { CurveCanvas } from '../components/curve-canvas.js';

let curvePresets = {};

export async function renderActions(container, dataset, detailId) {
  const [data, curvesData] = await Promise.all([
    api.actions(dataset),
    api.curves(dataset),
  ]);
  curvePresets = curvesData.presets || {};

  if (detailId) {
    const item = data.find(d => d.id === detailId);
    if (!item) { container.innerHTML = '<div class="empty-state">Not found</div>'; return; }
    renderDetail(container, item);
    return;
  }

  let html = '<h2>Actions</h2>';
  html += '<table><tr><th>ID</th><th>Name</th><th>Category</th><th>Responses</th><th>Steps</th><th>Embodied</th></tr>';
  for (const a of data) {
    const respCount = Object.keys(a.propertyResponses || {}).length;
    const stepCount = a.steps?.length || 0;
    const emb = a.requirements?.embodied != null ? (a.requirements.embodied ? 'Yes' : 'No') : '-';
    html += `<tr class="card" data-id="${a.id}" style="cursor:pointer">
      <td style="font-family:monospace;font-size:12px">${a.id}</td>
      <td>${a.displayName}</td>
      <td><span class="tag">${a.category || '-'}</span></td>
      <td>${respCount}</td>
      <td>${stepCount}</td>
      <td>${emb}</td>
    </tr>`;
  }
  html += '</table>';
  container.innerHTML = html;

  container.querySelectorAll('tr[data-id]').forEach(row => {
    row.addEventListener('click', () => {
      location.hash = `#/actions/${row.dataset.id}`;
    });
  });
}

function renderDetail(container, a) {
  let html = `<div class="detail-panel">
    <span class="back-link" onclick="location.hash='#/actions'">&larr; Back to list</span>
    <div class="detail-header">
      <h2>${a.displayName}</h2>
      <span class="id-label">${a.id}</span>
      ${a.category ? `<span class="tag">${a.category}</span>` : ''}
    </div>`;

  // Requirements
  if (a.requirements) {
    html += `<div class="detail-section">
      <div class="detail-section-title">Requirements</div>
      <div style="font-size:12px">`;
    const r = a.requirements;
    if (r.embodied != null) html += `<div>Embodied: ${r.embodied}</div>`;
    if (r.nearbyTags?.length) html += `<div>Nearby tags: ${r.nearbyTags.join(', ')}</div>`;
    if (r.timeOfDay) html += `<div>Time: ${r.timeOfDay.min}:00 - ${r.timeOfDay.max}:00</div>`;
    if (r.propertyMin) html += `<div>Min properties: ${JSON.stringify(r.propertyMin)}</div>`;
    if (r.propertyBelow) html += `<div>Below properties: ${JSON.stringify(r.propertyBelow)}</div>`;
    if (r.blockedByStates?.length) html += `<div>Blocked by: ${r.blockedByStates.join(', ')}</div>`;
    if (r.noActiveModifier?.length) html += `<div>No active modifier: ${r.noActiveModifier.join(', ')}</div>`;
    html += `</div></div>`;
  }

  // Property Responses with curve thumbnails
  if (a.propertyResponses && Object.keys(a.propertyResponses).length > 0) {
    html += `<div class="detail-section">
      <div class="detail-section-title">Property Responses</div>`;
    for (const [propId, resp] of Object.entries(a.propertyResponses)) {
      const curveName = typeof resp.curve === 'string' ? resp.curve : 'custom';
      html += `<div style="display:flex;align-items:center;gap:12px;margin:8px 0">
        <div class="curve-container">
          <canvas data-curve-prop="${propId}" width="160" height="80"></canvas>
        </div>
        <div>
          <div style="font-size:13px;font-weight:600">${propId}</div>
          <div style="font-size:11px;color:var(--text-dim)">Curve: ${curveName}</div>
          <div style="font-size:11px;color:var(--text-dim)">Magnitude: ${resp.magnitude}</div>
        </div>
      </div>`;
    }
    html += `</div>`;
  }

  // Personality Affinity
  if (a.personalityAffinity && Object.keys(a.personalityAffinity).length > 0) {
    html += `<div class="detail-section">
      <div class="detail-section-title">Personality Affinity</div>`;
    for (const [axis, mult] of Object.entries(a.personalityAffinity)) {
      const barWidth = Math.min(mult / 2 * 100, 100);
      html += `<div class="score-bar-row">
        <span class="score-bar-name">${axis}</span>
        <div class="score-bar-track">
          <div class="score-bar-fill" style="width:${barWidth}%;background:var(--accent2)"></div>
        </div>
        <span class="score-bar-value">${mult.toFixed(1)}x</span>
      </div>`;
    }
    html += `</div>`;
  }

  // Steps
  if (a.steps?.length > 0) {
    html += `<div class="detail-section">
      <div class="detail-section-title">Steps</div>
      <ul class="step-list">`;
    for (const step of a.steps) {
      let desc = step.type;
      if (step.type === 'modifyProperty') desc += ` ${step.entity || 'self'}.${step.property} ${step.amount >= 0 ? '+' : ''}${step.amount}`;
      else if (step.type === 'wait') desc += ` ${step.duration || `${step.durationMin}-${step.durationMax}`}`;
      else if (step.type === 'moveTo') desc += ` ${step.target}`;
      else if (step.type === 'animate') desc += ` ${step.animation} (${step.duration}s)`;
      else if (step.type === 'emitEvent') desc += ` "${step.event}"`;
      else if (step.type === 'emitDirective') desc += ` ${step.modifier?.id || ''}`;
      html += `<li>${desc}</li>`;
    }
    html += `</ul></div>`;
  }

  // Flavor
  if (a.flavor) {
    html += `<div class="detail-section">
      <div class="detail-section-title">Flavor</div>`;
    if (a.flavor.onStart) html += a.flavor.onStart.map(t => `<div class="flavor">${t}</div>`).join('');
    if (a.flavor.onComplete) html += a.flavor.onComplete.map(t => `<div class="flavor">${t}</div>`).join('');
    html += `</div>`;
  }

  html += `</div>`;
  container.innerHTML = html;

  // Render curve thumbnails
  container.querySelectorAll('canvas[data-curve-prop]').forEach(canvas => {
    const propId = canvas.dataset.curveProp;
    const resp = a.propertyResponses[propId];
    if (!resp) return;

    const cc = new CurveCanvas(canvas, { width: 160, height: 80, padding: 4 });
    // Resolve curve: either from presets or inline
    let points;
    if (typeof resp.curve === 'string') {
      points = curvePresets[resp.curve]?.points;
    } else if (resp.curve?.points) {
      points = resp.curve.points;
    }
    if (points) {
      cc.renderSingle(points);
    }
  });
}
