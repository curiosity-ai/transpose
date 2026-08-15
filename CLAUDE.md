# CLAUDE.md — Transpose

Transpose is a **C# → JavaScript compiler** built entirely on **Roslyn**. It is the next
generation of the H5 project (itself a fork of Bridge.NET), rebranded and rebuilt around the
clean-room Roslyn translator. The legacy Bridge/NRefactory pipeline has been removed; Transpose is
*solely* the Roslyn-based translator and its CLI compiler.

- **Runtime global** in generated JavaScript: `Transpose` (e.g. `Transpose.assembly(...)`,
  `Transpose.define(...)`). The language-helper shim is `TransposeR`.
- **Short form** for the JavaScript runtime / config / files: `tps` (e.g. the runtime bundle
  `tps.js`, the config file `tps.json`, the JS module name `tps`, the compiler command `tps`).
- **NuGet package ids** use the `Transpose` / `Transpose.*` naming and (except for the base
  library) match each project's `AssemblyName` (e.g. `Transpose.Core`, `Transpose.Newtonsoft.Json`,
  `Transpose.Compiler`). The one exception is the base library: its `AssemblyName` is `Transpose`
  (so the DLL and runtime-detection stay `Transpose.dll` / assembly `Transpose`) but its package id
  is **`Transpose.BCL`**.

> Historical note: the codebase was renamed from H5 with a case-sensitive mapping — `H5` →
> `Transpose` (namespaces, runtime global, assembly names) and `h5` → `tps` (JS runtime file
> names, config, module name). NuGet **package ids** were subsequently renamed from `tps` /
> `tps.*` to `Transpose` / `Transpose.*` to match the assembly names (the base library's package id
> was later changed again to `Transpose.BCL`, though its assembly stays `Transpose`). Non-library tokens were
> deliberately preserved (e.g. the `<h5>` HTML tag binding in `Transpose.Core`, hash locals
> `h1..h5` in `ValueTuple`).

## Repository layout

