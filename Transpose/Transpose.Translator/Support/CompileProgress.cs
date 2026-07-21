using System;

namespace Transpose.Translator;

/// <summary>
/// A minimal progress channel from the translator to its host (the <c>tps</c> CLI). The compiler
/// spends most of a build inside a few long phases (binding, the unsupported-feature scan, and JS
/// emit), during which it would otherwise print nothing; <see cref="Report"/> lets those phases
/// surface visible progress. The host installs a <see cref="Sink"/> to display the messages; when
/// none is set (tests, library use) reporting is a no-op.
/// </summary>
public static class CompileProgress
{
    /// <summary>Receives progress messages. Set by the host; null disables reporting.</summary>
    public static Action<string>? Sink { get; set; }

    /// <summary>Reports a phase or step message (e.g. "emitting JavaScript: 128/262 types").</summary>
    public static void Report(string message) => Sink?.Invoke(message);

    /// <summary>Reports incremental progress through <paramref name="total"/> items, but only at a
    /// coarse cadence — at most ~<paramref name="steps"/> messages total plus the final one — so a
    /// large item count does not flood the output. Call once per item with the 1-based
    /// <paramref name="done"/> count.</summary>
    public static void ReportStep(string label, int done, int total, int steps = 10)
    {
        if (Sink is null || total <= 0) return;
        // Emit on every `stride`-th item and on the last item, so the user sees steady movement
        // without a line per item.
        var stride = total <= steps ? 1 : total / steps;
        if (done == total || done % stride == 0)
            Report($"{label}: {done}/{total}");
    }
}
