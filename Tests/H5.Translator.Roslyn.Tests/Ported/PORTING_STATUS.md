# Ported H5 integration tests — status

All **340** existing H5 integration test methods (from `Tests/H5.Compiler.IntegrationTests`)
are ported here into the `H5.Translator.Roslyn.Tests.Ported` namespace, running the
**new Roslyn translator** against the **real h5.js runtime** (extracted from H5.dll) and
diffing output against native .NET — the same contract as the original suite.

## Current results

- **~194 passing**, ~129 failing, **17 skipped** (`WebApiTests` — need the h5.core browser/DOM
  bindings + `Script.Write` JS-interop, out of scope for a runtime-only harness).

The translator emits H5-runtime-format code (`H5.assembly` + `H5.define`) and drives BCL
interop through H5's `[Template]`/`[Name]`/`[External]`/`[Convention]` attributes read from
H5.dll, so it composes with the real runtime.

## Remaining failure categories (long tail)

1. **BCL method/constructor naming mismatches** — a subset of runtime members whose h5.js
   name isn't reproduced by the current convention heuristic (e.g. some `Random`/`Guid`/
   `LinkedList`/`Queue`/`DateTimeOffset` members, `TimeSpan` operator helpers). Each is a
   small, localized fix (add the right `[Template]`/name handling or shim helper).
2. **Reflection metadata** — the `H5.setMetadata` block is not emitted, so `GetType()`,
   `typeof` details, `$$fullname`, and enum boxing-based `ToString` in some paths differ.
3. **Generic type arguments at runtime** — H5 threads type parameters as runtime arguments
   (`List$1(T)`); constructs that need `T` as a runtime value (e.g. `default(T)`, `new T[]`,
   `typeof(T)`) can emit an undefined `T`.
4. **A few unlowered syntax forms** — collection expressions (`[1,2,3]`), list patterns,
   range/index (`..`, `^`), `goto`/labels, some `out var` positions.
5. **Async scheduling order** — micro-task ordering can differ from native for
   `Task.WhenAll`-style interleavings.

These are the same kinds of features the legacy emitter handles via its metadata/overload
machinery; porting them incrementally (each with its mirrored test) is the path to parity.
