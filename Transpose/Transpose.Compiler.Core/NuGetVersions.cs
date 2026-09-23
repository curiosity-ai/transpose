namespace Transpose.Compiler;

/// <summary>
/// The two pieces of NuGet version handling a restore and the reference resolver have to agree on:
/// what a version string is called on disk, and which version a dependency's range asks for.
///
/// <para>They have to agree because they are two halves of one operation — <see cref="PackageRestore"/>
/// installs the graph and <see cref="ProjectResolver"/> reads it back — and a disagreement is silent:
/// the package is on disk, the resolver looks for it under a name nothing wrote, and the build fails
/// with "type or namespace not found" for a package that restored perfectly.</para>
/// </summary>
internal static class NuGetVersions
{
    /// <summary>
    /// A version as NuGet writes it in the global-packages folder: lower-cased, build metadata
    /// dropped, and the numeric part reduced to three segments (a fourth is kept only when non-zero).
    /// So <c>1.0</c> installs as <c>1.0.0</c> and <c>2.1.0.0</c> as <c>2.1.0</c>, which is why a
    /// lookup by the spelling a csproj happens to use has to try this one too.
    /// </summary>
    public static string Normalize(string version)
    {
        var text = version.Trim();
        if (text.Length == 0) return text;

        var plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];                  // build metadata is not part of the identity

        var dash       = text.IndexOf('-');
        var numeric    = dash >= 0 ? text[..dash] : text;
        var prerelease = dash >= 0 ? text[dash..] : "";

        var parts = numeric.Split('.');
        var n     = new int[4];

        for (var i = 0; i < 4 && i < parts.Length; i++)
            if (!int.TryParse(parts[i], out n[i])) return text.ToLowerInvariant(); // not a number — leave it alone

        var normalized = n[3] != 0
            ? $"{n[0]}.{n[1]}.{n[2]}.{n[3]}"
            : $"{n[0]}.{n[1]}.{n[2]}";

        return (normalized + prerelease).ToLowerInvariant();
    }

    /// <summary>
    /// The version a dependency's range resolves to: NuGet takes the range's lowest satisfying
    /// version, which for every shape that appears in a nuspec is the lower bound — <c>1.2.3</c>
    /// ("at least"), <c>[1.2.3]</c> (exactly) and <c>[1.2.3, 2.0)</c> alike.
    /// </summary>
    /// <remarks>
    /// An exclusive lower bound (<c>(1.2.3, )</c>) has no lowest satisfying version that can be named
    /// without asking a feed which ones exist, and no package in a Transpose graph writes one, so it is
    /// read as the bound itself — the same reading <see cref="ProjectResolver"/> has always used.
    /// Returns null for a range with no lower bound at all (<c>(, 2.0)</c>).
    /// </remarks>
    public static string? LowestSatisfying(string? range)
    {
        if (string.IsNullOrWhiteSpace(range)) return null;

        var text = range!.Trim();
        if (text.Length == 0) return null;

        if (text[0] is '[' or '(') text = text[1..];
        if (text.Length > 0 && text[^1] is ']' or ')') text = text[..^1];

        var lower = text.Split(',')[0].Trim();

        return lower.Length == 0 ? null : lower;
    }

    /// <summary>True when the version is floating (<c>*</c>, <c>1.2.*</c>). Nothing in this compiler
    /// resolves one — the reference resolver looks a package up by the exact version the project
    /// writes down — so a restore says so rather than installing something the build will not find.
    /// </summary>
    public static bool IsFloating(string version) => version.Contains('*');
}
