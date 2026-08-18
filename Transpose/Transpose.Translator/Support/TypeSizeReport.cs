using System;
using System.Collections.Generic;
using System.Text;

namespace Transpose.Translator;

/// <summary>
/// Process-wide accumulator for <em>how many bytes of JavaScript each type contributed</em>. Off by
/// default (near-zero cost); the CLI turns it on with <c>--type-sizes</c> or the
/// <c>TRANSPOSE_TYPE_SIZES</c> environment variable, and prints the result largest-first at the end
/// of the build.
///
/// The number answers a question neither the bundle size nor the chunk report can: a 3 MB payload
/// says nothing about <em>which</em> declarations produced it, and a chunk is a strongly-connected
/// component whose size is the sum of a group. The size recorded here is the exact text of that
/// type's <c>Transpose.define(...)</c> — the same string the chunker measures and the bundle
/// concatenates — so the column sums to the emitted payload minus the prelude and the reflection
/// metadata, and a type that dominates the list really is the code to look at.
///
/// A <b>package</b> build emits every type twice (the single bundle <i>and</i> the module chunks —
/// see <c>RoslynTranslator</c>), and the two texts differ only in indentation. Recording keeps the
/// larger of the two rather than summing them, so one type is one row whatever the output shape.
/// </summary>
public static class TypeSizeReport
{
    private static readonly object _gate = new();
    private static readonly Dictionary<(string assembly, string type), long> _sizes = new();

    /// <summary>When false, <see cref="Record"/> is a no-op. Set from
    /// <c>TRANSPOSE_TYPE_SIZES</c>: <c>1</c>/<c>true</c> reports every type, a positive integer
    /// reports that many.</summary>
    public static bool Enabled { get; set; } = ParseEnabled(Environment.GetEnvironmentVariable("TRANSPOSE_TYPE_SIZES"));

    /// <summary>How many rows the report prints, largest first. 0 = every type. Set from the
    /// numeric form of <c>TRANSPOSE_TYPE_SIZES</c> (e.g. <c>TRANSPOSE_TYPE_SIZES=20</c>).</summary>
    public static int Limit { get; set; } = ParseLimit(Environment.GetEnvironmentVariable("TRANSPOSE_TYPE_SIZES"));

    /// <summary>Where the machine-readable dump goes, from <c>TRANSPOSE_TYPE_SIZES_JSON</c> or
    /// <c>--type-sizes-json</c>. Setting it also enables the report — asking for the file is asking
    /// for the measurement.</summary>
    public static string? JsonPath { get; set; } = Environment.GetEnvironmentVariable("TRANSPOSE_TYPE_SIZES_JSON") is { Length: > 0 } p ? p : null;

    static TypeSizeReport()
    {
        if (JsonPath is not null) Enabled = true;
    }

    private static bool ParseEnabled(string? v)
        => v is "1" or "true" or "TRUE" || (int.TryParse(v, out var n) && n > 0);

    private static int ParseLimit(string? v)
        => int.TryParse(v, out var n) && n > 1 ? n : 0;

    /// <summary>Records the emitted JavaScript of one type. Called once per type per emit pass; the
    /// larger of two passes over the same type wins (see the type remarks).</summary>
    public static void Record(string assemblyName, string typeName, string js)
    {
        if (!Enabled) return;
        var bytes = Encoding.UTF8.GetByteCount(js);
        lock (_gate)
        {
            var key = (assemblyName, typeName);
            if (!_sizes.TryGetValue(key, out var cur) || bytes > cur) _sizes[key] = bytes;
        }
    }

    /// <summary>Every recorded type, largest first, ties broken by name so two builds of the same
    /// sources report the same order.</summary>
    public static IReadOnlyList<(string assembly, string type, long bytes)> Snapshot()
    {
        lock (_gate)
        {
            var list = new List<(string, string, long)>(_sizes.Count);
            foreach (var kv in _sizes) list.Add((kv.Key.assembly, kv.Key.type, kv.Value));
            list.Sort((a, b) => b.Item3 != a.Item3
                ? b.Item3.CompareTo(a.Item3)
                : string.CompareOrdinal(a.Item2, b.Item2));
            return list;
        }
    }

    /// <summary>Clears everything recorded (used between independent builds in one process).</summary>
    public static void Reset()
    {
        lock (_gate) _sizes.Clear();
    }
}
