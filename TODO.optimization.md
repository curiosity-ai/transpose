# TODO.optimization.md — Transpose compiler performance

Running log of compiler performance work: what was measured, what was tried, what worked, and — just
as important — **what did not**, so a future session does not re-walk a dead end.

The workflow, the measurement traps, and the correctness gate live in the
**`transpose-performance`** skill (`.claude/skills/transpose-performance/`). Read that for *how*;
read this for *what has already been done*.

---

## Where things stand

Clean-slate builds of the tesserae corpus, `-c Debug`, medians of 3, CPU-score-normalised
(`tps-bench`, 4-core Xeon @2.80 GHz container, score ≈ 92):

| scenario | before | after | delta | alloc delta |
|---|--:|--:|--:|--:|
| `tesserae` — 263 files / ~69k LOC → package DLL | 15.82 s | **5.71 s** | **−63.9%** | −44.4% |
| `tesserae+tests` — multi-project: dependency + site build | 23.16 s | **6.80 s** | **−70.6%** | −48.9% |
| `tesserae+tests-warm` — dependency already up to date | 9.03 s | **3.21 s** | **−64.4%** | −55.0% |

"After" is the compiler as it now ships: a ReadyToRun publish, `-c Debug` (which emits a metadata-only
assembly). The equivalent Release figures — full IL, and both minified and formatted bundles — are
8.34 s / 9.35 s / 3.73 s; Release is the slower configuration by design.

Baseline commit `79fcfc1`. The recorded reports are checked in so a later change can be compared
without rebuilding an old compiler:

| file | what it is |
|---|---|
| `docs/perf/baseline.json` | before any of this work (JIT compiler, full-IL Debug) |
| `docs/perf/optimized.json` | current, ReadyToRun publish, Debug — the default comparison target |
| `docs/perf/optimized-release.json` | current, ReadyToRun publish, Release |

```bash
tps-bench --tps <tps> --tesserae benchmarks/tesserae --iterations 3 --baseline docs/perf/optimized.json
```

The corpus is a submodule (`benchmarks/tesserae`), so `git submodule update --init` before benchmarking.

Emitted output is **byte-identical** to the pre-optimization compiler across all of it (verified with
`compare-site.sh` over the whole `Tesserae.Tests` site), and the 499-test suite stays green.

### Phase split now, for `tesserae` (Debug, ReadyToRun compiler)

| phase | ms | share | alloc |
|---|--:|--:|--:|
| resolve project | 37 | 0.6% | 15 MB |
| build compilation (parse + references) | 287 | 4.9% | 29 MB |
| scan unsupported features | 1 264 | 21.6% | 57 MB |
| bind + emit .NET assembly (metadata only) | 1 139 | 19.5% | 57 MB |
| emit JavaScript | 1 898 | 32.5% | 279 MB |
| body diagnostics (semantic models) | 899 | 15.4% | 75 MB |
| collect package resources (minify) | 60 | 1.0% | 40 MB |
| embed resources into DLL (Mono.Cecil) | 257 | 4.4% | 52 MB |

Two structural facts that bounded everything, and where they now stand:

1. **A build used to bind the whole project about twice** — once inside `Compilation.Emit` (to produce
   IL) and once through the `SemanticModel` (to drive the JS emitter). Roslyn's internal bound trees
   are not reachable from the public API, so the two cannot be merged; the only way to remove one is to
   stop producing IL, which is what the metadata-only Debug emit does (§12). What is left is one bind,
   shared between the scan, the JS emit, and the body-diagnostics pass.
2. **Every `tps` invocation had a ~2.2 s floor** — a *one-file* project took 2.2 s while allocating only
   5 MB, essentially all JIT of Roslyn — and the SDK runs `tps` once per project. ReadyToRun packaging
   (§13) halves that floor.

In Release (full IL, both bundle variants) the assembly emit is back to ~3.3 s and minification adds its
own cost, which is why Release measures 8.34 s against Debug's 5.71 s.

---

## What worked

Ordered by size. Every one was verified against the correctness gate in the skill.

### 1. Runtime configuration: `TieredPGO=off` — ~25–30%

`Transpose.Compiler.csproj` → `<TieredPGO>false</TieredPGO>`.

