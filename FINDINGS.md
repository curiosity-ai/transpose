# Findings

## Switch expressions over tuples emitted unparseable `FromTemp<(int a, int b)>` type args (FIXED)

`VisitSwitchExpression` rendered the governing expression's type with
`ToMinimalDisplayString`, so tuple types appeared in C# 7 tuple syntax
(`(int q, int r)`) inside `H5.Script.FromTemp<...>` / `Write<...>` type
arguments — which the NRefactory (mcs) parser cannot read (`Unexpected symbol
'q', expecting '.'`). Fixed by rendering through
`SyntaxHelper.GenerateTypeSyntax`, which lowers tuples to
`System.ValueTuple<...>`. Covered by
`RC_CompositionTests.Patterns_LocalFunctions_Tuples_Combined`.

## Switch expression with `var (x, y)` deconstruction arm left identifiers unbound (FIXED)

`IsPatternReplacer` only handled `SingleVariableDesignationSyntax` in var
patterns; a var *deconstruction* designation (`var (x, y)`) lowered to `true`
without hoisting or assigning the variables, producing
`UnknownIdentifierResolveResult`. Fixed by recursing through
`ParenthesizedVariableDesignationSyntax` (hoisting each element with its tuple
element type, binding via `ItemN`). Covered by
`RC_CompositionTests.SwitchExpr_VarDeconstructionArm` and `_PerElementVarBindings`.

## Derived records: inherited positional properties were redeclared (FIXED)

`VisitRecordDeclaration` synthesized a property for *every* positional
parameter, so `record Dog(string Name, string Breed) : Animal(Name)` declared
its own `Name`, shadowing `Animal.Name`. This broke `with`-expression cloning
(the copy lost `Name`) and derived-record equality. Fixed: parameters whose
name matches a base-type property no longer synthesize a property or a
constructor assignment (matching Roslyn, where the value flows through the
base constructor arguments). Covered by `RC_S21_RecordTests.Records_InheritanceAndWith`.

## Record equality used EqualityComparer<T>.Default in operator== (FIXED)

The synthesized `operator==` called
`EqualityComparer<T>.Default.Equals(left, right)`, which in the H5 runtime
does not dispatch to the synthesized `Equals`. Now lowered to
`ReferenceEquals(left, right) || (!ReferenceEquals(left, null) && left.Equals((object)right))`
(virtual dispatch, so derived equality applies through base-typed references),
and `Equals(T)` gained a `GetType() != other.GetType()` guard approximating
the record EqualityContract.

## Record ToString omitted non-positional members (FIXED)

Synthesized `PrintMembers` printed only positional parameters;
.NET records also print the body's public instance fields and readable
properties in declaration order. `Tagged(string Name) { public int Extra ... }`
printed `Tagged { Name = n }` instead of `Tagged { Name = n, Extra = 1 }`.
Covered by `RC_S21_RecordTests.Records_EqualityHashToStringDeconstruct`.

## String slices with composite bounds computed wrong lengths (FIXED)

The Index/Range lowering built `Substring(start, end - start)` with raw
`SyntaxFactory.BinaryExpression` operands, which never auto-parenthesize:
`s[^5..]` became `s.Substring(s.Length-5, s.Length-s.Length-5)` (length -5 →
empty string). All synthesized subtraction operands are now parenthesized
(`ParenthesizeOperand`). Covered by `RC_S15_IndexRangeTests`.

## Zero-argument expanded call of a params local function crashes at runtime

`LocalFunctionReplacer` lowers `int SumAll(params int[] xs)` to a custom
delegate keeping `params`. Call sites with arguments are wrapped into an array
(`SumAll([1,2,3])` in JS), but a zero-argument expanded call emits `SumAll()`
— `xs` is `undefined` and the body crashes (`Cannot read properties of
undefined`). Repro: `RC_S9_LocalFunctionTests.LocalFunctions_ParamsZeroArgs_MinimalFailing`
([Ignore]d). Likely an emitter gap in empty params expansion for *delegate*
invocations (regular methods handle it).

## Generic local functions are not supported (known limitation)

`LocalFunctionReplacer` lowers local functions to delegate-typed locals, but
no delegate instance can be open-generic, so `T Identity<T>(T v) => v;` calls
fail to resolve (`UnknownIdentifierResolveResult`). Repro:
`RC_S9_LocalFunctionTests.LocalFunctions_Generic_MinimalFailing` ([Ignore]d).
A proper fix would lift generic local functions to private generic methods on
the containing type (capture analysis required).

## Integration-test infra: local h5 package restore races

`H5Compiler.EnsurePackageRestored` force-deletes and re-restores the local
`h5 0.0.42` package from `~/.nuget/packages` on first use per test class;
under parallel test execution another class can observe the half-restored
package and fail with "NuGet package not found on path ... h5/0.0.42"
(seen as sub-second failures that pass on re-run).

## `nameof(...)` crash when a user-defined method is named `nameof` (FIXED)

`SharpSixRewriter.VisitInvocationExpression` treated every invocation of an
identifier called `nameof` as the nameof operator; when the program defines
its own `nameof` method the constant value is null and
`SyntaxFactory.Literal(null)` threw `ArgumentNullException`. Fixed by only
taking the nameof branch when the invocation binds to no method symbol (the
operator never has one). Covered by `RC_P2_NameofTests.Nameof_UserMethodNamedNameof`.

## Roslyn-scripting reference runner cannot execute using-alias-heavy code

Not a compiler bug: `RoslynCompiler.CompileAndRunAsync` runs the reference
implementation as a C# *script*, which mishandles some using-alias directives
(empty output). Tests covering aliases should use `skipRoslyn: true` with a
direct output assertion (see `RC_S2_UsingStaticTests.UsingAliases_TypesGenericsAndNested`).

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
