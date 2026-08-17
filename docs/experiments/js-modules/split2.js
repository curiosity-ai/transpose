// Experiment 2: lazy per-type modules + metadata-backed TYPE STUBS, so reflection
// (Assembly.GetTypes / IsAssignableFrom / GetCustomAttributes / Name) still sees every type
// while its code stays unloaded. Activator.CreateInstance on a stub triggers the load.
//
//   node split2.js <siteDir> <outDir> [preloadOnStub]
const fs = require('fs');
const path = require('path');

const siteDir = process.argv[2];
const outDir = process.argv[3];

const BUNDLES = [{ file: 'tss.js', asm: 'tss' }, { file: 'app.js', asm: 'Tesserae.Tests' }];

fs.rmSync(outDir, { recursive: true, force: true });
fs.mkdirSync(outDir, { recursive: true });
for (const e of fs.readdirSync(siteDir)) fs.cpSync(path.join(siteDir, e), path.join(outDir, e), { recursive: true });
fs.rmSync(path.join(outDir, 'tss.js'));
fs.rmSync(path.join(outDir, 'app.js'));
const MODDIR = path.join(outDir, 'm');
fs.mkdirSync(MODDIR, { recursive: true });

const blocks = [], tails = [];
for (const { file, asm } of BUNDLES) {
  const text = fs.readFileSync(path.join(siteDir, file), 'utf8');
  const marker = '\n    Transpose.define(';
  const idxs = [];
  for (let i = text.indexOf(marker); i >= 0; i = text.indexOf(marker, i + 1)) idxs.push(i);
  for (let k = 0; k < idxs.length; k++) {
    const start = idxs[k] + 1;
    const end = k + 1 < idxs.length ? idxs[k + 1] : text.lastIndexOf('\n});');
    let body = text.slice(start, end);
    if (k === idxs.length - 1) {
      const ms = body.indexOf('\n    var $m = Transpose.setMetadata');
      if (ms >= 0) { tails.push({ asm, js: body.slice(ms) }); body = body.slice(0, ms); }
    }
    const m = body.match(/^ {4}Transpose\.define\("([^"]+)"/);
    if (m) blocks.push({ name: m[1], body, asm });
  }
}

const byName = new Map(blocks.map(b => [b.name, b]));
const names = [...byName.keys()];
const nameSet = new Set(names);
const idOf = new Map(names.map((n, i) => [n, i]));
const PATH = /\b[A-Za-z_$][A-Za-z0-9_$]*(?:\.[A-Za-z_$][A-Za-z0-9_$]*)+/g;
function knownRefs(text) {
  const f = new Set();
  for (const m of text.match(PATH) || []) {
    let p = m;
    while (p.length) { if (nameSet.has(p)) { f.add(p); break; } const d = p.lastIndexOf('.'); if (d < 0) break; p = p.slice(0, d); }
  }
  return f;
}
const dt = new Map(), any = new Map(), kind = new Map(), inheritNames = new Map();
for (const b of blocks) {
  const im = b.body.match(/inherits:\s*function\s*\(\)\s*\{\s*return\s*\[([^\]]*)\]/);
  const s = im ? knownRefs(im[1]) : new Set(); s.delete(b.name);
  dt.set(b.name, s);
  const a = knownRefs(b.body); a.delete(b.name); any.set(b.name, a);
  kind.set(b.name, /\$kind:\s*"interface"/.test(b.body) ? 'interface' : /\$kind:\s*"(struct|enum)"/.exec(b.body)?.[1] || 'class');
  // Full inherits list as *emitted text* (may include runtime types outside this graph).
  inheritNames.set(b.name, im ? im[1].split(',').map(s => s.trim()).filter(Boolean) : []);
}

const fileOf = n => `t${idOf.get(n)}.mjs`;
for (const b of blocks) {
  const deps = [...dt.get(b.name)].filter(d => byName.has(d));
  const imports = ["import '../_rt.mjs';", ...deps.map(d => `import './${fileOf(d)}';`)].join('\n');
  fs.writeFileSync(path.join(MODDIR, fileOf(b.name)),
    `${imports}\nTranspose.$useAssembly(${JSON.stringify(b.asm)});\n${b.body.replace(/^ {4}/gm, '')}\n`);
}

function reach(roots, g) { const s = new Set(roots), st = [...roots]; while (st.length) { const n = st.pop(); for (const r of g.get(n) || []) if (!s.has(r)) { s.add(r); st.push(r); } } return s; }
const ENTRY = 'Tesserae.Tests.App';
const eagerSet = reach([ENTRY], any);
const lazy = names.filter(n => !eagerSet.has(n));

