// Experiment: mechanically split the emitted tss.js / app.js bundles into one ES module
// per type, wire define-time (inherits) dependencies as side-effect imports, and boot.
//
//   node split.js <siteDir> <outDir> <mode>
//     mode = eager   : boot imports every module (validates the split itself)
//     mode = lazy    : boot imports only the statically reachable closure; the rest are
//                      fetched on demand through a reflection-aware loader
const fs = require('fs');
const path = require('path');

const siteDir = process.argv[2];
const outDir = process.argv[3];
const mode = process.argv[4] || 'eager';

const BUNDLES = [
  { file: 'tss.js', asm: 'tss' },
  { file: 'app.js', asm: 'Tesserae.Tests' },
];

fs.rmSync(outDir, { recursive: true, force: true });
fs.mkdirSync(outDir, { recursive: true });
// Copy the site verbatim, then replace the two bundles with modules.
for (const e of fs.readdirSync(siteDir)) {
  fs.cpSync(path.join(siteDir, e), path.join(outDir, e), { recursive: true });
}
fs.rmSync(path.join(outDir, 'tss.js'));
fs.rmSync(path.join(outDir, 'app.js'));

const MODDIR = path.join(outDir, 'm');
fs.mkdirSync(MODDIR, { recursive: true });

// ---- parse the bundles into per-define blocks + trailing metadata --------------------
const blocks = [];
const tails = [];
for (const { file, asm } of BUNDLES) {
  const text = fs.readFileSync(path.join(siteDir, file), 'utf8');
  const marker = '\n    Transpose.define(';
  const idxs = [];
  for (let i = text.indexOf(marker); i >= 0; i = text.indexOf(marker, i + 1)) idxs.push(i);
  for (let k = 0; k < idxs.length; k++) {
    const start = idxs[k] + 1;
    const end = k + 1 < idxs.length ? idxs[k + 1] : text.lastIndexOf('\n});');
    const body = text.slice(start, end);
    const m = body.match(/^ {4}Transpose\.define\("([^"]+)"/);
    if (m) blocks.push({ name: m[1], body, asm });
  }
  // Anything after the last define and before the closing `});` — the inline $m metadata.
  const lastEnd = text.lastIndexOf('\n});');
  const afterLast = idxs.length ? text.slice(idxs[idxs.length - 1] + 1) : '';
  const metaStart = afterLast.indexOf('\n    var $m = Transpose.setMetadata');
  if (metaStart >= 0) {
    tails.push({ asm, js: afterLast.slice(metaStart, afterLast.lastIndexOf('\n});')) });
    const b = blocks[blocks.length - 1];
    b.body = b.body.slice(0, b.body.length - (afterLast.length - metaStart) + (afterLast.length - afterLast.length));
    b.body = afterLast.slice(0, metaStart);
    b.body = b.body; // trimmed above
  }
}

const byName = new Map(blocks.map(b => [b.name, b]));
const names = [...byName.keys()];
const nameSet = new Set(names);
const idOf = new Map(names.map((n, i) => [n, i]));

const PATH = /\b[A-Za-z_$][A-Za-z0-9_$]*(?:\.[A-Za-z_$][A-Za-z0-9_$]*)+/g;
function knownRefs(text) {
  const found = new Set();
  for (const m of text.match(PATH) || []) {
    let p = m;
    while (p.length) {
      if (nameSet.has(p)) { found.add(p); break; }
      const d = p.lastIndexOf('.');
      if (d < 0) break;
      p = p.slice(0, d);
    }
  }
  return found;
}

// Define-time dependencies: whatever the `inherits` thunk returns must already be defined.
function defineTimeDeps(b) {
  const m = b.body.match(/inherits:\s*function\s*\(\)\s*\{\s*return\s*\[([^\]]*)\]/);
  const s = m ? knownRefs(m[1]) : new Set();
  s.delete(b.name);
  return s;
}
// Everything the body mentions — used only for the reachability closure, not for imports.
function allDeps(b) { const s = knownRefs(b.body); s.delete(b.name); return s; }

const dtDeps = new Map(blocks.map(b => [b.name, defineTimeDeps(b)]));
const anyDeps = new Map(blocks.map(b => [b.name, allDeps(b)]));

