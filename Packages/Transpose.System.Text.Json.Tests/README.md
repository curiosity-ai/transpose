# Transpose.System.Text.Json.Tests

End-to-end tests for the **`Transpose.System.Text.Json`** package — the `System.Text.Json.*` surface a
Transpose app compiles against, whose behaviour lives in the hand-written
[`Resources/Manual/JsonSerializer.js`](../Transpose.System.Text.Json/Resources/Manual/JsonSerializer.js).

## How a test works

There are two kinds, and the difference is which oracle a test is diffed against.

### 1. Against the real System.Text.Json (`JsonTestBase`)

Each test is a small C# program run **twice**, and the console output is diffed:

| | runner | what it exercises |
| --- | --- | --- |
| oracle | `NativeJsonRunner` | Roslyn-compiles the snippet in-process against the **real** `System.Text.Json` and invokes its entry point |
| subject | `TranslatedJsonRunner` | translates the snippet with `Transpose.Translator` against the package's own reference assembly, prepends the Transpose runtime + the package's glue + `JsonSerializer.js`, and runs it on Node |

`System.Text.Json` ships in the shared framework, so the oracle needs no package reference — the
snippet's `using System.Text.Json;` binds to the framework copy, and the *same snippet text* binds to
this package when translated. The package exists to behave like System.Text.Json, so "what does
System.Text.Json print" *is* the specification.

Where the package deliberately or knowingly differs, the test uses `RunJs(code, expected, nativePrints)`
instead: it pins the JavaScript output **and** re-asserts what native prints, so a documented divergence
cannot quietly rot into something else.

JSON member **order** is canonicalized before comparing (`TestOutput.CanonicalizeJson`), because it is
the one difference that would otherwise show up in nearly every test:
`SerializationTests.MemberOrderIsAlphabetical` pins it down once. (Json.NET is referenced *only* as the
parser that does this canonicalization — it is never an oracle, and no snippet references it.)

### 2. Against `Transpose.Newtonsoft.Json` (`CrossPackageTestBase`)

The Curiosity front-end is migrating off the Newtonsoft binding onto this package, and the question
that decides how risky each call site is — *does the payload on the wire change?* — is only answerable
by running the same program through both. `CrossPackageTests` and `MosaikShapeTests` do exactly that:

- **`AssertSame`** — the two packages agree, so that shape can be swapped over without thinking.
- **`AssertDiffers`** — a real behavioural change, with **both** sides recorded so neither can drift.

One template is written in a small dialect and rendered into each package's API: `#USINGS#`,
`[#PROP("n")]` (→ `JsonProperty` / `JsonPropertyName`), `#JSONEX#`, and `Json.Write` / `Json.WriteIndented`
/ `Json.Read<T>` supplied by a per-dialect shim. Everything else is ordinary C# and identical in both
renderings — which is the point: what differs, differs because the serializer differs.

`MosaikShapeTests` covers the types that exist *because* a Transpose JSON binding has no converter
registry — `UID128` and `LanguageDTO`, whose values are plain JavaScript strings reached only through a
conversion operator — plus the enum and renamed-member shapes the shared DTOs use. None of those are
expressible in a native snippet (they are `extern` + `[Template]`), so the sibling package is the only
meaningful oracle for them.

```bash
dotnet test Packages/Transpose.System.Text.Json.Tests

# see the JavaScript a test actually ran
TPS_DUMP_JS=/tmp/out.js dotnet test Packages/Transpose.System.Text.Json.Tests --filter SimpleRoundTrip

# ... and the Newtonsoft rendering of a cross-package test
TPS_DUMP_JS_NEWTONSOFT=/tmp/nj.js dotnet test Packages/Transpose.System.Text.Json.Tests --filter CrossPackageTests
```

Requirements: **Node** on `PATH` (or `/opt/node22/bin/node`) and a `Transpose.dll` runtime — the
`Transpose.BCL` package from the NuGet cache, or one built from this repo via
`TRANSPOSE_DLL_PATH=…/BCL/Transpose.BCL/bin/Release/netstandard2.0/Transpose.dll`.

## Layout

