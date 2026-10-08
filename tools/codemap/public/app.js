'use strict';
// CodeMap -- клиент. Без библиотек: force-directed раскладка и отрисовка на canvas.

const $ = s => document.querySelector(s);
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const store = {
  get(k, d) { try { const v = localStorage.getItem('codemap.' + k); return v === null ? d : JSON.parse(v); } catch { return d; } },
  set(k, v) { try { localStorage.setItem('codemap.' + k, JSON.stringify(v)); } catch { /* приватный режим */ } },
};

// ------------------------------------------------------------------ справочники

const EDGE_TYPES = {
  uses:       { label: 'использует',            color: '#7d889e', len: 130, k: 0.6 },
  component:  { label: 'GetComponent / Find',   color: '#57c7ff', len: 120, k: 0.8 },
  event:      { label: 'подписка на событие',   color: '#ff9f43', len: 120, k: 0.8 },
  inherits:   { label: 'наследует',             color: '#f2a93b', len: 80,  k: 1.2 },
  implements: { label: 'реализует интерфейс',   color: '#e5c07b', len: 90,  k: 1.0 },
  tests:      { label: 'тестирует',             color: '#4fd18b', len: 120, k: 0.7 },
  scene:      { label: 'на сцене / в префабе',  color: '#c792ea', len: 110, k: 0.5 },
  nested:     { label: 'вложенный тип',         color: '#4b556a', len: 45,  k: 1.5 },
};
const KINDS = {
  mono:       { label: 'MonoBehaviour',       color: '#57c7ff' },
  so:         { label: 'ScriptableObject',    color: '#f2a93b' },
  static:     { label: 'static-класс',        color: '#9b7bff' },
  class:      { label: 'обычный класс',       color: '#8fa4c4' },
  struct:     { label: 'struct',              color: '#7ad3c5' },
  enum:       { label: 'enum',                color: '#6b7a90' },
  interface:  { label: 'interface',           color: '#e5c07b' },
  editor:     { label: 'Editor-инструмент',   color: '#ff7eb6' },
  test:       { label: 'тест',                color: '#4fd18b' },
  scene:      { label: 'сцена',               color: '#c792ea' },
  prefab:     { label: 'префаб',              color: '#b48ead' },
  'so-asset': { label: 'ассет данных',        color: '#d19a66' },
};
const PALETTE = ['#57c7ff', '#f2a93b', '#9b7bff', '#4fd18b', '#ff7eb6', '#ff9f43', '#7ad3c5', '#e5c07b', '#c792ea', '#ff5d6c', '#8fa4c4', '#a3e635', '#38bdf8', '#f472b6'];
const COLOR_MODES = {
  folder:     'Папка',
  kind:       'Тип',
  complexity: 'Сложность',
  size:       'Размер',
  coverage:   'Тесты',
  churn:      'Git',
  lint:       'Lint',
};

// ------------------------------------------------------------------ состояние

const state = {
  graph: null,
  nodes: [], edges: [], byId: new Map(),
  vNodes: [], vEdges: [],
  neighbors: new Map(),
  catColor: {},
  colorMode: store.get('colorMode', 'folder'),
  edgeOn: store.get('edgeOn', { uses: true, component: true, event: true, inherits: true, implements: true, tests: true, scene: true, nested: false }),
  opts: store.get('opts', { assets: true, tests: true, editor: true, small: false, third: false, cluster: true, labels: false }),
  cats: store.get('cats', {}),
  selected: null, hover: null,
  searchHits: null,
  view: 'graph',
  frozen: false,
  alpha: 1,
  pulses: new Map(),
  history: [],
  feedUnread: 0,
  cam: { x: 0, y: 0, s: 1 },
  fitted: false,
  p95: { complexity: 1, lines: 1, churn: 1 },
};

// ------------------------------------------------------------------ загрузка данных

async function loadGraph(update) {
  const res = await fetch('/api/graph', { cache: 'no-store' });
  const g = await res.json();
  mergeGraph(g, update);
}

function mergeGraph(g, update) {
  const old = state.byId;
  state.graph = g;
  $('#projectName').textContent = '· ' + (g.root || '');

  const cats = [...new Set(g.nodes.map(n => n.category))].sort();
  cats.forEach((c, i) => { if (!state.catColor[c]) state.catColor[c] = c === 'ThirdParty' ? '#4b556a' : PALETTE[i % PALETTE.length]; });

  const nodes = g.nodes.map(n => {
    const o = old.get(n.id);
    const r = n.asset ? 9 : Math.max(5, Math.min(26, 4 + Math.sqrt(n.lines || 1) * 0.9));
    if (o) return Object.assign(n, { x: o.x, y: o.y, vx: o.vx, vy: o.vy, pinned: o.pinned, r });
    // новый узел -- рядом с соседями или у якоря папки
    return Object.assign(n, { x: (Math.random() - 0.5) * 600, y: (Math.random() - 0.5) * 400, vx: 0, vy: 0, r, isNew: true });
  });
  state.nodes = nodes;
  state.byId = new Map(nodes.map(n => [n.id, n]));
  state.edges = g.edges.filter(e => state.byId.has(e.from) && state.byId.has(e.to)).map(e => Object.assign(e, { a: state.byId.get(e.from), b: state.byId.get(e.to) }));

  for (const n of nodes) if (n.isNew && old.size) {
    const nb = state.edges.find(e => e.a === n && !e.b.isNew) || state.edges.find(e => e.b === n && !e.a.isNew);
    if (nb) { const o = nb.a === n ? nb.b : nb.a; n.x = o.x + (Math.random() - 0.5) * 80; n.y = o.y + (Math.random() - 0.5) * 80; }
    delete n.isNew;
  }

  const own = nodes.filter(n => !n.asset && !n.thirdParty);
  const pct = (arr, q) => { const s = arr.filter(v => v > 0).sort((a, b) => a - b); return s.length ? s[Math.floor((s.length - 1) * q)] : 1; };
  state.p95 = { complexity: pct(own.map(n => n.complexity), 0.95), lines: pct(own.map(n => n.lines), 0.95), churn: pct(own.map(n => n.churn), 0.95) || 1 };

  state.history.push({ v: g.version, at: Date.now(), ...pickStats(g.stats) });
  if (state.history.length > 200) state.history.shift();

  if (state.selected) state.selected = state.byId.get(state.selected.id) || null;
  if (update) {
    const now = performance.now();
    for (const id of update.added || []) state.pulses.set(id, { t: now, color: '#4fd18b' });
    for (const c of update.changed || []) state.pulses.set(c.id, { t: now, color: '#f2a93b' });
  }

  buildSidebar();
  applyFilters();
  // первая загрузка: стартуем каждый узел у якоря своей папки -- раскладка сходится быстрее и ровнее
  if (!old.size) for (const n of state.nodes) {
    const an = state.anchors[n.category];
    if (an) { n.x = an.x + (Math.random() - 0.5) * 160; n.y = an.y + (Math.random() - 0.5) * 160; }
  }
  if (!old.size) {
    // сразу прокручиваем раскладку вперёд и ставим камеру -- без "прыжка" через пару секунд
    for (let i = 0; i < 260 && state.alpha > 0.05; i++) tick();
    fitView(state.vNodes, 70, true);
  }
  reheat(update ? 0.35 : 1);
  if (state.selected) renderDetails(state.selected);
  if (state.view === 'stats') renderStats();
  renderOverview();
}

function pickStats(s) {
  return { lines: s.lines, types: s.types, methods: s.methods, edges: s.edges, tests: s.tests, lint: s.lintWarnings, tested: Math.round(s.testedRatio * 100), complexity: state.nodes.reduce((t, n) => t + (n.asset || n.thirdParty ? 0 : n.complexity), 0) };
}

// ------------------------------------------------------------------ фильтры

function isVisible(n) {
  const o = state.opts;
  if (n.thirdParty && !o.third) return false;
  if (n.asset && !o.assets) return false;
  if (n.kind === 'test' && !o.tests) return false;
  if (n.kind === 'editor' && !o.editor) return false;
  if (!o.small && (n.kind === 'enum' || n.kind === 'struct' || (n.parent && !n.asset))) return false;
  if (state.cats[n.category] === false) return false;
  return true;
}

