// Reachability analysis over the emitted Transpose bundles.
// Splits each bundle into per-`Transpose.define` blocks, extracts the dotted type
// references inside each, and computes what the entry point can reach.
const fs = require('fs');
const dir = process.argv[2];

const bundles = ['tss.js', 'app.js'];

function splitDefines(text, file) {
  const marker = '\n    Transpose.define(';
  const out = [];
  let i = text.indexOf(marker);
  while (i >= 0) {
    const next = text.indexOf(marker, i + 1);
    const body = text.slice(i + 1, next < 0 ? text.length : next);
    const m = body.match(/^ {4}Transpose\.define\("([^"]+)"/);
    if (m) out.push({ name: m[1], body, file, size: body.length });
    i = next;
  }
  return out;
}

const blocks = [];
for (const b of bundles) {
  const t = fs.readFileSync(`${dir}/${b}`, 'utf8');
  blocks.push(...splitDefines(t, b));
}

// Define name -> block. Strip the $N arity suffix for matching too.
const byName = new Map();
for (const b of blocks) byName.set(b.name, b);

// Sort names longest-first so `tss.UI.Theme` wins over `tss.UI`.
const names = [...byName.keys()].sort((a, b) => b.length - a.length);
const nameSet = new Set(names);

// Extract every identifier-path occurrence in a body and keep the ones that are define names.
const PATH = /\b[A-Za-z_$][A-Za-z0-9_$]*(?:\.[A-Za-z_$][A-Za-z0-9_$]*)+/g;

function refsOf(block) {
  const found = new Set();
  for (const m of block.body.match(PATH) || []) {
    // Longest matching prefix that is a known define.
    let path = m;
    while (path.length) {
      if (nameSet.has(path)) { found.add(path); break; }
      const dot = path.lastIndexOf('.');
      if (dot < 0) break;
      path = path.slice(0, dot);
    }
  }
  found.delete(block.name);
  return found;
}

// The `inherits: function () { return [...]; }` refs are needed at DEFINE time.
function inheritsOf(block) {
  const found = new Set();
  const m = block.body.match(/inherits:\s*function\s*\(\)\s*\{\s*return\s*\[([^\]]*)\]/);
  if (!m) return found;
  for (const p of m[1].match(PATH) || []) {
    let path = p;
    while (path.length) {
      if (nameSet.has(path)) { found.add(path); break; }
      const dot = path.lastIndexOf('.');
      if (dot < 0) break;
      path = path.slice(0, dot);
    }
  }
  found.delete(block.name);
  return found;
}

const graph = new Map(), inh = new Map();
for (const b of blocks) { graph.set(b.name, refsOf(b)); inh.set(b.name, inheritsOf(b)); }

function reach(roots, g) {
  const seen = new Set(roots);
  const stack = [...roots];
  while (stack.length) {
    const n = stack.pop();
    for (const r of g.get(n) || []) if (!seen.has(r)) { seen.add(r); stack.push(r); }
  }
  return seen;
}

const entry = 'Tesserae.Tests.App';
const totalSize = blocks.reduce((a, b) => a + b.size, 0);
const sizeOf = s => [...s].reduce((a, n) => a + (byName.get(n)?.size || 0), 0);

const allRefReach = reach([entry], graph);
const inhReach = reach([entry], inh);

const fmt = n => (n / 1024).toFixed(0) + ' KB';
console.log(`types (defines):            ${blocks.length}  (${fmt(totalSize)})`);
console.log(`  tss.js                    ${blocks.filter(b => b.file === 'tss.js').length}`);
console.log(`  app.js                    ${blocks.filter(b => b.file === 'app.js').length}`);
console.log();
console.log(`reachable from ${entry} following ALL emitted refs:`);
console.log(`  ${allRefReach.size} types (${fmt(sizeOf(allRefReach))}) = ${(100 * allRefReach.size / blocks.length).toFixed(1)}% of types, ${(100 * sizeOf(allRefReach) / totalSize).toFixed(1)}% of bytes`);
console.log();
console.log(`reachable following ONLY inherits (define-time) edges:`);
console.log(`  ${inhReach.size} types (${fmt(sizeOf(inhReach))})`);
console.log();

// Strongly-connected components over ALL refs (how clusterable is the graph?).
function scc(g, nodes) {
  let idx = 0; const index = new Map(), low = new Map(), on = new Set(), st = [], out = [];
  function strong(v) {
    const work = [[v, 0]];
    index.set(v, idx); low.set(v, idx); idx++; st.push(v); on.add(v);
    while (work.length) {
      const frame = work[work.length - 1];
      const [n, pi] = frame;
      const succ = [...(g.get(n) || [])];
      if (pi < succ.length) {
        frame[1]++;
        const w = succ[pi];
        if (!index.has(w)) { index.set(w, idx); low.set(w, idx); idx++; st.push(w); on.add(w); work.push([w, 0]); }
        else if (on.has(w)) low.set(n, Math.min(low.get(n), index.get(w)));
      } else {
        if (low.get(n) === index.get(n)) {
          const comp = []; let w;
          do { w = st.pop(); on.delete(w); comp.push(w); } while (w !== n);
          out.push(comp);
        }
        work.pop();
        if (work.length) { const p = work[work.length - 1][0]; low.set(p, Math.min(low.get(p), low.get(n))); }
      }
    }
  }
  for (const n of nodes) if (!index.has(n)) strong(n);
  return out;
}

const comps = scc(graph, names).sort((a, b) => b.length - a.length);
console.log(`strongly-connected components over ALL refs: ${comps.length}`);
console.log(`  largest 5: ${comps.slice(0, 5).map(c => c.length).join(', ')}`);
console.log(`  largest SCC = ${fmt(sizeOf(new Set(comps[0])))}`);
const inhComps = scc(inh, names).sort((a, b) => b.length - a.length);
console.log(`strongly-connected components over inherits only: ${inhComps.length}, largest ${inhComps[0].length}`);

// What is NOT reachable statically -- the true lazy-load candidates.
const unreach = names.filter(n => !allRefReach.has(n));
console.log();
console.log(`NOT statically reachable from the entry point: ${unreach.length} types (${fmt(sizeOf(new Set(unreach)))})`);
console.log(`  sample: ${unreach.slice(0, 15).join(', ')}`);
