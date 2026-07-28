#  Transpose 🚀 - C# to JavaScript compiler

<a href="https://github.com/curiosity-ai/transpose"><img src="https://raw.githubusercontent.com/curiosity-ai/transpose/master/logo/transpose-512.png" width="120" height="120" align="right" /></a>

Transpose is a modern **C# → JavaScript** compiler built entirely on **Roslyn**.
It is the next generation of the [h5](https://github.com/curiosity-ai/h5) project
(itself a fork of the original [Bridge](https://github.com/bridgedotnet/bridge)
compiler), rebranded and rebuilt around a clean-room Roslyn translator. The
legacy Bridge/NRefactory pipeline has been removed — Transpose is *solely* the
Roslyn-based translator and its CLI compiler (`tps`).

The compiler runs on .NET 10.0; Transpose projects target .NET Standard 2.0/2.1.
Transpose targets a fast, integrated development experience for C# web
developers.

> **Coming from h5?** See **[MIGRATION.md](https://github.com/curiosity-ai/transpose/blob/master/MIGRATION.md)** for a step-by-step
> guide to porting an existing h5 project.

|  Package | NuGet           |
| -------------: |:-------------:|
| Base Library | [![Nuget](https://img.shields.io/nuget/v/Transpose.BCL.svg?maxAge=0&colorB=brightgreen)](https://www.nuget.org/packages/Transpose.BCL/) |
| Core Library | [![Nuget](https://img.shields.io/nuget/v/Transpose.Core.svg?maxAge=0&colorB=brightgreen)](https://www.nuget.org/packages/Transpose.Core/) |
| SDK Target | [![Nuget](https://img.shields.io/nuget/v/Transpose.Build.Target.svg?maxAge=0&colorB=brightgreen)](https://www.nuget.org/packages/Transpose.Build.Target/) |
| Json Library | [![Nuget](https://img.shields.io/nuget/v/Transpose.Newtonsoft.Json.svg?maxAge=0&colorB=brightgreen)](https://www.nuget.org/packages/Transpose.Newtonsoft.Json/) |
| Template | [![Nuget](https://img.shields.io/nuget/v/Transpose.Template.svg?maxAge=0&colorB=brightgreen)](https://www.nuget.org/packages/Transpose.Template/) |
| Compiler as a Library | [![Nuget](https://img.shields.io/nuget/v/Transpose.Compiler.Service.svg?maxAge=0&colorB=brightgreen)](https://www.nuget.org/packages/Transpose.Compiler.Service/) |
| UI Toolkit | [![Nuget](https://img.shields.io/nuget/v/tesserae.svg?maxAge=0&colorB=brightgreen)](https://www.nuget.org/packages/tesserae/)|

> The base library's package id is **`Transpose.BCL`**, but its assembly is
> `Transpose` (so the DLL stays `Transpose.dll` and the runtime global in
> generated JS is `Transpose`).

##  Getting Started ⚡

A Transpose project references the `Transpose.Build.Target` SDK, which runs the
`tps` compiler as part of a normal `dotnet build` — there is no global-tool
compiler to install and no compilation server. Start from this project shape
(replace `*` with the latest published versions):

````xml
<Project Sdk="Transpose.Build.Target/*">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Transpose.BCL" Version="*" />
    <PackageReference Include="Transpose.Core" Version="*" />
  </ItemGroup>
</Project>
````

Build it with a plain:

````bash
dotnet build
````

The output (the `tps.js` runtime, your `app.js` bundle, resources, and
`index.html`) lands under `bin/<Configuration>/<tfm>/tps/`. Serve it locally:

````bash
cd bin/Debug/netstandard2.0/tps/
dotnet serve --port 5000
````

You can also install the `dotnet new` template and scaffold a project:

````bash
dotnet new install Transpose.Template
dotnet new transpose
````

### How a build works

`tps` reads the `.csproj` directly (no MSBuild evaluation), globs `**/*.cs`,
resolves `PackageReference`s from the NuGet cache, transpiles, and writes either
a runnable site or — for a library — a .NET DLL with the compiled JS embedded.
Behavior is configured per project by a **`tps.json`** file (output path,
`fileName`, `html`, `reflection`, `resources`, `outputFormatting`).

When a project references another project, the site build consumes the
referenced project's already-built package DLL (extracting its compiled JS)
instead of recompiling its sources — so a dependency is compiled once and reused.

### Compiling from your own .NET application

`Transpose.Compiler.Service` lets a .NET application compile C# source held in
memory to JavaScript in process, with no `tps` process, `.csproj`, or disk I/O
involved:

````xml
<PackageReference Include="Transpose.Compiler.Service" Version="*" />
````

````csharp
using Transpose.Compiler.Service;

var result = TransposeCompilerService.Compile(
    new CompilationRequest("App")
        .WithSourceFile("Program.cs", "System.Console.WriteLine(\"Hello!\");"));

if (result.Success) Console.WriteLine(result.Javascript);
else foreach (var error in result.Errors) Console.Error.WriteLine(error);
````

`CompilationRequest` also supports `.WithPackageReference(id, version)` (resolved
from the local NuGet cache, exactly like a csproj `<PackageReference>`),
`.WithReferenceAssembly(path)`, `.WithRuntime()` (prepend the `tps.js` runtime so
the output is directly runnable), and `.AsPackageAssembly()` (also emit a .NET
assembly with the JS embedded, like `tps --emit-package`). An async
`CompileAsync` is available too; concurrent calls in one process are queued and
run one at a time (see the type's XML docs for why).

## Samples

The [Tesserae](https://github.com/curiosity-ai/tesserae) UI toolkit and its
sample app are built with Transpose and are a good end-to-end reference.

##  Relationship to h5 📜

Transpose is the evolution of h5, with two large changes:

- **Roslyn rebuild.** The translator was rewritten as a clean-room Roslyn
  compiler. The emitter walks Roslyn syntax trees guided by the semantic model
  and emits JavaScript directly — there is no NRefactory and no `SharpSixRewriter`
  lowering pass.
- **Rebrand.** `H5` → `Transpose` (namespaces, runtime global, assembly names)
  and `h5` → `tps` (runtime file, config file, module name, compiler command).
  A handful of non-library tokens are deliberately preserved — the `<h5>` HTML
  tag binding and hash locals `h1..h5` — so they are *not* renamed.

The compiler is a plain CLI: **caching and the hosted compilation server are
out of scope by design**. **Retyped/Bridge packages are not supported.**

Package/SDK renames (h5 → Transpose):

- `h5` → **`Transpose.BCL`** (assembly stays `Transpose`)
- `h5.Core` → `Transpose.Core`
- `h5.Target` (SDK) → `Transpose.Build.Target`
- `h5.Newtonsoft.Json` → `Transpose.Newtonsoft.Json`
- `h5.WebGL2` → `Transpose.WebGL2`
- `h5.template` → `Transpose.Template`

See **[MIGRATION.md](https://github.com/curiosity-ai/transpose/blob/master/MIGRATION.md)** for the full porting guide.