function applyFilters() {
  state.vNodes = state.nodes.filter(isVisible);
  const vis = new Set(state.vNodes);
  state.vEdges = state.edges.filter(e => state.edgeOn[e.type] && vis.has(e.a) && vis.has(e.b));
  state.neighbors = new Map(state.vNodes.map(n => [n, new Set()]));
  for (const e of state.vEdges) { state.neighbors.get(e.a).add(e.b); state.neighbors.get(e.b).add(e.a); }
  // якоря папок по кругу -- для группировки
  const cats = [...new Set(state.vNodes.map(n => n.category))].sort();
  const R = 220 + 70 * Math.sqrt(state.vNodes.length);
  state.anchors = {};
  cats.forEach((c, i) => { const a = (i / cats.length) * Math.PI * 2 - Math.PI / 2; state.anchors[c] = { x: Math.cos(a) * R, y: Math.sin(a) * R * 0.75 }; });
  renderOverview();
  updateLegend();
}

// ------------------------------------------------------------------ цвета

function lerpColor(a, b, t) {
  const pa = parseInt(a.slice(1), 16), pb = parseInt(b.slice(1), 16);
  const r = ((pa >> 16) & 255) + (((pb >> 16) & 255) - ((pa >> 16) & 255)) * t;
  const g = ((pa >> 8) & 255) + (((pb >> 8) & 255) - ((pa >> 8) & 255)) * t;
  const bl = (pa & 255) + ((pb & 255) - (pa & 255)) * t;
  return `rgb(${r | 0},${g | 0},${bl | 0})`;
}
const heat = t => { t = Math.max(0, Math.min(1, t)); return t < 0.5 ? lerpColor('#3fb27f', '#f2c14e', t * 2) : lerpColor('#f2c14e', '#ff5d6c', (t - 0.5) * 2); };

function nodeColor(n) {
  const m = state.colorMode;
  if (n.asset && m !== 'folder' && m !== 'kind' && m !== 'churn') return '#5d4a72';
  switch (m) {
    case 'kind': return (KINDS[n.kind] || KINDS.class).color;
    case 'complexity': return heat(Math.log(n.complexity || 1) / Math.log(Math.max(2, state.p95.complexity)));
    case 'size': return lerpColor('#24435c', '#57c7ff', Math.min(1, Math.sqrt((n.lines || 0) / state.p95.lines)));
    case 'coverage':
      if (n.kind === 'test') return '#3d7bff';
      if (['editor', 'enum', 'interface'].includes(n.kind) || !(n.methods || []).length) return '#3a4252';
      return n.testedBy && n.testedBy.length ? '#4fd18b' : '#ff5d6c';
    case 'churn':
      if (n.gitStatus === 'new') return '#4fd18b';
      if (n.gitStatus === 'modified') return '#f2a93b';
      return n.churn ? lerpColor('#2d3a55', '#9b7bff', Math.min(1, n.churn / state.p95.churn)) : '#2a3040';
    case 'lint': {
      const l = n.lint || [];
      if (l.some(x => x.level === 'ERROR')) return '#ff5d6c';
      if (l.length) return '#f2b33b';
      return n.asset ? '#3a4252' : '#3f6b57';
    }
    default: return state.catColor[n.category] || '#8fa4c4';
  }
}

function updateLegend() {
  const el = $('#legend');
  const row = (c, t) => `<div class="row"><span class="sw" style="background:${c}"></span>${esc(t)}</div>`;
  const grad = (a, b, l, r) => `<div class="grad" style="background:linear-gradient(90deg,${a},${b})"></div><div class="ends"><span>${l}</span><span>${r}</span></div>`;
  switch (state.colorMode) {
    case 'kind': {
      const used = new Set(state.vNodes.map(n => n.kind));
      el.innerHTML = Object.entries(KINDS).filter(([k]) => used.has(k)).map(([, v]) => row(v.color, v.label)).join(''); break;
    }
    case 'complexity': el.innerHTML = `<div class="grad" style="background:linear-gradient(90deg,#3fb27f,#f2c14e,#ff5d6c)"></div><div class="ends"><span>простые</span><span>≥ ${state.p95.complexity} ветвлений</span></div>`; break;
    case 'size': el.innerHTML = grad('#24435c', '#57c7ff', 'мало строк', `≥ ${state.p95.lines} строк`) + '<div class="note">Размер кружка — тоже строки кода</div>'; break;
    case 'coverage': el.innerHTML = row('#4fd18b', 'есть тесты') + row('#ff5d6c', 'нет тестов') + row('#3d7bff', 'сам тест') + row('#3a4252', 'не применимо') +
      `<div class="note">Покрытие — диагностика: где нет ни одного теста. Не gate.</div>`; break;
    case 'churn': el.innerHTML = row('#4fd18b', 'новый (не в git)') + row('#f2a93b', 'изменён (не закоммичен)') + grad('#2d3a55', '#9b7bff', 'редко правят', 'часто правят') +
      (state.graph && !state.graph.features.git ? '<div class="note">git недоступен</div>' : ''); break;
    case 'lint': el.innerHTML = row('#ff5d6c', 'ошибки') + row('#f2b33b', 'предупреждения') + row('#3f6b57', 'чисто') +
      (state.graph && !state.graph.features.lint ? '<div class="note">Python/ci/lint_cs.py не найден</div>' : ''); break;
    default: {
      const cats = [...new Set(state.vNodes.map(n => n.category))].sort();
      el.innerHTML = cats.map(c => row(state.catColor[c], c)).join('');
    }
  }
}

// ------------------------------------------------------------------ боковая панель

function buildSidebar() {
  const modes = $('#colorModes');
  modes.innerHTML = Object.entries(COLOR_MODES).map(([k, v]) => `<button data-mode="${k}" class="${k === state.colorMode ? 'active' : ''}">${v}</button>`).join('');

  const counts = {};
  for (const e of state.edges) counts[e.type] = (counts[e.type] || 0) + 1;
  $('#edgeToggles').innerHTML = Object.entries(EDGE_TYPES).map(([k, v]) =>
    `<span class="chip ${state.edgeOn[k] ? 'on' : ''}" data-edge="${k}"><i style="background:${v.color}"></i>${v.label}<b>${counts[k] || 0}</b></span>`).join('');

  const catCount = {};
  for (const n of state.nodes) catCount[n.category] = (catCount[n.category] || 0) + 1;
  $('#categories').innerHTML = Object.keys(catCount).sort().map(c =>
    `<label class="check"><span><input type="checkbox" data-cat="${esc(c)}" ${state.cats[c] === false ? '' : 'checked'}><i class="sw" style="background:${state.catColor[c]}"></i>${esc(c)}</span><small>${catCount[c]}</small></label>`).join('');

  for (const [id, key] of [['optAssets', 'assets'], ['optTests', 'tests'], ['optEditor', 'editor'], ['optSmall', 'small'], ['optThird', 'third'], ['optCluster', 'cluster'], ['optLabels', 'labels']])
    $('#' + id).checked = !!state.opts[key];
}

function wireSidebar() {
  $('#colorModes').addEventListener('click', e => {
    const b = e.target.closest('button[data-mode]'); if (!b) return;
    state.colorMode = b.dataset.mode; store.set('colorMode', state.colorMode);
    buildSidebar(); updateLegend();
  });
  $('#edgeToggles').addEventListener('click', e => {
    const c = e.target.closest('[data-edge]'); if (!c) return;
    state.edgeOn[c.dataset.edge] = !state.edgeOn[c.dataset.edge]; store.set('edgeOn', state.edgeOn);
    buildSidebar(); applyFilters(); reheat(0.4);
  });
  $('#categories').addEventListener('change', e => {
    const c = e.target.dataset.cat; if (!c) return;
    state.cats[c] = e.target.checked; store.set('cats', state.cats);
    applyFilters(); reheat(0.4);
  });
  $('#catAll').addEventListener('click', () => { state.cats = {}; store.set('cats', {}); buildSidebar(); applyFilters(); reheat(0.4); });
  for (const [id, key] of [['optAssets', 'assets'], ['optTests', 'tests'], ['optEditor', 'editor'], ['optSmall', 'small'], ['optThird', 'third'], ['optCluster', 'cluster'], ['optLabels', 'labels']]) {
    $('#' + id).addEventListener('change', e => {
      state.opts[key] = e.target.checked; store.set('opts', state.opts);
      if (key !== 'labels') { applyFilters(); reheat(0.5); }
    });
  }
}

