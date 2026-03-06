import { createServer } from 'node:http';
import { readdir, readFile, stat } from 'node:fs/promises';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';

const __dirname = fileURLToPath(new URL('.', import.meta.url));
const PROJECT_ROOT = resolve(__dirname, '..');
const REPO_ROOT = resolve(PROJECT_ROOT, '..');
const WEB_ROOT = __dirname;
const PORT = 3800;

// Analysis output directories: CLI writes to AutonomeSimulator/output/analysis/,
// Godot game writes to UtilityAi/output/analysis/ (repo root)
const ANALYSIS_DIRS = [
  { path: join(PROJECT_ROOT, 'output', 'analysis'), source: 'cli' },
  { path: join(REPO_ROOT, 'output', 'analysis'), source: 'game' },
];

const MIME = {
  '.html': 'text/html',
  '.css': 'text/css',
  '.js': 'application/javascript',
  '.mjs': 'application/javascript',
  '.json': 'application/json',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
};

// Validate dataset name to prevent traversal
function validDataset(name) {
  return /^[a-zA-Z0-9_\-\/]+$/.test(name) && !name.includes('..');
}

async function readJsonDir(dirPath) {
  try {
    const s = await stat(dirPath);
    if (!s.isDirectory()) return [];
  } catch { return []; }

  const files = await readdir(dirPath);
  const results = [];
  for (const f of files) {
    if (!f.endsWith('.json')) continue;
    try {
      const raw = await readFile(join(dirPath, f), 'utf-8');
      const parsed = JSON.parse(raw);
      if (Array.isArray(parsed)) results.push(...parsed);
      else results.push(parsed);
    } catch (e) {
      console.error(`Error reading ${join(dirPath, f)}: ${e.message}`);
    }
  }
  return results;
}

async function getDatasets() {
  const datasets = [];
  // Scan known root folders for dataset subdirectories
  for (const root of ['samples', 'worlds']) {
    const rootDir = join(PROJECT_ROOT, root);
    try {
      const entries = await readdir(rootDir);
      for (const e of entries) {
        const s = await stat(join(rootDir, e));
        if (s.isDirectory()) datasets.push(`${root}/${e}`);
      }
    } catch {}
  }
  try {
    const s = await stat(join(PROJECT_ROOT, 'data'));
    if (s.isDirectory()) datasets.push('data');
  } catch {}
  return datasets;
}

async function getOutputs() {
  const dir = join(PROJECT_ROOT, 'output');
  try {
    const files = await readdir(dir);
    return files.filter(f => f.endsWith('.json'));
  } catch { return []; }
}

// Maps run name → absolute directory path (populated by getAnalysisRuns)
const _runDirMap = new Map();

async function getAnalysisRuns() {
  _runDirMap.clear();
  const runs = [];

  for (const { path: dir, source } of ANALYSIS_DIRS) {
    try {
      const entries = await readdir(dir);
      for (const e of entries) {
        const runDir = join(dir, e);
        const reportPath = join(runDir, 'report.json');
        try {
          await stat(reportPath);
          _runDirMap.set(e, runDir);
          runs.push({ name: e, source });
        } catch {}
      }
    } catch {}
  }

  // Sort by timestamp descending (newest first)
  runs.sort((a, b) => b.name.localeCompare(a.name));
  return runs;
}

function resolveRunDir(runName) {
  return _runDirMap.get(runName) || join(PROJECT_ROOT, 'output', 'analysis', runName);
}

