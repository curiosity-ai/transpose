// Experiment 3: chunk = strongly-connected component of the FULL reference graph.
// The condensation of that graph is a DAG, so a chunk can import every chunk it references
// as a side-effect ESM import and the evaluation order is always safe — including for
// define-time (inherits) references, which is what a per-class split cannot guarantee.
//
//   node split3.js <siteDir> <outDir>
const fs = require('fs');
const path = require('path');

const siteDir = process.argv[2], outDir = process.argv[3];
const BUNDLES = [{ file: 'tss.js', asm: 'tss' }, { file: 'app.js', asm: 'Tesserae.Tests' }];

fs.rmSync(outDir, { recursive: true, force: true });
fs.mkdirSync(outDir, { recursive: true });
for (const e of fs.readdirSync(siteDir)) fs.cpSync(path.join(siteDir, e), path.join(outDir, e), { recursive: true });
fs.rmSync(path.join(outDir, 'tss.js')); fs.rmSync(path.join(outDir, 'app.js'));
const MODDIR = path.join(outDir, 'c'); fs.mkdirSync(MODDIR, { recursive: true });

// ---- parse ---------------------------------------------------------------------------
const blocks = [], tails = [];
for (const { file, asm } of BUNDLES) {
  const text = fs.readFileSync(path.join(siteDir, file), 'utf8');
  const idxs = [];
  for (const m of text.matchAll(/\n {4}Transpose\.definei?\(/g)) idxs.push(m.index);
  for (let k = 0; k < idxs.length; k++) {
    let body = text.slice(idxs[k] + 1, k + 1 < idxs.length ? idxs[k + 1] : text.lastIndexOf('\n});'));
    if (k === idxs.length - 1) {
      const ms = body.indexOf('\n    var $m = Transpose.setMetadata');
      if (ms >= 0) { tails.push({ asm, js: body.slice(ms) }); body = body.slice(0, ms); }
    }
    const m = body.match(/^ {4}Transpose\.definei?\("([^"]+)"/);
    if (m) blocks.push({ name: m[1], body, asm, order: blocks.length });
  }
}
const byName = new Map(blocks.map(b => [b.name, b]));
const names = [...byName.keys()], nameSet = new Set(names);
const PATH = /\b[A-Za-z_$][A-Za-z0-9_$]*(?:\.[A-Za-z_$][A-Za-z0-9_$]*)+/g;
// HARD reference: the emitted code *uses* the type — constructs it (`new X(`), reads a static
// member (`X.`), or extends it (inherits). Those need the real class, so they are import edges.
// SOFT reference: a bare mention — `typeof(X)`, a generic type argument, an `is`/cast operand.
// Those only need a Type object, which a stub already provides, so they are NOT import edges.
// Is the reference at `at` inside the argument list of a `Name$N(...)` generic application?
function isGenericArgument(text, at) {
  let depth = 0;
  for (let i = at - 1; i >= 0 && at - i < 4000; i--) {
    const ch = text.charAt(i);
    if (ch === ')') depth++;
    else if (ch === '(') {
      if (depth === 0) return /\$\d+$/.test(text.slice(Math.max(0, i - 40), i));
      depth--;
    }
  }
  return false;
}
function hardRefs(text) {
  const f = new Set();
  for (const m of text.matchAll(PATH)) {
    const after = text.charAt(m.index + m[0].length);
    let p = m[0];
    while (p.length) {
      if (nameSet.has(p)) {
        // `p` is the type; anything after it in the matched path means a member access.
        // Hard when the code reaches *into* the type (a member access or a construction), and
        // also when the type is a generic type ARGUMENT: `Foo$1(X)` builds a generic instance
        // whose base class can be X itself, so X must be defined before the application runs.
        const isMemberAccess = p.length < m[0].length || after === '.' || after === '(';
        if (isMemberAccess || isGenericArgument(text, m.index)) f.add(p);
        break;
      }
      const d = p.lastIndexOf('.'); if (d < 0) break; p = p.slice(0, d);
    }
  }
  return f;
}
const g = new Map();
for (const b of blocks) {
  const s = hardRefs(b.body);
  // inherits is always hard, even though it reads as a bare mention.
  for (const im of b.body.matchAll(/inherits:\s*function\s*\(\)\s*\{\s*return\s*\[([^\]]*)\]/g))
    for (const m of im[1].matchAll(PATH)) { let p = m[0]; while (p.length) { if (nameSet.has(p)) { s.add(p); break; } const d = p.lastIndexOf('.'); if (d < 0) break; p = p.slice(0, d); } }
  s.delete(b.name);
  g.set(b.name, s);
}

// ---- SCC (iterative Tarjan) ------------------------------------------------------------
function scc(g, nodes) {
  let idx = 0; const index = new Map(), low = new Map(), on = new Set(), st = [], out = [];
  for (const root of nodes) {
    if (index.has(root)) continue;
    const work = [[root, 0]];
    index.set(root, idx); low.set(root, idx); idx++; st.push(root); on.add(root);
    while (work.length) {
      const fr = work[work.length - 1], n = fr[0], succ = [...(g.get(n) || [])];
      if (fr[1] < succ.length) {
        const w = succ[fr[1]++];
        if (!index.has(w)) { index.set(w, idx); low.set(w, idx); idx++; st.push(w); on.add(w); work.push([w, 0]); }
        else if (on.has(w)) low.set(n, Math.min(low.get(n), index.get(w)));
      } else {
        if (low.get(n) === index.get(n)) { const c = []; let w; do { w = st.pop(); on.delete(w); c.push(w); } while (w !== n); out.push(c); }
        work.pop();
        if (work.length) { const p = work[work.length - 1][0]; low.set(p, Math.min(low.get(p), low.get(n))); }
      }
    }
  }
  return out;
}
const comps = scc(g, names);
const chunkOf = new Map();
comps.forEach((c, i) => c.forEach(n => chunkOf.set(n, i)));

// Chunk-level DAG.
const chunkDeps = comps.map(() => new Set());
for (const n of names) for (const r of g.get(n)) { const a = chunkOf.get(n), b = chunkOf.get(r); if (a !== b) chunkDeps[a].add(b); }

// ---- write one module per chunk -------------------------------------------------------
// Inside a chunk, types are emitted in the bundle's original order — which the compiler already
// sorted by dependency depth, so inherits is satisfied within the chunk exactly as it is today.
for (let i = 0; i < comps.length; i++) {
  const members = comps[i].map(n => byName.get(n)).sort((a, b) => a.order - b.order);
  const imports = ["import '../_rt.mjs';", ...[...chunkDeps[i]].map(d => `import './c${d}.mjs';`)].join('\n');
  const body = members.map(b => `Transpose.$useAssembly(${JSON.stringify(b.asm)});\n${b.body.replace(/^ {4}/gm, '')}`).join('\n');
  fs.writeFileSync(path.join(MODDIR, `c${i}.mjs`), `${imports}\n${body}\n`);
}

fs.writeFileSync(path.join(outDir, '_rt.mjs'), `
Transpose.$useAssembly = function (name) {
  var asm = System.Reflection.Assembly.assemblies[name];
  if (!asm) asm = new System.Reflection.Assembly(name, {});
  Transpose.$currentAssembly = asm;
};
`);

// ---- eager set = chunks reachable from the entry point ---------------------------------
const ENTRY = 'Tesserae.Tests.App';
const entryChunk = chunkOf.get(ENTRY);
const eagerChunks = new Set([entryChunk]); {
  const st = [entryChunk];
  while (st.length) { const c = st.pop(); for (const d of chunkDeps[c]) if (!eagerChunks.has(d)) { eagerChunks.add(d); st.push(d); } }
}
const lazyChunks = comps.map((_, i) => i).filter(i => !eagerChunks.has(i));
const lazyTypes = lazyChunks.flatMap(i => comps[i]);

const kindOf = n => /\$kind:\s*"interface"/.test(byName.get(n).body) ? 'interface' : 'class';
const inheritsOf = n => { const m = byName.get(n).body.match(/^ {4}Transpose\.definei?\("[^"]+",[\s\S]*?inherits:\s*function\s*\(\)\s*\{\s*return\s*\[([^\]]*)\]/); return m ? m[1].split(',').map(s => s.trim()).filter(Boolean) : []; };
const man = {};
for (const n of lazyTypes) man[n] = { c: `./c/c${chunkOf.get(n)}.mjs`, k: kindOf(n), a: byName.get(n).asm, i: inheritsOf(n) };

const tailJs = tails.map(t => `Transpose.$useAssembly(${JSON.stringify(t.asm)});\n(function ($asm, globals) {\n${t.js}\n})(Transpose.$currentAssembly, Transpose.global);`).join('\n');

fs.writeFileSync(path.join(outDir, 'boot.mjs'), `
import './_rt.mjs';
${[...eagerChunks].map(i => `import './c/c${i}.mjs';`).join('\n')}

Transpose.assemblyVersion("tss", "1.0.0.0");
Transpose.assemblyVersion("Tesserae.Tests", "1.0.0.0");

const MAN = ${JSON.stringify(man)};
const STUBS = Object.create(null), STUB_CARRY = Object.create(null);
const CHUNK_MEMBERS = ${JSON.stringify(Object.fromEntries(lazyChunks.map(i => [`./c/c${i}.mjs`, comps[i]])))};

function place(name, value) {
  const parts = name.split('.');
  let scope = Transpose.global;
  for (let i = 0; i < parts.length - 1; i++) scope = scope[parts[i]] || (scope[parts[i]] = {});
  const leaf = parts[parts.length - 1];
  if (typeof scope[leaf] === 'function') return;
  const existing = scope[leaf];
  if (existing) for (const k of Object.keys(existing)) value[k] = existing[k];
  scope[leaf] = value;
}
function makeStub(name, info) {
  const fn = function () { throw new Error('Transpose: ' + name + ' is in an unloaded chunk'); };
  fn.$$name = name; fn.$stub = true; fn.$chunk = info.c; fn.$kind = info.k;
  if (info.k === 'interface') fn.$isInterface = true;
  fn.prototype = { constructor: fn };
  fn.$assembly = System.Reflection.Assembly.assemblies[info.a];
  return fn;
}
for (const name of Object.keys(MAN).sort((a, b) => a.split('.').length - b.split('.').length)) {
  if (name.indexOf('$') >= 0) continue;
  const info = MAN[name];
  const asm = System.Reflection.Assembly.assemblies[info.a] || new System.Reflection.Assembly(info.a, {});
  const stub = makeStub(name, info);
  STUBS[name] = stub; place(name, stub); asm.$types[name] = stub;
}
for (const name in STUBS) {
  const list = MAN[name].i.map(e => { try { return eval(e); } catch { return null; } }).filter(Boolean);
  STUBS[name].$$inherits = list;
  const ifc = [];
  for (const b of list) { if (b.$isInterface) ifc.push(b); if (b.$interfaces) ifc.push.apply(ifc, b.$interfaces); if (b.$$inherits) for (const gg of b.$$inherits) if (gg.$isInterface) ifc.push(gg); }
  STUBS[name].$interfaces = ifc;
}

${tailJs}

// ---- synchronous chunk fault-in --------------------------------------------------------
const LOADED = new Set(${JSON.stringify(['_rt.mjs', ...[...eagerChunks].map(i => `./c/c${i}.mjs`)])});
function loadChunk(url) {
  if (LOADED.has(url)) return;
  LOADED.add(url);
  for (const name of (CHUNK_MEMBERS[url] || [])) {
    const parts = name.split('.');
    let scope = Transpose.global;
    for (let i = 0; i < parts.length - 1 && scope; i++) scope = scope[parts[i]];
    const stub = scope && scope[parts[parts.length - 1]];
    if (stub && stub.$stub) {
      const carry = {};
      for (const k of Object.keys(stub)) if (k.charAt(0) !== '$') carry[k] = stub[k];
      STUB_CARRY[name] = { carry: carry, meta: stub.$metadata };
      delete scope[parts[parts.length - 1]];
      const asm = System.Reflection.Assembly.assemblies[MAN[name].a];
      if (asm) delete asm.$types[name];
    }
  }
  const xhr = new XMLHttpRequest();
  xhr.open('GET', new URL(url, import.meta.url).href, false);
  xhr.send(null);
  let src = xhr.responseText;
  const base = url.slice(0, url.lastIndexOf('/') + 1);
  for (const m of src.matchAll(/^import\\s+'([^']+)';$/gm)) {
    const rel = m[1];
    loadChunk(rel.indexOf('../') === 0 ? rel.slice(3) : base + rel.replace('./', ''));
  }
  (0, eval)(src.replace(/^import\\s+'[^']+';$/gm, ''));
  Transpose.init();
  for (const name of (CHUNK_MEMBERS[url] || [])) {
    const saved = STUB_CARRY[name]; if (!saved) continue;
    const real = Transpose.Reflection.getType(name);
    if (real && !real.$stub) {
      for (const k of Object.keys(saved.carry)) if (real[k] === undefined) real[k] = saved.carry[k];
      if (!real.$metadata && saved.meta) { real.$metadata = saved.meta; real.$getMetadata = Transpose.Reflection.getMetadata; }
    }
    delete STUB_CARRY[name];
  }
}
function faultIn(type) {
  if (!type || !type.$stub) return type;
  loadChunk(type.$chunk);
  const real = Transpose.Reflection.getType(type.$$name);
  return real && !real.$stub ? real : type;
}
const _ci = Transpose.createInstance;
Transpose.createInstance = function (t, nonPublic, args) { return _ci.call(this, faultIn(t), nonPublic, args); };
Transpose.$faultIn = faultIn;
Transpose.$loadChunk = loadChunk;

Transpose.init();
`);

let html = fs.readFileSync(path.join(outDir, 'index.html'), 'utf8');
html = html.replace(/\s*<script src="tss\.js" defer><\/script>/, '');
html = html.replace(/(\s*)<script src="app\.js" defer><\/script>/, '$1<script type="module" src="boot.mjs"></script>');
fs.writeFileSync(path.join(outDir, 'index.html'), html);

const sz = i => fs.statSync(path.join(MODDIR, `c${i}.mjs`)).size;
const tot = a => a.reduce((x, i) => x + sz(i), 0);
console.log(`types=${names.length}  chunks=${comps.length}`);
console.log(`  eager chunks ${eagerChunks.size}: ${(tot([...eagerChunks]) / 1024).toFixed(0)} KB  (${comps.filter((_, i) => eagerChunks.has(i)).flat().length} types)`);
console.log(`  lazy  chunks ${lazyChunks.length}: ${(tot(lazyChunks) / 1024).toFixed(0)} KB  (${lazyTypes.length} types)`);
console.log(`  biggest chunk: ${(Math.max(...comps.map((_, i) => sz(i))) / 1024).toFixed(0)} KB`);
