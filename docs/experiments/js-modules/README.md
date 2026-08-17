# js-modules experiment harness

The evidence behind [`TODO.modules.md`](../../../TODO.modules.md): can `tps` emit ES modules per
class / per cluster, loaded on demand?

Nothing here is part of the compiler. Each script works on an **already-emitted site**, re-chunking
its `tss.js` / `app.js` into modules and re-rendering the page, so every runtime question is answered
without an emitter change. The parsing is textual and deliberately throwaway — a real implementation
classifies references on Roslyn symbols, which is precisely the difference that made `split4.js`
unsound (see §4 of the TODO).

## Prerequisites

A built Tesserae sample site, and Playwright's Chromium:

```bash
cd /path/to/transpose && dotnet build Transpose.slnx -c Release
export PATH="$PWD/Transpose/Transpose.Compiler/bin/Release/net10.0:$PATH"
cd /path/to/tesserae && dotnet build Tesserae.sln -c Debug
SITE=/path/to/tesserae/Tesserae.Tests/bin/Debug/netstandard2.0/tps
```

The scripts hardcode `/opt/pw-browsers/chromium-1194/chrome-linux/chrome` and require
`playwright` resolvable from `/opt/node22/lib/node_modules` — adjust for another machine.

## The scripts

| Script | What it does |
| --- | --- |
| `graph.js <site>` | Statistics only. Splits the bundles into per-`define` blocks, builds the reference graph, reports reachability from the entry point, SCC sizes, and which types are statically unreachable. |
| `split.js <site> <out> eager\|lazy` | Experiment A/B — one module per type, imports from `inherits` edges only. `eager` imports everything (validates the split); `lazy` imports only the entry closure (shows reflection breaking). |
| `split2.js <site> <out>` | Experiment C — adds metadata-backed type stubs and a synchronous fault-in loader. Restores the full sidebar; still fails on static calls into stubs. |
| `split3.js <site> <out>` | **Experiment D — the working design.** Chunk = SCC of the full reference graph, so the chunk DAG is safe to wire with side-effect imports. Renders identically to the baseline with zero errors. |
| `split4.js <site> <out>` | Experiment E — D plus a hard/soft reference classifier for finer chunks. Better on paper, **not sound**; kept as the statement of the open problem. |
| `probe.js <site> <prefix> [port]` | The correctness oracle. Serves the site, loads it in Chromium, fingerprints the page, clicks all 140 samples and records each rendered pane, writes `<prefix>.json` + `<prefix>.png`. |
| `marginal.js` | Static per-sample marginal load cost from the chunk DAG produced by `split3.js` into `site-scc`. |

## The loop

```bash
node probe.js  "$SITE" baseline 5199          # oracle for the unmodified build
node split3.js "$SITE" site-scc               # re-chunk
node probe.js  "$PWD/site-scc" scc 5213       # oracle for the modular build
```

Then diff the two fingerprints — this is the gate, and it is what "0 errors, 140/140 identical" in
the TODO means:

```bash
node -e "
const a=require('./baseline.json'), b=require('./scc.json');
const k=r=>r.samples.map(s=>\`\${s.label}|\${s.n}|\${s.t}\`);
const ka=k(a), kb=k(b);
console.log('differing rows:', ka.filter((x,i)=>x!==kb[i]).length,
            '| errors:', a.errors.length, b.errors.length);
"
```

## Caveats worth knowing before trusting a number

- Reference extraction is **textual**, so anything that looks like a dotted type name counts. It was
  checked against the one case that mattered (the samples' `typeof(OtherSample)` "See Also" lists are
  real `System.Array.init([...])` references, not string literals), but it is not a substitute for
  the symbol graph.
- `Transpose.definei` blocks (interfaces with variance) are a *second* block-start form and a block
  may contain more than one `inherits`. `split`/`split2`/`split3` survive this because their
  all-references graph is conservative enough to cover it; `split4.js` had to handle both explicitly.
- The fault-in path uses a **synchronous `XMLHttpRequest` + `eval`**. That is what lets an existing
  synchronous `Activator.CreateInstance` work at all, and it is a dev-mode device only.
- The page fingerprint reports 1648 elements against the baseline's 1649. The per-sample fingerprints
  match exactly across all 140, and the difference reproduces in every modular variant including the
  fully-eager one, so it is a property of the split boot sequence rather than of lazy loading. It was
  not chased down.
