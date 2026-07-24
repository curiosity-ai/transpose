using System.Text;
using System.Text.Json;

namespace Transpose.Bench;

/// <summary>
/// <c>tps-bench</c> — the benchmark harness for the Transpose compiler.
///
/// It answers "did that change make the compiler faster?" in a way that survives being run on a
/// different machine, or a noisy one:
///
///  1. Print the machine (CPU model, cores, RAM, SIMD/crypto ISAs) — timings without it are
///     uninterpretable.
///  2. Run a short deterministic CPU + memory benchmark and derive a <b>score</b> (100 = the
///     reference machine). Every compiler timing is then also reported <i>normalised</i>
///     (measured × score ÷ 100), which is what makes two machines' numbers comparable.
///  3. Run the compiler over real projects, from a genuinely clean slate (wiping the project's and
///     its dependencies' bin/obj, because <c>tps</c> otherwise skips up-to-date dependencies), and
///     report wall time, CPU time, allocations, peak working set, GC counts, and the compiler's own
///     per-phase breakdown.
///
/// Usage:
///   tps-bench --tps &lt;path-to-tps&gt; [--scenario name=project.csproj[:clean|:warm]] …
///             [--iterations N] [--warmups N] [--configuration Debug|Release]
///             [--json out.json] [--markdown out.md] [--baseline prev.json] [--no-cpu-bench]
///
/// With no <c>--scenario</c>, the two standard scenarios are used if the sibling <c>tesserae</c>
/// checkout is found: the single-project build (Tesserae) and the multi-project build
/// (Tesserae.Tests, which also builds its Tesserae dependency).
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("-h") || args.Contains("--help")) { ShowHelp(); return 0; }

        var tps = "tps";
        var configuration = "Debug";
        var iterations = 3;
        var warmups = 1;
        string? jsonOut = null, markdownOut = null, baselineIn = null, label = null;
        var runCpuBench = true;
        var verbose = false;
        var scenarioArgs = new List<string>();
        string? tesseraeRoot = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--tps": tps = args[++i]; break;
                case "--configuration" or "-c": configuration = args[++i]; break;
                case "--iterations" or "-n": iterations = int.Parse(args[++i]); break;
                case "--warmups": warmups = int.Parse(args[++i]); break;
                case "--scenario" or "-s": scenarioArgs.Add(args[++i]); break;
                case "--tesserae": tesseraeRoot = args[++i]; break;
                case "--json": jsonOut = args[++i]; break;
                case "--markdown": markdownOut = args[++i]; break;
                case "--baseline": baselineIn = args[++i]; break;
                case "--label": label = args[++i]; break;
                case "--no-cpu-bench": runCpuBench = false; break;
                case "--verbose" or "-v": verbose = true; break;
                default:
                    Console.Error.WriteLine($"Unexpected argument: {args[i]}");
                    return 1;
            }
        }

        var machine = MachineInfo.Collect();
        Console.WriteLine(machine.Describe());

        CpuScore.Result? cpu = null;
        if (runCpuBench)
        {
            Console.WriteLine("Running CPU benchmark (fixed work, best-of-3 per workload) …");
            cpu = CpuScore.Run();
            Console.WriteLine(cpu.Describe());
        }
        var score = cpu?.Score ?? 100.0;

        var scenarios = scenarioArgs.Count > 0
            ? scenarioArgs.Select(a => ParseScenario(a, configuration)).ToList()
            : DefaultScenarios(tesseraeRoot, configuration);

        if (scenarios.Count == 0)
        {
            Console.Error.WriteLine(
                "No scenarios. Pass --scenario name=project.csproj[:clean|:warm], or --tesserae <repo-root> "
                + "so the default Tesserae scenarios can be located.");
            return 1;
        }

        var resolvedTps = ResolveTps(tps);
        if (resolvedTps is null)
        {
            Console.Error.WriteLine($"Could not find the tps compiler at '{tps}'. Pass --tps <path>.");
            return 1;
        }
        Console.WriteLine($"Compiler: {resolvedTps}");
        Console.WriteLine($"Config:   {configuration}   iterations: {iterations} (+{warmups} warm-up)\n");

        var tempDir = Path.Combine(Path.GetTempPath(), "tps-bench");
        var runner = new ScenarioRunner(resolvedTps, tempDir, verbose);

        var results = new List<ScenarioResult>();
        foreach (var s in scenarios)
        {
            Console.WriteLine($"Scenario {s.Name}  ({(s.Clean ? "clean slate" : "warm/incremental")}) — {Path.GetFileName(s.CsprojPath)}");
            results.Add(runner.Run(s, warmups, iterations));
            Console.WriteLine();
        }

        var report = new Report(label ?? "current", machine, cpu, configuration, iterations, results);
        Console.WriteLine(report.Describe(score));

        Baseline? baseline = baselineIn is not null ? Baseline.Load(baselineIn) : null;
        if (baseline is not null) Console.WriteLine(report.CompareTo(baseline, score));

        if (jsonOut is not null) { report.WriteJson(jsonOut, score); Console.WriteLine($"Wrote {jsonOut}"); }
        if (markdownOut is not null) { report.WriteMarkdown(markdownOut, score, baseline); Console.WriteLine($"Wrote {markdownOut}"); }

        return results.All(r => r.Succeeded) ? 0 : 1;
    }

    /// <summary>Parses <c>name=path.csproj[:clean|:warm]</c>. Clean is the default — an incremental
    /// build measures a different (much cheaper) code path and must be asked for explicitly.</summary>
    private static Scenario ParseScenario(string arg, string configuration)
    {
        var eq = arg.IndexOf('=');
        var name = eq > 0 ? arg[..eq] : Path.GetFileNameWithoutExtension(arg);
        var rest = eq > 0 ? arg[(eq + 1)..] : arg;
        var clean = true;
        string? note = null;
        if (rest.EndsWith(":warm", StringComparison.OrdinalIgnoreCase))
        {
            clean = false; rest = rest[..^5];
            note = "outputs left in place — measures the incremental path";
        }
        else if (rest.EndsWith(":clean", StringComparison.OrdinalIgnoreCase))
        {
            rest = rest[..^6];
        }
        return new Scenario(name, Path.GetFullPath(rest), clean, configuration, note);
    }

    /// <summary>The two standard scenarios: a single project, and a multi-project build where the
    /// root project's dependency must be compiled first. Located relative to this repository (the
    /// <c>tesserae</c> checkout is normally a sibling of <c>transpose</c>).</summary>
    private static List<Scenario> DefaultScenarios(string? tesseraeRoot, string configuration)
    {
        var roots = new List<string>();
        if (tesseraeRoot is not null) roots.Add(tesseraeRoot);
        var here = AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(here); d is not null; d = d.Parent)
        {
            roots.Add(Path.Combine(d.FullName, "tesserae"));
            roots.Add(Path.Combine(d.FullName, "..", "tesserae"));
        }
        roots.Add(Path.Combine(Directory.GetCurrentDirectory(), "..", "tesserae"));

        foreach (var root in roots)
        {
            var lib = Path.Combine(root, "Tesserae", "Tesserae.csproj");
            var tests = Path.Combine(root, "Tesserae.Tests", "Tesserae.Tests.csproj");
            if (!File.Exists(lib)) continue;
            var list = new List<Scenario>
            {
                new("tesserae", Path.GetFullPath(lib), true, configuration,
                    "single project, ~69k LOC / 263 files → package DLL with embedded JS"),
            };
            if (File.Exists(tests))
            {
                list.Add(new("tesserae+tests", Path.GetFullPath(tests), true, configuration,
                    "multi-project: builds the Tesserae dependency, then the site build"));
                list.Add(new("tesserae+tests-warm", Path.GetFullPath(tests), false, configuration,
                    "incremental: dependency already up to date, only the site project recompiles"));
            }
            return list;
        }
        return new List<Scenario>();
    }

    private static string? ResolveTps(string tps)
    {
        if (File.Exists(tps)) return Path.GetFullPath(tps);
        if (Directory.Exists(tps))
        {
            var candidate = Path.Combine(tps, OperatingSystem.IsWindows() ? "tps.exe" : "tps");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        // Bare command name: let the OS resolve it from PATH, but verify it exists first so the
        // failure is a clear message rather than a Win32Exception mid-run.
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            var candidate = Path.Combine(dir, tps);
            if (File.Exists(candidate)) return candidate;
            if (OperatingSystem.IsWindows() && File.Exists(candidate + ".exe")) return candidate + ".exe";
        }
        return null;
    }

    private static void ShowHelp() => Console.WriteLine("""
        tps-bench — Transpose compiler benchmark harness

        Prints the machine's CPU/RAM/ISA details, scores it with a short deterministic CPU+memory
        benchmark, then times `tps` builds from a clean slate and reports wall time, CPU time,
        allocations, peak working set and the compiler's per-phase breakdown — both raw and
        normalised by the CPU score, so results from different machines are comparable.

        Usage:
          tps-bench --tps <path-to-tps> [options]

        Options:
          --tps <path>              The tps compiler to measure (file, directory, or PATH command).
          -s, --scenario <spec>     name=project.csproj[:clean|:warm]  (repeatable; clean is default)
          --tesserae <repo-root>    Locate the default Tesserae scenarios explicitly.
          -c, --configuration <c>   Build configuration passed to tps (default Debug).
          -n, --iterations <N>      Measured iterations per scenario (default 3).
          --warmups <N>             Discarded iterations before measuring (default 1).
          --label <name>            Name this run in the report (e.g. a commit or "baseline").
          --json <file>             Write the full result as JSON (use as a --baseline later).
          --markdown <file>         Write a Markdown summary table.
          --baseline <file>         Compare against a previous --json run and print the deltas.
          --no-cpu-bench            Skip the CPU benchmark (score assumed 100).
          -v, --verbose             Echo the compiler's output for each run.
        """);
}
