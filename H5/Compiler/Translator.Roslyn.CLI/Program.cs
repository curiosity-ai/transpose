using System.Diagnostics;
using Microsoft.CodeAnalysis;

namespace H5.Translator.Roslyn.CLI;

/// <summary>
/// A small command-line front-end for the Roslyn-only C# → JavaScript translator, in the
/// spirit of the existing <c>h5</c> compiler: point it at a project and it resolves the
/// sources and package references, runs the translator, and writes the JavaScript bundle.
///
///   h5-roslyn &lt;project.csproj|dir&gt; [--out &lt;file.js&gt;] [--with-runtime] [--quiet]
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

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" or "-o": outPath = args[++i]; break;
                case "--site-dir": siteDir = args[++i]; break;
                case "--configuration" or "-c": configuration = args[++i]; break;
                case "--with-runtime": withRuntime = true; break;
                case "--quiet" or "-q": quiet = true; break;
                case "--max-errors": maxErrors = int.Parse(args[++i]); break;
                default:
                    if (projectArg is null) projectArg = args[i];
                    else { Console.Error.WriteLine($"Unexpected argument: {args[i]}"); return 1; }
                    break;
            }
        }

        var csproj = LocateProject(projectArg);
        if (csproj is null)
        {
            Console.Error.WriteLine($"No .csproj found at '{projectArg}'.");
            return 1;
        }

        Console.WriteLine($"h5-roslyn: compiling {Path.GetFileName(csproj)}");
        var sw = Stopwatch.StartNew();

        ResolvedProject project;
        try
        {
            project = ProjectResolver.Resolve(csproj);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to resolve project: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"  sources:    {project.Sources.Count} file(s)");
        Console.WriteLine($"  references: {project.ReferencePaths.Count} assembly(ies) — {string.Join(", ", project.ReferencePaths.Select(Path.GetFileNameWithoutExtension))}");
        Console.WriteLine($"  defines:    {string.Join(";", project.DefineConstants)}");
        Console.WriteLine($"  lang:       {project.LanguageVersion}");

        // Reflection settings come from the project's h5.json (target inline vs a .meta.js file).
        var h5cfg = H5Json.TryLoad(project.ProjectDir);
        var reflectionEnabled = !(h5cfg?.ReflectionDisabled ?? false);
        var metadataTarget = (h5cfg?.ReflectionTarget ?? "file").ToLowerInvariant() switch
        {
            "inline" => MetadataTarget.Inline,
            "type" => MetadataTarget.Type,
            "assembly" => MetadataTarget.Assembly,
            _ => MetadataTarget.File,
        };

        var translator = new RoslynTranslator();
        TranslationResult result;
        try
        {
            result = translator.Translate(
                project.Sources,
                project.AssemblyName,
                project.ReferencePaths,
                project.DefineConstants,
                project.LanguageVersion,
                reflectionEnabled,
                metadataTarget);
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

        // Site build: when the project has an h5.json and no single-file --out was requested,
        // assemble a runnable output folder (runtime JS + bundle + resources + index.html),
        // exactly like the existing h5 compiler.
        var config = h5cfg;
        var minified = configuration.Equals("Release", StringComparison.OrdinalIgnoreCase);
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

    /// <summary>Resolves h5.json's output path, expanding the $(OutDir) MSBuild token.</summary>
    private static string ResolveOutputDir(H5Json config, string projectDir, string configuration)
    {
        var outBase = Path.Combine(projectDir, "bin", configuration, "netstandard2.0");
        var raw = (config.Output ?? "$(OutDir)/h5/").Replace("$(OutDir)", outBase).Replace('\\', '/');
        return Path.GetFullPath(raw);
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
            h5-roslyn — Roslyn-only C# → JavaScript translator (experimental)

            Usage:
              h5-roslyn <project.csproj | directory> [options]

            Options:
              -o, --out <file.js>   Output path (default: <project>/bin/<assembly>.js)
              -c, --configuration <name>
                                    Build configuration (Debug/Release; default Debug). Release
                                    selects the .min.js resource variants where both exist.
              --site-dir <dir>      Output directory for the assembled site
              --with-runtime        Prepend the h5.js runtime + shim to the output
              --max-errors <n>      Max individual errors to print (default 40)
              -q, --quiet           Suppress warning output
              -h, --help            Show this help
            """);
    }
}
