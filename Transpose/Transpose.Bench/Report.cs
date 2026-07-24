using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Transpose.Bench;

/// <summary>
/// Formats a benchmark run for humans (console + Markdown) and for machines (JSON, so a later run
/// can diff against it with <c>--baseline</c>).
///
/// Every timing appears twice: as measured, and <b>normalised</b> — multiplied by the machine's CPU
/// score ÷ 100. A slow machine's 30 s build and a fast machine's 12 s build normalise to the same
/// number, which is the only way a result recorded in one session can be compared to another.
/// </summary>
internal sealed record Report(
    string Label,
    MachineInfo Machine,
    CpuScore.Result? Cpu,
    string Configuration,
    int Iterations,
    IReadOnlyList<ScenarioResult> Results)
{
    /// <summary>Scales a measured duration to reference-machine time. A machine that scores 200
    /// (twice the reference) has its 5 s build reported as a normalised 10 s, so the normalised
    /// number is machine-independent and directly comparable across sessions.</summary>
    public static double Normalise(double ms, double score) => ms * score / 100.0;

    public string Describe(double score)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Results");
        sb.AppendLine($"  (normalised = measured × score/100, i.e. reference-machine milliseconds; score {score:F1})");
        sb.AppendLine();
        sb.AppendLine($"  {"scenario",-22} {"wall (median)",14} {"normalised",12} {"±stddev",9} {"cpu-time",10} {"alloc",10} {"peak WS",10}");
        sb.AppendLine($"  {new string('-', 22)} {new string('-', 14)} {new string('-', 12)} {new string('-', 9)} {new string('-', 10)} {new string('-', 10)} {new string('-', 10)}");
        foreach (var r in Results)
        {
            if (!r.Succeeded) { sb.AppendLine($"  {r.Scenario.Name,-22} FAILED"); continue; }
            sb.AppendLine($"  {r.Scenario.Name,-22} {r.MedianWallMs / 1000,11:F2} s  {Normalise(r.MedianWallMs, score) / 1000,9:F2} s  "
                + $"{r.StdDevMs / 1000,7:F2} s  {r.MedianCpuMs / 1000,8:F2} s  {Fmt.Mb(r.MedianAllocBytes),10} {Fmt.Mb(r.MedianPeakWsBytes),10}");
        }
        sb.AppendLine();

        foreach (var r in Results)
        {
            if (!r.Succeeded) continue;
            var phases = r.MedianPhases();
            if (phases.Count == 0) continue;
            sb.AppendLine($"  {r.Scenario.Name} — phase breakdown (median across {r.Iterations.Count} run(s))");
            // Sub-phases are indented in their name and already counted inside their parent.
            var topLevel = phases.Where(p => !p.name.StartsWith(' ')).ToList();
            var total = topLevel.Sum(p => p.ms);
            foreach (var (name, ms, bytes) in phases)
            {
                var isSub = name.StartsWith(' ');
                var share = isSub || total <= 0 ? "" : $"{ms * 100.0 / total,5:F1}%";
                sb.AppendLine($"      {ms,8:N0} ms  {share,6}  {Fmt.Mb(bytes),10}  {name}");
            }
            sb.AppendLine($"      {total,8:N0} ms  {"",6}  {Fmt.Mb(topLevel.Sum(p => p.bytes)),10}  (sum of top-level phases)");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public string CompareTo(Baseline baseline, double score)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Comparison vs baseline '{baseline.Label}' (recorded on {baseline.CpuModel}, score {baseline.Score:F1})");
        sb.AppendLine($"  {"scenario",-22} {"baseline",12} {"current",12} {"delta",12} {"alloc delta",14}");
        sb.AppendLine($"  {new string('-', 22)} {new string('-', 12)} {new string('-', 12)} {new string('-', 12)} {new string('-', 14)}");
        foreach (var r in Results)
        {
            if (!r.Succeeded) continue;
            var bs = baseline.Scenarios.FirstOrDefault(s => s.Name == r.Scenario.Name);
            if (bs is null) { sb.AppendLine($"  {r.Scenario.Name,-22} (not in baseline)"); continue; }
            var cur = Normalise(r.MedianWallMs, score);
            var pct = bs.NormalisedWallMs > 0 ? (cur - bs.NormalisedWallMs) / bs.NormalisedWallMs * 100 : 0;
            var allocPct = bs.AllocBytes > 0 ? (r.MedianAllocBytes - bs.AllocBytes) / bs.AllocBytes * 100 : 0;
            sb.AppendLine($"  {r.Scenario.Name,-22} {bs.NormalisedWallMs / 1000,9:F2} s  {cur / 1000,9:F2} s  "
                + $"{pct,+10:F1}%  {allocPct,+12:F1}%");
        }
        sb.AppendLine("  (negative = faster / less allocation than the baseline)");
        return sb.ToString();
    }

    public void WriteJson(string path, double score)
    {
        var dto = new BaselineDto
        {
            Label = Label,
            Score = score,
            CpuModel = Machine.CpuModel,
            LogicalCores = Machine.LogicalCores,
            PhysicalCores = Machine.PhysicalCores,
            TotalRamBytes = Machine.TotalRamBytes,
            Os = Machine.Os,
            Runtime = Machine.Runtime,
            Capabilities = Machine.Capabilities.ToList(),
            Configuration = Configuration,
            Iterations = Iterations,
            CpuWorkloads = Cpu?.Workloads.Select(w => new WorkloadDto { Name = w.Name, Ms = w.Ms, ReferenceMs = w.ReferenceMs, Score = w.Score }).ToList(),
            Scenarios = Results.Where(r => r.Succeeded).Select(r => new ScenarioDto
            {
                Name = r.Scenario.Name,
                Project = r.Scenario.CsprojPath,
                Clean = r.Scenario.Clean,
                WallMs = r.MedianWallMs,
                MinWallMs = r.MinWallMs,
                StdDevMs = r.StdDevMs,
                NormalisedWallMs = Normalise(r.MedianWallMs, score),
                CpuMs = r.MedianCpuMs,
                AllocBytes = r.MedianAllocBytes,
                PeakWorkingSetBytes = r.MedianPeakWsBytes,
                Phases = r.MedianPhases().Select(p => new PhaseDto { Name = p.name, Ms = p.ms, Bytes = p.bytes }).ToList(),
            }).ToList(),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void WriteMarkdown(string path, double score, Baseline? baseline)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Transpose compiler benchmark — {Label}");
        sb.AppendLine();
        sb.AppendLine($"- **CPU**: {Machine.CpuModel} — {Machine.PhysicalCores} physical / {Machine.LogicalCores} logical cores");
        sb.AppendLine($"- **RAM**: {Fmt.Gb(Machine.TotalRamBytes)}");
        sb.AppendLine($"- **OS / runtime**: {Machine.Os} · {Machine.Runtime} ({Machine.Architecture})");
        sb.AppendLine($"- **CPU features**: `{string.Join(" ", Machine.Capabilities)}`");
        sb.AppendLine($"- **CPU score**: **{score:F1}** (100 = reference machine; normalised times are `measured × score/100`)");
        sb.AppendLine($"- **Configuration**: {Configuration}, {Iterations} measured iteration(s) per scenario");
        sb.AppendLine();
        sb.AppendLine("| scenario | wall (median) | normalised | ±stddev | cpu-time | alloc | peak WS |");
        sb.AppendLine("|---|--:|--:|--:|--:|--:|--:|");
        foreach (var r in Results.Where(r => r.Succeeded))
            sb.AppendLine($"| `{r.Scenario.Name}` | {r.MedianWallMs / 1000:F2} s | {Normalise(r.MedianWallMs, score) / 1000:F2} s "
                + $"| {r.StdDevMs / 1000:F2} s | {r.MedianCpuMs / 1000:F2} s | {Fmt.Mb(r.MedianAllocBytes)} | {Fmt.Mb(r.MedianPeakWsBytes)} |");
        sb.AppendLine();

        foreach (var r in Results.Where(r => r.Succeeded))
        {
            var phases = r.MedianPhases();
            if (phases.Count == 0) continue;
            var total = phases.Where(p => !p.name.StartsWith(' ')).Sum(p => p.ms);
            sb.AppendLine($"## `{r.Scenario.Name}` phase breakdown");
            sb.AppendLine();
            sb.AppendLine("| phase | ms | share | alloc |");
            sb.AppendLine("|---|--:|--:|--:|");
            foreach (var (name, ms, bytes) in phases)
            {
                var share = name.StartsWith(' ') || total <= 0 ? "" : $"{ms * 100.0 / total:F1}%";
                sb.AppendLine($"| {name.Replace("├", "&boxvr;").Replace("└", "&boxur;")} | {ms:N0} | {share} | {Fmt.Mb(bytes)} |");
            }
            sb.AppendLine();
        }

        if (baseline is not null) sb.AppendLine("```\n" + CompareTo(baseline, score) + "```");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, sb.ToString());
    }
}

