using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace Transpose.Bench;

/// <summary>
/// One thing to measure: run <c>tps</c> on a project, optionally after wiping every build output it
/// and its project references produce ("clean slate"). Cleaning matters because <c>tps</c> skips a
/// referenced project whose package DLL is already up to date — without a wipe the second iteration
/// would measure a different, much cheaper build than the first.
/// </summary>
internal sealed record Scenario(string Name, string CsprojPath, bool Clean, string Configuration, string? Note)
{
    /// <summary>The <c>bin</c>/<c>obj</c> directories to wipe before a clean run: this project's and
    /// those of every project it references, transitively (a stale dependency DLL is exactly what
    /// makes a build incremental).</summary>
    public IReadOnlyList<string> OutputDirsToClean()
    {
        var dirs = new List<string>();
        foreach (var proj in ProjectClosure(CsprojPath))
        {
            var dir = Path.GetDirectoryName(proj)!;
            dirs.Add(Path.Combine(dir, "bin"));
            dirs.Add(Path.Combine(dir, "obj"));
        }
        return dirs;
    }

    /// <summary>The project and all its transitive <c>ProjectReference</c>s.</summary>
    public static IReadOnlyList<string> ProjectClosure(string csproj)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        void Walk(string p)
        {
            p = Path.GetFullPath(p);
            if (!File.Exists(p) || !seen.Add(p)) return;
            order.Add(p);
            var dir = Path.GetDirectoryName(p)!;
            XDocument doc;
            try { doc = XDocument.Load(p); } catch { return; }
            foreach (var pr in doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
            {
                var include = pr.Attribute("Include")?.Value;
                if (!string.IsNullOrWhiteSpace(include))
                    Walk(Path.Combine(dir, include!.Replace('\\', '/')));
            }
        }
        Walk(csproj);
        return order;
    }
}

/// <summary>The measurements from a single <c>tps</c> invocation.</summary>
internal sealed record Iteration(
    double WallMs,
    long CompilerReportedMs,
    long TotalAllocatedBytes,
    long PeakWorkingSetBytes,
    long ProcessorTimeMs,
    int Gen0, int Gen1, int Gen2,
    IReadOnlyList<PhaseSample> Phases,
    bool Succeeded,
    string Output);

internal sealed record PhaseSample(string Name, long Ms, long Bytes, int Count);

/// <summary>Everything measured for one scenario across its iterations.</summary>
internal sealed record ScenarioResult(Scenario Scenario, IReadOnlyList<Iteration> Iterations)
{
    public bool Succeeded => Iterations.Count > 0 && Iterations.All(i => i.Succeeded);
    public IReadOnlyList<double> WallMsSamples => Iterations.Select(i => i.WallMs).ToArray();
    public double MedianWallMs => Fmt.Median(WallMsSamples);
    public double MinWallMs => Iterations.Count == 0 ? 0 : Iterations.Min(i => i.WallMs);
    public double StdDevMs => Fmt.StdDev(WallMsSamples);
    public double MedianAllocBytes => Fmt.Median(Iterations.Select(i => (double)i.TotalAllocatedBytes).ToArray());
    public double MedianPeakWsBytes => Fmt.Median(Iterations.Select(i => (double)i.PeakWorkingSetBytes).ToArray());
    public double MedianCpuMs => Fmt.Median(Iterations.Select(i => (double)i.ProcessorTimeMs).ToArray());

    /// <summary>Per-phase medians across iterations, in the order the compiler reported them.</summary>
    public IReadOnlyList<(string name, double ms, double bytes)> MedianPhases()
    {
        var order = new List<string>();
        var byName = new Dictionary<string, List<(double ms, double bytes)>>();
        foreach (var it in Iterations)
            foreach (var p in it.Phases)
            {
                if (!byName.TryGetValue(p.Name, out var list)) { byName[p.Name] = list = new(); order.Add(p.Name); }
                list.Add((p.Ms, p.Bytes));
            }
        return order
            .Select(n => (n,
                Fmt.Median(byName[n].Select(x => x.ms).ToArray()),
                Fmt.Median(byName[n].Select(x => x.bytes).ToArray())))
            .ToArray();
    }
}

internal sealed class ScenarioRunner
{
    private readonly string _tps;
    private readonly string _tempDir;
    private readonly bool _verbose;
    private readonly IReadOnlyList<(string key, string value)> _env;

