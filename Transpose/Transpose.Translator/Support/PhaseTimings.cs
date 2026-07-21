using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Transpose.Translator;

/// <summary>
/// Lightweight, process-wide phase timer for the compiler. Off by default (near-zero cost); the
/// CLI turns it on with <c>--timing</c> (or the <c>TRANSPOSE_TIMING</c> env var). When enabled,
/// <see cref="Measure{T}"/> records the wall-clock time of each named phase so the compiler can
/// print a breakdown of where a build spends its time. Phases with the same name accumulate, so a
/// per-file loop reports one total per phase rather than one line per file.
/// </summary>
public static class PhaseTimings
{
    private static readonly object _gate = new();
    private static readonly List<string> _order = new();
    private static readonly Dictionary<string, (long ms, int count)> _phases = new();

    /// <summary>When false, <see cref="Measure{T}"/> and <see cref="Record"/> are no-ops
    /// beyond running the action itself.</summary>
    public static bool Enabled { get; set; }
        = string.Equals(Environment.GetEnvironmentVariable("TRANSPOSE_TIMING"), "1", StringComparison.Ordinal);

    /// <summary>Runs <paramref name="action"/>, and — when enabled — attributes its elapsed time to
    /// the named phase (accumulating across calls with the same name).</summary>
    public static T Measure<T>(string phase, Func<T> action)
    {
        if (!Enabled) return action();
        var sw = Stopwatch.StartNew();
        try { return action(); }
        finally { sw.Stop(); Record(phase, sw.ElapsedMilliseconds); }
    }

    /// <inheritdoc cref="Measure{T}"/>
    public static void Measure(string phase, Action action) =>
        Measure<object?>(phase, () => { action(); return null; });

    /// <summary>Adds <paramref name="ms"/> to the named phase's running total.</summary>
    public static void Record(string phase, long ms)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            if (_phases.TryGetValue(phase, out var cur))
                _phases[phase] = (cur.ms + ms, cur.count + 1);
            else { _phases[phase] = (ms, 1); _order.Add(phase); }
        }
    }

    /// <summary>The recorded phases in first-seen order: name, total milliseconds, and call count.</summary>
    public static IReadOnlyList<(string phase, long ms, int count)> Snapshot()
    {
        lock (_gate)
        {
            var list = new List<(string, long, int)>(_order.Count);
            foreach (var p in _order) { var v = _phases[p]; list.Add((p, v.ms, v.count)); }
            return list;
        }
    }

    /// <summary>Clears all recorded phases (used between independent builds in one process).</summary>
    public static void Reset()
    {
        lock (_gate) { _order.Clear(); _phases.Clear(); }
    }
}
