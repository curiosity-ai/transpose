using System.Net.WebSockets;

namespace Transpose.Compiler;

/// <summary>
/// The watch-mode engine: decides <em>when</em> to rebuild a project, <em>what kind</em> of rebuild a
/// change needs, and <em>what to tell the browser</em> afterwards. Everything except the HTTP server —
/// so both the <c>tps --watch</c> dev server and a hosting application that already runs its own Kestrel
/// (the Curiosity CLI's <c>serve --watch</c>, via <c>Transpose.Compiler.Library</c>) drive the exact same
/// logic instead of each reimplementing the debounce, the change classification and the reload protocol.
///
/// The host's only obligations are to serve <see cref="BuildOutcome.OutDir"/> as static files and to map
/// a websocket endpoint at <see cref="ReloadHub.Path"/> (under its own base path, if it has one) to
/// <see cref="Hub"/>.
/// </summary>
internal sealed class WatchSession : IDisposable
{
    /// <summary>How long to wait after the last file-system event before acting — coalesces the burst of
    /// Created/Changed/Renamed events a single save produces (editors commonly write a temp file and
    /// rename it, or write-then-touch), so one save triggers exactly one rebuild.</summary>
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(300);

    private readonly string _rootCsproj;
    private readonly Func<string, BuildOutcome> _build;
    private readonly Action<BuildOutcome, bool>? _afterUpdate;
    private readonly BuildLog _log;
    private readonly string _reloadPath;
    private readonly Debouncer _debouncer;
    private readonly List<FileSystemWatcher> _watchers = new();

    private readonly object _gate = new();
    private bool _running;
    private List<string> _pending = new();

    /// <summary>Every successful build gets a version number, embedded in that build's own index.html. A
    /// client compares its embedded version against the server's current one when it (re)connects (see
    /// <see cref="ReloadHub.HandleAsync"/>) and catches up immediately if they differ — without this, a
    /// client whose reload navigation is still in flight when a second rebuild lands could miss that
    /// rebuild's broadcast entirely (its socket isn't open yet to receive it) and be stuck showing stale
    /// content until another edit happens to trigger a further broadcast.</summary>
    private long _version;

    private string? _outDir;
    private IReadOnlyList<OutputBuilder.CssResource> _cssResources = Array.Empty<OutputBuilder.CssResource>();
    private BuildOutcome _lastOutcome;

    /// <param name="rootCsproj">The project being watched; its own directory and every directory of a
    /// project it transitively references are watched.</param>
    /// <param name="build">Runs one build, given the live-reload script to inline into index.html.</param>
    /// <param name="reloadPath">The URL path the host maps to <see cref="Hub"/>, as the browser sees it
    /// — <see cref="ReloadHub.Path"/> prefixed with the host's base path, if any.</param>
    /// <param name="afterUpdate">Runs after each successful update (the flag says whether it was the
    /// CSS-only path) and <em>before</em> the browser is told, so a host that post-processes the site —
    /// rewriting the generated index.html, say — does so on output the page has not loaded yet.</param>
    public WatchSession(string rootCsproj, Func<string, BuildOutcome> build, BuildLog? log = null,
                        string? reloadPath = null, Action<BuildOutcome, bool>? afterUpdate = null)
    {
        _rootCsproj = Path.GetFullPath(rootCsproj);
        _build = build;
        _afterUpdate = afterUpdate;
        _log = log ?? BuildLog.Console;
        _reloadPath = reloadPath ?? ReloadHub.Path;
        _debouncer = new Debouncer(DebounceDelay, OnChanges);
    }

    public ReloadHub Hub { get; } = new();

