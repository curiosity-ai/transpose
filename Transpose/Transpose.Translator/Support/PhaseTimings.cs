using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Transpose.Translator;

/// <summary>
/// Lightweight, process-wide phase timer for the compiler. Off by default (near-zero cost); the
/// CLI turns it on with <c>--timing</c> (or the <c>TRANSPOSE_TIMING</c> env var). When enabled,
/// <see cref="Measure{T}"/> records the wall-clock time of each named phase so the compiler can
/// print a breakdown of where a build spends its time. Phases with the same name accumulate, so a
/// per-file loop reports one total per phase rather than one line per file.
///
/// A phase also records how many bytes the CLR allocated while it ran
/// (<see cref="GC.GetTotalAllocatedBytes(bool)"/> — process-wide, so a phase that fans out over
/// worker threads still has their allocations attributed to it). Allocation is the compiler's
/// dominant scaling cost, and knowing *which* phase produces the garbage is what makes it
/// actionable. Nested or concurrent phases would double-count those bytes, so only the outermost
/// running scope is charged; an inner one records time alone.
/// </summary>
public static class PhaseTimings
{
    private static readonly object _gate = new();
    private static readonly List<string> _order = new();
    private static readonly Dictionary<string, Entry> _phases = new();

    /// <summary>Number of <see cref="Measure{T}"/> scopes currently running. Allocation deltas are
    /// attributed only by the scope that sees a transition from 0, so nested/overlapping phases do
    /// not charge the same bytes twice.</summary>
    private static int _depth;

    private struct Entry
    {
        public long Ms;
        public long Bytes;
        public int Count;
    }

    /// <summary>When false, <see cref="Measure{T}"/> and <see cref="Record"/> are no-ops
    /// beyond running the action itself.</summary>
    public static bool Enabled { get; set; }
        = string.Equals(Environment.GetEnvironmentVariable("TRANSPOSE_TIMING"), "1", StringComparison.Ordinal);

    /// <summary>Runs <paramref name="action"/>, and — when enabled — attributes its elapsed time
    /// (plus, for an outermost scope, the bytes allocated while it ran) to the named phase,
    /// accumulating across calls with the same name.</summary>
    public static T Measure<T>(string phase, Func<T> action)
    {
        if (!Enabled) return action();
        var outermost = Interlocked.Increment(ref _depth) == 1;
        var bytes0 = outermost ? GC.GetTotalAllocatedBytes(precise: false) : 0;
        var sw = Stopwatch.StartNew();
        try { return action(); }
        finally
        {
            sw.Stop();
            var bytes = outermost ? GC.GetTotalAllocatedBytes(precise: false) - bytes0 : 0;
            Interlocked.Decrement(ref _depth);
            Record(phase, sw.ElapsedMilliseconds, bytes);
        }
    }

    /// <inheritdoc cref="Measure{T}"/>
    public static void Measure(string phase, Action action) =>
        Measure<object?>(phase, () => { action(); return null; });

    /// <summary>Adds <paramref name="ms"/> (and <paramref name="bytes"/> allocated) to the named
    /// phase's running total.</summary>
    public static void Record(string phase, long ms, long bytes = 0)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            if (_phases.TryGetValue(phase, out var cur))
                _phases[phase] = new Entry { Ms = cur.Ms + ms, Bytes = cur.Bytes + bytes, Count = cur.Count + 1 };
            else { _phases[phase] = new Entry { Ms = ms, Bytes = bytes, Count = 1 }; _order.Add(phase); }
        }
    }

    /// <summary>The recorded phases in first-seen order: name, total milliseconds, bytes allocated
    /// while the phase ran, and call count.</summary>
    public static IReadOnlyList<(string phase, long ms, long bytes, int count)> Snapshot()
    {
        lock (_gate)
        {
            var list = new List<(string, long, long, int)>(_order.Count);
            foreach (var p in _order) { var v = _phases[p]; list.Add((p, v.Ms, v.Bytes, v.Count)); }
            return list;
        }
    }

    /// <summary>Clears all recorded phases (used between independent builds in one process).</summary>
    public static void Reset()
    {
        lock (_gate) { _order.Clear(); _phases.Clear(); _depth = 0; }
    }
}
