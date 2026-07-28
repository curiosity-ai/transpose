using System.Net.WebSockets;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Transpose.Compiler;

/// <summary>
/// <c>--watch</c>: rebuilds a site whenever a source file changes — the root project's own sources
/// and every project it references, transitively — and serves the assembled output over a local
/// Kestrel server. The served index.html carries a small inline script (see
/// <see cref="LiveReloadScript"/>) that opens a websocket back to this server and reloads the page
/// once a rebuild completes, so the browser tracks the compiled output without a manual refresh.
///
/// The server and the file watchers are independent of <see cref="Program.RunOnce"/>: this class
/// only decides *when* to rebuild and *how to tell the browser*; the actual compile is the same
/// build a plain <c>tps</c> invocation runs, passed in as <paramref name="build"/> so this file has
/// no dependency on the translator/output pipeline's internals.
/// </summary>
internal static class WatchMode
{
    /// <summary>How long to wait after the last file-system event before rebuilding — coalesces the
    /// burst of Created/Changed/Renamed events a single save produces (editors commonly write a temp
    /// file and rename it, or write-then-touch), so one save triggers exactly one rebuild.</summary>
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(300);

    public static int Run(string rootCsproj, int port, Func<string?, BuildRunResult> build)
    {
        // Every successful build gets a version number, embedded in that build's own index.html.
        // A client compares its embedded version against the server's current one when it (re)connects
        // (see ReloadHub.HandleAsync) and catches up immediately if they differ — without this, a
        // client whose reload navigation is still in flight when a second rebuild lands could miss
        // that rebuild's broadcast entirely (its socket isn't open yet to receive it) and be stuck
        // showing stale content until another edit happens to trigger a further broadcast.
        var version = 0L;
        var hub = new ReloadHub();

        var initial = build(LiveReloadScript(port, version));
        if (initial.OutDir is null)
        {
            // A failure on the very first build leaves nothing to serve, and RunOnce already printed
            // why. A non-site project (package/bundle/runtime) is rejected earlier in Main, so
            // reaching here with a null OutDir on exit code 0 should not happen — guarded anyway.
            if (initial.ExitCode == 0)
                MsBuildDiagnostic.WriteError(MsBuildDiagnostic.CodeWatchRequiresSiteBuild,
                    "--watch requires a site build, but this build produced nothing to serve.");
            else
                Console.Error.WriteLine("\ntps: --watch could not complete an initial build; fix the error above and re-run.");
            return initial.ExitCode == 0 ? 1 : initial.ExitCode;
        }

        WebApplication app;
        try
        {
            app = StartServer(initial.OutDir, port, hub);
        }
        catch (Exception ex)
        {
            MsBuildDiagnostic.WriteError(MsBuildDiagnostic.CodeWatchServerFailed,
                $"Could not start the watch server on port {port}: {ex.Message}");
            return 1;
        }

        var watchDirs = WatchDirectories(rootCsproj);

        var rebuilding = false;
        var rebuildQueued = false;
        var rebuildGate = new object();

        void Rebuild()
        {
            lock (rebuildGate)
            {
                if (rebuilding) { rebuildQueued = true; return; }
                rebuilding = true;
            }
            try
            {
                Console.WriteLine("\ntps: change detected, rebuilding...");
                var nextVersion = Interlocked.Increment(ref version);
                BuildRunResult result;
                try
                {
                    result = build(LiveReloadScript(port, nextVersion));
                }
                catch (Exception ex)
                {
                    // build() (RunOnce) already turns a compile failure into a clean exit code without
                    // ever reaching OutputBuilder, so the site on disk is untouched for that, the common,
                    // case. This catch is for the uncommon one — a genuine exception from the write phase
                    // itself (a locked file, a full disk, a directory raced out from under it) — which
                    // would otherwise be unhandled on this Timer callback's thread pool thread and take
                    // the whole watch server down with it. Treat it exactly like a failed build: report,
                    // keep whatever the previous successful build left on disk, and keep watching.
                    MsBuildDiagnostic.WriteError(MsBuildDiagnostic.CodeInternalError, $"rebuild crashed: {ex.Message}");
                    Console.Error.WriteLine(ex.StackTrace);
                    result = new BuildRunResult(2, null, false);
                }
                if (result.ExitCode == 0)
                {
                    Console.WriteLine("tps: rebuilt — reloading browser");
                    hub.SetVersion(nextVersion);
                    _ = hub.BroadcastReloadAsync();
                }
                else
                {
                    // The failed build never reached OutputBuilder, so the site on disk still embeds
                    // the previous (successful) version's script — roll the counter back to match, or
                    // the next successful build's embedded version would skip ahead of what a client
                    // still on the old page believes is current.
                    Interlocked.Decrement(ref version);
                    Console.Error.WriteLine("tps: rebuild failed — keeping the previous build (see errors above)");
                }
            }
            finally
            {
                bool again;
                lock (rebuildGate) { rebuilding = false; again = rebuildQueued; rebuildQueued = false; }
                if (again) Rebuild();
            }
        }

        using var debouncer = new Debouncer(DebounceDelay, Rebuild);
        var watchers = watchDirs
            .Select(dir => CreateWatcher(dir, initial.OutDir, debouncer))
            .Where(w => w is not null)
            .Cast<FileSystemWatcher>()
            .ToList();

        Console.WriteLine($"\ntps: watching {watchDirs.Count} director{(watchDirs.Count == 1 ? "y" : "ies")} for changes");
        Console.WriteLine($"tps: serving http://localhost:{port}/  (Ctrl+C to stop)");

        var exit = new ManualResetEventSlim();
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; exit.Set(); };
        Console.CancelKeyPress += onCancel;
        try
        {
            exit.Wait();
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
            foreach (var w in watchers) w.Dispose();
            app.StopAsync().GetAwaiter().GetResult();
        }
        return 0;
    }

    /// <summary>Every directory to watch: the root project's, then every project it references
    /// (transitively), in whatever order <see cref="ProjectResolver.ReferencedProjectsInBuildOrder"/>
    /// returns them. A change under any of these can change the compiled output — the root project's
    /// own sources, or a library it depends on — so all of them feed the same rebuild trigger.</summary>
    private static List<string> WatchDirectories(string rootCsproj)
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
    /// (<c>.cs</c>, <c>.csproj</c>, <c>tps*.json</c>), signalling <paramref name="debouncer"/> for
    /// each relevant event. Skips <c>bin/</c>/<c>obj/</c> (build output, never a source) and anything
    /// under <paramref name="outDir"/> itself — without that exclusion the site build's own writes
    /// would retrigger the watcher that is serving it, rebuilding forever.</summary>
    private static FileSystemWatcher? CreateWatcher(string dir, string outDir, Debouncer debouncer)
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
            debouncer.Signal();
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

    private static bool IsRelevant(string path, string projectDir, string fullOutDir)
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
            || (name.StartsWith("tps", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Starts Kestrel serving <paramref name="outDir"/> as static content, plus a websocket
    /// endpoint the live-reload script connects to. Logging is disabled entirely — tps's own
    /// Console.WriteLine progress lines are the only intended console output, and ASP.NET Core's
    /// default per-request logging would otherwise clutter it (and risk a stray "warning:"/"error:"
    /// line that MsBuildDiagnostic's contract requires tps itself to control — see MsBuildDiagnostic).</summary>
    private static WebApplication StartServer(string outDir, int port, ReloadHub hub)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>() });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://localhost:{port}");

        var app = builder.Build();
        app.UseWebSockets();

        app.Map(ReloadHub.Path, async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            var clientVersion = long.TryParse(context.Request.Query["v"], out var v) ? v : 0L;
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await hub.HandleAsync(socket, clientVersion, context.RequestAborted);
        });

        var files = new PhysicalFileProvider(Path.GetFullPath(outDir));
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = files });

        app.StartAsync().GetAwaiter().GetResult();
        return app;
    }

    /// <summary>The inline script injected into index.html: connects to <see cref="ReloadHub.Path"/>,
    /// tagged with the version of the build this exact page came from, and reloads on any message.
    /// Reconnects with a short fixed backoff if the socket drops (e.g. the server is mid-rebuild), so
    /// a page left open survives more than one reload — and the version tag lets the server detect,
    /// right at (re)connect time, a client that is already behind (see <see cref="ReloadHub.HandleAsync"/>).</summary>
    private static string LiveReloadScript(int port, long version) => $$"""
        <script>
        (function tpsLiveReload() {
            var url = "ws://localhost:{{port}}{{ReloadHub.Path}}?v={{version}}";
            function connect() {
                var ws = new WebSocket(url);
                ws.onmessage = function () { location.reload(); };
                ws.onclose = function () { setTimeout(connect, 1000); };
                ws.onerror = function () { ws.close(); };
            }
            connect();
        })();
        </script>
        """;
}

