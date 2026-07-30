using System.Net.WebSockets;

namespace Transpose.Compiler.Library;

/// <summary>
/// Watch mode as a library: rebuilds a Transpose project whenever its sources change — the project's own
/// files and those of every project it transitively references — and tells connected browsers to update.
/// This is the same engine behind <c>tps --watch</c>; the difference is that the HTTP server is yours.
///
/// A host has three obligations:
/// <list type="number">
/// <item>call <see cref="Start"/> and serve the resulting <see cref="ProjectBuildResult.SiteDirectory"/>
/// as static files;</item>
/// <item>accept websockets at <see cref="ReloadEndpointPath"/> (prefixed with the host's own base path,
/// which it must then pass as <c>reloadEndpointPath</c>) and hand each one to
/// <see cref="HandleWebSocketAsync"/>;</item>
/// <item>call <see cref="BeginWatching"/> once the server is actually serving.</item>
/// </list>
///
/// The live-reload client is injected into the generated index.html automatically — the watcher supplies
/// it to each build — so nothing else is needed to make the page track the compiler. Two kinds of update
/// are distinguished: an ordinary rebuild reloads the page, while a change confined to stylesheets the
/// site copies from disk skips the compiler entirely and swaps the page's <c>&lt;link&gt;</c> hrefs in
/// place, leaving the running application's state alone.
///
/// <code>
/// var watcher = new TransposeWatcher(new ProjectBuildRequest(csproj) { Incremental = true });
/// var initial = watcher.Start();
/// if (!initial.Success) return 1;
/// // ... start a web server on initial.SiteDirectory, mapping TransposeWatcher.ReloadEndpointPath ...
/// watcher.BeginWatching();
/// </code>
/// </summary>
public sealed class TransposeWatcher : IDisposable
{
    /// <summary>The URL path a host must map to <see cref="HandleWebSocketAsync"/>. A host serving under a
    /// base path maps <c>&lt;base&gt; + ReloadEndpointPath</c> and passes that whole path to the
    /// constructor, so the injected client connects to the right place.</summary>
    public const string ReloadEndpointPath = ReloadHub.Path;

    private readonly ProjectBuildRequest _request;
    private readonly WatchSession _session;

    /// <summary>The output of the most recent build, kept so <see cref="Start"/> (and the update callback)
    /// can report it — the watch engine itself only deals in outcomes.</summary>
    private IReadOnlyList<string> _lastOutput = Array.Empty<string>();

    /// <param name="request">How to build the project on every change. Its
    /// <see cref="ProjectBuildRequest.InjectedHtmlScript"/> is set by the watcher and must not be set by
    /// the caller. Setting <see cref="ProjectBuildRequest.Incremental"/> is strongly recommended: a watch
    /// loop rebuilds the same project over and over, which is exactly what the cache is for.</param>
    /// <param name="reloadEndpointPath">The path, as the browser sees it, that the host maps to
    /// <see cref="HandleWebSocketAsync"/>. Defaults to <see cref="ReloadEndpointPath"/>.</param>
    /// <param name="onUpdated">Runs after each successful update and before the browser is told about it;
    /// the flag says whether only stylesheets changed. This is the hook for a host that post-processes the
    /// generated site (rewriting index.html, for instance) — doing it here means the page never loads the
    /// un-processed output.</param>
    public TransposeWatcher(
        ProjectBuildRequest request,
        string? reloadEndpointPath = null,
        Action<ProjectBuildResult, bool>? onUpdated = null)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        if (request.InjectedHtmlScript is not null)
            throw new ArgumentException("InjectedHtmlScript is set by the watcher itself.", nameof(request));

        ProjectPath = TransposeCompilerLibrary.LocateProject(request.ProjectPath);
        _session = new WatchSession(
            ProjectPath,
            Build,
            reloadPath: reloadEndpointPath,
            afterUpdate: onUpdated is null
                ? null
                : (outcome, cssOnly) => onUpdated(new ProjectBuildResult(outcome, _lastOutput), cssOnly));
    }

    /// <summary>The resolved <c>.csproj</c> being watched.</summary>
    public string ProjectPath { get; }

    private BuildOutcome Build(string liveReloadScript)
    {
        var (outcome, output) = TransposeCompilerLibrary.RunProjectBuild(_request, liveReloadScript);
        _lastOutput = output;
        return outcome;
    }

    /// <summary>The project directories being watched — available after <see cref="BeginWatching"/>.</summary>
    public IReadOnlyList<string> WatchedDirectories => _session.WatchedDirectories;

    /// <summary>
    /// Runs the initial build. The host serves <see cref="ProjectBuildResult.SiteDirectory"/> from here on;
    /// a result with no site directory (a failed build, or a project whose shape is a package rather than a
    /// site) means there is nothing to watch and the host should report the errors and stop.
    /// </summary>
    public ProjectBuildResult Start() => new(_session.Start(), _lastOutput);

    /// <summary>Starts watching for changes. Call once the host is serving the initial build, so a rebuild
    /// can never broadcast to a server that is not up yet. Throws if <see cref="Start"/> did not produce a
    /// site.</summary>
    public void BeginWatching() => _session.BeginWatching();

    /// <summary>
    /// Serves one live-reload websocket until the browser disconnects. <paramref name="clientVersion"/> is
    /// the <c>v</c> query-string value the injected client sends: the build the page it is running came
    /// from. A client that reconnects already behind the current build is brought up to date immediately,
    /// which is what keeps a page whose reload overlapped a second rebuild from getting stuck.
    /// </summary>
    public Task HandleWebSocketAsync(WebSocket socket, long clientVersion, CancellationToken cancellationToken = default)
        => _session.Hub.HandleAsync(socket, clientVersion, cancellationToken);

    public void Dispose() => _session.Dispose();
}
