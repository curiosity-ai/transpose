# TODO.modules — emitting JavaScript modules per class / per cluster

Investigation into adding an output mode where `tps` emits ES modules (`.mjs`) — one per class, or
one per *cluster* of classes — that the page loads on demand, instead of the single bundle it emits
today. Measured end to end against **Tesserae** (`Tesserae.Tests`, the sample gallery), with the
rendered page diffed against an unmodified build.

Status: **implemented, end to end.** `outputBy: "Module"` in a project's `tps.json` makes `tps` emit
one ES module per chunk plus an entry module, and the runtime (`Transpose.Modules`,
`Activator.CreateInstanceAsync`) loads the deferred ones on demand. See §5a for the runtime half and
§7a for the emitter half and what it measures on Tesserae.

The report below is the original feasibility investigation, kept because it is the reasoning the
implementation follows and it records what was rejected and why. Its measurements were made by
mechanically re-chunking an already-emitted site; §7a has the numbers from the real compiler.

---

## 1. The short version

| Question | Answer |
| --- | --- |
| Can the emitted code be split into ES modules at all? | **Yes**, and cheaply. The output format needs no change beyond a wrapper. |
| Does the page still render correctly? | **Yes** — 140/140 Tesserae samples structurally identical to the baseline, zero console errors. |
| Can modules be loaded on demand? | **Yes**, if the chunk boundary is an SCC of the reference graph. Validated. |
| Does reflection survive? | **Yes**, with eager metadata plus registered *type stubs*. `GetTypes` / `IsAssignableFrom` / `GetCustomAttributes` / `Name` all work against unloaded types. |
| Can reflection auto-load a module? | **Only asynchronously** — and that half is now **implemented**: `Transpose.Modules` + `Activator.CreateInstanceAsync` (§5a). `Activator.CreateInstance` stays synchronous and now fails with a message naming the module. |
| Is it worth it for Tesserae? | **~14% off the initial page** (gzipped). Less than it sounds, and for reasons that are structural — see §6. |

The single most surprising number: **reflection metadata is 25% of the gzipped payload** of the
Tesserae sample app (274 KB of 1.10 MB) and module splitting does not touch it. There is a much
cheaper win available there than in code splitting.

---

## 2. Why splitting is easy: the emitted code has no imports to rewrite

Three properties of the current output make this far less work than it would be for a typical
compiler:

1. **Every type reference is a dotted global.** `TypeRef` (`Emitter.Types.cs:66`) emits
   `tss.Button`, `System.Collections.Generic.List$1(T)` — never a lexical binding. Splitting a
   bundle into files therefore requires **no import/export rewriting at all**; a module only has to
   be *evaluated* before the reference is read.
2. **Type bodies never touch the assembly wrapper.** `Transpose.assembly("tss", function ($asm, globals) {…})`
   binds `$asm` and `globals`, and across all 688 types in `tss.js` + `app.js` there is exactly one
   occurrence of each — the signature itself. A per-type file needs only to name its assembly
   (one line) so the bare `Transpose.define` registers into the right `$types` map.
3. **`Emitter.EmitClassPath()` already emits one bare `Transpose.define` per type**, at a
   `<ns>/<Type>.js` path. That is how `Transpose.BCL` is built. The emit half of "one file per class"
   exists; it is gated to the base library (`ProjectBuild.cs:250`) and produces plain scripts rather
   than modules.

So the per-class file layout is a small change. Everything hard is in *load ordering* and
*reflection*.

## 3. What must be loaded before what

`Transpose.define` resolves `inherits` **eagerly** — `Class.js:471` does `extend = extend()` inside
`define`, so the lazy-looking `inherits: function () { return [Base]; }` thunk runs immediately. Base
classes and interfaces must therefore be defined before the type that extends them.

That splits every reference into two kinds:

- **Hard** — the emitted code reaches *into* the type: `new X()`, a static member read, `inherits`,
  and (non-obviously) **a generic type argument**, because `Foo$1(X)` builds a generic instance whose
  base class can be `X` itself. A hard reference must be an `import`.
- **Soft** — a bare mention: `typeof(X)`, an `is`/cast operand, a metadata reference. These only need
  a *Type object*, which a stub provides (§5). They need not be imports.

