#if TRANSPOSE_INTROSPECT
using System.Diagnostics;
using System.Text;
using Memory.Introspect;
using Memory.Introspect.Trace;

namespace Transpose.Compiler;

/// <summary>
/// Opt-in self-profiling of a <c>tps</c> run with <c>Memory.Introspect</c>. Compiled in only when the
/// compiler is built with <c>-p:TransposeIntrospect=true</c> (the shipped tool carries none of it), and
/// switched on per run by <c>TPS_INTROSPECT</c>:
/// <list type="bullet">
///   <item><c>cpu</c> — sampling profile, reported as top methods by exclusive and inclusive time;</item>
///   <item><c>alloc</c> — allocation sampling, reported by type;</item>
///   <item><c>alloc-stacks</c> — the same with the call stacks the allocations came from (slow to analyse);</item>
///   <item><c>jit</c> — every method the runtime compiled, with its tier (<c>jit.nettrace</c>; summarise it
///   with an offline reader of <c>MethodLoadVerbose</c> events);</item>
///   <item><c>gcdump</c> — a heap graph captured when the build finishes (what is live at peak).</item>
/// </list>
/// One mode per run: two EventPipe sessions against the same process multiply the cost and would
/// distort the numbers each is there to measure. Output lands in <c>TPS_INTROSPECT_DIR</c>
/// (default <c>./tps-introspect</c>): the raw <c>.nettrace</c>/<c>.gcdump</c> plus a text report.
/// </summary>
internal sealed class Introspection : IDisposable
{
    private readonly string _mode;
    private readonly string _dir;
    private readonly CancellationTokenSource _stop = new();
    private readonly MemoryIntrospector _introspector = MemoryIntrospector.Create(new());
    private Task? _capture;

    private Introspection(string mode, string dir)
    {
        _mode = mode;
        _dir  = dir;
    }

    /// <summary>Starts the capture the environment asks for, or returns null when it asks for none.</summary>
    public static Introspection? StartFromEnvironment()
    {
        var mode = Environment.GetEnvironmentVariable("TPS_INTROSPECT");
        if (string.IsNullOrWhiteSpace(mode)) return null;

        var dir = Path.GetFullPath(Environment.GetEnvironmentVariable("TPS_INTROSPECT_DIR") ?? "tps-introspect");
        Directory.CreateDirectory(dir);

        var session = new Introspection(mode.Trim().ToLowerInvariant(), dir);
        session.Start();
        return session;
    }

    private void Start()
    {
        var pid   = Environment.ProcessId;
        var upper = TimeSpan.FromMinutes(10); // an upper bound: the build ends the capture, not the clock

        switch (_mode)
        {
            case "cpu":
                _capture = CaptureCpuAsync(pid, upper);
                break;
            case "alloc":
            case "alloc-stacks":
                _capture = CaptureAllocationsAsync(pid, upper, _mode == "alloc-stacks");
                break;
            case "jit":
                _capture = CaptureJitAsync(pid, upper);
                break;
            case "gcdump":
                return; // taken in Finish(), once the build has built its heap
            default:
                Console.Error.WriteLine($"tps: unknown TPS_INTROSPECT mode '{_mode}' (cpu, alloc, alloc-stacks, gcdump)");
                return;
        }

        // The session is opened asynchronously over the diagnostics socket; give it a moment so the
        // first phases of the build are inside the capture. The delay is outside the timed build.
        Thread.Sleep(1500);
    }

    /// <summary>Ends the capture and writes its report. Call after the build, before the process exits.</summary>
    public void Finish()
    {
        try
        {
            if (_mode == "gcdump")
            {
                var graph = _introspector.CollectMemoryGraphAsync(Environment.ProcessId).GetAwaiter().GetResult();
                if (graph.Success) graph.SaveToDisk(Path.Combine(_dir, "tps.gcdump"));
                Console.Error.WriteLine($"tps: heap graph {(graph.Success ? "written to " + _dir : "FAILED")}");
                return;
            }

            _stop.Cancel();
            _capture?.GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"tps: introspection failed: {e.Message}");
        }
    }

    private async Task CaptureCpuAsync(int pid, TimeSpan upper)
    {
        var sample = await _introspector.CollectSamplingProfileAsync(pid, upper, _stop.Token);
        if (sample.TraceSizeInBytes == 0)
        {
            Console.Error.WriteLine($"tps: sampling failed: {sample.Exception?.Message}");
            return;
        }

        sample.SaveToDisk(Path.Combine(_dir, "cpu.nettrace"));

        var sb = new StringBuilder();
        foreach (var inclusive in new[] { false, true })
        {
            sb.AppendLine(inclusive ? "== inclusive (call tree cost) ==" : "== exclusive (where the CPU is) ==");
            foreach (var m in sample.TopMethods(count: 250, inclusive: inclusive))
            {
                var pct = inclusive ? m.InclusiveMetricPercent : m.ExclusiveMetricPercent;
                sb.AppendLine($"{pct,6:0.00}%  {m.Name}");
            }
            sb.AppendLine();
        }

        File.WriteAllText(Path.Combine(_dir, "cpu-report.txt"), sb.ToString());
        Console.Error.WriteLine($"tps: cpu profile ({sample.TraceSizeInBytes / 1024} KB) written to {_dir}");
    }

    private async Task CaptureJitAsync(int pid, TimeSpan upper)
    {
        // JIT (0x10) + Loader (0x8) + NGen (0x20, which carries the ReadyToRun method loads) + TieredCompilation
        // (0x8000000000): enough to tell a method that was compiled at start-up from one re-compiled once hot.
        var trace = await _introspector.CollectTraceAsync(pid, new TraceCollectionOptions
        {
            Duration         = upper,
            Providers        = new[] { "Microsoft-Windows-DotNETRuntime:0x8000000038:5" },
            Rundown          = false,
            RequestStackwalk = false,
            OutputPath       = Path.Combine(_dir, "jit.nettrace"),
        }, _stop.Token);
        Console.Error.WriteLine($"tps: jit trace ({trace.TraceSizeInBytes / 1024} KB) written to {_dir}");
    }

    private async Task CaptureAllocationsAsync(int pid, TimeSpan upper, bool stacks)
    {
        var path   = Path.Combine(_dir, "alloc.nettrace");
        var report = await _introspector.CollectAllocationReportAsync(pid, upper, count: 80, outputPath: path, resolveCallStacks: stacks, cancellationToken: _stop.Token);

        using var w = new StreamWriter(Path.Combine(_dir, "alloc-report.txt"));
        AllocationTracing.Write(w, report);
        if (stacks) AllocationTracing.WriteCallStacks(w, report, maxFrames: 14);
        Console.Error.WriteLine($"tps: allocation profile written to {_dir}");
    }

    public void Dispose() => _stop.Dispose();
}
#endif