/// <summary>Tracks the currently-connected live-reload websockets and broadcasts a reload notification
/// to all of them after a successful rebuild. Content-free by design — the browser always does a full
/// page reload, so the message itself carries no payload; a client that fails to send (already gone)
/// is dropped rather than failing the broadcast for everyone else.</summary>
internal sealed class ReloadHub
{
    public const string Path = "/__tps-livereload";

    private static readonly byte[] ReloadMessage = System.Text.Encoding.UTF8.GetBytes("reload");

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
                await socket.SendAsync(ReloadMessage, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

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

    public async Task BroadcastReloadAsync()
    {
        List<WebSocket> targets;
        lock (_gate) targets = new List<WebSocket>(_sockets);

        foreach (var socket in targets)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                    await socket.SendAsync(ReloadMessage, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
            }
            catch (WebSocketException) { /* client dropped; it will be removed by HandleAsync's own loop */ }
        }
    }
}

/// <summary>Coalesces a burst of file-system events into a single call to <paramref name="action"/>,
/// fired <paramref name="delay"/> after the last <see cref="Signal"/>. If a signal arrives while
/// <paramref name="action"/> is still running, one more run is queued for when it finishes — so a
/// change made during a rebuild is never silently dropped, but concurrent rebuilds never overlap.</summary>
internal sealed class Debouncer : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly Action _action;
    private readonly object _gate = new();
    private Timer? _timer;

    public Debouncer(TimeSpan delay, Action action)
    {
        _delay = delay;
        _action = action;
    }

    public void Signal()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = new Timer(_ => _action(), null, _delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate) _timer?.Dispose();
    }
}