// ---- JSON shapes (also the --baseline input format) ------------------------------------------

internal sealed class BaselineDto
{
    public string Label { get; set; } = "";
    public double Score { get; set; }
    public string CpuModel { get; set; } = "";
    public int LogicalCores { get; set; }
    public int PhysicalCores { get; set; }
    public long TotalRamBytes { get; set; }
    public string Os { get; set; } = "";
    public string Runtime { get; set; } = "";
    public List<string>? Capabilities { get; set; }
    public string Configuration { get; set; } = "";
    public int Iterations { get; set; }
    public List<WorkloadDto>? CpuWorkloads { get; set; }
    public List<ScenarioDto> Scenarios { get; set; } = new();
}

internal sealed class WorkloadDto
{
    public string Name { get; set; } = "";
    public double Ms { get; set; }
    public double ReferenceMs { get; set; }
    public double Score { get; set; }
}

internal sealed class ScenarioDto
{
    public string Name { get; set; } = "";
    public string Project { get; set; } = "";
    public bool Clean { get; set; }
    public double WallMs { get; set; }
    public double MinWallMs { get; set; }
    public double StdDevMs { get; set; }
    public double NormalisedWallMs { get; set; }
    public double CpuMs { get; set; }
    public double AllocBytes { get; set; }
    public double PeakWorkingSetBytes { get; set; }
    public List<PhaseDto> Phases { get; set; } = new();
}

internal sealed class PhaseDto
{
    public string Name { get; set; } = "";
    public double Ms { get; set; }
    public double Bytes { get; set; }
}

/// <summary>A previously recorded run, loaded from <c>--json</c> output for comparison.</summary>
internal sealed record Baseline(string Label, double Score, string CpuModel, IReadOnlyList<ScenarioDto> Scenarios)
{
    public static Baseline? Load(string path)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<BaselineDto>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto is null) return null;
            return new Baseline(dto.Label, dto.Score, dto.CpuModel, dto.Scenarios);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read baseline '{path}': {ex.Message}");
            return null;
        }
    }
}