```
Transpose.slnx                 # Solution: the compiler toolchain (.NET projects)
bootstrap.sh                   # Builds reference assemblies + transpiles the BCL/Packages with tps

Transpose/                     # The compiler toolchain
├── Transpose.Translator/      # The translator library (Roslyn -> JS).  AssemblyName Transpose.Translator
│   ├── Compilation/           #   CompilationBuilder, RoslynTranslator, TransposeAssemblies, results
│   ├── Emit/                  #   Emitter.*.cs (syntax-tree walk -> JS), JsWriter, TreeModel
│   ├── Support/               #   TransposeNaming, NameMangler, UnsupportedFeatureScanner
│   └── Runtime/tps.shim.js    #   embedded TransposeR shim (language helpers over the tps.js runtime)
├── Transpose.Compiler/        # The CLI compiler.  AssemblyName + tool command: `tps`
│   ├── Program.cs             #   arg parsing -> BuildOptions, the --timing report, watch-mode entry
│   └── WatchMode.cs           #   `tps --watch`'s dev server only: Kestrel static files + the reload socket
├── Transpose.Compiler.Core/   # Shared engine behind the CLI and Transpose.Compiler.Library — not itself
│   │                          #   packaged, like Transpose.Translator above. Namespace Transpose.Compiler.
│   ├── ProjectBuild.cs        #   one build of one project, end to end (everything `tps` does after parsing
│   │                          #   its command line): BuildOptions -> BuildOutcome, plus BuildLog
│   ├── WatchSession.cs        #   the watch engine: file watchers + debounce, the rebuild-vs-CSS-only
│   │                          #   decision, ReloadHub (websocket) and the injected live-reload script
│   ├── ProjectResolver.cs     #   reads the .csproj (raw XML), globs sources, resolves references
│   ├── ProjectXml.cs          #   csproj + <Import> flattening (shared projects' .projitems)
│   ├── OutputBuilder.cs       #   site build (runtime + bundle + resources + index.html) + stale-output prune
│   ├── ResourceEmbedder.cs    #   embeds JS + resources into the package DLL (Mono.Cecil)
│   ├── RuntimeAssembler.cs    #   stitches Resources/*.js + generated ClassPath files into tps.js
│   ├── BuildCache.cs          #   the --incremental on-disk cache
│   ├── JsMinifier.cs          #   NUglify-based bundle minification
│   ├── CssProcessor.cs        #   strips /* … */ comments from every stylesheet (site + embedded)
│   ├── TransposeJson.cs       #   reads tps.json (output/fileName/html/resources/reflection)
│   └── MsBuildDiagnostic.cs   #   canonical MSBuild diagnostic formatting (Origin : Category Code : Text)
├── Transpose.Compiler.Library/# Compiler-as-a-library. Package id + AssemblyName Transpose.Compiler.Library
│   ├── CompilationRequest.cs  #   fluent request: in-memory sources, package/reference assemblies, settings
│   ├── CompilationResult.cs   #   JS/assembly bytes + diagnostics on success, formatted errors on failure
│   ├── ProjectBuildRequest.cs #   build a real on-disk .csproj (the library form of `tps --project`)
│   ├── ProjectBuildResult.cs  #   exit code + site directory + formatted errors/warnings + captured output
│   ├── TransposeWatcher.cs    #   watch mode for a host that runs its own web server: Start/BeginWatching +
│   │                          #   HandleWebSocketAsync. Used by `curiosity-cli serve --watch` (mosaik repo)
│   └── TransposeCompilerLibrary.cs # Compile/BuildProject (+Async) — serialized (CompileProgress/PhaseTimings
│                              #   and the diagnostic sink are process-wide mutable state, so concurrent
│                              #   compiles are queued, not parallel)
├── Transpose.Bench/           # Benchmark harness. AssemblyName + tool command: `tps-bench`
│   ├── MachineInfo.cs         #   CPU model / cores / RAM / SIMD-ISA detection
│   ├── CpuScore.cs            #   short deterministic CPU+memory benchmark -> normalisation score
│   ├── Scenario.cs            #   clean-slate `tps` runs (wipes bin/obj of the project closure)
│   ├── R2RCheck.cs            #   is this tps (or .nupkg) ReadyToRun-compiled?
│   ├── Report.cs              #   console / Markdown / JSON output + --baseline comparison
│   └── ab.sh                  #   interleaved A/B of two tps binaries on one project
├── Transpose.Build.Target/    # MSBuild SDK (package id Transpose.Build.Target) that invokes `tps`
├── Transpose.Template/        # `dotnet new` template (package id Transpose.Template)
├── Transpose.Translator.Tests/# MSTest suite; transpiles snippets and diffs vs native .NET on Node
│   ├── Ported/                #   the suites ported from h5, one per language/BCL area
│   └── Linq/                  #   the LINQ surface: every Enumerable/EnumerableExtras overload, every
│                              #   element type (class/struct/record/record struct/enum/nullable/tuple/
│                              #   anonymous/dynamic/…), query syntax, and the exception/edge cases
└── Transpose.WatchMode.Tests/ # MSTest suite for `tps --watch`: a real tps subprocess + headless
                               #   Chromium (Playwright). Separate project on purpose — it waits on
                               #   wall-clock events, so sharing a host with the Roslyn-heavy suite
                               #   above starved the browser and timed the waits out spuriously.

BCL/                           # The base runtime libraries
├── Transpose.BCL/             # Base library. AssemblyName Transpose, package id `Transpose.BCL`, namespace Transpose
│   ├── Transpose/             #   codegen attributes ([External],[Template],[Name],[Script],...) + markers
│   ├── System/, shared/       #   C# definitions of the .NET BCL (System.Object, string, collections, ...)
│   ├── Resources/*.js         #   hand-written JavaScript runtime primitives (Core.js, Class.js, ...)
│   └── tps.json               #   declares how Resources + generated JS combine into tps.js
└── Transpose.Core/            # Web API bindings (DOM, ES5/ES6). package id `Transpose.Core`, ns Transpose.Core

Packages/                      # Additional binding libraries (all [assembly: External])
├── Transpose.Newtonsoft.Json/ #   package Transpose.Newtonsoft.Json
├── Transpose.Newtonsoft.Json.Tests/ # MSTest suite for it: every snippet runs natively against the
│                              #   real Json.NET *and* as translated JS on Node, and the outputs are
│                              #   diffed (see its README for the documented divergences)
├── Transpose.System.Text.Json/#   package Transpose.System.Text.Json — the whole-document
│                              #   JsonSerializer/JsonSerializerOptions surface a browser app uses.
│                              #   Deliberately *not* the streaming (Utf8JsonReader/Writer), document
│                              #   (JsonDocument/JsonNode) or source-generated APIs, and no converter
│                              #   registry. Behaviour lives in Resources/Manual/JsonSerializer.js
├── Transpose.System.Text.Json.Tests/ # MSTest suite for it, with two oracles: the real
│                              #   System.Text.Json (it ships in the shared framework, so the same
│                              #   snippet binds to both), and — for the Curiosity migration —
│                              #   Transpose.Newtonsoft.Json, so every wire-format change between the
│                              #   two packages is recorded on both sides (see its README)
├── Transpose.Howler/          #   package Transpose.Howler
├── Transpose.WebGL2/          #   package Transpose.WebGL2
├── Transpose.P2/              #   package Transpose.P2
├── Transpose.HttpClient/      #   package Transpose.HttpClient
└── Transpose.Placeholders/    #   placeholder attributes (package Transpose.Placeholders)

benchmarks/tesserae/           # git submodule: the benchmark corpus (see Performance below)
docs/perf/                     # recorded tps-bench reports (baseline + current)
.devops/                       # Azure DevOps pipelines (one per package, plus the benchmark). The
                               #   exception is build-transpose-compiler, which publishes both
                               #   Transpose.Compiler and Transpose.Compiler.Library: they share the
                               #   unpublished Translator/Compiler.Core, so both have to ship at the
                               #   same version and are packed and pushed in one run.
                               #   Two of them carry test steps — build-transpose-compiler (the
                               #   translator + watch-mode suites) and build-transpose-json — but both
                               #   are commented out: the DevOps agents are too resource-limited to
                               #   run them, so the suites are run by hand for now. Keep them that
                               #   way (and keep the steps in place) unless that changes.

docs/, logo/, lib/, External-less # docs, brand assets (see below), misc
```

### Brand assets (`logo/`)

One artwork, three derived files — regenerate all three together if it ever changes:

| File | Size | Used by |
| --- | --- | --- |
| `logo/transpose.png` | 256×256 RGBA | `PackageIcon` of **every** package (each packable csproj packs it to the package root) |
| `logo/transpose-512.png` | 512×512 RGBA | the README header image (referenced by its `raw.githubusercontent.com` URL, so it also renders on nuget.org) |
| `logo/transpose.ico` | 16/24/32/48/64/128/256 | `ApplicationIcon` of the `tps` and `tps-bench` executables (only the Windows apphost carries it; other platforms ignore it) |

