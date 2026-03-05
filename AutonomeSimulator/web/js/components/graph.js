export class ForceGraph {
  constructor(container, options = {}) {
    this.container = container;
    this.width = options.width || 600;
    this.height = options.height || 400;
    this.nodes = [];
    this.edges = [];
    this.onNodeClick = options.onNodeClick || null;
    this.onEdgeClick = options.onEdgeClick || null;
  }

  setData(nodes, edges) {
    this.nodes = nodes.map((n, i) => ({
      ...n,
      x: this.width / 2 + Math.cos(i * 2 * Math.PI / nodes.length) * 120,
      y: this.height / 2 + Math.sin(i * 2 * Math.PI / nodes.length) * 120,
      vx: 0, vy: 0,
    }));
    this.edges = edges;
    this._simulate();
    this._render();
  }

  _simulate() {
    const nodes = this.nodes;
    const edges = this.edges;
    const nodeMap = {};
    nodes.forEach(n => nodeMap[n.id] = n);

    for (let iter = 0; iter < 200; iter++) {
      // Repulsion between all node pairs
      for (let i = 0; i < nodes.length; i++) {
        for (let j = i + 1; j < nodes.length; j++) {
          let dx = nodes[j].x - nodes[i].x;
          let dy = nodes[j].y - nodes[i].y;
          let dist = Math.sqrt(dx * dx + dy * dy) || 1;
          let force = 8000 / (dist * dist);
          let fx = (dx / dist) * force;
          let fy = (dy / dist) * force;
          nodes[i].vx -= fx; nodes[i].vy -= fy;
          nodes[j].vx += fx; nodes[j].vy += fy;
        }
      }

      // Attraction along edges
      for (const e of edges) {
        const a = nodeMap[e.source];
        const b = nodeMap[e.target];
        if (!a || !b) continue;
        let dx = b.x - a.x;
        let dy = b.y - a.y;
        let dist = Math.sqrt(dx * dx + dy * dy) || 1;
        let force = (dist - 150) * 0.02;
        let fx = (dx / dist) * force;
        let fy = (dy / dist) * force;
        a.vx += fx; a.vy += fy;
        b.vx -= fx; b.vy -= fy;
      }

      // Center gravity
      for (const n of nodes) {
        n.vx += (this.width / 2 - n.x) * 0.005;
        n.vy += (this.height / 2 - n.y) * 0.005;
      }

      // Apply velocity with damping
      for (const n of nodes) {
        n.vx *= 0.85; n.vy *= 0.85;
        n.x += n.vx; n.y += n.vy;
        n.x = Math.max(40, Math.min(this.width - 40, n.x));
        n.y = Math.max(40, Math.min(this.height - 40, n.y));
      }
    }
  }

  _render() {
    const nodeMap = {};
    this.nodes.forEach(n => nodeMap[n.id] = n);

    let svg = `<svg width="${this.width}" height="${this.height}" xmlns="http://www.w3.org/2000/svg">`;

    // Edges
    for (const e of this.edges) {
      const a = nodeMap[e.source];
      const b = nodeMap[e.target];
      if (!a || !b) continue;

      const mx = (a.x + b.x) / 2;
      const my = (a.y + b.y) / 2;

      svg += `<line x1="${a.x}" y1="${a.y}" x2="${b.x}" y2="${b.y}"
        stroke="rgba(255,255,255,0.2)" stroke-width="${e.weight || 1.5}"
        data-edge="${e.source}|${e.target}" style="cursor:pointer"/>`;

      if (e.label) {
        svg += `<text x="${mx}" y="${my - 6}" text-anchor="middle"
          fill="rgba(255,255,255,0.4)" font-size="10">${e.label}</text>`;
      }
    }

    // Nodes
    for (const n of this.nodes) {
      const color = n.color || '#0db8de';
      svg += `<circle cx="${n.x}" cy="${n.y}" r="20" fill="${color}" opacity="0.8"
        data-node="${n.id}" style="cursor:pointer"/>`;
      svg += `<text x="${n.x}" y="${n.y + 4}" text-anchor="middle"
        fill="white" font-size="11" font-weight="600" pointer-events="none">${n.shortLabel || n.label?.substring(0, 6) || n.id.substring(0, 6)}</text>`;
      svg += `<text x="${n.x}" y="${n.y + 36}" text-anchor="middle"
        fill="rgba(255,255,255,0.6)" font-size="10" pointer-events="none">${n.label || n.id}</text>`;

      // Tag pills below
      if (n.tags) {
        let tx = n.x - (n.tags.length * 18);
        for (const tag of n.tags.slice(0, 4)) {
          svg += `<rect x="${tx}" y="${n.y + 42}" width="${tag.length * 6 + 8}" height="14" rx="7"
            fill="rgba(255,255,255,0.1)" stroke="rgba(255,255,255,0.2)" stroke-width="0.5"/>`;
          svg += `<text x="${tx + tag.length * 3 + 4}" y="${n.y + 52}" text-anchor="middle"
            fill="rgba(255,255,255,0.5)" font-size="8">${tag}</text>`;
          tx += tag.length * 6 + 12;
        }
      }
    }

    svg += '</svg>';

    this.container.innerHTML = `<div class="graph-container">${svg}</div>`;

    // Event listeners
    this.container.querySelectorAll('[data-node]').forEach(el => {
      el.addEventListener('click', () => this.onNodeClick?.(el.dataset.node));
    });
    this.container.querySelectorAll('[data-edge]').forEach(el => {
      el.addEventListener('click', () => {
        const [s, t] = el.dataset.edge.split('|');
        this.onEdgeClick?.({ source: s, target: t });
      });
    });
  }
}
