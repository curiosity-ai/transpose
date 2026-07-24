# TODO.optimization.md — Transpose compiler performance

Running log of compiler performance work: what was measured, what was tried, what worked, and — just
as important — **what did not**, so a future session does not re-walk a dead end.

Read this together with the **`transpose-performance`** skill (`.claude/skills/transpose-performance/`),
which describes the workflow and the measurement traps on this hardware.

---

## How to measure (short version)

```bash
# 1. Build the compiler and the benchmark harness
dotnet build Transpose/Transpose.Compiler/Transpose.Compiler.csproj -c Release
dotnet build Transpose/Transpose.Bench/Transpose.Bench.csproj      -c Release

# 2. Full report: machine info + CPU score + clean-slate compiler scenarios
./Transpose/Transpose.Bench/bin/Release/net10.0/tps-bench \
    --tps ./Transpose/Transpose.Compiler/bin/Release/net10.0/tps \
    --tesserae /home/user/tesserae \
    --iterations 3 --label my-change \
    --json artifacts/bench/my-change.json --baseline artifacts/bench/baseline.json

# 3. Quick A/B of two compiler binaries, interleaved (the only trustworthy way on a noisy host)
./Transpose/Transpose.Bench/ab.sh <tps-A> <tps-B> /home/user/tesserae/Tesserae/Tesserae.csproj 4

# 4. Per-phase timing + per-phase allocations for one build
tps --project <proj.csproj> -c Debug --timing
```

### ⚠ The measurement trap on this hardware

The dev container is a 4-core Firecracker VM whose throughput **drifts by 20–40% over minutes**. A
single before/after pair is worthless — we recorded the *same binary and the same input* at 14.1 s and
20.3 s within one session. Rules:

- Never conclude anything from one run. Use `ab.sh` (interleaves A,B,A,B…) or `tps-bench --iterations 3+`.
- A phase can also change time for a reason that has nothing to do with that phase: **whichever phase
  touches Roslyn's binder first pays the JIT warm-up for it**. That is why "scan unsupported features"
  appeared to grow from 0.68 s to 2.0 s when the `GetDiagnostics` pass in front of it was removed — the
  scan did not get slower, it inherited the warm-up bill. Compare *totals*, and only then read phases.

---

## Baseline (before any of this work)

Commit `79fcfc1`, `-c Debug`, 4-core Xeon @2.8 GHz container, CPU score ≈ 92, medians of 3:

| scenario | wall | alloc | peak WS |
|---|--:|--:|--:|
| `tesserae` (263 files / ~69k LOC → package DLL) | **17.1 s** | 1 086 MB | 389 MB |
| `tesserae+tests` (multi-project: dependency + site build) | **25.1 s** | 1 623 MB | 511 MB |
| `tesserae+tests-warm` (dependency up to date) | **9.8 s** | 529 MB | 256 MB |

Phase split for `tesserae`:

| phase | ms | share | alloc |
|---|--:|--:|--:|
| resolve project | 39 | 0.2% | 15 MB |
| build compilation (parse + references) | 1 011 | 5.9% | 28 MB |
| bind + diagnostics (`GetDiagnostics`) | 4 397 | 25.7% | 144 MB |
| scan unsupported features | 2 661 | 15.6% | 150 MB |
| emit .NET assembly (`Compilation.Emit`) | 4 251 | 24.9% | 207 MB |
| emit JavaScript | 3 706 | 21.7% | **430 MB** |
| collect package resources (minify) | 104 | 0.6% | 40 MB |
| embed resources into DLL (Mono.Cecil) | 927 | 5.4% | 71 MB |

Headline observation: a build binds the whole project **more than once** (GetDiagnostics, then Emit,
then the scanner's per-identifier queries, then the JS emitter's), and JS emit is by far the biggest
allocator.

---

## What worked

Ordered by size of win. All were verified to leave the emitted site **byte-identical** (see
"Correctness gate" below) and to keep the 499-test suite green.

### 1. Runtime configuration: `TieredPGO=off` — ~25–30%

`Transpose.Compiler.csproj` → `<TieredPGO>false</TieredPGO>`.

Tiered PGO instruments tier-0 code to collect a profile. Roslyn's binder is exactly the deeply
polymorphic code that instrumentation taxes hardest, and a compile is over in seconds — long before
the better tier-1 code the profile would buy can pay that tax back. Measured (interleaved, Server GC
on for both): **14.1–14.6 s → 10.3 s** on `tesserae`, and **neutral** (2.11 s vs 2.13 s) on a
one-file project, so there is no small-project regression.

Related knobs measured at the same time:

