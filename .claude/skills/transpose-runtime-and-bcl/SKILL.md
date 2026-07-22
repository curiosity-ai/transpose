---
name: transpose-runtime-and-bcl
description: >-
  Rebuild the Transpose runtime, add or change BCL APIs and runtime JavaScript, and add/run the
  translator regression tests — the "make and validate a fix" side of Transpose work. Use this
  WHENEVER a change touches the runtime JS (BCL/Transpose.BCL/Resources/*.js, tps.shim.js) or the BCL
  C# (System.*), when adding a missing BCL method/type (e.g. a LINQ operator, a string/Task API),
  when the emitted metadata/init changes, or when adding a regression test to
  EmitRegressionTests.cs and running the suite. Triggers on "add X to the BCL", "rebuild the
  runtime", "implement this .NET API in transpose", "add a regression test", or "run the transpose
  tests". Pairs with transpose-debugging (inspection) and transpose-h5-audit (finding bugs).
---

# Runtime, BCL changes, and regression tests

## How the pieces fit

- **`BCL/Transpose.BCL/`** *defines* the C# BCL. Most `System.*` types are `[Transpose.External]` and
  bind to hand-written JS in `Resources/*.js` via `[Template]`/`[Name]`/`[Convention]`; other types
  are ordinary C# that gets transpiled. The whole assembly compiles to a self-contained reference
  assembly **`Transpose.dll`** (also the sole BCL reference for every compile) with the runtime JS
  bundles embedded.
- **The runtime bundle `tps.js`** is stitched from `Resources/*.js` + the transpiled
  `Resources/.generated/*.js` per the explicit ordered file list in `BCL/Transpose.BCL/tps.json`.
- **The language shim `tps.shim.js`** (`Transpose/Transpose.Translator/Runtime/`) is embedded in the
  **translator**, not the runtime.

## Rebuilding the runtime

After changing BCL C# or `Resources/*.js`, rebuild `Transpose.dll` (~25s):

```bash
dotnet Transpose/Transpose.Compiler/bin/Debug/net10.0/tps.dll \
  --project BCL/Transpose.BCL/Transpose.BCL.csproj --build-runtime -c Debug \
  -o BCL/Transpose.BCL/bin/Debug/netstandard2.0/Transpose.dll
```

This transpiles the BCL into `Resources/.generated/*.js`, stitches the bundles per `tps.json`, and
emits `Transpose.dll` with them embedded. Both the runners and the test suite read this DLL via
`TRANSPOSE_DLL_PATH`. (For a **shim** change, rebuild the translator/compiler instead — the shim is
embedded there.)

## Adding a BCL API — two paths

Pick based on whether the behaviour is best expressed in JS or C#:

**A. `extern` + `[Template]` binding to runtime JS** — the norm for the external BCL types. Declare the
member `extern` on the (external) type and either give it a `[Template("...js...")]` or rely on the
type's `[Convention(CamelCase)]` to map the name (e.g. `Delay` → runtime `delay`). Then implement the
matching function in the type's `Resources/*.js`. Example: `Task.Yield()` → declare
`public static extern Task Yield();` on the CamelCase-convention `Task`, and add a `yield: function(){
var tcs = new System.Threading.Tasks.TaskCompletionSource(); Transpose.setImmediate(function(){
tcs.setResult(null); }); return tcs.task; }` to `Resources/Task.js`. Redirects are trivial — give the
new member the SAME `[Template]` as the existing one (e.g. `ToLowerInvariant()` reuses
`{this}.toLowerCase()`).

**B. Real C# in a NON-external class** — for logic cleaner to write in C#. `[External]` types emit no
bodies, so put the code in a new, non-`[External]` class; it transpiles like user code (iterators →
`function*`, etc.). Example: LINQ `Chunk`/`MinBy`/`MaxBy` live in a new `EnumerableExtras` class (the
`Enumerable` binding is external). **Then add the generated file to `tps.json`** — the bundle is a
manual ordered list, so a new `Resources/.generated/System/.../Foo.js` must be inserted (after its
dependencies, e.g. right after `linq.js`) or it compiles but never loads at runtime. Symptom of a
forgotten entry: `Cannot read properties of undefined (reading 'YourMethod')` and a byte-identical
`tps.js` size after `--build-runtime`.

Match native .NET semantics exactly — that is the whole point (empty-sequence throw-vs-default, null
handling, comparer overloads, validation exceptions). Note the `.generated/*.js` files are gitignored
build artifacts; commit the C# source + `tps.json`, not the generated JS.

## Regression tests

Tests live in `Transpose/Transpose.Translator.Tests/EmitRegressionTests.cs` (and the `Ported/` suites).
`RunTest(code, waitForOutput?, skipRoslyn?)` transpiles the snippet, runs it in Node, AND asserts the
output equals native .NET's. Two flavours:

- **Behavioural** (default): `await RunTest(code, waitForOutput: "<<DONE>>");` — compares Node output
  to native. End the program with a sentinel `Console.WriteLine("<<DONE>>")`.
- **Emit-shape / Transpose-only**: `new RoslynTranslator().Translate(code)` then assert on
  `result.Javascript` (e.g. `Contains("TransposeR.combine(")`). Use for things that are a no-op in
  native, or pass `skipRoslyn: true` to `RunTest` and assert on the returned JS output string.

Harness gotchas:

- `Console.Write` (no newline) appends a newline per call under Node — build a string and `WriteLine`
  once, or the native comparison mismatches.
- **Fire-and-forget async is non-deterministic under native.** `static void Main() { Run(); }` where
  `Run` truly yields (e.g. `await Task.Yield()`) can let the native process exit before continuations
  run, while Node drains its queue — so a native-comparison test flakes. Use `skipRoslyn: true` and
  assert on the JS output sequence instead.
- Tests use the DEBUG translator (project reference) and read the runtime via `TRANSPOSE_DLL_PATH` —
  rebuild the runtime first if your change touched it.

Run the suite:

```bash
export TRANSPOSE_DLL_PATH=<repo>/BCL/Transpose.BCL/bin/Debug/netstandard2.0/Transpose.dll
# whole suite (~2 min):
dotnet test Transpose/Transpose.Translator.Tests/Transpose.Translator.Tests.csproj -c Debug
# a subset while iterating (unqualified substring match):
dotnet test Transpose/Transpose.Translator.Tests/Transpose.Translator.Tests.csproj -c Debug --filter MyNewTest
```

The suite MUST stay fully green before committing. Confirm the `Passed! - Failed: 0` line — a
non-zero exit / any `Failed` line means regressions.

## Commit discipline

Group related fixes into separate, descriptive commits (one concern each). Reproduce → confirm wrong
JS → fix → regression test → full suite green → commit. Write commit messages with a file (`git commit
-F msg.txt`) when they contain backticks, so the shell doesn't run command-substitution on them.

## Related skills

- Inspecting emitted JS / the fast per-construct loop → **transpose-debugging**.
- Finding the bugs to fix, systematically, against h5 → **transpose-h5-audit**.
