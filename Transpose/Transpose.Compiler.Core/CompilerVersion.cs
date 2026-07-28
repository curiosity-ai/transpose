namespace Transpose.Compiler;

/// <summary>
/// The version of the compiler that is running, and whether it is a version the minimum-version gate
/// (<see cref="BuildStamp"/>) may be enforced against.
///
/// A released <c>tps</c> is stamped by CI with a CalVer version — <c>yy.M.&lt;buildId&gt;</c>, e.g.
/// <c>26.7.1234</c> — passed as <c>/p:Version=</c>, which MSBuild propagates to every project in the
/// build (so this assembly carries it too, not just the <c>tps</c> executable). A build from a
/// developer's tree is passed nothing, and
/// <c>Transpose.Compiler.Core.csproj</c> pins it to <see cref="Unversioned"/> in that case: there is
/// no meaningful version to compare, so <see cref="EnforceMinimum"/> is false and no build is ever
/// failed for being "too old" on a machine where the compiler is being worked on.
///
/// <see cref="EnforceMinimum"/> is additionally off in a Debug-built compiler, which is what the dev
/// tree and <c>bootstrap.sh</c> produce. The two conditions overlap on purpose: a Release build in a
/// dev tree (the translator tests, <c>scripts/setup-toolkit.sh</c>) still has no version, and a
/// hypothetical Debug build on CI still has no business rejecting anything.
/// </summary>
internal static class CompilerVersion
{
    /// <summary>The placeholder a build that was handed no version carries. Written into the assemblies
    /// such a compiler produces as their minimum, where it can never fail a check (every real version
    /// is greater), and recognised here as "unversioned" rather than as version zero.</summary>
    public const string Unversioned = "0.0.0";

    /// <summary>This compiler's version, or null when it was built without one (a dev tree).</summary>
    public static Version? Current { get; } = ReadCurrent();

    /// <summary>This compiler's version as it is written into an assembly's <see cref="BuildStamp"/> —
    /// <see cref="Unversioned"/> for a dev build.</summary>
    public static string Text { get; } = Current?.ToString() ?? Unversioned;

    /// <summary>
    /// Whether a referenced assembly's declared minimum compiler version may fail this build. Only a
    /// versioned Release compiler enforces it; see the type-level remarks for why.
    /// </summary>
    public static bool EnforceMinimum =>
#if DEBUG
        false;
#else
        Current is not null;
#endif

    /// <summary>
    /// Parses a version as it appears in a <see cref="BuildStamp"/> or in an assembly's informational
    /// version: <c>26.7.1234</c>, possibly with a <c>+&lt;sha&gt;</c> build-metadata or
    /// <c>-preview</c> pre-release suffix. Returns null for the <see cref="Unversioned"/> placeholder
    /// and for anything that is not a version at all — both mean "nothing to compare".
    /// </summary>
    public static Version? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var core = text!.Trim();
        var cut = core.IndexOfAny(new[] { '+', '-' });
        if (cut >= 0) core = core.Substring(0, cut);
        if (!Version.TryParse(core, out var version)) return null;
        return Normalize(version) == Normalize(new Version(Unversioned)) ? null : version;
    }

    /// <summary>
    /// A version with all four components present. <see cref="Version"/> compares an unspecified
    /// component as less than zero, so <c>26.7.500</c> would otherwise sort *before*
    /// <c>26.7.500.0</c> — and the two spellings do occur, since a stamp carries the three-part
    /// package version while an assembly's <c>AssemblyVersion</c> is always four-part.
    /// </summary>
    public static Version Normalize(Version version)
        => new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    /// <summary>
    /// This assembly's version, preferring the informational version (which is the NuGet package
    /// version CI stamps, e.g. <c>26.7.1234+&lt;sha&gt;</c>) over the assembly version (which the SDK
    /// derives from it, four-part).
    /// </summary>
    private static Version? ReadCurrent()
    {
        var assembly = typeof(CompilerVersion).Assembly;
        var informational = assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;
        return TryParse(informational) ?? TryParse(assembly.GetName().Version?.ToString());
    }
}