// Manifest for the lazy types: module path, kind, assembly, and the *emitted* inherits list.
const man = {};
for (const n of lazy) man[n] = { m: `./m/${fileOf(n)}`, k: kind.get(n), a: byName.get(n).asm, i: inheritNames.get(n) };

fs.writeFileSync(path.join(outDir, '_rt.mjs'), `
Transpose.$useAssembly = function (name) {
  var asm = System.Reflection.Assembly.assemblies[name];
  if (!asm) asm = new System.Reflection.Assembly(name, {});
  Transpose.$currentAssembly = asm;
};
`);

const tailJs = tails.map(t => `Transpose.$useAssembly(${JSON.stringify(t.asm)});\n(function ($asm, globals) {\n${t.js}\n})(Transpose.$currentAssembly, Transpose.global);`).join('\n');

fs.writeFileSync(path.join(outDir, 'boot.mjs'), `
import './_rt.mjs';
${[...eagerSet].map(n => `import './m/${fileOf(n)}';`).join('\n')}

Transpose.assemblyVersion("tss", "1.0.0.0");
Transpose.assemblyVersion("Tesserae.Tests", "1.0.0.0");

const MAN = ${JSON.stringify(man)};

// ---- stub types -------------------------------------------------------------------
// A stub stands in for a type whose module is not loaded. It is registered exactly where the
// real Transpose.define would put it (the global path + the assembly's $types map), so
// Assembly.GetTypes(), Type.Name, IsInterface, IsAssignableFrom and the reflection metadata
// all behave as if the type were present. Only *using* the type (construction, calls) needs
// the real thing, and that is what forces the module load.
const STUBS = Object.create(null);
const STUB_CARRY = Object.create(null);
function place(name, value) {
  const parts = name.split('.');
  let scope = Transpose.global;
  for (let i = 0; i < parts.length - 1; i++) scope = scope[parts[i]] || (scope[parts[i]] = {});
  const leaf = parts[parts.length - 1];
  if (scope[leaf] !== undefined && typeof scope[leaf] === 'function') return; // a real type already lives here
  // Carry over anything already hung off this path (nested types placed earlier).
  const existing = scope[leaf];
  if (existing) for (const k of Object.keys(existing)) value[k] = existing[k];
  scope[leaf] = value;
}
// Dev-mode fault-in: resolve a stub's module synchronously so an existing synchronous call
// (Activator.CreateInstance, a static-member touch) keeps working. Production wants the async
// boundary instead; this exists to prove the semantics are reachable at all.
// Seeded with every module the boot graph already pulled in, so a fault-in never re-evaluates
// a define that has already run (Transpose.define rejects a redefinition).
const SYNC_LOADED = new Set(${JSON.stringify(['_rt.mjs', ...[...eagerSet].map(n => `./m/${fileOf(n)}`)])});
const MOD_OF = ${JSON.stringify(Object.fromEntries(lazy.map(n => [`./m/${fileOf(n)}`, n])))};
function loadSync(url) {
  if (SYNC_LOADED.has(url)) return;
  SYNC_LOADED.add(url);
  // The stub occupies the global path and the assembly's $types slot; clear both so the real
  // Transpose.define does not report "Class X is already defined".
  const owner = MOD_OF[url];
  if (owner) {
    const parts = owner.split('.');
    let scope = Transpose.global;
    for (let i = 0; i < parts.length - 1 && scope; i++) scope = scope[parts[i]];
    const stub = scope && scope[parts[parts.length - 1]];
    if (stub && stub.$stub) {
      const carry = {};
      for (const k of Object.keys(stub)) if (k.charAt(0) !== '$') carry[k] = stub[k];
      delete scope[parts[parts.length - 1]];
      STUB_CARRY[owner] = carry;
      const asm = System.Reflection.Assembly.assemblies[MAN[owner].a];
      if (asm) delete asm.$types[owner];
    }
  }
  const xhr = new XMLHttpRequest();
  xhr.open('GET', new URL(url, import.meta.url).href, false);
  xhr.send(null);
  let src = xhr.responseText;
  // Follow the module's own side-effect imports first, then evaluate it with them stripped.
  const base = url.slice(0, url.lastIndexOf('/') + 1);
  for (const m of src.matchAll(/^import\\s+'([^']+)';$/gm)) {
    const rel = m[1];
    loadSync(rel.startsWith('../') ? rel.slice(3) : base + rel.replace('./', ''));
  }
  src = src.replace(/^import\\s+'[^']+';$/gm, '');
  (0, eval)(src);
  if (owner && STUB_CARRY[owner]) {
    const real = Transpose.Reflection.getType(owner);
    if (real) for (const k of Object.keys(STUB_CARRY[owner])) if (real[k] === undefined) real[k] = STUB_CARRY[owner][k];
    delete STUB_CARRY[owner];
  }
}

function makeStub(name, info) {
  const fn = function () {
    loadSync(info.m);
    Transpose.init();
    const real = Transpose.Reflection.getType(name);
    if (!real || real.$stub) throw new Error('Transpose: could not fault in module for ' + name);
    if (!real.$metadata && fn.$metadata) { real.$metadata = fn.$metadata; real.$getMetadata = Transpose.Reflection.getMetadata; }
    // Re-dispatch the construction onto the real class.
    return new real();
  };
  fn.$$name = name;
  fn.$stub = true;
  fn.$module = info.m;
  fn.$kind = info.k;
  if (info.k === 'interface') fn.$isInterface = true;
  fn.prototype = { constructor: fn };
  fn.$assembly = System.Reflection.Assembly.assemblies[info.a];
  return fn;
}
// Outermost first: a nested type is placed *onto* its container, so the container's stub has
// to exist before it, or placing the nested one would create a plain {} that the container's
// own stub then overwrites (losing the nested type).
for (const name of Object.keys(MAN).sort((a, b) => a.split('.').length - b.split('.').length)) {
  const info = MAN[name];
  if (name.indexOf('$') >= 0) continue;                 // skip generic definitions in this experiment
  const asm = System.Reflection.Assembly.assemblies[info.a] || new System.Reflection.Assembly(info.a, {});
  const stub = makeStub(name, info);
  STUBS[name] = stub;
  place(name, stub);
  asm.\$types[name] = stub;
}
// Resolve the inherits chain once every stub exists (a stub may extend another stub).
for (const name in STUBS) {
  const list = MAN[name].i.map(expr => { try { return eval(expr); } catch { return null; } }).filter(Boolean);
  STUBS[name].$$inherits = list;
  const iface = [];
  for (const b of list) {
    if (b.$isInterface) iface.push(b);
    if (b.$interfaces) iface.push.apply(iface, b.$interfaces);
    if (b.$$inherits) for (const g of b.$$inherits) if (g.$isInterface) iface.push(g);
  }
  STUBS[name].$interfaces = iface;
}

// Reflection metadata (both assemblies) — eager, and it attaches to the stubs.
${tailJs}

// ---- the loader -------------------------------------------------------------------
Transpose.$loadType = async function (name) {
  const info = MAN[name];
  if (!info) return Transpose.Reflection.getType(name);
  const meta = STUBS[name] && STUBS[name].$metadata;
  await import(info.m);
  Transpose.init();
  const real = Transpose.Reflection.getType(name);
  if (real && meta && !real.$metadata) { real.$metadata = meta; real.$getMetadata = Transpose.Reflection.getMetadata; }
  return real;
};
Transpose.$isStub = t => !!(t && t.$stub);

// The runtime hook: any reflective *use* of a type faults its module in first.
// (Activator.CreateInstance is the one Tesserae's sample gallery goes through.)
function faultIn(type) {
  if (!type || !type.$stub) return type;
  const name = type.$$name;
  loadSync(MAN[name].m);
  Transpose.init();
  const real = Transpose.Reflection.getType(name);
  if (real && !real.$stub) {
    if (!real.$metadata && type.$metadata) { real.$metadata = type.$metadata; real.$getMetadata = Transpose.Reflection.getMetadata; }
    return real;
  }
  return type;
}
const _createInstance = Transpose.createInstance;
Transpose.createInstance = function (type, nonPublic, args) { return _createInstance.call(this, faultIn(type), nonPublic, args); };
Transpose.$faultIn = faultIn;

Transpose.init();
`);

let html = fs.readFileSync(path.join(outDir, 'index.html'), 'utf8');
html = html.replace(/\s*<script src="tss\.js" defer><\/script>/, '');
html = html.replace(/(\s*)<script src="app\.js" defer><\/script>/, '$1<script type="module" src="boot.mjs"></script>');
fs.writeFileSync(path.join(outDir, 'index.html'), html);

const bytes = n => fs.statSync(path.join(MODDIR, fileOf(n))).size;
const sum = s => [...s].reduce((a, n) => a + bytes(n), 0);
console.log(`types=${names.length}  eager=${eagerSet.size} (${(sum(eagerSet) / 1024).toFixed(0)} KB)  lazy=${lazy.length} (${(sum(new Set(lazy)) / 1024).toFixed(0)} KB)`);