ES modules give the ordering guarantee for free: a side-effect `import './Base.mjs';` at the top of a
module is fully evaluated before the importer's body runs. No name rewriting, no loader.

**But only if the import graph is acyclic.** With a cycle A↔B, whichever module is entered first runs
its body while the other is still initialising, and a `inherits` reference across the cycle throws.
The inheritance graph alone is acyclic (measured: every SCC over `inherits` edges is a single type),
but the *full* reference graph is not, and a purely inheritance-based import graph is unsound —
proven, see §4.

**The rule that works: a chunk is a strongly-connected component of the hard-reference graph.** The
condensation of an SCC graph is a DAG, so chunk-level side-effect imports are always safe, and inside
a chunk the emitter's existing dependency-depth ordering already satisfies `inherits`.

## 4. What was measured

Baseline: `Tesserae.Tests` built with `tps` at `8abfab4`, Debug, into
`bin/Debug/netstandard2.0/tps/`. 688 source types (526 in `tss.js`, 162 in `app.js`). Correctness
oracle: headless Chromium loads the page, records the sidebar, clicks all 140 samples and
fingerprints each rendered pane (element count + text length), plus every console error.

Four chunkings were built by re-chunking that emitted site and re-run through the same oracle:

| Experiment | Chunks | Initial load | Result |
| --- | --- | --- | --- |
| **A** one module per type, all imported eagerly | 688 | 3140 KB | **140/140 samples identical, 0 errors** |
| **B** one module per type, only the entry closure eager | 404 eager | 2058 KB | Renders, but only **3 of 140** samples — reflection cannot see unloaded types, and it fails *silently* |
| **C** as B + metadata-backed type stubs + sync fault-in | 404 eager | 2058 KB | Full 140-item sidebar restored; **27 errors** — static calls into stubs |
| **D** chunk = SCC of the full reference graph | 366 (211 eager) | 2053 KB | **140/140 samples identical, 0 errors** |

Experiment A proves the split itself is sound. D is the working design.

Experiment C is the interesting failure: with per-type modules whose imports are only the `inherits`
edges, a lazily-loaded sample calls `tss.Bind.Bind$2(...)` — a static method on a type that is still
a stub. There is no interception point for a plain property read on a global, so the chunk boundary
*must* be closed over hard references. That is what D does.

A fifth experiment (E) refined D by classifying references as hard/soft to break up the chunks. It
improves granularity on paper — 366 → 499 chunks, eager 2053 → 1979 KB — but the classifier here works
on *emitted text* and could not be made sound; it kept mis-filing edges (a generic type argument that
becomes a base class; a second `inherits` inside a `Transpose.definei` block). A real implementation
classifies on Roslyn symbols at the point `TypeRef` is called and would not have this problem. Left as
the main open question, because it is where the remaining upside is.

### Compression

Splitting costs bytes. Measured over the same content:

```
366 chunks, gzipped individually   598 KB
the same bytes as one file, gzip   519 KB     -> per-file split costs +15%
```

Small files lose the shared compression dictionary. This is real and must be stated in any pitch.

### Payload, gzipped, for the Tesserae sample app

```
tps.js        278 KB      the runtime                     not splittable by this work
tps.meta.js    57 KB      BCL reflection metadata         stays eager
tss.js        365 KB      Tesserae                        splittable
tss.meta.js   217 KB      Tesserae reflection metadata    stays eager
app.js        183 KB      the sample app (incl. 11% inline metadata)   splittable
              -------
             1102 KB total initial page

with the SCC split:  eager chunks 376 KB + boot 19 KB  (was 548 KB)
                     949 KB total initial page  ->  -14%
```

## 5. Reflection

This was the part flagged as "to be checked", and it resolved better than expected.

**Metadata can stay entirely eager and does not need the code.** `Transpose.setMetadata`
(`Reflection.js:4`) already defers an entry whose type is not yet defined, and `Transpose.init()`
re-defers it if the type is still missing. So the whole `$m(...)` block can ship up front and attach
to each type as its module arrives.

**Type stubs make unloaded types visible to reflection.** Registering, for each unloaded type, a
function carrying `$$name`, `$kind`, `$isInterface`, `$$inherits`, `$interfaces` and `$assembly` — at
its global path *and* in the assembly's `$types` map, which is exactly where `Transpose.define` would
put the real class (`Class.js:818`) — makes all of this work with the code absent:

- `Assembly.GetTypes()` (`getAssemblyTypes` enumerates `$types`)
- `IsAssignableFrom` (walks `$$inherits`)
- `IsInterface`, `Name`
- `GetCustomAttributes(...)`, including *constructing* the attribute instances

Tesserae's sample gallery is discovered entirely through those four calls
(`Tesserae.Tests/src/App.cs:116`), and it rebuilds its full 140-item sidebar off stubs alone.

Two things the stubs need care with, both found the hard way:

- Register outermost-first. Placing a nested type creates a plain `{}` at its container's path, which
  the container's own stub then overwrites, silently losing the nested type.
- Carry `$metadata` across when the real class replaces the stub, and evict the stub from the global
  path first, or `Transpose.define` reports *"Class X is already defined"*.

### 5a. The runtime half, implemented

`Resources/Modules.js` and the `Transpose.Modules` / `Activator.CreateInstanceAsync` bindings now
ship in the BCL. This is the part that does not depend on the emitter, so it landed first:

- `Modules.Register(manifest)` — declares the types a build deferred (`type name -> { m: module url,
  k: kind, a: assembly, i: [base type names] }`) and stubs each one, outermost-first, at its global
  path and in its assembly's `$types`. Resolves the `$$inherits`/`$interfaces` chains afterwards, so
  a stub may extend another stub, then calls `Transpose.init()` so metadata deferred while the type
  was missing attaches.
- `Modules.LoadAsync(type)` / `LoadAsync(name)` — fetches the module and completes with the **real**
  type. Concurrent calls share one fetch; awaiting a type that was never deferred is a no-op, so a
  call site can await unconditionally. A caller still holding the stub it got from `Type.GetType()`
  before the load is handed the live type rather than its own stale reference.
- `Modules.IsLoaded` / `IsStub`, and `Modules.SetLoader(url => Task)` — the default loader is a
  dynamic `import()`, built through `new Function` so `tps.js` stays parseable in an engine that
  cannot compile one; a host that serves chunks another way substitutes its own.
- `Activator.CreateInstanceAsync(type[, args | nonPublic])` and `CreateInstanceAsync<T>()` — load,
  then construct.
- `Transpose.createInstance` gains a stub guard, so the **synchronous** path fails with
  *"Cannot create an instance of 'X' synchronously: it lives in module 'Y', which has not been
  loaded"* instead of a `not a constructor` deeper in. The silent-degradation failure mode that
  experiment B hit is gone.
- A failed fetch restores the stubs and clears the memoised promise, so reflection still sees the
  type and a retry is allowed.

Covered by `LazyModuleActivatorTests` (7 tests): a stub is visible to `Type.GetType`, `Name`,
`IsInterface`, `IsAssignableFrom` and `Assembly.GetTypes()` before its module loads; the synchronous
path throws naming the module; `CreateInstanceAsync` loads once and returns a working instance;
`LoadAsync` is idempotent; and a failed load is recoverable. They run with `skipRoslyn: true` — there
is no native .NET counterpart to diff against — and substitute a loader that defines the type from
C#, so no test touches the network or a real ESM import.

What is still missing is the *producer*: nothing emits chunks or a manifest yet, so today a host has
to call `Modules.Register` itself. That is the emitter work in §7.

**The one thing that cannot work synchronously is using the type.** `Activator.CreateInstance(t)`,
`new X()`, a static call — these are synchronous C#, and `import()` is asynchronous. The prototype
bridged it with a synchronous `XMLHttpRequest` + `eval`, which is how it reaches 0 errors; that is
acceptable for a dev mode and not for production (it blocks the main thread and is deprecated).

So the production answer has to be an **explicit asynchronous boundary** — an attribute marking a
lazily-loadable type and an `await` at the point the app crosses into it. A `Task<T>`-returning
`Modules.Load<T>()` fits the existing `Task`→`Promise` mapping. Automatic per-class lazy loading
cannot be sound for synchronous C# semantics; this is a language-surface change, not just a codegen
one.

## 6. Why the win is smaller than it looks

Two structural facts, both visible in the chunking:

**The library's own shape sets the floor.** The largest chunk is **1538 KB raw, 192 types** — the
Tesserae component core. `tss.UI` is a static facade that references every component, and the
components reference `UI` back, so the entire component library is one strongly-connected component
and can never be split by any sound automatic rule. That chunk alone is 75% of the eager payload. The
fix is not in the compiler: it is breaking the `UI` facade cycle in Tesserae.

**Cross-references fuse unrelated code.** 122 of the 131 sample types landed in one 760 KB chunk,
because each sample's "See Also" list is `typeof(OtherSample)` — emitted as
`System.Array.init([Tesserae.Tests.Samples.ButtonSample, …])`. Those are *soft* references that a stub
satisfies, so the hard/soft classification of §3 is what unlocks this; without it the median cost of
opening one sample is 913 KB, i.e. nearly the whole lazy set.

And a third, which is about where the bytes actually are: **metadata is 25% of the payload**
(274 KB gz) and 37% of the raw bytes. `tss.meta.js` alone is 2.58 MB raw — the largest single file in
the site, larger than `tss.js`. It must stay eager for reflection to work over unloaded types, so
module splitting cannot reduce it. Emitting only the metadata a project actually reflects over, or
splitting metadata per type alongside the chunks and accepting an async `GetTypes()`, is a
*separate* and probably higher-value piece of work.

## 7a. What was built

`outputBy: "Module"` in `tps.json`. One `<script type="module">` in index.html; everything else is
imports.

```
tps/
  app.js            entry module: imports the eager chunks, the reflection metadata for every type,
                    the manifest of what was deferred, Transpose.init()
  chunks/c0.mjs …   one file per chunk: side-effect imports of the chunks it references, then the
                    Transpose.define of each of its types
```

**Translator** (`Emitter.Modules.cs`, ~300 lines):

- `TypeRef` — the single choke point every emitted type reference goes through — records the source
  types each type's body reaches into, so an edge exists exactly when a reference was emitted. A
  `typeof` operand is the one *soft* position: it wants a Type object, which a stub already answers
  for, so it is not recorded. That distinction is what stops a "see also" list of `typeof(...)` from
  fusing every type it names into one chunk. A *constructed* generic is the exception and stays hard
  even inside a `typeof` — it has no object to point at, it is built by applying its definition, and
  applying a stub throws (§7c).
- Iterative Tarjan over that graph; each SCC is a chunk. Components come out in reverse-topological
  order, so numbering them gives a DAG in which every import points at a lower index — which makes
  the import lists and the file names deterministic (`OutputIsDeterministic` guards it).
- Inside a chunk the emitter's existing dependency-depth ordering is kept, so `inherits` is satisfied
  without any extra work.
- Eager set = the entry point's chunk plus its transitive chunk dependencies. A project with no entry
  point (a library) keeps everything eager — there is nothing to be lazy relative to.

**Runtime** — beyond §5a, two things the prototype had not needed:

- `Transpose.$useAssembly(name)`, so a bare `define` outside the `Transpose.assembly(...)` wrapper
  registers into the right assembly.
- **Stub replacement moved into `Transpose.define` itself.** A chunk can be evaluated by two routes:
  the loader, or a plain static `import` from another chunk that ESM resolves directly. Only the
  first went through `Modules.load`, so a type arriving by the second hit *"Class X is already
  defined"* against its own stub. Doing the swap in `Class.set` covers both. For the same reason the
  metadata hand-off is keyed by type **name** (`Modules.$metaFor`, populated from
  `Reflection.setMetadata`) rather than held on the stub object — the stub may be taken out by a
  different route than the one replacing it. Both were found by the Tesserae run, not by reasoning.

**Entry-module ordering is load-bearing.** Metadata is emitted *before* the manifest, because
`Modules.register` ends with a `Transpose.init()` and `init` runs the entry point — anything emitted
after it would not exist yet when `Main` runs. That is what keeps `[SampleDetails]` readable off the
stubs; with the order reversed, Tesserae's whole sidebar collapsed into one "Others" group.

### Measured on the Tesserae sample gallery

Same sources built both ways, both rendered in headless Chromium, all 140 samples clicked:

