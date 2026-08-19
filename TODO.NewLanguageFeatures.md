# Language feature status (C# 7.x – 14.0)

Every entry below was **verified by running it**, not by reading the emitter: each snippet was
translated with the `emitrunner` from the **`transpose-debugging`** skill, executed on Node, and — for
anything whose result was not self-evident — diffed against the same snippet run natively on .NET.

- `[x]` — compiles and produces the same result as native .NET.
- `[ ]` — refused at compile time. The build fails with a diagnostic, so it cannot reach production
  unnoticed. The entry records the exact diagnostic and what would have to change.
- `[!]` — **compiles and is silently wrong.** No error, no warning; the emitted JavaScript just does
  something other than what .NET does. These are the dangerous ones and are listed together below.

Last verified: **2026-08-19**, dev tree at `32ca3c3`. To re-verify one line, write the snippet to
`/tmp` and run it through the loop in `.claude/skills/transpose-debugging/SKILL.md` — that is how
every status here was established, and it takes about a minute per feature.

> Anything marked *deliberate* is a browser-incompatible construct that `UnsupportedFeatureScanner`
> rejects on purpose. Those are **not** pending work; see [Deliberate non-goals](#deliberate-non-goals).

## Start here: the silent divergences

Four constructs compile clean and then misbehave at runtime. Nothing in the test suite or a user's
build output points at them, so they cost debugging time out of all proportion to their size:

| Construct | Native .NET | Transpose |
| --- | --- | --- |
| `[CallerMemberName]` / `[CallerLineNumber]` / `[CallerFilePath]` / `[CallerArgumentExpression]` | `[Main:8] hello`, `expr=<1 > 2>` | `[:0] hello`, `expr=<>` |
| Positional pattern over a hand-written `Deconstruct` | `True` | `False` |
| Partial constructor (C# 14) | body runs | body never runs |
| User-defined `operator +=` (C# 14) | `V=5` | `V=` |

Then, in rough order of what a real application hits: **default interface methods**, **async streams**
(`await foreach`) and **`await using`**, **`foreach` over a `Span<T>`**, and **`goto case`**.

## C# 7.0 / 7.1 / 7.2

- [x] **Ref Returns and Locals**: `ref int Method()`, `ref var x = ref y;`. Verified: writing through
      the returned `ref` updates the underlying array element.

## C# 7.3

- [x] **In Method Overload Resolution**: `M(in a)` picks the `in` overload, `M(a)` the by-value one.
- [x] **Delegate Constraint**: `where T : Delegate`.
- [x] **Enum Constraint**: `where T : Enum`.
- [ ] **Unmanaged Constraint**: `where T : unmanaged`. Fails with *"Predefined type
      `System.Runtime.InteropServices.UnmanagedType` is not defined or imported"* — Roslyn needs that
      enum to bind the constraint. The namespace itself is well populated (`CharSet`, `LayoutKind`,
      `InAttribute`, … under `shared/System/Runtime/InteropServices/`); only `UnmanagedType` is absent,
      so dropping the enum in beside `CharSet.cs` is likely the whole fix — nothing about the
      constraint needs emitting. (Priority: Not Important)
- [ ] **Fixed Sized Buffers**: requires `unsafe`. *Deliberate.*

## C# 8.0

- [x] **Readonly Members**: `public readonly int Method()`.
- [x] **Switch Expressions**: `x switch { ... }`.
- [x] **Using Declarations**: `using var x = ...`.
- [x] **Disposable Ref Structs**: `ref struct` with `Dispose` — `using` calls it.
- [x] **Nullable Reference Types**: `string?`, `notnull` constraint.
- [x] **Indices and Ranges**: `^1`, `1..5`.
- [x] **Null-Coalescing Assignment**: `x ??= y`.
- [x] **Pattern Matching Enhancements**
    - [x] **Property Patterns**: `{ P: 1 }`.
    - [x] **Tuple Patterns**: `(1, 2)`.
    - [!] **Positional Patterns**: `Deconstruct` based. **Silently wrong.** `x is P(1, 2)` against a
          type with a hand-written `Deconstruct(out int, out int)` evaluates to `False` where native
          .NET says `True`; records and tuples are fine. A pattern test is emitted as a single JS
          expression, so it reads `Item1`/`Item2` rather than calling `Deconstruct` with out-holders
          (see `PositionalPatternMemberNames`). The honest fixes are to emit the test through an IIFE
          when the type has a user `Deconstruct`, or to reject it — a wrong `False` is worse than
          either. (Priority: High)
- [ ] **Default Interface Methods**: refused with *"Target runtime doesn't support default interface
      implementation"*, because the BCL declares no
      `System.Runtime.CompilerServices.RuntimeFeature`. **Opening that gate is necessary but not
      sufficient**: adding `RuntimeFeature.DefaultImplementationsOfInterfaces` and rebuilding the
      runtime makes the snippet compile and then fail at runtime with
      `TypeError: i.I$Twice is not a function` — the default body reaches the reflection metadata but
      is emitted nowhere. The emitter must either copy default bodies onto each implementer or emit
      them on the interface object and resolve through it. (Priority: High)
- [ ] **Async Streams**: `await foreach`, `IAsyncEnumerable<T>`. The BCL declares neither
      `IAsyncEnumerable<T>` nor `IAsyncEnumerator<T>`, so the iterator method fails to bind before the
      emitter is ever consulted. Note `AsyncIteratorStateMachineAttribute` is already present, so this
      was started. Needs the BCL interfaces, then async-iterator emit. (Priority: High)
- [ ] **`await using` / `IAsyncDisposable`**: same shape — `IAsyncDisposable` is absent from the BCL.
      Worth doing together with async streams. (Priority: High)
- [ ] **Unmanaged Constructed Types**: `struct S<T> where T : unmanaged`. Blocked by the same missing
      `UnmanagedType` as the constraint above. (Priority: Not Important)
- [ ] **StackAlloc in Nested Expressions**: `stackalloc` is rejected outright. *Deliberate.*

## C# 9.0

- [x] **Records**: including `with` expressions, value equality and the synthesized `ToString`
      (`Person { Name = a, Age = 1 }`).
- [x] **Init Only Setters**: `public int X { get; init; }`.
- [x] **Pattern Matching Enhancements**: type, parenthesized, `and`, `or`, `not`, relational.
- [x] **Target-Typed New**: `List<int> l = new();`.
- [x] **Static Anonymous Functions**: `static x => x`.
- [x] **Target-Typed Conditional**: `flag ? 1 : 1.5` infers `double`.
- [x] **Covariant Return Types**: override with a derived return type.
- [x] **Extension GetEnumerator**: `foreach` over a type with an extension `GetEnumerator`.
- [x] **Lambda Discard Parameters**: `(_, _) => 0`.
- [x] **Attributes on Local Functions**: `[Attr] void Local()`.
- [x] **Module Initializers**: `[ModuleInitializer]` runs before `Main`.
- [ ] **Top-Level Statements**: rejected with a clear message pointing at an explicit `Main`.
      *Deliberate.*
- [ ] **Native Sized Integers**: `nint`, `nuint`. *Deliberate.*

## C# 10.0

- [x] **Record Structs**: `public record struct Point(int X, int Y);`.
- [x] **Struct Improvements**: parameterless constructors and field initializers.
- [x] **File-Scoped Namespaces**: `namespace MyNamespace;`.
- [x] **Extended Property Patterns**: `{ Inner.X: 2 }`.
- [x] **Lambda Improvements**: attributes and explicit return types — `[Obsolete] int (int x) => x * 3`.
- [x] **Sealed ToString in Records**: `sealed override string ToString()`.
- [!] **Caller info attributes** — **silently wrong, and the widest-reach item in this file.**
      `[CallerMemberName]`, `[CallerLineNumber]`, `[CallerFilePath]` and `[CallerArgumentExpression]`
      are all ignored: the call simply omits the argument, so the parameter's own declared default
      stands and a logging helper reports `[:0]` instead of `[Main:8]`, with `ArgumentExpression`
      coming through as `""`. All four
      attribute classes already exist in the BCL — the gap is entirely in the emitter, which contains
      no reference to any of them. The fix belongs in the omitted-optional-argument handling of a
      call (`Emitter.Expressions2.cs:821`-`880`), which today either leaves a trailing omitted
      argument off entirely or passes `void 0` and lets the callee apply its own declared default —
      exactly why the values come through empty. A caller-info parameter has to be *substituted at the
      call site* instead: the enclosing member's name, the call's line, its file path, or the argument's
      source text. This shape is everywhere —
      `OnPropertyChanged([CallerMemberName] string p = null)`, guard helpers, every logging wrapper.
      (Priority: High)
- [ ] **AsyncMethodBuilder Attribute**: on methods. Not probed — plain `async Task` works, and a
      custom builder has no plausible use here. (Priority: Not Important)

## C# 11.0

- [x] **Raw String Literals**: `"""..."""`.
- [x] **Generic Attributes**: `class Attr<T> : Attribute`, applied as `[MyAttr<int>]`.
- [x] **List Patterns**: `a is [1, .., 3]`.
- [x] **File-Local Types**: `file class Local`.
- [x] **Required Members**: `public required int X { get; set; }`.
- [x] **Extended Nameof**: `nameof(p)` naming a parameter inside an attribute.
- [x] **Scoped Ref**: `scoped ref` parameters and locals.
- [ ] **Generic Math** (`static abstract` interface members): refused with *"Target runtime doesn't
      support static abstract members in interfaces"* — the same missing `RuntimeFeature` as default
      interface methods. **And again the gate is not the whole story**: with
      `RuntimeFeature.VirtualStaticsInInterfaces` added, `T.Zero` under a
      `where T : IZero<T>` constraint emits `IZero$1(T).IZero$1$Zero`, i.e. it reads the *interface's*
      own slot and yields `undefined` instead of dispatching to the type argument's `S.Zero`.
      Constrained static dispatch has to be modelled in the emitter. This also gates the
      `System.Numerics` generic-math interfaces (`INumber<T>` and friends), none of which the BCL
      declares yet. (Priority: Low as syntax, High if generic math is wanted)
- [ ] **UTF-8 String Literals**: `"text"u8` → *"not supported yet: Utf8StringLiteralExpression"*.
      (Priority: Low)
- [ ] **Ref Fields**: `ref int x` in a `ref struct` → *"Target runtime doesn't support ref fields"*.
      Needs `RuntimeFeature.ByRefFields` plus emit for a by-ref field, which JavaScript has no direct
      equivalent for. (Priority: Low)
- [ ] **Pattern Match Span\<char\>**: against a constant string. *Deliberate* — the scanner rejects
      span pattern matching explicitly.
- [ ] **Numeric IntPtr**: `nint` as an alias for `System.IntPtr`. *Deliberate.*

## C# 12.0

- [x] **Primary Constructors**: `class C(int x) { ... }`.
- [x] **Collection Expressions**: `[1, 2, 3]`, including the spread element `[..a, 4]`.
- [x] **Optional Params in Lambdas**: `(int x = 1) => x`.
- [x] **Ref Readonly Parameters**: `ref readonly int`.
- [x] **Experimental Attribute**: `[Experimental("ID")]`.
- [ ] **Inline Arrays**: `[InlineArray(10)]`. *Deliberate.*

## C# 13.0

- [x] **Params Collections**: `params List<int>` works.
- [x] **Escape Sequence `\e`**: ESC character.
- [ ] **Params Collections over a span**: `params ReadOnlySpan<int>` fails — *"`ReadOnlySpan<int>` does
      not contain a public instance or extension definition for `GetEnumerator`"*. The BCL's `Span<T>`
      and `ReadOnlySpan<T>` support indexing and `Length` but expose no enumerator, so **plain
      `foreach (var c in span)` does not compile either** — see
      [Older constructs](#older-constructs-still-missing). Adding `GetEnumerator` to both span types
      fixes this entry and that one together. (Priority: Medium)
- [!] **Implicit Index Access**: `^1` in an object initializer. `new C { A = { [^1] = 9 } }` compiles
      and emits `$o.A.setItem(System.Index.FromEnd(1), 9)` — an `Index` object where the array setter
      expects an integer offset — so it throws a `TypeError` at runtime. The offset has to be resolved
      against the target's length at the assignment site. (Priority: Medium)
- [ ] **Lock Object**: `System.Threading.Lock`. *Deliberate* — denied as a threading primitive. Note
      `lock (someObject)` still compiles and emits the body, since the runtime is single-threaded.

## C# 14.0

- [x] **`field` Keyword**: `public int X { get => field; set => field = value * 2; }`.
- [x] **Null-Conditional Assignment**: `c?.X = 5` — skips the assignment when the receiver is null.
- [!] **Partial Constructors**: **silently a no-op.** A `public partial C();` declaration plus its
      implementing part compiles, and the implementing body never runs — `new C()` prints nothing
      where native .NET prints `ctor`. The emitter picks up the declaring part and drops the part
      carrying the body. (Priority: Medium)
- [!] **User-Defined Compound Assignment**: **silently a no-op.** `public void operator +=(int n)` is
      never called: `c += 5` leaves the field `undefined` where native .NET gives `5`. The emitter
      falls back to a plain JS `+=` on the object. (Priority: Medium)
- [ ] **Extension Members** (extension blocks): `extension(string s) { ... }` → *"not supported yet:
      Extension members (C# 14 extension blocks)"*, reported from `Emitter.Types.cs:30`. Classic
      `this`-parameter extension methods are unaffected. (Priority: Medium)

## Older constructs still missing

Not "new language features", but the same kind of gap and worth tracking here:

- [ ] **`goto case` / `goto default`**: → *"not supported yet: goto"*. A plain `goto label` **does**
      work — it lowers to a state machine — but `EmitGoto` (`Emitter.Statements.cs:307`) only handles
      `SyntaxKind.GotoStatement`, so the two switch forms fall through to `Unsupported`. Common in
      ported parsers and state machines. (Priority: Medium)
- [ ] **`foreach` over `Span<T>` / `ReadOnlySpan<T>`**: no `GetEnumerator` on the BCL span types.
      Indexing and `Length` work, so this is a small BCL addition that also unblocks
      `params ReadOnlySpan<T>` above. (Priority: Medium)

## Deliberate non-goals

These are rejected on purpose, with a specific diagnostic, because they cannot work in a browser.
They are listed so nobody re-opens them as bugs: pointers and `unsafe`, fixed-size buffers,
`stackalloc` (including nested), `nint`/`nuint` and numeric `IntPtr`, `checked` arithmetic, span
pattern matching, top-level statements, global usings, inline arrays, P/Invoke, and
`System.Threading.Lock`. See `UnsupportedFeatureScanner` for the full set and the exact messages.

## Prerequisites shared across the entries above

Several unrelated-looking features are blocked by the same few missing BCL declarations. Adding these
does not implement anything on its own, but nothing above can proceed without them:

| Missing from the BCL | Unblocks |
| --- | --- |
| `System.Runtime.CompilerServices.RuntimeFeature` (`DefaultImplementationsOfInterfaces`, `VirtualStaticsInInterfaces`, `ByRefFields`) | default interface methods, static abstract members / generic math, ref fields |
| `IAsyncEnumerable<T>`, `IAsyncEnumerator<T>`, `IAsyncDisposable` | async streams, `await using` |
| `GetEnumerator` on `Span<T>` / `ReadOnlySpan<T>` | `foreach` over a span, `params ReadOnlySpan<T>` |
| `System.Runtime.InteropServices.UnmanagedType` | `where T : unmanaged`, unmanaged constructed types |

Wider BCL gaps that are *not* language features — `Lazy<T>`, `Memory<T>`, `DateOnly`/`TimeOnly`,
`SortedDictionary`, `PriorityQueue`, `System.Collections.Concurrent`/`.Immutable`, `System.Numerics`,
`SemaphoreSlim`, `IProgress<T>`, `GC` — are out of scope for this file. `CLAUDE.md` §"Known remaining
work" covers the compiler and tooling side.
