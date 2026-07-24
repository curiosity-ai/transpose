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
| `tesserae` — 263 files / ~69k LOC → package DLL | 15.82 s | **7.23 s** | **−54.3%** | −36.9% |
| `tesserae+tests` — multi-project: dependency + site build | 23.16 s | **8.64 s** | **−62.7%** | −41.5% |
| `tesserae+tests-warm` — dependency already up to date | 9.03 s | **4.23 s** | **−53.1%** | −46.4% |

Baseline commit `79fcfc1`. The recorded reports are checked in as `docs/perf/baseline.json` and
`docs/perf/optimized.json` (plus `optimized.md`), so a later change can be compared against either
without rebuilding an old compiler:

```bash
tps-bench --tps <tps> --tesserae <tesserae> --iterations 3 --baseline docs/perf/optimized.json
```

Emitted output is **byte-identical** to the pre-optimization compiler across all of it (verified with
`compare-site.sh` over the whole `Tesserae.Tests` site), and the 499-test suite stays green.

### Phase split now, for `tesserae`

| phase | ms | share | alloc |
|---|--:|--:|--:|
| resolve project | 47 | 0.6% | 15 MB |
| build compilation (parse + references) | 396 | 5.0% | 29 MB |
| scan unsupported features | 1 823 | 23.1% | 57 MB |
| bind + emit .NET assembly | 3 258 | 41.3% | 199 MB |
| emit JavaScript | 1 689 | 21.4% | 274 MB |
| collect package resources (minify) | 49 | 0.6% | 40 MB |
| embed resources into DLL (Mono.Cecil) | 633 | 8.0% | 71 MB |

Two structural facts that bound everything below:

1. **A build binds the whole project about twice** — once inside `Compilation.Emit` (to produce IL) and
   once through the `SemanticModel` (to drive the JS emitter). Roslyn's internal bound trees are not
   reachable from the public API, so the two cannot be merged. The only way to remove one is to stop
   producing IL at all (see `--metadata-only-assembly` below).
2. **Every `tps` invocation has a ~2.2 s floor.** A *one-file* project takes 2.2 s and allocates 5 MB;
   that is JIT of Roslyn plus importing `Transpose.dll`'s metadata. The SDK runs `tps` once per
   project, so a solution pays it per project.

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

## Available but not enabled: `--metadata-only-assembly` — a further ~18%

`<TransposeMetadataOnlyAssembly>true</TransposeMetadataOnlyAssembly>` or `--metadata-only-assembly`
emits the project's .NET assembly as metadata only (full metadata **including private members**,
`throw null` method bodies) instead of compiling IL. Method-body diagnostics then come from the
semantic models, which the scan and the JS emit have already populated, so that pass reads cached bound
trees instead of binding again.

Measured on `tesserae`: **8.3 s → 6.8 s (−18%)**, `bind + emit .NET assembly` 3.4 s → 1.3 s (the
replacement diagnostics pass costs 0.8 s), total allocation 687 MB → 603 MB, and the DLL roughly halves
(1.58 MB → 0.81 MB). Emitted JavaScript is byte-identical, and body errors (CS0103 / CS0029 / CS1503)
are still reported — verified on a purpose-built broken project.

**Off by default because it changes what a published package contains, and that is a maintainer
decision.** The argument for turning it on: a Transpose-compiled assembly can never execute. It binds
against `Transpose.dll`, a stand-in BCL with no implementations, so no .NET host can load it and no
ordinary .NET project can reference it. Its only jobs are being *bound against* by another Transpose
project and carrying the compiled JS as embedded resources — both need metadata alone, so the IL is
already dead weight. Note it implies no debug information (Roslyn rejects an embedded PDB when there
are no bodies to describe).

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

### `gcConcurrent=0` with Server GC — **rejected**

13.8 s vs 11.7 s with background GC on.

### `TieredCompilation=0` — **rejected**

Fastest single setting for a big project (9.7 s) but 2.3× slower on a one-file project (4.85 s vs
2.11 s). A compiler must not punish small projects to reward large ones.

---

## Ideas not yet tried

Roughly in expected-value order.

- **Ship `tps` ReadyToRun — the largest untaken win.** Measured: a one-file project 2.2 s → **1.1 s**,
  and `tesserae` 8.4–9.4 s → **7.3–7.9 s** (~11%), i.e. ~1 s off *every* invocation. The csproj already
  sets `PublishReadyToRun`, so it only needs a RID-specific publish:
  `dotnet publish -c Release -r linux-x64 --self-contained false -p:PublishReadyToRun=true`.
  The blocker is packaging: `Transpose.Compiler` ships as a portable dotnet tool, so capturing this
  means RID-specific tool packages and per-platform CI. Since the SDK invokes `tps` once per project,
  the saving multiplies across a solution.
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
- **`ISymbol.ToDisplayString()` comparisons against string literals** remain in the emitter
  (`IsTaskType` and friends in `Emitter.cs`, ~11 sites in `Emitter.Expressions2.cs`). Each formats a
  fresh fully-qualified string. Resolving the target types once from the compilation and comparing with
  `SymbolEqualityComparer` would remove them from hot paths. Smaller now that the naming layer is
  cached, but still real.
- **Remaining allocation sites** after the current round (from a fresh trace, ~557 MB total):
  `ImmutableArray.CreateRange` 36 MB, `StringBuilder.ExpandByABlock` 30 MB, `StringBuilder.ToString`
  23 MB, `SyntaxNode.ChildNodes()` 11 MB. Most are inside Roslyn, reached through `GetMembers()` /
  `AllInterfaces` / syntax walks — reducing them means making fewer such calls, not micro-tuning.
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