| file | area |
| --- | --- |
| `SmokeTests` | the harness itself: package build, runtime glue, Node |
| `SerializationTests` | member selection, order, nesting, indentation, enums, depth |
| `EscapingTests` | the escaping allow-list, control characters, non-ASCII, surrogate pairs |
| `DeserializationTests` | member matching, case sensitivity, constructors, primitives, `object` targets |
| `CollectionTests` | arrays, lists, sets, dictionaries, collection interfaces, nesting |
| `AttributeTests` | `[JsonPropertyName]`, `[JsonIgnore]`, `[JsonInclude]`, `[JsonConstructor]`, `[JsonPropertyOrder]`, `[JsonNumberHandling]` |
| `OptionsTests` | naming policies, case insensitivity, ignore conditions, read switches, `JsonSerializerDefaults.Web` |
| `BclTypeTests` | dates, times, GUIDs, URIs, versions, byte arrays, characters, nullables, 64-bit integers |
| `PolymorphismTests` | `[JsonPolymorphic]` / `[JsonDerivedType]`, `$type`, run-time registration |
| `ErrorHandlingTests` | malformed input, type and shape mismatches, depth limit |
| `CrossPackageTests` | this package vs `Transpose.Newtonsoft.Json` — the migration's risk register |
| `MosaikShapeTests` | `UID128`, `LanguageDTO`, enum and renamed-member shapes from the Curiosity front-end |

## Known divergences from System.Text.Json

Each is asserted by the test named beside it, so this list stays true.

| behaviour | System.Text.Json | this package | test |
| --- | --- | --- | --- |
| member order | declaration order, most-derived first | alphabetical | `MemberOrderIsAlphabetical` |
| `long` / `ulong` | JSON numbers | JSON strings (JavaScript cannot hold them exactly) | `SixtyFourBitIntegersAreWrittenAsStringsWhileDecimalsStayNumbers` |
| enum from its **name** | needs a `JsonStringEnumConverter` | always accepted | `AnEnumIsAlsoReadFromItsName` |
| deserializing to `object` | a `JsonElement` | the raw parsed JavaScript value | `DeserializingToObjectReturnsTheRawParsedValue` |
| an assembly-qualified `$type` | unrecognised discriminator | also matches the bare type name | `AnAssemblyQualifiedDiscriminatorStillMatchesTheBareName` |
| an integer written `1.0` / `2e0` | rejected (the token carries a decimal point) | accepted (the value is integral) | `AnIntegralNumberWrittenWithADecimalPointIsAccepted` |
| a **null** document | `ArgumentNullException` | the target's `default` | `ANullDocumentReadsBackAsTheDefault` |

The first two are inherited from `Transpose.Newtonsoft.Json` on purpose: the Curiosity server's
`Long`/`ULong`-from-string converters and its `JsonStringEnumConverter` are written against exactly
that wire shape, so matching the *server* matters more than matching the framework here. A `decimal`
deliberately stays a JSON number, because the server has no decimal-from-string converter.

The `1.0` row is the one nobody chose: System.Text.Json rejects it by reading the token's *text*, while a document here has already been through `JSON.parse`, which resolves `1.0` and `1` to the same JavaScript number. A value with a real fraction (`1.5`) is still rejected, as is one outside the target's range.

The `$type` one exists so a store written by Json.NET's `TypeNameHandling` (which wrote
`"Some.Type, Some.Assembly"` into `$type`) keeps deserializing against a hierarchy that declares the
bare type name as its discriminator.

The **null document** is the one deviation adopted from `Transpose.Newtonsoft.Json` rather than from
the Curiosity server. Note that *real* Json.NET throws `ArgumentNullException` here too — returning
`default(T)` is a long-standing behaviour of the Transpose binding, not of Json.NET — but every
Transpose front-end moving onto this package was written against it, because reading a slot that was
never written (local storage, a cached response, an unset node field) is an ordinary thing to do and
answering it with `default(T)` is what those call sites expect. Only "no document at all" is
affected: an empty or whitespace-only string, and any malformed document, still throw exactly as
System.Text.Json does — so a slot holding corrupt JSON is still reported rather than silently read
as nothing.

## Declaring a hierarchy the attribute cannot reach

`[JsonDerivedType]` has to name the derived types from the base type's own file, so the base must see
them at compile time. In a layered application it often does not — an interface in a shared,
low-level project and its implementations in the UI project that references it — and the attribute
cannot be written at all. `JsonPolymorphicTypes.Register<TBase>(typeof(Derived), "discriminator")`
declares the same pair at run time, from somewhere that can see both, and behaves exactly as the
attribute would. The two can be mixed, which is what a project shared between a server (attributes)
and a Transpose front-end (registration) needs.

## What the package deliberately does not cover

The streaming (`Utf8JsonReader` / `Utf8JsonWriter`), document (`JsonDocument` / `JsonNode` /
`JsonElement`) and source-generated (`JsonSerializerContext`) APIs, and the custom-converter registry
(`JsonConverter<T>`, `JsonStringEnumConverter`). The package covers whole-document
`Serialize` / `Deserialize` over a `JsonSerializerOptions`, which is the surface a browser app uses.
