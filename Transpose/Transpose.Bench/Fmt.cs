namespace Transpose.Bench;

/// <summary>Small formatting helpers shared by the report writers.</summary>
internal static class Fmt
{
    public static string Gb(long bytes) => $"{bytes / (1024.0 * 1024 * 1024):N1} GB";
    public static string Mb(long bytes) => $"{bytes / (1024.0 * 1024):N1} MB";
    public static string Mb(double bytes) => $"{bytes / (1024.0 * 1024):N1} MB";

    /// <summary>Median of a sample. Used instead of the mean for the headline number: a single
    /// scheduling hiccup in one iteration should not move the reported figure.</summary>
    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    public static double StdDev(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0;
        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
    }
}