// ------------------------------------------------------------------ физика

function reheat(a = 1) { if (!state.frozen) state.alpha = Math.max(state.alpha, a); }

function tick() {
  const ns = state.vNodes, alpha = state.alpha;
  if (alpha < 0.004 || state.frozen) return false;
  const n = ns.length;
  for (const a of ns) { a.fx = 0; a.fy = 0; }

  // отталкивание
  for (let i = 0; i < n; i++) {
    const a = ns[i];
    for (let j = i + 1; j < n; j++) {
      const b = ns[j];
      let dx = a.x - b.x, dy = a.y - b.y;
      let d2 = dx * dx + dy * dy;
      if (d2 < 0.01) { dx = Math.random() - 0.5; dy = Math.random() - 0.5; d2 = 0.5; }
      if (d2 > 640000) continue;
      const min = a.r + b.r + 18;
      const f = (4200 + (a.r + b.r) * 90) / d2 + (d2 < min * min ? 1.5 : 0);
      const d = Math.sqrt(d2);
      const fx = dx / d * f, fy = dy / d * f;
      a.fx += fx; a.fy += fy; b.fx -= fx; b.fy -= fy;
    }
  }
  // пружины
  for (const e of state.vEdges) {
    const t = EDGE_TYPES[e.type];
    const a = e.a, b = e.b;
    const dx = b.x - a.x, dy = b.y - a.y;
    const d = Math.sqrt(dx * dx + dy * dy) || 0.01;
    const L = t.len + a.r + b.r;
    const w = Math.min(2.2, 1 + Math.log(e.weight || 1) * 0.25);
    const f = (d - L) * 0.012 * t.k * w;
    const fx = dx / d * f, fy = dy / d * f;
    a.fx += fx; a.fy += fy; b.fx -= fx; b.fy -= fy;
  }
  // гравитация к центру / к якорю папки
  for (const a of ns) {
    if (state.opts.cluster && state.anchors[a.category]) {
      const an = state.anchors[a.category];
      a.fx += (an.x - a.x) * 0.02; a.fy += (an.y - a.y) * 0.02;
    }
    a.fx -= a.x * 0.0025; a.fy -= a.y * 0.0025;
  }
  for (const a of ns) {
    if (a.pinned || a === drag.node) { a.vx = a.vy = 0; continue; }
    a.vx = (a.vx + a.fx * alpha) * 0.62;
    a.vy = (a.vy + a.fy * alpha) * 0.62;
    const sp = Math.hypot(a.vx, a.vy);
    if (sp > 40) { a.vx *= 40 / sp; a.vy *= 40 / sp; }
    a.x += a.vx; a.y += a.vy;
  }
  state.alpha *= 0.988;
  return true;
}

// ------------------------------------------------------------------ отрисовка

const canvas = $('#canvas');
const ctx = canvas.getContext('2d');
let W = 0, H = 0, DPR = 1;

function resize() {
  const r = canvas.getBoundingClientRect();
  DPR = window.devicePixelRatio || 1;
  W = r.width; H = r.height;
  canvas.width = Math.max(1, W * DPR); canvas.height = Math.max(1, H * DPR);
}
new ResizeObserver(resize).observe(canvas);

const toScreen = (x, y) => [(x - state.cam.x) * state.cam.s + W / 2, (y - state.cam.y) * state.cam.s + H / 2];
const toWorld = (sx, sy) => [(sx - W / 2) / state.cam.s + state.cam.x, (sy - H / 2) / state.cam.s + state.cam.y];

function fitView(nodes = state.vNodes, pad = 80, instant = false) {
  if (!nodes.length || !W) return;
  let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
  for (const n of nodes) { x0 = Math.min(x0, n.x - n.r); y0 = Math.min(y0, n.y - n.r); x1 = Math.max(x1, n.x + n.r); y1 = Math.max(y1, n.y + n.r); }
  const s = Math.min((W - pad * 2) / Math.max(1, x1 - x0), (H - pad * 2) / Math.max(1, y1 - y0), 2.2);
  const to = { x: (x0 + x1) / 2, y: (y0 + y1) / 2, s: Math.max(0.15, s) };
  if (instant) Object.assign(state.cam, to); else animateCam(to);
}

let camAnim = null;
function animateCam(to, ms = 450) {
  const from = { ...state.cam }, t0 = performance.now();
  camAnim = t => {
    const k = Math.min(1, (t - t0) / ms), e = 1 - Math.pow(1 - k, 3);
    state.cam.x = from.x + (to.x - from.x) * e; state.cam.y = from.y + (to.y - from.y) * e; state.cam.s = from.s + (to.s - from.s) * e;
    if (k >= 1) camAnim = null;
  };
}

function focusSet() {
  const f = state.selected || state.hover;
  if (!f || !state.neighbors.has(f)) return null;
  const s = new Set([f, ...state.neighbors.get(f)]);
  return s;
}

