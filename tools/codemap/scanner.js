// Сканер проекта: разбирает C#-скрипты и Unity-ассеты и строит граф связей.
// Без зависимостей -- только регулярки и подсчёт скобок (полноценный парсер C# тут не нужен).
'use strict';

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const UNITY_CALLBACKS = new Set([
  'Awake', 'Start', 'Update', 'FixedUpdate', 'LateUpdate', 'OnEnable', 'OnDisable', 'OnDestroy',
  'OnTriggerEnter', 'OnTriggerStay', 'OnTriggerExit', 'OnCollisionEnter', 'OnCollisionStay', 'OnCollisionExit',
  'OnGUI', 'OnDrawGizmos', 'OnDrawGizmosSelected', 'OnValidate', 'Reset', 'OnApplicationQuit',
]);
const KEYWORDS = new Set(['if', 'for', 'foreach', 'while', 'switch', 'catch', 'using', 'return', 'new', 'lock', 'nameof', 'typeof', 'sizeof', 'default', 'base', 'this']);
const COMPLEXITY_RX = /\b(if|for|foreach|while|case|catch)\b|&&|\|\||\?\?|\?(?![.\[?])/g;

// ---------------------------------------------------------------- utils

function walk(dir, exts, out = []) {
  let entries;
  try { entries = fs.readdirSync(dir, { withFileTypes: true }); } catch { return out; }
  for (const e of entries) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, exts, out);
    else if (exts.some(x => e.name.endsWith(x))) out.push(p);
  }
  return out;
}

const rel = (root, p) => path.relative(root, p).split(path.sep).join('/');

// Заменяет содержимое комментариев и строк пробелами (переносы строк сохраняются -- номера строк не едут)
function stripCode(src) {
  let out = '', i = 0;
  const n = src.length;
  while (i < n) {
    const c = src[i];
    if (c === '/' && src[i + 1] === '/') {
      let j = src.indexOf('\n', i); if (j < 0) j = n;
      out += ' '.repeat(j - i); i = j;
    } else if (c === '/' && src[i + 1] === '*') {
      let j = src.indexOf('*/', i + 2); j = j < 0 ? n : j + 2;
      out += src.slice(i, j).replace(/[^\n]/g, ' '); i = j;
    } else if (c === '"' || ((c === '@' || c === '$') && src[i + 1] === '"') || (c === '$' && src[i + 1] === '@' && src[i + 2] === '"')) {
      const verbatim = src.slice(i, i + 3).includes('@');
      const start = i;
      i = src.indexOf('"', i) + 1;
      while (i < n) {
        if (src[i] === '\\' && !verbatim) { i += 2; continue; }
        if (src[i] === '"') { if (verbatim && src[i + 1] === '"') { i += 2; continue; } i++; break; }
        if (src[i] === '\n' && !verbatim) break;
        i++;
      }
      out += '"' + src.slice(start + 1, i - 1).replace(/[^\n]/g, ' ') + '"';
    } else if (c === "'") {
      let j = i + 1;
      if (src[j] === '\\') j += 2; else j += 1;
      const end = src.indexOf("'", j);
      if (end >= 0 && end - i <= 8) { out += ' '.repeat(end + 1 - i); i = end + 1; }
      else { out += c; i++; }
    } else { out += c; i++; }
  }
  return out;
}

function lineAt(lineStarts, pos) {
  let lo = 0, hi = lineStarts.length - 1;
  while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (lineStarts[mid] <= pos) lo = mid; else hi = mid - 1; }
  return lo + 1;
}

function matchBrace(code, openPos) {
  let depth = 0;
  for (let i = openPos; i < code.length; i++) {
    if (code[i] === '{') depth++;
    else if (code[i] === '}') { depth--; if (depth === 0) return i; }
  }
  return code.length - 1;
}

// ---------------------------------------------------------------- C# parsing