The corners are transparent, so the rounded icon reads correctly on nuget.org's white
background and on a dark README. `logo/transpose.svg` is the retired pre-2026 mark, kept only so
old absolute links do not 404.

Every packable project also sets `PackageReadmeFile` and packs the repository `README.md`
(`Transpose.Placeholders` packs its own, package-specific README instead). Because that README ships
to nuget.org, links in it must be **absolute** — a relative `[x](FILE.md)` renders as a dead link
there.

## Compilation pipeline

```
sources ──► CompilationBuilder ──► CSharpCompilation + SemanticModel   (C# Latest, references = Transpose.dll [+ extras])
                                        │
                                        ├─► Roslyn diagnostics (errors) ───────────► fail
                                        ├─► UnsupportedFeatureScanner ─────────────► fail (browser-incompatible)
                                        └─► Emitter (syntax walk, semantic-guided) ► JavaScript
                                                                                     (runtime global `Transpose`,
                                                                                      helpers via `TransposeR`)
```

Entry point: `RoslynTranslator` (`Transpose.Translator`). The emitter walks Roslyn `SyntaxNode`s
guided by the `SemanticModel` and emits JS directly — there is **no** NRefactory and **no**
`SharpSixRewriter` lowering pass. See `H5.Translator.Roslyn.PORT_PLAN.md` for the original design
and the feature-by-feature roadmap (naming there predates the rebrand).

### The base reference assembly (`Transpose.dll`) and the runtime (`tps.js`)

`CompilationBuilder` always injects `Transpose.dll` as the sole BCL reference (`TransposeAssemblies`
locates it in the NuGet cache under package `Transpose.BCL` — the historical `Transpose` id is still
probed for older caches — or via the `TRANSPOSE_DLL_PATH` env var).
`Transpose.dll` redefines `System.*` with the codegen attributes that drive emission, and embeds the
JS runtime `tps.js`. Generated code runs against that runtime.

`TransposeAssemblies` also reads `tps.js` (embedded resource) for the output prelude, and computes
the set of body-less (`extern`) methods so overload numbering matches the hand-written runtime.

### The minimum compiler version (`Transpose.Build.json`)

Every assembly `tps` produces — a package DLL, a site build's DLL, and `Transpose.dll` itself — carries
a small embedded JSON stamp (`BuildStamp`, resource name `Transpose.Build.json`) recording the compiler
that built it and, as `minimumCompilerVersion`, the oldest compiler allowed to consume it:

```json
{ "compilerVersion": "26.7.1234", "minimumCompilerVersion": "26.7.1234" }
```

The minimum is simply the version that built the assembly — nothing declares it by hand. A Transpose
package's real payload is the JavaScript inside it, and how a consumer's own emitted JS binds to that
payload (names, overload numbering, the `TransposeR` helpers it calls) is decided by the *consuming*
compiler, so an older `tps` fed a newer package produces a subtly wrong bundle rather than an error.
Before compiling, `tps` therefore reads the stamp of **every** assembly the project binds against —
every reference plus the injected `Transpose.dll` — and fails with `TPS0008` if any of them needs a
newer compiler, naming the version required and the `dotnet tool install --global Transpose.Compiler`
command that installs it. The check runs before the incremental cache is consulted, so an up-to-date
build cannot skip it.

Two properties keep this out of the way of working *on* Transpose:

- **It is only enforced by a versioned Release build of the compiler.** Versions are stamped by CI
  (`/p:Version=yy.M.<buildId>`, propagated into `Transpose.Compiler.Core`, which is where
  `CompilerVersion` reads it back); a dev tree has none and pins `0.0.0` instead, which turns the check
  off. A Debug-built compiler — `bootstrap.sh`, the test suite — never enforces it either.
- **An unstamped assembly is skipped**, so packages published before this existed keep working, and
  assemblies built in a dev tree carry the `0.0.0` placeholder, which can never fail a check.

The stamp is embedded but deliberately *not* listed in `Transpose.Resources.json` — it is compiler
metadata, not a web resource, so `OutputBuilder` never extracts it into a site.

## Code-generation attributes (namespace `Transpose`)

- `[External]` — type/member is defined in external JS (no body emitted). Can be applied at the
  **assembly** level (`[assembly: External]`) — every type in binding libraries like
  `Transpose.Core` and the `Packages/*` is external this way.
- `[Name]` — override the emitted JS name.
- `[Template]` — a JS code template for a call (e.g. `[Template("Transpose.getEnumerator({this})")]`).
- `[Script]` — raw JS body.
- `[GlobalMethods]` / `[Scope]` — project static members / a type onto ambient JS globals.
- `[ObjectLiteral]` — treat a class/struct as a plain JS object.

## Building and bootstrapping

### 1. Build the toolchain (standard .NET)

```bash
dotnet build Transpose.slnx
```

This builds `Transpose.Translator`, `Transpose.Compiler.Core`, the `tps` compiler, the
`Transpose.Compiler.Library` library, the tests, and the template.

The `Transpose.Build.Target` SDK is listed in the solution so its targets are editable from the IDE,
but it is marked `<Build Project="false" />` and is **never built** as part of a solution build: it
is an MSBuild SDK package rather than code the toolchain compiles against, and building it inside
the solution fails with `NETSDK1199` (`ArtifactsPath` cannot be set in a csproj). Release it on its
own with `dotnet pack Transpose/Transpose.Build.Target`.

