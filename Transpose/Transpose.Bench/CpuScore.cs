using System.Diagnostics;
using System.Text;

namespace Transpose.Bench;

/// <summary>
/// A short, deterministic CPU + memory benchmark whose only purpose is to make compiler timings
/// comparable across machines. The compiler's cost is dominated by four things — pointer-chasing
/// through Roslyn's symbol graph, hash-table lookups on string keys, allocating and copying short
/// strings, and single-threaded scalar work — so the score is built from micro-workloads that
/// mirror exactly those, not from a generic FLOPS number. A machine that scores 2× here really does
/// compile roughly 2× faster, which is what lets a normalised timing be compared to another run.
///
/// Every workload is deterministic (a fixed-seed xorshift, never <see cref="Random"/> or the clock)
/// so the score is stable run to run, and each is calibrated against a reference measurement so a
/// score of 100 means "as fast as the reference machine". The total budget is ~2 s.
/// </summary>
internal static class CpuScore
{
    /// <summary>One micro-workload's result: how long it took, and the score it earns
    /// (reference ÷ measured × 100, so higher is faster).</summary>
    internal readonly record struct Workload(string Name, string Measures, double Ms, double ReferenceMs)
    {
        public double Score => ReferenceMs / Ms * 100.0;
    }

    /// <summary>
    /// The reference machine's per-workload milliseconds. Calibrated on a 4-core / 4-thread
    /// Intel Xeon @ 2.80 GHz x64 Linux container (AVX-512 capable, 16 GB, .NET 10, workstation GC),
    /// so that box scores ~100 and every other machine's score is its speed relative to it.
    ///
    /// These are deliberately constants: a self-calibrating baseline would always report 100 and
    /// destroy the only thing the score is for — comparing a timing taken on one machine with a
    /// timing taken on another. Re-calibrate (and say so in TODO.optimization.md) only if the
    /// workloads themselves change, never to "re-centre" a new machine.
    ///
    /// Caveat worth knowing when reading a report: <c>pointer-chase</c> is pure dependent-load
    /// memory latency and is the one workload a virtualised or noisy host can perturb by ~1.5×
    /// even with best-of-5. Its weight in the geometric mean is 1/6, so that shows up as roughly
    /// ±7% on the score; the other five are stable to about ±3%.
    /// </summary>
    private static readonly (string name, string measures, double ms)[] Reference =
    {
        ("pointer-chase",  "memory latency (Roslyn symbol-graph walks)",   85.0),
        ("dictionary",     "hashing + probing (symbol/name tables)",       68.0),
        ("string-churn",   "allocation + GC throughput (JS emit)",         62.0),
        ("scalar-int",     "scalar ALU + branch prediction",               69.0),
        ("sort",           "cache-friendly compare/swap (type ordering)",  74.0),
        ("memcpy",         "memory bandwidth (StringBuilder growth)",      62.0),
    };