function draw(now) {
  ctx.setTransform(DPR, 0, 0, DPR, 0, 0);
  ctx.clearRect(0, 0, W, H);
  const cam = state.cam;
  ctx.setTransform(DPR * cam.s, 0, 0, DPR * cam.s, DPR * (W / 2 - cam.x * cam.s), DPR * (H / 2 - cam.y * cam.s));

  const focus = focusSet();
  const hits = state.searchHits;

  // подписи папок при группировке
  if (state.opts.cluster && cam.s < 1.6) {
    ctx.font = `600 ${14 / Math.max(cam.s, 0.5)}px Segoe UI, sans-serif`;
    ctx.textAlign = 'center';
    for (const [c, an] of Object.entries(state.anchors || {})) {
      const members = state.vNodes.filter(n => n.category === c);
      if (!members.length) continue;
      let cx = 0, cy = 0, minY = Infinity;
      for (const m of members) { cx += m.x; cy += m.y; minY = Math.min(minY, m.y - m.r); }
      ctx.fillStyle = (state.catColor[c] || '#888') + '55';
      ctx.fillText(c, cx / members.length, minY - 18 / Math.max(cam.s, 0.5));
    }
  }

  // рёбра
  for (const e of state.vEdges) {
    const t = EDGE_TYPES[e.type];
    const active = focus ? (focus.has(e.a) && focus.has(e.b) && (e.a === (state.selected || state.hover) || e.b === (state.selected || state.hover))) : true;
    const dim = (focus && !active) || (hits && !(hits.has(e.a) || hits.has(e.b)));
    const a = e.a, b = e.b;
    const dx = b.x - a.x, dy = b.y - a.y, d = Math.hypot(dx, dy) || 1;
    const ux = dx / d, uy = dy / d;
    const x1 = a.x + ux * a.r, y1 = a.y + uy * a.r, x2 = b.x - ux * (b.r + 2), y2 = b.y - uy * (b.r + 2);
    // лёгкий изгиб, чтобы встречные связи не сливались
    const mx = (x1 + x2) / 2 - uy * d * 0.08, my = (y1 + y2) / 2 + ux * d * 0.08;
    ctx.globalAlpha = dim ? 0.06 : focus ? 0.95 : 0.42;
    ctx.strokeStyle = t.color;
    ctx.lineWidth = (focus && active ? 2 : 1) * Math.min(3, 0.8 + Math.log(e.weight || 1) * 0.5) / Math.max(0.6, Math.min(cam.s, 1.5));
    if (e.type === 'tests') ctx.setLineDash([6 / cam.s, 4 / cam.s]); else ctx.setLineDash([]);
    ctx.beginPath(); ctx.moveTo(x1, y1); ctx.quadraticCurveTo(mx, my, x2, y2); ctx.stroke();
    // стрелка
    if (!dim && cam.s > 0.35) {
      const ax = x2 - mx, ay = y2 - my, al = Math.hypot(ax, ay) || 1;
      const hx = ax / al, hy = ay / al, s = 7 / Math.max(0.7, Math.min(cam.s, 1.4));
      ctx.setLineDash([]);
      ctx.fillStyle = t.color;
      ctx.beginPath(); ctx.moveTo(x2, y2); ctx.lineTo(x2 - hx * s - hy * s * 0.5, y2 - hy * s + hx * s * 0.5); ctx.lineTo(x2 - hx * s + hy * s * 0.5, y2 - hy * s - hx * s * 0.5); ctx.closePath(); ctx.fill();
    }
  }
  ctx.setLineDash([]);
  ctx.globalAlpha = 1;

  // узлы
  for (const n of state.vNodes) {
    const dim = (focus && !focus.has(n)) || (hits && !hits.has(n));
    ctx.globalAlpha = dim ? 0.18 : 1;
    const col = nodeColor(n);

    // пульс изменения
    const p = state.pulses.get(n.id);
    if (p) {
      const age = (now - p.t) / 1000;
      if (age > 4) state.pulses.delete(n.id);
      else {
        const k = (age % 1.3) / 1.3;
        ctx.strokeStyle = p.color; ctx.globalAlpha = (1 - k) * (1 - age / 4);
        ctx.lineWidth = 3 / cam.s;
        ctx.beginPath(); ctx.arc(n.x, n.y, n.r + 4 + k * 22, 0, Math.PI * 2); ctx.stroke();
        ctx.globalAlpha = dim ? 0.18 : 1;
      }
    }

    ctx.fillStyle = col;
    ctx.beginPath();
    shape(n);
    ctx.fill();

    // обводки-статусы
    const sel = n === state.selected, hov = n === state.hover;
    if (sel || hov) { ctx.strokeStyle = '#fff'; ctx.lineWidth = 2.5 / cam.s; ctx.stroke(); }
    else if (n.maybeUnused) { ctx.setLineDash([3 / cam.s, 3 / cam.s]); ctx.strokeStyle = '#c9d1e0'; ctx.lineWidth = 1.5 / cam.s; ctx.stroke(); ctx.setLineDash([]); }
    else { ctx.strokeStyle = '#0f1218'; ctx.lineWidth = 1.5 / cam.s; ctx.stroke(); }
    if (n.pinned) { ctx.fillStyle = '#fff'; ctx.beginPath(); ctx.arc(n.x, n.y, 2 / cam.s + 1, 0, Math.PI * 2); ctx.fill(); }

    // значок lint
    const lc = (n.lint || []).length;
    if (lc && state.colorMode !== 'lint') {
      const bx = n.x + n.r * 0.75, by = n.y - n.r * 0.75;
      ctx.fillStyle = n.lint.some(x => x.level === 'ERROR') ? '#ff5d6c' : '#f2b33b';
      ctx.beginPath(); ctx.arc(bx, by, 4.5 / Math.min(cam.s, 1.3) + 1, 0, Math.PI * 2); ctx.fill();
    }
    // git: дужка только у изменённых (новых обычно слишком много -- для них есть режим "Git")
    if (n.gitStatus === 'modified' && state.colorMode !== 'churn') {
      ctx.strokeStyle = '#f2a93b';
      ctx.lineWidth = 1.5 / cam.s;
      ctx.beginPath(); ctx.arc(n.x, n.y, n.r + 3.5 / cam.s, -Math.PI * 0.25, Math.PI * 0.25); ctx.stroke();
    }
  }
  ctx.globalAlpha = 1;

  // подписи
  ctx.textAlign = 'center';
  ctx.textBaseline = 'top';
  const fs = 11.5 / Math.max(cam.s, 0.55);
  ctx.font = `500 ${fs}px Segoe UI, sans-serif`;
  ctx.lineJoin = 'round';
  for (const n of state.vNodes) {
    const important = n === state.selected || n === state.hover || (focus && focus.has(n)) || (hits && hits.has(n));
    const show = important || state.opts.labels || cam.s > 0.9 || (cam.s > 0.28 && (n.r > 12 || n.fanIn >= 8 || n.asset && n.kind === 'scene'));
    if (!show) continue;
    if ((focus && !focus.has(n)) || (hits && !hits.has(n))) continue;
    const y = n.y + n.r + 3 / cam.s;
    ctx.lineWidth = 3.5 / cam.s; ctx.strokeStyle = '#0f1218cc'; ctx.strokeText(n.name, n.x, y);
    ctx.fillStyle = important ? '#fff' : '#c9d1e0'; ctx.fillText(n.name, n.x, y);
  }
}

function shape(n) {
  const r = n.r;
  if (n.asset) {
    const k = r * 0.95, rr = r * 0.35;
    ctx.roundRect ? ctx.roundRect(n.x - k, n.y - k, k * 2, k * 2, rr) : ctx.rect(n.x - k, n.y - k, k * 2, k * 2);
  } else if (n.kind === 'interface') {
    ctx.moveTo(n.x, n.y - r * 1.15); ctx.lineTo(n.x + r * 1.15, n.y); ctx.lineTo(n.x, n.y + r * 1.15); ctx.lineTo(n.x - r * 1.15, n.y); ctx.closePath();
  } else if (n.kind === 'static' || n.kind === 'editor') {
    for (let i = 0; i < 6; i++) { const a = Math.PI / 3 * i + Math.PI / 6; ctx[i ? 'lineTo' : 'moveTo'](n.x + Math.cos(a) * r * 1.08, n.y + Math.sin(a) * r * 1.08); }
    ctx.closePath();
  } else {
    ctx.arc(n.x, n.y, r, 0, Math.PI * 2);
  }
}

function loop(now) {
  if (camAnim) camAnim(now);
  if (state.view === 'graph') {
    const steps = state.alpha > 0.3 ? 5 : 2;
    for (let i = 0; i < steps; i++) tick();
    draw(now);
    // подгоняем вид, когда раскладка почти устоялась (и ещё раз чуть позже -- на случай дрейфа)
    if (state.vNodes.length && !camAnim && !drag.pan) {
      if (!state.fitted && state.alpha < 0.12) { state.fitted = 1; fitView(); }
      else if (state.fitted === 1 && state.alpha < 0.03) { state.fitted = 2; fitView(); }
    }
  }
  requestAnimationFrame(loop);
}

// ------------------------------------------------------------------ мышь

const drag = { node: null, pan: false, sx: 0, sy: 0, moved: false, cx: 0, cy: 0 };

function nodeAt(sx, sy) {
  const [wx, wy] = toWorld(sx, sy);
  let best = null, bestD = Infinity;
  for (const n of state.vNodes) {
    const d = Math.hypot(n.x - wx, n.y - wy);
    const hit = n.r + 4 / state.cam.s;
    if (d < hit && d < bestD) { best = n; bestD = d; }
  }
  return best;
}

canvas.addEventListener('mousedown', e => {
  const n = nodeAt(e.offsetX, e.offsetY);
  drag.sx = e.offsetX; drag.sy = e.offsetY; drag.moved = false;
  if (n) { drag.node = n; }
  else { drag.pan = true; drag.cx = state.cam.x; drag.cy = state.cam.y; }
  canvas.classList.add('dragging');
});
window.addEventListener('mousemove', e => {
  const r = canvas.getBoundingClientRect();
  const sx = e.clientX - r.left, sy = e.clientY - r.top;
  if (drag.node || drag.pan) {
    if (Math.hypot(sx - drag.sx, sy - drag.sy) > 3) drag.moved = true;
    if (drag.node && drag.moved) {
      const [wx, wy] = toWorld(sx, sy);
      drag.node.x = wx; drag.node.y = wy; reheat(0.25);
    } else if (drag.pan) {
      state.cam.x = drag.cx - (sx - drag.sx) / state.cam.s;
      state.cam.y = drag.cy - (sy - drag.sy) / state.cam.s;
    }
    hideTooltip();
    return;
  }
  if (e.target !== canvas) return;
  const n = nodeAt(sx, sy);
  if (n !== state.hover) { state.hover = n; canvas.classList.toggle('hovering', !!n); }
  if (n) showTooltip(n, sx, sy); else hideTooltip();
});
window.addEventListener('mouseup', e => {
  if (drag.node) {
    if (!drag.moved) select(drag.node);
    else if (e.shiftKey) drag.node.pinned = !drag.node.pinned;
  } else if (drag.pan && !drag.moved && e.target === canvas) select(null);
  drag.node = null; drag.pan = false;
  canvas.classList.remove('dragging');
});
canvas.addEventListener('dblclick', e => {
  const n = nodeAt(e.offsetX, e.offsetY);
  if (n) { select(n); animateCam({ x: n.x, y: n.y, s: Math.max(state.cam.s, 1.6) }); }
  else fitView();
});
canvas.addEventListener('wheel', e => {
  e.preventDefault();
  const [wx, wy] = toWorld(e.offsetX, e.offsetY);
  const s = Math.max(0.08, Math.min(5, state.cam.s * Math.exp(-e.deltaY * 0.0015)));
  state.cam.s = s;
  state.cam.x = wx - (e.offsetX - W / 2) / s;
  state.cam.y = wy - (e.offsetY - H / 2) / s;
}, { passive: false });
canvas.addEventListener('mouseleave', () => { state.hover = null; hideTooltip(); });