Tiered PGO instruments tier-0 code to collect a profile. Roslyn's binder is exactly the deeply
polymorphic code instrumentation taxes hardest, and a compile is over in seconds — long before the
better tier-1 code the profile would buy can pay that tax back. Measured interleaved, Server GC on for
both: **14.1–14.6 s → 10.3 s** on `tesserae`, and **neutral** on a one-file project (2.13 s vs 2.11 s),
so small projects do not regress.

Everything measured in this area:

| setting | `tesserae` | one-file project | verdict |
|---|--:|--:|---|
| default | 14.1–14.6 s | 2.11 s | — |
| `TieredPGO=0` | **10.3 s** | 2.13 s | **adopted** |
| `TC_CallCountingDelayMs=0` | 11.9–12.1 s | 2.17 s | **adopted** (stacks with the above → 9.2 s) |
| `TC_QuickJitForLoops=0` | 13.1–13.6 s | — | rejected — not worth it alone |
| `TieredCompilation=0` (no tiering) | 9.7–10.0 s | **4.85 s (2.3× worse)** | **rejected** — cripples small projects |

`CallCountingDelayMs` has no MSBuild property, so it is set via
`Transpose.Compiler/runtimeconfig.template.json`.

### 2. Runtime configuration: Server GC — ~15%

`<ServerGarbageCollection>true</ServerGarbageCollection>`. A build makes ~700–900 MB of short-lived
garbage inside several `Parallel.ForEach` phases; on Workstation GC every collection serialises on one
heap and stalls all workers. Interleaved: **15.1–17.3 s → 12.2–12.9 s**. Peak working set rises
~390 MB → ~490 MB; .NET's DATAS keeps the heap count adaptive, so a trivial project does not pay a
many-heap memory penalty.

Rejected variant: `gcConcurrent=0` alongside Server GC — measurably *worse* (13.8 s vs 11.7 s).

### 3. `TransposeNaming`: cache the naming layer — JS emit 2.3× faster

An allocation profile put a quarter of the build's entire allocation inside `TransposeNaming`.
Resolving a member's JavaScript name is an expensive graph walk and it was redone once per *reference*
in the source. Now cached per symbol:

- `MemberJsName` — for a property/field it walks `AllInterfaces` and every interface's members to decide
  whether the member must yield its plain JS slot.
- `JsBaseName` — `OverloadGroup` calls it for every candidate method in every base type, to group
  overloads by their final JS name.
- `ImplementedInterfaceMember` — O(interfaces × their members) with a
  `FindImplementationForInterfaceMember` call each, walked once per level of the override chain.

Caches are keyed on the symbol **as passed**, never on `OriginalDefinition`: a constructed generic's
member is a distinct symbol, and caching it separately cannot change an answer while normalising could.

Result: `emit JavaScript` **3 706 ms / 430 MB → 1 775 ms / 275 MB**.

### 4. `UnsupportedFeatureScanner`: screen identifiers by text before binding — 2 661 ms → 684 ms

`VisitIdentifierName` fires on nearly every identifier in the source and used to call `GetSymbolInfo`
on each, which binds the whole enclosing member. It now first checks the identifier's *text* against a
set precomputed once per compilation: the simple names of every type in a denied namespace
(`System.IO`, `System.Net.Sockets`, `System.Threading`) that the scanner would actually reject, plus
`nint`/`nuint`/`var`.

**Sound, not heuristic** — an identifier can only bind to a denied type if its text is that type's
name. The three escapes are handled explicitly:

- `var` is in the set, so an inferred local whose type is never written out is still caught.
- `using MyFile = System.IO.File;` — the alias name joins the file's name set when the directive is
  visited, so the *usage site* is still reported. (Reporting the directive itself would have rejected
  an *unused* import, which the scanner never did.)
- `using static System.IO.File;` — same, with the imported type's member names.

Also prefiltered: `CheckDllImport` (screen the written attribute name before binding) and
`VisitIsPatternExpression` (only a *string-literal* constant pattern can be the span-pattern form).

