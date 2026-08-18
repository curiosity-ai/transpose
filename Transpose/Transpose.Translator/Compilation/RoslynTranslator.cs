using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Transpose.Translator;

/// <summary>
/// Public entry point for the Roslyn-only C# → JavaScript translator.
///
/// Pipeline: parse + compile (Roslyn) → surface errors → scan for browser-incompatible
/// features → walk syntax tree guided by the semantic model → emit JavaScript.
/// </summary>
public sealed class RoslynTranslator
{
    /// <summary>
    /// Roslyn errors that are artifacts of compiling against the Transpose BCL (which targets a
    /// runtime feature-set narrower than the language) but are harmless when the output is
    /// untyped JavaScript. CS8830: covariant return types in overrides — no runtime type
    /// check exists in JS, so the override simply works.
    /// </summary>
    private static readonly HashSet<string> BenignForJs = new()
    {
        "CS8830", // covariant return types in overrides — no runtime type check in JS
        "CS5001", // no static Main — library-style snippets simply emit no entry point
        // async ValueTask: Transpose.dll's ValueTask lacks the async method-builder attribute, so
        // Roslyn (against the Transpose BCL) rejects it as a task-like return type and then reports
        // a missing return. We emit ValueTask exactly like Task (a Promise → tps.js Task), so
        // both are harmless. (The native comparison compiles against the real BCL, unaffected.)
        "CS1983", // return type of an async method must be void/Task/task-like…
        "CS0161", // not all code paths return a value (fallout of the above; JS returns undefined)
        "CS4032", // 'await' in a method with a non-task-like return (same ValueTask fallout)
        // Repeated `out var X` names that collapse into the same enclosing scope after hoisting
        // (CS0128). JS `var` allows redeclaration and each out-var is assigned immediately before
        // use, so a single shared binding is harmless — the legacy compiler tolerated this too.
        "CS0128", // a local variable named 'X' is already defined in this scope
    };

    /// <summary>Translate a single source file.</summary>
    public TranslationResult Translate(string source, string path = "App.cs", string assemblyName = CompilationBuilder.DefaultAssemblyName)
        => Translate(new[] { (path, source) }, assemblyName);

    /// <summary>Translate multiple source files into a single JS bundle.</summary>
    public TranslationResult Translate(IEnumerable<(string path, string text)> sources, string assemblyName = CompilationBuilder.DefaultAssemblyName)
        => Translate(sources, assemblyName, null);

    /// <summary>
    /// Translate multiple source files into a single JS bundle, referencing extra assemblies
    /// (e.g. tps.core, tps.Newtonsoft.Json) alongside Transpose.dll — used when compiling a real project.
    /// </summary>
    public TranslationResult Translate(
        IEnumerable<(string path, string text)> sources,
        string assemblyName,
        IEnumerable<string>? extraReferencePaths,
        IEnumerable<string>? preprocessorSymbols = null,
        LanguageVersion languageVersion = LanguageVersion.Latest,
        bool reflectionEnabled = true,
        MetadataTarget metadataTarget = MetadataTarget.Inline)
    {
        var r = BuildAssembly(sources, assemblyName, extraReferencePaths, preprocessorSymbols,
            languageVersion, reflectionEnabled, metadataTarget, emitAssembly: false);
        return new TranslationResult(r.Javascript, r.Diagnostics, r.MetadataJavascript);
    }