function showTooltip(n, sx, sy) {
  const t = $('#tooltip');
  const kind = (KINDS[n.kind] || {}).label || n.kind;
  const rows = n.asset
    ? [['скриптов', Object.keys(n).length && state.edges.filter(e => e.a === n).length]]
    : [['строк', n.lines], ['методов', (n.methods || []).length], ['сложность', n.complexity], ['входящих / исходящих', `${n.fanIn} / ${n.fanOut}`]];
  if (n.testedBy && n.testedBy.length) rows.push(['тесты', n.testedBy.length]);
  if ((n.lint || []).length) rows.push(['lint', n.lint.length]);
  t.innerHTML = `<b>${esc(n.name)}</b> <span class="muted">· ${esc(kind)}</span><div class="muted" style="font-family:var(--mono);font-size:11px">${esc(n.file)}</div>
    <div class="kv">${rows.map(([k, v]) => `<span>${k}</span><span>${esc(v)}</span>`).join('')}</div>
    ${n.maybeUnused ? '<div style="margin-top:4px;color:#c9d1e0">⚠ возможно не используется</div>' : ''}`;
  t.hidden = false;
  const tw = t.offsetWidth, th = t.offsetHeight;
  t.style.left = Math.min(sx + 16, W - tw - 8) + 'px';
  t.style.top = Math.min(sy + 16, H - th - 8) + 'px';
}
function hideTooltip() { $('#tooltip').hidden = true; }

// ------------------------------------------------------------------ выбор и детали

function select(n, center) {
  state.selected = n;
  if (!n) { $('#details').hidden = true; return; }
  if (!state.vNodes.includes(n)) {
    // узел скрыт фильтрами -- показываем его категорию/слой
    if (state.cats[n.category] === false) state.cats[n.category] = true;
    if (n.asset) state.opts.assets = true;
    if (n.kind === 'test') state.opts.tests = true;
    if (n.kind === 'editor') state.opts.editor = true;
    if (n.thirdParty) state.opts.third = true;
    if (n.kind === 'enum' || n.kind === 'struct' || n.parent) state.opts.small = true;
    store.set('opts', state.opts); store.set('cats', state.cats);
    buildSidebar(); applyFilters();
  }
  renderDetails(n);
  if (center) animateCam({ x: n.x, y: n.y, s: Math.max(state.cam.s, 1.2) });
}

function vscodeLink(file, line) {
  const root = (state.graph.rootPath || '').replace(/\\/g, '/');
  return `vscode://file/${encodeURI(root + '/' + file)}:${line || 1}`;
}

function renderDetails(n) {
  const d = $('#details');
  d.hidden = false;
  const kind = KINDS[n.kind] || KINDS.class;
  const out = state.edges.filter(e => e.a === n && e.type !== 'nested');
  const inc = state.edges.filter(e => e.b === n && e.type !== 'nested');
  const p95c = state.p95.complexity;
  const metric = (v, l, cls = '') => `<div class="metric ${cls}"><b>${v}</b><span>${l}</span></div>`;
  const tags = [`<span class="tag kind" style="background:${kind.color}">${esc(kind.label)}</span>`, `<span class="tag" style="border-color:${state.catColor[n.category]}">${esc(n.category)}</span>`];
  if (n.gitStatus) tags.push(`<span class="tag ${n.gitStatus}">${n.gitStatus === 'new' ? 'новый' : 'изменён'}</span>`);
  if (n.maybeUnused) tags.push(`<span class="tag unused">возможно не используется</span>`);
  if (n.testedBy && n.testedBy.length) tags.push(`<span class="tag" style="color:#4fd18b">тесты: ${n.testedBy.length}</span>`);
  for (const a of n.attrs || []) tags.push(`<span class="tag">[${esc(a)}]</span>`);

  const relList = (edges, dir) => {
    const groups = {};
    for (const e of edges) (groups[e.type] ||= []).push(e);
    return Object.entries(groups).map(([type, es]) => `
      <div style="margin:4px 0 2px;font-size:11px;color:${EDGE_TYPES[type].color}">${EDGE_TYPES[type].label}</div>
      ${es.sort((x, y) => y.weight - x.weight).map(e => { const o = dir === 'out' ? e.b : e.a; return `<div class="li" data-go="${esc(o.id)}"><span class="ic" style="color:${nodeColor(o)}">●</span><span class="nm">${esc(o.name)}</span><small>${e.label ? esc(e.label) + ' ' : ''}${e.weight > 1 ? '×' + e.weight : ''}</small></div>`; }).join('')}`).join('') || '<div class="empty">нет</div>';
  };

  let html = `<div class="d-title">${esc(n.name)}</div><div class="tags">${tags.join('')}</div>
    <a class="file" href="${vscodeLink(n.file, n.line)}" title="Открыть в VS Code">${esc(n.file)}${n.line ? ':' + n.line : ''}</a>`;

  if (n.asset) {
    html += `<div class="metrics">${metric(out.length, 'скриптов')}${metric((n.bytes / 1024).toFixed(0), 'КБ')}${metric(n.churn || 0, 'коммитов')}</div>
      <div class="d-sec"><h4>Скрипты на объекте</h4><div class="list">${relList(out, 'out')}</div></div>`;
  } else {
    html += `<div class="metrics">
      ${metric(n.lines, 'строк')}${metric(n.methods.length, 'методов')}${metric(n.fields.length, 'полей')}
      ${metric(n.complexity, 'сложность', n.complexity > p95c ? 'hot' : n.complexity > p95c * 0.6 ? 'warm' : '')}
      ${metric(n.fanIn, 'зависят от него', n.fanIn >= 10 ? 'warm' : '')}${metric(n.fanOut, 'зависит от')}
      ${metric(n.churn || 0, 'коммитов')}${metric(n.todo || 0, 'TODO')}</div>`;
    if (n.bases && n.bases.length) html += `<div class="d-sec"><h4>Базовые типы</h4><div class="pills">${n.bases.map(b => `<span class="pill2">${esc(b)}</span>`).join('')}</div></div>`;
    if (n.callbacks && n.callbacks.length) html += `<div class="d-sec"><h4>Unity-колбэки</h4><div class="pills">${n.callbacks.map(c => `<span class="pill2 cb">${c}</span>`).join('')}</div></div>`;
    if ((n.lint || []).length) html += `<div class="d-sec"><h4>Lint <span>${n.lint.length}</span></h4>${n.lint.map(l => `<div class="lint-item ${l.level === 'ERROR' ? 'err' : ''}" data-line="${l.line}"><code>${l.code} :${l.line}</code> ${esc(l.message)}</div>`).join('')}</div>`;
    if (n.methods.length) {
      const maxC = Math.max(...n.methods.map(m => m.complexity || 1), 4);
      html += `<div class="d-sec"><h4>Методы <span>${n.methods.length}</span></h4><div class="list">${[...n.methods].sort((a, b) => a.line - b.line).map(m => `
        <div class="li" data-line="${m.line}" title="${m.access}${m.static ? ' static' : ''} ${esc(m.ret)} ${esc(m.name)} · ${m.lines} строк · сложность ${m.complexity}">
          <span class="ic">${m.test ? '✓' : m.unity ? '◆' : m.access === 'public' ? '+' : '·'}</span>
          <span class="nm" style="${m.unity ? 'color:#ffd88a' : m.test ? 'color:#4fd18b' : ''}">${esc(m.name)}()</span>
          <span class="bar"><i style="width:${Math.min(100, (m.complexity || 1) / maxC * 100)}%;background:${heat((m.complexity || 1) / maxC)}"></i></span>
          <small>${m.lines}стр :${m.line}</small></div>`).join('')}</div></div>`;
    }
    const fieldsHtml = [
      ...n.fields.map(f => `<div class="li" data-line="${f.line}"><span class="ic">${f.serialized ? '◆' : '·'}</span><span class="nm">${esc(f.name)}</span><small>${esc(f.type)}</small></div>`),
      ...n.properties.map(p => `<div class="li" data-line="${p.line}"><span class="ic">⟐</span><span class="nm">${esc(p.name)}</span><small>${esc(p.type)} (свойство)</small></div>`),
      ...n.events.map(ev => `<div class="li" data-line="${ev.line}"><span class="ic" style="color:#ff9f43">⚡</span><span class="nm">${esc(ev.name)}</span><small>${esc(ev.type)} (событие)</small></div>`),
    ];
    if (fieldsHtml.length) html += `<div class="d-sec"><h4>Поля, свойства, события <span>◆ = в инспекторе</span></h4><div class="list">${fieldsHtml.join('')}</div></div>`;
  }
  html += `<div class="d-sec"><h4>Зависит от <span>${out.length}</span></h4><div class="list">${relList(out, 'out')}</div></div>`;
  html += `<div class="d-sec"><h4>Используется в <span>${inc.length}</span></h4><div class="list">${relList(inc, 'in')}</div></div>`;
  html += `<div class="btn-row"><button class="btn" id="btnCode">Показать код</button><a class="btn" href="${vscodeLink(n.file, n.line)}">VS Code</a><button class="btn" id="btnPin">${n.pinned ? 'Открепить' : 'Закрепить'}</button></div>`;

  $('#detailsBody').innerHTML = html;
  $('#btnCode').onclick = () => openCode(n.file, n.line, n.endLine);
  $('#btnPin').onclick = () => { n.pinned = !n.pinned; renderDetails(n); };
}

