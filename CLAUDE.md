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
│   ├── OutputBuilder.cs       #   site build (runtime + bundle + resources + index.html)
│   ├── ResourceEmbedder.cs    #   embeds JS + resources into the package DLL (Mono.Cecil)
│   └── TransposeJson.cs       #   reads tps.json (output/fileName/html/resources/reflection)
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

docs/, logo/, lib/, External-less # docs, transpose.png/svg, misc
```

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

`tps` reads the csproj directly (no MSBuild evaluation), globs `**/*.cs`, resolves
`PackageReference`s from the NuGet cache, synthesizes `[assembly: ...]` from `<AssemblyAttribute>`
items, transpiles, and writes the site (runtime + bundle + resources + `index.html`) or a package
DLL (`--emit-package`). **There is no compilation server and no cache** — the new compiler is a
plain CLI, by design.

### `tps` CLI options (selected)

`--out/-o`, `--site-dir`, `--configuration/-c`, `--emit-package`, `--separate-assemblies`,
`--with-runtime`, `--reference/-r <dll>` (extra assemblies not in the NuGet cache),
`--define/-D <SYM>`, `--assembly-version <v>`, `--project/-p`, `--max-errors`, `--quiet/-q`.

## Known remaining work (compilation-related)

- **Runtime assembly must be a clean corlib.** `tps --build-runtime` (auto-selected when a project's
  `tps.json` declares `outputBy: ClassPath`) already transpiles `Transpose.BCL` into
  `Resources/.generated/*.js`, stitches those with the hand-written `Resources/*.js` primitives per
  `BCL/Transpose.BCL/tps.json` into `tps.js` + `tps.meta.js`, embeds them into `Transpose.dll`, and
  `dotnet build`/`pack` wraps that DLL into the **`Transpose.BCL`** NuGet package. The remaining gap:
  the DLL it emits is not a clean core library — Roslyn adds an `mscorlib` assembly reference (and the
  Mono.Cecil resource-embed step re-adds one), so Roslyn will not treat it as the corlib downstream
  (`CS0518: predefined type … not defined`). The translator tests therefore still need a clean
  `NoStdLib` reference assembly (as `bootstrap.sh` builds via csc) with `tps.js` embedded at compile
  time; making `tps --build-runtime` emit such a corlib directly is the main outstanding item.
- **`outputBy` file-layout modes** (Class/ClassPath/Namespace/…): `ClassPath` (used by the runtime
  build above) is implemented; the other layouts still emit a single bundle.
- **Bundle minification and source maps** for the emitted bundle (Release only selects pre-minified
  resource variants today).
- **Reference resolution beyond the NuGet cache** — `<Reference HintPath>` and `tps.json`
  `references`/`referencesPath` (partially covered by `--reference`).
- **Wider `tps.json` surface** (outputBy, module formats, locales, before/after build, etc.).

Caching and the compilation server are intentionally **out of scope**.

## Conventions when editing

- Emitted JS identifiers use the `Transpose` global and `TransposeR` helpers; keep new emit code and
  the `tps.shim.js` in sync.
- Attribute-match string literals in the emitter (`"Transpose.TemplateAttribute"`, etc.) must match
  the C# namespaces in `Transpose.BCL`.
- The base assembly name is `Transpose`; `TransposeNaming.IsTransposeRuntimeAssembly` treats
  `Transpose` and `Transpose.*` as runtime/BCL packages (fixed JS names), distinct from user
  libraries compiled with `--emit-package`.
- Do not blanket-replace `h5`/`H5` — non-library tokens (HTML tags, hash locals) must be preserved.
