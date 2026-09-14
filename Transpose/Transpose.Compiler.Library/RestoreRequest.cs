namespace Transpose.Compiler.Library;

/// <summary>
/// A request to install the NuGet packages a project binds against — the library form of
/// <c>tps restore …</c>, for a host that compiles Transpose projects in process and would otherwise
/// have to ship the .NET SDK to shell out to <c>dotnet restore</c>.
///
/// <code>
/// var restored = await TransposeCompilerLibrary.RestoreAsync(
///     new RestoreRequest("/src/App/App.csproj") { OnProgress = Console.WriteLine });
///
/// if (!restored.Success) foreach (var error in restored.Errors) Console.Error.WriteLine(error);
/// </code>
///
/// <para>It resolves the package graph exactly the way <see cref="ProjectBuildRequest"/>'s build reads
/// it back, and lays each package out the way NuGet does, so the folder it fills is one a later
/// <c>dotnet restore</c> accepts as already installed. It restores what the *compiler* binds against:
/// building the same project with MSBuild additionally needs the project's SDK package, which only
/// MSBuild resolves. Lock files, package-source mapping, authenticated feeds, floating versions and
/// <c>project.assets.json</c> are out of scope — nothing in the Transpose toolchain reads them.</para>
/// </summary>
public sealed class RestoreRequest
{
    /// <summary>The project to restore, with every project it transitively references. A directory is
    /// accepted too, as long as it holds exactly one <c>.csproj</c>.</summary>
    public string ProjectPath { get; }

    public RestoreRequest(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            throw new ArgumentException("A project path must be provided.", nameof(projectPath));
        ProjectPath = projectPath;
    }

    /// <summary>Where packages are installed. Null takes <c>NUGET_PACKAGES</c>, then the configured
    /// <c>globalPackagesFolder</c>, then <c>~/.nuget/packages</c> — NuGet's own order, and the same one
    /// the build's reference resolution reads.</summary>
    public string? PackagesFolder { get; set; }

    /// <summary>
    /// Sources consulted <b>before</b> the ones the nuget.config chain configures: a folder of
    /// <c>.nupkg</c> files, or a V3 feed's <c>index.json</c>. First so that a host shipping the
    /// packages it was built from answers out of that folder rather than reaching a feed for a version
    /// it already has.
    /// </summary>
    public IList<string> Sources { get; } = new List<string>();

    /// <summary>Consult only <see cref="Sources"/>, ignoring the nuget.config chain. What a host that
    /// must not reach the network wants: an unreachable feed costs a timeout per package.</summary>
    public bool IgnoreConfiguredSources { get; set; }

    /// <summary>Re-download and re-extract packages that are already installed.</summary>
    public bool Force { get; set; }

    /// <summary>How many packages are fetched at once (default 8).</summary>
    public int MaxParallelDownloads { get; set; } = 8;

    /// <summary>How long one package download may take (default 5 minutes).</summary>
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Where the restore's progress output goes — the lines <c>tps restore</c> would print.
    /// Null discards them; the diagnostics are on the result either way.</summary>
    public Action<string>? OnProgress { get; set; }
}
