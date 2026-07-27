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
│   ├── Program.cs             #   arg parsing, orchestration, output modes
│   ├── ProjectResolver.cs     #   reads the .csproj (raw XML), globs sources, resolves references
│   ├── OutputBuilder.cs       #   site build (runtime + bundle + resources + index.html) + stale-output prune
│   ├── ResourceEmbedder.cs    #   embeds JS + resources into the package DLL (Mono.Cecil)
│   └── TransposeJson.cs       #   reads tps.json (output/fileName/html/resources/reflection)
├── Transpose.Bench/           # Benchmark harness. AssemblyName + tool command: `tps-bench`
│   ├── MachineInfo.cs         #   CPU model / cores / RAM / SIMD-ISA detection
│   ├── CpuScore.cs            #   short deterministic CPU+memory benchmark -> normalisation score
│   ├── Scenario.cs            #   clean-slate `tps` runs (wipes bin/obj of the project closure)
│   ├── R2RCheck.cs            #   is this tps (or .nupkg) ReadyToRun-compiled?
│   ├── Report.cs              #   console / Markdown / JSON output + --baseline comparison
│   └── ab.sh                  #   interleaved A/B of two tps binaries on one project
├── Transpose.Build.Target/    # MSBuild SDK (package id Transpose.Build.Target) that invokes `tps`
├── Transpose.Template/        # `dotnet new` template (package id Transpose.Template)
└── Transpose.Translator.Tests/# MSTest suite; transpiles snippets and diffs vs native .NET via Playwright

BCL/                           # The base runtime libraries
├── Transpose.BCL/             # Base library. AssemblyName Transpose, package id `Transpose.BCL`, namespace Transpose
│   ├── Transpose/             #   codegen attributes ([External],[Template],[Name],[Script],...) + markers
│   ├── System/, shared/       #   C# definitions of the .NET BCL (System.Object, string, collections, ...)
│   ├── Resources/*.js         #   hand-written JavaScript runtime primitives (Core.js, Class.js, ...)
│   └── tps.json               #   declares how Resources + generated JS combine into tps.js
└── Transpose.Core/            # Web API bindings (DOM, ES5/ES6). package id `Transpose.Core`, ns Transpose.Core

Packages/                      # Additional binding libraries (all [assembly: External])
├── Transpose.Newtonsoft.Json/ #   package Transpose.Newtonsoft.Json
├── Transpose.Howler/          #   package Transpose.Howler
├── Transpose.WebGL2/          #   package Transpose.WebGL2
├── Transpose.P2/              #   package Transpose.P2
├── Transpose.HttpClient/      #   package Transpose.HttpClient
└── Transpose.Placeholders/    #   placeholder attributes (package Transpose.Placeholders)

benchmarks/tesserae/           # git submodule: the benchmark corpus (see Performance below)
docs/perf/                     # recorded tps-bench reports (baseline + current)
.devops/                       # Azure DevOps pipelines (one per package, plus the benchmark)

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

This builds `Transpose.Translator`, the `tps` compiler, the tests, and the template. (The
`Transpose.Build.Target` SDK and the BCL/Packages projects are **not** in the solution: their
`Sdk="Transpose.Build.Target/..."` is not a resolvable NuGet package in a dev tree, and the BCL is
compiled by `tps`, not `dotnet`.)

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
`--max-errors <n>` (a cap; by default **every** error is reported, ordered by file and line).

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
  — so a site build reuses those and only minifies the per-project bundle/metadata/shim itself (with a
  compile-time fallback for older packages that predate the `.min.js`). `OutputBuilder` emits
  `index.html` (formatted) and `index.min.html` (minified) and collapses them per build configuration
  (Release keeps the minified one as `index.html`, Debug the formatted one) — a port of the legacy
  `HtmlGenerator`. **Source maps** for the emitted bundle are still remaining.
- **Reference resolution beyond the NuGet cache** — `<Reference HintPath>` and `tps.json`
  `references`/`referencesPath` (partially covered by `--reference`).
- **MSBuild evaluation** stays deliberately shallow: `<Import>` is followed (see above) but
  conditions, arbitrary properties, `Directory.Build.props` and item metadata are not evaluated.
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
