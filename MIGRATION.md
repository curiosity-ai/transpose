# Migrating a project from h5 to Transpose

Transpose is the next generation of the [h5](https://github.com/curiosity-ai/h5)
C#-to-JavaScript compiler. The compiler was rebuilt around a clean-room **Roslyn**
translator (the legacy Bridge/NRefactory pipeline is gone), and the project was
rebranded from **H5** to **Transpose**. Migrating an existing h5 project is
mostly a mechanical rename of packages, the config file, and namespaces — the
codegen attributes, the `Script.Write` interop surface, and your component code
behave the same.

This guide covers everything that changes. It is based on the real migration of
the [Tesserae](https://github.com/curiosity-ai/tesserae) UI toolkit and its
sample app.

---

## What changed at a glance

| Concept | h5 | Transpose |
| --- | --- | --- |
| Runtime global (generated JS) | `H5` | `Transpose` |
| Language-helper shim | `H5R` | `TransposeR` |
| Runtime bundle / config / module | `h5.js` / `h5.json` / `h5` | `tps.js` / `tps.json` / `tps` |
| Compiler command | `h5` (`h5-compiler` global tool) | `tps` (invoked by the SDK) |
| Codegen attribute namespace | `H5` | `Transpose` |
| Web-API bindings namespace | `H5.Core` | `Transpose.Core` |
| Conditional-compilation symbol | `H5` | `Transpose` |

### NuGet package / SDK mapping

| h5 package | Transpose package | Notes |
| --- | --- | --- |
| `h5.Target` (SDK) | `Transpose.Build.Target` | MSBuild SDK that runs `tps` per project |
| `h5` (base library) | **`Transpose.BCL`** | package id is `Transpose.BCL`; the assembly/DLL stays `Transpose.dll` |
| `h5.Core` | `Transpose.Core` | DOM / ES5 / ES6 bindings |
| `h5.Newtonsoft.Json` | `Transpose.Newtonsoft.Json` | |
| `h5.WebGL2` | `Transpose.WebGL2` | |
| `h5.template` | `Transpose.Template` | `dotnet new` template |

> The base library is the one exception to "package id matches assembly name":
> its package id is **`Transpose.BCL`** but its assembly is still `Transpose`
> (so `Transpose.dll` / runtime assembly `Transpose` are unchanged).

---

## Step 1 — Update the project file

Change the SDK and the package references. An h5 project like:

```xml
<Project Sdk="h5.Target/*">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="h5" Version="*" />
    <PackageReference Include="h5.Core" Version="*" />
    <PackageReference Include="h5.Newtonsoft.Json" Version="*" />
  </ItemGroup>
</Project>
```

becomes:

```xml
<Project Sdk="Transpose.Build.Target/*">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Transpose.BCL" Version="*" />
    <PackageReference Include="Transpose.Core" Version="*" />
    <PackageReference Include="Transpose.Newtonsoft.Json" Version="*" />
  </ItemGroup>
</Project>
```

Notes:
- Replace `*` with the latest published versions (see the badges in the README).
- `netstandard2.0` and `netstandard2.1` both work. The SDK inherits
  `LangVersion=latest`; pin `<LangVersion>` only if you need an older one.
- If you had `<UpdateH5>false</UpdateH5>`, drop it — there is no global-tool
  auto-update to disable (see Step 4).
- A **library** you distribute as a package keeps `<GeneratePackageOnBuild>true</GeneratePackageOnBuild>`;
  Transpose emits it as a .NET DLL with the compiled JS embedded, which
  referencing projects consume directly.

## Step 2 — Rename `h5.json` → `tps.json`

Rename the config file and update the token inside it. The schema is otherwise
the same (`output`, `fileName`, `html`, `reflection`, `resources`, …) minus
`outputFormatting`, which no longer exists — see below.

- `output`: `"$(OutDir)/h5/"` → `"$(OutDir)/tps/"`
- Any resource `files`/paths that referenced an `h5/…` asset folder now
  reference `tps/…`. If you keep your web assets under a folder named `h5/`,
  rename it to `tps/` (or repoint the paths) so the two stay consistent.

```jsonc
{
    "output": "$(OutDir)/tps/",
    "fileName": "app.js",
    "resources": [
        { "name": "app.css", "files": [ "tps/assets/css/app.css" ] }
    ]
}
```

A per-configuration overlay is supported: `tps.Release.json` (or
`tps.<Configuration>.json`) is merged on top of `tps.json` for that build.

**`outputFormatting` is gone.** What shape the JavaScript takes follows from the
build instead: a Debug site is one formatted bundle (and never chunked modules,
whatever `outputBy` says), a Release site is one minified bundle — or chunked
modules if `outputBy` asks for them — and a **library ships all three**, so the
application referencing it picks the variant matching its own configuration. An
h5/Bridge project that declared its own compiled `.js` and `.min.js` as resources
to force both into the package can simply delete those entries; if such an entry
was there to keep the bundle *out* of index.html (a `.dontload` name), say
`"loadCompiledOutput": false` instead.

### Resources you load yourself

h5 marked a resource that must be copied but *not* referenced from `index.html`
by suffixing its name: `"name": "lazy-module.js.dontload"`. Transpose still
honours that suffix, and adds a plain flag on the group — use whichever you
prefer (either one alone suppresses the injection):

```jsonc
{
    "resources": [
        { "name": "lazy-module.js", "files": [ "tps/assets/js/lazy-module.js" ], "load": false },
        { "name": "theme-dark.css", "files": [ "tps/assets/css/dark.css" ],      "load": false }
    ]
}
```

It applies to every resource kind the generated HTML can load — scripts and
stylesheets — and it survives packaging: the flag is recorded in the resource
manifest embedded in the package DLL, so a project referencing the library
extracts the file without auto-loading it either.

### Cleaning the output folder

h5's `cleanOutputFolderBeforeBuild` / `cleanOutputFolderBeforeBuildPattern`
deleted files matching a glob **before** compiling. Transpose replaces that with
a safer, zero-config `cleanOutputFolder` (default **on**): after a successful
build it diffs the output folder against exactly the files this build produced
and removes only the leftovers — a bundle you renamed, a `.min` variant a
`Formatted` build no longer emits, a resource you deleted, a stale
`index.min.html`. Nothing the current build wrote is ever touched, and a build
that fails leaves the previous output intact. Drop the old keys; they are no
longer read. To keep hand-placed files that live in the output folder, list
them under `cleanOutputFolderExclude` (glob patterns, the equivalent of h5's
`!` skip patterns); to disable pruning entirely, set `"cleanOutputFolder": false`.

```jsonc
{
    "output": "$(OutDir)/tps/",
    "cleanOutputFolder": true,                    // the default; set false to keep stale files
    "cleanOutputFolderExclude": [ "favicon.ico", "vendor/*" ]
}
```

## Step 3 — Update source code

The change is almost entirely in `using` directives:

- `using H5;` → `using Transpose;`
- `using H5.Core;` → `using Transpose.Core;`
- Fully-qualified references `H5.Something` → `Transpose.Something`,
  `H5.Core.Something` → `Transpose.Core.Something`.
- Conditional compilation: `#if H5` → `#if TRANSPOSE` (Transpose defines the
  `TRANSPOSE` symbol during transpilation).

Things that **do not** change (they are unqualified names or preserved tokens):

- Codegen attributes: `[External]`, `[Name(...)]`, `[Template(...)]`,
  `[Script(...)]`, `[ObjectLiteral]`, `[GlobalMethods]`, `[Scope]`, `[Enum(...)]`
  — same names, now in namespace `Transpose`, so a single `using Transpose;`
  covers them.
- Raw JS interop: `Script.Write<T>("…")` / `Script.Write("…")` are unchanged.
- The runtime helper types you call by their short name.

### Do NOT blanket-replace `h5` / `H5`

A few tokens are deliberately **not** library identifiers and must be preserved.
A global search-and-replace of `h5`/`H5` will break them:

- The **`<h5>` HTML tag** (and any helper named `H5` that emits it) — it is the
  HTML heading element, not the compiler.
- Hash locals such as `h1..h5` (e.g. in `ValueTuple` hashing).
- Any user identifier that merely happens to contain `h5`.

Prefer targeted replacements (`using H5;`, `using H5.Core;`, `H5.`, `H5R`,
`#if H5`, `h5.json`, `h5.js`) over a blind rename.

### Hand-written JavaScript / embedded resources

If you ship hand-written JS (via `[Script]`, embedded resource files, or a
`resources` bundle) that reaches into the runtime, update the globals:

- `H5.` → `Transpose.` (e.g. `H5.assembly(...)` → `Transpose.assembly(...)`)
- `H5R.` → `TransposeR.`
- References to the runtime file `h5.js` → `tps.js`.

## Step 4 — Build and run

There is **no global-tool compiler and no compilation server** anymore. The
`Transpose.Build.Target` SDK runs the `tps` CLI once per project as part of a
normal `dotnet build`:

```bash
dotnet build
```

Output lands under `bin/<Configuration>/<tfm>/tps/` (a runnable site: the `tps.js`
runtime + your `app.js` bundle + resources + `index.html`, formatted in Debug and
minified in Release). Serve it like before:

```bash
cd bin/Debug/netstandard2.0/tps/
dotnet serve --port 5000
```

To start a project from scratch instead of migrating, use the template:

```bash
dotnet new install Transpose.Template
dotnet new transpose
```

### Referenced projects are compiled once

When project **B** references project **A**, a Transpose site build for **B**
consumes **A**'s already-built package DLL (extracting its embedded JS) rather
than recompiling **A**'s sources into **B**'s bundle. `dotnet build` compiles
**A** first, then **B** reuses it — so editing **B** re-transpiles only **B**.

---

## Behavioral notes & known gaps

- **Retyped / Bridge packages** are not supported (unchanged from h5).
- **Source maps** for the emitted bundle are not emitted yet.
- Some `tps.json` surface is still narrowing in (module formats, locales,
  before/after-build hooks); the common `output` / `fileName` / `html` /
  `reflection` / `resources` / `cleanOutputFolder` fields are supported.
- Reference resolution beyond the NuGet cache (`<Reference HintPath>` and
  `tps.json` `references`/`referencesPath`) is partial; the `tps --reference`
  flag covers the common cases.

If you hit a construct that transpiled under h5 but is rejected or emitted
differently by Transpose, please open an issue with a minimal repro.
