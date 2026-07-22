---
name: transpose-h5-audit
description: >-
  Systematically audit the Transpose C#→JS compiler for behavioural discrepancies against the
  proven-correct h5 baseline (h5 is the predecessor Transpose was forked from), using the Curiosity
  front-end as a real-world corpus. Use this WHENEVER the task is to hunt for compiler bugs, validate
  a language feature or BCL area against h5/native, run a broad correctness sweep, or answer "does
  Transpose handle X the same as h5/.NET?". Triggers on requests to audit/validate/compare the
  compiler, investigate a checklist area (async, pattern matching, LINQ, formatting, delegates,
  generics, Newtonsoft, collections, exceptions, static init), or confirm a construct matches h5.
  Builds on the transpose-debugging skill for the per-construct loop.
---

# Auditing Transpose against the h5 baseline

h5 is the mature predecessor Transpose was rebuilt from; its emitted JavaScript is the known-good
reference. An audit lines up **three** things for a sampled construct and looks for divergence:

1. the **h5-emitted JS** (extracted from shipped h5 NuGet DLLs — the corpus),
2. the **transpose-emitted JS** (regenerate with the emit/JSON runners),
3. the **C# source and native .NET behaviour**.

## The rule for a real finding

> A finding is real ONLY if transpose diverges from **BOTH** native .NET **AND** h5.

Many apparent divergences are intentional h5-compatible contracts (e.g. a boxed `[Enum(Emit.Value)]`
enum stringifies to its number — NOT a bug). Before filing anything, grep the extracted h5 corpus for
how h5 emits the same construct. If h5 shares a divergence from native, note it but treat it as low
priority — it is a deliberate contract far more often than a bug.

## Setup

1. Set up the runners (see the **transpose-debugging** skill): `setup-toolkit.sh`, then
   `export TRANSPOSE_DLL_PATH=...`.
2. Download the h5 corpus DLLs and extract their embedded `.js` manifest resources with `jsdumper`.
   The Curiosity front-end packages are the corpus (versions may differ — check nuget.org):

```bash
mkdir -p /tmp/h5pkg && cd /tmp/h5pkg
V=26.7.2802   # bump to the current Curiosity.FrontEnd version if needed
for p in curiosity.frontend curiosity.frontend.admin curiosity.frontend.api curiosity.frontend.core; do
  curl -sSL -o "$p.$V.nupkg" "https://api.nuget.org/v3-flatcontainer/$p/$V/$p.$V.nupkg"
  mkdir -p "${p}_x" && (cd "${p}_x" && unzip -oq "../$p.$V.nupkg")
  dll=$(find "${p}_x/lib" -name "*.dll"); short="${p##*.}"
  dotnet /tmp/jsdumper/bin/Debug/net10.0/jsdumper.dll "$dll" "/tmp/h5js/$short"
done
```

This yields the known-good h5 output under `/tmp/h5js/{frontend,admin,api,core}/*.js`. Grep these to
confirm how h5 emits a construct (h5 uses the `H5.` runtime global and `H5.is(...)` type tests, so
translate names mentally: `Transpose.` ↔ `H5.`, `TransposeR` ↔ helper).

## Picking what to sample

The matching transpose-based C# source is in the **mosaik** repo under
`FrontEnd/Mosaik.FrontEnd{,.Admin,.API,.Core}` (assembly names `Curiosity.FrontEnd*`). Read it first
to pick classes/methods worth comparing, and grep it to gauge what the corpus actually uses (bias
toward high-frequency constructs):

```bash
grep -rhoE '\.ToString\("[^"]+"\)' --include=*.cs FrontEnd | sort | uniq -c | sort -rn | head
```

Random-sample by class and method name across the checklist areas below.

## Checklist areas to scrutinise

Write deterministic snippets (avoid wall-clock races; sequence async work with awaited
`Task.FromResult`/`TaskCompletionSource`). For each: run in Node, compare to native, then grep the h5
corpus before filing.

- **async/await**: `Task.WhenAll`/`WhenAny` ordering, `ContinueWith`, `TaskCompletionSource`,
  exception surfacing (single vs aggregate), cancellation, `ConfigureAwait` as a no-op, `async void`.
- **pattern matching**: switch expressions with property/positional/relational/list patterns, `when`
  guards, `not`/`and`/`or`, `var` captures, tuple patterns (real ValueTuple instances), exhaustiveness.
