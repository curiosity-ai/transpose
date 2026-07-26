# TODO.incremental.md — incremental compilation in `tps`

What it takes to make `tps` reuse a previous build, what the prototype behind `--incremental` actually
does, what it measured on the tesserae corpus, and what is left.

Companion to [`TODO.optimization.md`](TODO.optimization.md), which is about making a *single* build
cheaper. This is about not doing one at all. The two are complementary and the second is now the
bigger lever: a clean build of tesserae is ~7 s and about half of that is work a previous build
already did.

> `CLAUDE.md` used to read "Caching and the compilation server are intentionally **out of scope**".
> The caching half of that is what this document revisits; the compilation-server half still stands,
> and the measurements below say exactly what it would be worth.

---

## Where things stand

`tps --incremental` (off by default; `--cache-dir <dir>` / `TRANSPOSE_CACHE_DIR` to relocate the
cache, `TRANSPOSE_INCREMENTAL=1` to enable it for a session). Measured on the tesserae corpus,
`-c Debug`, medians of 3 interleaved runs, 4-core container:

| scenario | today | `--incremental` | speedup |
|---|--:|--:|--:|
| library (263 files → package DLL), nothing changed | 7.98 s | **0.40 s** | **20×** |
| library, one method body edited | 7.98 s | **4.09 s** | **1.95×** |
| library, clean build (cache written) | 7.98 s | 8.06 s | −1% |
| app (site build, library up to date), nothing changed | 4.27 s | **0.41 s** | **10×** |
| app, one method body edited in the app | 4.27 s | **3.46 s** | 1.23× |
| app, one method body edited in the **library** | 8.82 s | **4.44 s** | **2.0×** |

Every one of those outputs is **byte-identical** to a from-scratch build of the same sources,
verified with `compare-site.sh` over the whole `Tesserae.Tests` site across seven edit shapes
(app body, library body, both, a new public method, a statement added to a body, a changed field
initializer, whitespace in a body). The 584-test suite stays green, plus 8 new tests in
`IncrementalEmitTests.cs`.

The ~1% cold-build cost is the price of admission: hashing every file's declaration surface (~150 ms
on 270 files) and writing ~5 MB of cache. Measured by an interleaved A/B of four clean-build pairs
(7.19 s vs 7.26 s) — i.e. inside the noise.

---

## The design

Three verdicts, decided by `Transpose.Compiler/BuildCache.cs` before the translator is even
constructed:

| verdict | when | what happens |
|---|---|---|
| **UpToDate** | every input hashes the same **and** every output file is still on disk with the same length and write time | nothing is compiled |
| **BodyOnlyChange** | files changed, but every change is inside a method/accessor body | unchanged types keep their cached JavaScript; only changed files are scanned and diagnosed; the reflection metadata, the scanner's denied-name filter and (in a metadata-only configuration) the .NET assembly are reused |
| **FullBuild** | anything else — a declaration moved, a file appeared or vanished, a reference or setting changed, no cache | compile everything, write a fresh cache |

The reuse *mechanism* lives in `Transpose.Translator/Compilation/IncrementalPlan.cs`: the translator
does no file I/O and holds no policy, it just consults a plan. The reuse *policy* — and the soundness
argument — lives entirely in `BuildCache`.

### Why "body-only" is the right dividing line

It is the coarsest condition under which each phase's output provably cannot move:

* **Per-type JavaScript.** A type's emitted JS is a function of its own syntax plus the *declarations*
  it binds against: member names, overload numbering (`$ctorN`, `foo$1`), `[Template]`/`[Name]`
  attributes, constants, base types. None of that can shift because a body elsewhere changed. The
  emitter already emits every type into its own `JsWriter` in parallel and concatenates them in
  dependency order, so a per-type cache slots straight in — and crucially `NameMangler` derives every
  name from the symbol alone, with **no global counter** whose value depends on how many types were
  emitted. (That was the property to check first; had names been allocated by emit order, per-type
  caching would have been impossible without renaming everything.)
* **Diagnostics.** A body can only produce a diagnostic in its own file. So an unchanged file's Roslyn
  body diagnostics and its unsupported-feature scan verdict both still stand — and the cached build
  succeeded, or nothing was cached.
