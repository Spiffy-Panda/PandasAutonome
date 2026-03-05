import { api } from '../api.js';
import { CurveCanvas } from '../components/curve-canvas.js';

export async function renderCurves(container, dataset) {
  const data = await api.curves(dataset);
  const presets = data.presets || {};
  const names = Object.keys(presets);

  let html = `<h2>Response Curve Presets</h2>
    <div style="display:flex;gap:20px">
      <div style="min-width:180px">
        <div class="detail-section">
          <div class="detail-section-title">Presets</div>
          <div id="curve-list">
            <div class="curve-item" data-name="" style="padding:6px 10px;cursor:pointer;font-size:13px;border-radius:var(--radius);margin:2px 0;background:var(--accent);color:#fff">All curves</div>`;
  for (const name of names) {
    const desc = presets[name].description || '';
    html += `<div class="curve-item" data-name="${name}" style="padding:6px 10px;cursor:pointer;font-size:13px;border-radius:var(--radius);margin:2px 0" title="${desc}">${name}</div>`;
  }
  html += `</div></div></div>
      <div style="flex:1">
        <div class="curve-container">
          <canvas id="main-curve-canvas" width="600" height="400"></canvas>
        </div>
        <div id="curve-info" style="margin-top:12px"></div>
      </div>
    </div>`;

  container.innerHTML = html;

  const canvas = container.querySelector('#main-curve-canvas');
  const cc = new CurveCanvas(canvas, { width: 600, height: 400 });
  cc.setCurves(presets);

  const infoEl = container.querySelector('#curve-info');
  const items = container.querySelectorAll('.curve-item');

  items.forEach(item => {
    item.addEventListener('click', () => {
      items.forEach(i => i.style.background = '');
      items.forEach(i => i.style.color = '');
      item.style.background = 'var(--accent)';
      item.style.color = '#fff';

      const name = item.dataset.name;
      if (name) {
        cc.setHighlight(name);
        const preset = presets[name];
        infoEl.innerHTML = `<div class="detail-section">
          <div class="detail-section-title">${name}</div>
          <p class="flavor">${preset.description || ''}</p>
          <p style="font-size:12px;margin-top:8px">Keyframes: ${preset.keys.length}</p>
          <table><tr><th>Time</th><th>Value</th><th>In Tangent</th><th>Out Tangent</th></tr>
          ${preset.keys.map(k => `<tr>
            <td>${k.time}</td><td>${k.value}</td>
            <td>${k.inTangent || 0}</td>
            <td>${k.outTangent || 0}</td>
          </tr>`).join('')}
          </table>
        </div>`;
      } else {
        cc.setHighlight(null);
        infoEl.innerHTML = '';
      }
    });
  });

  // Hover effect
  items.forEach(item => {
    item.addEventListener('mouseenter', () => { if (item.style.background !== 'var(--accent)') item.style.background = 'var(--bg-hover)'; });
    item.addEventListener('mouseleave', () => { if (item.style.background === 'var(--bg-hover)') item.style.background = ''; });
  });
}