    /// <summary>
    /// Compiles a project as a distributable assembly: builds the Roslyn compilation once, then
    /// (optionally) emits the real .NET DLL AND translates the sources to JavaScript. This mirrors
    /// the existing compiler, where <c>tps</c> both produces the assembly and the JS that later
    /// gets embedded into it — so the assembly can be referenced by another project which extracts
    /// the JS back out.
    /// </summary>
    public AssemblyBuildResult BuildAssembly(
        IEnumerable<(string path, string text)> sources,
        string assemblyName,
        IEnumerable<string>? extraReferencePaths,
        IEnumerable<string>? preprocessorSymbols = null,
        LanguageVersion languageVersion = LanguageVersion.Latest,
        bool reflectionEnabled = true,
        MetadataTarget metadataTarget = MetadataTarget.Inline,
        bool emitAssembly = true,
        string? assemblyVersion = null,
        bool emitDebugInformation = true,
        bool metadataOnlyAssembly = false,
        IncrementalPlan? incremental = null,
        bool emitModules = false,
        string chunkDirectory = "chunks",
        IReadOnlyDictionary<string, string>? externalChunks = null,
        bool packageModules = false,
        IReadOnlyDictionary<string, List<string>>? externalSkipClusterDeps = null,
        int minChunkBytes = Emitter.DefaultMinChunkBytes,
        int maxChunkBytes = Emitter.DefaultMaxChunkBytes)
    {
        CompileProgress.Report("parsing sources + resolving references");
        var compilation = PhaseTimings.Measure("build compilation (parse + references)", () =>
            CompilationBuilder.Build(
                sources, assemblyName, languageVersion,
                extraReferencePaths: extraReferencePaths,
                preprocessorSymbols: preprocessorSymbols));

        // The declaration-surface hash of every file, for the *next* build's cache. Computed from the
        // trees this build already parsed, so it costs one extra walk rather than a second parse.
        var declarationHashes = incremental is null
            ? null
            : PhaseTimings.Measure("hash declaration surface", () => IncrementalPlan.DeclarationHashes(compilation.SyntaxTrees, incremental));

        // An incremental build only rescans and re-diagnoses the files whose text changed: a method
        // body cannot produce a diagnostic in another file, and the plan is only ever populated when
        // the declaration surface of every file is unchanged (see IncrementalPlan).
        var changedTrees = incremental is null
            ? null
            : compilation.SyntaxTrees.Where(incremental.IsChanged).ToList();

        var diagnostics = new List<Diagnostic>();

        // One semantic-model cache for the whole build, shared by the unsupported-feature scan and
        // the JS emitter. A SemanticModel retains the bound form of each member it is asked about, so
        // a member the scan binds (to resolve the inferred type behind a `var`, say) is already bound
        // when the emitter gets to it — the two passes bind the project once between them instead of
        // once each.
        var models = new TreeModel(compilation);

        // The scan runs before the assembly emit deliberately: it is the cheaper of the two and its
        // diagnostics are the ones a user most wants to see first. It runs even when Roslyn reported
        // errors — an unsupported construct (e.g. an [InlineArray] whose attribute the BCL keeps
        // internal) can surface a Roslyn error first, and the clear "… not supported" message should
        // still appear alongside it. Scanning a compilation with errors is safe (the scanner tolerates
        // missing symbols), but an unexpected throw must never lose the Roslyn errors.
        CompileProgress.Report("scanning for unsupported features");
        IReadOnlyList<Diagnostic> unsupported;
        try { unsupported = PhaseTimings.Measure("scan unsupported features", () => UnsupportedFeatureScanner.Scan(compilation, models, changedTrees, incremental)); }
        catch { unsupported = System.Array.Empty<Diagnostic>(); }

        // Binding the whole compilation is the single most expensive thing a build does, and
        // Compilation.Emit already does it — its result carries every declaration and method-body
        // diagnostic GetDiagnostics would have produced. So when an assembly is being emitted we take
        // the diagnostics from the emit and never bind twice; only a source-only build (the test
        // suite, `--out`) needs a standalone GetDiagnostics pass.

        CompileProgress.Report("binding + analyzing");

        byte[]? assemblyBytes = null;
        List<Diagnostic> roslynErrors;

        // A metadata-only assembly is a function of the declarations alone (it has no method bodies at
        // all — see ResolvedProject.MetadataOnlyAssembly), so an incremental build over a body-only
        // edit reuses the previous one byte for byte and skips the single most expensive Roslyn call a
        // build makes. The caller only ever supplies these bytes for a metadata-only emit.
        if (emitAssembly && incremental?.AssemblyBytes is { } reusedAssembly)
        {
            assemblyBytes = reusedAssembly;
            roslynErrors = new List<Diagnostic>();
        }
        else if (emitAssembly)
        {
            var asmCompilation = compilation.WithOptions(
                compilation.Options.WithOutputKind(OutputKind.DynamicallyLinkedLibrary));
            using var ms = new MemoryStream();
            // Include private members so a referencing project sees the full member set — the
            // overload numbering (e.g. $ctorN) must match what this assembly emits for itself,
            // and that numbering counts private overloads too.
            // Debug information is embedded in the PE (there is no separate .pdb alongside a tps
            // output) unless the project asked for none — see ResolvedProject.EmitDebugInformation.
            // Roslyn rejects an embedded PDB alongside a metadata-only emit (there are no bodies to
            // describe), so the metadata-only mode implies no debug information.
            var debugFormat = emitDebugInformation && !metadataOnlyAssembly
                ? Microsoft.CodeAnalysis.Emit.DebugInformationFormat.Embedded
                : default;
            var emit = PhaseTimings.Measure("bind + emit .NET assembly", () => asmCompilation.Emit(ms, options: new Microsoft.CodeAnalysis.Emit.EmitOptions(
                metadataOnly: metadataOnlyAssembly,
                includePrivateMembers: true,
                debugInformationFormat: debugFormat)));

            roslynErrors = emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error && !BenignForJs.Contains(d.Id))
                .ToList();

            // A failed emit stops before compiling method bodies when the *declarations* already have
            // errors, so its diagnostic list can be a subset of the full picture. Only on that (rare,
            // already-failing) path do we pay for a full GetDiagnostics, so the reported error list is
            // as complete as it always was.
            if (!emit.Success)
            {
                var full = PhaseTimings.Measure("bind + diagnostics (error path)", () => compilation.GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error && !BenignForJs.Contains(d.Id))
                    .ToList());
                // Diagnostic has reference equality, so the same error reported by both passes has to
                // be matched on what identifies it to a user — its id and location — or the list would
                // show every error twice.
                var already = new HashSet<(string, Location)>();
                foreach (var d in roslynErrors) already.Add((d.Id, d.Location));
                foreach (var d in full)
                    if (already.Add((d.Id, d.Location))) roslynErrors.Add(d);
            }
            else
            {
                assemblyBytes = ms.ToArray();
            }
        }
        else
        {
            roslynErrors = PhaseTimings.Measure("bind + diagnostics", () => compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error && !BenignForJs.Contains(d.Id))
                .ToList());
        }

        diagnostics.AddRange(unsupported);
        diagnostics.AddRange(roslynErrors);
        if (diagnostics.Count > 0 && diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return new AssemblyBuildResult(null, null, null, diagnostics);

        string? js = null, metadataJs = null;
        Emitter.ModuleOutput? moduleOutput = null;
        TranslationException? emitterFailure = null;
        try
        {
            var emitter = new Emitter(compilation, assemblyName, models, incremental)
            {
                ReflectionEnabled = reflectionEnabled,
                MetadataTarget = metadataTarget,
                AssemblyVersion = string.IsNullOrWhiteSpace(assemblyVersion) ? "1.0.0.0" : assemblyVersion!,
            };
            if (emitModules)
            {
                // Module mode carries its reflection metadata inside the entry module (it has to be
                // eager and cover every type, including the deferred ones), so there is no separate
                // metadata script and the "javascript" of this build is the entry module.
                CompileProgress.Report("emitting JavaScript modules");
                moduleOutput = PhaseTimings.Measure("emit JavaScript (modules)",
                    () => emitter.EmitModules(chunkDirectory, externalChunks, packageModules, externalSkipClusterDeps,
                                              minChunkBytes, maxChunkBytes));
                js = moduleOutput.EntryJs;
            }
            else
            {
                CompileProgress.Report("emitting JavaScript");
                js = PhaseTimings.Measure("emit JavaScript", () => emitter.Emit());
                metadataJs = emitter.MetadataScript;
            }
        }
        catch (TranslationException ex)
        {
            emitterFailure = ex;
        }

        // A metadata-only assembly emit never compiled the method bodies, so their diagnostics have
        // to come from somewhere else. They come from the semantic models — which the scan and the JS
        // emit have already populated, so this pass mostly reads cached bound trees rather than
        // binding again, and is an order of magnitude cheaper than the body codegen it replaced.
        // (It runs after the JS emit for exactly that reason; on the error path that means a broken
        // project does the JS emit first, which is why an emitter failure is held rather than thrown.)
        if (emitAssembly && metadataOnlyAssembly)
        {
            var bodyErrors = PhaseTimings.Measure("body diagnostics (semantic models)", () =>
            {
                var found = new System.Collections.Concurrent.ConcurrentBag<Diagnostic>();
                // On an incremental build only the changed files' bodies can have new diagnostics: an
                // unchanged body binds against an unchanged declaration surface, so its verdict from
                // the cached build still stands (and that build succeeded, or nothing was cached).
                Parallel.ForEach(changedTrees ?? (IEnumerable<SyntaxTree>)compilation.SyntaxTrees, tree =>
                {
                    foreach (var d in models.SemanticModelFor(tree).GetDiagnostics())
                        if (d.Severity == DiagnosticSeverity.Error && !BenignForJs.Contains(d.Id))
                            found.Add(d);
                });
                return found.ToList();
            });
            if (bodyErrors.Count > 0)
            {
                // Real compile errors explain an emitter failure far better than the emitter's own
                // "unsupported construct" would, so they are reported instead of it.
                diagnostics.AddRange(bodyErrors);
                return new AssemblyBuildResult(null, null, null, diagnostics);
            }
        }

        if (emitterFailure is not null)
        {
            diagnostics.Add(Diagnostics.Create(Diagnostics.Unsupported, emitterFailure.Location, emitterFailure.Message));
            return new AssemblyBuildResult(null, null, null, diagnostics);
        }

        return new AssemblyBuildResult(js, metadataJs, assemblyBytes, diagnostics)
        {
            DeclarationHashes = declarationHashes,
            Modules = moduleOutput,
        };
    }

    /// <summary>
    /// Builds the base runtime library (Transpose.BCL): compiles it self-contained (it defines the
    /// BCL, so no base reference), emits the real .NET reference assembly, and transpiles it with
    /// <c>outputBy: ClassPath</c> — one bare <c>Transpose.define</c> per non-external type plus the
    /// reflection metadata block. The caller stitches the per-class files with the hand-written
    /// <c>Resources/*.js</c> primitives into <c>tps.js</c> and embeds it into the assembly.
    /// </summary>
    public RuntimePackageResult BuildRuntimePackage(
        IEnumerable<(string path, string text)> sources,
        string assemblyName,
        IEnumerable<string>? preprocessorSymbols = null,
        LanguageVersion languageVersion = LanguageVersion.Latest,
        bool reflectionEnabled = true)
    {
        CompileProgress.Report("parsing the base library");
        var compilation = PhaseTimings.Measure("build compilation (parse, self-contained BCL)", () =>
            CompilationBuilder.Build(
                sources, assemblyName, languageVersion,
                preprocessorSymbols: preprocessorSymbols, selfContainedBcl: true));

        // Note for anyone optimizing this path: it binds the BCL three times over — here, again
        // through the emitter's semantic models, and a third time inside EmitAssembly below. The main
        // BuildAssembly path collapsed two of its binds (see the comments there); the same is possible
        // here but has not been done, because this build's diagnostics gate the JS emit and the
        // assembly emit happens last (it needs the assembled bundles as manifest resources).
        var diagnostics = new List<Diagnostic>();
        CompileProgress.Report("binding + analyzing the base library");
        diagnostics.AddRange(PhaseTimings.Measure("bind + diagnostics", () => compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error && !BenignForJs.Contains(d.Id))
            .ToList()));
        if (diagnostics.Count > 0)
            return new RuntimePackageResult(null, null, diagnostics);

        // The UnsupportedFeatureScanner is deliberately NOT run here: the base library *defines* the
        // BCL surface (System.Threading.Timer, System.IO stubs, …) as bindings backed by the
        // hand-written runtime, which is exactly what the scanner flags in user code.

        var asmCompilation = compilation.WithOptions(
            compilation.Options.WithOutputKind(OutputKind.DynamicallyLinkedLibrary));

        var emitter = new Emitter(compilation, assemblyName) { ReflectionEnabled = reflectionEnabled };
        CompileProgress.Report("emitting per-class JavaScript");
        var classPath = PhaseTimings.Measure("emit JavaScript (ClassPath)", emitter.EmitClassPath);

        // Emit the assembly with the runtime JS bundles embedded as manifest resources, through
        // Roslyn — see RuntimePackageResult.EmitAssembly for why this must not be a Mono.Cecil
        // post-process. No embedded debug info: the base library ships with none (matching its
        // csproj), and an embedded PDB makes the emitted corlib harder for Roslyn to consume.
        byte[] EmitAssembly(IReadOnlyList<(string name, byte[] bytes)> resources)
        {
            var descriptions = resources
                .Select(r => new ResourceDescription(r.name, () => new MemoryStream(r.bytes), isPublic: false))
                .ToList();
            using var ms = new MemoryStream();
            var emit = PhaseTimings.Measure("bind + emit runtime assembly (with bundles)", () =>
                asmCompilation.Emit(ms, manifestResources: descriptions,
                    options: new Microsoft.CodeAnalysis.Emit.EmitOptions(metadataOnly: false, includePrivateMembers: true)));
            if (!emit.Success)
            {
                var errors = string.Join("\n", emit.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error && !BenignForJs.Contains(d.Id))
                    .Select(d => d.GetMessage()));
                throw new TranslationException($"Emitting the runtime assembly failed:\n{errors}");
            }
            return ms.ToArray();
        }

        return new RuntimePackageResult(EmitAssembly, classPath, diagnostics);
    }

    /// <summary>
    /// Convenience: translate and throw with a readable message if anything failed.
    /// Mirrors the behavior tests expect (compilation failures throw).
    /// </summary>
    public string TranslateOrThrow(string source, string path = "App.cs")
    {
        var result = Translate(source, path);
        if (!result.Success)
        {
            var messages = string.Join("\n", result.Errors.Select(d => d.GetMessage()));
            throw new TranslationException(messages.Length > 0 ? messages : "Translation failed.");
        }
        return result.Javascript!;
    }

    private static string? _shim;

    /// <summary>
    /// The full runtime prelude: the real tps.js followed by the thin TransposeR shim that
    /// adapts the emitter's language-level helpers onto tps.js primitives.
    /// </summary>
    public static string LoadRuntime()
    {
        return TransposeAssemblies.RuntimeJs + "\n" + RuntimeShim;
    }

    /// <summary>The thin TransposeR shim (the emitter's language-level helpers over tps.js primitives),
    /// loaded once. A site build ships this as its own script after tps.js and before the bundle.</summary>
    public static string RuntimeShim => _shim ??= ReadShim();

    private static string ReadShim()
    {
        var asm = typeof(RoslynTranslator).Assembly;
        var name = asm.GetManifestResourceNames().First(n => n.EndsWith("tps.shim.js", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