Allocation 150 MB → 27 MB. Diagnostics were diffed against the old compiler on a file exercising every
rule: the same set of rejected constructs, minus five *duplicate* reports at the same site (the old
code reported both `File` and `ReadAllText` in `File.ReadAllText(...)`, and separately flagged the
`var` on the same line).

### 5. Allocation-free attribute and location lookups — −42 MB

`GetAttributes().FirstOrDefault(a => AttrIs(a, name))` allocates a closure (it captures `name`) and
boxes the `ImmutableArray` enumerator on *every* call — it was the single largest allocation site in
the build. Now `TransposeNaming.FindAttr`/`HasAttr`, plain loops. Same treatment for
`Locations.Any(l => l.IsInSource)` (`AnyInSource`) and the `GetMembers().OfType<IMethodSymbol>()` walks
in the overload collection.

### 6. One shared semantic-model cache for the scan *and* the emit

A `SemanticModel` retains the bound form of every member it is asked about. Previously the scanner
built throw-away models and `Emitter.Clone()` built a **fresh `TreeModel` per type** (483 of them). Now
a single `TreeModel` (a `ConcurrentDictionary`, created in `RoslynTranslator`) is shared by the scan and
every emitter clone.

**Proof that it works**: temporarily dropping `var` from the scanner's name set moved ~780 ms *out* of
the scan and ~465 ms *into* the JS emit, leaving the total flat (8.43–8.53 s vs 8.28–8.83 s). The
scan's binds are prepaid emitter work, not extra work — which is why the scan looks expensive in the
phase table and yet costs nothing.

Sharing between emitter *clones* specifically was smaller than hoped, and the reason is worth knowing:
Roslyn caches at *member* granularity, so two types in the same file never shared bound bodies anyway —
only the (cheap) tree-level model object was duplicated.

### 7. Take diagnostics from `Compilation.Emit` instead of a separate `GetDiagnostics()` — ~0.7 s, −75 MB

`Emit`'s result already carries every declaration and method-body diagnostic. When an assembly is being
emitted (every real project build) the standalone `GetDiagnostics()` pass is skipped.

**Much less of a win than the phase table implies**, and the reason matters: the two were not doing the
same work twice, they were *sharing* it. `Emit` runs on `compilation.WithOptions(…Library)`, and the
preceding `GetDiagnostics` on the original compilation warmed the reference manager and the JIT for the
binder. Removing it made `Emit` alone go 4.25 s → ~6.5 s, so the net was ~0.7 s, not 4.3 s.

Source-only builds (the test suite, `--out`) keep the `GetDiagnostics` path. A *failed* emit falls back
to a full `GetDiagnostics`, because `Emit` stops before compiling method bodies when declarations
already have errors and would otherwise report a subset. That fallback now dedupes on `(Id, Location)`
— `Diagnostic` has reference equality, so errors found by both passes were printed twice.

### 8. Parallel parsing (−0.6 s) and parallel type collection (−0.25 s)

`CompilationBuilder.Build` parsed 263 files sequentially (**1 011 ms → ~410 ms**); `Emitter.CollectTypes`
walked every tree sequentially calling `GetDeclaredSymbol` (**345 ms → ~77 ms**). Both fan out per file
into a pre-sized array and merge in input order, so tree order — and therefore bundle order — is
unchanged. The partial-type dedupe happens after the merge.

### 9. Honour the project's `DebugType`

The assembly emit hardcoded an embedded PDB even for a project that declares `DebugType=None`
(Tesserae does). Now read from the csproj (`DebugType`/`DebugSymbols`), defaulting to on so a project
that says nothing keeps what it always got. ~5% off the emit phase and ~12% off the DLL size for
projects that opt out.

### 10. Write the package DLL once, not twice

`WritePackage` wrote the emitted assembly to disk and then had Mono.Cecil read it straight back to add
the resources and rewrite it. Cecil now reads the assembly from memory and writes the final file
directly. A package DLL with the JS, CSS and fonts embedded is tens of megabytes (Tesserae's is
~15 MB), so this halves the file I/O — though it measured **flat on this box**, where everything was in
the page cache. Kept because it is simpler and strictly less work; expect it to matter on real disks
and in CI.

### 11. Reproducible output (a correctness fix found while verifying)