function json(res, data, status = 200) {
  res.writeHead(status, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
  res.end(JSON.stringify(data));
}

async function handleApi(req, res) {
  const url = new URL(req.url, `http://localhost:${PORT}`);
  const path = url.pathname;

  if (path === '/api/datasets') {
    return json(res, await getDatasets());
  }

  if (path === '/api/outputs') {
    return json(res, await getOutputs());
  }

  if (path === '/api/analysis-runs') {
    return json(res, await getAnalysisRuns());
  }

  // /api/analysis/:run/simulation — serves simulation_result.json from the run folder
  const analysisSimMatch = path.match(/^\/api\/analysis\/([^/]+)\/simulation$/);
  if (analysisSimMatch) {
    const run = analysisSimMatch[1];
    if (!validDataset(run)) return json(res, { error: 'invalid' }, 400);
    try {
      const raw = await readFile(join(resolveRunDir(run), 'simulation_result.json'), 'utf-8');
      return json(res, JSON.parse(raw));
    } catch { return json(res, { error: 'not found' }, 404); }
  }

  // /api/analysis/:run/meta — serves meta.json (dataset path) from the run folder
  const analysisMetaMatch = path.match(/^\/api\/analysis\/([^/]+)\/meta$/);
  if (analysisMetaMatch) {
    const run = analysisMetaMatch[1];
    if (!validDataset(run)) return json(res, { error: 'invalid' }, 400);
    try {
      const raw = await readFile(join(resolveRunDir(run), 'meta.json'), 'utf-8');
      return json(res, JSON.parse(raw));
    } catch { return json(res, {}, 200); }
  }

  // /api/analysis/:run/inventory — serves inventory.json from the run folder
  const analysisInvMatch = path.match(/^\/api\/analysis\/([^/]+)\/inventory$/);
  if (analysisInvMatch) {
    const run = analysisInvMatch[1];
    if (!validDataset(run)) return json(res, { error: 'invalid' }, 400);
    try {
      const raw = await readFile(join(resolveRunDir(run), 'inventory.json'), 'utf-8');
      return json(res, JSON.parse(raw));
    } catch { return json(res, { error: 'not found' }, 404); }
  }

  // /api/analysis/:run
  const analysisMatch = path.match(/^\/api\/analysis\/([^/]+)$/);
  if (analysisMatch) {
    const run = analysisMatch[1];
    if (!validDataset(run)) return json(res, { error: 'invalid' }, 400);
    try {
      const raw = await readFile(join(resolveRunDir(run), 'report.json'), 'utf-8');
      return json(res, JSON.parse(raw));
    } catch { return json(res, { error: 'not found' }, 404); }
  }

  // /api/output/:filename
  const outputMatch = path.match(/^\/api\/output\/([^/]+)$/);
  if (outputMatch) {
    const filename = outputMatch[1];
    if (!filename.endsWith('.json') || filename.includes('..')) return json(res, { error: 'invalid' }, 400);
    try {
      const raw = await readFile(join(PROJECT_ROOT, 'output', filename), 'utf-8');
      return json(res, JSON.parse(raw));
    } catch { return json(res, { error: 'not found' }, 404); }
  }

  // /api/data/:dataset/:category
  const dataMatch = path.match(/^\/api\/data\/(.+)\/(autonomes|actions|relationships|locations|curves|property_levels)$/);
  if (dataMatch) {
    const dataset = dataMatch[1];
    const category = dataMatch[2];
    if (!validDataset(dataset)) return json(res, { error: 'invalid dataset' }, 400);

    const basePath = join(PROJECT_ROOT, dataset);

    if (category === 'curves') {
      try {
        const raw = await readFile(join(basePath, 'curves.json'), 'utf-8');
        return json(res, JSON.parse(raw));
      } catch { return json(res, { presets: {} }); }
    }

    if (category === 'property_levels') {
      try {
        const raw = await readFile(join(basePath, 'property_levels.json'), 'utf-8');
        return json(res, JSON.parse(raw));
      } catch { return json(res, { sets: {}, entityTypeDefaults: {} }); }
    }

    const items = await readJsonDir(join(basePath, category));
    return json(res, items);
  }

  return json(res, { error: 'not found' }, 404);
}

async function handleRun(req, res) {
  // Read JSON body
  const body = await new Promise((resolve) => {
    let data = '';
    req.on('data', chunk => data += chunk);
    req.on('end', () => {
      try { resolve(JSON.parse(data)); }
      catch { resolve({}); }
    });
  });

  const dataPath = body.data || 'data';
  if (!validDataset(dataPath)) {
    res.writeHead(400, { 'Content-Type': 'text/event-stream' });
    res.write('data: ' + JSON.stringify({ type: 'error', text: 'Invalid dataset name' }) + '\n\n');
    res.end();
    return;
  }

  // Build CLI args
  const args = ['run', '--project', join(PROJECT_ROOT, 'src/Autonome.Cli'), '--'];
  args.push('--data', dataPath);
  if (body.ticks) args.push('--ticks', String(body.ticks));
  if (body.snapshotInterval) args.push('--snapshot-interval', String(body.snapshotInterval));
  if (body.output) args.push('--output', body.output);
  if (body.analyze) args.push('--analyze');
  if (body.validateOnly) args.push('--validate-only');

  // SSE headers
  res.writeHead(200, {
    'Content-Type': 'text/event-stream',
    'Cache-Control': 'no-cache',
    'Connection': 'keep-alive',
    'Access-Control-Allow-Origin': '*',
  });

  const send = (type, text) => {
    res.write('data: ' + JSON.stringify({ type, text }) + '\n\n');
  };

  send('info', `dotnet ${args.join(' ')}`);

  const proc = spawn('dotnet', args, {
    cwd: PROJECT_ROOT,
    shell: true,
  });

  proc.stdout.on('data', (data) => {
    const lines = data.toString().split('\n');
    for (const line of lines) {
      const trimmed = line.replace(/\r/g, '');
      if (trimmed) send('stdout', trimmed);
    }
  });

  proc.stderr.on('data', (data) => {
    const lines = data.toString().split('\n');
    for (const line of lines) {
      const trimmed = line.replace(/\r/g, '');
      if (trimmed) send('error', trimmed);
    }
  });

  proc.on('close', (code) => {
    send(code === 0 ? 'info' : 'error', `Process exited with code ${code}`);
    res.end();
  });

  proc.on('error', (err) => {
    send('error', `Failed to start process: ${err.message}`);
    res.end();
  });

  // Handle client disconnect
  req.on('close', () => {
    proc.kill();
  });
}

// --- Interactive server management ---
let interactiveProc = null;

async function handleRunInteractive(req, res) {
  const body = await new Promise((resolve) => {
    let data = '';
    req.on('data', chunk => data += chunk);
    req.on('end', () => {
      try { resolve(JSON.parse(data)); }
      catch { resolve({}); }
    });
  });

  const dataPath = body.data || 'worlds/coastal_city';
  if (!validDataset(dataPath)) {
    res.writeHead(400, { 'Content-Type': 'text/event-stream' });
    res.write('data: ' + JSON.stringify({ type: 'error', text: 'Invalid dataset name' }) + '\n\n');
    res.end();
    return;
  }

  // Kill existing if running
  if (interactiveProc) {
    interactiveProc.kill();
    interactiveProc = null;
  }

  const args = ['run', '--project', join(PROJECT_ROOT, 'src/Autonome.Web'), '--', dataPath];

  res.writeHead(200, {
    'Content-Type': 'text/event-stream',
    'Cache-Control': 'no-cache',
    'Connection': 'keep-alive',
    'Access-Control-Allow-Origin': '*',
  });

  const send = (type, text) => {
    res.write('data: ' + JSON.stringify({ type, text }) + '\n\n');
  };

  send('info', `dotnet ${args.join(' ')}`);

  const proc = spawn('dotnet', args, { cwd: PROJECT_ROOT, shell: true });
  interactiveProc = proc;

  proc.stdout.on('data', (data) => {
    for (const line of data.toString().split('\n')) {
      const trimmed = line.replace(/\r/g, '');
      if (trimmed) {
        send('stdout', trimmed);
        if (trimmed.includes('Listening on')) {
          send('ready', trimmed);
        }
      }
    }
  });

  proc.stderr.on('data', (data) => {
    for (const line of data.toString().split('\n')) {
      const trimmed = line.replace(/\r/g, '');
      if (trimmed) send('stderr', trimmed);
    }
  });

  proc.on('close', (code) => {
    send(code === 0 ? 'info' : 'error', `Interactive server exited with code ${code}`);
    if (interactiveProc === proc) interactiveProc = null;
    res.end();
  });

  proc.on('error', (err) => {
    send('error', `Failed to start: ${err.message}`);
    if (interactiveProc === proc) interactiveProc = null;
    res.end();
  });

  req.on('close', () => {
    // Don't kill on disconnect — server should keep running
  });
}

function handleStopInteractive(req, res) {
  if (interactiveProc) {
    interactiveProc.kill();
    interactiveProc = null;
    json(res, { status: 'stopped' });
  } else {
    json(res, { status: 'not_running' });
  }
}

function handleInteractiveStatus(req, res) {
  json(res, { running: interactiveProc != null });
}

async function handleStatic(req, res) {
  let urlPath = new URL(req.url, `http://localhost:${PORT}`).pathname;
  if (urlPath === '/') urlPath = '/index.html';

  // Prevent traversal
  const filePath = join(WEB_ROOT, urlPath);
  if (!filePath.startsWith(WEB_ROOT)) {
    res.writeHead(403); res.end('Forbidden'); return;
  }

  const ext = extname(filePath);
  const mime = MIME[ext] || 'application/octet-stream';

  try {
    const data = await readFile(filePath);
    res.writeHead(200, { 'Content-Type': mime });
    res.end(data);
  } catch {
    res.writeHead(404);
    res.end('Not found');
  }
}

const server = createServer(async (req, res) => {
  try {
    if (req.method === 'POST' && req.url === '/api/run') {
      await handleRun(req, res);
    } else if (req.method === 'POST' && req.url === '/api/run-interactive') {
      await handleRunInteractive(req, res);
    } else if (req.method === 'POST' && req.url === '/api/stop-interactive') {
      handleStopInteractive(req, res);
    } else if (req.method === 'GET' && req.url === '/api/interactive/status') {
      handleInteractiveStatus(req, res);
    } else if (req.url.startsWith('/api/')) {
      await handleApi(req, res);
    } else {
      await handleStatic(req, res);
    }
  } catch (e) {
    console.error(e);
    res.writeHead(500);
    res.end('Internal server error');
  }
});

server.listen(PORT, () => {
  console.log(`Autonome Visualizer running at http://localhost:${PORT}`);
});
