import { api } from '../api.js';

// Action category colors matching ActionPicker in Godot
const CATEGORY_COLORS = {
  work: '#e89030',
  sustenance: '#44aa44',
  social: '#4488cc',
  trade: '#ccaa44',
  rest: '#9966cc',
  political: '#cc4444',
  governance: '#882222',
  movement: '#888888',
  hauling: '#d2b48c',
  other: '#607d8b',
};

const CATEGORY_ORDER = ['work', 'sustenance', 'social', 'rest', 'trade', 'hauling', 'political', 'governance', 'movement', 'other'];

export async function renderDailyRhythm(container, dataset) {
  // Load analysis runs and action definitions
  let runs = [];
  try {
    runs = await api.analysisRuns();
  } catch {
    container.innerHTML = '<div class="empty-state">No analysis runs found. Run a simulation first.</div>';
    return;
  }

  if (runs.length === 0) {
    container.innerHTML = '<div class="empty-state">No analysis runs found. Run a simulation first.</div>';
    return;
  }

  // Normalize runs to {name, source} objects (API may return strings or objects)
  const runItems = runs.map(r => typeof r === 'string' ? { name: r, source: '' } : r);

  // Build UI with run selector
  let html = `<h2>Daily Rhythm</h2>
    <div style="margin-bottom:12px;display:flex;gap:12px;align-items:center">
      <label style="font-size:12px;color:var(--text-secondary)">Analysis run:</label>
      <select id="rhythm-run" style="padding:4px 8px;background:var(--bg-secondary);color:var(--text-primary);border:1px solid var(--border);border-radius:4px">
        ${runItems.map(r => `<option value="${r.name}">${r.name}${r.source ? ` (${r.source})` : ''}</option>`).join('')}
      </select>
      <span id="rhythm-status" style="font-size:11px;color:var(--text-muted)">Loading...</span>
    </div>
    <div id="rhythm-legend" style="margin-bottom:12px;display:flex;flex-wrap:wrap;gap:8px"></div>
    <div id="rhythm-chart" style="background:var(--bg-primary);border-radius:8px;padding:16px;overflow:hidden"></div>
    <div id="rhythm-table" style="margin-top:16px"></div>`;

  container.innerHTML = html;

  const runSelect = container.querySelector('#rhythm-run');
  const statusEl = container.querySelector('#rhythm-status');
  const legendEl = container.querySelector('#rhythm-legend');
  const chartEl = container.querySelector('#rhythm-chart');
  const tableEl = container.querySelector('#rhythm-table');

  // Load action categories
  let actionCategories = {};
  try {
    const actions = await api.actions(dataset);
    for (const a of actions) {
      actionCategories[a.id] = a.category || 'other';
    }
  } catch {
    // Fallback: guess from action ID
  }

  async function loadRun(runName) {
    statusEl.textContent = 'Loading simulation data...';
    chartEl.innerHTML = '';
    tableEl.innerHTML = '';

    let simResult;
    try {
      simResult = await api.analysisSimulation(runName);
    } catch {
      statusEl.textContent = 'Failed to load simulation data.';
      return;
    }

    const events = simResult.actionEvents || [];
    const minutesPerTick = simResult.minutesPerTick || 15;

    statusEl.textContent = `${events.length} events, ${minutesPerTick} min/tick`;

    // Build hourly distribution
    // hourData[hour][category] = count
    const hourData = Array.from({ length: 24 }, () => ({}));
    const hourTotals = new Array(24).fill(0);
    const categoriesUsed = new Set();

    for (const evt of events) {
      if (!evt.embodied) continue;
      if (evt.eventType && evt.eventType !== 'action_start') continue;

      // Extract hour from gameTime "Day X, HH:MM"
      let hour = 0;
      const timeMatch = evt.gameTime?.match(/(\d{2}):(\d{2})/);
      if (timeMatch) {
        hour = parseInt(timeMatch[1], 10);
      } else {
        // Fallback: compute from tick
        hour = Math.floor((evt.tick * minutesPerTick / 60) % 24);
      }

      const cat = actionCategories[evt.actionId] || 'other';
      categoriesUsed.add(cat);
      hourData[hour][cat] = (hourData[hour][cat] || 0) + 1;
      hourTotals[hour]++;
    }

    // Convert to percentages
    const hourPcts = hourData.map((cats, h) => {
      const total = hourTotals[h] || 1;
      const pcts = {};
      for (const cat of CATEGORY_ORDER) {
        pcts[cat] = ((cats[cat] || 0) / total) * 100;
      }
      return pcts;
    });

    // Render legend
    legendEl.innerHTML = CATEGORY_ORDER
      .filter(c => categoriesUsed.has(c))
      .map(c => `<span style="display:flex;align-items:center;gap:4px;font-size:11px;color:var(--text-secondary)">
        <span style="display:inline-block;width:12px;height:12px;border-radius:2px;background:${CATEGORY_COLORS[c]}"></span>
        ${c}
      </span>`)
      .join('');

    // Render stacked bar chart as SVG
    const barWidth = 30;
    const gap = 5;
    const leftPad = 40;
    const chartWidth = leftPad + 24 * (barWidth + gap) + 10;
    const chartHeight = 300;
    const topPad = 20;
    const bottomPad = 40;
    const plotHeight = chartHeight - topPad - bottomPad;

    let svg = `<svg viewBox="0 0 ${chartWidth} ${chartHeight}" style="width:100%;height:auto" xmlns="http://www.w3.org/2000/svg">`;

    // Y-axis labels
    for (let pct = 0; pct <= 100; pct += 25) {
      const y = topPad + plotHeight * (1 - pct / 100);
      svg += `<text x="${leftPad - 4}" y="${y + 4}" text-anchor="end" fill="rgba(255,255,255,0.4)" font-size="10">${pct}%</text>`;
      svg += `<line x1="${leftPad}" y1="${y}" x2="${chartWidth}" y2="${y}" stroke="rgba(255,255,255,0.08)"/>`;
    }

    // Night shading (22:00-06:00)
    for (let h = 0; h < 24; h++) {
      const isNight = h >= 22 || h < 6;
      if (isNight) {
        const x = leftPad + h * (barWidth + gap);
        svg += `<rect x="${x - 2}" y="${topPad}" width="${barWidth + 4}" height="${plotHeight}" fill="rgba(30,30,80,0.3)" rx="2"/>`;
      }
    }

    // Stacked bars
    for (let h = 0; h < 24; h++) {
      const x = leftPad + h * (barWidth + gap);
      let yOffset = 0;

      for (const cat of CATEGORY_ORDER) {
        const pct = hourPcts[h][cat] || 0;
        if (pct <= 0) continue;
        const barH = (pct / 100) * plotHeight;
        const y = topPad + plotHeight - yOffset - barH;

        svg += `<rect x="${x}" y="${y}" width="${barWidth}" height="${barH}" fill="${CATEGORY_COLORS[cat]}" opacity="0.85" rx="1">
          <title>${h}:00 - ${cat}: ${pct.toFixed(1)}% (${hourData[h][cat] || 0} actions)</title>
        </rect>`;
        yOffset += barH;
      }

      // Hour label
      svg += `<text x="${x + barWidth / 2}" y="${chartHeight - bottomPad + 14}" text-anchor="middle"
        fill="rgba(255,255,255,0.5)" font-size="10">${h.toString().padStart(2, '0')}</text>`;

      // Total count
      svg += `<text x="${x + barWidth / 2}" y="${chartHeight - bottomPad + 26}" text-anchor="middle"
        fill="rgba(255,255,255,0.25)" font-size="8">${hourTotals[h]}</text>`;
    }

    // Day/Night labels
    svg += `<text x="${leftPad + 3 * (barWidth + gap)}" y="${chartHeight - 2}" text-anchor="middle" fill="rgba(100,100,200,0.5)" font-size="9">NIGHT</text>`;
    svg += `<text x="${leftPad + 14 * (barWidth + gap)}" y="${chartHeight - 2}" text-anchor="middle" fill="rgba(200,200,100,0.5)" font-size="9">DAY</text>`;
    svg += `<text x="${leftPad + 23 * (barWidth + gap)}" y="${chartHeight - 2}" text-anchor="middle" fill="rgba(100,100,200,0.5)" font-size="9">NIGHT</text>`;

    svg += '</svg>';
    chartEl.innerHTML = svg;

    // Render data table
    let thtml = `<h3>Hourly Breakdown</h3>
      <table><tr><th>Hour</th><th>Total</th>`;
    const displayCats = CATEGORY_ORDER.filter(c => categoriesUsed.has(c));
    for (const c of displayCats) {
      thtml += `<th style="color:${CATEGORY_COLORS[c]}">${c}</th>`;
    }
    thtml += '</tr>';

    for (let h = 0; h < 24; h++) {
      const isNight = h >= 22 || h < 6;
      thtml += `<tr style="${isNight ? 'background:rgba(30,30,80,0.2)' : ''}">
        <td>${h.toString().padStart(2, '0')}:00</td>
        <td>${hourTotals[h]}</td>`;
      for (const c of displayCats) {
        const count = hourData[h][c] || 0;
        const pct = hourPcts[h][c] || 0;
        thtml += `<td style="font-size:11px">${count} <span style="color:var(--text-muted)">(${pct.toFixed(0)}%)</span></td>`;
      }
      thtml += '</tr>';
    }
    thtml += '</table>';
    tableEl.innerHTML = thtml;
  }

  // Load initial run (first in list = most recent)
  runSelect.addEventListener('change', () => loadRun(runSelect.value));
  loadRun(runItems[0].name);
}
