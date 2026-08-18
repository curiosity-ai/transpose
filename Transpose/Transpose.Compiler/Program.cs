using System.Diagnostics;

namespace Transpose.Compiler;

/// <summary>
/// A small command-line front-end for the Roslyn-only C# → JavaScript translator, in the
/// spirit of the existing <c>tps</c> compiler: point it at a project and it resolves the
/// sources and package references, runs the translator, and writes the JavaScript bundle.
///
///   tps &lt;project.csproj|dir&gt; [--out &lt;file.js&gt;] [--with-runtime] [--quiet]
///
/// The build itself lives in <see cref="ProjectBuild"/> (Transpose.Compiler.Core), shared with the
/// <c>Transpose.Compiler.Library</c> package; this file is argument parsing, the timing report and
/// watch mode's dev server.
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
        // 0 = print every error. A cap loses information the user needs, so it is opt-in.
        var maxErrors = 0;
        var emitPackage = false;
        var separateAssemblies = false;
        var buildRuntime = false;
        var extraReferences = new List<string>();
        var extraDefines = new List<string>();
        string? assemblyVersion = null;
        // Overrides the project's <TransposeMetadataOnlyAssembly>, which in turn overrides the
        // per-configuration default. Null = nothing on the command line expressed a preference.
        bool? metadataOnlyAssembly = null;
        // Incremental builds are opt-in: reusing a previous build's output is only correct while the
        // rules in BuildCache/IncrementalPlan hold, and a stale cache is a silently wrong build rather
        // than a failed one. TRANSPOSE_INCREMENTAL=1 turns it on for a whole session.
        var incremental = Environment.GetEnvironmentVariable("TRANSPOSE_INCREMENTAL") is "1" or "true";
        var noChunkCoalescing = Environment.GetEnvironmentVariable("TRANSPOSE_NO_CHUNK_COALESCING") is "1" or "true" or "TRUE";
        string? cacheDir = null;
        var watch = false;
        var watchPort = 4300;

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
                case "--max-errors": maxErrors = Math.Max(0, int.Parse(args[++i])); break;
                case "--reference" or "-r": extraReferences.Add(args[++i]); break;
                case "--define" or "-D": extraDefines.Add(args[++i]); break;
                case "--timing": PhaseTimings.Enabled = true; break;
                case "--timing-json": _timingJsonPath = args[++i]; PhaseTimings.Enabled = true; break;
                case "--no-chunk-coalescing": noChunkCoalescing = true; break;
                case "--chunk-coalescing": noChunkCoalescing = false; break;
                case "--incremental": incremental = true; break;
                case "--no-incremental": incremental = false; break;
                case "--cache-dir": cacheDir = args[++i]; incremental = true; break;
                case "--watch": watch = true; break;
                case "--watch-port": watchPort = int.Parse(args[++i]); break;
                case "--metadata-only-assembly": metadataOnlyAssembly = true; break;
                case "--no-metadata-only-assembly": metadataOnlyAssembly = false; break;
                case "--assembly-version": assemblyVersion = args[++i]; break;
                case "--project" or "-p": projectArg = args[++i]; break;
                default:
                    if (projectArg is null) projectArg = args[i];
                    else
                    {
                        MsBuildDiagnostic.WriteError(MsBuildDiagnostic.CodeInvalidCommandLine,
                            $"Unexpected argument '{args[i]}'.");
                        return 1;
                    }
                    break;
            }
        }
        // assemblyVersion (from --assembly-version) is emitted into the bundle via Transpose.assemblyVersion(...).

        var csproj = LocateProject(projectArg);
        if (csproj is null)
        {
            MsBuildDiagnostic.WriteError(MsBuildDiagnostic.CodeProjectNotFound,
                $"No .csproj found at '{projectArg}'.");
            return 1;
        }

        var options = ProjectBuild.ResolveOutputMode(new BuildOptions
        {
            CsprojPath = csproj,
            OutPath = outPath,
            SiteDir = siteDir,
            Configuration = configuration,
            WithRuntime = withRuntime,
            Quiet = quiet,
            MaxErrors = maxErrors,
            EmitPackage = emitPackage,
            SeparateAssemblies = separateAssemblies,
            BuildRuntime = buildRuntime,
            ExtraReferences = extraReferences,
            ExtraDefines = extraDefines,
            AssemblyVersion = assemblyVersion,
            MetadataOnlyAssembly = metadataOnlyAssembly,
            Incremental = incremental,
            NoChunkCoalescing = noChunkCoalescing,
            CacheDir = cacheDir,
        });

        // --watch rebuilds this same project over and over as its sources change, serving the result
        // from a directory it can keep pointing a browser at — only a site build (a tps.json project,
        // no --emit-package/--build-runtime/--out) produces that. Reject the combination up front
        // rather than starting a server for a build that will never populate an outDir.
        if (watch && !ProjectBuild.IsSiteBuild(options))
        {
            MsBuildDiagnostic.WriteError(MsBuildDiagnostic.CodeWatchRequiresSiteBuild,
                "--watch requires a site build: the project must have a tps.json and not use --emit-package, --build-runtime, or --out.");
            return 1;
        }

        var sw = Stopwatch.StartNew();
        if (watch)
        {
            // Every rebuild re-times its own phases, and the timing report is a whole-invocation
            // summary — a running watch server has no "the build" to report on, so the report is
            // simply not printed between rebuilds.
            return WatchMode.Run(csproj, watchPort,
                liveReloadScript => ProjectBuild.Run(options with { LiveReloadScript = liveReloadScript }));
        }

        var outcome = ProjectBuild.Run(options);
        PrintTimings(sw.ElapsedMilliseconds);
        return outcome.ExitCode;
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
        catch (Exception ex)
        {
            MsBuildDiagnostic.WriteWarning(MsBuildDiagnostic.CodeTimingJsonNotWritten,
                $"could not write --timing-json: {ex.Message}");
        }
    }

    private static string JsonString(string s)
    {
        var sb = new System.Text.StringBuilder("\"");
        foreach (var c in s)
            sb.Append(c switch { '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", _ => c.ToString() });
        return sb.Append('"').ToString();
    }

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
                MsBuildDiagnostic.WriteError(MsBuildDiagnostic.CodeInvalidCommandLine,
                    $"Multiple .csproj files in '{arg}'; pass one explicitly.");
                return null;
            }
        }
        return null;
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
              --max-errors <n>      Cap how many individual errors are printed. By default there is
                                    no cap — every error is reported, ordered by file and line. Pass 0
                                    to restore that explicitly.
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
              --no-chunk-coalescing Emit one ES module per strongly-connected component instead of
                                    coalescing them into the size band (outputBy: "Module" only; also
                                    settable with TRANSPOSE_NO_CHUNK_COALESCING=1, which reaches a
                                    build driven by MSBuild). This is a build for MEASURING: hundreds
                                    of ~2 KB chunks are the wrong thing to serve, but they are the
                                    only way to see what a running application really fetches
                                    together — a coalesced build has already made that decision, so a
                                    capture of it can only confirm it. The evidence a
                                    tps.chunks.json oracle is written from comes from this build.
              --incremental, --no-incremental
                                    Reuse the previous build of this project where its inputs are
                                    unchanged (off by default). A build whose files all hash the same
                                    does nothing at all; one whose edits are confined to method and
                                    accessor bodies keeps the cached JavaScript of every type it did
                                    not touch, the reflection metadata, and — in a metadata-only
                                    configuration — the .NET assembly. Anything else (a changed
                                    declaration, a new or deleted file, a different reference or
                                    setting) is a full build. Emitted output is byte-identical either
                                    way; the cache lives in the project's obj/tps-cache/.
              --cache-dir <dir>     Put the incremental cache under <dir> instead of the project's
                                    obj/ (implies --incremental). A temp directory is fine — losing
                                    the cache only costs a full build.
              --watch               Rebuild whenever a source file changes — the root project's and
                                    every referenced project's — and serve the assembled site over
                                    HTTP on localhost. The served index.html carries a small injected
                                    script that reconnects over a websocket and reloads the page after
                                    each successful rebuild. A change confined to stylesheets the site
                                    copies from disk (tps.json `resources`) skips the compile entirely:
                                    the CSS is re-copied and the page swaps it in without reloading.
                                    Requires a site build (a project with tps.json; incompatible with
                                    --emit-package, --build-runtime, --out).
              --watch-port <n>      Port for --watch's HTTP server and websocket (default 4300)
              --timing              Print a per-phase timing/allocation breakdown of the build
              --timing-json <file>  Also write that breakdown (plus GC/memory totals) as JSON
              -q, --quiet           Suppress warning output
              -h, --help            Show this help
            """);
    }
}
