#!/usr/bin/env node
// CodeMap -- живая карта кода BeerAndDragon.
//   node tools/codemap/server.js [--port 5177] [--open] [--root <путь к Unity-проекту>]
// Без зависимостей: только встроенные модули Node.
'use strict';

const http = require('http');
const fs = require('fs');
const path = require('path');
const { exec } = require('child_process');
const { buildGraph, lintInfo } = require('./scanner');

const args = process.argv.slice(2);
const argVal = (name, def) => { const i = args.indexOf(name); return i >= 0 && args[i + 1] ? args[i + 1] : def; };
const ROOT = path.resolve(argVal('--root', path.join(__dirname, '..', '..')));
const PUBLIC = path.join(__dirname, 'public');
let port = Number(argVal('--port', process.env.PORT || 5177));

const MIME = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.json': 'application/json; charset=utf-8', '.svg': 'image/svg+xml' };
const SOURCE_EXT = new Set(['.cs', '.json', '.py', '.md', '.yml', '.txt', '.asset', '.prefab', '.unity', '.meta']);

let graph = null;
let version = 0;
let lint = { available: false, byFile: {} };
const clients = new Set();
const feed = []; // последние события изменения

function log(...a) { console.log(new Date().toLocaleTimeString(), ...a); }

// ------------------------------------------------------------ rebuild

function rebuild(reason, changedFiles = []) {
  const t0 = Date.now();
  try { lint = lintInfo(ROOT); } catch { /* линтер необязателен */ }
  let next;
  try { next = buildGraph(ROOT, { lint }); }
  catch (e) { log('Ошибка сканирования:', e.message); broadcast('scan-error', { message: e.message }); return; }

  const diff = diffGraphs(graph, next);
  version++;
  next.version = version;
  next.rootPath = ROOT; // для ссылок vscode://file/... (сервер слушает только localhost)
  graph = next;

  if (reason !== 'initial') {
    const entry = { at: Date.now(), reason, files: changedFiles.slice(0, 20), ...diff.summary };
    feed.unshift(entry);
    feed.length = Math.min(feed.length, 50);
    broadcast('update', { version, ...diff, entry });
  }
  log(`Скан #${version} (${reason}): ${graph.nodes.length} узлов, ${graph.edges.length} связей, ${Date.now() - t0} мс` +
      (diff.summary.text ? ' -- ' + diff.summary.text : ''));
}

function diffGraphs(prev, next) {
  const added = [], removed = [], changed = [];
  if (!prev) return { added, removed, changed, summary: { text: '' } };
  const p = new Map(prev.nodes.map(n => [n.id, n]));
  const nn = new Map(next.nodes.map(n => [n.id, n]));
  for (const [id, n] of nn) {
    const o = p.get(id);
    if (!o) { added.push(id); continue; }
    if (o.lines !== n.lines || o.complexity !== n.complexity || (o.methods || []).length !== (n.methods || []).length ||
        (o.fields || []).length !== (n.fields || []).length || o.fanOut !== n.fanOut || (o.lint || []).length !== (n.lint || []).length) {
      changed.push({ id, dLines: (n.lines || 0) - (o.lines || 0), dMethods: (n.methods || []).length - (o.methods || []).length, dComplexity: (n.complexity || 0) - (o.complexity || 0) });
    }
  }
  for (const id of p.keys()) if (!nn.has(id)) removed.push(id);
  const pe = new Set(prev.edges.map(e => e.id)), ne = new Set(next.edges.map(e => e.id));
  const edgesAdded = [...ne].filter(e => !pe.has(e)).length, edgesRemoved = [...pe].filter(e => !ne.has(e)).length;
  const parts = [];
  if (added.length) parts.push(`+${added.length} узл.`);
  if (removed.length) parts.push(`−${removed.length} узл.`);
  if (changed.length) parts.push(`~${changed.length} изм.`);
  if (edgesAdded || edgesRemoved) parts.push(`связи +${edgesAdded}/−${edgesRemoved}`);
  return { added, removed, changed, summary: { text: parts.join(', '), edgesAdded, edgesRemoved } };
}

// ------------------------------------------------------------ watch

let timer = null;
const pending = new Set();
function schedule(file, reason = 'файлы') {
  if (file) pending.add(file);
  clearTimeout(timer);
  timer = setTimeout(() => {
    const files = [...pending]; pending.clear();
    rebuild(reason, files);
  }, 400);
}