| setting | `tesserae` | one-file project | verdict |
|---|--:|--:|---|
| default | 14.1–14.6 s | 2.11 s | — |
| `TieredPGO=0` | **10.3 s** | 2.13 s | **adopted** |
| `TC_CallCountingDelayMs=0` | 11.9–12.1 s | 2.17 s | adopted (stacks with the above → 9.2 s) |
| `TC_QuickJitForLoops=0` | 13.1–13.6 s | — | not worth it on its own |
| `TieredCompilation=0` (no tiering at all) | 9.7–10.0 s | **4.85 s (2.3× worse)** | **rejected** — cripples small projects |

`CallCountingDelayMs` has no MSBuild property, so it is set through
`Transpose.Compiler/runtimeconfig.template.json`.

### 2. Runtime configuration: Server GC — ~15%

`<ServerGarbageCollection>true</ServerGarbageCollection>`. A build produces ~900 MB of short-lived
garbage inside several `Parallel.ForEach` phases; on Workstation GC every collection serialises on one
heap and stalls all workers. Interleaved measurement: **15.1–17.3 s → 12.2–12.9 s**. Peak working set
rises ~390 MB → ~490 MB, and .NET's DATAS keeps the heap count adaptive so a trivial project does not
pay a many-heap memory penalty.

Rejected variant: `gcConcurrent=0` alongside Server GC — measurably *worse* (13.8 s vs 11.7 s).

### 3. `UnsupportedFeatureScanner`: screen identifiers by text before binding — −2.0 s, −124 MB

`VisitIdentifierName` fires on nearly every identifier in the source and used to call
`GetSymbolInfo` on each one, which binds the whole enclosing member. It now first checks the
identifier's *text* against a set precomputed once per compilation: the simple names of every type in
a denied namespace (`System.IO`, `System.Net.Sockets`, `System.Threading`) that the scanner would
actually reject, plus `nint`/`nuint`/`var`.

This is **sound, not heuristic** — an identifier can only bind to a denied type if its text is that
type's name. The three ways around that are each handled explicitly:

- `var` is in the set, so an inferred local whose type is never written out is still caught.
- `using MyFile = System.IO.File;` — the alias name is added to the file's name set when the directive
  is visited, so the *usage site* is still reported (reporting the directive instead would have
  rejected an unused import, which the scanner never did).
- `using static System.IO.File;` — same, with the imported type's member names.

Also prefiltered: `CheckDllImport` (screen the written attribute name before binding) and
`VisitIsPatternExpression` (only a *string-literal* constant pattern can be the span-pattern form).

Result: **2 661 ms → 684 ms**, allocations 150 MB → 27 MB. Diagnostics were diffed against the old
compiler on a purpose-built file exercising every rule: same set of rejected constructs, minus five
*duplicate* reports at the same site (the old code reported both `File` and `ReadAllText` in
`File.ReadAllText(...)`, and separately flagged the `var` on the same line).

### 4. Take diagnostics from `Compilation.Emit` instead of a separate `GetDiagnostics()` — ~0.7 s, −75 MB

`Emit`'s result already carries every declaration and method-body diagnostic. When an assembly is
being emitted (every real project build) the standalone `GetDiagnostics()` pass is now skipped.

**This is much less of a win than the phase table suggests** and the reason is worth recording:
`GetDiagnostics` and `Emit` were not doing the same work twice, they were *sharing* it. `Emit` runs on
`compilation.WithOptions(…DynamicallyLinkedLibrary)`, and the preceding `GetDiagnostics` on the
original compilation warmed the reference manager and the JIT for the binder. Removing it made `Emit`
alone go 4.25 s → ~6.5 s, so the net was ~0.7 s, not the 4.3 s the table implies. Source-only builds
(the test suite, `--out`) keep the `GetDiagnostics` path. On a *failed* emit we fall back to a full
`GetDiagnostics`, because `Emit` stops before compiling method bodies when declarations already have
errors and would otherwise report a subset.

### 5. Parallel parsing — −0.6 s

`CompilationBuilder.Build` parsed 263 files sequentially. Now `Parallel.For` into a pre-sized array so
tree order (and therefore bundle order) is preserved. **1 011 ms → ~450 ms.**

### 6. Parallel type collection — −0.25 s

`Emitter.CollectTypes` walked every tree sequentially calling `GetDeclaredSymbol`. Now per-tree in
parallel, merged in tree order, with the dedupe (partial types) applied after the merge.
**345 ms → ~80 ms.**

### 7. One shared semantic-model cache for the scan *and* the emit — ~5% of JS emit

A `SemanticModel` retains the bound form of every member it is asked about. Previously the scanner
built throw-away models, and `Emitter.Clone()` built a **fresh `TreeModel` per type** (483 of them).
Now a single `TreeModel` (backed by a `ConcurrentDictionary`) is created in `RoslynTranslator` and
passed to both the scan and every emitter clone, so a member the scan bound is already bound when the
emitter reaches it. JS emit 2 543 → ~2 300 ms, allocations 446 → 432 MB.