| | initial JS payload (the app's own) |
| --- | --- |
| single bundle | 1,109 KB raw / 183 KB gz |
| `outputBy: Module` | **164 KB raw / 19 KB gz** |

160 chunks: 5 loaded up front, 155 on demand (157 of 162 types deferred). **All 140 samples render
with identical element and text counts, and the sidebar is identical, with zero console errors** —
including the reflection-driven discovery, which reads names, interfaces and attributes off stubs.

That ratio is the best case: a gallery of independent samples is exactly the shape module splitting
suits. It also required a two-line change in the app, because instantiating a deferred type is
asynchronous:

```csharp
// Sample.cs   Func<IComponent>  ->  Func<Task<IComponent>>
() => Activator.CreateInstance(t) as IComponent
async () => await Activator.CreateInstanceAsync(t) as IComponent   // and DeferSync -> Defer
```

### Packages split too (§7b)

`--emit-package` emits chunks as well, so a *library* splits. Two pieces make that work across the
assembly boundary:

- **A library defers everything.** It has no entry point to be lazy relative to, so its eager set is
  just the chunks holding its `[Ready]` handlers (usually none). Its entry module imports nothing and
  registers the whole manifest as stubs.
- **It publishes a chunk map** — emitted type name → chunk file — embedded as `Transpose.Modules.json`
  (embedded but *not* listed in `Transpose.Resources.json`, like the `BuildStamp`, so no consumer
  extracts it into a site). A consuming build reads the maps of all its references (`ModuleMap.Read`)
  and, wherever its own code reaches into a library type, emits `import '../<lib>/cN.mjs'` from the
  chunk that uses it. Without that the reference would land on the library's stub, and a stub cannot
  be resolved synchronously.

`RecordRef` therefore records two sets: source types (chunked here) and referenced-assembly types
(chunked over there). BCL types are recorded in neither — they live in `tps.js`, which always loads.

The one non-obvious bug this surfaced: **a stub must be retired in place, not deleted.** `Class.set`
already copies the members of whatever previously occupied a type's global slot onto the new class,
which is how a nested type registered onto a stub survives — and it has to survive *before* the
define resolves `inherits`, because a type's own base can mention its nested type
(`Nav : ...<Nav.NavLink>`). Deleting the stub and restoring later left `Nav.NavLink` undefined at
exactly that moment. So `$replaceStub` now clears the stub markers and leaves the object in place,
keeping `$$name` (a caller holding the stub still has to resolve by name afterwards) and setting
`$retiredStub` for the redefinition check to look past.

### Measured with both Tesserae and the app split

| | initial JS payload |
| --- | --- |
| single bundle (tss.js + tss.meta.js + app.js) | 5,968 KB raw / 762 KB gz |
| both as modules | **4,235 KB raw / 527 KB gz** |

628 chunks (468 library, 160 app); 210 chunks load up front, 418 on demand. 137 of 140 samples
fingerprint identically; the other 3 (`Searchable List`, `Searchable Grouped List`, `Avatar`) differ
run-to-run in the *single-bundle* build too — virtualized list windows and randomised avatars — so
they are the measurement's noise floor rather than a regression. Zero console errors.

The eager remainder is dominated by **reflection metadata**: 2,708 KB raw of the 4,235 is the two
entry modules, almost all of it the `$m` blocks that used to be `tss.meta.js`. It has to stay eager
for reflection to see deferred types at all, which is the same conclusion §1 reached from the other
direction — metadata, not code, is what the split cannot touch.

### Re-measured with the library's reflection turned off

Tesserae subsequently set `reflection.disabled: true` in its own `tps.json` (the library does not
need to be reflectable; the app still is), which deletes the 2.5 MB `tss.meta.js` the paragraph above
identified as the floor. Same 628 chunks, same code, re-measured:

| | tss + app JavaScript | whole page |
| --- | --- | --- |
| single bundle | 3,473 KB raw / 534 KB gz | 6,938 KB raw / 1,105 KB gz |
| both as modules | **1,750 KB raw / 323 KB gz** | **5,215 KB raw / 893 KB gz** |

212 chunks eager (207 library, 5 app), 416 on demand; the entry modules are 60 KB (`tss.js`) and
149 KB (`app.js`). So the split's own contribution went from 29% raw / 31% gz to **50% raw / 40% gz**
— not because the splitter improved, but because with the metadata gone what remains eager is code,
and code is what it can move. The two changes compose: eager metadata was the larger of the two
costs, and neither removes the other's.

139 of 140 samples fingerprint identically; the one that differs (`Avatar`) is the randomised sample
from the noise floor above. Reflection still enumerates all 528 + 162 types. Zero console errors.

### Not done

- **`--incremental`.** Chunk assignment is a whole-program property, so a body-only edit that today
  reuses cached per-type JavaScript could still reshuffle chunks. Module mode has not been checked
  against the cache and the two should not be combined yet.
- **Minification** of chunk files, and the `.js`/`.min.js` variant switch across N files.
- **Watch mode** with module output.
- **Minification** — a module entry and its chunks are emitted formatted only (they carry `import`
  syntax `JsMinifier` is not set up for), and a module-mode package embeds no `.min.js` sibling.
- An **open** generic base — `class Relay<T> : IHandler<T>` — reaches the manifest as the bare
  definition (`IHandler$1`), because there is no `T` to write down until the definition is applied.
  The definition-level relationship is reported; a question about one instantiation still needs the
  module. Same for an argument that is an array, or a base nested inside a generic. See §7c for the
  constructed case, which is answered.

### 7c. Constructed generics from a stub

The first cut flattened every generic base to its definition name (`Foo$1`) because the manifest was
resolved with `Transpose.unroll`, a dotted-path walk over globals — and a constructed generic
deliberately has no global path. `Transpose.define` places only the *definition*; the instantiation
lives in `fn.$cache`, keyed by the argument objects, reachable only by **applying** the definition.
So a stub reported `IHandler$1` where the caller asked about `IHandler<Order>`, and
`varianceAssignable` — which matches on `$genericTypeDefinition` + `$typeArguments`, neither of which
a definition object carries — answered false. Silently: indistinguishable from a real false.

It is **not** interface-specific. `ManifestBaseNames` ran the base class through the same flattening,
and in Tesserae the most common generic base is a class: 133 of the library's 528 deferred types name
one, 63 of them `ComponentBase<Self, THTML>`.

Two changes fix it.

- **The manifest carries the arguments.** A base is now either a name (as before) or
  `[definition, ...arguments]`, nesting for `IFoo<IBar<int>>`:
  `i: [["tss.CB$2", "tss.Avatar", "HTMLElement"], "tss.IC", …]`. 228 of Tesserae's specs take the
  array form.
- **The runtime applies it lazily.** `Modules.$resolveType` builds the instantiation on the *first
  read* of a stub's `$$inherits`/`$interfaces`/`$allInterfaces`, not at `register` time — because a
  base may itself be a stub, and applying a stub throws. A partial resolution is never cached, so a
  base whose module arrives later is picked up on the next question.

Lazy is not just cheaper, it is what makes the eager alternative unnecessary. Forcing every generic
definition into the eager set (so the manifest could emit a real application) looked attractive —
only 20 distinct definitions in the library — but it is not needed: **to ask about `IFoo<Bar>` at all
you must already have the definition**, since `typeof(IFoo<Bar>)` emits the same application. At query
time it is loaded by construction.

That invariant only holds because of the second half of this change: `typeof` was treated as a soft
reference, and a soft reference is satisfiable by a stub — but a constructed generic has no object to
point at, it is *built* by applying the definition, and a stub throws when applied. So
`typeof(IFoo<Bar>)` on a definition in an unimported chunk was a latent runtime throw, not a wrong
answer. `RecordConstructedTypeRefs` now records the definition and its arguments as hard references
from inside a `typeof`; a plain `typeof(X)` and an unbound `typeof(Foo<>)` (which emits the definition
object, no application) stay soft, so a "see also" list still does not fuse chunks.

`$allInterfaces` was a third, smaller hole found on the way: `getInterfaces` reads it and answers `[]`
without it, so `Type.GetInterfaces()` on any deferred type — generic or not — reported nothing at all.

Measured on the live gallery: for a still-deferred `tss.Avatar`,
`IsAssignableFrom(ComponentBase<Avatar, HTMLElement>, …)` is true, `GetInterfaces()` returns 4 rather
than 0, and the type is still a stub afterwards — the answer came from the manifest, not from a load.
The eager payload grows 8 KB raw / 2 KB gzipped (a bigger `tps.js` plus the longer specs), and the
chunk assignment is unchanged at 628/212.

## 7. What the compiler change would be

Ordered by how much of it already exists.

**Already there**
- The whole runtime/BCL half: `Resources/Modules.js`, `Transpose.Modules`,
  `Activator.CreateInstanceAsync`, the stub guard in `Transpose.createInstance` (§5a).
- Per-class emission: `Emitter.EmitClassPath()`, currently gated to the base library.
- Per-file packaging: `EmbeddedItem` (`ResourceEmbedder.cs:9`) already carries an `Output`
  subdirectory and a `Load` flag for "write it, but do not put a `<script>` in index.html" — exactly
  what N chunk files plus one boot module need.
- Deferred metadata attachment in the runtime (`Reflection.js`).

**New, in the translator**
- A **reference-kind classifier**. All 82 `TypeRef(...)` call sites funnel through the single
  `TypeRef(ITypeSymbol)` in `Emitter.Types.cs:66`, so *recording* an edge is a one-place change; but
  classifying it hard vs soft needs the calling context, so either those sites pass a kind, or a
  separate analysis pass walks the symbols. A separate pass is preferable — it keeps the emitter's
  output byte-identical, which is the standing correctness gate, and `TreeModel` already caches the
  semantic models it would need.
- **SCC + condensation** over the hard-reference graph, then a chunk-merge heuristic (a 200-byte
  chunk per type is worse than useless — see the +15% compression cost).
- A **chunk manifest**: type → chunk, chunk → chunk deps, plus each type's kind and inherits list so
  the boot module can build stubs.

**New, in the compiler core**
- An `outputBy: Module` / `--modules` mode in `TransposeJson` + `ProjectBuild`, writing chunk files
  and a boot module instead of one bundle.
- `OutputBuilder`: script-tag only the boot module; write chunks as unlinked files.
- Package builds: a library must embed its chunks *and* its manifest, and a consuming site build must
  merge the manifests of every referenced package. This is the fiddliest part — it is a new
  cross-assembly protocol, versioned by the `BuildStamp` minimum-compiler check.

**New, in the runtime (`Transpose.BCL/Resources`)**
- `Transpose.$useAssembly(name)` — name the ambient assembly for a bare `define` outside a wrapper.
  The only runtime piece still outstanding; everything else in this section is done (§5a).

**Interactions to check that this investigation did not**
- `--incremental`: the chunk assignment is a *whole-program* property, so a body-only edit that today
  reuses cached per-type JS could still reshuffle chunks. `TODO.incremental.md`'s guarantee of
  byte-identical output needs re-examining.
- `--watch`: a rebuild that moves a type between chunks must invalidate both.
- Minification: chunks minify independently, so `JsMinifier`'s local-variable renaming stays safe, but
  the `.js`/`.min.js` variant switch now applies to N files.

## 8. Recommendation

Worth doing, but not as "modules per class" — that granularity is what the measurements argue
against, on both compression (+15%) and the fact that the largest SCC is 192 types regardless.

Sequence it as:

1. **Hard/soft reference classification on Roslyn symbols**, with SCC chunking and a merge
   heuristic. This is the whole technical core, and it is measurable on its own.
2. **An explicit `[LazyModule]`-style boundary** plus `await Modules.Load<T>()`. Without it, lazy
   loading is only reachable through reflection, and only asynchronously.
3. ~~**Stubs + eager metadata** in the runtime, so reflection keeps working across the boundary.~~
   **Done** — see §5a.
4. Only then, packaging across assembly boundaries.

And separately, ahead of all of it, look at the **metadata**: it is a quarter of the gzipped payload,
every project pays for it whether it reflects or not, and reducing it needs none of the machinery
above.

---

### Reproducing

The harness is in `docs/experiments/js-modules/` — see its README. It works on an emitted site, so it
needs no compiler changes:

```bash
cd /path/to/tesserae && dotnet build Tesserae.sln -c Debug        # with tps on PATH
node graph.js  <site>                    # reachability + SCC statistics
node split3.js <site> out-scc            # the working SCC chunking (experiment D)
node probe.js  out-scc report            # render, click all 140 samples, fingerprint
```
