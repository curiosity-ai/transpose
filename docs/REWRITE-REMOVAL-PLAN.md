# Plan: Removing the pre-translation rewrite pipeline

**Status:** living document — updated as cases are removed.
**Tracking table:** see [§8](#8-progress-tracking).

## 1. Background: why H5 rewrites source before translating

H5 today compiles every project **twice**, with a lowering step in between:

```
 C# source files
   │
   ├─(a)── Roslyn build ───────────────► project .dll  (Translator.Build.cs)
   │        · StackAllocRewriter            (metadata/reflection only)
   │        · CovariantReturnTypeRewriter
   │
   └─(b)── SharpSixRewriter + replacers ─► lowered C# source strings
            (Translator.InspectAssembly.cs → Rewrite())
                     │
                     ▼
            NRefactory (mcs) parser ─► NRefactory AST ─► resolver ─► Emitter ─► JavaScript
            (BuildSyntaxTree / BuildSyntaxTreeForFile)
```

The JS-producing frontend is the **NRefactory 5 / mcs parser** vendored in
`External/NRefactory`. Its parser supports **at most C# 6**
(`LanguageVersion.V_6` in `Parser/mcs/settings.cs`), and the H5 emitter
(`H5/Compiler/Translator/Emitter`) only understands the AST shapes NRefactory
produces for ~C# 5/6 code. Everything newer is therefore *lowered to old C#
source text* by a ~10,300-line Roslyn-based rewrite pipeline
(`H5/Compiler/Translator/Utils/Roslyn/`) before being re-parsed by NRefactory.

This double-frontend architecture is the single largest source of complexity
and bugs in the compiler (see `FINDINGS.md`: nested local functions, exception
filter casts, out-var hoisting collisions, …). Every new C# feature costs a
new lowering, and every lowering must produce *source text* that survives a
second, weaker parser — including smuggling JS through `H5.Script.Write`,
`H5.Script.ToTemp/FromTemp`, and `H5.Ref<T>` closures.

**Goal:** make the translator consume the *original* source (ultimately: the
single Roslyn compilation it already builds for step (a)) so the rewrite step
can be deleted from the compile flow entirely.

## 2. The rewrite-case inventory

Three layers perform rewrites today. The tables below are the complete list of
covered cases (verified against the source, 2026-07).

### 2.1 Pre-build rewrites (`Translator.Build.cs`, run before the Roslyn *assembly* build)

| # | Case | Lowering | Semantic? |
|---|------|----------|-----------|
| B1 | `stackalloc T[n]` / `stackalloc[] {…}` | heap array `new T[n]` / `new[] {…}` | no |
| B2 | C# 9 covariant return types | override return type widened back to base type; casts inserted at all read sites | yes |

### 2.2 Pre-pass replacers (run from `SharpSixRewriter.Rewrite()` before the main visit)

| # | Case | Lowering | Semantic? |
|---|------|----------|-----------|
| P1 | Expression-bodied members (all member kinds) | `=> e` → `{ return e; }` blocks | throw-expr case only |
| P2 | `nameof(...)` | string literal | yes |
| P3 | Discards: `out _`, `_ = e`, `is T _`, tuple `_`, `out var x` hoisting | `_discardN` locals hoisted; `H5.Script.Discard`; `out var` split into declaration + `out x` | yes |
| P4 | Tuple deconstruction `(a,b) = e`, `foreach (var (a,b) in …)` | hoisted locals + explicit `Deconstruct(out a, out b)` calls (instance/extension/`H5.Script.Deconstruct`) | yes |

### 2.3 Main pass (`SharpSixRewriter`, ~5,900 lines)

Grouped by C# version; "into" is the lowered form handed to NRefactory.

| # | Case | Lowered into | Semantic? |
|---|------|--------------|-----------|
| S1 | String interpolation `$"…"` (incl. alignment/format, `FormattableString`) | `string.Format(…)` / `FormattableStringFactory.Create(…)` | yes |
| S2 | `using static` / using-aliases | directives removed; usages fully qualified | yes |
| S3 | Getter-only auto-props + property initializers | synthesized `private set;` + `__Property__Initializer__X` backing fields | yes |
| S4 | Exception filters `catch … when (f)` | single `catch (Exception _e)` + if/else-if chain with `H5.Script.SafeFunc` | yes |
| S5 | Tuples: `(a,b)` expressions, `(int,string)` types, named-field access | `System.ValueTuple<…>` ctor/type; `ItemN` accesses | yes |
| S6 | `is` pattern expressions (all pattern kinds up to C# 11 list patterns) | boolean expressions + hoisted pattern variables (`IsPatternReplacer`) | yes |
| S7 | `case` patterns / `when` clauses in `switch` | `do { if-chains } while(false)` (`SwitchPatternReplacer`) | yes |
| S8 | `ref` locals/returns/params, `ref readonly`, `in` params/args | `H5.Ref<T>` getter/setter closures; `.Value` reads | yes |
| S9 | Local functions | hoisted `Func/Action`/custom-delegate local + lambda assignment (`LocalFunctionReplacer`) | yes |
| S10 | Binary literals / digit separators | plain decimal literals | yes (const value) |
| S11 | `default` literal | `default(T)` | yes |
| S12 | Throw expressions | `((Func<T>)(() => { throw e; }))()` IIFE | yes |
| S13 | `stackalloc` (again, for the JS path) | heap arrays | no |
| S14 | Switch expressions `x switch {…}` | nested ternaries over `is`-patterns + `ToTemp/FromTemp` | yes |
| S15 | Index/Range: `^n`, `a..b`, `a[^n]`, `a[r]`, slices | `System.Index/Range` ctors; `Length-n` arithmetic; `Substring`/`Slice` | yes |
| S16 | `using var x = …;` declarations | wrapped `using (…) { rest-of-block }` | no |
| S17 | Null-coalescing assignment `??=` | `a = (a ?? b)` with `ToTemp/FromTemp` LHS stabilization | yes |
| S18 | Null-conditional `a?.b`, `a?[i]` chains | flattened conditionals `(c1 && c2) ? x.y : null` with temp spilling | yes |
| S19 | `readonly struct` / readonly members | `[H5.Immutable]`; `readonly` stripped | yes |
| S20 | Static lambdas / static local functions | `static` stripped | no |
| S21 | Records & record structs | full class synthesis: props, ctors, `Deconstruct`, `Equals`/`GetHashCode`, `ToString`/`PrintMembers`, `Clone`, operators | yes |
| S22 | Target-typed `new()` | `new Type(…)` | yes |
| S23 | `with` expressions | `H5.Script.CallFor(H5.clone(…), _w => {…})` | yes |
| S24 | `init` accessors | `set` | no |
| S25 | `[ModuleInitializer]` | `[H5.Init(After)]` | yes |
| S26 | Top-level statements | synthesized `Program.Main` (NB: `Translator.Build.cs` *rejects* them today — dead path) | no |
| S27 | `nint`/`nuint` | `int`/`uint` | yes |
| S28 | File-scoped namespaces | braced namespace | no |
| S29 | `[CallerArgumentExpression]` | inserted string-literal arguments | yes |
| S30 | Raw string literals `"""…"""` | regular string literals | no (token) |
| S31 | `required` members | modifier stripped | no |
| S32 | Checked operators (`op_Checked*`) decls + call sites | plain static methods + explicit calls | yes |
| S33 | `params` collections (C# 13) + params-array expansion | `new T[]{…}` (+ collection ctor wrap) | yes |
| S34 | Primary constructors (class/struct) | synthesized ctor + `_ctor_param_*` capture fields; initializer moves | yes |
| S35 | Collection expressions `[a, b, ..c]` | arrays / `new List<T>(…)` / IIFE with `AddRange` for spreads | yes |
| S36 | Default lambda parameters | synthesized private delegate types | yes |
| S37 | Explicit-return-type lambdas | `(Func<…>)(lambda)` casts | no |
| S38 | `System.Threading.Lock` `lock` | `using (l.EnterScope())` | yes |
| S39 | `ValueTask`/`ValueTask<T>` returns | `Task`/`Task<T>` | yes |
| S40 | `[ToAwait]` methods (H5-specific) | `await x.WaitTask(…)` + async-mark propagation | yes |
| S41 | Extension-method `foreach` (`GetEnumerator` ext.) | manual enumerator loop | yes |
| S42 | `private protected` | `protected internal` + `[H5.PrivateProtected]` | no |
| S43 | `is` with non-type RHS | `x.Equals(y)` | yes |
| S44 | Generic-method type-arg re-qualification; reduced extension → static calls | fully-qualified static invocations | yes |
| S45 | Constant initializer folding (incl. `NaN`, `long.MinValue`) | literals / well-known member accesses | yes |
| S46 | Object/collection initializers needing methods/indexers | `H5.Script.CallFor/AsyncCallFor(target, _o => {…})` | yes |
| S47 | Interpolated-/verbatim trivia & removed-using trivia migration | comment preservation bookkeeping | no |

### 2.4 Post-pass replacers (flag-gated, run after the main visit)

| # | Case | Trigger flag |
|---|------|--------------|
| R1 | `MethodImplAttributeRewriter` — strips `[MethodImpl]` | always |
| R2 | `LocalFunctionReplacer` (see S9) | `hasLocalFunctions` |
| R3 | `ChainingAssigmentReplacer` — splits self-referential declaration/assignment chains | `hasChainingAssigment` |
| R4 | `UsingStaticReplacer` — removes `using static`/alias directives (see S2) | `hasStaticUsingOrAliases` |
| R5 | `IsPatternReplacer` (see S6) | `hasIsPattern` |
| R6 | `SwitchPatternReplacer` (see S7) | `hasCasePatternSwitchLabel` |

## 3. Why "just delete it" doesn't work — and what does

Three components downstream of the rewrite all have to understand a construct
for its rewrite to be removable:

1. **Parser** — mcs (`cs-parser.jay`/`cs-tokenizer.cs`) parses at most C# 6.
   Anything with C# 7+ *syntax* (tuple literals, patterns, `switch`
   expressions, records, `^`/`..`, `"""`, `[a, b]`, primary ctors, …) is a
   parse error.
2. **Resolver** — NRefactory's type system/`CSharpAstResolver` implements C# 5
   semantics (plus partial C# 6). Even parseable constructs may not resolve.
3. **Emitter** — `Translator/Emitter` emits JS per NRefactory AST node type; a
   new node kind needs a new emitter block.

That yields exactly three viable removal strategies per case:

- **(A) Redundant** — the construct already round-trips through
  parser+resolver+emitter; the rewrite is legacy. *Action: delete, test.*
- **(B) Native support in the current frontend** — construct is C# 6-or-lower
  syntax (or a trivial lexer/grammar extension), and the emitter can be taught
  to emit it directly. *Action: move logic from source-rewriter into
  emitter, delete rewrite.* This genuinely reduces the pipeline: no re-parse,
  no source-text round-trip, better source maps and error locations.
- **(C) Frontend replacement** — construct cannot reasonably be parsed by mcs.
  Removing these rewrites requires the translator to consume **Roslyn** syntax
  trees + semantic model instead of NRefactory AST + resolver. Since step (a)
  of the build already produces exactly that compilation, the endgame is a
  single-parse architecture.

The honest conclusion of the inventory: **most of the table is category (C)**.
There is no path to "nothing left" that keeps the mcs frontend. The plan
therefore has two horizons:

- **Horizon 1 (this effort, incremental):** pin behavior with tests; delete
  every category-(A) case; migrate category-(B) cases into the emitter;
  quarantine the rest behind an explicit, documented boundary. Each removal
  shrinks the rewriter and is independently shippable.
- **Horizon 2 (the endgame):** port the Inspector/MemberResolver/Emitter from
  NRefactory AST to Roslyn (`Microsoft.CodeAnalysis`) syntax + `ISymbol`,
  reusing the emitter's JS-generation logic. Category-(C) lowerings then
  become either unnecessary (the emitter sees the original nodes) or become
  *AST-level* lowerings inside the emitter — no source-text round-trip, no
  second parse, and `Rewrite()` is deleted from `BuildSyntaxTree`. This is a
  large migration and is deliberately out of scope of the case-by-case phase,
  but every category-(B) migration (emitter learns a construct) is work that
  survives the port.

## 4. Methodology: strangler pattern with per-case kill switches

For each case, in order:

1. **Pin behavior with tests first** (§5). Every variation of the construct —
   simple, nested, async context, generics, side-effect ordering — must have a
   Roslyn-vs-H5 integration test *before* touching the rewriter.
2. **Add a kill switch.** Each case gets a guard so it can be disabled
   without recompiling: environment variable
   `H5_DISABLE_REWRITE=<case-id>[,<case-id>…]` checked in
   `SharpSixRewriter`/`Rewrite()`. This lets us (a) probe empirically what
   breaks, (b) run the full suite both ways during the transition, (c) revert
   instantly in the field.
3. **Probe.** Disable the case, run the targeted tests, catalog exactly which
   downstream component fails (parse / resolve / emit / runtime diff).
4. **Implement native handling** (category B) or **prove redundancy**
   (category A).
5. **Delete** the rewrite code, its flags, and its kill switch. Run the full
   integration suite.
6. **Update the tracking table** (§8) and commit — one case (or one coherent
   group) per commit.

### Tooling

- Starting a source file with `//DEBUG REWRITE` makes the rewriter dump the
  lowered source to `%TEMP%/h5/rewritten/<assembly>/<file>` — the fastest way
  to see exactly what a rewrite case produces.
- A minimal probe (console app referencing `H5.Compiler.Service` +
  `H5.Translator` project refs, calling `CompilationProcessor.CompileAsync`
  with a `CompilationRequest` and printing the dump) compiles a single
  snippet in ~10 s without the Playwright round-trip; invaluable for
  bisecting parse errors in lowered output.
- The rewriter cache is disabled in DEBUG builds (`TryGetFromCache` returns
  false), so probing with locally-built Debug binaries never sees stale
  lowering.

## 5. Test plan

New test folder: `Tests/H5.Compiler.IntegrationTests/RewriteCases/`, one class
per case-group, named `RC_<CaseId>_<Feature>Tests` so a case's tests can be
run with a single `--filter`. Coverage requirements per case:

- **Simple form** — the textbook example.
- **Nested/composed** — the construct inside lambdas, async methods,
  iterators, generic types/methods, and combined with *other* rewrite cases
  (the pipeline's passes interact; e.g. patterns × local functions ×
  interpolation).
- **Side-effect ordering** — single-evaluation guarantees (`??=`, `?.`,
  switch-expression governors, collection-expression spreads).
- **Depth** — recursion depth of the same construct (nested patterns, chained
  `?.`, nested tuples, records within records).

Existing coverage (mapped 2026-07): `Language/CSharp60Tests` …
`CSharp14Tests`, `PatternMatchingStressTests`, etc. already cover many happy
paths — the RewriteCases suite *supplements* with the composition/depth/order
variants and gives each case an addressable test bucket. Known gaps found
during the survey (now to be covered): chained assignments `a=b=c`,
`[MethodImpl]`, caller-info attributes, exception-filter variations,
`H5.Ref` in/ref/out interactions, extension-`GetEnumerator` foreach,
checked operators call sites, `with`-expression nesting, params-collection
edge cases, covariant-return read/write sites, stackalloc expressions.

Full-suite runs are expensive (~13 s/test × 430+ tests); per-case filters are
used during development, and the complete suite runs before each merge.

## 6. Removal order

Ordered easiest→hardest by (parser ∧ resolver ∧ emitter) feasibility, so the
pipeline monotonically shrinks and each step de-risks the next:

**Wave 0 — dead or duplicated code (category A candidates)**
1. S26 top-level statements (dead: rejected earlier in `Translator.Build.cs`)
2. R1 `[MethodImpl]` stripping (probe: Inspector likely ignores unknown
   attributes already; if the emitter chokes, teach the Inspector to skip it —
   that's 5 lines vs. a whole rewriter)
3. S45 constant folding — **found to be inter-case-dependent**: it also
   shields `const` field/property initializers from other lowerings that
   produce non-constant expressions (e.g. constant interpolated strings →
   `string.Format`, folded binary literals). Removable only together with /
   after S1 &amp; S10, or by scoping those lowerings to skip constant contexts.
   Note: B1 (`StackAllocRewriter` in the assembly build) and S13 (stackalloc
   in the rewriter) are *not* duplicates — the two frontends re-read the
   original sources independently, so each path needs its own lowering as
   long as both frontends exist.

**Wave 1 — C# 6 syntax the mcs parser already parses (category B)**
5. S24 `init` → `set` (parser: `init` is an identifier-token accessor — needs
   tokenizer tolerance; emitter treats as setter)
6. S20 static lambda modifier (tokenizer tolerance)
7. S31 `required` modifier (tokenizer tolerance + Inspector ignore)
8. S28 file-scoped namespaces (small grammar addition, purely structural)
9. S2/R4 `using static` + aliases (mcs parses C# 6 `using static`; resolver
   support exists in NRefactory 5.5 lookup — probe)
10. S1 string interpolation (mcs C# 6 parses `$"…"`; emitter gains an
    interpolation block that emits the same `string.Format` call — logic moves
    from rewriter to emitter, dropping the source round-trip)
11. P1 expression-bodied members (mcs C# 6 parses them for methods/props;
    emitter's block-emission already handles bodies — probe what breaks)
12. P2 `nameof` (mcs C# 6; resolver may hand it to us as invocation — emit
    constant string in the emitter)
13. S4 exception filters (mcs C# 6 parses `when`; emitter needs a filter
    lowering at JS level — same if/else chain, but generated in the emitter)
14. S3 auto-property initializers / getter-only autoprops (parseable C# 6;
    Inspector/ConstructorBlock must learn initializer emission)
15. S11 `default` literal → typed default (needs resolver's expected-type;
    emitter can synthesize `getDefaultValue` directly)
16. S42 `private protected` (accessibility is metadata-only for JS; Inspector
    maps it)
17. S30 raw strings (tokenizer-only extension: lex `"""…"""`, produce the
    same string token)
18. S10 binary literals/digit separators (tokenizer-only extension)

**Wave 2 — semantic lowerings better done at AST/emitter level (category B/C boundary)**
19. R3 chained-assignment splitting (probe whether emitter handles it)
20. S16 `using` declarations (statement-level AST transform in emitter)
21. S17 `??=` (emitter has compound-assignment machinery)
22. S37 explicit-return-type lambdas; S36 default lambda params
23. S12 throw expressions (emitter IIFE emission)
24. S18 `?.` chains (emitter-level conditional emission — significant but
    self-contained; kills `ToTemp/FromTemp` for this case)
25. B2 covariant returns (Inspector/emitter: JS is duck-typed — probe whether
    plain emission simply works without the widening + casts)
26. S43/S44/S27/S39/S40/S41 (small semantic adjustments, each self-contained)

**Wave 3 — C# 7+ syntax: blocked on the Roslyn frontend (category C)**
- S5 tuples, P4 deconstruction, P3 discards/out-var
- S6/R5 patterns, S7/R6 switch patterns, S14 switch expressions
- S9/R2 local functions
- S8 ref/in/`H5.Ref`
- S15 index/range, S35 collection expressions, S33 params collections
- S21 records, S34 primary ctors, S23 `with`, S22 target-typed `new`
- S29 caller-argument-expression, S32 checked operators, S38 `Lock`,
  S46 initializer lowering, S25 module initializers
- **Exit criterion for Wave 3** = Horizon 2: translator consumes the Roslyn
  compilation from step (a); `Rewrite()`, the rewriter cache
  (`*.h5.rewriter.cache`), and `Utils/Roslyn/*` lowering files are deleted;
  `BuildSyntaxTree` builds from Roslyn trees.

## 7. Risks & mitigations

- **Behavioral drift**: the rewriter encodes years of edge-case fixes
  (evaluation order, temp spilling, name collisions). → Tests-first (§5),
  side-by-side kill-switch runs, one-case-per-commit.
- **Rewriter-cache staleness during development** → tests clear
  `*.h5.rewriter.cache` (already done in several classes; do it centrally).
- **mcs grammar changes** (`??=`, explicit-return lambdas, `using var`,
  `new()`): `cs-parser.cs` is jay-generated; prefer tokenizer-level changes
  and hand-edits confined to `cs-tokenizer.cs`; avoid regenerating the grammar.
  **Regeneration is NOT faithful** (established 2026-07): jay 0.7 builds cleanly
  (`cc -DSKEL_DIRECTORY=... *.c`, invoke as `jay -c cs-parser.jay < skeleton.cs
  > out.cs`), but the committed `cs-parser.cs` was generated from a *newer/edited*
  grammar than the vendored `cs-parser.jay` — the committed actions use C# 7
  `is`-patterns and contain a fix (`result[n++]` in the token-name helper) that
  the vendored `.jay` lacks, and the output namespace is rewritten
  `Mono.CSharp` → `ICSharpCode.NRefactory.MonoCSharp`. Regenerating from the
  repo `.jay` would silently revert those and churn all 16k lines. Grammar-level
  cases are therefore **blocked** until the exact upstream `.jay`+skeleton are
  recovered, or deferred to Horizon 2 (where the Roslyn frontend obviates them).
  Token-stream rewrites in `cs-tokenizer.cs` remain the safe lever (used for
  S28/S20/S10/S30/S24).
- **Emitter regressions on the h5 base library** (`H5/H5` builds with the
  same compiler): CI builds the base library + runs the suite against the
  locally built `h5.0.0.42.nupkg`.
- **Long tail of user code**: keep `H5_DISABLE_REWRITE` (inverted:
  `H5_FORCE_REWRITE`) for one release after each removal wave.

## 8. Progress tracking

| Case | Wave | Status | Notes |
|------|------|--------|-------|
| S26 top-level statements | 0 | **removed** | dead code — `Translator.BuildAssembly` rejects `GlobalStatementSyntax` before the rewriter runs |
| R1 `[MethodImpl]` strip | 0 | **removed** | frontend+emitter handle the attribute fine; whole replacer deleted (RC_Wave0_Tests.MethodImpl_*) |
| S24 `init` accessors | 1 | **removed** | mcs tokenizer maps contextual `init` → SET; setter-presence checks updated (RC_ModifierStripTests, RC_S3, CSharp9Tests) |
| S10 binary literals / digit separators | 1 | **removed** | mcs tokenizer handles `0b...` (`handle_binary`) and skips `_` separators in decimal/hex/binary lexing; verified by RC_S10_LiteralTests + full language-suite sweep |
| S30 raw string literals | 1 | **removed** | mcs tokenizer lexes `"""…"""` natively (`consume_raw_string`, permissive since Roslyn pre-validates); interpolated raw strings are consumed by S1 and never reach the tokenizer |
| P2 `nameof` | 1 | **removed** | folded natively at three layers: `ResolveVisitor` (expressions; only when `nameof` binds to no member, preserving user-defined nameof methods), `TypeSystemConvertVisitor.ConstantValueBuilder` (attribute args incl. C#11 extended nameof, const initializers, default params), `InvocationBlock` (emits the constant). `NameofReplacer` deleted. |
| S2a `using static` + R4 | 1 | **removed** | native static imports in the frontend: `UsingDeclaration.IsStatic` (parser) → `UsingScope.StaticUsings` → `ResolvedUsingScope.StaticUsings` → `CSharpResolver.LookInCurrentUsingScope` member/nested-type lookup + `GetAllExtensionMethods` inclusion. Directives now pass through to the frontend; `UsingStaticReplacer` (R4) deleted (alias directives are removed by the main visit). |
| S2b using-aliases | 1 | kept (narrowed) | classic aliases resolve natively downstream, but C# 12 alias-any-type targets (tuples, arrays, nullable, ...) are unparseable by mcs — alias usage rewriting stays until Horizon 2 (or a per-target-shape split). |
| S41 extension `foreach` | 2 | **removed** | resolver finds an extension `GetEnumerator` (`ResolveVisitor.TryResolveExtensionGetEnumerator`, guarded on `IsExtensionMethod` so ordinary foreach is untouched); emitter calls it statically (`ForeachBlock.TryEmitExtensionGetEnumerator`, both sync + async paths). |
| S16 `using var` decl | 1 | **blocked (grammar)** | needs a `using` local-declaration-statement grammar rule; parser regen not faithful (see §7). |
| S17 `??=` | 1 | **blocked (grammar)** | needs an `OP_COALESCING_ASSIGN` token + assignment rule; parser regen not faithful (see §7). Current lowering (`a = a ?? b` with temp-stabilized LHS) is correct and stays. |
| S22 target-typed `new()` | 1 | **blocked (grammar)** | `new()`/`new(args)` without a type needs a grammar rule + resolver target-type inference; parser regen not faithful (see §7). |
| S37 explicit-return lambdas | 1 | **blocked (grammar)** | `T (args) => …` needs a lambda-production grammar change; parser regen not faithful (see §7). |
| S27 `nint`/`nuint` | 1 | **removed** | contextual-keyword fallback in `CSharpResolver.LookupSimpleNameOrTypeName` (next to `dynamic`): resolves to int/uint only when nothing else in scope matches. |
| S25 `[ModuleInitializer]` | 1 | **removed** | `Helpers.IsInitAttribute` treats `ModuleInitializerAttribute` as `[H5.Init(After)]` at all four Init-consuming sites (TypeInfo, CaptureAnalyzer, VisitorMethodBlock, ClassBlock); no source rewriting. |
| S39 `ValueTask`→`Task` | 2 | kept (now functional) | the rewrite was dead for `async ValueTask` — Roslyn rejected it (CS1983) because the BCL's ValueTask wasn't task-like. Fixed by adding `[AsyncMethodBuilder]` + `AsyncValueTaskMethodBuilder(/<T>)` to the BCL and allowing the attribute when compiling the h5 assembly itself. `AsyncValueTask` (previously a baseline failure) now passes. **Bootstrap note:** the h5-lib CI pipeline builds the BCL with the *released* compiler; the first release after this merge must ship the compiler before (or retry after) the BCL pipeline. Full S39 removal (native ValueTask emission) stays in Wave 2. |
| S28 file-scoped namespaces | 1 | **removed** | token-level lowering in the mcs tokenizer: the `;` ending `namespace X;` becomes `{` and a matching `}` is injected before EOF (`token()` state machine, snapshot-safe for `peek_token`; `advance()` keeps pulling while a close brace is pending so files without a trailing newline work). |
| S38 `System.Threading.Lock` | 1 | **removed** | the emitter lowers every lock statement to `expression; body` — JS is single-threaded and the old `using(EnterScope())` lowering was a pair of no-ops. |
| S20 static lambdas | 1 | **removed** | the mcs tokenizer drops `static` tokens inside blocks (`parsing_block > 0`), where they can only be anonymous-function modifiers (static local functions are lowered away before this parser runs). |
| S13 stackalloc (rewriter copy) | 3 | blocked | NOT a duplicate of B1 — both frontends re-read original sources; moved to Wave 3 |
| S45 constant folding | 1 | **removed** | NRefactory resolver folds constants itself; constant interpolated strings now fold in S1 (prerequisite), binary/separator literals handled by tokenizer (S10) |
| B2 covariant returns | — | **blocked (probed)** | NOT removable at the assembly-build layer: Roslyn rejects covariant overrides against h5's BCL (CS8830/CS8831 — no `RuntimeFeature.CovariantReturnsOfClasses`). Removal requires adding the runtime feature flag to the h5 BCL *and* verifying the Cecil/NRefactory type system handles covariant override metadata — revisit in Horizon 2 |
| everything else | 1–3 | pending | |

*(Update this table with every commit that removes a case.)*

Baseline note (2026-07): the following tests fail identically on master and on
this branch (pre-existing, not caused by any removal):
`AliasAnyType`, `OutVariables_RepeatedNameInSameScope`,
`unimplemented…NullableReferenceTypes`, `FileLocalTypes`, `GenericMath`,
`Utf8StringLiterals`, `NumericIntPtr`, `RefFields`, `ScopedRef`,
`unimplemented…InlineArrays`, `unimplemented…ImplicitIndexAccess`,
`IntArrayAliasTest`, `ArrayAlias` (C#12 array aliases — S2b lowering bug),
`AsyncValueTask` (ValueTask semantics, S39 — fixed on this branch),
`ExtensionMembers` (C#14 extension blocks were never lowered by any rewrite
case — an upstream coverage gap, they fail at the mcs parser).