$('#detailsBody').addEventListener('click', e => {
  const go = e.target.closest('[data-go]');
  if (go) { const t = state.byId.get(go.dataset.go); if (t) select(t, true); return; }
  const ln = e.target.closest('[data-line]');
  if (ln && state.selected) openCode(state.selected.file, +ln.dataset.line, null, true);
});
$('#detailsClose').onclick = () => select(null);

// ------------------------------------------------------------------ просмотр кода

const CS_KEYWORDS = new Set('abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual void volatile while get set value yield async await nameof when where partial'.split(' '));

function highlight(text) {
  const types = new Map(state.nodes.filter(n => !n.asset).map(n => [n.name, n.id]));
  let inBlock = false;
  return text.split('\n').map(line => {
    let out = '', i = 0;
    while (i < line.length) {
      if (inBlock) {
        const end = line.indexOf('*/', i);
        const seg = end < 0 ? line.slice(i) : line.slice(i, end + 2);
        out += `<span class="c">${esc(seg)}</span>`; i += seg.length; if (end >= 0) inBlock = false; continue;
      }
      const rest = line.slice(i);
      let m;
      if (rest.startsWith('//')) { out += `<span class="c">${esc(rest)}</span>`; break; }
      if (rest.startsWith('/*')) { inBlock = true; continue; }
      if ((m = rest.match(/^\$?@?"(?:[^"\\]|\\.|"")*"?/))) { out += `<span class="s">${esc(m[0])}</span>`; i += m[0].length; continue; }
      if ((m = rest.match(/^'(?:[^'\\]|\\.)'/))) { out += `<span class="s">${esc(m[0])}</span>`; i += m[0].length; continue; }
      if ((m = rest.match(/^\d+(\.\d+)?f?/))) { out += `<span class="n">${m[0]}</span>`; i += m[0].length; continue; }
      if ((m = rest.match(/^\[(\w+)/)) && /^\s*$/.test(line.slice(0, i))) { out += `[<span class="a">${m[1]}</span>`; i += m[0].length; continue; }
      if ((m = rest.match(/^[A-Za-z_]\w*/))) {
        const w = m[0];
        if (CS_KEYWORDS.has(w)) out += `<span class="k">${w}</span>`;
        else if (types.has(w)) out += `<span class="t" data-go="${esc(types.get(w))}">${w}</span>`;
        else out += w;
        i += w.length; continue;
      }
      out += esc(line[i]); i++;
    }
    return out;
  });
}

async function openCode(file, line, endLine, exact) {
  const res = await fetch('/api/source?file=' + encodeURIComponent(file));
  if (!res.ok) { toast(`Не удалось открыть ${esc(file)}`); return; }
  const { text } = await res.json();
  const lines = highlight(text);
  const from = line || 1, to = exact ? from : (endLine || from);
  $('#codeBody').innerHTML = lines.map((l, i) => `<span class="l ${i + 1 === from ? 'target' : (i + 1 > from && i + 1 <= to ? 'hl' : '')}">${l || ' '}</span>`).join('');
  $('#codeTitle').textContent = `${file}:${from}`;
  $('#codeOpen').href = vscodeLink(file, from);
  $('#codeModal').hidden = false;
  const target = $('#codeBody').children[from - 1];
  if (target) target.scrollIntoView({ block: 'center' });
}
$('#codeClose').onclick = () => { $('#codeModal').hidden = true; };
$('#codeModal').addEventListener('click', e => {
  if (e.target.id === 'codeModal') { $('#codeModal').hidden = true; return; }
  const t = e.target.closest('[data-go]');
  if (t) { const n = state.byId.get(t.dataset.go); if (n) { $('#codeModal').hidden = true; switchView('graph'); select(n, true); } }
});

// ------------------------------------------------------------------ поиск

const searchInput = $('#search'), searchBox = $('#searchResults');
let searchSel = 0, searchList = [];

function runSearch() {
  const q = searchInput.value.trim().toLowerCase();
  if (!q) { state.searchHits = null; searchBox.hidden = true; return; }
  const res = [];
  for (const n of state.nodes) {
    if (n.name.toLowerCase().includes(q)) res.push({ n, score: n.name.toLowerCase().startsWith(q) ? 3 : 2, label: n.name, sub: n.file });
    else if (n.file.toLowerCase().includes(q)) res.push({ n, score: 1, label: n.name, sub: n.file });
    for (const m of n.methods || []) if (m.name.toLowerCase().includes(q)) res.push({ n, m, score: m.name.toLowerCase().startsWith(q) ? 2.5 : 1.5, label: `${n.name}.${m.name}()`, sub: `:${m.line}` });
  }
  res.sort((a, b) => b.score - a.score || a.label.length - b.label.length);
  searchList = res.slice(0, 14);
  state.searchHits = new Set(res.map(r => r.n));
  searchSel = 0;
  searchBox.innerHTML = searchList.map((r, i) => `<div class="item ${i === 0 ? 'sel' : ''}" data-i="${i}"><span style="color:${nodeColor(r.n)}">●</span>${esc(r.label)}<small>${esc(r.sub)}</small></div>`).join('') || '<div class="item muted">ничего не найдено</div>';
  searchBox.hidden = false;
}
function pickSearch(i) {
  const r = searchList[i]; if (!r) return;
  searchBox.hidden = true;
  searchInput.value = ''; state.searchHits = null; // дальше работает фокус на выбранном узле
  searchInput.blur();
  switchView('graph');
  select(r.n, true);
  if (r.m) openCode(r.n.file, r.m.line, null, true);
}
searchInput.addEventListener('input', runSearch);
searchInput.addEventListener('keydown', e => {
  if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
    e.preventDefault();
    searchSel = (searchSel + (e.key === 'ArrowDown' ? 1 : -1) + searchList.length) % Math.max(1, searchList.length);
    [...searchBox.children].forEach((c, i) => c.classList.toggle('sel', i === searchSel));
  } else if (e.key === 'Enter') pickSearch(searchSel);
  else if (e.key === 'Escape') { searchInput.value = ''; runSearch(); searchInput.blur(); }
});
searchBox.addEventListener('mousedown', e => { const it = e.target.closest('[data-i]'); if (it) pickSearch(+it.dataset.i); });
searchInput.addEventListener('blur', () => setTimeout(() => { searchBox.hidden = true; }, 150));

// ------------------------------------------------------------------ статистика

function sparkline(values, w = 110, h = 30, color = '#57c7ff') {
  if (values.length < 2) return '';
  const min = Math.min(...values), max = Math.max(...values), span = max - min || 1;
  const pts = values.map((v, i) => `${(i / (values.length - 1) * w).toFixed(1)},${(h - 3 - (v - min) / span * (h - 6)).toFixed(1)}`).join(' ');
  return `<svg width="${w}" height="${h}" viewBox="0 0 ${w} ${h}"><polyline points="${pts}" fill="none" stroke="${color}" stroke-width="1.6" stroke-linejoin="round"/></svg>`;
}

function donut(parts, size = 130) {
  const total = parts.reduce((s, p) => s + p.value, 0) || 1;
  let acc = 0;
  const r = size / 2 - 10, c = size / 2, C = 2 * Math.PI * r;
  const arcs = parts.map(p => {
    const len = p.value / total * C;
    const s = `<circle r="${r}" cx="${c}" cy="${c}" fill="none" stroke="${p.color}" stroke-width="18" stroke-dasharray="${len} ${C - len}" stroke-dashoffset="${-acc}" transform="rotate(-90 ${c} ${c})"><title>${esc(p.label)}: ${p.value}</title></circle>`;
    acc += len; return s;
  }).join('');
  return `<svg width="${size}" height="${size}">${arcs}<text x="${c}" y="${c + 6}" text-anchor="middle" fill="#dfe5f0" font-size="20" font-family="var(--mono)">${total}</text></svg>`;
}

function hbars(items, { color = '#57c7ff', fmt = v => v, click = false } = {}) {
  const max = Math.max(1, ...items.map(i => i.value));
  return items.map(i => `<div class="hbar ${click && i.id ? 'click' : ''}" ${i.id ? `data-go="${esc(i.id)}"` : ''}>
    <span class="nm" title="${esc(i.name)}">${esc(i.name)}</span>
    <span class="track"><i style="width:${i.value / max * 100}%;background:${i.color || color}"></i></span>
    <span class="v">${fmt(i.value)}</span></div>`).join('') || '<div class="empty">нет данных</div>';
}

function renderStats() {
  const g = state.graph; if (!g) return;
  const s = g.stats;
  const h = state.history;
  const prev = h.length > 1 ? h[h.length - 2] : null;
  const kpi = (v, label, key, color, invert) => {
    let delta = '';
    if (prev && key && prev[key] !== h[h.length - 1][key]) {
      const dv = h[h.length - 1][key] - prev[key];
      const good = invert ? dv < 0 : dv > 0;
      delta = `<span class="delta ${good ? 'up' : 'down'}">${dv > 0 ? '+' : ''}${dv}</span>`;
    }
    return `<div class="kpi"><b>${v}</b><span>${label}</span>${delta}${key ? sparkline(h.map(x => x[key]), 70, 22, color) : ''}</div>`;
  };
  const own = state.nodes.filter(n => !n.asset && !n.thirdParty);

  const cb = {};
  for (const n of own) for (const c of n.callbacks || []) cb[c] = (cb[c] || 0) + 1;
  const longMethods = own.flatMap(n => (n.methods || []).map(m => ({ id: n.id, name: `${n.name}.${m.name}`, value: m.lines, c: m.complexity }))).sort((a, b) => b.value - a.value).slice(0, 8);
  const lintAll = own.flatMap(n => (n.lint || []).map(l => ({ ...l, n })));
  const covPct = Math.round(s.testedRatio * 100);
  const commentPct = s.lines ? Math.round(s.commentLines / s.lines * 100) : 0;

  $('#statsView').innerHTML = `
    <h2>Статистика проекта</h2>
    <div class="sub">Скан #${g.version} · ${new Date(g.generatedAt).toLocaleTimeString()} · ${s.scanMs} мс · обновляется сама при сохранении файлов</div>
    <div class="kpis">
      ${kpi(s.files, 'C#-файлов', null)}
      ${kpi(s.lines.toLocaleString('ru'), 'строк кода', 'lines', '#57c7ff')}
      ${kpi(s.types, 'классов и типов', 'types', '#9b7bff')}
      ${kpi(s.methods, 'методов', 'methods', '#f2a93b')}
      ${kpi(s.edges, 'связей', 'edges', '#c792ea')}
      ${kpi(s.tests, 'тестов', 'tests', '#4fd18b')}
      ${kpi(covPct + '%', 'классов с тестами', 'tested', '#4fd18b')}
      ${kpi(s.lintWarnings, 'замечаний lint', 'lint', '#f2b33b', true)}
      ${kpi(s.assets, 'сцен/префабов/ассетов', null)}
      ${kpi(commentPct + '%', 'строк-комментариев', null)}
      ${kpi(s.gitChanged, 'C#-файлов не закоммичено', null)}
      ${kpi(s.todo, 'TODO / FIXME', null)}
    </div>
    <div class="grid2">
      <div class="card"><h3>Код по папкам <small>строки</small></h3>
        ${hbars(Object.entries(s.byCategory).filter(([c]) => c !== 'ThirdParty').sort((a, b) => b[1].lines - a[1].lines).map(([c, v]) => ({ name: `${c} (${v.types})`, value: v.lines, color: state.catColor[c] })), { fmt: v => v.toLocaleString('ru') })}
      </div>
      <div class="card"><h3>Типы <small>по видам</small></h3>
        <div class="donut-wrap">${donut(Object.entries(s.byKind).map(([k, v]) => ({ label: (KINDS[k] || {}).label || k, value: v, color: (KINDS[k] || {}).color || '#888' })))}
          <div class="legend">${Object.entries(s.byKind).sort((a, b) => b[1] - a[1]).map(([k, v]) => `<div class="row"><span class="sw" style="background:${(KINDS[k] || {}).color}"></span>${esc((KINDS[k] || {}).label || k)} <span class="muted">${v}</span></div>`).join('')}</div></div>
      </div>
      <div class="card"><h3>Самые сложные классы <small>ветвления</small></h3>${hbars(s.topComplexity.map(t => ({ ...t, color: heat(t.value / Math.max(1, s.topComplexity[0].value)) })), { click: true })}</div>
      <div class="card"><h3>Хабы <small>от них зависят больше всего</small></h3>${hbars(s.topFanIn, { color: '#c792ea', click: true })}
        <div class="note">Изменения в хабах задевают много кода — им особенно нужны тесты.</div></div>
      <div class="card"><h3>Покрытие тестами <small>диагностика, не gate</small></h3>
        <div class="gauge"><span class="big" style="color:${heat(1 - s.testedRatio)}">${covPct}%</span>
          <span class="muted">${s.testedCount} из ${s.testableCount} классов с логикой упоминаются в тестах</span></div>
        <div style="margin-top:10px;font-size:12px" class="muted">Без тестов, самые сложные — кандидаты на первые тесты:</div>
        ${hbars(s.untested.map(t => ({ ...t, color: '#ff5d6c' })), { click: true })}
      </div>
      <div class="card"><h3>Зависят от многих <small>fan-out</small></h3>${hbars(s.topFanOut, { color: '#57c7ff', click: true })}</div>
      <div class="card"><h3>Самые длинные методы <small>строки</small></h3>${hbars(longMethods.map(m => ({ ...m, color: heat(m.c / 25) })), { click: true })}</div>
      <div class="card"><h3>Unity-колбэки <small>сколько классов используют</small></h3>${hbars(Object.entries(cb).sort((a, b) => b[1] - a[1]).map(([k, v]) => ({ name: k, value: v, color: '#ffd88a' })))}
        <div class="note">Каждый Update/LateUpdate вызывается каждый кадр для каждого объекта.</div></div>
      <div class="card"><h3>Связи по типам</h3>${hbars(Object.entries(s.edgeTypes).sort((a, b) => b[1] - a[1]).map(([k, v]) => ({ name: (EDGE_TYPES[k] || {}).label || k, value: v, color: (EDGE_TYPES[k] || {}).color })))}</div>
      <div class="card"><h3>Lint <small>ci/lint_cs.py</small></h3>
        ${lintAll.length ? lintAll.slice(0, 12).map(l => `<div class="lint-item ${l.level === 'ERROR' ? 'err' : ''}" data-go="${esc(l.n.id)}"><code>${l.code}</code> <b>${esc(l.n.name)}</b> ${esc(l.message)}</div>`).join('') : `<div class="empty">${g.features.lint ? 'Замечаний нет 🎉' : 'Линтер недоступен (нужен Python)'}</div>`}
      </div>
      <div class="card"><h3>Возможно не используются <small>нет ни в сцене, ни в коде</small></h3>
        ${s.maybeUnused.length ? hbars(s.maybeUnused.map(u => ({ ...u, value: 1, color: '#6b7a90' })), { click: true, fmt: () => '' }) : '<div class="empty">Все MonoBehaviour где-то используются</div>'}
        <div class="note">Скрипт может добавляться через AddComponent по строке или из другой сцены — проверьте вручную.</div></div>
      ${g.features.git ? `<div class="card"><h3>Горячие файлы git <small>коммиты за 180 дней</small></h3>${hbars(s.topChurn.length ? s.topChurn : [], { color: '#9b7bff', click: true })}</div>` : ''}
      <div class="card"><h3>Эта сессия <small>${h.length} сканов</small></h3>
        ${h.length > 1 ? `
          <div class="hbar"><span class="nm">строки</span>${sparkline(h.map(x => x.lines), 220, 28, '#57c7ff')}<span class="v">${h[h.length - 1].lines - h[0].lines >= 0 ? '+' : ''}${h[h.length - 1].lines - h[0].lines}</span></div>
          <div class="hbar"><span class="nm">сложность</span>${sparkline(h.map(x => x.complexity), 220, 28, '#ff5d6c')}<span class="v">${h[h.length - 1].complexity - h[0].complexity >= 0 ? '+' : ''}${h[h.length - 1].complexity - h[0].complexity}</span></div>
          <div class="hbar"><span class="nm">связи</span>${sparkline(h.map(x => x.edges), 220, 28, '#c792ea')}<span class="v">${h[h.length - 1].edges - h[0].edges >= 0 ? '+' : ''}${h[h.length - 1].edges - h[0].edges}</span></div>`
          : '<div class="empty">Сохрани какой-нибудь .cs — здесь появится динамика</div>'}
      </div>
    </div>`;
}
$('#statsView').addEventListener('click', e => {
  const t = e.target.closest('[data-go]'); if (!t) return;
  const n = state.byId.get(t.dataset.go); if (!n) return;
  switchView('graph'); select(n, true);
});

function renderOverview() {
  const g = state.graph; if (!g) return;
  $('#overview').innerHTML = `<span class="pill">узлов <b>${state.vNodes.length}</b>/${state.nodes.length}</span><span class="pill">связей <b>${state.vEdges.length}</b></span><span class="pill">строк <b>${g.stats.lines.toLocaleString('ru')}</b></span>`;
}

// ------------------------------------------------------------------ живые обновления

function toast(html, ms = 6000) {
  const el = document.createElement('div');
  el.className = 'toast'; el.innerHTML = html;
  $('#toasts').appendChild(el);
  setTimeout(() => { el.style.opacity = '0'; el.style.transition = 'opacity .4s'; setTimeout(() => el.remove(), 400); }, ms);
  while ($('#toasts').children.length > 4) $('#toasts').firstChild.remove();
}

function describeUpdate(u) {
  const parts = [];
  const name = id => (state.byId.get(id) || { name: id.replace(/^\w+:/, '') }).name;
  for (const id of (u.added || []).slice(0, 4)) parts.push(`<span style="color:#4fd18b">+ ${esc(name(id))}</span>`);
  for (const id of (u.removed || []).slice(0, 4)) parts.push(`<span style="color:#ff5d6c">− ${esc(id.replace(/^\w+:/, ''))}</span>`);
  for (const c of (u.changed || []).slice(0, 4)) {
    const bits = [];
    if (c.dLines) bits.push(`${c.dLines > 0 ? '+' : ''}${c.dLines} стр`);
    if (c.dMethods) bits.push(`${c.dMethods > 0 ? '+' : ''}${c.dMethods} мет`);
    if (c.dComplexity) bits.push(`слож ${c.dComplexity > 0 ? '+' : ''}${c.dComplexity}`);
    parts.push(`<span style="color:#f2a93b">~ ${esc(name(c.id))}</span>${bits.length ? ' <span class="muted">' + bits.join(', ') + '</span>' : ''}`);
  }
  return parts.join('<br>');
}

function addFeed(entry, html) {
  const list = $('#feedList');
  const el = document.createElement('div');
  el.className = 'feed-item';
  el.innerHTML = `<time>${new Date(entry.at).toLocaleTimeString()}</time> · ${esc(entry.reason)} ${entry.text ? '· ' + esc(entry.text) : ''}
    ${entry.files && entry.files.length ? `<div class="files">${entry.files.map(esc).join('<br>')}</div>` : ''}${html ? '<div>' + html + '</div>' : ''}`;
  list.prepend(el);
  while (list.children.length > 60) list.lastChild.remove();
}

function connect() {
  const live = $('#live'), text = $('#liveText');
  const es = new EventSource('/api/events');
  es.addEventListener('hello', () => { live.className = 'live on'; text.textContent = 'LIVE · v' + (state.graph ? state.graph.version : '…'); });
  es.addEventListener('update', async ev => {
    const u = JSON.parse(ev.data);
    await loadGraph(u);
    text.textContent = 'LIVE · v' + u.version;
    live.classList.add('flash'); setTimeout(() => live.classList.remove('flash'), 800);
    const html = describeUpdate(u);
    addFeed(u.entry, html);
    if (html || (u.entry.files || []).length) {
      toast(`<b>${esc(u.entry.reason === 'git' ? 'git обновился' : 'Код изменился')}</b> ${u.entry.text ? '· ' + esc(u.entry.text) : ''}<br>${html || `<span class="files">${(u.entry.files || []).slice(0, 3).map(esc).join('<br>')}</span>`}`);
    }
    if ($('#feed').hidden) { state.feedUnread++; $('#feedBadge').hidden = false; $('#feedBadge').textContent = state.feedUnread; }
  });
  es.addEventListener('scan-error', ev => toast(`<b style="color:#ff5d6c">Ошибка сканирования</b><br>${esc(JSON.parse(ev.data).message)}`));
  es.onerror = () => { live.className = 'live off'; text.textContent = 'нет связи — переподключаюсь'; };
}

// ------------------------------------------------------------------ вид, кнопки, клавиатура

function switchView(v) {
  state.view = v;
  document.querySelectorAll('.tab').forEach(t => t.classList.toggle('active', t.dataset.view === v));
  $('#graphView').classList.toggle('active', v === 'graph');
  $('#statsView').classList.toggle('active', v === 'stats');
  if (v === 'stats') renderStats();
}
document.querySelectorAll('.tab').forEach(t => t.addEventListener('click', () => switchView(t.dataset.view)));
$('#btnFit').onclick = () => fitView();
$('#btnFreeze').onclick = () => { state.frozen = !state.frozen; $('#btnFreeze').classList.toggle('on', state.frozen); if (!state.frozen) reheat(0.3); };
$('#btnRescan').onclick = () => fetch('/api/rescan');
$('#btnFeed').onclick = () => { const f = $('#feed'); f.hidden = !f.hidden; state.feedUnread = 0; $('#feedBadge').hidden = true; };
$('#feedClose').onclick = () => { $('#feed').hidden = true; };

window.addEventListener('keydown', e => {
  if (e.target.tagName === 'INPUT') return;
  if (e.key === '/') { e.preventDefault(); searchInput.focus(); }
  else if (e.key === 'Escape') { if (!$('#codeModal').hidden) $('#codeModal').hidden = true; else { select(null); searchInput.value = ''; runSearch(); } }
  else if (e.key === 'f' || e.key === 'F' || e.key === 'а' || e.key === 'А') fitView();
  else if (e.key === ' ') { e.preventDefault(); $('#btnFreeze').click(); }
  else if (e.key === '1') switchView('graph');
  else if (e.key === '2') switchView('stats');
});

// ------------------------------------------------------------------ старт

(async function start() {
  wireSidebar();
  resize();
  try {
    await loadGraph();
    const feed = await (await fetch('/api/feed')).json();
    for (const f of feed.reverse()) addFeed(f, '');
  } catch (e) {
    toast(`<b style="color:#ff5d6c">Сервер недоступен</b><br>Запусти: <code>node tools/codemap/server.js</code>`, 20000);
  }
  connect();
  requestAnimationFrame(loop);
})();
