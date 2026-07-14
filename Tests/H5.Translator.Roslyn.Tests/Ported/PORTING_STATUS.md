# Ported H5 integration tests — status

All **340** existing H5 integration test methods (from `Tests/H5.Compiler.IntegrationTests`)
are ported here into the `H5.Translator.Roslyn.Tests.Ported` namespace, running the
**new Roslyn translator** against the **real h5.js runtime** (extracted from H5.dll) and
diffing output against native .NET — the same contract as the original suite.

## Current results

- **~273 passing**, ~50 failing, **17 skipped** (`WebApiTests` — need the h5.core browser/DOM
  bindings, out of scope for a runtime-only harness).

Fixed since the re-target: LINQ/extension templates, enum values/ToString, `[Flags]` enums,
relative external templates, non-generic/`[External]` BCL `new`, local functions,
`throw`/`checked`, user `ToString`/`Equals`/`GetHashCode` naming, external base-ctor naming,
user indexers (`getItem`/`setItem`) and named indexers (`[Name]`/`[AccessorsIndexer]`),
deconstruction, records, struct `$clone`/`getDefaultValue`, `nameof`, out/ref args in
`[Template]` calls, base instance calls (`.prototype`), covariant returns, catch-clause
ordering, events + multicast delegates, property/indexer setter `[Template]`s, discards
(assignment/`out _`/lambda/deconstruction), nullable `.Value`, named-argument defaults,
optional lambda parameters, 32-bit integer multiply wrap (`Math.imul`), member-level
`[Convention]` (e.g. `KeyValuePair.Key`), `GetType()` (`{this:type}`/`<self>`), null-conditional
element access + property templates, switch-expression pattern variables, `using var`
block-scoped dispose, C#12 collection expressions, C#12 primary constructors (with capture
analysis), C#14 `field` keyword, `[ModuleInitializer]`, `Index`/`Range` on arrays (`^n`,
`a..b`), `H5.Script.Write` raw-JS interop, `[ObjectLiteral]`, universal
`ToString`/`GetHashCode`/`Equals` lowercasing for BCL types, and rejecting top-level
statements / global usings as unsupported.

The translator emits H5-runtime-format code (`H5.assembly` + `H5.define`) and drives BCL
interop through H5's `[Template]`/`[Name]`/`[External]`/`[Convention]` attributes read from
H5.dll, so it composes with the real runtime.

The translator now ports H5's exact `OverloadsCollection` ordering for method overload
suffixes, reads type- and member-level `[Convention]`, honours `[Name]`/`[Template]`/
`[External]`, and emits universal `toString`/`getHashCode`/`equals` names — so most member
naming now matches h5.js.

64-bit integers (`long`/`ulong`) are now emitted as h5.js `System.Int64`/`UInt64` objects
(literals, arithmetic/comparison operators, conversions, and constants such as
`long.MinValue`).

Async constructs are aligned with h5.js's contract: an `async` method/lambda/local
function emits a plain outer function whose body runs in a native `async` IIFE, and the
resulting promise is adapted to an h5.js **Task** via `H5R.fromPromise` (a
`TaskCompletionSource`). So async methods return real Tasks that compose with
`Task.Run`/`Task.WhenAll`/`ContinueWith` and carry faults through the Task (enabling
exception aggregation), while `await x` drives any Task or promise through `H5.toPromise`.

## Remaining failure categories (long tail, ~50)

1. **Hand-written BCL runtime quirks.** A few h5.js types are hand-authored (`// @source X.js`)
   and diverge from their C# metadata, so method names computed from metadata don't match:
   e.g. `Guid.ToString(string)` maps to `format(...)` in h5.js, not `toString$1`. Affects
   `Guid`, `Decimal`, `TimeSpan`, `Regex`, `Version`, `DateTimeOffset`, `CultureInfo`.
   These need per-type name maps rather than the generic overload algorithm.
2. **`async ValueTask` / `goto` across `await`** — `async ValueTask` trips a Roslyn
   task-like-metadata error from the H5 BCL (H5.dll's `ValueTask` lacks the async
   method-builder attribute, unfixable without changing the BCL). `goto` between labels
   that straddle an `await` needs the full step-state-machine lowering that native
   async/await cannot express.
3. **Generic type arguments at runtime** — constructs needing `T` as a runtime value
   (`new T()`, `default(T)`, `typeof(T)`, `Enum.IsDefined<T>`) can emit an undefined `T`.
5. **Reflection metadata** — the `H5.setMetadata` block is not emitted, so richer
   `GetType()` details and `[Enum(Emit.X)]` value modes differ.
6. **Newer/edge C# forms** — C#14 `extension` members, `params List<T>`/`Span<T>`
   (C#13), multi-dimensional-array indexing, `System.Threading.Lock`, `goto`,
   explicit interface implementation, and the `[ObjectLiteral]` Ignore/Initializer modes.
7. **File I/O** — `MemoryStream`/`BinaryWriter` (largely reported unsupported by design).

These are the same kinds of features the legacy emitter handles via its metadata/overload
machinery; porting them incrementally (each with its mirrored test) is the path to parity.