`EmitEnumeratorInit` named its enumerator temporary from `forEach.GetHashCode()` — a *reference* hash,
so an unchanged project emitted a byte-different bundle on every build. Now derived from
`forEach.SpanStart`, which is stable and unique within a file (all the uniqueness the name needs, since
it is local to one JS function). Two consecutive clean builds now produce an identical `app.js`, which
is also what makes the `diff -r` correctness gate usable.

---

### 12. Metadata-only assembly in Debug — ~18%

`Compilation.Emit` with `metadataOnly: true, includePrivateMembers: true` produces full metadata with
`throw null` method bodies instead of compiling IL. Method-body diagnostics then come from the semantic
models, which the scan and the JS emit have already populated, so that pass reads cached bound trees
instead of binding again — 0.8 s where the body codegen it replaces cost 2.1 s.

Measured on `tesserae`: **8.3 s → 6.8 s (−18%)**, `bind + emit .NET assembly` 3.4 s → 1.3 s, total
allocation 687 MB → 603 MB, DLL roughly halved. Emitted JavaScript byte-identical; body errors
(CS0103 / CS0029 / CS1503) still reported, verified on a purpose-built broken project.

**Default: on for Debug, off for Release.** It is sound because a Transpose-compiled assembly can never
execute — it binds against `Transpose.dll`, a stand-in BCL with no implementations, so no .NET host can
load it and no ordinary .NET project can reference it; its only jobs are being *bound against* by
another Transpose project and carrying the compiled JS as embedded resources, and both need metadata
alone. But Release is what `dotnet pack` publishes, so Release keeps real IL, and the SDK makes a Debug
package impossible: `GeneratePackageOnBuild` is forced off for Debug and `dotnet pack -c Debug` fails
with TPS1001. (`<TransposeMetadataOnlyAssembly>` / `--metadata-only-assembly` /
`--no-metadata-only-assembly` override the default.) It implies no debug information — Roslyn rejects
an embedded PDB when there are no bodies to describe.

The pack guard hooks `BeforeTargets="GenerateNuspec"`, not `"Pack"`: GenerateNuspec is a *dependency* of
Pack and is what writes the .nupkg, so a Pack hook fires after the package is already on disk. That was
observed, not theorised.

### 13. ReadyToRun tool packaging — ~1 s off every invocation

A `tps` run has a fixed floor — a one-file project takes 2.2 s and allocates 5 MB, essentially all of
it JIT-compiling Roslyn — and the SDK invokes tps once per project, so a solution pays it per project.
Publishing the tool ReadyToRun precompiles that away: **2.2 s → 1.1 s** on a one-file project, and on
`tesserae` the installed R2R tool measured 5.84–6.51 s against 6.72–7.18 s for the JIT build (~18%).

R2R is native code, so it requires RID-specific tool packages. `TransposePackRidSpecificTools=true`
(in `Transpose.Compiler.csproj`) sets `RuntimeIdentifiers` + `PublishReadyToRun`, and one `dotnet pack`
then produces nine `Transpose.Compiler.<rid>` packages plus a 2 KB outer `Transpose.Compiler` package
whose `DotnetToolSettings.xml` maps each RID to its package. `dotnet tool install Transpose.Compiler`
resolves the right one with no change for users — verified end to end locally, including that the
installed payload is R2R.

Things that cost time to work out, recorded so they do not have to be again:

- Use **`RuntimeIdentifiers`**, not `ToolPackageRuntimeIdentifiers`. The SDK derives the tool-package
  RID list from either, but only `RuntimeIdentifiers` also makes *restore* fetch the per-RID assets;
  with the other one every inner build fails `NETSDK1047`. The pipeline's restore step must pass the
  property too.
- A list-valued property cannot be passed as `-p:X=a;b;c` on the command line (MSBuild parses the
  semicolons as argument separators, and escaping them makes it one RID literal). It has to live in the
  csproj, gated by a plain boolean property.
- With RID-specific packages the outer package contains **no implementation**, so a platform absent
  from the RID list cannot run the tool at all. Hence the deliberately broad list (win/linux/osx ×
  x64/arm64, plus win-x86 and musl for Alpine containers) — an extra RID costs one ~15 MB package per
  release, a missing one breaks somebody.
