using System.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Transpose.Compiler;

/// <summary>
/// Where a build's human-readable progress goes. Defaults to the console — what the <c>tps</c> CLI
/// wants — and is overridden by a hosting application (via <c>Transpose.Compiler.Library</c>) that would
/// rather collect the lines than have them appear on its own console. Only ordinary progress output goes
/// here; diagnostics keep their canonical MSBuild form and their own sink
/// (<see cref="MsBuildDiagnostic.Sink"/>), because a build tool's errors are a contract, not chatter.
/// </summary>
internal class BuildLog
{
    /// <summary>The default log: progress on stdout, failures on stderr.</summary>
    public static readonly BuildLog Console = new();

    /// <summary>A log that discards everything.</summary>
    public static readonly BuildLog Silent = new SilentLog();

    public virtual void Info(string message) => System.Console.WriteLine(message);
    public virtual void Error(string message) => System.Console.Error.WriteLine(message);

    private sealed class SilentLog : BuildLog
    {
        public override void Info(string message) { }
        public override void Error(string message) { }
    }
}

/// <summary>
/// Everything one build of one project needs to know: which project, which configuration, and the
/// output shape. Mirrors the <c>tps</c> command line one-to-one — the CLI parses arguments into this and
/// nothing else — so a library caller has exactly the same knobs as the tool.
/// </summary>
internal sealed record BuildOptions
{
    /// <summary>Absolute path to the <c>.csproj</c> to build.</summary>
    public required string CsprojPath { get; init; }

    /// <summary>Write a single JavaScript bundle to this path instead of assembling a site or a package
    /// (<c>--out</c>). Null for the normal site/package build.</summary>
    public string? OutPath { get; init; }

    /// <summary>Override the site output directory tps.json's <c>output</c> would resolve to
    /// (<c>--site-dir</c>).</summary>
    public string? SiteDir { get; init; }

    public string Configuration { get; init; } = "Debug";

    /// <summary>Prepend the tps.js runtime to a <see cref="OutPath"/> bundle (<c>--with-runtime</c>).</summary>
    public bool WithRuntime { get; init; }

    /// <summary>Suppress warning output (<c>--quiet</c>).</summary>
    public bool Quiet { get; init; }

    /// <summary>Cap on how many individual errors are reported; 0 — the default — reports every one.</summary>
    public int MaxErrors { get; init; }

    /// <summary>Compile the project as a distributable package assembly (<c>--emit-package</c>).</summary>
    public bool EmitPackage { get; init; }

    /// <summary>Consume referenced projects as their built DLLs rather than recompiling their sources.</summary>
    public bool SeparateAssemblies { get; init; }

    /// <summary>Build the base runtime package (<c>--build-runtime</c>): only the BCL does this.</summary>
    public bool BuildRuntime { get; init; }

    /// <summary>Extra reference assemblies outside the NuGet cache (<c>--reference</c>).</summary>
    public IReadOnlyList<string> ExtraReferences { get; init; } = Array.Empty<string>();

    /// <summary>Extra preprocessor symbols (<c>--define</c>).</summary>
    public IReadOnlyList<string> ExtraDefines { get; init; } = Array.Empty<string>();

    public string? AssemblyVersion { get; init; }

    /// <summary>Force the emitted .NET assembly to be metadata-only, or real IL. Null leaves the
    /// decision to the csproj property and then the configuration default.</summary>
    public bool? MetadataOnlyAssembly { get; init; }

    /// <summary>
    /// Emit one chunk per strongly-connected component instead of coalescing them into the size band
    /// (<c>--no-chunk-coalescing</c>, or <c>TRANSPOSE_NO_CHUNK_COALESCING=1</c>).
    ///
    /// This is a build for <em>measuring</em>, not for shipping: hundreds of ~2 KB chunks are the
    /// wrong thing to serve, but they are the only way to see what is really fetched together. A
    /// coalesced build has already made the grouping decision, so a capture of it can only confirm
    /// that decision — the fine-grained build is what produces the evidence a <c>tps.chunks.json</c>
    /// oracle is written from. See the <c>chunk-oracle</c> skill.
    /// </summary>
    public bool NoChunkCoalescing { get; init; }
        = Environment.GetEnvironmentVariable("TRANSPOSE_NO_CHUNK_COALESCING") is "1" or "true" or "TRUE";

    /// <summary>Reuse the previous build of this project where its inputs are unchanged.</summary>
    public bool Incremental { get; init; }

    /// <summary>Where the incremental cache lives; null means the project's <c>obj/</c>.</summary>
    public string? CacheDir { get; init; }

    /// <summary>A script inlined into the generated index.html right before <c>&lt;/body&gt;</c>. Watch
    /// mode passes its live-reload script here; every other build passes null, leaving the output
    /// untouched.</summary>
    public string? LiveReloadScript { get; init; }
}

/// <summary>
/// The outcome of one build. <see cref="OutDir"/> is set only for a successful <em>site</em> build —
/// which is what makes it the test for "this build produced something servable" that watch mode needs;
/// a failure, or a package/bundle/runtime build, leaves it null. <see cref="CssResources"/> lists the
/// stylesheets that site assembled from files on disk, so a watcher can rewrite one without recompiling
/// (see <see cref="OutputBuilder.CssResource"/>).
/// </summary>
internal readonly record struct BuildOutcome(
    int ExitCode,
    string? OutDir,
    bool HtmlDisabled,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<OutputBuilder.CssResource> CssResources)
{
    public bool Success => ExitCode == 0;

    /// <summary>An outcome with no servable site: every failure, and every successful build whose shape
    /// is a package DLL, the runtime assembly or a single <c>--out</c> bundle.</summary>
    public static BuildOutcome NoSite(int exitCode, IReadOnlyList<Diagnostic>? diagnostics = null)
        => new(exitCode, null, false, diagnostics ?? Array.Empty<Diagnostic>(), Array.Empty<OutputBuilder.CssResource>());
}

/// <summary>
/// One build of one project, end to end: resolve the csproj, chain its referenced projects, translate,
/// and write the outputs (a site, a package DLL, the runtime assembly, or a single bundle).
///
/// This is the whole of what <c>tps</c> does after parsing its command line, and it lives here rather
/// than in the CLI so <c>Transpose.Compiler.Library</c> can run exactly the same build in-process — a
/// hosting application (the Curiosity CLI's watch-mode dev server, for one) needs a real project build,
/// not just the in-memory source compilation the library started out offering. The CLI keeps argument
/// parsing, the timing report and its watch-mode server; everything else is here.
/// </summary>
internal static class ProjectBuild
{
    /// <summary>
    /// Fills in the output shape the project implies, the way the <c>tps</c> command line always has —
    /// so a caller that only says "build this project" gets the same decision the CLI makes.
    ///
    /// When the Transpose SDK invokes <c>tps --project</c> for a library — no explicit <c>--out</c> and
    /// not the <c>--build-runtime</c> corlib — the build must produce the distributable package
    /// assembly, exactly as <c>--emit-package</c> does: the .NET DLL (with the compiled JS +
    /// Transpose.Resources.json manifest embedded) that <c>dotnet pack</c> wraps into
    /// <c>lib/&lt;tfm&gt;/&lt;Assembly&gt;.dll</c>. Without this the SDK build emits only a stray .js (or a
    /// runnable site folder) and <c>dotnet pack</c> fails with NU5026 (&lt;Assembly&gt;.dll not found).
    ///
    /// A binding library needs this even when it carries a tps.json (which only configures its JS
    /// layout / embedded resources): tps.json presence alone does not make it a site app. Only a
    /// *non-packable* project with a tps.json builds a runnable site; a packable one is a library.
    /// Projects that pass <c>--out</c> (bootstrap, tooling) keep writing a single .js bundle unchanged.
    ///
    /// A site build additionally consumes each referenced project's already-built package DLL —
    /// extracting its compiled JS — instead of recompiling that project's sources into this bundle. A
    /// dependency is therefore compiled once and reused: editing the app re-transpiles only the app's
    /// own files, not the whole referenced library. (With no project references this is equivalent to
    /// the old bundle build.)
    /// </summary>
    public static BuildOptions ResolveOutputMode(BuildOptions options)
    {
        if (options.EmitPackage || options.BuildRuntime || options.OutPath is not null) return options;

        var hasTpsJson = TransposeJson.TryLoad(Path.GetDirectoryName(Path.GetFullPath(options.CsprojPath))!) is not null;
        if (ProjectResolver.IsPackable(options.CsprojPath) || !hasTpsJson)
            return options with { EmitPackage = true, SeparateAssemblies = true };

        return options with { SeparateAssemblies = true };
    }