The BCL/Packages projects *are* in the solution, but they build only when a `tps` is on `PATH`
(their `Sdk="Transpose.Build.Target/…"` shells out to it) — the BCL is compiled by `tps`, not by
`dotnet`. Use `./bootstrap.sh` for a dev tree.

### 2. Bootstrap the BCL/Packages

```bash
./bootstrap.sh
```

The base library is special: it *defines* the C# BCL, so it is compiled to a **self-contained
reference assembly** `Transpose.dll` (`NoStdLib`, no framework references) rather than transpiled.
Every other project is a JavaScript binding library that `tps` transpiles, binding against the base.
`bootstrap.sh`:

1. builds `Transpose.dll` (base) and `Transpose.Core.dll` (core) reference assemblies,
2. runs `tps` on `Transpose.Core` and each `Packages/*` library, emitting their JS into
   `artifacts/bootstrap/`.

All six binding libraries currently transpile successfully.

Which path `tps` takes for a project is decided by its **assembly name**, not by its resolved
references: `outputBy: ClassPath` selects the runtime build only for the base library (assembly name
`Transpose`). A binding library may declare `ClassPath` for its own JS layout — `Transpose.Newtonsoft.Json`
does — and must still take the package path, because it *binds against* the BCL rather than defining it.
Keying that off a resolved reference to `Transpose.dll` does not work: the translator **injects** the base
library instead of taking it from the project's references, so in a dev tree (where the
`Transpose.BCL` PackageReference is not in the NuGet cache) such a project resolves zero references and
looked like the base library — which compiled it self-contained and failed with `CS0518` on every
predefined type.

### 3. How a normal project builds

A user project references the `Transpose.Build.Target` SDK, which runs `tps` once per project:

```
tps --project <proj.csproj> --configuration <cfg> --assembly-version <v>
```

The SDK passes `--incremental` by default; a project turns it off with
`<TransposeIncremental>false</TransposeIncremental>`.

`tps` reads the csproj directly (no MSBuild evaluation), globs `**/*.cs`, resolves
`PackageReference`s from the NuGet cache, synthesizes `[assembly: ...]` from `<AssemblyAttribute>`
items, transpiles, and writes the site (runtime + bundle + resources + `index.html`) or a package
DLL (`--emit-package`). **There is no compilation server** — the new compiler is a plain CLI, by
design. There *is* an opt-in build cache (`--incremental`, off by default) — see
**`TODO.incremental.md`** for what it reuses, why that is sound, and what it measures; a build with
the cache disabled behaves exactly as it always did.

Because there is no MSBuild evaluation, `ProjectXml` does the one bit of evaluation that changes
which files compile: it follows `<Import Project="…"/>` transitively and flattens the result, so a
**shared project**'s `.projitems` (where its `<Compile>` items live) is picked up. Each item is
expanded against the directory of the file that *declared* it for `$(MSBuildThisFileDirectory)`, and
against the project directory for a plain relative path — MSBuild's two rules. Any other `$(…)`
property is not guessed at: such an import or item is skipped, which is why SDK-internal imports
(`$(MSBuildToolsPath)…`) never get followed. Conditions are not evaluated anywhere in the resolver.

### `tps` CLI options (selected)

`--out/-o`, `--site-dir`, `--configuration/-c`, `--emit-package`, `--separate-assemblies`,
`--with-runtime`, `--reference/-r <dll>` (extra assemblies not in the NuGet cache),
`--define/-D <SYM>`, `--assembly-version <v>`, `--project/-p`, `--quiet/-q`,
`--incremental` / `--no-incremental` / `--cache-dir <dir>` (reuse the previous build; off by default),
`--timing` (per-phase time + allocations), `--timing-json <file>`,
`--metadata-only-assembly` / `--no-metadata-only-assembly`,
`--max-errors <n>` (a cap; by default **every** error is reported, ordered by file and line),
`--watch` / `--watch-port <n>` (rebuild a site on every source change — root project and every
referenced project — and serve it over Kestrel on localhost; the served index.html carries an
injected script that reconnects over a websocket and reloads the page after each rebuild, or swaps
the page's stylesheets in place when only CSS changed; see `Transpose.Compiler.Core/WatchSession.cs`
for the engine and `Transpose.Compiler/WatchMode.cs` for the dev server).

### Diagnostics are in MSBuild's canonical format

