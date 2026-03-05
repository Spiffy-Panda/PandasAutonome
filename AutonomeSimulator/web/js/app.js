import { api } from './api.js';
import { initNav, updateActiveNav } from './components/nav.js';
import { renderAutonomes } from './views/autonomes.js';
import { renderActions } from './views/actions.js';
import { renderCurves } from './views/curves.js';
import { renderRelationships } from './views/relationships.js';
import { renderLocations } from './views/locations.js';
import { renderSimulation } from './views/simulation.js';
import { renderAnalysis } from './views/analysis.js';
import { renderRunner } from './views/runner.js';
import { renderInventory } from './views/inventory.js';

let currentDataset = null;

async function init() {
  const sidebar = document.getElementById('sidebar');
  currentDataset = await initNav(sidebar, (ds) => {
    currentDataset = ds;
    route();
  });

  window.addEventListener('hashchange', route);
  route();
}

function route() {
  const hash = location.hash.slice(1) || '/autonomes';
  const main = document.getElementById('main-content');
  main.innerHTML = '<div class="empty-state">Loading...</div>';

  updateActiveNav(hash);

  const parts = hash.split('/').filter(Boolean);
  const view = parts[0];
  const detailId = parts.slice(1).join('/');

  switch (view) {
    case 'autonomes':
      renderAutonomes(main, currentDataset, detailId);
      break;
    case 'actions':
      renderActions(main, currentDataset, detailId);
      break;
    case 'curves':
      renderCurves(main, currentDataset);
      break;
    case 'relationships':
      renderRelationships(main, currentDataset);
      break;
    case 'locations':
      renderLocations(main, currentDataset);
      break;
    case 'simulation':
      renderSimulation(main, currentDataset);
      break;
    case 'analysis':
      renderAnalysis(main, detailId, currentDataset);
      break;
    case 'runner':
      renderRunner(main, currentDataset);
      break;
    case 'inventory':
      renderInventory(main, detailId);
      break;
    default:
      main.innerHTML = '<div class="empty-state">Unknown view</div>';
  }
}

init();
