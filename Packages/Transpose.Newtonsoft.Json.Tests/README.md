# Transpose.Newtonsoft.Json.Tests

End-to-end tests for the **`Transpose.Newtonsoft.Json`** binding library — the `Newtonsoft.Json.*`
surface a Transpose app compiles against, whose behaviour lives in the hand-written
[`Resources/Manual/JsonConvert.js`](../Transpose.Newtonsoft.Json/Resources/Manual/JsonConvert.js).

## How a test works

Each test is a small C# program. It is run **twice** and the console output is diffed:

| | runner | what it exercises |
| --- | --- | --- |
| oracle | `NativeJsonRunner` | Roslyn-compiles the snippet in-process against the **real Json.NET** (the `Newtonsoft.Json` NuGet package this test project references) and invokes its entry point |
| subject | `TranslatedJsonRunner` | translates the snippet with `Transpose.Translator` against the binding library's own reference assembly, prepends the Transpose runtime + the package's glue + `JsonConvert.js`, and runs it on Node |

The package exists to behave like Json.NET, so "what does Json.NET print" *is* the specification —
`JsonTestBase.RunAndCompare` asserts the two agree. Where the binding library deliberately or
knowingly differs, the test uses `RunJs(code, expected, nativePrints)` instead: it pins the
JavaScript output **and** re-asserts what native prints, so a documented divergence cannot quietly
rot into something else.

JSON member **order** is canonicalized before comparing (`TestOutput.CanonicalizeJson`), because it
is the one difference that would otherwise show up in nearly every test:
`SerializationTests.MemberOrderIsAlphabeticalFieldsFirst` pins it down once.

```bash
dotnet test Packages/Transpose.Newtonsoft.Json.Tests

# see the JavaScript a test actually ran
TPS_DUMP_JS=/tmp/out.js dotnet test Packages/Transpose.Newtonsoft.Json.Tests --filter SimpleRoundTrip
```

Requirements: **Node** on `PATH` (or `/opt/node22/bin/node`) and a `Transpose.dll` runtime — the
`Transpose.BCL` package from the NuGet cache, or one built from this repo via
`TRANSPOSE_DLL_PATH=…/BCL/Transpose.BCL/bin/Release/netstandard2.0/Transpose.dll`.

## Layout

| file | area |
| --- | --- |
| `SerializationTests` | member selection, order, nesting, formatting, enums, escaping, cycles |
| `DeserializationTests` | member matching, constructors, defaults, primitives, `object` targets |
| `CollectionTests` | lists, arrays, dictionaries, sets, collection interfaces, nesting |
| `AttributeTests` | `[JsonProperty]`, `[JsonIgnore]`, `[JsonConstructor]`, `[DefaultValue]`, callbacks |
| `SettingsTests` | null/default handling, camel-case resolver, object-creation handling |
| `BclTypeTests` | dates, times, GUIDs, URIs, 64-bit integers, decimals, byte arrays, nullables |
| `TypeNameHandlingTests` | `$type`, `ISerializationBinder`, polymorphic payloads |
| `PopulateObjectTests` | `JsonConvert.PopulateObject` |
| `ErrorHandlingTests` | malformed JSON, type mismatches, required members |
| `CuriosityUsageTests` | the shapes the Curiosity front-end actually sends and receives |

## Known divergences from Json.NET

Each is asserted by the test named beside it, so this list stays true.

| behaviour | Json.NET | this package | test |
| --- | --- | --- | --- |
| member order | declaration order (fields first) | alphabetical (fields first) | `MemberOrderIsAlphabeticalFieldsFirst` |
| `long` / `ulong` | JSON numbers | JSON strings (JS cannot hold them exactly) | `SixtyFourBitIntegersAreWrittenAsStrings` |
| whole-number `double` | `1.0` | `1` | `WholeNumberDoublesLoseTheirTrailingZero` |
| reference cycle | throws | drops the back-reference | `SelfReferencingLoopIsDroppedInsteadOfThrowing` |
| null into a non-nullable value member | throws | leaves the default | `NullIntoANonNullableValueTypeIsIgnoredInsteadOfThrowing` |
| empty input for a value type | throws | returns the default | `EmptyInputForAValueTypeReturnsZeroInsteadOfThrowing` |
| array where an object is expected (and vice versa) | throws | empty instance / empty collection | `ArrayIntoAnObjectTargetYieldsAnEmptyInstance`, `ObjectIntoACollectionTargetYieldsAnEmptyCollection` |
| private setter | not written unless `[JsonProperty]` | always written | `PrivateSetterIsPopulatedUnlikeJsonNet` |
| deserializing to `object` | `JObject` (Linq-to-JSON) | the raw parsed JavaScript value | `DeserializingToObjectReturnsTheRawParsedValue` |
| `Nullable<TEnum>` | prints the member name | prints the number (comparisons are fine) | `NullableEnumPrintsItsNumberInsteadOfItsName` |
| unknown enum name | `JsonSerializationException` | `ArgumentException` | `UnknownEnumNameThrowsArgumentExceptionNotJsonException` |

`FractionalSecondsRoundTrip` reports **inconclusive** against a runtime that predates the
`fractionToMilliseconds` fix in `BCL/Transpose.BCL/Resources/Date.js` (a `.25` fraction read as 25 ms
instead of 250 ms) — that fix ships with the runtime, not with this package, and reached NuGet in
**`Transpose.BCL 26.7.3064`**. So it is the one test here that can come out non-green without anything
being wrong with the package: it means the resolved `Transpose.dll` is stale, not that the bug is back.
Which runtime gets resolved is `TRANSPOSE_DLL_PATH` if set, else the **highest-versioned**
`Transpose.BCL` in the NuGet cache — so an old cached package is enough to trigger it, and the
inconclusive message names the DLL it actually used.
