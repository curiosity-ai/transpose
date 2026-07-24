---
name: transpose-performance
description: >-
  Measure and improve the Transpose compiler's build performance — wall time, allocations, GC and
  per-phase cost — using the tps-bench harness (CPU-score-normalised, clean-slate scenarios) and
  dotnet-trace profiles. Use this WHENEVER the task is to make `tps` faster or leaner, to find out
  where a build spends its time or memory, to benchmark a compiler change, or to investigate a
  build-time regression. Triggers on "why is compilation slow", "speed up tps", "profile the
  compiler", "reduce allocations", "benchmark this change", "how long does compiling X take",
  "parallelise the compiler", or any work touching Transpose.Bench / PhaseTimings / TODO.optimization.md.
  Pairs with transpose-debugging (correctness of emitted JS) — every optimization here must keep the
  emitted output byte-identical.
---

# Optimizing Transpose compilation

`tps` is a plain CLI with no cache and no compile server (deliberately — see CLAUDE.md), so a build's
cost is whatever one process does from scratch. This skill is the loop for measuring that and making
it smaller without changing a byte of emitted JavaScript.

**Read [`TODO.optimization.md`](../../../TODO.optimization.md) first.** It is the running log of every
optimization tried, with measurements — including the ones that *did not work*, which is most of the
value. Do not re-walk a dead end; add to it as you go.

## The one thing that will mislead you

The dev container is a 4-core Firecracker VM whose throughput **drifts 20–40% over minutes**. The same
binary on the same input measured 14.1 s and 20.3 s within one session. So:

- **Never** conclude anything from a single before/after pair. Use `ab.sh` (interleaves A,B,A,B…) or
  `tps-bench --iterations 3+`.
- **Allocation numbers are exact** (`GC.GetTotalAllocatedBytes`) while times are noisy — when a change
  is meant to reduce garbage, believe the MB and treat the ms as corroboration.
- **A phase's time can move for reasons unrelated to that phase.** Whichever phase touches Roslyn's
  binder first pays the JIT warm-up for all of it: removing a pass in front of the unsupported-feature
  scan made the scan look 3× slower while the total was flat. Compare **totals first**, phases second.
- Kill stray build servers before measuring (`ps aux | grep VBCSCompiler`), and check `/proc/loadavg`.

## Setup

```bash
cd <transpose>
dotnet build Transpose/Transpose.Compiler/Transpose.Compiler.csproj -c Release
dotnet build Transpose/Transpose.Bench/Transpose.Bench.csproj      -c Release
TPS=./Transpose/Transpose.Compiler/bin/Release/net10.0/tps
BENCH=./Transpose/Transpose.Bench/bin/Release/net10.0/tps-bench
```

A baseline compiler to compare against, built from the commit you are improving on:

```bash
git worktree add /tmp/tps-base <base-commit>
(cd /tmp/tps-base && dotnet build Transpose/Transpose.Compiler/Transpose.Compiler.csproj -c Release)
BASE=/tmp/tps-base/Transpose/Transpose.Compiler/bin/Release/net10.0/tps
```

The corpus is the **tesserae** repo (a sibling checkout): `Tesserae` is a single 263-file / ~69k-line
project, and `Tesserae.Tests` adds a second project that depends on it — which is what exercises the
multi-project path (`tps` compiles the dependency first, then the site build).

## The loop

### 1. Where does this build spend its time and memory?

```bash
cd <tesserae>
rm -rf Tesserae/bin Tesserae/obj                 # clean slate — see "Cleaning" below
$TPS --project Tesserae/Tesserae.csproj -c Debug --timing
```

`--timing` prints per-phase wall time, each phase's share, **and the bytes allocated while it ran**,
plus process totals (allocated, peak working set, gen0/1/2 counts). `--timing-json <file>` writes the
same as JSON. Sub-phases are indented (`├`) and are already counted inside their parent, so only
top-level phases sum to the total.

### 2. Is my change actually faster?

```bash
# Interleaved A/B on one project — the trustworthy quick check
./Transpose/Transpose.Bench/ab.sh $BASE $TPS <tesserae>/Tesserae/Tesserae.csproj 4
```