- **string/number/date formatting**: `ToString(format)` (N/F/X/D/G/E/P/C + custom `0.##`/`#,##0`),
  DateTime/TimeSpan format+parse, alignment+format combos, `StringBuilder`, `double` "R" round-trip.
- **LINQ**: GroupBy (element/result selectors), ToLookup, Join/GroupJoin, SelectMany-with-result,
  OrderBy stability, DefaultIfEmpty, SequenceEqual, Aggregate(seed[,resultSelector]), deferred
  execution + multiple enumeration, Cast/OfType, comparer overloads.
- **events & delegates**: `+=`/`-=` and binary `+`/`-` combine/remove order, multicast return value,
  `handler?.Invoke()`, delegate equality, method-group vs lambda identity, Func/Action variance.
- **generics & variance**: constrained generics, generic virtual dispatch, `IEnumerable<out T>` /
  `IComparer<in T>`, per-closed-type generic static fields, `default(T)` in ref/out/array positions.
- **Newtonsoft** (use the JSON runner): TypeNameHandling `$type`, custom `JsonConverter`,
  `JsonSerializerSettings`, collection target types, StringEnumConverter, JObject/JArray, PopulateObject,
  `[JsonProperty]`/`[JsonIgnore]`.
- **collections**: custom `IEqualityComparer`/`IComparer`, Sorted*/LinkedList/Stack/Queue,
  `TryGetValue` out semantics, indexer-throw vs ContainsKey, dup-Add throw.
- **numeric edge cases**: `Math.Round` MidpointRounding, `Convert.ToInt32` overflow/banker's rounding,
  bit ops on `long`, `double` special values.
- **exceptions**: finally ordering with return/throw, exception filters (`when`), rethrow vs
  `throw e`, `using` disposal order, try/catch around `await`.
- **static ctor / init order**: field-init order vs cctor, `beforefieldinit` laziness, cross-type deps.

## Newtonsoft JSON runner

`Transpose.Newtonsoft.Json` needs its binding referenced and its runtime JS prepended. Build it once
(after `./bootstrap.sh` produces the Core ref assembly), passing the base `Transpose.dll` explicitly
as a `--reference` so `tps` doesn't misclassify the ClassPath-outputBy package as a runtime build:

```bash
CORE=$(find artifacts/bootstrap/refs -name Transpose.Core.dll)
dotnet Transpose/Transpose.Compiler/bin/Debug/net10.0/tps.dll \
  --project Packages/Transpose.Newtonsoft.Json/Transpose.Newtonsoft.Json.csproj -c Debug --emit-package \
  -o Packages/Transpose.Newtonsoft.Json/bin/Debug/netstandard2.0/Transpose.Newtonsoft.Json.dll \
  --reference "$TRANSPOSE_DLL_PATH" --reference "$CORE"
dotnet /tmp/jsdumper/bin/Debug/net10.0/jsdumper.dll \
  Packages/Transpose.Newtonsoft.Json/bin/Debug/netstandard2.0/Transpose.Newtonsoft.Json.dll /tmp/nsjjs
```

Then `dotnet /tmp/jsonrunner/bin/Debug/net10.0/jsonrunner.dll /tmp/snippet.cs --run 2>&1 | node`
(the runner prepends `/tmp/nsjjs/newtonsoft.json.js` + `generated.meta.js`).

## Workflow per finding

Investigate a batch and **record findings first** (repro + emitted JS + native/h5 comparison + root
cause `file:line`) in a temp folder, then fix serially. For each fix: reproduce minimally → confirm
the emitted JS is wrong → fix the emitter/runtime → add a regression test → run the FULL suite → commit.
See **transpose-runtime-and-bcl** for the rebuild/test/commit mechanics.

Consider spawning parallel investigation agents, one per checklist area, each writing a findings file —
then fix serially. (Agents share the pre-built runners; tell them NOT to build anything, only invoke
the runner DLLs, to avoid races.)

## Working efficiently

- Bias toward areas the corpus actually uses heavily and areas not yet deeply covered by prior
  sessions — check the repo's recent git log / any `PORT_PLAN`/findings notes for what's already fixed,
  and don't re-investigate those.
- A missing BCL API surfaces as a *translation failure*, not a silent wrong result — that's a feature
  gap, not the behavioural-divergence this audit targets (unless the corpus depends on it).
