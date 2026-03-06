import { api } from '../api.js';

export async function renderRunner(container, dataset) {
  const datasets = await api.datasets();
  const interactiveStatus = await fetch('/api/interactive/status').then(r => r.json()).catch(() => ({ running: false }));

  let html = `<h2>Run Simulation</h2>
    <div class="runner-form">
      <div class="form-group">
        <label>Dataset</label>
        <select id="runner-dataset">
          ${datasets.map(ds => `<option value="${ds}" ${ds === dataset ? 'selected' : ''}>${ds}</option>`).join('')}
        </select>
      </div>
      <div class="form-group">
        <label>Ticks</label>
        <input type="number" id="runner-ticks" value="1000" min="1" max="100000">
      </div>
      <div class="form-group">
        <label>Snapshot Interval</label>
        <input type="number" id="runner-snapshot" value="100" min="1" max="10000">
      </div>
      <div class="form-group">
        <label>Output File</label>
        <input type="text" id="runner-output" value="output/simulation_result.json">
      </div>
      <div class="form-group">
        <div class="checkbox-group">
          <input type="checkbox" id="runner-analyze" checked>
          <label for="runner-analyze" style="display:inline;text-transform:none;letter-spacing:0;font-size:13px;color:var(--text)">Generate analysis report</label>
        </div>
      </div>
      <div class="form-group">
        <div class="checkbox-group">
          <input type="checkbox" id="runner-validate">
          <label for="runner-validate" style="display:inline;text-transform:none;letter-spacing:0;font-size:13px;color:var(--text)">Validate only (don't run simulation)</label>
        </div>
      </div>
      <button class="runner-btn" id="runner-start">Run Simulation</button>
    </div>
    <div class="runner-console" id="runner-output-log" style="display:none"></div>

    <h2 style="margin-top:32px">Interactive Server</h2>
    <div class="runner-form">
      <div class="form-group">
        <label>Dataset</label>
        <select id="interactive-dataset">
          ${datasets.map(ds => `<option value="${ds}" ${ds === dataset ? 'selected' : ''}>${ds}</option>`).join('')}
        </select>
      </div>
      <div style="display:flex;gap:8px;align-items:center;flex-wrap:wrap">
        <button class="runner-btn" id="interactive-start">${interactiveStatus.running ? 'Relaunch' : 'Launch Server'}</button>
        <button class="runner-btn" id="interactive-stop" style="background:var(--accent)" ${!interactiveStatus.running ? 'disabled' : ''}>Stop</button>
        <span id="interactive-status" style="font-size:12px;color:var(--text-dim)">${interactiveStatus.running ? 'Server running' : ''}</span>
      </div>
      <div id="interactive-ready" style="display:${interactiveStatus.running ? 'block' : 'none'};margin-top:12px;padding:10px 14px;background:rgba(13,184,222,0.1);border:1px solid var(--accent2);border-radius:var(--radius)">
        <span style="color:var(--accent2);font-weight:600">Server ready</span>
        <a href="#/controller" style="margin-left:12px;color:var(--accent2)">Open Controller &rarr;</a>
      </div>
    </div>
    <div class="runner-console" id="interactive-log" style="display:none"></div>`;

  container.innerHTML = html;

  // ===================== BATCH SIMULATION =====================

  const startBtn = container.querySelector('#runner-start');
  const logEl = container.querySelector('#runner-output-log');
  const outputField = container.querySelector('#runner-output');
  const analyzeCheckbox = container.querySelector('#runner-analyze');

  function updateOutputField() {
    if (analyzeCheckbox.checked) {
      outputField.disabled = true;
      outputField.dataset.savedValue = outputField.value;
      outputField.value = '(auto: in analysis folder)';
      outputField.style.opacity = '0.5';
    } else {
      outputField.disabled = false;
      outputField.value = outputField.dataset.savedValue || 'output/simulation_result.json';
      outputField.style.opacity = '1';
    }
  }
  analyzeCheckbox.addEventListener('change', updateOutputField);
  updateOutputField();

  startBtn.addEventListener('click', async () => {
    const dsVal = container.querySelector('#runner-dataset').value;
    const ticks = container.querySelector('#runner-ticks').value;
    const snapshot = container.querySelector('#runner-snapshot').value;
    const outputPath = container.querySelector('#runner-output').value;
    const analyze = container.querySelector('#runner-analyze').checked;
    const validateOnly = container.querySelector('#runner-validate').checked;

    startBtn.disabled = true;
    startBtn.textContent = 'Running...';
    logEl.style.display = 'block';
    logEl.innerHTML = '<span class="info">Starting simulation...</span>\n';

    try {
      const response = await fetch('/api/run', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          data: dsVal,
          ticks: parseInt(ticks),
          snapshotInterval: parseInt(snapshot),
          output: outputPath,
          analyze,
          validateOnly,
        }),
      });

      const reader = response.body.getReader();
      const decoder = new TextDecoder();

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        const text = decoder.decode(value, { stream: true });
        const lines = text.split('\n');
        for (const line of lines) {
          if (line.startsWith('data: ')) {
            try {
              const msg = JSON.parse(line.slice(6));
              appendLog(logEl, msg);
            } catch {}
          }
        }
      }

      logEl.innerHTML += '\n<span class="info">Simulation complete.</span>\n';
    } catch (err) {
      logEl.innerHTML += `\n<span class="error">Error: ${err.message}</span>\n`;
    }

    startBtn.disabled = false;
    startBtn.textContent = 'Run Simulation';
  });

  // ===================== INTERACTIVE SERVER =====================

  const iStartBtn = container.querySelector('#interactive-start');
  const iStopBtn = container.querySelector('#interactive-stop');
  const iLogEl = container.querySelector('#interactive-log');
  const iStatus = container.querySelector('#interactive-status');
  const iReady = container.querySelector('#interactive-ready');

  iStartBtn.addEventListener('click', async () => {
    const dsVal = container.querySelector('#interactive-dataset').value;

    iStartBtn.disabled = true;
    iStartBtn.textContent = 'Starting...';
    iStopBtn.disabled = false;
    iLogEl.style.display = 'block';
    iLogEl.innerHTML = '<span class="info">Launching interactive server...</span>\n';
    iReady.style.display = 'none';
    iStatus.textContent = 'Starting...';

    try {
      const response = await fetch('/api/run-interactive', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ data: dsVal }),
      });

      const reader = response.body.getReader();
      const decoder = new TextDecoder();

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        const text = decoder.decode(value, { stream: true });
        const lines = text.split('\n');
        for (const line of lines) {
          if (line.startsWith('data: ')) {
            try {
              const msg = JSON.parse(line.slice(6));
              appendLog(iLogEl, msg);
              if (msg.type === 'ready') {
                iReady.style.display = 'block';
                iStatus.textContent = 'Server running';
                iStartBtn.textContent = 'Relaunch';
                iStartBtn.disabled = false;
              }
            } catch {}
          }
        }
      }

      iLogEl.innerHTML += '\n<span class="info">Server stopped.</span>\n';
      iStatus.textContent = '';
      iReady.style.display = 'none';
    } catch (err) {
      iLogEl.innerHTML += `\n<span class="error">Error: ${err.message}</span>\n`;
    }

    iStartBtn.disabled = false;
    iStartBtn.textContent = 'Launch Server';
    iStopBtn.disabled = true;
  });

  iStopBtn.addEventListener('click', async () => {
    iStopBtn.disabled = true;
    try {
      await fetch('/api/stop-interactive', { method: 'POST' });
      iStatus.textContent = 'Stopped';
      iReady.style.display = 'none';
    } catch {}
  });
}

function appendLog(el, msg) {
  if (msg.type === 'error') {
    el.innerHTML += `<span class="error">${escapeHtml(msg.text)}</span>\n`;
  } else if (msg.type === 'info') {
    el.innerHTML += `<span class="info">${escapeHtml(msg.text)}</span>\n`;
  } else {
    el.innerHTML += escapeHtml(msg.text) + '\n';
  }
  el.scrollTop = el.scrollHeight;
}

function escapeHtml(text) {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