### 3. The full report (what to paste into a PR)

```bash
$BENCH --tps $TPS --tesserae <tesserae> --iterations 3 --label my-change \
       --json artifacts/bench/my-change.json --markdown artifacts/bench/my-change.md \
       --baseline docs/perf/optimized.json
```

`docs/perf/baseline.json` (before any of this work) and `docs/perf/optimized.json` (current) are
checked in, so you can compare against either without rebuilding an old compiler.

`tps-bench` prints, in order: the machine (CPU model, physical/logical cores, RAM, and the SIMD/crypto
ISAs the JIT will use), a short deterministic **CPU+memory benchmark** and the **score** it yields,
then each scenario's wall time, CPU time, allocations, peak working set and phase breakdown.

Every time is also reported **normalised** = `measured × score / 100`, i.e. converted to
reference-machine milliseconds. That is what makes a number recorded in one session comparable with
one recorded on different hardware — always quote the normalised figure when comparing across runs,
and the raw one when describing the machine you were on.

The score's reference values are **constants** calibrated on a 4-core Xeon @2.8 GHz container
(`CpuScore.Reference`). Re-calibrate only if the workloads themselves change — never to "re-centre" a
new machine, which would destroy the only property the score has.

### 4. Where is the allocation / CPU actually going?

```bash
dotnet tool install --global dotnet-trace     # once

# Allocation profile (GCAllocationTick — one sample per ~100 KB)
dotnet-trace collect -o /tmp/alloc.nettrace --providers "Microsoft-Windows-DotNETRuntime:0x1:5" \
  -- $TPS --project <tesserae>/Tesserae/Tesserae.csproj -c Debug -q
dotnet-trace convert --format Speedscope /tmp/alloc.nettrace -o /tmp/alloc

# CPU sampling profile
dotnet-trace collect --format speedscope -o /tmp/cpu.nettrace \
  --providers "Microsoft-DotNETCore-SampleProfiler:::EventLevel=Informational" \
  -- $TPS --project <tesserae>/Tesserae/Tesserae.csproj -c Debug -q
```

The speedscope JSON is `evented` (paired `O`/`C` records per frame), not `sampled`. To rank frames,
count ticks per frame — see [`scripts/analyze-trace.py`](scripts/analyze-trace.py):

```bash
python3 .claude/skills/transpose-performance/scripts/analyze-trace.py /tmp/alloc.speedscope.json
```

For an allocation trace, "inclusive" ≈ bytes allocated beneath a frame and the innermost managed
frame is the allocation site. Ignore the `CPU_TIME` / `UNMANAGED_CODE_TIME` pseudo-frames.

## Cleaning: what a "clean slate" means here

`tps` skips a referenced project whose package DLL is up to date (`ProjectResolver.IsPackageUpToDate`),
so a second run measures a *different, much cheaper* build. Before every timed clean run, delete
`bin` and `obj` **of the project and of every project it references transitively**. `tps-bench` and
`ab.sh` both do this; if you time by hand, do it too, or you will report an incremental build as a
full one. Benchmark the incremental path deliberately instead, with a `:warm` scenario.

## The correctness gate — non-negotiable

A compiler optimization that changes output is a bug, not an optimization. Before claiming any win:

1. **Test suite**: `dotnet test Transpose/Transpose.Translator.Tests/Transpose.Translator.Tests.csproj -c Release`
   — 499 tests, ~3.5 min, must stay green.
2. **Byte-compare the whole emitted site** against the baseline compiler, for `Tesserae.Tests` (which
   covers both the package build and the site build):
   ```bash
   cd <tesserae>
   rm -rf Tesserae/bin Tesserae/obj Tesserae.Tests/bin Tesserae.Tests/obj
   $BASE --project Tesserae.Tests/Tesserae.Tests.csproj -c Debug -q
   cp -r Tesserae.Tests/bin/Debug/netstandard2.0/tps /tmp/out-base
   rm -rf Tesserae/bin Tesserae/obj Tesserae.Tests/bin Tesserae.Tests/obj
   $TPS  --project Tesserae.Tests/Tesserae.Tests.csproj -c Debug -q
   cp -r Tesserae.Tests/bin/Debug/netstandard2.0/tps /tmp/out-new
   diff -r /tmp/out-base /tmp/out-new
   ```
   Output is byte-reproducible as of the enumerator-naming fix, so `diff -r` should be silent. If you
   are comparing against a compiler older than that fix, normalise `$e<hex>` names first
   (see [`scripts/compare-site.sh`](scripts/compare-site.sh)).