    /// <summary>Whether these (already <see cref="ResolveOutputMode"/>d) options describe a build that
    /// assembles a runnable site — the only shape watch mode can serve.</summary>
    public static bool IsSiteBuild(BuildOptions options)
        => !options.EmitPackage && !options.BuildRuntime && options.OutPath is null
           && TransposeJson.TryLoad(Path.GetDirectoryName(Path.GetFullPath(options.CsprojPath))!, options.Configuration) is not null;

    /// <summary>
    /// Runs one build of <paramref name="options"/> end to end. Watch mode calls this repeatedly (once
    /// per detected change) with a fresh <see cref="BuildOutcome"/> each time; a normal invocation calls
    /// it once. Never throws for a build failure — a crash in the translator or the write phase is
    /// reported as a diagnostic and returned as a non-zero exit code, so a long-running host (a watch
    /// server) can keep going.
    /// </summary>
    public static BuildOutcome Run(BuildOptions options, BuildLog? log = null)
    {
        log ??= BuildLog.Console;
        var csproj = options.CsprojPath;
        var configuration = options.Configuration;

        log.Info($"tps: compiling {Path.GetFileName(csproj)}");
        // Surface the translator's phase/step progress (binding, scanning, JS emit) so the long
        // silent phases show visible movement. Quiet mode suppresses it. Always assigned (rather than
        // only when not quiet): the sink is process-wide, so a quiet build must clear a previous build's
        // sink rather than keep reporting into it.
        CompileProgress.Sink = options.Quiet ? null : msg => log.Info($"  {msg}");
        var sw = Stopwatch.StartNew();

        ResolvedProject project;
        try
        {
            project = PhaseTimings.Measure("resolve project (csproj + globs + references)",
                () => ProjectResolver.Resolve(csproj, configuration, options.SeparateAssemblies));
        }
        catch (Exception ex)
        {
            MsBuildDiagnostic.WriteError(MsBuildDiagnostic.CodeProjectResolveFailed,
                $"Failed to resolve project '{Path.GetFileName(csproj)}': {ex.Message}");
            return BuildOutcome.NoSite(1);
        }

        // Extra references (--reference) and defines (--define) from the command line. --reference
        // lets a build reference assemblies that are not in the NuGet cache (e.g. locally-built
        // tps.core during bootstrap, or a <Reference HintPath> assembly).
        foreach (var r in options.ExtraReferences)
        {
            var full = Path.GetFullPath(r);
            if (File.Exists(full) && !project.ReferencePaths.Contains(full)) project.ReferencePaths.Add(full);
            else if (!File.Exists(full))
                MsBuildDiagnostic.WriteWarning(MsBuildDiagnostic.CodeReferenceNotFound,
                    $"--reference not found: {full}");
        }
        foreach (var d in options.ExtraDefines)
            if (!project.DefineConstants.Contains(d)) project.DefineConstants.Add(d);

        log.Info($"  sources:    {project.Sources.Count} file(s){(options.SeparateAssemblies ? " (own sources only)" : "")}");
        log.Info($"  references: {project.ReferencePaths.Count} assembly(ies) — {string.Join(", ", project.ReferencePaths.Select(Path.GetFileNameWithoutExtension))}");
        if (project.ReferencedProjectDlls.Count > 0)
            log.Info($"  projects:   {string.Join(", ", project.ReferencedProjectDlls.Select(Path.GetFileName))}");
        log.Info($"  defines:    {string.Join(";", project.DefineConstants)}");
        log.Info($"  config:     {configuration}");
        log.Info($"  lang:       {project.LanguageVersion}");

        // Command line beats the csproj property, which beats the per-configuration default. Printed,
        // because "why is my DLL half the size in Debug?" should be answerable from the build log.
        var metadataOnly = options.MetadataOnlyAssembly
                           ?? project.MetadataOnlyAssembly
                           ?? ResolvedProject.MetadataOnlyAssemblyDefault(configuration);
        log.Info($"  assembly:   {(metadataOnly ? "metadata only (throw-null bodies, not packable)" : "full IL")}");

        // The project's tps.json drives runtime-package detection and reflection settings. A
        // tps.<Configuration>.json overlay (e.g. tps.Release.json) is merged on top when present.
        var tpscfg = TransposeJson.TryLoad(project.ProjectDir, configuration);

        // Base runtime package build: compile Transpose.BCL self-contained, transpile it with
        // outputBy: ClassPath, stitch the per-class files with the hand-written Resources primitives
        // into tps.js per the project's tps.json, and embed tps.js + tps.meta.js into Transpose.dll.
        //
        // This path is ONLY for the base library, which *defines* the BCL. A binding library
        // (Transpose.Newtonsoft.Json, Transpose.WebGL2, …) may also declare outputBy: ClassPath for its
        // JS layout, but it BINDS against the BCL — compiling it self-contained leaves System.Object
        // and every other predefined type undefined (CS0518 ×N). Such libraries fall through to the
        // package path.
        //
        // The base library is recognised by its assembly name, which is the one identity that always
        // holds. Testing for a *resolved* reference to Transpose.dll instead did not: the translator
        // injects the BCL rather than taking it from ReferencePaths, so a project whose
        // `<PackageReference Include="Transpose.BCL" />` is absent from the NuGet cache — every fresh
        // dev tree, which is exactly what bootstrap.sh builds — looked like it referenced nothing and
        // took the runtime path. That is the CS0518 bootstrap.sh reported on Transpose.Newtonsoft.Json.
        var isBaseLibrary = string.Equals(project.AssemblyName, "Transpose", StringComparison.OrdinalIgnoreCase);
        if (options.BuildRuntime
            || (string.Equals(tpscfg?.OutputBy, "ClassPath", StringComparison.OrdinalIgnoreCase) && isBaseLibrary))
            return BuildOutcome.NoSite(BuildRuntimePackage(project, configuration, sw, options, log));

        // Refuse to compile against assemblies built by a newer Transpose than this one (see
        // BuildStamp). Checked before anything is compiled — and before the incremental cache can
        // declare the project up to date — so the answer never depends on how much of a previous
        // build survives. The base library is checked too: it is injected by the translator rather
        // than listed as a project reference, and it is the assembly most likely to be ahead of the
        // compiler in a NuGet cache.
        if (!CompilerIsNewEnough(project.ReferencePaths, log)) return BuildOutcome.NoSite(1);

        // Reflection settings come from the project's tps.json (target inline vs a .meta.js file).
        var (reflectionEnabled, metadataTarget) = ReflectionSettings(tpscfg);

        // Chain the referenced projects first (like the MSBuild-driven compiler): in
        // separate-assembly / package mode this project binds against its dependencies' built DLLs
        // and extracts their embedded JS, so each must be compiled — in dependency order — before
        // this one. Up-to-date packages are skipped.
        if (options.SeparateAssemblies && !EnsureReferencedProjectsBuilt(csproj, options, log))
        {
            log.Error("\nFAILED building referenced projects.");
            return BuildOutcome.NoSite(1);
        }

        // A site build still emits the .NET assembly: the Transpose.Build.Target SDK declares the
        // project's <Assembly>.dll as its build output, and MSBuild copies it for any project that
        // references this one. Emitting the DLL (with the compiled JS embedded, like a package) means
        // every project — app or library — produces the DLL the SDK/consumers expect.
        var isSiteBuild = !options.EmitPackage && !options.BuildRuntime && options.OutPath is null && tpscfg is not null;

        // Incremental build (--incremental): consult the cache written by the previous build of this
        // project. This happens *after* the referenced projects were rebuilt above, so a dependency's
        // freshly written DLL is part of what gets fingerprinted.
        BuildCache? cache = null;
        var verdict = BuildCache.Verdict.FullBuild;
        var changedSources = new List<string>();
        if (options.Incremental)
        {
            cache = BuildCache.Open(project, configuration,
                OutputMode(options.EmitPackage, isSiteBuild),
                // "watch={..}" so switching --watch on/off across builds sharing a cache dir invalidates
                // it — the injected live-reload script changes the written index.html even when nothing
                // else about the build did, and an "up to date" verdict skips writing it altogether.
                BuildSettingsFingerprint(project, configuration, tpscfg, reflectionEnabled, metadataTarget,
                    metadataOnly, options.EmitPackage, isSiteBuild, options.SeparateAssemblies, options.WithRuntime,
                    options.OutPath, options.SiteDir, options.AssemblyVersion, options.NoChunkCoalescing)
                    .Append($"watch={options.LiveReloadScript is not null}"),
                BuildContentFingerprint(project),
                options.CacheDir);
            (verdict, changedSources, var reason) = cache.Decide();

            if (verdict == BuildCache.Verdict.UpToDate)
            {
                log.Info($"\nOK — everything up to date, nothing to compile ({sw.ElapsedMilliseconds} ms).");
                foreach (var output in cache.PreviousOutputs.Take(4)) log.Info($"  output:   {output}");
                if (cache.PreviousOutputs.Count > 4)
                    log.Info($"            … and {cache.PreviousOutputs.Count - 4} more");
                // An up-to-date site is still a servable site: report where it is (and which
                // stylesheets it assembled) so a watch host does not mistake "nothing to do" for
                // "nothing to serve".
                return isSiteBuild
                    ? new BuildOutcome(0, SiteDirectory(options, tpscfg!, project), tpscfg!.HtmlDisabled,
                        Array.Empty<Diagnostic>(), OutputBuilder.CssResources(project, tpscfg, configuration))
                    : BuildOutcome.NoSite(0);
            }
            log.Info(verdict == BuildCache.Verdict.BodyOnlyChange
                ? $"  cache:      {changedSources.Count} file(s) changed, method bodies only — reusing cached declarations"
                : $"  cache:      full build ({reason})");
        }
        // Nothing in this project changed — only a referenced package's content did (its declarations
        // are identical). The compilation would reproduce the cached bundle byte for byte, so skip it
        // and go straight to writing the outputs, which do have to be rewritten.
        var replayed = options.Incremental && verdict == BuildCache.Verdict.BodyOnlyChange && changedSources.Count == 0
            ? cache!.TryReplayCompilation(canReuseAssembly: metadataOnly)
            : null;
        if (replayed is not null)
            log.Info("  cache:      reusing the previous compilation in full — writing outputs only");

        var plan = replayed is not null ? null : cache?.CreatePlan(verdict, changedSources, canReuseAssembly: metadataOnly);

        // What shape of JavaScript this build produces — derived from the build itself, never from
        // tps.json (see JsOutputProfile). A package ships every variant; a Release site ships one; a
        // Debug site ships one readable bundle and no chunks at all.
        var profile = JsOutputProfiles.For(options.EmitPackage, configuration);

        // Module mode applies to a site build AND to a package (--emit-package): a library emits its
        // chunks and publishes the map, and its consumer imports the chunks behind the types it uses.
        // A Debug site build opts out however tps.json is written: stepping through one bundle is the
        // point of a Debug build, and a stack trace spread over sixty on-demand chunks is not.
        var wantsModules = tpscfg is { OutputByModule: true } && (isSiteBuild || options.EmitPackage)
                           && profile.WantsModules(true);
        // A package emits the single bundle alongside its chunks, because it cannot know whether the
        // application that references it will be built Debug (one bundle) or Release (chunks).
        var alsoEmitBundle = wantsModules && options.EmitPackage;
        // Measured co-load groups from a capture of the running application, if the project keeps one.
        // Never fails a build: an unreadable or stale file simply contributes nothing (ChunkOracle).
        var chunkOracle = wantsModules ? ChunkOracle.TryLoad(project.ProjectDir) : null;
        if (chunkOracle is { IsEmpty: false })
            log.Info($"  chunks:     {ChunkOracle.FileName} — {chunkOracle.Groups.Count} measured group(s)");
        // A [SkipTypeClustering] facade in a referenced assembly publishes per-member dependency sets,
        // which a call site here has to turn into imports (the facade's own chunk carries none of them).
        //
        // Its chunk FILE names are deliberately not read: a cross-assembly reference is emitted as a
        // type placeholder and resolved when the site is assembled, so what this compilation emits does
        // not depend on which build of a library happens to be installed. See ModuleLinker.
        var externalSkipDeps = wantsModules ? ModuleMap.ReadSkipClusterDeps(project.ReferencePaths) : null;

        var translator = new RoslynTranslator();
        AssemblyBuildResult result;
        try
        {
            result = replayed ?? translator.BuildAssembly(
                project.Sources,
                project.AssemblyName,
                project.ReferencePaths,
                project.DefineConstants,
                project.LanguageVersion,
                reflectionEnabled,
                metadataTarget,
                emitAssembly: options.EmitPackage || isSiteBuild,
                assemblyVersion: options.AssemblyVersion,
                emitDebugInformation: project.EmitDebugInformation,
                metadataOnlyAssembly: metadataOnly,
                incremental: plan,
                emitModules: wantsModules,
                // Per assembly, so two module-mode assemblies in one site cannot collide.
                chunkDirectory: "chunks/" + project.AssemblyName,
                externalSkipClusterDeps: externalSkipDeps,
                // A library has no entry point to be lazy relative to: nothing is eager beyond its
                // [Ready] handlers, and its consumers pull in what they actually use.
                packageModules: options.EmitPackage,
                minChunkBytes: MinChunkBytes(options, tpscfg),
                maxChunkBytes: tpscfg?.ModuleMaxChunkBytes ?? Emitter.DefaultMaxChunkBytes,
                alsoEmitBundle: alsoEmitBundle,
                chunkOracle: chunkOracle);
        }
        catch (Exception ex)
        {
            ReportCrash("Translator", ex, log);
            return BuildOutcome.NoSite(2);
        }

        // The stopwatch keeps running: writing the package (minifying the embedded JS, the Cecil
        // resource embed) and assembling the site are a real part of a build's cost, and the
        // reported total should include them rather than stopping at the translator's last phase.
        if (!result.Success)
        {
            ReportDiagnostics(result.Diagnostics, options.MaxErrors, log);
            log.Error($"\nFAILED in {sw.ElapsedMilliseconds} ms.");
            return BuildOutcome.NoSite(1, result.Diagnostics);
        }

        var js = result.Javascript!;
        if (!options.Quiet) ReportDiagnostics(result.Diagnostics, options.MaxErrors, log); // surface warnings
        var config = tpscfg;

        // Package mode: compile this project as a distributable assembly — emit the .NET DLL and
        // embed its JS (+ tps.json resources) so another project can reference it and extract them.
        if (options.EmitPackage)
        {
            var written = TryWritePackage(project, config, configuration, result, log);
            if (written is null) return BuildOutcome.NoSite(2, result.Diagnostics);

            var (dllPath, items, packageBytes) = written.Value;
            SaveCache(cache, plan, result, new[] { dllPath });
            var resourceBytes = items.Sum(i => (long)i.Content.Length);
            log.Info($"\nOK — built package {project.AssemblyName}.dll ({packageBytes:N0} bytes, "
                   + $"{resourceBytes:N0} of it in {items.Count} embedded resource(s)) in {sw.ElapsedMilliseconds} ms.");
            log.Info($"  dll:      {dllPath}");
            if (result.Modules is { } pkgMods)
                log.Info($"  modules:    {pkgMods.Chunks.Count} chunk(s) — {pkgMods.EagerChunkCount} loaded up front, " +
                         $"{pkgMods.LazyChunkCount} on demand ({pkgMods.LazyTypeCount} type(s) deferred){ChunkSizes(pkgMods)}");
            log.Info($"  embedded: {string.Join(", ", items.Take(6).Select(i => i.Name))}{(items.Count > 6 ? ", …" : "")}");
            return BuildOutcome.NoSite(0, result.Diagnostics);
        }

        // Site build: when the project has an tps.json and no single-file --out was requested,
        // assemble a runnable output folder (runtime JS + bundle + resources + index.html),
        // exactly like the existing tps compiler.
        if (config is not null && options.OutPath is null)
        {
            // Also emit the package DLL (with JS embedded), so referencing projects can consume this
            // one and the SDK finds the <Assembly>.dll it declares as the build output.
            string? dllPath = null;
            if (result.AssemblyBytes is not null)
            {
                var package = TryWritePackage(project, config, configuration, result, log);
                if (package is null) return BuildOutcome.NoSite(2, result.Diagnostics);
                dllPath = package.Value.dllPath;
            }

            var outDir = SiteDirectory(options, config, project);
            var siteResult = PhaseTimings.Measure("write site (minify + resources + html)", () =>
                OutputBuilder.Build(project, config, js, outDir, configuration, result.MetadataJavascript, options.LiveReloadScript, result.Modules, result.SharedWorkerEntries));
            // Every file in the site counts as an output: a rebuild must notice if any of them was
            // deleted or edited, otherwise "up to date" would leave a broken site in place.
            SaveCache(cache, plan, result, SiteOutputs(outDir, dllPath));
            log.Info($"\nOK — built site in {outDir} ({js.Length:N0} bytes of {config.FileName}) in {sw.ElapsedMilliseconds} ms.");
            if (result.Modules is { } mods)
                log.Info($"  modules:    {mods.Chunks.Count} chunk(s) — {mods.EagerChunkCount} loaded up front, " +
                         $"{mods.LazyChunkCount} on demand ({mods.LazyTypeCount} type(s) deferred){ChunkSizes(mods)}");
            log.Info($"  index.html: {(config.HtmlDisabled ? "disabled" : "generated")}");
            if (result.SharedWorkerEntries.Count > 0)
                log.Info($"  workers:    {string.Join(", ", result.SharedWorkerEntries.Select(w => w.Name + ".worker.js"))}");
            if (dllPath is not null) log.Info($"  dll:        {dllPath}");
            if (siteResult.UnscriptedReferences.Count > 0)
                log.Info($"  not loaded: {string.Join(", ", siteResult.UnscriptedReferences)} " +
                         "(dontLoadReferences — extracted, but not referenced from index.html)");
            // An entry that matches nothing fails silently — the library goes on loading as if the
            // setting were not there — so say so. A warning rather than an error: a dropped dependency
            // must not break the build of a project that still lists it here.
            foreach (var pattern in siteResult.UnmatchedDontLoadReferences)
                MsBuildDiagnostic.WriteWarning(MsBuildDiagnostic.CodeDontLoadReferenceUnmatched,
                    $"tps.json 'dontLoadReferences' entry '{pattern}' matched no referenced assembly.");
            // An import naming a file the site does not have is a 404 on whichever screen first needs
            // that chunk, and nothing says so at build time. The usual cause is a package built by a
            // compiler that wrote its dependency's chunk file names instead of type placeholders, and
            // whose dependency has since been updated — rebuilding that package fixes it.
            foreach (var dangling in siteResult.DanglingModuleImports)
                MsBuildDiagnostic.WriteWarning(MsBuildDiagnostic.CodeDanglingModuleImport, dangling + ".");
            if (siteResult.RemovedStaleFiles.Count > 0)
            {
                var stale = siteResult.RemovedStaleFiles;
                log.Info($"  cleaned:    {stale.Count} stale file(s) from a previous build");
                foreach (var f in stale.Take(10)) log.Info($"                - {Path.GetRelativePath(outDir, f)}");
                if (stale.Count > 10) log.Info($"                … and {stale.Count - 10} more");
            }
            return new BuildOutcome(0, outDir, config.HtmlDisabled, result.Diagnostics,
                OutputBuilder.CssResources(project, config, configuration));
        }

        if (options.WithRuntime) js = RoslynTranslator.LoadRuntime() + "\n" + js;
        var outPath = options.OutPath ?? Path.Combine(project.ProjectDir, "bin", project.AssemblyName + ".js");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, js);
        SaveCache(cache, plan, result, new[] { Path.GetFullPath(outPath) });
        log.Info($"\nOK — wrote {js.Length:N0} bytes to {outPath} in {sw.ElapsedMilliseconds} ms.");
        return BuildOutcome.NoSite(0, result.Diagnostics);
    }

    /// <summary>The coalescing floor for this build: the project's <c>modules.minChunkSize</c>, or 0
    /// when the build asked for one chunk per component (see <see cref="BuildOptions.NoChunkCoalescing"/>).
    /// The switch is applied to the whole project closure, so a capture sees every assembly at the
    /// same granularity.</summary>
    private static int MinChunkBytes(BuildOptions options, TransposeJson? config)
        => options.NoChunkCoalescing ? 0 : config?.ModuleMinChunkBytes ?? Emitter.DefaultMinChunkBytes;

    /// <summary>The directory a site build writes to: <c>--site-dir</c> when given, otherwise the path
    /// tps.json's <c>output</c> resolves to.</summary>
    private static string SiteDirectory(BuildOptions options, TransposeJson config, ResolvedProject project)
        => options.SiteDir ?? ResolveOutputDir(config, project.ProjectDir, options.Configuration);

    /// <summary>
    /// Whether this compiler is new enough for everything the project binds against — every referenced
    /// assembly plus the injected base library (<c>Transpose.dll</c>). Reports the diagnostic and
    /// returns false when it is not; see <see cref="BuildStamp"/> for what is being compared and
    /// <see cref="CompilerVersion.EnforceMinimum"/> for when the check applies at all (never in a dev
    /// build of the compiler, which carries no version).
    /// </summary>
    private static bool CompilerIsNewEnough(IEnumerable<string> referencePaths, BuildLog log)
    {
        if (!CompilerVersion.EnforceMinimum) return true;

        var toCheck = new List<string>(referencePaths);
        // Discovering the base library can itself fail (no NuGet cache, no TRANSPOSE_DLL_PATH); that is
        // the translator's error to report, not this check's.
        try { toCheck.Add(TransposeAssemblies.TransposeDllPath); } catch { }

        var outdated = BuildStamp.CheckReferences(toCheck);
        if (outdated is null) return true;

        log.Error("");
        MsBuildDiagnostic.Write(MsBuildDiagnostic.Format(outdated), isError: true);
        return false;
    }

    /// <summary>
    /// Persists the cache after a successful build. A build that reused the previous compilation whole
    /// (no plan) has nothing new to record but its new output files; any other build writes the lot.
    /// </summary>
    private static void SaveCache(BuildCache? cache, IncrementalPlan? plan, AssemblyBuildResult result,
        IEnumerable<string> outputs)
    {
        if (cache is null) return;
        if (plan is null) cache.SaveOutputsOnly(outputs);
        else cache.Save(plan, result, outputs);
    }

    /// <summary>Every file under the assembled site, plus the emitted DLL — the outputs an incremental
    /// build has to find unchanged before it may declare the project up to date.</summary>
    private static IEnumerable<string> SiteOutputs(string outDir, string? dllPath)
    {
        if (dllPath is not null) yield return dllPath;
        if (!Directory.Exists(outDir)) yield break;
        foreach (var file in Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories))
            yield return file;
    }

    /// <summary>
    /// Everything about a build other than its source files: the settings, the output shape, and a
    /// fingerprint of every referenced assembly and config file. A change to any of these can change
    /// every byte of the output, so it invalidates the whole cache rather than any part of it.
    ///
    /// Anything the compiler starts to read in future has to be added here. That is the one
    /// maintenance obligation the cache imposes: an input nobody fingerprints is an input whose change
    /// goes unnoticed.
    /// </summary>
    private static IEnumerable<string> BuildSettingsFingerprint(
        ResolvedProject project, string configuration, TransposeJson? tpscfg, bool reflectionEnabled,
        MetadataTarget metadataTarget, bool metadataOnly, bool emitPackage, bool isSiteBuild,
        bool separateAssemblies, bool withRuntime, string? outPath, string? siteDir, string? assemblyVersion,
        bool noChunkCoalescing)
    {
        yield return $"configuration={configuration}";
        yield return $"assembly={project.AssemblyName}";
        yield return $"tfm={project.TargetFramework}";
        yield return $"lang={project.LanguageVersion}";
        yield return $"defines={string.Join(";", project.DefineConstants)}";
        yield return $"reflection={reflectionEnabled}/{metadataTarget}";
        yield return $"metadataOnly={metadataOnly}";
        yield return $"debugInfo={project.EmitDebugInformation}";
        yield return $"minifyLocals={project.MinifyLocalVariables}";
        yield return $"assemblyVersion={assemblyVersion}";
        yield return $"mode={OutputMode(emitPackage, isSiteBuild)}";
        yield return $"chunkCoalescing={!noChunkCoalescing}";
        yield return $"separate={separateAssemblies};runtime={withRuntime}";
        yield return $"out={outPath};siteDir={siteDir}";
        yield return $"tpsjson={tpscfg is null}";
        yield return "tps.chunks.json=" + BuildCache.FileContentFingerprint(Path.Combine(project.ProjectDir, ChunkOracle.FileName));

        // The project file and its tps.json (plus the per-configuration overlay) by content: they
        // decide which files compile, what gets embedded and how the site is laid out.
        yield return "csproj=" + BuildCache.FileContentFingerprint(project.CsprojPath);
        yield return "tps.json=" + BuildCache.FileContentFingerprint(Path.Combine(project.ProjectDir, "tps.json"));
        yield return $"tps.{configuration}.json="
            + BuildCache.FileContentFingerprint(Path.Combine(project.ProjectDir, $"tps.{configuration}.json"));

        // Every referenced assembly, by the part of it this project's output depends on: its metadata.
        // A package upgrade or a dependency whose declarations moved changes emitted names (overload
        // numbering counts the reference's full member set), so it is a full-rebuild input; a dependency
        // rebuilt from a body-only edit of its own is not (see ReferenceMetadataFingerprint).
        foreach (var reference in project.ReferencePaths.OrderBy(p => p, StringComparer.Ordinal))
            yield return "ref=" + BuildCache.ReferenceMetadataFingerprint(reference);

        // The Transpose base assembly and the JS runtime it carries are injected by the translator, not
        // by the project, so they have to be fingerprinted explicitly.
        yield return "bcl=" + BuildCache.ReferenceFingerprint(TransposeAssemblies.TransposeDllPath);

        // Resources the site/package build reads straight from disk (tps.json `resources`): tps.json's
        // own hash covers *which* files are listed, but not what is in them.
        if (tpscfg is not null)
            foreach (var file in tpscfg.Resources.SelectMany(g => g.Files).OrderBy(n => n, StringComparer.Ordinal))
                yield return "res=" + file + "=" + BuildCache.FileContentFingerprint(Path.Combine(project.ProjectDir, file));
    }

    /// <summary>The chunk size distribution, so a `modules.minChunkSize` setting can be judged from
    /// the build's own output rather than by measuring the site afterwards.</summary>
    private static string ChunkSizes(Translator.Emitter.ModuleOutput modules)
    {
        if (modules.Chunks.Count == 0) return "";
        var sizes = modules.Chunks.Select(c => c.js.Length).OrderBy(n => n).ToList();
        return $", median {sizes[sizes.Count / 2] / 1024.0:N1} KB, largest {sizes[^1] / 1024.0:N1} KB";
    }

    /// <summary>What shape of output this build produces — the distributable package DLL, a runnable
    /// site, or a single .js bundle. Each gets its own cache.</summary>
    private static string OutputMode(bool emitPackage, bool isSiteBuild)
        => emitPackage ? "package" : isSiteBuild ? "site" : "bundle";

    /// <summary>
    /// The inputs that cannot change a byte this project *emits*, but do change what it has to *write*:
    /// the full content of every referenced assembly, whose embedded JavaScript and resources are copied
    /// into the site and re-embedded into this project's own DLL. A change here means "rebuild the
    /// outputs, keep the compilation" — see <see cref="BuildCache.Open"/>.
    /// </summary>
    private static IEnumerable<string> BuildContentFingerprint(ResolvedProject project)
    {
        foreach (var reference in project.ReferencePaths.OrderBy(p => p, StringComparer.Ordinal))
            yield return "refbytes=" + BuildCache.ReferenceFingerprint(reference);
    }

    /// <summary>
    /// Builds the base runtime package (Transpose.BCL → the `tps` NuGet package): compiles the BCL
    /// self-contained, transpiles it with outputBy: ClassPath into Resources/.generated/, stitches
    /// those with the hand-written Resources/*.js primitives into tps.js (and the reflection block
    /// into tps.meta.js) per the project's tps.json, and embeds both into Transpose.dll.
    /// </summary>
    private static int BuildRuntimePackage(ResolvedProject project, string configuration, Stopwatch sw,
        BuildOptions options, BuildLog log)
    {
        var cfg = TransposeJson.TryLoad(project.ProjectDir, configuration);
        var reflectionEnabled = !(cfg?.ReflectionDisabled ?? false);

        RuntimePackageResult result;
        try
        {
            result = new RoslynTranslator().BuildRuntimePackage(
                project.Sources, project.AssemblyName, project.DefineConstants,
                project.LanguageVersion, reflectionEnabled);
        }
        catch (Exception ex) { ReportCrash("Runtime build", ex, log); return 2; }

        if (!result.Success)
        {
            ReportDiagnostics(result.Diagnostics, options.MaxErrors, log);
            log.Error($"\nFAILED building runtime in {sw.ElapsedMilliseconds} ms.");
            return 1;
        }

        // Write the ClassPath per-class files + reflection metadata under Resources/.generated/.
        var genRoot = Path.Combine(project.ProjectDir, "Resources", ".generated");
        PhaseTimings.Measure("write ClassPath files", () =>
        {
        if (Directory.Exists(genRoot)) Directory.Delete(genRoot, recursive: true);
        Directory.CreateDirectory(genRoot);
        // Group types that share a ClassPath file (same simple name across generic arities, e.g.
        // ValueTuple + ValueTuple$1..$8, or all the nested Enumerator types) so they are concatenated
        // into one file rather than overwriting each other.
        foreach (var grp in result.ClassPath!.Files.GroupBy(f => f.relPath))
        {
            var dest = Path.Combine(genRoot, grp.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest, string.Join("\n", grp.Select(g => g.js)));
        }
        if (result.ClassPath.MetaBlock is not null)
            File.WriteAllText(Path.Combine(genRoot, project.AssemblyName + ".meta.js"), result.ClassPath.MetaBlock);
        });
        log.Info($"  emitted:    {result.ClassPath!.Files.Count} ClassPath file(s) into Resources/.generated");
        if (result.ClassPath.Skipped.Count > 0)
        {
            log.Info($"  skipped:    {result.ClassPath.Skipped.Count} type(s) the emitter could not translate:");
            foreach (var (t, why) in result.ClassPath.Skipped.Take(20)) log.Info($"                - {t}: {why}");
        }

        // Assemble the resource bundles (tps.js, tps.meta.js, …) declared in tps.json, then add a
        // pre-minified variant of each next to it. Embedding the .min.js in the runtime package
        // means a referencing build never re-minifies the (large) runtime — it just picks the
        // variant its configuration wants, exactly as it does for the formatted/minified pair.
        var bundles = PhaseTimings.Measure("assemble runtime bundles (tps.js)",
            () => RuntimeAssembler.Assemble(project.ProjectDir));
        var minBundles = PhaseTimings.Measure("minify runtime bundles", () =>
        {
        var mins = new List<(string name, byte[] bytes)>();
        foreach (var (name, bytes) in bundles)
        {
            var minName = name.EndsWith(".js", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - ".js".Length) + ".min.js"
                : null;
            if (minName is null) continue;
            var minText = JsMinifier.Minify(System.Text.Encoding.UTF8.GetString(bytes), name, project.MinifyLocalVariables);
            mins.Add((minName, System.Text.Encoding.UTF8.GetBytes(minText)));
        }
        return mins;
        });
        bundles = bundles.Concat(minBundles).ToList();
        // Write the assembly to bin/<config>/<tfm>/, matching the SDK's output path so `dotnet pack`
        // finds it (the Transpose.Build.Target SDK forces netstandard2.0, so that is the effective tfm).
        var dllPath = options.OutPath ?? Path.Combine(project.ProjectDir, "bin", configuration, project.TargetFramework, project.AssemblyName + ".dll");
        var outDir = Path.GetDirectoryName(Path.GetFullPath(dllPath))!;
        Directory.CreateDirectory(outDir);
        foreach (var (name, bytes) in bundles)
        {
            File.WriteAllBytes(Path.Combine(outDir, name), bytes); // write bundles next to the DLL for reuse
            log.Info($"  assembled:  {name} ({bytes.Length:N0} bytes)");
        }

        // Emit the reference assembly with the JS bundles embedded as manifest resources, via
        // Roslyn (not a Mono.Cecil post-process) so the DLL stays a clean core library — Cecil's
        // writer injects an mscorlib reference, which stops Roslyn from treating the runtime as the
        // corlib when compiling user code against it (every type would fail with CS0518).
        //
        // The base library gets the same minimum-compiler-version stamp as every other assembly tps
        // builds — it is the reference every Transpose project binds against, so it is the one whose
        // "you need a newer tps" is most worth saying. Appended here rather than to `bundles`: the
        // bundles are also written to disk next to the DLL for reuse, and this belongs only inside it.
        var embedded = bundles.Append((BuildStamp.ResourceName, BuildStamp.ForCurrentCompiler().ToJsonBytes())).ToList();

        byte[] assemblyBytes;
        try { assemblyBytes = result.EmitAssembly!(embedded); }
        catch (Exception ex)
        {
            MsBuildDiagnostic.WriteError(MsBuildDiagnostic.CodeAssemblyEmitFailed,
                $"Runtime assembly emit failed: {ex.Message}");
            return 2;
        }
        File.WriteAllBytes(dllPath, assemblyBytes);

        log.Info($"\nOK — built runtime {Path.GetFileName(dllPath)} with {bundles.Count} embedded bundle(s) in {sw.ElapsedMilliseconds} ms.");
        log.Info($"  dll:      {dllPath}");
        log.Info($"  bundles:  written to {outDir}");
        return 0;
    }

    private static (bool reflectionEnabled, MetadataTarget target) ReflectionSettings(TransposeJson? tpscfg)
    {
        var reflectionEnabled = !(tpscfg?.ReflectionDisabled ?? false);
        var metadataTarget = (tpscfg?.ReflectionTarget ?? "file").ToLowerInvariant() switch
        {
            "inline" => MetadataTarget.Inline,
            "type" => MetadataTarget.Type,
            "assembly" => MetadataTarget.Assembly,
            _ => MetadataTarget.File,
        };
        return (reflectionEnabled, metadataTarget);
    }

    /// <summary>
    /// Builds every referenced project of <paramref name="rootCsproj"/> in dependency order,
    /// skipping any whose package DLL is already up-to-date. Mirrors the MSBuild-driven compiler,
    /// which builds project references (each producing a DLL with its JS embedded) before the
    /// project that consumes them.
    /// </summary>
    private static bool EnsureReferencedProjectsBuilt(string rootCsproj, BuildOptions options, BuildLog log)
    {
        foreach (var dep in ProjectResolver.ReferencedProjectsInBuildOrder(rootCsproj))
        {
            var name = Path.GetFileNameWithoutExtension(dep);
            // Without the cache, a dependency's freshness is judged by timestamps. With it, that
            // question is answered better inside BuildPackage — by hashing the content — so the
            // timestamp screen is skipped rather than layered on top: it can only disagree by calling a
            // project dirty when it is not (a touched file, a checkout that moved an mtime backwards),
            // and the cache then resolves that to "nothing to do" for the price of hashing the sources.
            // Two mechanisms answering the same question, weaker one first, is how a build ends up
            // rebuilding for reasons nobody can explain.
            if (!options.Incremental && ProjectResolver.IsPackageUpToDate(dep, options.Configuration))
            {
                log.Info($"  dependency up-to-date: {name}");
                continue;
            }
            log.Info($"  building dependency: {name}");
            if (!BuildPackage(dep, options, log))
            {
                MsBuildDiagnostic.WriteError(MsBuildDiagnostic.CodeDependencyBuildFailed,
                    $"Failed to build referenced project '{name}'.");
                return false;
            }
        }
        return true;
    }

    /// <summary>Compiles one project into its Transpose package DLL (the .NET assembly with the compiled JS
    /// and tps.json resources embedded). Its own project references are consumed as their built DLLs,
    /// so they must already have been built (this is called in dependency order).</summary>
    private static bool BuildPackage(string csproj, BuildOptions options, BuildLog log)
    {
        var configuration = options.Configuration;

        ResolvedProject project;
        try { project = ProjectResolver.Resolve(csproj, configuration, separateAssemblies: true); }
        catch (Exception ex)
        {
            MsBuildDiagnostic.WriteError(MsBuildDiagnostic.CodeProjectResolveFailed,
                $"Failed to resolve project '{Path.GetFileName(csproj)}': {ex.Message}");
            return false;
        }

        // A dependency can reference a package the root project does not, so it gets its own check.
        if (!CompilerIsNewEnough(project.ReferencePaths, log)) return false;

        var tpscfg = TransposeJson.TryLoad(project.ProjectDir, configuration);
        var (reflectionEnabled, metadataTarget) = ReflectionSettings(tpscfg);
        var assemblyVersion = ProjectResolver.ReadAssemblyVersion(csproj);
        // Each dependency reads its own csproj property, but a command-line override applies to the
        // whole invocation.
        var metadataOnly = options.MetadataOnlyAssembly
                           ?? project.MetadataOnlyAssembly
                           ?? ResolvedProject.MetadataOnlyAssemblyDefault(configuration);

        // A dependency gets the same treatment as the root project: its own cache, in its own obj/.
        // Without this, editing a method body in a referenced library — the common case in a
        // solution — would still cost that library a full rebuild.
        BuildCache? cache = null;
        var verdict = BuildCache.Verdict.FullBuild;
        var changedSources = new List<string>();
        if (options.Incremental)
        {
            cache = BuildCache.Open(project, configuration,
                OutputMode(emitPackage: true, isSiteBuild: false),
                BuildSettingsFingerprint(project, configuration, tpscfg, reflectionEnabled, metadataTarget,
                    metadataOnly, emitPackage: true, isSiteBuild: false, separateAssemblies: true,
                    withRuntime: false, outPath: null, siteDir: null, assemblyVersion: assemblyVersion,
                    noChunkCoalescing: options.NoChunkCoalescing),
                BuildContentFingerprint(project),
                options.CacheDir);
            (verdict, changedSources, var reason) = cache.Decide();
            if (verdict == BuildCache.Verdict.UpToDate)
            {
                // Every input and output of the dependency is unchanged, so its package DLL is already
                // the one this build wants. This is the incremental replacement for the timestamp screen
                // in EnsureReferencedProjectsBuilt, and it is cheaper than the replay path below
                // because it does not even rewrite the DLL.
                log.Info("    cache: up to date, nothing to do");
                return true;
            }
            log.Info(verdict == BuildCache.Verdict.FullBuild
                ? $"    cache: full build ({reason})"
                : $"    cache: {changedSources.Count} file(s) changed, method bodies only");
        }
        var replayed = options.Incremental && verdict == BuildCache.Verdict.BodyOnlyChange && changedSources.Count == 0
            ? cache!.TryReplayCompilation(canReuseAssembly: metadataOnly)
            : null;
        var plan = replayed is not null ? null : cache?.CreatePlan(verdict, changedSources, canReuseAssembly: metadataOnly);

        AssemblyBuildResult result;
        try
        {
            result = replayed ?? new RoslynTranslator().BuildAssembly(
                project.Sources, project.AssemblyName, project.ReferencePaths,
                project.DefineConstants, project.LanguageVersion,
                reflectionEnabled, metadataTarget, emitAssembly: true,
                assemblyVersion: assemblyVersion,
                emitDebugInformation: project.EmitDebugInformation,
                metadataOnlyAssembly: metadataOnly,
                incremental: plan,
                // A referenced project is always built as a package, and it declares module output
                // the same way a root project does. Without this the dependency would be rebuilt as
                // a single bundle here, silently replacing the chunked DLL its own build produced.
                emitModules: tpscfg is { OutputByModule: true },
                chunkDirectory: "chunks/" + project.AssemblyName,
                externalSkipClusterDeps: tpscfg is { OutputByModule: true } ? ModuleMap.ReadSkipClusterDeps(project.ReferencePaths) : null,
                packageModules: true,
                minChunkBytes: MinChunkBytes(options, tpscfg),
                maxChunkBytes: tpscfg?.ModuleMaxChunkBytes ?? Emitter.DefaultMaxChunkBytes,
                chunkOracle: ChunkOracle.TryLoad(project.ProjectDir),
                // …and the single bundle alongside them, for the same reason: a package ships every
                // variant, because which one is wanted is the consuming build's decision.
                alsoEmitBundle: tpscfg is { OutputByModule: true });
        }
        catch (Exception ex) { ReportCrash($"Translator on '{Path.GetFileName(csproj)}'", ex, log); return false; }

        if (!result.Success)
        {
            ReportDiagnostics(result.Diagnostics, options.MaxErrors, log);
            return false;
        }

        var written = TryWritePackage(project, tpscfg, configuration, result, log);
        if (written is null) return false;
        SaveCache(cache, plan, result, new[] { written.Value.dllPath });
        return true;
    }

    /// <summary>Writes a project's package DLL, turning a failure into a reported diagnostic (rather
    /// than an unhandled exception) and a null result. Embedding the resources re-serializes the
    /// assembly's metadata through Mono.Cecil, which resolves referenced assemblies as it goes — a
    /// step that can fail on its own, after a clean compile.</summary>
    private static (string dllPath, List<EmbeddedItem> items, long fileBytes)? TryWritePackage(
        ResolvedProject project, TransposeJson? config, string configuration, AssemblyBuildResult result, BuildLog log)
    {
        try { return WritePackage(project, config, configuration, result); }
        catch (Exception ex)
        {
            ReportCrash($"Writing the package for '{Path.GetFileName(project.CsprojPath)}'", ex, log);
            return null;
        }
    }

    /// <summary>Writes a project's emitted assembly and embeds its JS + resources, returning the DLL
    /// path, the embedded items and the size of the file written. The DLL path is the one the
    /// resolver references for this project, so a consumer finds it.
    /// <para>
    /// The size is measured here rather than by the caller because this is the only place that knows
    /// the file is complete. <c>result.AssemblyBytes</c> is what Roslyn emitted, and the resources
    /// are injected <em>after</em> it — for a library shipping its stylesheets, fonts and JavaScript
    /// they are most of what the package weighs, so reporting the emitted length understated
    /// Tesserae's package as 1.8 MB when the file is 17.1 MB.
    /// </para></summary>
    private static (string dllPath, List<EmbeddedItem> items, long fileBytes) WritePackage(
        ResolvedProject project, TransposeJson? config, string configuration, AssemblyBuildResult result)
    {
        var mainJsName = config?.ExplicitFileName ?? project.AssemblyName + ".js";
        var dllPath = ProjectResolver.OutputDll(project.CsprojPath, configuration)!;
        Directory.CreateDirectory(Path.GetDirectoryName(dllPath)!);

        var items = PhaseTimings.Measure("collect package resources (minify + read files)", () => config is not null
            ? OutputBuilder.CollectEmbeddableItems(project.ProjectDir, config, mainJsName, result.Javascript!, result.MetadataJavascript, project.MinifyLocalVariables, result.Modules)
            : new List<EmbeddedItem> { new(mainJsName, System.Text.Encoding.UTF8.GetBytes(result.Javascript!), null) });

        // Cecil re-serializes the assembly's metadata when embedding the resources; encoding a
        // parameter's default value whose type lives in a referenced assembly (e.g. a Tesserae enum)
        // makes it resolve that assembly. Seed the resolver with the reference directories so those
        // types are found (the referenced DLLs live in the NuGet cache / sibling bin folders, not
        // next to this DLL).
        // Writes the DLL — the emitted assembly plus the embedded resources — in one pass.
        PhaseTimings.Measure("embed resources into DLL (Cecil)",
            () => ResourceEmbedder.Embed(dllPath, result.AssemblyBytes!, items, project.ReferencePaths, result.Modules?.TypeToChunk, result.Modules?.SkipClusterDeps));
        // Record what a consumer's compilation actually depends on — this assembly's metadata — so a
        // rebuild of this project does not force a rebuild of everything referencing it when only its
        // method bodies moved. Cecil restamps the DLL's MVID on every embed, so its bytes cannot serve.
        BuildCache.WriteMetadataSidecar(dllPath, result.AssemblyBytes);
        return (dllPath, items, new FileInfo(dllPath).Length);
    }

    /// <summary>Resolves tps.json's output path, expanding the $(OutDir) MSBuild token.</summary>
    internal static string ResolveOutputDir(TransposeJson config, string projectDir, string configuration)
    {
        var raw = (config.Output ?? "$(OutDir)/tps/").Replace("$(OutDir)", ResolveBinDir(projectDir, configuration)).Replace('\\', '/');
        return Path.GetFullPath(raw);
    }

    /// <summary>The project's build output directory (bin/&lt;config&gt;/netstandard2.0), where the
    /// emitted assembly and tps output land — matching the Transpose SDK's default output path.</summary>
    private static string ResolveBinDir(string projectDir, string configuration)
        => Path.Combine(projectDir, "bin", configuration, "netstandard2.0");

    /// <summary>
    /// The errors to report, in the order to report them: by file, then line, then column — the order
    /// you would fix them in — rather than by the phase that produced them (the unsupported-feature
    /// scan runs before Roslyn's diagnostics, and its parallel per-file walk has no inherent order).
    /// Nothing is filtered out here; truncation, if any, is the caller's decision.
    /// </summary>
    internal static List<Diagnostic> OrderErrorsForReport(IReadOnlyList<Diagnostic> diagnostics)
        => diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .OrderBy(d => d.Location.SourceTree?.FilePath ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Location.GetLineSpan().StartLinePosition.Line)
            .ThenBy(d => d.Location.GetLineSpan().StartLinePosition.Character)
            .ThenBy(d => d.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Prints the build's diagnostics. **Every** error is printed by default: a truncated list makes a
    /// broken build take several compile cycles to fix, and the caller has no way to know whether the
    /// errors it cannot see are the same problem or a different one. <paramref name="maxErrors"/> caps
    /// the list only when a caller explicitly asked for a cap (<c>--max-errors</c>).
    ///
    /// Ordering comes from <see cref="OrderErrorsForReport"/>.
    /// </summary>
    private static void ReportDiagnostics(IReadOnlyList<Diagnostic> diagnostics, int maxErrors, BuildLog log)
    {
        var errors = OrderErrorsForReport(diagnostics);
        var warnings = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();

        if (errors.Count > 0)
        {
            // The summary lines are deliberately not in diagnostic form, and must stay that way: any
            // line MSBuild can parse becomes a build error, so a summary could otherwise conjure an
            // error nobody wrote. See MsBuildDiagnostic for the shape to avoid — writing the count as
            // "N error(s), by id: …" rather than "N error(s):" keeps a colon from ever landing where
            // the parser expects one.
            var byId = errors.GroupBy(d => d.Id).OrderByDescending(g => g.Count());
            log.Error("");
            log.Error($"{errors.Count} error(s), by id: "
                + string.Join(", ", byId.Select(g => $"{g.Key}×{g.Count()}")));
            log.Error("");
            var shown = maxErrors > 0 ? errors.Take(maxErrors) : errors;
            foreach (var d in shown)
                MsBuildDiagnostic.Write(MsBuildDiagnostic.Format(d), isError: true);
            if (maxErrors > 0 && errors.Count > maxErrors)
                log.Error($"… and {errors.Count - maxErrors} more (raise or drop --max-errors to see them)");
        }

        // Warnings are printed in full too, not just counted: in canonical form they reach the IDE's
        // task list, where a count on the console never would.
        foreach (var d in warnings)
            MsBuildDiagnostic.Write(MsBuildDiagnostic.Format(d), isError: false);
        if (warnings.Count > 0)
            log.Info($"{warnings.Count} warning(s) total");
    }

    /// <summary>
    /// Reports an unhandled exception from the translator as a single diagnostic — a crash is a build
    /// failure the caller must see, and MSBuild only sees a line it can parse. The messages of the
    /// whole exception chain go into that one line; the stack frames follow as plain text, since they
    /// are what makes a crash actionable but cannot fit on one line.
    ///
    /// The frames are printed on their own rather than via <c>ex.ToString()</c> deliberately: an
    /// exception message that happens to read like "... error: ..." would be parsed as a *second*
    /// diagnostic, and frames alone can never match.
    /// </summary>
    internal static void ReportCrash(string what, Exception ex, BuildLog log)
    {
        var chain = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
            chain.Add($"{e.GetType().Name}: {e.Message}");
        MsBuildDiagnostic.WriteError(MsBuildDiagnostic.CodeInternalError,
            $"{what} threw {string.Join(" ---> ", chain)}");

        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (!ReferenceEquals(e, ex)) log.Error($"--- inner {e.GetType().FullName}");
            log.Error(e.StackTrace ?? "");
        }
    }
}
