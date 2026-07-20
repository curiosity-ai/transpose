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
                case "--assembly-version": assemblyVersion = args[++i]; break;
                case "--project" or "-p": projectArg = args[++i]; break;
                default:
                    if (projectArg is null) projectArg = args[i];
                    else { Console.Error.WriteLine($"Unexpected argument: {args[i]}"); return 1; }
                    break;
            }
        }
        _ = assemblyVersion; // accepted for MSBuild-target compatibility; reflection metadata carries its own versioning

        var csproj = LocateProject(projectArg);
        if (csproj is null)
        {
            Console.Error.WriteLine($"No .csproj found at '{projectArg}'.");
            return 1;
        }

        // When the Transpose SDK invokes `tps --project` for a plain library — no explicit `--out`,
        // not the `--build-runtime` corlib, and no `tps.json` (so it is neither the ClassPath runtime
        // nor an app that builds a site) — produce the distributable package assembly, exactly as
        // `--emit-package` does: the .NET DLL (with the compiled JS + Transpose.Resources.json manifest
        // embedded) that `dotnet pack` wraps into `lib/<tfm>/<Assembly>.dll`. Without this the SDK
        // build emits only a stray .js and `dotnet pack` fails with NU5026 (<Assembly>.dll not found).
        // Projects that pass `--out` (bootstrap, tooling) keep writing a single .js bundle unchanged.
        if (!emitPackage && !buildRuntime && outPath is null
            && TransposeJson.TryLoad(Path.GetDirectoryName(csproj)!) is null)
        {
            emitPackage = true;
            separateAssemblies = true;
        }

        Console.WriteLine($"tps: compiling {Path.GetFileName(csproj)}");
        var sw = Stopwatch.StartNew();

        ResolvedProject project;
        try
        {
            project = ProjectResolver.Resolve(csproj, configuration, separateAssemblies);
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
        Console.WriteLine($"  lang:       {project.LanguageVersion}");

        // The project's tps.json drives runtime-package detection and reflection settings.
        var tpscfg = TransposeJson.TryLoad(project.ProjectDir);

        // Base runtime package build: compile Transpose.BCL self-contained, transpile it with
        // outputBy: ClassPath, stitch the per-class files with the hand-written Resources primitives
        // into tps.js per the project's tps.json, and embed tps.js + tps.meta.js into Transpose.dll.
        //
        // A project that declares outputBy: ClassPath in its tps.json *defines* the BCL (it is the
        // base runtime library), so it is always built this way — self-contained, with no
        // Transpose.dll reference — even when the SDK invokes tps without an explicit --build-runtime.
        if (buildRuntime || string.Equals(tpscfg?.OutputBy, "ClassPath", StringComparison.OrdinalIgnoreCase))
            return BuildRuntime(project, configuration, sw, outPath);

        // Reflection settings come from the project's tps.json (target inline vs a .meta.js file).
        var (reflectionEnabled, metadataTarget) = ReflectionSettings(tpscfg);

        // Chain the referenced projects first (like the MSBuild-driven compiler): in
        // separate-assembly / package mode this project binds against its dependencies' built DLLs
        // and extracts their embedded JS, so each must be compiled — in dependency order — before
        // this one. Up-to-date packages are skipped.
        if (separateAssemblies && !EnsureReferencedProjectsBuilt(csproj, configuration, maxErrors))
        {
            Console.Error.WriteLine("\nFAILED building referenced projects.");
            return 1;
        }

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
                emitAssembly: emitPackage);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Translator threw: {ex}");
            return 2;
        }

        sw.Stop();

        if (!result.Success)
        {
            ReportDiagnostics(result.Diagnostics, maxErrors);
            Console.Error.WriteLine($"\nFAILED in {sw.ElapsedMilliseconds} ms.");
            return 1;
        }

        var js = result.Javascript!;
        if (!quiet) ReportDiagnostics(result.Diagnostics, maxErrors); // surface warnings
        var config = tpscfg;
        var minified = configuration.Equals("Release", StringComparison.OrdinalIgnoreCase);

        // Package mode: compile this project as a distributable assembly — emit the .NET DLL and
        // embed its JS (+ tps.json resources) so another project can reference it and extract them.
        if (emitPackage)
        {
            var (dllPath, items) = WritePackage(project, config, configuration, result);
            Console.WriteLine($"\nOK — built package {project.AssemblyName}.dll ({result.AssemblyBytes!.Length:N0} bytes) with {items.Count} embedded resource(s) in {sw.ElapsedMilliseconds} ms.");
            Console.WriteLine($"  dll:      {dllPath}");
            Console.WriteLine($"  embedded: {string.Join(", ", items.Take(6).Select(i => i.Name))}{(items.Count > 6 ? ", …" : "")}");
            return 0;
        }

        // Site build: when the project has an tps.json and no single-file --out was requested,
        // assemble a runnable output folder (runtime JS + bundle + resources + index.html),
        // exactly like the existing tps compiler.
        if (config is not null && outPath is null)
        {
            var outDir = siteDir ?? ResolveOutputDir(config, project.ProjectDir, configuration);
            OutputBuilder.Build(project, config, js, outDir, minified, result.MetadataJavascript);
            Console.WriteLine($"\nOK — built site in {outDir} ({js.Length:N0} bytes of {config.FileName}) in {sw.ElapsedMilliseconds} ms.");
            Console.WriteLine($"  index.html: {(config.HtmlDisabled ? "disabled" : "generated")}");
            return 0;
        }

        if (withRuntime) js = RoslynTranslator.LoadRuntime() + "\n" + js;
        outPath ??= Path.Combine(project.ProjectDir, "bin", project.AssemblyName + ".js");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, js);
        Console.WriteLine($"\nOK — wrote {js.Length:N0} bytes to {outPath} in {sw.ElapsedMilliseconds} ms.");
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
        var cfg = TransposeJson.TryLoad(project.ProjectDir);
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
        Console.WriteLine($"  emitted:    {result.ClassPath.Files.Count} ClassPath file(s) into Resources/.generated");
        if (result.ClassPath.Skipped.Count > 0)
        {
            Console.WriteLine($"  skipped:    {result.ClassPath.Skipped.Count} type(s) the emitter could not translate:");
            foreach (var (t, why) in result.ClassPath.Skipped.Take(20)) Console.WriteLine($"                - {t}: {why}");
        }

        // Assemble the resource bundles (tps.js, tps.meta.js, …) declared in tps.json.
        var bundles = RuntimeAssembler.Assemble(project.ProjectDir);
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
    private static bool EnsureReferencedProjectsBuilt(string rootCsproj, string configuration, int maxErrors)
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
            if (!BuildPackage(dep, configuration, maxErrors))
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
    private static bool BuildPackage(string csproj, string configuration, int maxErrors)
    {
        ResolvedProject project;
        try { project = ProjectResolver.Resolve(csproj, configuration, separateAssemblies: true); }
        catch (Exception ex) { Console.Error.WriteLine($"    resolve failed: {ex.Message}"); return false; }

        var tpscfg = TransposeJson.TryLoad(project.ProjectDir);
        var (reflectionEnabled, metadataTarget) = ReflectionSettings(tpscfg);

        AssemblyBuildResult result;
        try
        {
            result = new RoslynTranslator().BuildAssembly(
                project.Sources, project.AssemblyName, project.ReferencePaths,
                project.DefineConstants, project.LanguageVersion,
                reflectionEnabled, metadataTarget, emitAssembly: true);
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
        File.WriteAllBytes(dllPath, result.AssemblyBytes!);

        var items = config is not null
            ? OutputBuilder.CollectEmbeddableItems(project.ProjectDir, config, mainJsName, result.Javascript!, result.MetadataJavascript)
            : new List<EmbeddedItem> { new(mainJsName, System.Text.Encoding.UTF8.GetBytes(result.Javascript!), null) };
        ResourceEmbedder.Embed(dllPath, items);
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
              -q, --quiet           Suppress warning output
              -h, --help            Show this help
            """);
    }
}
