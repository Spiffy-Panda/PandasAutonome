import { api } from '../api.js';

export async function renderRunner(container, dataset) {
  const datasets = await api.datasets();

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
    <div class="runner-console" id="runner-output-log" style="display:none"></div>`;

  container.innerHTML = html;

  const startBtn = container.querySelector('#runner-start');
  const logEl = container.querySelector('#runner-output-log');
  const outputField = container.querySelector('#runner-output');
  const analyzeCheckbox = container.querySelector('#runner-analyze');

  // Disable output field when analyze is checked (output goes into analysis folder)
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
  updateOutputField(); // Initial state (analyze is checked by default)

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
        // Parse SSE data lines
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