Every error and warning `tps` prints goes through `MsBuildDiagnostic` (`Transpose.Compiler`), which
writes the [canonical MSBuild/Visual Studio
form](https://learn.microsoft.com/visualstudio/msbuild/msbuild-diagnostic-format-for-tasks)
`Origin : Subcategory Category Code : Text`:

```
/src/App/Main.cs(17,20): error CS0103: The name 'x' does not exist in the current context
tps : error TPS0002: No .csproj found at '/src/Nope'.
```

MSBuild scans a tool's stdout **and** stderr line by line and promotes matching lines to real build
errors/warnings, so this is what makes a `tps` compile error land in the IDE's error list, navigable
to the file and line, instead of scrolling past as console text. Three rules follow from that:

- The **`error`/`warning` category is mandatory** and the file must be an **absolute** path — a
  relative one is resolved against the caller's working directory. Diagnostics with no source
  location are attributed to the tool (`tps`), with a `TPS####` code (errors `TPS0001`–`TPS0099`,
  warnings from `TPS0100`; the codes are a shipped contract, so retire rather than reuse one).
- The text must be **one line**; MSBuild matches per line, so a multi-line message would be
  truncated. A crash reports its exception chain on the diagnostic line and the stack frames
  separately.
- Everything else `tps` prints — progress, `--timing` tables, the "N error(s)" summary — must **not**
  match, or a build grows errors nobody wrote. `MsBuildDiagnosticFormatTests` guards both directions
  against MSBuild's own regex.

#### Why `dotnet build` looks silent (and why that is not fixable here)

Since .NET 9, `dotnet build` defaults to MSBuild's **terminal logger** whenever stdout is a terminal.
It renders *only* errors, warnings and a per-project summary: every `Message` and every line an
`Exec`ed tool writes is dropped at default verbosity **and** at `-v:normal`, whatever its importance
and regardless of `ConsoleToMsBuild`. Only `-v:detailed`/`-v:diagnostic` or `-tl:false` bring them
back — measured across all four channels (`Message` high/normal, `Exec` stdout, `Exec` stderr).

So the canonical diagnostic form above is exactly what makes a `tps` error visible; the progress
lines, the `OK — built …` summary and the timing table cannot be surfaced from a targets file at all.
This is **not** a regression against h5: an h5 project built with the real `h5` toolchain shows all of
its `[info] …` lines under `-tl:false` and **none** under `-tl:true`, identically — the two SDKs'
`Exec` invocations are the same shape. If you remember h5 printing them, that was the classic logger
(pre-.NET 9, a pipe, or CI).

What the SDK does about it: `_TransposeBuild` writes the compiler's full captured output to
`obj/<Configuration>/<tfm>/tps.log` on every build, and the failure error (`TPS1002`) points there.
For an interactive build that should show its work, use `dotnet build -tl:false`.

## Performance

A clean build's cost, and how to measure it, is documented in **`TODO.optimization.md`** (the running
log of what has been tried, including what did not work) and the **`transpose-performance`** skill.
The short version:

- `tps --timing` prints a per-phase breakdown with the bytes allocated in each phase, plus GC and
  peak-working-set totals. `--timing-json` writes the same machine-readably.
- `tps-bench` (`Transpose/Transpose.Bench`) reports the machine (CPU/cores/RAM/ISAs), scores it with a
  short deterministic CPU+memory benchmark, then times clean-slate builds and normalises every timing
  by that score so results from different machines are comparable. `ab.sh` interleaves two compilers.
- The compiler's runtime configuration is load-bearing: `<TieredPGO>false</TieredPGO>` and Server GC
  in `Transpose.Compiler.csproj` (plus `runtimeconfig.template.json`) are together worth ~40% of a
  build. Do not "tidy them away".
- Any change here must keep the emitted site **byte-identical** — output is reproducible, so
  `diff -r` against a baseline compiler is the gate. The emitted **assembly** is reproducible as well
  (`deterministic: true`), so the DLL is diffable too. The test suite is the other gate.
- The benchmark corpus is the **tesserae** submodule at `benchmarks/tesserae`
  (`git submodule update --init benchmarks/tesserae`). Recorded reports live in `docs/perf/`.
- **Debug and Release are structurally different builds.** Debug emits a *metadata-only* assembly
  (full metadata, `throw null` bodies — ~18% faster, and sound because a Transpose assembly binds
  against the stand-in BCL and can never execute); Release emits full IL. Consequently the SDK
  **refuses to package a Debug build**: `GeneratePackageOnBuild` is forced off and `dotnet pack -c Debug`
  fails with TPS1001. Pack Release.
- **The published tool is ReadyToRun.** `TransposePackRidSpecificTools=true` makes one `dotnet pack`
  produce a ReadyToRun `Transpose.Compiler.<rid>` package per RID plus the outer selector package;
  `dotnet tool install Transpose.Compiler` resolves the right one. That is worth ~1 s off *every*
  invocation, and `.devops/build-transpose-compiler.yml` gates on `tps-bench --verify-r2r` so it cannot
  be lost silently. Benchmark an R2R publish, not a `dotnet build` output, or you understate the
  shipped compiler.

## Known remaining work (compilation-related)

- **Runtime assembly build (done).** `tps --build-runtime` (auto-selected when a project's `tps.json`
  declares `outputBy: ClassPath`) transpiles `Transpose.BCL` into `Resources/.generated/*.js`,
  stitches those with the hand-written `Resources/*.js` primitives per `BCL/Transpose.BCL/tps.json`
  into `tps.js` + `tps.meta.js`, and emits `Transpose.dll` with those bundles embedded as manifest
  resources **through Roslyn's emitter** (the bundles are passed to `Compilation.Emit`, never via a
  Mono.Cecil post-process — Cecil's writer injects an `mscorlib` assembly reference that would stop
  Roslyn from treating the runtime as the corlib downstream, i.e. `CS0518: predefined type … not
  defined`). The result is a clean core library (zero assembly references) that doubles as the sole
  BCL reference. `dotnet build`/`pack` wraps it into the **`Transpose.BCL`** NuGet package
  (`lib/netstandard2.0/Transpose.dll`), and the translator tests run end-to-end against it.
- **`outputBy` file-layout modes** (Class/ClassPath/Namespace/…): `ClassPath` (used by the runtime
  build above) is implemented; the other layouts still emit a single bundle.
