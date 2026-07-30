using System.Text;

namespace Transpose.Compiler.Library;

/// <summary>
/// Compiles C# source held in memory to JavaScript, in process — the library form of the <c>tps</c>
/// CLI, for a .NET application that wants to translate code without shelling out to a separate
/// process or touching disk. Build a <see cref="CompilationRequest"/> and pass it to
/// <see cref="Compile"/> or <see cref="CompileAsync"/>.
///
/// <code>
/// var result = TransposeCompilerLibrary.Compile(
///     new CompilationRequest("App")
///         .WithSourceFile("Program.cs", "System.Console.WriteLine(\"Hello!\");"));
///
/// if (result.Success) Console.WriteLine(result.Javascript);
/// else foreach (var error in result.Errors) Console.Error.WriteLine(error);
/// </code>
///
/// Compilations run one at a time, process-wide: <c>Transpose.Translator.CompileProgress.Sink</c> and
/// <c>Transpose.Translator.PhaseTimings</c> are process-wide mutable state the translator reports
/// progress/timings through (by design — <c>tps</c> is a fresh, single-build-per-process CLI, so
/// nothing about them needed to be reentrant). Two compilations running at once in the same process
/// would attribute each other's timings and progress to the wrong build, so this service serializes
/// them instead. A single compilation is still fully synchronous/CPU-bound — concurrent *callers*
/// just queue behind whichever one runs first.
/// </summary>
public static class TransposeCompilerLibrary
{
    private static readonly object Gate = new();

    /// <summary>Runs <paramref name="request"/> synchronously, blocking until any other compilation
    /// already in progress (in this process) finishes.</summary>
    public static CompilationResult Compile(CompilationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (Gate)
        {
            return CompileCore(request);
        }
    }