// ---- write one module per type ------------------------------------------------------
const fileOf = n => `t${idOf.get(n)}.mjs`;
for (const b of blocks) {
  const deps = [...dtDeps.get(b.name)].filter(d => byName.has(d));
  const imports = ["import '../_rt.mjs';", ...deps.map(d => `import './${fileOf(d)}';`)].join('\n');
  const js = `${imports}\nTranspose.$useAssembly(${JSON.stringify(b.asm)});\n${b.body.replace(/^ {4}/gm, '')}\n`;
  fs.writeFileSync(path.join(MODDIR, fileOf(b.name)), js);
}

// ---- runtime shim -------------------------------------------------------------------
fs.writeFileSync(path.join(outDir, '_rt.mjs'), `
// Sets the ambient assembly a bare Transpose.define registers into. The single-bundle build
// gets this from the Transpose.assembly(...) wrapper; a per-type module has no wrapper, so
// it names its assembly explicitly right before its define.
Transpose.$useAssembly = function (name) {
  var asm = System.Reflection.Assembly.assemblies[name];
  if (!asm) asm = new System.Reflection.Assembly(name, {});
  Transpose.$currentAssembly = asm;
};
`);

// ---- reachability from the entry point ----------------------------------------------
function reach(roots, g) {
  const seen = new Set(roots), st = [...roots];
  while (st.length) { const n = st.pop(); for (const r of g.get(n) || []) if (!seen.has(r)) { seen.add(r); st.push(r); } }
  return seen;
}
const ENTRY = 'Tesserae.Tests.App';
const eagerSet = mode === 'eager' ? new Set(names) : reach([ENTRY], anyDeps);
const lazySet = names.filter(n => !eagerSet.has(n));

// ---- boot module --------------------------------------------------------------------
const manifest = {};
for (const n of names) manifest[n] = `m/${fileOf(n)}`;
fs.writeFileSync(path.join(outDir, 'modules.json'), JSON.stringify(manifest));

const eagerImports = [...eagerSet].map(n => `import './m/${fileOf(n)}';`).join('\n');
const tailJs = tails.map(t => `Transpose.$useAssembly(${JSON.stringify(t.asm)});\n(function ($asm, globals) {\n${t.js}\n})(Transpose.$currentAssembly, Transpose.global);`).join('\n');

fs.writeFileSync(path.join(outDir, 'boot.mjs'), `
import './_rt.mjs';
${eagerImports}

Transpose.assemblyVersion("tss", "1.0.0.0");
Transpose.assemblyVersion("Tesserae.Tests", "1.0.0.0");

// Reflection metadata for BOTH assemblies, always eager: it describes declarations only and
// Transpose.setMetadata already defers an entry whose type is not yet defined (Reflection.js),
// re-deferring on each Transpose.init() until the owning module arrives.
${tailJs}

// The lazily-split types, and the loader that can pull one in on demand.
const LAZY = ${JSON.stringify(Object.fromEntries(lazySet.map(n => [n, `./m/${fileOf(n)}`])))};
Transpose.$lazyTypes = LAZY;
Transpose.$loadType = async function (name) {
  const p = LAZY[name];
  if (!p) return Transpose.Reflection.getType(name);
  await import(p);
  Transpose.init();
  return Transpose.Reflection.getType(name);
};
Transpose.$loadAll = async function () {
  await Promise.all(Object.values(LAZY).map(p => import(p)));
  Transpose.init();
};

Transpose.init();
`);

// ---- rewrite index.html -------------------------------------------------------------
const idx = path.join(outDir, 'index.html');
let html = fs.readFileSync(idx, 'utf8');
html = html.replace(/\s*<script src="tss\.js" defer><\/script>/, '');
html = html.replace(/(\s*)<script src="app\.js" defer><\/script>/, '$1<script type="module" src="boot.mjs"></script>');
fs.writeFileSync(idx, html);

const bytes = n => fs.statSync(path.join(MODDIR, fileOf(n))).size;
const sum = s => [...s].reduce((a, n) => a + bytes(n), 0);
console.log(`mode=${mode}  types=${names.length}  modules written=${names.length}`);
console.log(`  eager: ${eagerSet.size} modules, ${(sum(eagerSet) / 1024).toFixed(0)} KB`);
console.log(`  lazy:  ${lazySet.length} modules, ${(sum(new Set(lazySet)) / 1024).toFixed(0)} KB`);