- Cross-OS and cross-architecture R2R works: a single pack on Linux produced verified-R2R payloads for
  win-x64, win-arm64, osx-arm64 and the rest. The compiler pipeline runs on `ubuntu-latest` because
  that is the host this was verified on.
- `tps-bench --verify-r2r <dir>` opens each produced `.nupkg` and checks the PE ManagedNativeHeader of
  the assemblies inside, so the pipeline gates on what it is about to push rather than on the build
  tree (which contains both the pre-publish IL copy and the R2R publish copy of every RID).
  `--require-r2r` does the same for a single compiler before benchmarking it.

---

## What did not work

### Overlapping two already-parallel phases — **rejected, twice**

Both attempts made things *worse* on this 4-core box, because each phase already saturates all cores
and they simply contend:

- **scan ∥ assembly emit**: scan 0.68 s → 2.2 s, emit 4.25 s → 8.3 s, total 14.4 s → 14.5 s.
- **JS emit ∥ assembly emit** (retested after all the other wins, in case the picture had changed):
  JS emit 1.6 s → 2.5–2.9 s, assembly emit 3.4 s → 5.1–5.6 s, total 8.2–8.7 s → 8.6–9.0 s.

The code is deliberately sequential there. Worth re-testing on many-core hardware, where there is idle
capacity to absorb it — but do not assume; measure.

### Pooling `Emitter.Capture`'s writers — **rejected (no effect)**

The emitter captures sub-fragments into a throw-away `JsWriter` (and `StringBuilder`) constantly, so
recycling them looked obviously right. Measured allocation was **unchanged**: 312.4 MB vs 312.9 MB.
`Capture`'s large inclusive cost is the emission work beneath it, not the writer itself. Reverted
rather than carry the complexity.

### Caching `ISymbol.ToDisplayString()` for the emitter's literal comparisons — **rejected (no effect)**

A fair amount of the emitter's dispatch is `type.ToDisplayString() == "System.DateTime"`-style, and
`ToDisplayString` formats a fresh string every call — including in `IsTaskType` (per await),
`ShouldWrapParams` (per `params` call) and the DateTime/TimeSpan arithmetic check (per binary
expression). Routing all 17 such sites through a per-symbol cache measured **flat**: 272.3–274.1 MB vs
273.8 MB, no time change. The paths turn out to be guarded by earlier returns often enough that they
were never hot, and once the naming layer was cached (§3) nothing else asked for these strings in bulk.
Reverted rather than carry an unbounded per-symbol dictionary for no measured benefit.

### `gcConcurrent=0` with Server GC — **rejected**

13.8 s vs 11.7 s with background GC on.

### `TieredCompilation=0` — **rejected**

Fastest single setting for a big project (9.7 s) but 2.3× slower on a one-file project (4.85 s vs
2.11 s). A compiler must not punish small projects to reward large ones.

---

## The runtime build (`--build-runtime`) — instrumented, not yet optimized

Building the base library (`BCL/Transpose.BCL`, 527 files) into `Transpose.dll` + `tps.js` is the
heaviest single operation in the repo, and until now it reported no phases at all. It is now
instrumented; measured on the reference box:

| phase | ms | share | alloc |
|---|--:|--:|--:|
| resolve project | 154 | 1.1% | 64 MB |
| build compilation (parse, self-contained BCL) | 892 | 6.4% | 87 MB |
| **bind + diagnostics** | **4 768** | **34.4%** | **1 383 MB** |
| emit JavaScript (ClassPath) | 1 910 | 13.8% | 103 MB |
| write ClassPath files | 58 | 0.4% | 1 MB |
| assemble runtime bundles (tps.js) | 29 | 0.2% | 25 MB |
| minify runtime bundles | 1 195 | 8.6% | 192 MB |
| **bind + emit runtime assembly (with bundles)** | **4 836** | **34.9%** | **1 911 MB** |
| total | 13 842 | | **3 767 MB** |

It binds the BCL **three times**: `GetDiagnostics`, then again through the emitter's semantic models,
then a third time inside the final `Emit`. The two visible binds alone are 3.3 GB of the 3.8 GB.

