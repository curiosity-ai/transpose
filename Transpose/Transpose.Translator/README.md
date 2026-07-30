# H5.Translator.Roslyn

A clean-room, **Roslyn-only** C# → JavaScript translator for H5. Unlike the legacy
pipeline (`H5/Compiler/Translator`), it uses **no NRefactory** and **no `SharpSixRewriter`
lowering pass**: it compiles and parses with Roslyn, walks the syntax tree guided by
the semantic model, and emits JavaScript directly.

See [`../../../H5.Translator.Roslyn.PORT_PLAN.md`](../../../H5.Translator.Roslyn.PORT_PLAN.md)
for the full design and the feature-by-feature roadmap.

## Pipeline

```
sources ──► CompilationBuilder ──► CSharpCompilation + SemanticModel
                                         │
                                         ├─► Roslyn errors ──────────► fail
                                         ├─► UnsupportedFeatureScanner ► fail (browser-incompatible)
                                         └─► Emitter (syntax walk) ────► JavaScript
                                                                         (+ embedded runtime)
```

Entry point: `RoslynTranslator.Translate(source)` → `TranslationResult { Javascript, Diagnostics, Success }`.

## Layout

| File / folder | Responsibility |
|---|---|
| `Compilation/RoslynTranslator.cs` | Public entry point; orchestrates the pipeline. |
| `Compilation/CompilationBuilder.cs` | Builds the `CSharpCompilation` (C# Latest, ref assemblies). |
| `Support/UnsupportedFeatureScanner.cs` | Reports browser-incompatible features as errors (H5R0001). |
| `Support/NameMangler.cs` | C# symbol → safe JS name; overload disambiguation. |
| `Emit/Emitter*.cs` | Syntax-tree walking emitter (types, members, statements, expressions). |
| `Emit/JsWriter.cs` | Indentation-aware output writer. |
| `Runtime/h5roslyn.runtime.js` | Embedded minimal JS runtime (Console, formatting, helpers). |

## Currently implemented

- Classes / structs, inheritance (`virtual`/`override`/`abstract`), interfaces (structural).
- Instance & static fields, auto- and full properties, indexers, methods, overloads.
- Constructors: overloads, `: this(...)` / `: base(...)` chaining, field initializers, static ctors.
- Statements: locals, `if/else`, `for`, `foreach`, `while`, `do`, `switch` (constant + pattern),
  `try/catch/finally`, `using`, `throw`, `break`/`continue`/`return`, `lock` (no-op body), local functions.
- Expressions: literals, arithmetic/relational/logical/bitwise ops, integer division, string concat &
  interpolation, ternary, null-coalescing, casts (numeric truncation), `is`/`as`, object & collection
  initializers, arrays, element access, lambdas / anonymous methods, `?.`, `await`, method groups, enums,
  `ref`/`out` parameters (holder objects), `params`, named/optional arguments.
- Modern C#: pattern matching (type/constant/relational/logical/property/positional), switch expressions,
  tuples + deconstruction, records — see below.
- Collections: `List<T>`, `Dictionary<K,V>`, `HashSet<T>`, arrays; LINQ-to-objects; iterators (`yield`).
- BCL surface: `Console`, `String` (+ static), `StringBuilder`, `Math`/`MathF`, `Convert`, numeric parsing,
  `Char`, `Task`/`Task<T>` (→ Promise), `TaskCompletionSource`, exceptions with `Message`/`InnerException`.
- Async/await → native JS `async`/`await`; async `Main` bootstrap.
- Unsupported-feature reporting: pointers, `unsafe`, `fixed`, `stackalloc`, P/Invoke, File I/O, sockets,
  threading primitives (Task-based async is allowed).

## Records

`record`, `record class`, `record struct` and `readonly record struct` are all supported, in the
one-line positional form and with a body, at any depth of a record inheritance chain. What the emitter
synthesizes (`Emitter.ValueTypes.cs`, `AddRecordMethodEntries` + `TryEmitRecordCtors`) mirrors C#:

| Member | Emitted as | Over which members |
| --- | --- | --- |
| `PrintMembers(StringBuilder)` | `PrintMembers` | the record's non-static **public** fields and **public readable** properties, chaining to the base record's `PrintMembers` first |
| `ToString()` | `toString` | `"Name { " + PrintMembers + " }"` — via the real `PrintMembers`, so an override participates |
| `Equals(object)` | `equals` | delegates to the typed `Equals` after a type check, as C#'s `Equals(obj as T)` does |
| `Equals(T)` | `equalsT` | every instance **field** slot, base record first — private fields and auto-property backing fields included, computed properties excluded |
| `GetHashCode()` | `getHashCode` | the same slots |
| `Deconstruct(out …)` | `Deconstruct` | the positional properties |
| primary constructor | `ctor` | parameter defaults, field initializers, the `: Base(…)` call, then the positional stores |

Two consequences worth keeping in mind when changing this code:

- **ToString and equality cover different member sets.** ToString *prints* members (so a computed
  `int Doubled => X * 2` shows up, and a non-public member never does); equality *compares* fields (so a
  public field of the record body participates, and a get-only property that allocates on each read —
  `int[] Cache => new[] { V }` — does not, or two equal records would compare unequal).
- **A declared member replaces the synthesized one.** Each entry above is emitted only when the record
  does not declare it (`ISymbol.IsImplicitlyDeclared`), and every key comes from
  `TransposeNaming.MemberJsName` on the synthesized symbol — which is also what puts a hand-written
  `Deconstruct(out int, out int)` on `Deconstruct$1`, clear of the synthesized one-parameter form.

A record may also carry `[ObjectLiteral]`, which makes it the declaration of a plain JavaScript
object's shape: `new Point(1, 2)` on `[ObjectLiteral] record Point(int X, int Y)` emits `{X: 1, Y: 2}`
(the positional arguments become the literal's members; the synthesized `EqualityContract` does not).

`RecordTests.cs` covers all of the above end to end, diffing against native .NET.

## Object model invariants

Three rules the emitter upholds for every type, each of which a record's synthesized members made
visible first (`ObjectToStringAndInitOrderTests.cs`):

- **Instance field initializers run before the base constructor**, which is C#'s order — so a base
  constructor observes the derived slots already initialized, and the side effects of an initializer are
  sequenced ahead of the base's.
- **A virtual or overriding auto-property has storage of its own** (`IsFieldBackedProperty`): it is
  emitted as a real accessor pair over a per-declaration backing slot (`$P`, and `$P$<Type>` for an
  override) rather than AS the slot named after the property. Sharing one slot collapsed the base's
  field and the override's into a single location, so the base constructor's initializer landed on the
  override's value and no read ever dispatched. A non-virtual auto-property is still the plain slot.
- **A local binding never shadows a type reference.** A type is named by its bare emitted identifier,
  so a parameter, local, `out var`, `foreach`/`catch` variable or query range variable of the same name
  would shadow it (`record RD(int A, int B) : RB(A)` with a base named `B` emitted `B.ctor.call(this, A)`
  inside `function (A, B)`). `EmitClassLike` collects the identifiers a type binds and `TypeRef` routes a
  colliding reference through `Transpose.global`, which nothing can intercept.

`object.ToString()` follows .NET rather than JavaScript, in the runtime (`Transpose.toString`): an
array reports its type name instead of its joined elements, a `DateTime` the culture's general
date/time pattern instead of the JS `Date` string, a `Type` its display name instead of the
constructor's source text, and the fallback for a type with no `ToString` of its own is
`GetType().ToString()` — which names generic arguments plainly (`List`1[System.Int32]`), not
assembly-qualified as `FullName` does.

## Extending

1. Add a language feature by handling its `SyntaxNode` in the relevant `Emitter.*.cs` file.
2. Grow `h5roslyn.runtime.js` only as much as the feature needs (keep it minimal).
3. Add a mirrored test in `Tests/H5.Translator.Roslyn.Tests/` — it diffs JS output against
   native Roslyn execution, so behavior parity is enforced automatically.
4. If a feature can't run in a browser, report it from `UnsupportedFeatureScanner` instead of emitting.
