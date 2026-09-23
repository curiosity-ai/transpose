namespace Transpose.Compiler.Library;

/// <summary>One package a restore accounted for — installed by this run, or already present.</summary>
public sealed class RestoredPackageInfo
{
    internal RestoredPackageInfo(string id, string version, string folder, bool downloaded, string? source)
    {
        Id         = id;
        Version    = version;
        Folder     = folder;
        Downloaded = downloaded;
        Source     = source;
    }

    public string Id { get; }

    /// <summary>The version as it is written on disk (NuGet's normalized spelling).</summary>
    public string Version { get; }

    /// <summary>The package's folder in the global-packages folder.</summary>
    public string Folder { get; }

    /// <summary>False when the package was already installed and nothing was fetched.</summary>
    public bool Downloaded { get; }

    /// <summary>The source it came from, or null when it was already installed.</summary>
    public string? Source { get; }

    public override string ToString() => $"{Id} {Version}";
}

/// <summary>What a <see cref="RestoreRequest"/> did.</summary>
public sealed class RestoreResult
{
    internal RestoreResult(bool success, string packagesFolder,
                           IReadOnlyList<string> errors, IReadOnlyList<string> warnings,
                           IReadOnlyList<RestoredPackageInfo> packages, IReadOnlyList<string> output)
    {
        Success        = success;
        PackagesFolder = packagesFolder;
        Errors         = errors;
        Warnings       = warnings;
        Packages       = packages;
        Output         = output;
    }

    /// <summary>True when every package the project declares is installed.</summary>
    public bool Success { get; }

    /// <summary>The folder the packages were installed into.</summary>
    public string PackagesFolder { get; }

    /// <summary>Why the restore failed — a package a project declares that no source could serve, or a
    /// version that cannot be resolved at all.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>What the restore could not do but carried on without: a source it could not reach
    /// while another answered, a transitive package no source had, a <c>PackageReference</c> with no
    /// version. A transitive package that is genuinely needed and missing surfaces afterwards as an
    /// ordinary C# error naming the type, which says more than this can.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Every package the graph reached, whether it was fetched now or already there.</summary>
    public IReadOnlyList<RestoredPackageInfo> Packages { get; }

    /// <summary>The restore's progress output, as <c>tps restore</c> would have printed it.</summary>
    public IReadOnlyList<string> Output { get; }

    /// <summary>How many packages this run actually fetched.</summary>
    public int Downloaded => Packages.Count(p => p.Downloaded);
}
