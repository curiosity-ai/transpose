namespace Transpose.Compiler;

/// <summary>
/// Just enough target-framework reasoning to pick the right <c>&lt;group&gt;</c> out of a nuspec's
/// dependencies — the one thing a restore has to get right that a reference resolver does not. A
/// package declares a different dependency set per framework, and installing the wrong set means
/// either a package missing at compile time or a framework's worth of dependencies this project never
/// binds against.
/// </summary>
/// <remarks>
/// The full compatibility table lives in NuGet.Frameworks, and taking that dependency for one
/// comparison would pull the whole NuGet client stack into a compiler that otherwise reads packages as
/// files. What is needed here is narrow: a Transpose project is <c>netstandard2.0</c> (the SDK forces
/// it), so the question is only which of a package's groups a netstandard consumer may use.
/// </remarks>
internal static class NuGetFrameworks
{
    private enum Family { Unknown, NetStandard, NetCoreApp, NetFramework, Any }

    /// <summary>
    /// The dependency group of <paramref name="groups"/> that <paramref name="targetFramework"/>
    /// should take, or -1 when none applies — the same choice NuGet makes, so what a
    /// <c>tps restore</c> installs matches what a <c>dotnet restore</c> of the same project installed.
    /// </summary>
    /// <param name="groups">Each group's <c>targetFramework</c> attribute, in nuspec order. A null or
    /// empty entry is the group that applies to every framework.</param>
    public static int BestGroup(string targetFramework, IReadOnlyList<string?> groups)
    {
        var consumer = Parse(targetFramework);

        var best      = -1;
        var bestScore = (family: -1, version: -1.0);

        for (var i = 0; i < groups.Count; i++)
        {
            var score = Score(consumer, groups[i]);
            if (score is null) continue;

            // Closest family first (an exact family beats netstandard-by-compatibility, which beats the
            // catch-all group), then the highest version that is still compatible.
            if (score.Value.family > bestScore.family
             || (score.Value.family == bestScore.family && score.Value.version > bestScore.version))
            {
                best      = i;
                bestScore = score.Value;
            }
        }

        return best;
    }

    private static (int family, double version)? Score((Family family, double version) consumer, string? group)
    {
        if (string.IsNullOrWhiteSpace(group)) return (0, 0); // the catch-all group: usable, never preferred

        var (family, version) = Parse(group!);

        if (family == Family.Any)     return (0, 0);
        if (family == Family.Unknown) return null;

        if (family == consumer.family) return version <= consumer.version ? (2, version) : null;

        // A .NET / .NET Core / .NET Framework consumer can take a netstandard group; a netstandard
        // project can take nothing but netstandard.
        if (family == Family.NetStandard && consumer.family is Family.NetCoreApp or Family.NetFramework)
            return version <= MaxNetStandardFor(consumer) ? (1, version) : null;

        return null;
    }

    /// <summary>The highest netstandard a framework implements — the part of NuGet's table that is not
    /// derivable, so it is written down. Anything past the listed versions implements 2.1.</summary>
    private static double MaxNetStandardFor((Family family, double version) consumer) => consumer.family switch
    {
        Family.NetCoreApp   => consumer.version >= 3.0  ? 2.1 : consumer.version >= 2.0 ? 2.0 : 1.6,
        Family.NetFramework => consumer.version >= 4.61 ? 2.0 : consumer.version >= 4.5 ? 1.1 : 1.0,
        _                   => 0,
    };

    // Longest first: "netcoreapp" and "netstandard" both start with "net".
    private static readonly (string prefix, Family family)[] Prefixes =
    {
        (".NETStandard",  Family.NetStandard),
        ("netstandard",   Family.NetStandard),
        (".NETCoreApp",   Family.NetCoreApp),
        ("netcoreapp",    Family.NetCoreApp),
        (".NETFramework", Family.NetFramework),
        ("net",           Family.NetFramework),
    };

    /// <summary>
    /// Reads a target framework written in any of the three spellings a nuspec or csproj uses: the
    /// short folder name (<c>netstandard2.0</c>, <c>net472</c>, <c>net10.0</c>), the long form
    /// (<c>.NETStandard2.0</c>) and the full name (<c>.NETStandard,Version=v2.0</c>).
    /// </summary>
    private static (Family family, double version) Parse(string moniker)
    {
        var text = moniker.Trim();
        if (text.Length == 0 || text.Equals("any", StringComparison.OrdinalIgnoreCase)) return (Family.Any, 0);

        string? digits = null;
        var     family = Family.Unknown;

        var comma = text.IndexOf(',');
        if (comma >= 0)
        {
            // ".NETStandard,Version=v2.0": the identifier and the version, spelled out separately.
            family = FamilyOf(text[..comma].Trim(), out var leftover);
            if (leftover.Length > 0) return (Family.Unknown, 0);

            var v = text.IndexOf('v', comma);
            digits = v >= 0 ? text[(v + 1)..].Trim() : "";
        }
        else
        {
            family = FamilyOf(text, out digits);
        }

        if (family == Family.Unknown) return (Family.Unknown, 0);

        var version = ReadVersion(digits ?? "");

        // net5.0 and up are .NET, not .NET Framework, however the moniker starts.
        if (family == Family.NetFramework && version >= 5) family = Family.NetCoreApp;

        return (family, version);

        static Family FamilyOf(string text, out string rest)
        {
            foreach (var (prefix, family) in Prefixes)
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    rest = text[prefix.Length..];
                    return family;
                }

            rest = text;
            return Family.Unknown;
        }
    }

    /// <summary>
    /// The version after the moniker's name. A dotted one is read as written (<c>2.0</c>,
    /// <c>10.0</c>); a dotless one is the .NET Framework's packed spelling, where <c>48</c> is 4.8 and
    /// <c>472</c> is 4.7.2 — which is why the dots have to survive this far rather than being stripped
    /// with the identifier's, <c>net10.0</c> and <c>net472</c> being otherwise the same three digits.
    /// </summary>
    private static double ReadVersion(string digits)
    {
        digits = digits.Trim();
        if (digits.Length == 0) return 0;

        if (digits.Contains('.'))
        {
            var parts = digits.Split('.');
            if (!int.TryParse(parts[0], out var major)) return 0;
            var minor = parts.Length > 1 && int.TryParse(parts[1], out var m) ? m : 0;
            return major + minor / 10.0;
        }

        if (!int.TryParse(digits, out var n)) return 0;

        return digits.Length switch
        {
            1 => n,             // net5 -> 5, net4 -> 4
            2 => n / 10.0,      // net48 -> 4.8
            _ => n / 100.0,     // net472 -> 4.72
        };
    }
}