    /// <summary>Runs <paramref name="request"/> on a thread-pool thread, still serialized against any
    /// other compilation in this process (see the type-level remarks). Cancelling before this
    /// request's turn comes up skips it entirely; cancelling once it has started does not interrupt
    /// the (CPU-bound) compile in progress.</summary>
    public static Task<CompilationResult> CompileAsync(CompilationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => Compile(request), cancellationToken);
    }

    /// <summary>
    /// Builds a real, on-disk project — the library form of <c>tps --project …</c>: resolves the csproj,
    /// builds every project it references, translates, and writes the site (or the package DLL). Serialized
    /// against every other compilation in this process, for the reasons in the type-level remarks.
    /// </summary>
    public static ProjectBuildResult BuildProject(ProjectBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (Gate)
        {
            var (outcome, output) = RunProjectBuild(request, request.InjectedHtmlScript);
            return new ProjectBuildResult(outcome, output);
        }
    }

    /// <summary>Runs <paramref name="request"/> on a thread-pool thread, still serialized against any other
    /// compilation in this process.</summary>
    public static Task<ProjectBuildResult> BuildProjectAsync(ProjectBuildRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => BuildProject(request), cancellationToken);
    }

    /// <summary>
    /// The shared build path behind <see cref="BuildProject"/> and <see cref="TransposeWatcher"/>: turns the
    /// request into the CLI's own <see cref="BuildOptions"/>, redirects the build's console output into
    /// <paramref name="output"/> (and the request's progress callback), and runs it.
    ///
    /// Not locked here — <see cref="BuildProject"/> takes the gate, and the watcher's builds are already
    /// serialized by its own single-threaded update loop — but it does install process-wide sinks
    /// (<see cref="MsBuildDiagnostic.Sink"/>, <c>CompileProgress.Sink</c>) for the duration of the build,
    /// which is why nothing else may be compiling at the same time.
    /// </summary>
    internal static (BuildOutcome Outcome, IReadOnlyList<string> Output) RunProjectBuild(
        ProjectBuildRequest request, string? injectedHtmlScript)
    {
        var lines = new List<string>();
        var log = new CollectingLog(lines, request.OnProgress);

        var options = ProjectBuild.ResolveOutputMode(new BuildOptions
        {
            CsprojPath = LocateProject(request.ProjectPath),
            SiteDir = request.SiteDirectory,
            Configuration = request.Configuration,
            Quiet = request.Quiet,
            MaxErrors = request.MaxErrors,
            ExtraReferences = request.ExtraReferences.ToList(),
            ExtraDefines = request.ExtraDefines.ToList(),
            AssemblyVersion = request.AssemblyVersion,
            Incremental = request.Incremental,
            CacheDir = request.CacheDirectory,
            LiveReloadScript = injectedHtmlScript,
        });

        var previousSink = MsBuildDiagnostic.Sink;
        try
        {
            MsBuildDiagnostic.Sink = (line, _) => log.Info(line);
            return (ProjectBuild.Run(options, log), lines);
        }
        finally
        {
            MsBuildDiagnostic.Sink = previousSink;
        }
    }

    /// <summary>Resolves a project argument the way the <c>tps</c> CLI does: a <c>.csproj</c> path as given,
    /// or the single <c>.csproj</c> in a directory.</summary>
    internal static string LocateProject(string projectPath)
    {
        var full = Path.GetFullPath(projectPath);
        if (File.Exists(full) && full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return full;
        if (Directory.Exists(full))
        {
            var found = Directory.GetFiles(full, "*.csproj", SearchOption.TopDirectoryOnly);
            if (found.Length == 1) return Path.GetFullPath(found[0]);
            if (found.Length > 1)
                throw new ArgumentException($"'{full}' holds {found.Length} .csproj files; name the one to build.", nameof(projectPath));
        }
        throw new FileNotFoundException($"No .csproj found at '{projectPath}'.", full);
    }

    /// <summary>Collects the build's output so a failure can be reported in full afterwards, forwarding each
    /// line to the request's progress callback as it happens.</summary>
    private sealed class CollectingLog : BuildLog
    {
        private readonly List<string> _lines;
        private readonly Action<string>? _forward;

        public CollectingLog(List<string> lines, Action<string>? forward)
        {
            _lines = lines;
            _forward = forward;
        }

        public override void Info(string message) => Add(message);
        public override void Error(string message) => Add(message);

        private void Add(string message)
        {
            lock (_lines) _lines.Add(message);
            _forward?.Invoke(message);
        }
    }

    private static CompilationResult CompileCore(CompilationRequest request)
    {
        // The same gate the `tps` CLI applies: a reference built by a newer Transpose than this one
        // cannot be consumed correctly, so it is an error rather than a subtly wrong bundle. Off unless
        // this is a version-stamped Release build — see Transpose.Compiler.CompilerVersion.
        if (BuildStamp.CheckReferences(request.ReferencePaths) is { } outdated)
            return CompilationResult.Failed(new[] { outdated });

        var translator = new RoslynTranslator();
        var result = translator.BuildAssembly(
            request.Sources,
            request.AssemblyName,
            request.ReferencePaths,
            request.DefineConstants,
            request.LanguageVersion,
            request.ReflectionEnabled,
            request.MetadataTarget,
            emitAssembly: request.EmitPackageAssembly,
            assemblyVersion: request.AssemblyVersion);

        if (!result.Success) return CompilationResult.Failed(result.Diagnostics);

        byte[]? packageAssemblyBytes = null;
        if (request.EmitPackageAssembly && result.AssemblyBytes is not null)
            packageAssemblyBytes = EmbedPackageResources(request, result);

        var js = request.IncludeRuntime
            ? RoslynTranslator.LoadRuntime() + "\n" + result.Javascript
            : result.Javascript!;

        return CompilationResult.Succeeded(js, result.MetadataJavascript, result.AssemblyBytes, packageAssemblyBytes, result.Diagnostics);
    }

    /// <summary>Embeds the compiled JS (and its separate metadata script, if any) into the emitted
    /// .NET assembly as manifest resources, fully in memory — the same shape
    /// <c>ResourceEmbedder</c>/<c>OutputBuilder</c> produce for a <c>tps --emit-package</c> build, so a
    /// referencing project can extract them exactly as it would from a package built by the CLI.</summary>
    private static byte[] EmbedPackageResources(CompilationRequest request, AssemblyBuildResult result)
    {
        var utf8 = new UTF8Encoding(false);
        var items = new List<EmbeddedItem> { new(request.AssemblyName + ".js", utf8.GetBytes(result.Javascript!), null) };
        if (result.MetadataJavascript is not null)
            items.Add(new EmbeddedItem(request.AssemblyName + ".meta.js", utf8.GetBytes(result.MetadataJavascript), null));

        using var output = new MemoryStream();
        ResourceEmbedder.Embed(output, result.AssemblyBytes!, items, request.ReferencePaths);
        return output.ToArray();
    }
}