    internal sealed record Result(double Score, IReadOnlyList<Workload> Workloads, double TotalMs)
    {
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"CPU benchmark  →  score {Score:F1}  (100 = reference machine; higher is faster)");
            foreach (var w in Workloads)
                sb.AppendLine($"  {w.Name,-14} {w.Ms,7:F1} ms  (ref {w.ReferenceMs,5:F0} ms)  score {w.Score,6:F1}   {w.Measures}");
            sb.AppendLine($"  {"total",-14} {TotalMs,7:F1} ms");
            return sb.ToString();
        }
    }

    // Workload sizes. Fixed (never time-boxed) so every machine performs exactly the same amount of
    // work — that is what makes the resulting score comparable at all. Chosen so each workload runs
    // for roughly 100 ms on the reference machine, keeping the whole benchmark near 2 s.
    private const int ChaseSlots = 1 << 22;         // 4M slots = 16 MB, past L2/L3 on most CPUs
    private const int ChaseSteps = 1_000_000;
    private const int DictOps = 900_000;
    private const int StringOps = 1_200_000;
    private const int ScalarIters = 12_000_000;
    private const int SortItems = 900_000;
    /// <summary>Repetitions per workload; the fastest one is kept.</summary>
    private const int Repetitions = 5;

    private const int CopyBytes = 1 << 21;          // 2 MB
    private const int CopyReps = 420;

    public static Result Run()
    {
        // Warm the JIT so the first measured workload is not paying for tier-0 code.
        PointerChase(BuildChaseTable(1 << 14), 100_000);
        DictionaryChurn(20_000);
        StringChurn(20_000);
        ScalarInt(2_000_000);
        SortWorkload(BuildRandomInts(20_000));
        MemCopy(new byte[1 << 16], new byte[1 << 16], 20);

        var total = Stopwatch.StartNew();
        var results = new List<Workload>();

        // Input data is built outside the timed region: a workload should measure the operation it
        // is named for, not the cost of generating its input.
        var chaseTable = BuildChaseTable(ChaseSlots);
        results.Add(Measure(0, () => PointerChase(chaseTable, ChaseSteps)));
        results.Add(Measure(1, () => DictionaryChurn(DictOps)));
        results.Add(Measure(2, () => StringChurn(StringOps)));
        results.Add(Measure(3, () => ScalarInt(ScalarIters)));
        var sortSource = BuildRandomInts(SortItems);
        results.Add(Measure(4, () => SortWorkload(sortSource)));
        var copySrc = new byte[CopyBytes];
        var copyDst = new byte[CopyBytes];
        for (var i = 0; i < CopyBytes; i++) copySrc[i] = (byte)i;
        results.Add(Measure(5, () => MemCopy(copySrc, copyDst, CopyReps)));

        total.Stop();

        // Geometric mean: one pathological workload (e.g. a machine with an odd cache hierarchy)
        // then moves the score proportionally instead of dominating it, as an arithmetic mean would.
        var logSum = 0.0;
        foreach (var r in results) logSum += Math.Log(r.Score);
        var score = Math.Exp(logSum / results.Count);

        return new Result(score, results, total.Elapsed.TotalMilliseconds);
    }

    private static Workload Measure(int index, Func<long> body)
    {
        var (name, measures, refMs) = Reference[index];
        // Best-of-N: the minimum is the least noisy estimator of a machine's real capability
        // (interference from another process only ever makes a run slower). Five repetitions rather
        // than three because on a shared/virtualised host the memory-latency workload in particular
        // can be perturbed by a factor of two, and that noise would otherwise leak into the score.
        var best = double.MaxValue;
        long sink = 0;
        for (var i = 0; i < Repetitions; i++)
        {
            var sw = Stopwatch.StartNew();
            sink += body();
            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }
        Consume(sink);
        return new Workload(name, measures, Math.Max(best, 0.001), refMs);
    }

    /// <summary>Keeps a computed value observably live so the JIT cannot delete the workload.</summary>
    private static long _sink;
    private static void Consume(long v) => System.Threading.Volatile.Write(ref _sink, v);

    /// <summary>Deterministic xorshift64* — the same sequence on every machine and architecture.</summary>
    private static ulong Next(ref ulong s)
    {
        s ^= s >> 12; s ^= s << 25; s ^= s >> 27;
        return s * 0x2545F4914F6CDD1DUL;
    }

    /// <summary>A random permutation of [0, slots): following it produces a dependent-load chain.</summary>
    private static int[] BuildChaseTable(int slots)
    {
        var next = new int[slots];
        for (var i = 0; i < slots; i++) next[i] = i;
        // Fisher–Yates with the fixed-seed PRNG → the same permutation on every machine.
        var s = 0x9E3779B97F4A7C15UL;
        for (var i = slots - 1; i > 0; i--)
        {
            var j = (int)(Next(ref s) % (ulong)(i + 1));
            (next[i], next[j]) = (next[j], next[i]);
        }
        return next;
    }

    /// <summary>Dependent-load chase through a permutation of a large array: each read's address
    /// depends on the previous read, so the loop cannot be prefetched and measures raw memory
    /// latency — the single best proxy for walking Roslyn's symbol and syntax graphs.</summary>
    private static long PointerChase(int[] next, int steps)
    {
        var p = 0;
        for (var i = 0; i < steps; i++) p = next[p];
        return p;
    }

    /// <summary>String-keyed dictionary insert + lookup churn: hashing, probing and equality on
    /// short strings, which is what Roslyn's name lookups and the emitter's mangling caches do
    /// constantly.</summary>
    private static long DictionaryChurn(int n)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        var keys = new string[Math.Min(n, 4096)];
        for (var i = 0; i < keys.Length; i++) keys[i] = "Tesserae.Components.Symbol_" + i;
        for (var i = 0; i < n; i++)
        {
            var k = keys[i % keys.Length];
            d[k] = i;
        }
        long hits = 0;
        var s = 0x243F6A8885A308D3UL;
        for (var i = 0; i < n; i++)
        {
            var k = keys[(int)(Next(ref s) % (ulong)keys.Length)];
            if (d.TryGetValue(k, out var v)) hits += v & 1;
        }
        return hits;
    }

    /// <summary>Short-string allocation, concatenation and StringBuilder growth — a direct stand-in
    /// for the JS emitter, whose cost is almost entirely producing small strings and appending
    /// them. Exercises the allocator and gen0 GC throughput.</summary>
    private static long StringChurn(int n)
    {
        var sb = new StringBuilder(1 << 16);
        long len = 0;
        for (var i = 0; i < n; i++)
        {
            var name = "Tesserae$" + i.ToString() + "$" + (i & 31).ToString();
            sb.Append(name);
            sb.Append(" = function (a, b) { return a + b; };\n");
            if (sb.Length > 1 << 18) { len += sb.Length; sb.Clear(); }
        }
        return len + sb.Length;
    }

    /// <summary>Scalar integer arithmetic with an unpredictable branch: measures ALU throughput and
    /// branch-prediction quality, which govern the tight syntax-walk loops.</summary>
    private static long ScalarInt(int iterations)
    {
        var s = 0xDEADBEEFCAFEBABEUL;
        long acc = 0;
        for (var i = 0; i < iterations; i++)
        {
            var v = Next(ref s);
            // A genuinely unpredictable branch (50/50 on a PRNG bit).
            if ((v & 1) != 0) acc += (long)(v >> 33);
            else acc -= (long)(v & 0xFFFF);
        }
        return acc;
    }

    private static int[] BuildRandomInts(int n)
    {
        var a = new int[n];
        var s = 0x13198A2E03707344UL;
        for (var i = 0; i < n; i++) a[i] = (int)(Next(ref s) & 0x7FFFFFFF);
        return a;
    }

    /// <summary>Sorting a large int array: compare/swap over a cache-resident-then-spilling working
    /// set, mirroring the emitter's dependency-depth ordering of types. The source array is copied
    /// each rep so every repetition sorts the same unsorted input (sorting an already-sorted array
    /// is a different, much cheaper operation).</summary>
    private static long SortWorkload(int[] source)
    {
        var a = new int[source.Length];
        Array.Copy(source, a, source.Length);
        Array.Sort(a);
        return a[0] + a[a.Length / 2] + a[^1];
    }

    /// <summary>Bulk block copy: measures sustained memory bandwidth, which is what bounds
    /// StringBuilder growth and writing multi-megabyte JS bundles.</summary>
    private static long MemCopy(byte[] src, byte[] dst, int reps)
    {
        long sum = 0;
        for (var r = 0; r < reps; r++)
        {
            Buffer.BlockCopy(src, 0, dst, 0, src.Length);
            sum += dst[r % src.Length];
        }
        return sum;
    }
}