* **Reflection metadata.** It describes types, members, signatures and attributes: declarations.
* **The .NET assembly**, in Debug. A metadata-only emit has no method bodies at all, so its content is
  a function of the declarations. This is the single biggest win available (~19% of a build) and the
  claim most worth distrusting, so it was tested rather than argued: a body edit adding a closure, a
  local function, an 8-case string switch and an iterator local function to one method produces a
  fresh metadata-only assembly that differs from the reused one in exactly **16 bytes** — the module
  MVID and the PE timestamp — with identical length and identical metadata. (Those 16 bytes already
  differ between any two full builds; Roslyn's emit here is not deterministic, and neither is
  Mono.Cecil's resource embed, which restamps 17–18 bytes of the final DLL every time.) In Release the
  assembly is full IL and is *not* reused.

`IncrementalPlan.DeclarationHash` establishes the condition: a SHA-256 of each file's text with every
method/accessor/operator/constructor body and expression-body cut out and replaced by a marker. Field
initializers, default parameter values, attribute arguments and base lists are deliberately **kept** —
a constant folds into other files' output and an attribute argument steers emission. Being a hash of
text rather than of a normalised syntax model, it over-reports (reformatting a declaration, or
swapping `=> expr;` for `{ return expr; }`, forces a full rebuild) and never under-reports. That
asymmetry is the whole point and `TheDeclarationHashMayOverReportButNeverUnderReports` pins it down.
Only the files whose *text* hash changed are re-parsed to answer it, so the check costs nothing on a
large project.

### Two keys, not one

A referenced Transpose package contributes two different things: its **metadata**, which the consumer
binds against, and its **embedded JavaScript**, which the consumer copies into the site untouched. So
the cache carries two keys:

* `SettingsKey` — configuration, defines, language version, output mode, `tps.json` (+ the
  per-configuration overlay), reflection settings, and every reference by its *metadata* fingerprint.
  A change here means a full rebuild.
* `ContentKey` — additionally every reference by its raw bytes. A change here alone means the
  compilation is still valid but the outputs are stale.

That second tier is what makes the multi-project inner loop work. A dependency rebuilt after a
body-only edit of its own has *identical metadata* but a different DLL — Mono.Cecil stamps a fresh
MVID on every embed, so the bytes never compare equal. Without the split, editing a method body in
Tesserae would force a full rebuild of every project referencing it. So a package build now writes a
`<Assembly>.dll.tpsmeta` sidecar next to its DLL holding the hash of the assembly Roslyn emitted
(before the resources went in), and consumers fingerprint that instead of the DLL. The consumer then
lands on "0 files changed, bodies only", and since nothing it emits can differ, it skips the
compilation **entirely** (`TryReplayCompilation`) and only rewrites its outputs.

### Where the cache lives

`<project>/obj/tps-cache/<Configuration>/` — so `dotnet clean`, `rm -rf obj` and `tps-bench`'s
clean-slate scenarios all drop it, and Debug/Release (structurally different builds) never share.
`--cache-dir` puts it anywhere, keying projects apart by a slug; a temp folder is fine, since losing
the cache only ever costs a full build. Contents: `manifest.json` (keys, per-file text +
declaration hashes, output file list), `types.js` (per-type JavaScript, one framed blob rather than
500 files), `bundle.js`, `meta.js`, `assembly.bin`, `denied-names.txt`. ~5 MB for tesserae.

A cache is also keyed on the compiler's own identity — assembly version, informational version, and
the write times of `tps.dll` and `Transpose.Translator.dll`, so rebuilding the compiler in place
invalidates it rather than silently mixing emitters.

---

## What a body-only build still spends, and why

`tesserae` library project, one method body edited (3.9 s):

| phase | ms | what it is |
|---|--:|---|
| scan unsupported features | 1 433 | **one file** is scanned. This is not scanning — it is Roslyn building the compilation's symbol tables and importing the reference metadata, which lands on whichever phase binds first |
| emit JavaScript | 630 | 431 ms of it emitting the one changed type and checking reuse for the other 499 |
| embed resources into DLL (Cecil) | 385 | the JavaScript changed, so the DLL must be rewritten |
| body diagnostics | 270 | the changed file only |
| build compilation (parse) | 236 | all 270 files, in parallel |
| hash declaration surface | 144 | changed files only; the rest carried forward from the manifest |
| resolve project + minify | 135 | |

**The floor is the reference metadata import.** A one-file project referencing the same packages takes
1.74 s end to end, and `Transpose.dll` is 10.6 MB imported with `MetadataImportOptions.All` (required
so overload numbering matches). That cost is paid per process and no on-disk cache can avoid it. It is
also the precise size of the prize a **compilation server** would collect: keeping the
`MetadataReference` alive across builds would take a body-only edit from ~3.9 s to well under a
second, and the no-op case is already there without one.

---

## What is left

Roughly in expected-value order.

- **Decide the default.** It is opt-in, because a stale cache is a *silently wrong* build rather than a
  failed one. Before flipping it on: put the `compare-site.sh` gate for the seven edit shapes into CI
  (the script in `.claude/skills/transpose-performance/scripts/` plus
  `/tmp/.../verify-incremental.sh`, which should move into the repo), and have the
  `Transpose.Build.Target` SDK pass `--incremental` so a normal `dotnet build` benefits.
- **Declaration-level dependency tracking**, so a changed *declaration* re-emits only the types that
  bind to it instead of everything. This is the big remaining tier: adding a method is at least as
  common an edit as changing a body, and today it costs a full build. It needs the emitter to record,
  per type, which other types' member sets it consulted — feasible (every symbol query already goes
  through `TreeModel`) but invasive, and the reverse-dependency closure has to include overload
  numbering, which makes *any* member added to a type invalidate every caller of that type. Worth
  prototyping against real edit traces before committing to it.
- **Reuse across `--emit-package` and site modes.** The cache is keyed on output mode, so a project
  built both ways caches twice. Harmless but wasteful.
- **The runtime build (`--build-runtime`) has no cache.** It is a maintainer-only operation, so the
  risk/benefit is worse; but it binds the BCL three times over and would benefit most.
- **`ProjectResolver.IsPackageUpToDate` and the cache now answer overlapping questions** by different
  means (timestamps vs. content hashes). The timestamp check runs first and can send a project into a
  build the cache then declares up to date — harmless (it becomes a "0 files changed" replay) but it
  should be one mechanism, not two.
- **Cache eviction.** Nothing prunes `obj/tps-cache`; it is one generation per configuration, so it
  cannot grow, but a `--cache-dir` shared across many projects accumulates directories forever.
- **Make the emitted assembly deterministic** (`CSharpCompilationOptions.WithDeterministic(true)`) and
  stop Cecil restamping the MVID. Then a body-only edit would produce a byte-identical DLL, the
  content/metadata key split would be unnecessary, and `diff -r` would become a usable gate on the DLL
  as well as the site.
- **`--incremental --timing` reports a phase that no longer does what its name says.** "scan
  unsupported features" is now mostly metadata import; splitting that out would make the floor visible
  in the timing table instead of only in this document.