- **Bundle minification (done).** `outputFormatting` (`Formatted`/`Minified`/`Both`, read from
  `tps.json` and a merged `tps.<Configuration>.json` overlay) drives NUglify-based minification
  (pinned to `NUglify 1.21.15`; the legacy compiler used 1.20.7, but that version mis-parenthesised
  a `??` operand of `&&`/`||` and emitted invalid JS, fixed in NUglify 1.21.14 — not the newer
  1.22.0, which regressed by inserting a stray empty statement when unwrapping a braced if/else
  body) via `JsMinifier`. Packages ship
  their compiled JS in both a formatted and a pre-minified variant — the runtime (`tps.min.js` /
  `tps.meta.min.js`, embedded by `tps --build-runtime`) and library packages (`CollectEmbeddableItems`)
  — so a site build reuses those and only minifies the per-project bundle/metadata/shim itself; a
  package's JavaScript is never re-minified. `OutputBuilder` emits
  `index.html` (formatted) and `index.min.html` (minified) and collapses them per build configuration
  (Release keeps the minified one as `index.html`, Debug the formatted one) — a port of the legacy
  `HtmlGenerator`. **Source maps** for the emitted bundle are still remaining.

  **The `.js` ⇄ `.min.js` switch only applies to a file that exists in both variants.** That is what a
  compiled bundle always looks like, and it is also how a library declares an authored bundle it wants
  both variants of (Curiosity's `ExternalBundle.js` + `.min.js`). A resource that ships in **one**
  variant — Monaco's `editor.main.js`, a vendored `d3.min.js` — has no other variant to switch to and
  is copied through under its authored name in *every* configuration, whether the site build reads it
  from disk or extracts it from a referenced package. Renaming it would break the app: a module loader,
  a `new Worker(...)` or an import map fetches that file by a path the compiler does not rewrite
  (`PackageResourceVariantTests`).

  Two NUglify transforms are switched off through `CodeSettings.KillSwitch` because they are unsound
  for the JavaScript we emit, and `JsMinifierTests` guards both. Besides the `??`-under-`&&` collapse
  above, **`InvertIfReturn`/`InvertIfContinue`** rewrite a guard clause by moving every following
  statement into a *new* block (`if (c) return; rest…` → `if (!c) { rest… }`). That is fine for `var`
  and wrong for `let`: we emit locals as `let` (`LocalDeclKeyword`) and hoist a C# local function to
  the top of its block as a `var f = () => …` (it must be callable before its textual position), so
  the closure ends up *before* the guard and the `let` it captures ends up *after* it — the
  declaration is swept into the new block and the closure throws
  `ReferenceError: <name> is not defined`. Disabling both costs ~0.03% of bundle size.

  The lesson is more general than the two flags: **a minification bug is invisible to the test
  suite**, which runs the *formatted* output on Node — that output was valid JavaScript in both
  cases. Only `JsMinifierTests` exercises the minified form, so a bug reported against a
  `*.min.js` belongs there.
- **Value-copy semantics are scoped to in-source structs.** A struct copy (assignment, by-value
  argument/return, array fill, boxing, a collection insert, `with`) clones the value — including,
  since `StructAndClassInitializerTests`, its struct-typed slots recursively — but only for a struct
  **declared in the compilation being translated** (`IsSourceStruct` in `Emitter.Expressions.cs`).
  A struct from a referenced library, a BCL struct and a **ValueTuple** are copied by reference, so
  `var b = a; b.Inner.V = 9;` still writes through `a` for those. Widening it would make every
  `DateTime`/tuple assignment allocate, so it is a deliberate trade-off rather than an oversight.
- **`Span<T>` does not accept the implicit array conversion** — `Span<int> s = new int[3];` emits
  the bare array, so `s[0] = 1` throws "setItem is not a function". Unrelated to `stackalloc`;
  the conversion itself is simply not modelled. A span therefore reaches JS in one of two shapes —
  a real span object (built by a span constructor, e.g. through `string.AsSpan`) or the bare array —
  so a span helper has to normalise first (`TransposeR.spanArray`). `MemoryExtensions.SequenceEqual`
  does: C# resolves `someArray.SequenceEqual(other)` to *it* rather than to `Enumerable.SequenceEqual`
  (the array-to-span conversion beats array-to-`IEnumerable`), so that very common LINQ call would
  otherwise throw "getItem is not a function".
- **A boxed numeric loses its exact type.** Every JS number is a double, so `(object)1 is double` is
  true and `objects.OfType<double>()` also matches the boxed `int`s. `long`/`ulong`/`decimal` are
  real runtime objects and are unaffected, as are reference types and structs.
- **`dynamic` has no runtime overload resolver.** A generic call with a `dynamic` argument works when
  the method has one candidate (`Enumerable.Count(dyn)`); with numeric overloads to choose between
  (`Enumerable.Sum(dyn)`) there is no single binding and the emitted call does not exist.
- **The BCL's `ThrowHelper` messages are resource NAMES, not text.** `SR` is not ported, so a throw
  routed through `ThrowHelper.Throw*Exception(ExceptionResource.X)` reports "X" as its message where
  .NET reports a sentence. Sites whose message is user-facing (e.g. the duplicate key
  `ToDictionary` surfaces) call the keyed helper instead, which carries real text.
- **The modern LINQ surface lives in `EnumerableExtras` (done).** `Enumerable` is the external binding
  onto `linq.js`, whose API predates everything `System.Linq` gained after it, so those operators are
  implemented alongside it as plain transpiled C# in `BCL/Transpose.BCL/System/Linq/EnumerableExtras.cs`:
  `Append`, `Prepend`, `ToHashSet`, `Chunk`, `MinBy`, `MaxBy`, `DistinctBy`, `UnionBy`, `IntersectBy`,
  `ExceptBy`, `SkipLast`, `TakeLast`, `Order`, `OrderDescending`, `Index`, `CountBy`, `AggregateBy`,
  `TryGetNonEnumeratedCount`, `Shuffle`, `LeftJoin`, `RightJoin`, the tuple-returning `Zip` overloads,
  `ElementAt`/`ElementAtOrDefault` by `Index` / `Take` by `Range`, and the
  `FirstOrDefault`/`LastOrDefault`/`SingleOrDefault` overloads that take an explicit default (only for an
  `IEnumerable<T>` receiver — `EnumerableInstance`, what a chained query evaluates to, already binds those
  onto `linq.js`). Covered by `Linq/LinqModernOperatorTests`. `TryGetNonEnumeratedCount` carries the one
  documented difference: it answers true only for a real collection, where .NET also answers true for lazy
  operators whose count it can work out cheaply — false is a permitted answer, but it is not always the
  *same* answer.
- **A positional pattern only resolves members for tuples and records** — `x is Foo(1, 2)` against a
  type with a hand-written `Deconstruct(out …)` still reads `Item1`/`Item2` (see
  `PositionalPatternMemberNames`), because a pattern test is emitted as a single JS expression and
  cannot call `Deconstruct` with out-holders.
- **Reference resolution beyond the NuGet cache** — `<Reference HintPath>` and `tps.json`
  `references`/`referencesPath` (partially covered by `--reference`).
- **MSBuild evaluation** stays deliberately shallow: `<Import>` is followed (see above) but
  conditions, arbitrary properties, `Directory.Build.props` and item metadata are not evaluated.
- **Resource globs recurse with `**` (done).** A `resources` group's `files` entry may use a `**` path
  segment — `assets/img/**`, or `assets/img/**/*.svg` to narrow it — and a copy-through group then
  reproduces the sub-folder each file was found in, both in the site it writes and in the package DLL
  it embeds (the sub-directory lands in the manifest entry's `Path`, and *also* qualifies its `Name`,
  which is the manifest key — two files sharing a leaf name in different folders would otherwise
  collapse onto one entry). A plain `*` still matches one directory, which is what every tps.json
  written so far means by it; and because .NET's own search patterns collapse `**` to `*`, an older
  compiler reads a recursive pattern as the non-recursive one rather than failing.

  This is the *only* channel a package has for its assets: a library that instead let MSBuild's
  `<None … CopyToOutputDirectory>` carry them ships fine inside its own repository — a content item
  flows across a `ProjectReference` — and silently ships nothing to anyone consuming it as a NuGet
  package, which does not pack `None` items. Curiosity.FrontEnd did exactly that, so every application
  built against the package was missing 243 icons/illustrations, both variable-font families and the
  favicon.
- **Wider `tps.json` surface** (outputBy, module formats, locales, before/after build, etc.).
- **Incremental compilation (done for body-level edits; on by default via the SDK).** `--incremental`
  reuses the previous build of a project: nothing at all is compiled when every input hashes the same
  (19× on tesserae), and an edit confined to method/accessor bodies keeps the cached JavaScript of every
  untouched type, the reflection metadata, the scanner's denied-name filter and (in Debug) the
  metadata-only assembly (~2×). A body-only edit in a referenced library leaves its consumers'
  compilation cached too, because a package DLL now carries a `.tpsmeta` sidecar hashing the *metadata*
  a consumer actually binds against. Output is byte-identical either way, gated in CI over eight edit
  shapes. The `tps` CLI still defaults to off; the SDK opts in. Remaining: re-emitting only the
  *dependent* types when a declaration changes. See **`TODO.incremental.md`**.
- **Watch mode (done).** `tps --watch` rebuilds a site whenever a source file changes — the root
  project's own sources and every project it transitively references (via
  `ProjectResolver.ReferencedProjectsInBuildOrder`), debounced so one save triggers one rebuild — and
  serves the assembled site over a Kestrel dev server (`--watch-port`, default 4300) started with
  `Microsoft.AspNetCore.App` as a `FrameworkReference` (no `Sdk.Web` switch needed). `OutputBuilder`
  gets an optional `liveReloadScript` inlined before `</body>`; it opens a websocket to
  `/__tps-livereload` and acts on the message (`reload` → reload the page, `css` → re-fetch the
  stylesheets), and a monotonic build version embedded in the script lets a reconnecting client (e.g. one
  whose reload navigation overlapped a second rebuild) catch up immediately instead of waiting on a
  broadcast it could otherwise miss.

  The split is deliberate: `WatchSession` (`Transpose.Compiler.Core`) is the whole engine — watchers,
  debounce, the change classification, the reload hub and the injected script — and
  `Transpose.Compiler/WatchMode.cs` is *only* the dev server (static files + the websocket endpoint), so
  a host with its own web server drives the identical loop through `TransposeWatcher`
  (`Transpose.Compiler.Library`). `curiosity-cli serve --watch` in the **mosaik** repo is that host.

  **A CSS-only change skips the compiler.** When every file in a debounced batch is a source of a
  stylesheet the last successful build already produced from disk (a `tps.json` `resources` group, in the
  root project or in any project it references), the site's CSS is re-copied — byte for byte what a full
  build would have written, via `OutputBuilder.CssResources`/`WriteCssResources` — and the page is told
  `css` rather than `reload`, so the running app keeps its state. Anything else is a real build: a new
  stylesheet adds a `<link>` to index.html, a deleted one has to remove it, and a `.cs`/`.csproj`/tps.json
  edit obviously needs compiling. The build version deliberately does **not** advance for a CSS update,
  because index.html was not rewritten.

  Covered end to end by `WatchModeTests` in its own **`Transpose.WatchMode.Tests`** project (a real
  `tps --watch` subprocess + headless Chromium via Playwright): editing both the root and a referenced
  project and asserting the page reloads itself — never calling `page.ReloadAsync()` — and editing a
  stylesheet and asserting the computed style changes while a value set on `window` survives, i.e. the
  page was *not* reloaded and `app.js` was not even rewritten.