Smaller than hoped, and the reason is instructive: Roslyn caches at *member* granularity, so two
types in the same file were never sharing bound bodies anyway — only the (cheap) tree-level model
object was duplicated. The real saving comes from the scan/emit overlap, not from the clones.

### 8. Deterministic output (correctness, found while verifying)

`EmitEnumeratorInit` named its enumerator temp from `forEach.GetHashCode()` — a *reference* hash, so
the emitted bundle differed byte-for-byte on every compile of unchanged sources. Now derived from
`forEach.SpanStart`, which is stable and unique within a file (all the uniqueness the name needs,
since it is local to one JS function). Confirmed: two consecutive clean builds now produce an
identical `app.js`.

---

## What did not work

### Running the unsupported-feature scan concurrently with the assembly emit — **rejected**

Both phases are already internally parallel (`Parallel.ForEach` / Roslyn's concurrent build) and
saturate all 4 cores, so overlapping them just made them contend: the scan went 0.68 s → 2.2 s and the
emit 4.25 s → 8.3 s, for a **worse** total (14.5 s vs 14.4 s sequential). Worth re-testing on a
many-core machine, where there is idle capacity to absorb it — the code was left sequential and the
`Task.Run` removed.

### `gcConcurrent=0` with Server GC — **rejected**

13.8 s vs 11.7 s with concurrent (background) GC on. See table above.

### `TieredCompilation=0` — **rejected**

Fastest option for a big project (9.7 s) but 2.3× slower on a one-file project (4.85 s vs 2.11 s).
A compiler must not punish small projects to reward large ones.

---

## Ideas not yet tried

Roughly in expected-value order.

- **Replace the Mono.Cecil resource embed with Roslyn `manifestResources`** (0.6–0.9 s + 71 MB, and the
  DLL is currently written twice). `BuildRuntimePackage` already does this. The obstacle is ordering:
  the resources include the compiled JS, which is only available *after* the JS emit, while the
  assembly is emitted *before* it (that emit is what produces the diagnostics gating the JS emit).
  Sketched way out: start `Emit` on a worker with lazy `ResourceDescription` providers that block on
  the JS-emit task, so the two overlap and Cecil disappears. Needs care — Roslyn calls the provider
  synchronously during metadata writing, so a mis-ordering deadlocks.
- **`metadataOnly` assembly emit.** The package DLL is only ever *bound against* (by `tps`), never
  executed, so a ref-assembly-with-private-members would do and would skip method-body codegen
  entirely. But it also skips method-body *diagnostics*, so `GetDiagnostics` comes back: measured
  trade is roughly break-even, and it changes what a published NuGet package contains (method bodies
  become `throw null`). **Do not do this silently** — it needs a product decision.
- **Drop the embedded PDB** from the package emit (`DebugInformationFormat.Embedded` is hardcoded even
  though Tesserae sets `DebugType=None`). Not yet measured in isolation.
- **JS emit allocation reduction** — still the largest allocator at ~430 MB for `tesserae`. Suspects:
  `ISymbol.ToDisplayString()` on hot paths (it formats a fresh string every call and several
  comparisons are done against the *formatted* name), LINQ in the emitter walkers, one `JsWriter` +
  `StringBuilder` per type, and `TypeRef`/`MemberJsName` recomputation. A `ToDisplayString` result
  cache keyed on symbol is the obvious first move.
- **`IsTaskType` and friends compare `ToDisplayString()` against string literals.** Replacing those
  with `SymbolEqualityComparer` against symbols resolved once from the compilation would remove a
  large number of string formats from the hottest emitter paths.
- **Reflection metadata build** (~450–650 ms inside JS emit) is sequential; it is a per-type map and
  should parallelise like the type bodies do.
- **`MetadataImportOptions.All`** makes Roslyn import every non-public member of every reference. It is
  required for overload numbering to match, but the cost has never been measured — worth quantifying
  before assuming it is unavoidable.
- **Re-test phase overlap on a many-core machine.** All the "parallelising made it worse" results above
  are 4-core results.

---

## Correctness gate for any change here

1. `dotnet test Transpose/Transpose.Translator.Tests/Transpose.Translator.Tests.csproj -c Release`
   — 499 tests, ~3.5 min, must stay green.
2. Byte-compare the whole emitted site against the pre-change compiler:
   ```bash
   git worktree add /tmp/tps-base <base-commit>
   (cd /tmp/tps-base && dotnet build Transpose/Transpose.Compiler/Transpose.Compiler.csproj -c Release)
   # build Tesserae.Tests with each compiler into /tmp/out-base and /tmp/out-new, then
   diff -r /tmp/out-base /tmp/out-new
   ```
3. For anything touching `UnsupportedFeatureScanner`, diff the diagnostics of a file that exercises
   every rule (pointers, `unsafe`, `checked`, `nint`, P/Invoke, `System.IO`/`System.Threading` types,
   an alias import and a static import) against the pre-change compiler.