    /// <summary>The directories being watched — available after <see cref="Start"/>.</summary>
    public IReadOnlyList<string> WatchedDirectories { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// Runs the first build. On success the session remembers where the site went and which stylesheets
    /// it assembled from disk, which is what later lets a CSS-only change skip the compiler. The caller
    /// decides what to do with a failure — there is nothing to serve, so it is not this class's call.
    /// </summary>
    public BuildOutcome Start()
    {
        var outcome = _build(LiveReloadScript(_version));
        if (outcome.Success && outcome.OutDir is not null) Remember(outcome);
        return outcome;
    }

    /// <summary>Starts watching for changes. Call once the host is actually serving the initial build, so
    /// the first rebuild cannot broadcast to a server that is not up yet.</summary>
    public void BeginWatching()
    {
        if (_outDir is null) throw new InvalidOperationException("Start() must succeed before watching begins.");

        WatchedDirectories = WatchDirectories(_rootCsproj);
        foreach (var dir in WatchedDirectories)
        {
            var watcher = CreateWatcher(dir, _outDir);
            if (watcher is not null) _watchers.Add(watcher);
        }
    }

    /// <summary>
    /// The inline script for the *current* build, for a host that generates its index.html itself rather
    /// than letting <see cref="OutputBuilder"/> inline it.
    /// </summary>
    public string CurrentLiveReloadScript() => LiveReloadScript(Interlocked.Read(ref _version));

    private void Remember(BuildOutcome outcome)
    {
        _outDir = outcome.OutDir;
        _cssResources = outcome.CssResources;
        _lastOutcome = outcome;
    }

    /// <summary>
    /// Handles one debounced batch of file-system changes, serialized against itself: a change that
    /// arrives while a rebuild is running is folded into the next batch rather than dropped, and two
    /// rebuilds never overlap (they would write the same output files).
    /// </summary>
    private void OnChanges(IReadOnlyList<string> changed)
    {
        lock (_gate)
        {
            _pending.AddRange(changed);
            if (_running) return;
            _running = true;
        }

        while (true)
        {
            List<string> batch;
            lock (_gate) { batch = _pending; _pending = new List<string>(); }

            try
            {
                Process(batch);
            }
            catch (Exception ex)
            {
                // Process() already turns a compile failure into a clean exit code without ever reaching
                // OutputBuilder, so the site on disk is untouched for that, the common, case. This catch
                // is for the uncommon one — a genuine exception from the write phase itself (a locked
                // file, a full disk, a directory raced out from under it) — which would otherwise be
                // unhandled on this thread-pool thread and take the whole watch server down with it.
                // Report, keep whatever the previous successful build left on disk, and keep watching.
                MsBuildDiagnostic.WriteError(MsBuildDiagnostic.CodeInternalError, $"rebuild crashed: {ex.Message}");
                _log.Error(ex.StackTrace ?? "");
            }

            lock (_gate)
            {
                if (_pending.Count == 0) { _running = false; return; }
            }
        }
    }

    private void Process(IReadOnlyList<string> changed)
    {
        // A change confined to stylesheets this site copies straight from disk cannot change a single byte
        // of the compiled JavaScript or of index.html, so there is nothing for the compiler to do: re-copy
        // exactly the files the site build would have written and let the page swap them in. That turns a
        // CSS tweak from a full recompile of the project closure into a file copy.
        var css = CssOnlyUpdate(changed);
        if (css is not null)
        {
            _log.Info($"\ntps: {Describe(changed)} changed (stylesheets only) — updating CSS without rebuilding");
            OutputBuilder.WriteCssResources(_outDir!, css);
            foreach (var resource in css) _log.Info($"  css:        {resource.OutputRelativePath}");
            // The build version deliberately does NOT move: index.html was not rewritten, so every
            // connected page is still the current build and must not be told it is behind.
            _afterUpdate?.Invoke(_lastOutcome, true);
            _ = Hub.BroadcastAsync(ReloadHub.Message.Css);
            return;
        }

        _log.Info("\ntps: change detected, rebuilding...");
        var nextVersion = Interlocked.Increment(ref _version);
        var outcome = _build(LiveReloadScript(nextVersion));

        if (outcome.Success)
        {
            if (outcome.OutDir is not null) Remember(outcome);
            _log.Info("tps: rebuilt — reloading browser");
            Hub.SetVersion(nextVersion);
            _afterUpdate?.Invoke(outcome, false);
            _ = Hub.BroadcastAsync(ReloadHub.Message.Reload);
        }
        else
        {
            // The failed build never reached OutputBuilder, so the site on disk still embeds the previous
            // (successful) version's script — roll the counter back to match, or the next successful
            // build's embedded version would skip ahead of what a client still on the old page believes
            // is current.
            Interlocked.Decrement(ref _version);
            _log.Error("tps: rebuild failed — keeping the previous build (see errors above)");
        }
    }

    /// <summary>
    /// The stylesheets to re-copy for <paramref name="changed"/>, or null when this batch needs a real
    /// build. A batch qualifies only when <em>every</em> changed path is a source file of a stylesheet the
    /// last successful build already produced — which is exactly the case where the fast path is provably
    /// equivalent to a full one:
    ///
    /// <list type="bullet">
    /// <item>a changed <c>.cs</c>/<c>.csproj</c>/<c>tps.json</c> obviously needs the compiler;</item>
    /// <item>a <em>new</em> stylesheet (one no resource group resolved to last time, or one that a glob
    /// has only now started matching) adds a <c>&lt;link&gt;</c> to index.html, which only the site build
    /// writes;</item>
    /// <item>a deleted stylesheet has to remove that link and prune the stale output.</item>
    /// </list>
    /// </summary>
    private IReadOnlyList<OutputBuilder.CssResource>? CssOnlyUpdate(IReadOnlyList<string> changed)
    {
        if (_outDir is null || changed.Count == 0) return null;

        var known = _cssResources;
        if (known.Count == 0) return null;

        var affected = new List<OutputBuilder.CssResource>();
        foreach (var path in changed.Distinct(OutputBuilder.PathComparer))
        {
            if (!path.EndsWith(".css", StringComparison.OrdinalIgnoreCase)) return null;
            if (!File.Exists(path)) return null;   // deleted or renamed away: index.html has to change

            var matches = known.Where(r => r.SourceFiles.Contains(path, OutputBuilder.PathComparer)).ToList();
            if (matches.Count == 0) return null;   // not (yet) part of the site: a full build decides

            foreach (var match in matches)
                if (!affected.Contains(match)) affected.Add(match);
        }
        return affected;
    }

    private static string Describe(IReadOnlyList<string> changed)
    {
        var names = changed.Distinct(OutputBuilder.PathComparer).Select(Path.GetFileName).ToList();
        return names.Count <= 3
            ? string.Join(", ", names)
            : $"{string.Join(", ", names.Take(3))} and {names.Count - 3} more";
    }

    /// <summary>Every directory to watch: the root project's, then every project it references
    /// (transitively), in whatever order <see cref="ProjectResolver.ReferencedProjectsInBuildOrder"/>
    /// returns them. A change under any of these can change the compiled output — the root project's
    /// own sources, or a library it depends on — so all of them feed the same trigger.</summary>
    public static List<string> WatchDirectories(string rootCsproj)
    {
        var dirs = new List<string> { Path.GetDirectoryName(Path.GetFullPath(rootCsproj))! };
        foreach (var dep in ProjectResolver.ReferencedProjectsInBuildOrder(rootCsproj))
        {
            var dir = Path.GetDirectoryName(dep);
            if (dir is not null) dirs.Add(dir);
        }
        return dirs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Watches one project directory for changes to the files a rebuild cares about
    /// (<c>.cs</c>, <c>.csproj</c>, <c>tps*.json</c>, <c>.css</c>), signalling the debouncer for each
    /// relevant event. Skips <c>bin/</c>/<c>obj/</c> (build output, never a source) and anything under
    /// <paramref name="outDir"/> itself — without that exclusion the site build's own writes would
    /// retrigger the watcher that is serving it, rebuilding forever.</summary>
    private FileSystemWatcher? CreateWatcher(string dir, string outDir)
    {
        if (!Directory.Exists(dir)) return null;
        var fullOutDir = Path.GetFullPath(outDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var watcher = new FileSystemWatcher(dir)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
        };

        void OnEvent(object sender, FileSystemEventArgs e)
        {
            if (!IsRelevant(e.FullPath, dir, fullOutDir)) return;
            _debouncer.Signal(Path.GetFullPath(e.FullPath));
        }

        watcher.Changed += OnEvent;
        watcher.Created += OnEvent;
        watcher.Deleted += OnEvent;
        watcher.Renamed += (s, e) => OnEvent(s, e);
        watcher.Error += (_, e) =>
            MsBuildDiagnostic.WriteWarning(MsBuildDiagnostic.CodeWatchServerFailed,
                $"File watcher for '{dir}' failed: {e.GetException().Message}");
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    internal static bool IsRelevant(string path, string projectDir, string fullOutDir)
    {
        var full = Path.GetFullPath(path);
        // Never watch the site's own output — every rebuild writes there, which would otherwise
        // retrigger itself. Comparing full paths (not just a prefix string) avoids a false match on a
        // sibling directory that merely shares the outDir's name as a prefix (e.g. "bin-tools").
        if (full.Equals(fullOutDir, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(fullOutDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return false;

        var rel = Path.GetRelativePath(projectDir, full).Replace('\\', '/');
        if (rel.StartsWith("bin/", StringComparison.Ordinal) || rel.Contains("/bin/", StringComparison.Ordinal)
            || rel.StartsWith("obj/", StringComparison.Ordinal) || rel.Contains("/obj/", StringComparison.Ordinal))
            return false;

        var name = Path.GetFileName(full);
        if (name.StartsWith('.') || name.EndsWith('~')) return false; // editor swap/backup files

        return name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            // Stylesheets: a tps.json `resources` group copies these into the site, so editing one
            // changes what the browser loads even though no C# moved (see CssOnlyUpdate).
            || name.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
            || (name.StartsWith("tps", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The inline script injected into index.html: connects to the reload endpoint, tagged with
    /// the version of the build this exact page came from, and either swaps the page's stylesheets (a
    /// CSS-only update) or reloads (anything else). Reconnects with a short fixed backoff if the socket
    /// drops (e.g. the server is mid-rebuild), so a page left open survives more than one reload — and
    /// the version tag lets the server detect, right at (re)connect time, a client that is already
    /// behind (see <see cref="ReloadHub.HandleAsync"/>).
    ///
    /// The websocket URL is derived from the page's own <c>location</c> rather than baked in, so the
    /// script works unchanged behind a base path and over https (<c>wss:</c>) — a hosting application's
    /// dev server, not just tps's own <c>http://localhost:port/</c>.</summary>
    private string LiveReloadScript(long version) => $$"""
        <script>
        (function tpsLiveReload() {
            var url = (location.protocol === "https:" ? "wss://" : "ws://") + location.host + "{{_reloadPath}}?v={{version}}";
            function swapStylesheets() {
                var links = document.querySelectorAll('link[rel="stylesheet"]');
                for (var i = 0; i < links.length; i++) {
                    var href = links[i].getAttribute("href");
                    if (href) links[i].setAttribute("href", href.split("?")[0] + "?tps=" + Date.now());
                }
            }
            function connect() {
                var ws = new WebSocket(url);
                ws.onmessage = function (e) {
                    if (e.data === "{{ReloadHub.Message.Css}}") swapStylesheets();
                    else location.reload();
                };
                ws.onclose = function () { setTimeout(connect, 1000); };
                ws.onerror = function () { ws.close(); };
            }
            connect();
        })();
        </script>
        """;

    public void Dispose()
    {
        _debouncer.Dispose();
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }
}

/// <summary>Tracks the currently-connected live-reload websockets and tells all of them what happened
/// after a successful build. The payload is one of <see cref="Message"/>: a full page reload, or — for a
/// change that only touched stylesheets — a request to re-fetch them in place, which keeps the running
/// app's state. A client that fails to send (already gone) is dropped rather than failing the broadcast
/// for everyone else.</summary>
internal sealed class ReloadHub
{
    public const string Path = "/__tps-livereload";

    /// <summary>What the server tells a connected page. Plain strings, because the client side of this
    /// protocol is the few lines of JavaScript in <see cref="WatchSession"/>'s injected script.</summary>
    public static class Message
    {
        /// <summary>The build changed; reload the page.</summary>
        public const string Reload = "reload";

        /// <summary>Only stylesheets changed; re-fetch them without reloading.</summary>
        public const string Css = "css";
    }

    private readonly List<WebSocket> _sockets = new();
    private readonly object _gate = new();
    private long _version;

    /// <summary>The version of the most recent successful build. Set once that build's outputs (and
    /// the index.html embedding that same version) have actually been written to disk.</summary>
    public void SetVersion(long version) => Interlocked.Exchange(ref _version, version);

    public async Task HandleAsync(WebSocket socket, long clientVersion, CancellationToken cancellationToken)
    {
        lock (_gate) _sockets.Add(socket);
        try
        {
            // The page this socket was opened from was generated by build `clientVersion`. If a newer
            // build has already completed — e.g. two edits landed back to back while this client was
            // navigating during a previous reload, so it missed that build's broadcast entirely — catch
            // it up right away instead of leaving it stuck on stale content until the next edit.
            if (Interlocked.Read(ref _version) > clientVersion && socket.State == WebSocketState.Open)
                await SendAsync(socket, Message.Reload, cancellationToken);

            var buffer = new byte[16];
            // The client never sends anything meaningful; this just blocks until it disconnects
            // (browser navigation/close), which is what a receive on a closed socket reports.
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch (OperationCanceledException) { /* server shutting down */ }
        catch (WebSocketException) { /* client dropped mid-read */ }
        finally
        {
            lock (_gate) _sockets.Remove(socket);
        }
    }

    public async Task BroadcastAsync(string message)
    {
        List<WebSocket> targets;
        lock (_gate) targets = new List<WebSocket>(_sockets);

        foreach (var socket in targets)
        {
            try
            {
                if (socket.State == WebSocketState.Open) await SendAsync(socket, message, CancellationToken.None);
            }
            catch (WebSocketException) { /* client dropped; it will be removed by HandleAsync's own loop */ }
        }
    }

    private static Task SendAsync(WebSocket socket, string message, CancellationToken cancellationToken)
        => socket.SendAsync(System.Text.Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text,
                            endOfMessage: true, cancellationToken);
}

/// <summary>Coalesces a burst of file-system events into a single call to <c>action</c>, fired
/// <c>delay</c> after the last <see cref="Signal"/>, passing every path signalled since the previous
/// call. The paths matter: they are what lets the watcher tell a stylesheet edit (no compile needed)
/// from a source edit.</summary>
internal sealed class Debouncer : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly Action<IReadOnlyList<string>> _action;
    private readonly object _gate = new();
    private readonly List<string> _paths = new();
    private Timer? _timer;

    public Debouncer(TimeSpan delay, Action<IReadOnlyList<string>> action)
    {
        _delay = delay;
        _action = action;
    }

    public void Signal(string path)
    {
        lock (_gate)
        {
            _paths.Add(path);
            _timer?.Dispose();
            _timer = new Timer(_ => Fire(), null, _delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        List<string> batch;
        lock (_gate)
        {
            batch = new List<string>(_paths);
            _paths.Clear();
        }
        _action(batch);
    }

    public void Dispose()
    {
        lock (_gate) _timer?.Dispose();
    }
}