- **Reading a package's embedded resources never loads it (`OutputBuilder.AssemblyResources`).** The site
  build reads each reference's embedded JS/CSS through Mono.Cecil, not `Assembly.LoadFrom`. Loading an
  assembly to read its resources is fine for a one-shot CLI and broken for anything long-running: the
  file stays locked for the process's lifetime, so the *next* rebuild of a referenced project fails to
  write its DLL (`IOException`, which is exactly what watch mode used to do on its second rebuild of a
  multi-project app), and resolution is by assembly identity, so a re-read of a rebuilt DLL silently
  returns the copy loaded the first time.

A compilation server is still intentionally **out of scope** — though the measurements in
`TODO.incremental.md` say what it would be worth (the residual cost of an incremental build is almost
entirely Roslyn re-importing `Transpose.dll`'s metadata in a fresh process).

## Debugging, testing & auditing skills

Reusable workflows for working on the compiler live under `.claude/skills/` (auto-discovered by
Claude Code; read the `SKILL.md` directly in other contexts):

- **`transpose-debugging`** — set up the emit→Node→native-.NET loop (`scripts/setup-toolkit.sh` builds
  the Release translator + the `/tmp` emit/JSON/dump runners) and inspect/reproduce how a C# snippet
  compiles. Start here for any "what/why does Transpose emit X?" question. Captures the stale-build
  and runner-rebuild pitfalls.
- **`transpose-h5-audit`** — systematically hunt behavioural divergences against the proven-correct
  **h5** baseline, using the Curiosity front-end (in the *mosaik* repo) as the corpus. Includes the
  h5-JS corpus extraction, the "diverges from BOTH native AND h5" rule, the checklist areas, and the
  Newtonsoft JSON runner.
- **`transpose-runtime-and-bcl`** — rebuild the runtime (`--build-runtime`), add/modify BCL APIs
  (extern+`[Template]`+`Resources/*.js` vs. real C# in a non-external class + a `tps.json` bundle
  entry), and add/run regression tests in `EmitRegressionTests.cs`.
- **`transpose-performance`** — measure and improve *build* performance: the `tps-bench` harness
  (CPU-score-normalised, clean-slate scenarios), `--timing`, dotnet-trace allocation/CPU profiles, and
  the correctness gate that keeps emitted output byte-identical. Captures the measurement traps on this
  hardware (the host drifts 20–40%, so never trust a single run) and the cost structure of a build.
- **`write-changelog`** — produce the weekly, user-facing changelog for the whole product: read the
  week's `master` commits, classify them into the six product categories, fetch the published NuGet
  versions, and write `.changelog/<yy.M>/<yy.M.compilerBuild>.md` plus `.changelog/CurrentVersion`.
  Also mirrors the entry into the public docs site (`documentation/transpose/changelog/`) in Neko's
  changelog markup. Captures the NuGet-registry quirks (the flat-container index 404s for these ids;
  the response carries a BOM) and the fact that Transpose's changelog history starts at the week of
  2026-07-13 — anything earlier is h5.

## The changelog

`.changelog/` holds one user-facing file per week, named by the release revision it corresponds to
(`<yy.M>/<yy.M.compilerBuild>.md`, anchored on the newest `Transpose.Compiler` build in that week),
plus a `CurrentVersion` file carrying the current `yy.M` calendar version for the next release to
stamp. Both are produced by the **`write-changelog`** skill — read it before writing one by hand, and
mirror any new entry into `documentation/transpose/changelog/` so docs.curiosity.ai stays in sync.

## Conventions when editing

- Emitted JS identifiers use the `Transpose` global and `TransposeR` helpers; keep new emit code and
  the `tps.shim.js` in sync.
- Attribute-match string literals in the emitter (`"Transpose.TemplateAttribute"`, etc.) must match
  the C# namespaces in `Transpose.BCL`.
- The base assembly name is `Transpose`; `TransposeNaming.IsTransposeRuntimeAssembly` treats
  `Transpose` and `Transpose.*` as runtime/BCL packages (fixed JS names), distinct from user
  libraries compiled with `--emit-package`.
- Do not blanket-replace `h5`/`H5` — non-library tokens (HTML tags, hash locals) must be preserved.
- **Never emit a raw `(() => { … })()` wrapper.** Where a C# expression needs statements to emit (out/ref
  holders, object initializers, reordered named arguments, building a concrete collection, a throw
  expression), go through `OpenIife`/`CloseIife` (`Emitter.Expressions2.cs`) and pass the syntax that will
  be emitted *inside* the wrapper. If that syntax contains an `await`, the arrow must be `async` and the
  call awaited — a bare `await` in a plain arrow is a JavaScript **syntax** error, so one such expression
  makes the entire bundle fail to parse. `EmitRegressionTests.NoPlainArrowIifeWrapsAnAwait` guards this.
