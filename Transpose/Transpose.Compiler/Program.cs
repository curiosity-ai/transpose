using System.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Transpose.Compiler;

/// <summary>
/// A small command-line front-end for the Roslyn-only C# → JavaScript translator, in the
/// spirit of the existing <c>tps</c> compiler: point it at a project and it resolves the
/// sources and package references, runs the translator, and writes the JavaScript bundle.
///
///   tps &lt;project.csproj|dir&gt; [--out &lt;file.js&gt;] [--with-runtime] [--quiet]
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            ShowHelp();
            return args.Length == 0 ? 1 : 0;
        }

        string? projectArg = null;
        string? outPath = null;
        string? siteDir = null;
        var configuration = "Debug";
        var withRuntime = false;
        var quiet = false;
        var maxErrors = 40;
        var emitPackage = false;
        var separateAssemblies = false;
        var buildRuntime = false;
        var extraReferences = new List<string>();
        var extraDefines = new List<string>();
        string? assemblyVersion = null;
        // Overrides the project's <TransposeMetadataOnlyAssembly>, which in turn overrides the
        // per-configuration default. Null = nothing on the command line expressed a preference.
        bool? metadataOnlyAssembly = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" or "-o": outPath = args[++i]; break;
                case "--site-dir": siteDir = args[++i]; break;
                case "--configuration" or "-c": configuration = args[++i]; break;
                case "--emit-package": emitPackage = true; separateAssemblies = true; break;
                case "--build-runtime": buildRuntime = true; break;
                case "--separate-assemblies": separateAssemblies = true; break;
                case "--with-runtime": withRuntime = true; break;
                case "--quiet" or "-q": quiet = true; break;
                case "--max-errors": maxErrors = int.Parse(args[++i]); break;
                case "--reference" or "-r": extraReferences.Add(args[++i]); break;
                case "--define" or "-D": extraDefines.Add(args[++i]); break;
                case "--timing": PhaseTimings.Enabled = true; break;
                case "--timing-json": _timingJsonPath = args[++i]; PhaseTimings.Enabled = true; break;
                case "--metadata-only-assembly": metadataOnlyAssembly = true; break;
                case "--no-metadata-only-assembly": metadataOnlyAssembly = false; break;
                case "--assembly-version": assemblyVersion = args[++i]; break;
                case "--project" or "-p": projectArg = args[++i]; break;
                default:
                    if (projectArg is null) projectArg = args[i];
                    else { Console.Error.WriteLine($"Unexpected argument: {args[i]}"); return 1; }
                    break;
            }
        }
        // assemblyVersion (from --assembly-version) is emitted into the bundle via Transpose.assemblyVersion(...).

        var csproj = LocateProject(projectArg);
        if (csproj is null)
        {
            Console.Error.WriteLine($"No .csproj found at '{projectArg}'.");
            return 1;
        }

        // When the Transpose SDK invokes `tps --project` for a library — no explicit `--out` and not
        // the `--build-runtime` corlib — produce the distributable package assembly, exactly as
        // `--emit-package` does: the .NET DLL (with the compiled JS + Transpose.Resources.json manifest
        // embedded) that `dotnet pack` wraps into `lib/<tfm>/<Assembly>.dll`. Without this the SDK
        // build emits only a stray .js (or a runnable site folder) and `dotnet pack` fails with
        // NU5026 (<Assembly>.dll not found).
        //
        // A binding library needs this even when it carries a tps.json (which only configures its JS
        // layout / embedded resources): tps.json presence alone does not make it a site app. Only a
        // *non-packable* project with a tps.json builds a runnable site; a packable one is a library.
        // Projects that pass `--out` (bootstrap, tooling) keep writing a single .js bundle unchanged.
        var hasTpsJson = TransposeJson.TryLoad(Path.GetDirectoryName(csproj)!) is not null;
        if (!emitPackage && !buildRuntime && outPath is null
            && (ProjectResolver.IsPackable(csproj) || !hasTpsJson))
        {
            emitPackage = true;
            separateAssemblies = true;
        }
        // A site build (a non-packable project with a tps.json) consumes each referenced project's
        // already-built package DLL — extracting its compiled JS — instead of recompiling that
        // project's sources into this bundle. A dependency is therefore compiled once and reused:
        // editing the app re-transpiles only the app's own files, not the whole referenced library.
        // (With no project references this is equivalent to the old bundle build.)
        else if (!emitPackage && !buildRuntime && outPath is null && hasTpsJson)
        {
            separateAssemblies = true;
        }

        Console.WriteLine($"tps: compiling {Path.GetFileName(csproj)}");
        // Surface the translator's phase/step progress (binding, scanning, JS emit) so the long
        // silent phases show visible movement. Quiet mode suppresses it.
        if (!quiet) CompileProgress.Sink = msg => Console.WriteLine($"  {msg}");
        var sw = Stopwatch.StartNew();

        ResolvedProject project;
        try
        {
            project = PhaseTimings.Measure("resolve project (csproj + globs + references)",
                () => ProjectResolver.Resolve(csproj, configuration, separateAssemblies));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to resolve project: {ex.Message}");
            return 1;
        }

        // Extra references (--reference) and defines (--define) from the command line. --reference
        // lets a build reference assemblies that are not in the NuGet cache (e.g. locally-built
        // tps.core during bootstrap, or a <Reference HintPath> assembly).
        foreach (var r in extraReferences)
        {
            var full = Path.GetFullPath(r);
            if (File.Exists(full) && !project.ReferencePaths.Contains(full)) project.ReferencePaths.Add(full);
            else if (!File.Exists(full)) Console.Error.WriteLine($"  warning: --reference not found: {full}");
        }
        foreach (var d in extraDefines)
            if (!project.DefineConstants.Contains(d)) project.DefineConstants.Add(d);

        Console.WriteLine($"  sources:    {project.Sources.Count} file(s){(separateAssemblies ? " (own sources only)" : "")}");
        Console.WriteLine($"  references: {project.ReferencePaths.Count} assembly(ies) — {string.Join(", ", project.ReferencePaths.Select(Path.GetFileNameWithoutExtension))}");
        if (project.ReferencedProjectDlls.Count > 0)
            Console.WriteLine($"  projects:   {string.Join(", ", project.ReferencedProjectDlls.Select(Path.GetFileName))}");
        Console.WriteLine($"  defines:    {string.Join(";", project.DefineConstants)}");
        Console.WriteLine($"  config:     {configuration}");
        Console.WriteLine($"  lang:       {project.LanguageVersion}");

        // Command line beats the csproj property, which beats the per-configuration default. Printed,
        // because "why is my DLL half the size in Debug?" should be answerable from the build log.
        var metadataOnly = metadataOnlyAssembly
                           ?? project.MetadataOnlyAssembly
                           ?? ResolvedProject.MetadataOnlyAssemblyDefault(configuration);
        Console.WriteLine($"  assembly:   {(metadataOnly ? "metadata only (throw-null bodies, not packable)" : "full IL")}");

        // The project's tps.json drives runtime-package detection and reflection settings. A
        // tps.<Configuration>.json overlay (e.g. tps.Release.json) is merged on top when present.
        var tpscfg = TransposeJson.TryLoad(project.ProjectDir, configuration);

        // Base runtime package build: compile Transpose.BCL self-contained, transpile it with
        // outputBy: ClassPath, stitch the per-class files with the hand-written Resources primitives
        // into tps.js per the project's tps.json, and embed tps.js + tps.meta.js into Transpose.dll.
        //
        // This path is ONLY for the base library, which *defines* the BCL and therefore references no
        // Transpose.dll of its own. A binding library (Transpose.Newtonsoft.Json, Transpose.WebGL2, …)
        // may also declare outputBy: ClassPath for its JS layout, but it references the Transpose BCL
        // and must bind against it — compiling it self-contained would leave System.Object and every
        // other predefined type undefined (CS0518 ×N). Such libraries fall through to the package path.
        var referencesTransposeBcl = project.ReferencePaths.Any(p =>
            string.Equals(Path.GetFileNameWithoutExtension(p), "Transpose", StringComparison.OrdinalIgnoreCase));
        if (buildRuntime
            || (string.Equals(tpscfg?.OutputBy, "ClassPath", StringComparison.OrdinalIgnoreCase) && !referencesTransposeBcl))
            return BuildRuntime(project, configuration, sw, outPath);

        // Reflection settings come from the project's tps.json (target inline vs a .meta.js file).
        var (reflectionEnabled, metadataTarget) = ReflectionSettings(tpscfg);

        // Chain the referenced projects first (like the MSBuild-driven compiler): in
        // separate-assembly / package mode this project binds against its dependencies' built DLLs
        // and extracts their embedded JS, so each must be compiled — in dependency order — before
        // this one. Up-to-date packages are skipped.
        if (separateAssemblies && !EnsureReferencedProjectsBuilt(csproj, configuration, maxErrors, metadataOnlyAssembly))
        {
            Console.Error.WriteLine("\nFAILED building referenced projects.");
            return 1;
        }

        // A site build still emits the .NET assembly: the Transpose.Build.Target SDK declares the
        // project's <Assembly>.dll as its build output, and MSBuild copies it for any project that
        // references this one. Emitting the DLL (with the compiled JS embedded, like a package) means
        // every project — app or library — produces the DLL the SDK/consumers expect.
        var isSiteBuild = !emitPackage && !buildRuntime && outPath is null && tpscfg is not null;
        var translator = new RoslynTranslator();
        AssemblyBuildResult result;
        try
        {
            result = translator.BuildAssembly(
                project.Sources,
                project.AssemblyName,
                project.ReferencePaths,
                project.DefineConstants,
                project.LanguageVersion,
                reflectionEnabled,
                metadataTarget,
                emitAssembly: emitPackage || isSiteBuild,
                assemblyVersion: assemblyVersion,
                emitDebugInformation: project.EmitDebugInformation,
                metadataOnlyAssembly: metadataOnly);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Translator threw: {ex}");
            return 2;
        }

        // The stopwatch keeps running: writing the package (minifying the embedded JS, the Cecil
        // resource embed) and assembling the site are a real part of a build's cost, and the
        // reported total should include them rather than stopping at the translator's last phase.
        if (!result.Success)
        {
            ReportDiagnostics(result.Diagnostics, maxErrors);
            Console.Error.WriteLine($"\nFAILED in {sw.ElapsedMilliseconds} ms.");
            return 1;
        }

        var js = result.Javascript!;
        if (!quiet) ReportDiagnostics(result.Diagnostics, maxErrors); // surface warnings
        var config = tpscfg;

        // Package mode: compile this project as a distributable assembly — emit the .NET DLL and
        // embed its JS (+ tps.json resources) so another project can reference it and extract them.
        if (emitPackage)
        {
            var (dllPath, items) = WritePackage(project, config, configuration, result);
            Console.WriteLine($"\nOK — built package {project.AssemblyName}.dll ({result.AssemblyBytes!.Length:N0} bytes) with {items.Count} embedded resource(s) in {sw.ElapsedMilliseconds} ms.");
            Console.WriteLine($"  dll:      {dllPath}");
            Console.WriteLine($"  embedded: {string.Join(", ", items.Take(6).Select(i => i.Name))}{(items.Count > 6 ? ", …" : "")}");
            PrintTimings(sw.ElapsedMilliseconds);
            return 0;
        }

        // Site build: when the project has an tps.json and no single-file --out was requested,
        // assemble a runnable output folder (runtime JS + bundle + resources + index.html),
        // exactly like the existing tps compiler.
        if (config is not null && outPath is null)
        {
            // Also emit the package DLL (with JS embedded), so referencing projects can consume this
            // one and the SDK finds the <Assembly>.dll it declares as the build output.
            string? dllPath = null;
            if (result.AssemblyBytes is not null)
                (dllPath, _) = WritePackage(project, config, configuration, result);

            var outDir = siteDir ?? ResolveOutputDir(config, project.ProjectDir, configuration);
            var siteResult = PhaseTimings.Measure("write site (minify + resources + html)", () =>
                OutputBuilder.Build(project, config, js, outDir, configuration, result.MetadataJavascript));
            Console.WriteLine($"\nOK — built site in {outDir} ({js.Length:N0} bytes of {config.FileName}) in {sw.ElapsedMilliseconds} ms.");
            Console.WriteLine($"  index.html: {(config.HtmlDisabled ? "disabled" : "generated")}");
            if (dllPath is not null) Console.WriteLine($"  dll:        {dllPath}");
            if (siteResult.RemovedStaleFiles.Count > 0)
            {
                var stale = siteResult.RemovedStaleFiles;
                Console.WriteLine($"  cleaned:    {stale.Count} stale file(s) from a previous build");
                foreach (var f in stale.Take(10)) Console.WriteLine($"                - {Path.GetRelativePath(outDir, f)}");
                if (stale.Count > 10) Console.WriteLine($"                … and {stale.Count - 10} more");
            }
            PrintTimings(sw.ElapsedMilliseconds);
            return 0;
        }

        if (withRuntime) js = RoslynTranslator.LoadRuntime() + "\n" + js;
        outPath ??= Path.Combine(project.ProjectDir, "bin", project.AssemblyName + ".js");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, js);
        Console.WriteLine($"\nOK — wrote {js.Length:N0} bytes to {outPath} in {sw.ElapsedMilliseconds} ms.");
        PrintTimings(sw.ElapsedMilliseconds);
        return 0;
    }

    /// <summary>
    /// Builds the base runtime package (Transpose.BCL → the `tps` NuGet package): compiles the BCL
    /// self-contained, transpiles it with outputBy: ClassPath into Resources/.generated/, stitches
    /// those with the hand-written Resources/*.js primitives into tps.js (and the reflection block
    /// into tps.meta.js) per the project's tps.json, and embeds both into Transpose.dll.
    /// </summary>
    private static int BuildRuntime(ResolvedProject project, string configuration, Stopwatch sw, string? outPath)
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
        catch (Exception ex) { Console.Error.WriteLine($"Runtime build threw: {ex}"); return 2; }

        if (!result.Success)
        {
            ReportDiagnostics(result.Diagnostics, 40);
            Console.Error.WriteLine($"\nFAILED building runtime in {sw.ElapsedMilliseconds} ms.");
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
        Console.WriteLine($"  emitted:    {result.ClassPath.Files.Count} ClassPath file(s) into Resources/.generated");
        if (result.ClassPath.Skipped.Count > 0)
        {
            Console.WriteLine($"  skipped:    {result.ClassPath.Skipped.Count} type(s) the emitter could not translate:");
            foreach (var (t, why) in result.ClassPath.Skipped.Take(20)) Console.WriteLine($"                - {t}: {why}");
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
        var dllPath = outPath ?? Path.Combine(project.ProjectDir, "bin", configuration, project.TargetFramework, project.AssemblyName + ".dll");
        var outDir = Path.GetDirectoryName(Path.GetFullPath(dllPath))!;
        Directory.CreateDirectory(outDir);
        foreach (var (name, bytes) in bundles)
        {
            File.WriteAllBytes(Path.Combine(outDir, name), bytes); // write bundles next to the DLL for reuse
            Console.WriteLine($"  assembled:  {name} ({bytes.Length:N0} bytes)");
        }

        // Emit the reference assembly with the JS bundles embedded as manifest resources, via
        // Roslyn (not a Mono.Cecil post-process) so the DLL stays a clean core library — Cecil's
        // writer injects an mscorlib reference, which stops Roslyn from treating the runtime as the
        // corlib when compiling user code against it (every type would fail with CS0518).
        byte[] assemblyBytes;
        try { assemblyBytes = result.EmitAssembly!(bundles); }
        catch (Exception ex) { Console.Error.WriteLine($"Runtime assembly emit failed: {ex.Message}"); return 2; }
        File.WriteAllBytes(dllPath, assemblyBytes);

        Console.WriteLine($"\nOK — built runtime {Path.GetFileName(dllPath)} with {bundles.Count} embedded bundle(s) in {sw.ElapsedMilliseconds} ms.");
        Console.WriteLine($"  dll:      {dllPath}");
        Console.WriteLine($"  bundles:  written to {outDir}");
        PrintTimings(sw.ElapsedMilliseconds);
        return 0;
    }

    /// <summary>Prints the per-phase timing breakdown gathered when <c>--timing</c> (or
    /// <c>TRANSPOSE_TIMING=1</c>) is set. Shows each phase's total time, its share of the sum, and
    /// the bytes allocated while it ran, so a build's hotspots (binding, JS emit, minification) and
    /// its garbage producers are visible at a glance. With <c>--timing-json</c> the same numbers —
    /// plus process-wide GC/memory totals — are written as JSON for a benchmark harness to consume.</summary>
    private static void PrintTimings(long wallClockMs)
    {
        if (!PhaseTimings.Enabled) return;
        var phases = PhaseTimings.Snapshot();
        if (phases.Count == 0) return;
        // Sub-phases are named with a leading indent ("  ├ …") and are already counted inside their
        // parent, so only top-level phases contribute to the total.
        long sum = 0, sumBytes = 0;
        foreach (var p in phases)
        {
            if (p.phase.StartsWith(" ", StringComparison.Ordinal)) continue;
            sum += p.ms; sumBytes += p.bytes;
        }
        var denom = sum == 0 ? 1 : sum;
        Console.WriteLine("\n  timing breakdown:");
        foreach (var (phase, ms, bytes, count) in phases)
        {
            var share = ms * 100.0 / denom;
            var times = count > 1 ? $" ×{count}" : "";
            Console.WriteLine($"    {ms,7:N0} ms  {share,5:F1}%  {Mb(bytes),9} alloc  {phase}{times}");
        }
        Console.WriteLine($"    {sum,7:N0} ms          {Mb(sumBytes),9} alloc  measured phases (wall clock {wallClockMs:N0} ms)");
        Console.WriteLine($"    total allocated {Mb(GC.GetTotalAllocatedBytes(precise: true))}, "
            + $"peak working set {Mb(Process.GetCurrentProcess().PeakWorkingSet64)}, "
            + $"GC {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)} (gen0/1/2)");

        if (_timingJsonPath is not null) WriteTimingJson(_timingJsonPath, wallClockMs, phases);
    }

    private static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):N1} MB";

    /// <summary>Path passed to <c>--timing-json</c>: where the machine-readable build-stats dump goes.
    /// The benchmark harness (<c>tps-bench</c>) reads this instead of scraping console output.</summary>
    private static string? _timingJsonPath;

    private static void WriteTimingJson(string path, long wallClockMs,
        IReadOnlyList<(string phase, long ms, long bytes, int count)> phases)
    {
        var proc = Process.GetCurrentProcess();
        var sb = new System.Text.StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"wallClockMs\": {wallClockMs},\n");
        sb.Append($"  \"totalAllocatedBytes\": {GC.GetTotalAllocatedBytes(precise: true)},\n");
        sb.Append($"  \"peakWorkingSetBytes\": {proc.PeakWorkingSet64},\n");
        sb.Append($"  \"gen0\": {GC.CollectionCount(0)}, \"gen1\": {GC.CollectionCount(1)}, \"gen2\": {GC.CollectionCount(2)},\n");
        sb.Append($"  \"processorTimeMs\": {(long)proc.TotalProcessorTime.TotalMilliseconds},\n");
        sb.Append("  \"phases\": [\n");
        for (var i = 0; i < phases.Count; i++)
        {
            var (phase, ms, bytes, count) = phases[i];
            sb.Append($"    {{ \"name\": {JsonString(phase)}, \"ms\": {ms}, \"bytes\": {bytes}, \"count\": {count} }}");
            sb.Append(i == phases.Count - 1 ? "\n" : ",\n");
        }
        sb.Append("  ]\n}\n");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, sb.ToString());
        }
        catch (Exception ex) { Console.Error.WriteLine($"  warning: could not write --timing-json: {ex.Message}"); }
    }

    private static string JsonString(string s)
    {
        var sb = new System.Text.StringBuilder("\"");
        foreach (var c in s)
            sb.Append(c switch { '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", _ => c.ToString() });
        return sb.Append('"').ToString();
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
    private static bool EnsureReferencedProjectsBuilt(string rootCsproj, string configuration, int maxErrors, bool? metadataOnlyAssembly)
    {
        foreach (var dep in ProjectResolver.ReferencedProjectsInBuildOrder(rootCsproj))
        {
            var name = Path.GetFileNameWithoutExtension(dep);
            if (ProjectResolver.IsPackageUpToDate(dep, configuration))
            {
                Console.WriteLine($"  dependency up-to-date: {name}");
                continue;
            }
            Console.WriteLine($"  building dependency: {name}");
            if (!BuildPackage(dep, configuration, maxErrors, metadataOnlyAssembly))
            {
                Console.Error.WriteLine($"  dependency build FAILED: {name}");
                return false;
            }
        }
        return true;
    }

    /// <summary>Compiles one project into its Transpose package DLL (the .NET assembly with the compiled JS
    /// and tps.json resources embedded). Its own project references are consumed as their built DLLs,
    /// so they must already have been built (this is called in dependency order).</summary>
    private static bool BuildPackage(string csproj, string configuration, int maxErrors, bool? metadataOnlyAssembly)
    {
        ResolvedProject project;
        try { project = ProjectResolver.Resolve(csproj, configuration, separateAssemblies: true); }
        catch (Exception ex) { Console.Error.WriteLine($"    resolve failed: {ex.Message}"); return false; }

        var tpscfg = TransposeJson.TryLoad(project.ProjectDir, configuration);
        var (reflectionEnabled, metadataTarget) = ReflectionSettings(tpscfg);

        AssemblyBuildResult result;
        try
        {
            result = new RoslynTranslator().BuildAssembly(
                project.Sources, project.AssemblyName, project.ReferencePaths,
                project.DefineConstants, project.LanguageVersion,
                reflectionEnabled, metadataTarget, emitAssembly: true,
                assemblyVersion: ProjectResolver.ReadAssemblyVersion(csproj),
                emitDebugInformation: project.EmitDebugInformation,
                // Each dependency reads its own csproj property, but a command-line override applies
                // to the whole invocation.
                metadataOnlyAssembly: metadataOnlyAssembly
                                      ?? project.MetadataOnlyAssembly
                                      ?? ResolvedProject.MetadataOnlyAssemblyDefault(configuration));
        }
        catch (Exception ex) { Console.Error.WriteLine($"    translator threw: {ex.Message}"); return false; }

        if (!result.Success)
        {
            ReportDiagnostics(result.Diagnostics, maxErrors);
            return false;
        }

        WritePackage(project, tpscfg, configuration, result);
        return true;
    }

    /// <summary>Writes a project's emitted assembly and embeds its JS + resources, returning the DLL
    /// path and the embedded items. The DLL path is the one the resolver references for this
    /// project, so a consumer finds it.</summary>
    private static (string dllPath, List<EmbeddedItem> items) WritePackage(
        ResolvedProject project, TransposeJson? config, string configuration, AssemblyBuildResult result)
    {
        var mainJsName = config?.ExplicitFileName ?? project.AssemblyName + ".js";
        var dllPath = ProjectResolver.OutputDll(project.CsprojPath, configuration)!;
        Directory.CreateDirectory(Path.GetDirectoryName(dllPath)!);

        var items = PhaseTimings.Measure("collect package resources (minify + read files)", () => config is not null
            ? OutputBuilder.CollectEmbeddableItems(project.ProjectDir, config, mainJsName, result.Javascript!, result.MetadataJavascript, project.MinifyLocalVariables)
            : new List<EmbeddedItem> { new(mainJsName, System.Text.Encoding.UTF8.GetBytes(result.Javascript!), null) });
        // Cecil re-serializes the assembly's metadata when embedding the resources; encoding a
        // parameter's default value whose type lives in a referenced assembly (e.g. a Tesserae enum)
        // makes it resolve that assembly. Seed the resolver with the reference directories so those
        // types are found (the referenced DLLs live in the NuGet cache / sibling bin folders, not
        // next to this DLL).
        // Writes the DLL — the emitted assembly plus the embedded resources — in one pass.
        PhaseTimings.Measure("embed resources into DLL (Cecil)",
            () => ResourceEmbedder.Embed(dllPath, result.AssemblyBytes!, items, project.ReferencePaths));
        return (dllPath, items);
    }

    /// <summary>Resolves tps.json's output path, expanding the $(OutDir) MSBuild token.</summary>
    private static string ResolveOutputDir(TransposeJson config, string projectDir, string configuration)
    {
        var raw = (config.Output ?? "$(OutDir)/tps/").Replace("$(OutDir)", ResolveBinDir(projectDir, configuration)).Replace('\\', '/');
        return Path.GetFullPath(raw);
    }

    /// <summary>The project's build output directory (bin/&lt;config&gt;/netstandard2.0), where the
    /// emitted assembly and tps output land — matching the Transpose SDK's default output path.</summary>
    private static string ResolveBinDir(string projectDir, string configuration)
        => Path.Combine(projectDir, "bin", configuration, "netstandard2.0");

    private static string? LocateProject(string? arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) arg = Directory.GetCurrentDirectory();
        if (File.Exists(arg) && arg.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return Path.GetFullPath(arg);
        if (Directory.Exists(arg))
        {
            var found = Directory.GetFiles(arg, "*.csproj", SearchOption.TopDirectoryOnly);
            if (found.Length == 1) return Path.GetFullPath(found[0]);
            if (found.Length > 1)
            {
                Console.Error.WriteLine($"Multiple .csproj files in '{arg}'; pass one explicitly.");
                return null;
            }
        }
        return null;
    }

    private static void ReportDiagnostics(IReadOnlyList<Diagnostic> diagnostics, int maxErrors)
    {
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var warnings = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();

        if (errors.Count > 0)
        {
            Console.Error.WriteLine($"\n{errors.Count} error(s):");
            var byId = errors.GroupBy(d => d.Id).OrderByDescending(g => g.Count());
            Console.Error.WriteLine("  by id: " + string.Join(", ", byId.Select(g => $"{g.Key}×{g.Count()}")));
            Console.Error.WriteLine();
            foreach (var d in errors.Take(maxErrors))
                Console.Error.WriteLine("  " + Format(d));
            if (errors.Count > maxErrors)
                Console.Error.WriteLine($"  … and {errors.Count - maxErrors} more.");
        }

        if (warnings.Count > 0)
            Console.WriteLine($"\n{warnings.Count} warning(s).");
    }

    private static string Format(Diagnostic d)
    {
        var loc = d.Location.GetLineSpan();
        var file = string.IsNullOrEmpty(loc.Path) ? "" : $"{Path.GetFileName(loc.Path)}({loc.StartLinePosition.Line + 1},{loc.StartLinePosition.Character + 1}): ";
        return $"{file}{d.Id}: {d.GetMessage()}";
    }

    private static void ShowHelp()
    {
        Console.WriteLine("""
            tps — Roslyn-only C# → JavaScript translator (experimental)

            Usage:
              tps <project.csproj | directory> [options]

            Options:
              -o, --out <file.js>   Output path (default: <project>/bin/<assembly>.js)
              -c, --configuration <name>
                                    Build configuration (Debug/Release; default Debug). Release
                                    selects the .min.js resource variants where both exist.
              --emit-package        Compile this project as a distributable assembly: emit its
                                    .NET DLL with the compiled JS + tps.json resources embedded
                                    (Transpose.Resources.json manifest), for referencing by other projects.
              --separate-assemblies Consume referenced projects as their built DLLs (extract their
                                    embedded JS) instead of recompiling their source into the bundle.
              --site-dir <dir>      Output directory for the assembled site
              --with-runtime        Prepend the tps.js runtime + shim to the output
              --max-errors <n>      Max individual errors to print (default 40)
              --metadata-only-assembly, --no-metadata-only-assembly
                                    Force the .NET assembly to be metadata only (full metadata
                                    including private members, `throw null` bodies) or real IL. A
                                    Transpose assembly is only ever bound against — it cannot execute,
                                    since it binds to the stand-in BCL — so metadata-only skips a
                                    second full bind of every method body (~18% off a large build).
                                    Default: metadata only for Debug, real IL for Release. A project
                                    can pin it with <TransposeMetadataOnlyAssembly>. The Transpose SDK
                                    refuses to pack a Debug build, so a metadata-only assembly cannot
                                    reach a NuGet feed.
              --timing              Print a per-phase timing/allocation breakdown of the build
              --timing-json <file>  Also write that breakdown (plus GC/memory totals) as JSON
              -q, --quiet           Suppress warning output
              -h, --help            Show this help
            """);
    }
}
