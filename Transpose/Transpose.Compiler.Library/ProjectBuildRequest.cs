namespace Transpose.Compiler.Library;

/// <summary>
/// A request to build a real, on-disk Transpose project — the library form of a <c>tps --project …</c>
/// invocation, for an application that wants to run the compiler in-process rather than shell out to the
/// <c>tps</c> tool. Where <see cref="CompilationRequest"/> compiles source held in memory,
/// this resolves the <c>.csproj</c>, chains its project references, and writes the same outputs the CLI
/// writes (a runnable site, or the project's package DLL).
///
/// <code>
/// var result = TransposeCompilerLibrary.BuildProject(
///     new ProjectBuildRequest("/src/App/App.csproj") { Configuration = "Debug", Incremental = true });
///
/// if (result.Success) Console.WriteLine($"site at {result.SiteDirectory}");
/// else foreach (var error in result.Errors) Console.Error.WriteLine(error);
/// </code>
///
/// The defaults match the CLI's: the output shape is inferred from the project (a non-packable project
/// with a <c>tps.json</c> assembles a site, anything else produces its package DLL), and the incremental
/// cache is off unless asked for.
/// </summary>
public sealed class ProjectBuildRequest
{
    /// <summary>The project to build. A directory is accepted too, as long as it holds exactly one
    /// <c>.csproj</c> — the same rule <c>tps</c> applies to its project argument.</summary>
    public string ProjectPath { get; }

    public ProjectBuildRequest(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            throw new ArgumentException("A project path must be provided.", nameof(projectPath));
        ProjectPath = projectPath;
    }

    /// <summary>Build configuration — <c>Debug</c> (the default) or <c>Release</c>. Release selects the
    /// minified resource variants and emits a full-IL assembly.</summary>
    public string Configuration { get; set; } = "Debug";

    /// <summary>Where a site build writes its output. Null uses the directory the project's
    /// <c>tps.json</c> <c>output</c> resolves to — normally <c>bin/&lt;config&gt;/netstandard2.0/tps/</c>.</summary>
    public string? SiteDirectory { get; set; }

    /// <summary>Reuse the previous build of this project where its inputs are unchanged. Off by default,
    /// matching the CLI; a dev-loop host (a watch server) wants it on.</summary>
    public bool Incremental { get; set; }

    /// <summary>Where the incremental cache lives. Null puts it in the project's <c>obj/</c>.</summary>
    public string? CacheDirectory { get; set; }

    /// <summary>The <c>AssemblyVersion</c> baked into the bundle via <c>Transpose.assemblyVersion(...)</c>
    /// and into the emitted assembly. Null reads it from the project.</summary>
    public string? AssemblyVersion { get; set; }

    /// <summary>Reference assemblies that are not resolvable from the NuGet cache (the CLI's
    /// <c>--reference</c>).</summary>
    public IList<string> ExtraReferences { get; } = new List<string>();

    /// <summary>Extra preprocessor symbols for the build (the CLI's <c>--define</c>).</summary>
    public IList<string> ExtraDefines { get; } = new List<string>();

    /// <summary>Cap on how many individual errors <see cref="ProjectBuildResult.Errors"/> carries; 0 —
    /// the default — reports every one.</summary>
    public int MaxErrors { get; set; }

    /// <summary>Suppress warnings from the reported output.</summary>
    public bool Quiet { get; set; }

    /// <summary>
    /// A script inlined into the generated index.html immediately before <c>&lt;/body&gt;</c>. This is how a
    /// watch host injects its live-reload client; leave it null (the default) and the generated HTML is
    /// exactly what an ordinary build produces. <see cref="TransposeWatcher"/> fills this in for you.
    /// </summary>
    public string? InjectedHtmlScript { get; set; }

    /// <summary>Where the build's progress output goes — the lines <c>tps</c> would print to its console.
    /// Null discards them; the diagnostics are returned on the result either way.</summary>
    public Action<string>? OnProgress { get; set; }
}
