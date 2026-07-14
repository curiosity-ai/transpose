# Ported H5 integration tests — status

All **340** existing H5 integration test methods (from `Tests/H5.Compiler.IntegrationTests`)
are ported here into the `H5.Translator.Roslyn.Tests.Ported` namespace, running the
**new Roslyn translator** against the **real h5.js runtime** (extracted from H5.dll) and
diffing output against native .NET — the same contract as the original suite.

## Current results

- **~218 passing**, ~105 failing, **17 skipped** (`WebApiTests` — need the h5.core browser/DOM
  bindings + `Script.Write` JS-interop, out of scope for a runtime-only harness).

Fixed since the re-target: LINQ/extension templates, enum values/ToString, relative external
templates, non-generic BCL `new`, local functions, `throw`/`checked`, user
`ToString`/`Equals`/`GetHashCode` override naming, external base-ctor naming, user indexers
(`getItem`/`setItem`), deconstruction, records (value semantics/`with`/`Deconstruct`), struct
`$clone`/`getDefaultValue`, `nameof`, and out/ref args inside `[Template]` calls.

The translator emits H5-runtime-format code (`H5.assembly` + `H5.define`) and drives BCL
interop through H5's `[Template]`/`[Name]`/`[External]`/`[Convention]` attributes read from
H5.dll, so it composes with the real runtime.

## Remaining failure categories (long tail)

1. **H5 member-naming subsystem (largest bucket).** h5's JS member names are produced by a
   specific system that the current heuristic (library methods → camelCase) only partially
   matches. Verified against H5.dll / h5.js:
   - **`[Convention(Notation, Member, …)]`** on a type controls casing per member-kind. e.g.
     `StringBuilder`/`Console`/`Math` carry `Convention(Member = Method|Field, Notation = CamelCase)`
     → methods & fields camelCase, **properties preserved**.
   - **No `[Convention]` ⇒ preserve** (PascalCase): `System.Random.Next` stays `Next`.
   - **Interface-implementing methods take the (camelCased) interface member name** even without
     a type convention: `List<T>.Add` → `add` (implements `ICollection<T>.Add`) but the
     List-specific `AddRange` stays `AddRange`.
   - **Overload suffixes**: overloaded members get `$1`, `$2`… (`Next`, `Next$1`, `Next$2`),
     ordered by H5's `OverloadsCollection`.
   - **Property accessors** vary: `List.Count` is a JS property `.Count`, but `StringBuilder.Length`
     is `getLength()`/`setLength()` methods.
   **Implemented so far** (net +9): a `[Convention]` reader driving library **method** naming —
   convention notation, interface-member-inherited camelCase, `[External]` camelCase, else
   preserve. This alone fixed a batch (StringBuilder/Console/Math methods, List interface methods,
   preserve-style types).

   **Still needed** (measured to regress when approximated, so deferred until done faithfully):
   - **Overload suffixes for library methods.** h5 names overloads `Next`/`Next$1`/`Next$2` via its
     `OverloadsCollection` ordering. A naive (param-count, signature) ordering produces the wrong
     suffix for many BCL methods and regressed the suite (218→185), so it must reproduce H5's exact
     ordering. (Source-method overload suffixes are also needed to avoid JS collisions, but must not
     be applied to virtual/interface overrides — a blanket version regressed 218→217.)
   - **Property-accessor representation.** Some library properties are JS properties (`List.Count`
     → `.Count`) and some are accessor methods (`StringBuilder.Length` → `getLength()`/`setLength()`).
     The trigger is finer than "type has a `[Convention]`" (that blanket rule regressed 218→198);
     it needs H5's exact property-emission condition.
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