const TYPE_RX = /((?:\[[^\]\n]*\]\s*)*)((?:(?:public|internal|private|protected|static|abstract|sealed|partial|readonly|ref)\s+)*)(class|struct|interface|enum|record)\s+([A-Za-z_]\w*)\s*(<[^>{]*>)?\s*(?::\s*([^{]+?))?\s*(?:where[^{]*)?\{/g;

function parseFile(absPath, root) {
  const src = fs.readFileSync(absPath, 'utf8').replace(/^﻿/, '');
  const code = stripCode(src);
  const lineStarts = [0];
  for (let i = 0; i < src.length; i++) if (src[i] === '\n') lineStarts.push(i + 1);
  const file = rel(root, absPath);

  const ns = (code.match(/\bnamespace\s+([\w.]+)/) || [])[1] || '';
  const types = [];
  TYPE_RX.lastIndex = 0;
  let m;
  while ((m = TYPE_RX.exec(code))) {
    const open = m.index + m[0].length - 1;
    const close = matchBrace(code, open);
    const attrs = (m[1] || '').match(/\[\s*(\w+)/g)?.map(a => a.replace(/[\[\s]/g, '')) || [];
    const mods = (m[2] || '').trim().split(/\s+/).filter(Boolean);
    const bases = (m[6] || '').split(',').map(s => s.trim().replace(/<.*$/, '').split('.').pop()).filter(Boolean);
    types.push({
      name: m[4], keyword: m[3], mods, attrs, bases,
      start: m.index + (m[1] || '').length, open, close,
      line: lineAt(lineStarts, m.index + (m[1] || '').length),
      endLine: lineAt(lineStarts, close),
    });
  }

  // вложенность: тип принадлежит самому внутреннему из охватывающих
  for (const t of types) {
    const parent = types.filter(o => o !== t && o.open < t.start && o.close > t.close).sort((a, b) => b.open - a.open)[0];
    t.parent = parent ? parent.name : null;
  }

  for (const t of types) {
    // тело без вложенных типов
    let body = code.slice(t.open + 1, t.close);
    for (const inner of types) {
      if (inner !== t && inner.open > t.open && inner.close < t.close) {
        const a = inner.start - t.open - 1, b = inner.close - t.open;
        body = body.slice(0, a) + body.slice(a, b).replace(/[^\n]/g, ' ') + body.slice(b);
      }
    }
    t.body = body;
    t.bodyOffset = t.open + 1;
    Object.assign(t, parseMembers(t, body, code, lineStarts));
    t.lines = t.endLine - t.line + 1;
    t.complexity = 1 + ((body.match(COMPLEXITY_RX) || []).length);
    t.todo = (src.slice(t.start, t.close).match(/\b(TODO|FIXME|HACK)\b/g) || []).length;
  }

  const fileLines = src.split('\n').length;
  const commentLines = src.split('\n').filter(l => /^\s*(\/\/|\/\*|\*)/.test(l)).length;
  return { file, ns, types, lines: fileLines, commentLines, src, code };
}

function parseMembers(t, body, code, lineStarts) {
  const methods = [], fields = [], properties = [], events = [], callbacks = [];
  // считаем только верхний уровень тела типа (глубина скобок 0)
  let depth = 0;
  const top = new Array(body.length);
  for (let i = 0; i < body.length; i++) {
    if (body[i] === '{') { top[i] = depth === 0; depth++; continue; }
    if (body[i] === '}') { depth--; top[i] = depth === 0; continue; }
    top[i] = depth === 0;
  }
  const isTop = i => top[i];
  const at = i => lineAt(lineStarts, t.bodyOffset + i);

  const METHOD_RX = /((?:\[[^\]\n]*\]\s*)*)((?:(?:public|private|protected|internal|static|virtual|override|abstract|async|sealed|new|extern|unsafe)\s+)*)([\w<>\[\],.?]+(?:\s*<[^>]*>)?)\s+([A-Za-z_]\w*)\s*(<[^>(]*>)?\s*\(([^)]*)\)\s*(?:where[^{;=]*)?(\{|=>|;)/g;
  let m;
  while ((m = METHOD_RX.exec(body))) {
    const idx = m.index + (m[1] || '').length;
    if (!isTop(idx)) continue;
    const ret = m[3], name = m[4];
    if (KEYWORDS.has(ret) || KEYWORDS.has(name) || ret === 'else') continue;
    const mods = (m[2] || '').trim().split(/\s+/).filter(Boolean);
    let bodyText = '';
    if (m[7] === '{') {
      const open = m.index + m[0].length - 1;
      bodyText = body.slice(open, matchBrace(body, open) + 1);
    } else if (m[7] === '=>') {
      const end = body.indexOf(';', m.index + m[0].length);
      bodyText = body.slice(m.index + m[0].length, end < 0 ? undefined : end);
    }
    const access = mods.find(x => ['public', 'private', 'protected', 'internal'].includes(x)) || 'private';
    methods.push({
      name, ret, line: at(idx), access, static: mods.includes('static'),
      params: m[6].trim() ? m[6].split(',').length : 0,
      lines: bodyText.split('\n').length,
      complexity: 1 + ((bodyText.match(COMPLEXITY_RX) || []).length),
      test: /\b(Test|TestCase|UnityTest)\b/.test(m[1] || ''),
      unity: UNITY_CALLBACKS.has(name),
    });
    if (UNITY_CALLBACKS.has(name)) callbacks.push(name);
  }
  // конструкторы
  const CTOR_RX = new RegExp(`(?:public|private|protected|internal)?\\s*\\b${t.name}\\s*\\(([^)]*)\\)\\s*(?::\\s*(?:base|this)\\s*\\([^)]*\\)\\s*)?\\{`, 'g');
  while ((m = CTOR_RX.exec(body))) if (isTop(m.index)) methods.push({ name: t.name, ret: 'ctor', line: at(m.index), access: 'public', params: 0, lines: 1, complexity: 1 });

  // поле -- отдельный оператор: начинается после ; { } или переноса строки, инициализатор без {} и не =>
  const FIELD_RX = /(?<=^|[\n;{}])[ \t]*((?:\[[^\]\n]*\]\s*)*)((?:(?:public|private|protected|internal|static|readonly|const|volatile|new)\s+)*)([\w<>\[\],.?]+(?:\s*<[^>]*>)?)\s+([A-Za-z_]\w*)\s*(=(?!>)[^;{}]*)?;/g;
  while ((m = FIELD_RX.exec(body))) {
    const idx = m.index + (m[0].length - m[0].trimStart().length) + (m[1] || '').length;
    if (!isTop(idx)) continue;
    const type = m[3], name = m[4];
    if (/\bevent\s*$/.test(body.slice(Math.max(0, idx - 12), idx + (m[2] || '').length))) continue;
    if (KEYWORDS.has(type) || type === 'return' || type === 'using') continue;
    const mods = (m[2] || '').trim().split(/\s+/).filter(Boolean);
    if (type === 'event' || mods.includes('event')) continue;
    const serialized = mods.includes('public') && !mods.includes('static') && !mods.includes('const') || /SerializeField/.test(m[1] || '');
    fields.push({ name, type: type.replace(/\s+/g, ''), line: at(idx), serialized, static: mods.includes('static') || mods.includes('const') });
  }
  const PROP_RX = /\b(public|private|protected|internal)\s+(?:static\s+|virtual\s+|override\s+|abstract\s+)*([\w<>\[\],.?]+)\s+([A-Z]\w*)\s*(\{|=>)/g;
  while ((m = PROP_RX.exec(body))) if (isTop(m.index)) properties.push({ name: m[3], type: m[2], line: at(m.index) });
  const EVENT_RX = /\bevent\s+([\w<>,\s.]+?)\s+([A-Za-z_]\w*)\s*;/g;
  while ((m = EVENT_RX.exec(body))) if (isTop(m.index)) events.push({ name: m[2], type: m[1].trim(), line: at(m.index) });

  return { methods, fields, properties, events, callbacks };
}

// ---------------------------------------------------------------- Unity assets

function readGuid(metaPath) {
  try {
    const m = fs.readFileSync(metaPath, 'utf8').match(/^guid:\s*([0-9a-f]{32})/m);
    return m ? m[1] : null;
  } catch { return null; }
}

function scanAssets(root, scriptGuids) {
  const assetsDir = path.join(root, 'Assets');
  const files = walk(assetsDir, ['.unity', '.prefab', '.asset']).filter(f => !rel(root, f).startsWith('Assets/TextMesh Pro'));
  const assets = [];
  for (const f of files) {
    let text;
    try {
      const st = fs.statSync(f);
      if (st.size > 30 * 1024 * 1024) continue;
      text = fs.readFileSync(f, 'utf8');
    } catch { continue; }
    if (!text.startsWith('%YAML')) continue; // бинарные ассеты пропускаем
    const uses = {};
    const rx = /m_Script:\s*\{fileID:\s*11500000,\s*guid:\s*([0-9a-f]{32})/g;
    let m;
    while ((m = rx.exec(text))) {
      const file = scriptGuids.get(m[1]);
      if (file) uses[file] = (uses[file] || 0) + 1;
    }
    // ScriptableObject-ассеты ссылаются на скрипт так же, а ещё на префабы по guid
    if (Object.keys(uses).length === 0) continue;
    const r = rel(root, f);
    assets.push({
      id: 'asset:' + r,
      file: r,
      name: path.basename(r),
      kind: r.endsWith('.unity') ? 'scene' : r.endsWith('.prefab') ? 'prefab' : 'so-asset',
      uses,
      bytes: text.length,
    });
  }
  return assets;
}

// ---------------------------------------------------------------- git & lint

function gitInfo(root) {
  const info = { status: {}, churn: {}, lastChange: {}, available: false };
  try {
    const status = execFileSync('git', ['status', '--porcelain', '-uall', '--', 'Assets'], { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
    for (const line of status.split('\n')) {
      if (!line.trim()) continue;
      const code = line.slice(0, 2).trim();
      let p = line.slice(3).trim().replace(/^"|"$/g, '');
      if (p.includes(' -> ')) p = p.split(' -> ')[1];
      info.status[p] = code === '??' ? 'new' : code.includes('M') ? 'modified' : code.includes('A') ? 'new' : code.includes('D') ? 'deleted' : 'changed';
    }
    const log = execFileSync('git', ['log', '--since=180.days', '--name-only', '--pretty=format:@%ct', '--', 'Assets'], { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'], maxBuffer: 64 * 1024 * 1024 });
    let ts = 0;
    for (const line of log.split('\n')) {
      if (line.startsWith('@')) { ts = Number(line.slice(1)) * 1000; continue; }
      const p = line.trim();
      if (!p) continue;
      info.churn[p] = (info.churn[p] || 0) + 1;
      if (!info.lastChange[p]) info.lastChange[p] = ts;
    }
    info.available = true;
  } catch { /* git нет -- не страшно */ }
  return info;
}

function lintInfo(root) {
  const result = { available: false, byFile: {} };
  const script = path.join(root, 'ci', 'lint_cs.py');
  if (!fs.existsSync(script)) return result;
  const files = walk(path.join(root, 'Assets', 'Game'), ['.cs']).map(f => rel(root, f));
  for (const py of ['python', 'python3', 'py']) {
    try {
      let out;
      try {
        out = execFileSync(py, [script, ...files], { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'], env: { ...process.env, PYTHONIOENCODING: 'utf-8' } });
      } catch (e) {
        if (e.stdout === undefined) throw e; // python не найден
        out = e.stdout; // exit 1 = есть ошибки, но вывод нам нужен
      }
      for (const line of out.split(/\r?\n/)) {
        const m = line.match(/^(ERROR|WARN)\s+(\w+)\s+(.+?):(\d+)\s+(.*)$/);
        if (!m) continue;
        (result.byFile[m[3]] ||= []).push({ level: m[1], code: m[2], line: +m[4], message: m[5].trim() });
      }
      result.available = true;
      return result;
    } catch { /* пробуем следующий */ }
  }
  return result;
}

// ---------------------------------------------------------------- graph

function category(file) {
  const parts = file.split('/');
  if (file.startsWith('Assets/Game/Tests')) return 'Tests';
  if (file.startsWith('Assets/Game/Scripts/')) return parts[3] || 'Scripts';
  if (/\.unity$/.test(file)) return file.startsWith('Assets/Game/') ? 'Scenes' : 'ThirdParty';
  if (/\.(prefab|asset)$/.test(file)) return file.startsWith('Assets/Game/') ? 'Prefabs & Data' : 'ThirdParty';
  if (file.startsWith('Assets/Game/') && parts.length > 3) return parts[2];
  return 'ThirdParty';
}

function classify(t, file, monoTypes, soTypes) {
  if (t.keyword === 'interface') return 'interface';
  if (t.keyword === 'enum') return 'enum';
  if (t.keyword === 'struct') return 'struct';
  if (t.methods.some(m => m.test)) return 'test';
  if (/\/Editor\//.test(file)) return 'editor';
  if (monoTypes.has(t.name)) return 'mono';
  if (soTypes.has(t.name)) return 'so';
  if (t.mods.includes('static')) return 'static';
  return 'class';
}

function buildGraph(root, opts = {}) {
  const t0 = Date.now();
  const csFiles = walk(path.join(root, 'Assets'), ['.cs']);
  const parsed = [];
  const errors = [];
  for (const f of csFiles) {
    try { parsed.push(parseFile(f, root)); }
    catch (e) { errors.push({ file: rel(root, f), message: e.message }); }
  }

  // все типы проекта
  const typeMap = new Map(); // name -> {type, file}
  for (const pf of parsed) for (const t of pf.types) if (!typeMap.has(t.name)) typeMap.set(t.name, { t, pf });

  // транзитивное наследование от MonoBehaviour / ScriptableObject
  const inherits = (name, target, seen = new Set()) => {
    if (seen.has(name)) return false; seen.add(name);
    const e = typeMap.get(name); if (!e) return false;
    return e.t.bases.some(b => b === target || inherits(b, target, seen));
  };
  const monoTypes = new Set([...typeMap.keys()].filter(n => inherits(n, 'MonoBehaviour')));
  const soTypes = new Set([...typeMap.keys()].filter(n => inherits(n, 'ScriptableObject')));

  // события: имя события -> владелец
  const eventOwners = new Map();
  for (const { t } of typeMap.values()) for (const ev of t.events) eventOwners.set(ev.name, t.name);

  // GUID скриптов -> файл
  const scriptGuids = new Map();
  for (const pf of parsed) {
    const g = readGuid(path.join(root, pf.file + '.meta'));
    if (g) scriptGuids.set(g, pf.file);
  }

  const git = opts.skipGit ? { status: {}, churn: {}, lastChange: {}, available: false } : gitInfo(root);
  const lint = opts.lint || { available: false, byFile: {} };

  const nodes = [];
  const edges = [];
  const edgeKey = new Map();
  const addEdge = (from, to, type, weight = 1, label) => {
    if (from === to) return;
    const k = `${from}|${to}|${type}`;
    const e = edgeKey.get(k);
    if (e) { e.weight += weight; return; }
    const ne = { id: k, from, to, type, weight, label };
    edgeKey.set(k, ne); edges.push(ne);
  };

  const names = [...typeMap.keys()].filter(n => n.length > 2);
  const nameRx = names.length ? new RegExp(`\\b(${names.sort((a, b) => b.length - a.length).join('|')})\\b`, 'g') : null;

  for (const pf of parsed) {
    for (const t of pf.types) {
      if (typeMap.get(t.name).t !== t) continue; // дубликаты имён (partial и т.п.) -- берём первый
      const id = 'type:' + t.name;
      const kind = classify(t, pf.file, monoTypes, soTypes);
      const lintItems = (lint.byFile[pf.file] || []).filter(l => l.line >= t.line && l.line <= t.endLine);
      nodes.push({
        id, kind, name: t.name, file: pf.file, line: t.line, endLine: t.endLine,
        category: category(pf.file), thirdParty: category(pf.file) === 'ThirdParty',
        keyword: t.keyword, mods: t.mods, attrs: t.attrs, parent: t.parent, ns: pf.ns,
        bases: t.bases, lines: t.lines, complexity: t.complexity, todo: t.todo,
        methods: t.methods, fields: t.fields, properties: t.properties, events: t.events, callbacks: t.callbacks,
        gitStatus: git.status[pf.file] || null,
        churn: git.churn[pf.file] || 0,
        lastChange: git.lastChange[pf.file] || null,
        lint: lintItems,
      });

      // наследование / интерфейсы
      for (const b of t.bases) {
        const target = typeMap.get(b);
        if (target) addEdge(id, 'type:' + b, target.t.keyword === 'interface' ? 'implements' : 'inherits');
      }
      if (t.parent) addEdge(id, 'type:' + t.parent, 'nested');

      if (!nameRx) continue;
      const body = t.body;
      // GetComponent / Find / AddComponent
      const gc = /\b(GetComponent(?:InChildren|InParent|s)?|AddComponent|Find(?:First|Any)?ObjectsByType|Find(?:First|Any)?ObjectByType|FindObjectOfType|RequireComponent)\s*<\s*(\w+)\s*>/g;
      let m;
      const viaComponent = new Set();
      while ((m = gc.exec(body))) {
        if (typeMap.has(m[2]) && m[2] !== t.name) { addEdge(id, 'type:' + m[2], kind === 'test' ? 'tests' : 'component', 1, m[1]); viaComponent.add(m[2]); }
      }
      for (const a of (t.attrs || [])) { /* атрибуты класса -- RequireComponent(typeof(X)) */ }
      const req = /RequireComponent\s*\(\s*typeof\s*\(\s*(\w+)\s*\)/g;
      while ((m = req.exec(pf.code.slice(Math.max(0, t.start - 300), t.open)))) if (typeMap.has(m[1])) addEdge(id, 'type:' + m[1], 'component', 1, 'RequireComponent');

      // подписки на события: x.Event += ...
      const sub = /\.(\w+)\s*\+=/g;
      while ((m = sub.exec(body))) {
        const owner = eventOwners.get(m[1]);
        if (owner && owner !== t.name) addEdge(id, 'type:' + owner, 'event', 1, m[1]);
      }

      // прочие упоминания типов
      const counts = {};
      nameRx.lastIndex = 0;
      while ((m = nameRx.exec(body))) counts[m[1]] = (counts[m[1]] || 0) + 1;
      for (const [n, c] of Object.entries(counts)) {
        if (n === t.name || viaComponent.has(n)) continue;
        const target = typeMap.get(n);
        if (!target || target.t === t) continue;
        if (target.t.parent === t.name) continue; // свой вложенный тип
        addEdge(id, 'type:' + n, kind === 'test' ? 'tests' : 'uses', c);
      }
    }
  }

  // ассеты: сцены, префабы, SO
  const assets = opts.noAssets ? [] : scanAssets(root, scriptGuids);
  const fileToType = new Map();
  for (const n of nodes) if (!fileToType.has(n.file) || n.kind === 'mono' || n.kind === 'so') {
    const base = path.basename(n.file, '.cs');
    if (!fileToType.has(n.file) || n.name === base) fileToType.set(n.file, n.id);
  }
  for (const a of assets) {
    nodes.push({ id: a.id, kind: a.kind, name: a.name, file: a.file, category: category(a.file), asset: true, bytes: a.bytes, thirdParty: category(a.file) === 'ThirdParty', gitStatus: git.status[a.file] || null, churn: git.churn[a.file] || 0 });
    for (const [file, count] of Object.entries(a.uses)) {
      const target = fileToType.get(file);
      if (target) addEdge(a.id, target, 'scene', count);
    }
  }

  // метрики связности
  const byId = new Map(nodes.map(n => [n.id, n]));
  for (const n of nodes) { n.fanIn = 0; n.fanOut = 0; n.testedBy = []; n.usedInAssets = 0; }
  for (const e of edges) {
    const a = byId.get(e.from), b = byId.get(e.to);
    if (!a || !b) continue;
    if (e.type !== 'nested') { a.fanOut++; b.fanIn++; }
    if (e.type === 'tests') b.testedBy.push(a.name);
    if (e.type === 'scene') b.usedInAssets += e.weight;
  }
  // "возможно не используется": MonoBehaviour, которого нет ни в сцене/префабе, и его никто не ищет/не добавляет
  for (const n of nodes) {
    if (n.kind !== 'mono' || n.thirdParty) continue;
    // никто не ссылается (кроме тестов): нет ни на сцене/в префабе, ни в чужом коде
    const referenced = edges.some(e => e.to === n.id && e.type !== 'tests' && e.type !== 'nested');
    n.maybeUnused = !referenced;
  }

  const stats = computeStats(parsed, nodes, edges, git, lint);
  stats.scanMs = Date.now() - t0;
  return {
    root: path.basename(root),
    generatedAt: new Date().toISOString(),
    nodes, edges, stats, errors,
    features: { git: git.available, lint: lint.available },
  };
}

function computeStats(parsed, nodes, edges, git, lint) {
  const own = parsed.filter(p => !p.file.startsWith('Assets/TextMesh Pro'));
  const types = nodes.filter(n => !n.asset && !n.thirdParty);
  const byCategory = {};
  for (const p of own) {
    const c = category(p.file);
    const s = (byCategory[c] ||= { files: 0, lines: 0, types: 0, methods: 0 });
    s.files++; s.lines += p.lines;
  }
  for (const n of types) { const s = byCategory[n.category]; if (s) { s.types++; s.methods += n.methods.length; } }
  const byKind = {};
  for (const n of nodes) if (!n.thirdParty) byKind[n.kind] = (byKind[n.kind] || 0) + 1;
  const edgeTypes = {};
  for (const e of edges) edgeTypes[e.type] = (edgeTypes[e.type] || 0) + 1;

  const testable = types.filter(n => !['test', 'editor', 'enum', 'interface'].includes(n.kind) && n.methods.length > 0);
  const tested = testable.filter(n => n.testedBy.length > 0);
  const top = (arr, key, k = 6) => [...arr].sort((a, b) => b[key] - a[key]).slice(0, k).map(n => ({ id: n.id, name: n.name, value: n[key] }));
  const lintCount = Object.values(lint.byFile).reduce((s, l) => s + l.length, 0);

  return {
    files: own.length,
    lines: own.reduce((s, p) => s + p.lines, 0),
    commentLines: own.reduce((s, p) => s + p.commentLines, 0),
    types: types.length,
    methods: types.reduce((s, n) => s + n.methods.length, 0),
    tests: types.reduce((s, n) => s + n.methods.filter(m => m.test).length, 0),
    unityCallbacks: types.reduce((s, n) => s + n.callbacks.length, 0),
    edges: edges.length,
    assets: nodes.filter(n => n.asset).length,
    todo: types.reduce((s, n) => s + n.todo, 0),
    lintWarnings: lintCount,
    testedRatio: testable.length ? tested.length / testable.length : 0,
    testedCount: tested.length,
    testableCount: testable.length,
    untested: testable.filter(n => !n.testedBy.length).sort((a, b) => b.complexity - a.complexity).slice(0, 8).map(n => ({ id: n.id, name: n.name, value: n.complexity })),
    maybeUnused: types.filter(n => n.maybeUnused).map(n => ({ id: n.id, name: n.name })),
    gitChanged: Object.keys(git.status).filter(p => p.endsWith('.cs')).length,
    byCategory, byKind, edgeTypes,
    topComplexity: top(types, 'complexity'),
    topLines: top(types, 'lines'),
    topFanIn: top(types, 'fanIn'),
    topFanOut: top(types, 'fanOut'),
    topChurn: top(types.filter(n => n.churn), 'churn'),
  };
}

module.exports = { buildGraph, lintInfo, stripCode };
