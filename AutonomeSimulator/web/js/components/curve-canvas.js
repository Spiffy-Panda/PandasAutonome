export class CurveCanvas {
  constructor(canvas, options = {}) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.w = options.width || canvas.width;
    this.h = options.height || canvas.height;
    canvas.width = this.w;
    canvas.height = this.h;
    this.pad = options.padding || 30;
    this.curves = {};
    this.highlight = null;
    this.colors = [
      '#e94560', '#0db8de', '#66cc88', '#ccbb44',
      '#a066cc', '#e07040', '#cc66aa', '#66bbbb'
    ];
  }

  setCurves(presets) {
    this.curves = presets;
    this.render();
  }

  setHighlight(name) {
    this.highlight = name;
    this.render();
  }

  render() {
    const { ctx, w, h, pad } = this;
    const pw = w - pad * 2;
    const ph = h - pad * 2;

    ctx.clearRect(0, 0, w, h);

    // Grid
    ctx.strokeStyle = 'rgba(255,255,255,0.08)';
    ctx.lineWidth = 1;
    for (let i = 0; i <= 4; i++) {
      const x = pad + (pw * i / 4);
      const y = pad + (ph * i / 4);
      ctx.beginPath(); ctx.moveTo(x, pad); ctx.lineTo(x, pad + ph); ctx.stroke();
      ctx.beginPath(); ctx.moveTo(pad, y); ctx.lineTo(pad + pw, y); ctx.stroke();
    }

    // Axes
    ctx.strokeStyle = 'rgba(255,255,255,0.25)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(pad, pad); ctx.lineTo(pad, pad + ph); ctx.lineTo(pad + pw, pad + ph);
    ctx.stroke();

    // Labels
    ctx.fillStyle = 'rgba(255,255,255,0.4)';
    ctx.font = '10px sans-serif';
    ctx.textAlign = 'center';
    ctx.fillText('0', pad, pad + ph + 14);
    ctx.fillText('0.5', pad + pw / 2, pad + ph + 14);
    ctx.fillText('1', pad + pw, pad + ph + 14);
    ctx.textAlign = 'right';
    ctx.fillText('0', pad - 4, pad + ph + 3);
    ctx.fillText('1', pad - 4, pad + 3);

    // Draw curves
    const names = Object.keys(this.curves);
    let colorIdx = 0;
    for (const name of names) {
      const preset = this.curves[name];
      const keys = preset.keys || preset;
      const isHighlighted = this.highlight === name;
      const isOther = this.highlight && !isHighlighted;

      ctx.strokeStyle = isOther ? 'rgba(255,255,255,0.1)' : this.colors[colorIdx % this.colors.length];
      ctx.lineWidth = isHighlighted ? 3 : 1.5;
      ctx.globalAlpha = isOther ? 0.3 : 1;

      this._drawCurve(keys, pad, pad, pw, ph);

      // Keyframe dots for highlighted curve
      if (isHighlighted) {
        for (const k of keys) {
          const cx = pad + k.time * pw;
          const cy = pad + (1 - k.value) * ph;
          ctx.fillStyle = this.colors[colorIdx % this.colors.length];
          ctx.beginPath();
          ctx.arc(cx, cy, 4, 0, Math.PI * 2);
          ctx.fill();
        }
      }

      ctx.globalAlpha = 1;
      colorIdx++;
    }

    // Legend
    if (names.length > 0 && this.w >= 400) {
      let ly = pad + 4;
      colorIdx = 0;
      for (const name of names) {
        ctx.fillStyle = this.colors[colorIdx % this.colors.length];
        ctx.globalAlpha = this.highlight && this.highlight !== name ? 0.3 : 1;
        ctx.fillRect(pad + pw - 100, ly, 12, 12);
        ctx.fillStyle = 'rgba(255,255,255,0.7)';
        ctx.font = '11px sans-serif';
        ctx.textAlign = 'left';
        ctx.fillText(name, pad + pw - 84, ly + 10);
        ly += 18;
        colorIdx++;
      }
      ctx.globalAlpha = 1;
    }
  }

  _drawCurve(keys, ox, oy, pw, ph) {
    if (!keys || keys.length < 2) return;
    const ctx = this.ctx;

    ctx.beginPath();
    const toX = (v) => ox + v * pw;
    const toY = (v) => oy + (1 - v) * ph;

    // Sample the Hermite curve at fine intervals for smooth rendering
    const steps = 100;
    for (let s = 0; s <= steps; s++) {
      const x = s / steps;
      const y = this._evaluateHermite(keys, x);
      if (s === 0) {
        ctx.moveTo(toX(x), toY(y));
      } else {
        ctx.lineTo(toX(x), toY(y));
      }
    }

    ctx.stroke();
  }

  _evaluateHermite(keys, x) {
    if (keys.length === 0) return 0;
    if (keys.length === 1) return Math.max(0, Math.min(1, keys[0].value));

    x = Math.max(0, Math.min(1, x));

    // Find segment
    let i = 0;
    for (; i < keys.length - 2; i++) {
      if (x < keys[i + 1].time) break;
    }

    const k0 = keys[i];
    const k1 = keys[i + 1];
    const segWidth = k1.time - k0.time;
    if (segWidth <= 0) return Math.max(0, Math.min(1, k0.value));

    const t = (x - k0.time) / segWidth;
    const m0 = (k0.outTangent || 0) * segWidth;
    const m1 = (k1.inTangent || 0) * segWidth;

    const t2 = t * t;
    const t3 = t2 * t;

    const h00 = 2 * t3 - 3 * t2 + 1;
    const h10 = t3 - 2 * t2 + t;
    const h01 = -2 * t3 + 3 * t2;
    const h11 = t3 - t2;

    const y = h00 * k0.value + h10 * m0 + h01 * k1.value + h11 * m1;
    return Math.max(0, Math.min(1, y));
  }

  // Render a single named curve (for thumbnails)
  renderSingle(keys, color = '#0db8de') {
    const { ctx, w, h } = this;
    const pad = 4;
    const pw = w - pad * 2;
    const ph = h - pad * 2;

    ctx.clearRect(0, 0, w, h);

    // Light grid
    ctx.strokeStyle = 'rgba(255,255,255,0.06)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(pad, pad + ph / 2); ctx.lineTo(pad + pw, pad + ph / 2);
    ctx.moveTo(pad + pw / 2, pad); ctx.lineTo(pad + pw / 2, pad + ph);
    ctx.stroke();

    ctx.strokeStyle = color;
    ctx.lineWidth = 2;
    this._drawCurve(keys, pad, pad, pw, ph);
  }
}