3. **Touching `UnsupportedFeatureScanner`**: also diff the diagnostics of a file exercising every rule
   (pointers, `unsafe`, `checked`, `nint`, P/Invoke, `System.IO`/`System.Threading` types, a using
   alias and a static import) — see the scanner section of `TODO.optimization.md`.

## What the cost structure actually looks like

Measured on the reference box for a clean `Tesserae` build (after the current round of optimization —
re-measure, do not trust these as current):

| phase | share | notes |
|---|--:|---|
| `bind + emit .NET assembly` | ~43% | Roslyn binding every method body + IL codegen |
| `scan unsupported features` | ~22% | the *first* Roslyn consumer, so it absorbs JIT warm-up |
| `emit JavaScript` | ~21% | the largest single allocator |
| `embed resources into DLL (Cecil)` | ~7% | Mono.Cecil re-serialises the assembly |
| `build compilation (parse + references)` | ~5% | parallel |

Two structural facts worth internalising before optimizing anything:

- **A build binds the project roughly twice**: once inside `Compilation.Emit` (for IL) and once through
  the `SemanticModel` (for the JS emitter). Roslyn's internal bound trees are not reachable from the
  public API, so these cannot be merged — the only way to remove one is to stop producing IL
  (`--metadata-only-assembly`, opt-in).
- **The unsupported-feature scan and the JS emitter share one `TreeModel`**, so a member the scan binds
  is already bound when the emitter reaches it. This is why the scan looks expensive but is nearly
  free: removing its `var` resolution moved ~0.8 s into the JS emit and left the total flat.

### Per-invocation fixed cost

A **one-file** project takes ~2.2 s and allocates 5 MB. That is almost entirely JIT of Roslyn plus
importing `Transpose.dll`'s metadata, and every project in a solution pays it, since the SDK runs `tps`
once per project. `PublishReadyToRun` halves it (measured 2.2 s → 1.1 s on a one-file project, and
~1 s off any build) but needs RID-specific tool packaging. This is the largest untaken opportunity.

## Rules of thumb learned here

- **Runtime configuration beat every code change.** `TieredPGO=false` alone was ~25–30%; Server GC ~15%.
  Check `Transpose.Compiler.csproj` + `runtimeconfig.template.json` before writing code.
- **Parallelising two already-parallel phases makes things worse** on a 4-core box — both the scan/emit
  overlap and the JS-emit/assembly-emit overlap were measured *slower*. Re-test on many-core hardware
  before assuming otherwise; the code is deliberately sequential there.
- **Cache the expensive pure functions of a symbol.** The naming layer (`TransposeNaming`) recomputed a
  member's JS name once per *reference*; caching `MemberJsName`, `JsBaseName` and
  `ImplementedInterfaceMember` cut JS emit 2.3×. Key caches on the symbol **as passed**, never on
  `OriginalDefinition`, or you change answers.
- **`GetAttributes().FirstOrDefault(a => AttrIs(a, name))` is not free**: the closure and the boxed
  `ImmutableArray` enumerator made attribute lookup the single largest allocation site in the build.
  Plain loops (`TransposeNaming.FindAttr`/`HasAttr`/`AnyInSource`) instead. Same for `.OfType<T>()` over
  `GetMembers()`.
- **Screen syntactically before asking the semantic model.** Binding an identifier binds its whole
  enclosing member. The scanner's per-identifier filter (identifier text vs. a precomputed name set) cut
  it from 2.7 s to 0.7 s — but only do this when the filter is provably *sound*, and handle the escapes
  (`var`, using aliases, static imports) explicitly.
- **Don't keep a change that measures flat.** Pooling `Emitter.Capture`'s writers looked obviously right
  and moved allocation by 0.5 MB out of 312 MB. It was reverted; the finding is logged.
