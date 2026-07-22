# Findings

## Transpose-vs-h5 compiler audit

Audit of the Roslyn translator against the proven-correct h5 baseline, using the Curiosity
front-end (`Curiosity.FrontEnd*` 26.7.2802) as the corpus. A divergence counts as a bug when the
emitted JS behaves differently from BOTH native .NET AND h5 (cosmetic JS differences are fine);
native-only divergences are also fixed where practical.

### Fixed this session

- **Reflection metadata built attributes with the wrong ctor overload.** `at:[...]` emitted a bare
  `new T(args)` (primary ctor), dropping the arguments for any attribute applied through a
  non-primary overload. Broke `[JsonProperty("wireName")]` (67 uses in the corpus) — Newtonsoft
  (de)serialized under the C# member names. Now emits `new T.$ctorN(args)` like h5.
- **`DEBUG`/`TRACE` not defined per `-c` configuration.** `tps` never added the SDK's implicit
  symbols, so `#if DEBUG` always took the `#else` branch in a Debug build. Now defines `TRACE`
  (always) and `DEBUG` (Debug config).
- **long/ulong/decimal operators.** Compound assignment (`+= /= %= <<=` …), `++`/`--`, and a
  non-decimal integer on the LEFT of a decimal operator all ran native JS operators on the boxed
  Int64/Decimal (string-concat, integer division, precision loss, crashes). Rebuilt through the
  runtime type's methods; `long op decimal` now promotes to decimal.
- **`bool.ToString()`** returned "true"/"false" instead of .NET "True"/"False" (both the direct
  call and a boxed bool via `Transpose.toString`).
- **String-interpolation alignment** `{x,N}` / `{x,-N}` was silently dropped; now padded via
  `System.String.alignString`.
- **Default-value init.** Static non-primitive-struct fields/auto-props with no initializer stayed
  `null` (member access threw); `default(T)` for a generic-*method* type parameter emitted `null`
  regardless of the value-type argument. Both now use `Transpose.getDefaultValue`.
- **`params` passing.** An array passed to `params object[]` was double-wrapped (element-type test:
  `object[]` → `object` exists); an optional parameter omitted before a `params` array was dropped
  (the array shifted into its slot). Now tests convertibility to the array type and emits `void 0`
  for skipped optionals.
- **for-loop closures.** A `for` header variable was emitted with `let` (fresh per-iteration ES6
  binding); C#'s single shared variable means closures see the final value. Now `var`, like h5.
- **`foreach` disposal.** Emitted a bare `while (moveNext())` with no try/finally+Dispose, so an
  iterator's `finally` never ran on `break`/`return`/throw. Now wraps in try/finally and disposes
  the enumerator (the shim's enumerator wrapper forwards `dispose`, and iterator generators run
  their `finally` via `it.return()`).
- **`foreach` over null** threw a raw JS `TypeError`; now throws `System.NullReferenceException`.

### Known open (not fixed)

- **`ValueTuple` literals emit plain `{Item1,Item2}` objects** (no `ValueTuple$N` prototype), so
  `tuple.ToString()` → "[object Object]" (native "(1, x)") and `tuple.Equals()`/`.GetHashCode()`
  throw. `==`, deconstruction, and dict/hashset keys work via structural helpers. h5 builds
  `new (System.ValueTuple$N(types)).$ctor1(vals)`. Deferred: changing the representation touches
  deconstruction/patterns/LINQ/JSON broadly — needs its own focused change + test pass.
- **Reordered named arguments** are emitted inline in parameter order, so side-effecting argument
  expressions evaluate in parameter order, not C# source order (values are always correct). Fixing
  it needs source-order temp evaluation (an IIFE) around the call. Niche; deferred.
- **`enum.ToString(format)`** (e.g. `.ToString("D")`) throws — the `{this:type}` template emits
  `Transpose.getType(value)`, which returns Int32 for a numeric enum value. Not exercised by the
  corpus. `[Enum(Emit.Value)]` enums (e.g. `DateTimeKind`) intentionally stringify to their number
  when boxed — that is an h5-compatible contract, NOT a bug.
- **`[Init]`/`[Script]`** attributes are on the unsupported-feature allow-list but have no emitter
  handler (`[Init]` methods never run; `[Script]` extern bodies are dropped). 0 corpus usage.
- **`Console.Write(object)`** of a boxed bool prints "true" (uses `value.toString()`), and
  `Console.Write` appends a newline per call under Node (`console.log` semantics — shared with h5).

## Integers_ArithmeticAndBitwise Failure

The test `Integers_ArithmeticAndBitwise` failed because `Console.WriteLine` with a `long` argument prints the type name `System.Int64` instead of the numeric value in H5.

**Roslyn Output:**
```
...
2469135780246
...
```

**H5 Output:**
```
...
System.Int64
...
```

## Enums Failure

The test `Enums` failed because `Console.WriteLine(enumValue)` prints the underlying integer value instead of the string representation (name), and `[Flags]` enums are not formatted as comma-separated strings.

**Roslyn Output:**
```
Green
2
Read, Write
True
False
```

**H5 Output:**
```
2
2
3
True
False
```

## C# 6.0 Exception Filters Failure (FIXED)

The `ExceptionFilters` test in `CSharp60Tests.cs` was failing due to improper casting in the rewritten filter expression.

### Fix
The `SharpSixRewriter` was generating `(Exception)ex.Message`, which was interpreted as `(Exception)(ex.Message)` due to operator precedence, resulting in `H5.cast(ex.Message, Exception)`. This caused the filter to fail as it tried to cast a string to an Exception. The fix involved wrapping the cast expression in parentheses to ensure `((Exception)ex).Message` is generated.

### Status
The test `ExceptionFilters_Failing` has been renamed to `ExceptionFilters_PropertyCheck` and now passes.

## Synchronous Local Functions inside Async Lambdas with Outer Local Functions

The H5 compiler fails to generate valid JavaScript when a **synchronous** local function is defined inside an **async lambda**, AND there is another local function defined in the outer scope (whether hoisted or not).

The generated JavaScript produces a `SyntaxError: Unexpected identifier 'LocalName'`, suggesting that the inner local function is not being emitted correctly or its scope is being mishandled during the async state machine generation or closure lifting.

### Minimal Failing Case
```csharp
public static async Task Main()
{
    Func<Task> lambda = async () =>
    {
        void InnerLocal() { } // Synchronous local function inside async lambda
        InnerLocal();
        await Task.Delay(1);
    };
    await lambda();

    void OuterLocal() { } // Presence of this outer local function triggers the bug
}
```

### Minimal Passing Case
Removing `OuterLocal` makes the test pass.
```csharp
public static async Task Main()
{
    Func<Task> lambda = async () =>
    {
        void InnerLocal() { }
        InnerLocal();
        await Task.Delay(1);
    };
    await lambda();
}
```

### Affected Test Cases
- `H5.Compiler.IntegrationTests.NestedFunctionsTests.ComplexNestingAndHoisting` (Ignored)
- `H5.Compiler.IntegrationTests.NestedFunctionsTests.SyncLocalFunctionInsideAsyncLambda_WithOuterLocalFunction` (Ignored)
