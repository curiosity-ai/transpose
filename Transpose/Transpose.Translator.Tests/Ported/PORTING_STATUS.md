# Ported h5 integration tests — status

The suites in this folder began as the h5 integration tests (`Tests/H5.Compiler.IntegrationTests`),
ported onto the Roslyn translator and since extended one language or BCL area per file. Each test
translates a complete program with `Transpose.Translator`, runs the JavaScript on Node against the real
`tps.js` runtime, compiles and runs the same program natively, and asserts the two outputs are equal
(`TranslatorTestBase.RunTest`). A test that checks Transpose-only behaviour passes `skipRoslyn: true`
and asserts on the JavaScript output instead; a test for a rejected construct uses
`RunTestExpectingError`.

Everything that goes wrong in the browser in a way .NET does not is a bug, and the fix for one belongs
in the file that covers its area, next to a test that reproduces it.

## Current results

**384 tests in 48 files: 367 passing, 0 failing, 17 skipped.** The skipped
tests are all of `WebApiTests`, which need the `Transpose.Core` DOM bindings and a browser — out of scope
for a runtime-only harness.

The whole translator suite (this folder plus the top-level `*Tests.cs` files and `Linq/`) is run by hand,
not in CI — see the `.devops` note in the repository's `CLAUDE.md`:

```bash
export TRANSPOSE_DLL_PATH=$PWD/BCL/Transpose.BCL/bin/Debug/netstandard2.0/Transpose.dll
dotnet test Transpose/Transpose.Translator.Tests -c Debug --filter "FullyQualifiedName~Tests.Ported"
```

## Rejected by design

These tests pass by asserting that translation **fails** with the documented error, because the
construct has no browser equivalent (`UnsupportedFeatureScanner`, `TransposeR0001`) or the emitter does
not translate it. The user-facing list, with every message, is the documentation's
[Unsupported Features](https://docs.curiosity.ai/transpose/core-concepts/unsupported-features) page.

| Test | Construct |
| --- | --- |
| `UnsupportedFeaturesTests.UnsafePointers` | pointers, `&`, `*` |
| `UnsupportedFeaturesTests.FixedSizeBuffers` | `unsafe` / `fixed` buffers |
| `PrimitiveTypesTests.Integers_Overflow` | `checked` integer arithmetic |
| `Int64OperationsTests.Int64BackedEnumIsUnsupported_Tests` | an enum with a 64-bit underlying type |
| `StandardLibraryTests.BinaryWriterReader_Tests` | `BinaryWriter` / `BinaryReader` (File I/O) |
| `CSharp9Tests.TopLevelStatements` | top-level statements |
| `CSharp9Tests.NativeSizedIntegers` | `nint` / `nuint` |
| `CSharp10Tests.GlobalUsings` | `global using` |
| `CSharp10Tests.LambdaImprovements` | a lambda with a `ref` parameter (the rest of the test's lambda forms translate) |
| `CSharp11Tests.PatternMatchSpanOnConstantString` | `span is "text"` |
| `CSharp12Tests.InlineArrays` | `[InlineArray]` |
| `CSharp13Tests.LockObject` | `System.Threading.Lock` (`lock` on an `object` works) |
| `CSharp14Tests.ExtensionMembers` | C# 14 `extension` blocks |
| `CSharp14Tests.SimpleLambdaModifiers` | `(ref x) => …` |

## Known differences from .NET

The behaviours that deliberately or knowingly differ — struct copy semantics outside the compilation, a
boxed number losing its exact type, `dynamic` with no runtime overload resolver, `Span<T>` and the
implicit array conversion, positional patterns against a hand-written `Deconstruct` — are listed, with
the reason for each, under **Known remaining work** in the repository's `CLAUDE.md`. They are not
failures here: no test in this folder expects the .NET result for them.

## Fixed since the last revision of this file

The previous revision listed the port's failure categories as of the h5 → Roslyn switch (303 of 340
passing). All of those are resolved — generic method type arguments (`new T()` on a method type
parameter), the reflection metadata block, null-conditional assignment, `params` collections,
multi-dimensional arrays — or are now rejected by design and pinned above (C# 14 extension blocks and
`ref` lambda parameters, `System.Threading.Lock`, `nint`/`nuint`, `checked`, binary file I/O).

Later fixes, each with its own suite outside this folder:

- **A JavaScript error reaching a C# `catch` is mapped onto its .NET exception**
  (`JavaScriptErrorCatchTests`). A null dereference is a `TypeError` in JavaScript, and the catch used to
  hand it to the clauses raw, so `catch (NullReferenceException)` never matched one. Every catch now
  passes the value through `System.Exception.create`: `TypeError` → `NullReferenceException`,
  `RangeError` → `ArgumentOutOfRangeException` (now with the error's own message), any other `Error` →
  `SystemException`, and any other value → `Exception` (a thrown `0` keeps its text; a thrown `null` no
  longer makes `StackTrace` throw).
- **`ref` locals and `ref` returns hold a reference, not a copy** (`RefLocalAndReturnTests`,
  `Emitter.RefCells.cs`), for ref-returning methods, local functions, properties and indexers. Members
  of the Transpose runtime packages (`Span<T>`'s indexer) keep value semantics.
- **Invoking a delegate with `ref`/`out` parameters writes back** (`RefLocalAndReturnTests`).
- **C# 14 instance operators** (`public void operator +=(…)`, `operator ++()`) are calls on the receiver,
  and a classic static `operator ++` on a class is applied instead of a JavaScript `++`
  (`InstanceOperatorTests`).
- **`++`/`--` on an indexer element** (`dict[key]++`) is valid JavaScript, **`int`/`uint` `++`/`--`
  wrap**, a **null `int?` stays null** under `++`, and **`arr[k++] += v` evaluates `k++` once**
  (`IncrementDecrementTests`).
- **An indexer used through a user-defined interface works** (`InterfaceIndexerTests`). The implementer's
  alias table named it `this[]` rather than aliasing its `getItem`/`setItem`, so every read or write
  through the interface threw "IFace$setItem is not a function".
- **A missing `System.IO`/`System.Threading`/`System.Net.Sockets` type is reported as `TransposeR0001`**
  rather than as a Roslyn "type not found" error (`BrowserApiDiagnosticTests`).