function watch() {
  const watchDir = (dir, filter, reason) => {
    try {
      fs.watch(dir, { recursive: true }, (ev, name) => {
        if (!name) return;
        const r = (path.relative(ROOT, path.join(dir, name)) || name).split(path.sep).join('/');
        if (filter(r)) schedule(r, reason);
      });
      log('Слежу за', path.relative(ROOT, dir) || '.');
    } catch (e) { log('Не удалось следить за', dir, e.message); }
  };
  watchDir(path.join(ROOT, 'Assets'), r => /\.(cs|unity|prefab|asset|meta)$/.test(r), 'файлы');
  if (fs.existsSync(path.join(ROOT, 'ci'))) watchDir(path.join(ROOT, 'ci'), r => r.endsWith('.py'), 'правила lint');
  // коммит/checkout меняют .git/index и HEAD -- обновляем git-статусы
  const gitDir = path.join(ROOT, '.git');
  if (fs.existsSync(gitDir)) {
    try {
      fs.watch(gitDir, (ev, name) => { if (name === 'index' || name === 'HEAD') schedule(null, 'git'); });
    } catch { /* нет так нет */ }
  }
}

// ------------------------------------------------------------ http

function broadcast(event, data) {
  const msg = `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
  for (const res of clients) res.write(msg);
}

function send(res, code, body, type = 'application/json; charset=utf-8') {
  res.writeHead(code, { 'Content-Type': type, 'Cache-Control': 'no-store' });
  res.end(typeof body === 'string' || Buffer.isBuffer(body) ? body : JSON.stringify(body));
}

function safeProjectPath(relPath) {
  if (!relPath) return null;
  const abs = path.resolve(ROOT, relPath);
  if (!abs.startsWith(ROOT + path.sep)) return null;            // не выходим за пределы проекта
  if (!SOURCE_EXT.has(path.extname(abs).toLowerCase())) return null;
  return abs;
}

const server = http.createServer((req, res) => {
  const url = new URL(req.url, 'http://localhost');
  const p = url.pathname;

  if (p === '/api/graph') return send(res, 200, graph);
  if (p === '/api/feed') return send(res, 200, feed);

  if (p === '/api/events') {
    res.writeHead(200, { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-cache', Connection: 'keep-alive' });
    res.write(`event: hello\ndata: ${JSON.stringify({ version })}\n\n`);
    clients.add(res);
    const ping = setInterval(() => res.write(': ping\n\n'), 20000);
    req.on('close', () => { clearInterval(ping); clients.delete(res); });
    return;
  }

  if (p === '/api/source') {
    const abs = safeProjectPath(url.searchParams.get('file'));
    if (!abs || !fs.existsSync(abs)) return send(res, 404, { error: 'нет такого файла' });
    const text = fs.readFileSync(abs, 'utf8');
    if (text.length > 400000) return send(res, 413, { error: 'файл слишком большой' });
    return send(res, 200, { file: url.searchParams.get('file'), text });
  }

  if (p === '/api/rescan') { rebuild('вручную'); return send(res, 200, { version }); }

  // статика
  const file = p === '/' ? 'index.html' : p.slice(1);
  const abs = path.resolve(PUBLIC, file);
  if (!abs.startsWith(PUBLIC + path.sep) || !fs.existsSync(abs)) return send(res, 404, 'Not found', 'text/plain; charset=utf-8');
  send(res, 200, fs.readFileSync(abs), MIME[path.extname(abs)] || 'application/octet-stream');
});

function listen(attempt = 0) {
  server.once('error', e => {
    if (e.code === 'EADDRINUSE' && attempt < 10) { port++; listen(attempt + 1); }
    else { console.error(e); process.exit(1); }
  });
  server.listen(port, '127.0.0.1', () => {
    const url = `http://localhost:${port}`;
    log(`CodeMap: ${url}   (проект: ${ROOT})`);
    if (args.includes('--open')) {
      const cmd = process.platform === 'win32' ? `start "" "${url}"` : process.platform === 'darwin' ? `open "${url}"` : `xdg-open "${url}"`;
      exec(cmd);
    }
  });
}

if (!fs.existsSync(path.join(ROOT, 'Assets'))) {
  console.error(`В ${ROOT} нет папки Assets -- укажи Unity-проект через --root`);
  process.exit(1);
}
rebuild('initial');
watch();
listen();