    /// <param name="env">Extra environment variables for each compiler process. The compiler's runtime
    /// configuration is load-bearing (TieredPGO off and Server GC are together worth ~40% of a build),
    /// and those are exactly the settings a DOTNET_* variable can override — so being able to set them
    /// per run is what makes "is that still the right default on this runtime?" a measurable question
    /// rather than a belief.</param>
    public ScenarioRunner(string tpsPath, string tempDir, bool verbose,
                          IReadOnlyList<(string key, string value)>? env = null)
    {
        _tps = tpsPath;
        _tempDir = tempDir;
        _verbose = verbose;
        _env = env ?? Array.Empty<(string, string)>();
        Directory.CreateDirectory(_tempDir);
    }

    public ScenarioResult Run(Scenario scenario, int warmups, int iterations)
    {
        for (var i = 0; i < warmups; i++)
        {
            Console.Write($"  {scenario.Name}: warm-up {i + 1}/{warmups} … ");
            var w = RunOnce(scenario);
            Console.WriteLine(w.Succeeded ? $"{w.WallMs / 1000:F2} s (discarded)" : "FAILED");
            if (!w.Succeeded) { Console.WriteLine(Indent(w.Output)); return new ScenarioResult(scenario, new[] { w }); }
        }

        var samples = new List<Iteration>();
        for (var i = 0; i < iterations; i++)
        {
            Console.Write($"  {scenario.Name}: run {i + 1}/{iterations} … ");
            var it = RunOnce(scenario);
            samples.Add(it);
            Console.WriteLine(it.Succeeded
                ? $"{it.WallMs / 1000:F2} s   {Fmt.Mb(it.TotalAllocatedBytes)} alloc   peak {Fmt.Mb(it.PeakWorkingSetBytes)}"
                : "FAILED");
            if (!it.Succeeded) { Console.WriteLine(Indent(it.Output)); break; }
        }
        return new ScenarioResult(scenario, samples);
    }

    private static string Indent(string text) =>
        string.Join('\n', text.Split('\n').TakeLast(30).Select(l => "      " + l));

    private Iteration RunOnce(Scenario scenario)
    {
        if (scenario.Clean)
            foreach (var dir in scenario.OutputDirsToClean())
                if (Directory.Exists(dir))
                    try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }

        var statsPath = Path.Combine(_tempDir, $"stats-{Guid.NewGuid():N}.json");

        var psi = new ProcessStartInfo(_tps)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(scenario.CsprojPath);
        psi.ArgumentList.Add("--configuration");
        psi.ArgumentList.Add(scenario.Configuration);
        psi.ArgumentList.Add("--timing-json");
        psi.ArgumentList.Add(statsPath);
        foreach (var (key, value) in _env) psi.Environment[key] = value;

        var sw = Stopwatch.StartNew();
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        proc.WaitForExit();
        sw.Stop();

        var output = stdout.Result + stderr.Result;
        if (_verbose) Console.WriteLine();
        if (_verbose) Console.WriteLine(Indent(output));

        var stats = ReadStats(statsPath);
        try { if (File.Exists(statsPath)) File.Delete(statsPath); } catch { /* best effort */ }

        return new Iteration(
            WallMs: sw.Elapsed.TotalMilliseconds,
            CompilerReportedMs: stats?.WallClockMs ?? 0,
            TotalAllocatedBytes: stats?.TotalAllocatedBytes ?? 0,
            PeakWorkingSetBytes: stats?.PeakWorkingSetBytes ?? 0,
            ProcessorTimeMs: stats?.ProcessorTimeMs ?? 0,
            Gen0: stats?.Gen0 ?? 0, Gen1: stats?.Gen1 ?? 0, Gen2: stats?.Gen2 ?? 0,
            Phases: stats?.Phases?.Select(p => new PhaseSample(p.Name, p.Ms, p.Bytes, p.Count)).ToArray()
                    ?? Array.Empty<PhaseSample>(),
            Succeeded: proc.ExitCode == 0,
            Output: output);
    }

    private sealed record StatsDump(
        long WallClockMs, long TotalAllocatedBytes, long PeakWorkingSetBytes,
        int Gen0, int Gen1, int Gen2, long ProcessorTimeMs, List<PhaseDump>? Phases);
    private sealed record PhaseDump(string Name, long Ms, long Bytes, int Count);

    private static StatsDump? ReadStats(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<StatsDump>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }
}