The main `BuildAssembly` path collapsed two of its binds, but the same fix is not a drop-in here:

- The diagnostics **gate the JS emit**, and the assembly emit must come *last* (it takes the assembled
  `tps.js`/`tps.meta.js` bundles as manifest resources), so its diagnostics arrive too late to gate.
- Swapping `Compilation.GetDiagnostics()` for per-tree `SemanticModel.GetDiagnostics()` on a
  `TreeModel` shared with the emitter *would* prepay the emitter's bind (the trick that works in the
  main path), but a self-contained-BCL compilation is exactly where compilation-level diagnostics like
  `CS0518 predefined type not defined` matter, and those are not guaranteed to come out of a per-tree
  pass. Needs a careful diagnostics diff on a deliberately-broken BCL before it can be trusted.

Left alone deliberately: this is a maintainer-only operation (producing the `Transpose.BCL` package),
not something a user's build runs, so the risk/benefit is much worse than on the project path.

## Ideas not yet tried

Roughly in expected-value order.

- **Replace the Mono.Cecil resource embed with Roslyn `manifestResources`** (0.6 s + 71 MB, and Cecil
  re-serialises the whole assembly). `BuildRuntimePackage` already does this. The obstacle is ordering:
  the resources include the compiled JS, available only *after* the JS emit, while the assembly is
  emitted *before* it (that emit is what produces the diagnostics gating the JS emit). Sketched way out:
  start `Emit` on a worker with lazy `ResourceDescription` providers that block on the JS-emit task.
  Needs care — Roslyn calls the provider synchronously during metadata writing, so a mis-ordering
  deadlocks — and the overlap results above suggest the parallelism itself will not pay on 4 cores;
  the win would be dropping Cecil, not the overlap.
- **Reflection metadata build** (~400 ms inside JS emit) is sequential and would parallelise like the
  type bodies do — *but* `BuildMetadataBlock` assigns namespace indices into a shared `_nsCache` in
  visit order, and those indices are baked into the emitted JSON. Parallelising it would change the
  `$n` array and therefore the output, so it needs a deterministic pre-pass over namespaces first.
  Deferred: 5% for a real risk to byte-identical output.
- **`MetadataImportOptions.All`** makes Roslyn import every non-public member of every reference
  (`Transpose.dll` is large). It is required for overload numbering to match, but its cost has never
  been isolated — worth measuring before assuming it is unavoidable.
- **Remaining allocation sites.** A fresh trace of the current compiler totals ~519 MB (down from
  669 MB at the start) and shows **no dominant site left in Transpose's own code** — `TransposeNaming`
  has disappeared from the profile entirely (it was 164 MB / 24% inclusive). What is left is spread
  thinly through Roslyn's data structures: `StringBuilder.ExpandByABlock` 30 MB,
  `StringBuilder.ToString` 24 MB, bare array allocation 16 MB, `ImmutableArray.CreateRange` 12 MB,
  `ImmutableArray.Builder.ToArray` 11 MB, `SyntaxNode.ChildNodes()` 11 MB, `MethodSymbol.AsMember`
  11 MB. Reducing these means making *fewer* Roslyn calls (fewer `GetMembers()` / `AllInterfaces`
  walks, fewer syntax re-walks), not micro-tuning — and each is now worth only ~2% of allocation, so
  measure before investing.
- **`TransposeAssemblies.RuntimeJs` uses `Assembly.LoadFrom`** on the stand-in BCL just to read an
  embedded resource. Not on the hot path (only `--with-runtime`), but a `PEReader` read — as
  `NoBodyMethodTokens` already does — would avoid loading a fake corlib into the compiler process.
- **`HasNoBody` looks up a method's metadata token in `NoBodyMethodTokens`, which is built from
  `Transpose.dll` only** — but it is called for methods from *any* referenced assembly, and metadata
  tokens are only meaningful within their own module. This is a latent correctness bug (a spurious
  token match changes an emitted name), not a performance one, and it is why changing what a
  referenced package DLL contains can shift emitted output. Worth fixing on its own merits.
- **Re-test every "parallelising made it worse" result on many-core hardware.** All of them are 4-core
  results.
