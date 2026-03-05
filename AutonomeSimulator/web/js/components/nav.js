import { api } from '../api.js';

const NAV_ITEMS = [
  { section: 'Data', items: [
    { hash: '#/autonomes', label: 'Autonomes' },
    { hash: '#/actions', label: 'Actions' },
    { hash: '#/curves', label: 'Curves' },
    { hash: '#/relationships', label: 'Relationships' },
    { hash: '#/locations', label: 'Locations' },
  ]},
  { section: 'Results', items: [
    { hash: '#/runner', label: 'Runner' },
    { hash: '#/simulation', label: 'Simulation' },
    { hash: '#/analysis', label: 'Analysis' },
  ]},
];

export async function initNav(sidebar, onDatasetChange) {
  const datasets = await api.datasets();

  let html = `<h1>Autonome</h1>`;
  html += `<select id="dataset-picker">`;
  for (const ds of datasets) {
    html += `<option value="${ds}">${ds}</option>`;
  }
  html += `</select>`;

  for (const sec of NAV_ITEMS) {
    html += `<div class="nav-section">`;
    html += `<div class="nav-section-label">${sec.section}</div>`;
    for (const item of sec.items) {
      html += `<a href="${item.hash}" data-nav="${item.hash}">${item.label}</a>`;
    }
    html += `</div>`;
  }

  sidebar.innerHTML = html;

  const picker = sidebar.querySelector('#dataset-picker');
  picker.addEventListener('change', () => onDatasetChange(picker.value));

  // Return initial dataset
  return datasets[0] || 'samples/minimal';
}

export function updateActiveNav(hash) {
  document.querySelectorAll('#sidebar a').forEach(a => {
    const navHash = a.getAttribute('data-nav');
    if (!navHash) return;
    // Match exact or prefix (e.g. #/autonomes matches #/autonomes/npc_marta)
    a.classList.toggle('active', hash.startsWith(navHash));
  });
}
