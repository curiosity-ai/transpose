using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Transpose.Compiler;

/// <summary>
/// <c>--watch</c>: serves the assembled site over a local Kestrel server and keeps it in step with the
/// sources. Everything about <em>when</em> to rebuild, <em>what kind</em> of update a change needs and
/// <em>what to tell the browser</em> lives in <see cref="WatchSession"/> (Transpose.Compiler.Core), which
/// the <c>Transpose.Compiler.Library</c> package exposes so a hosting application can run the same watch
/// loop behind its own web server. This file is only the dev server: static files plus the websocket
/// endpoint the injected live-reload script connects to.
/// </summary>
internal static class WatchMode
{
    public static int Run(string rootCsproj, int port, Func<string, BuildOutcome> build)
    {
        using var session = new WatchSession(rootCsproj, build);

        var initial = session.Start();
        if (initial.OutDir is null)
        {
            // A failure on the very first build leaves nothing to serve, and the build already printed
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
            app = StartServer(initial.OutDir, port, session.Hub);
        }
        catch (Exception ex)
        {
            MsBuildDiagnostic.WriteError(MsBuildDiagnostic.CodeWatchServerFailed,
                $"Could not start the watch server on port {port}: {ex.Message}");
            return 1;
        }

        session.BeginWatching();

        var dirs = session.WatchedDirectories.Count;
        Console.WriteLine($"\ntps: watching {dirs} director{(dirs == 1 ? "y" : "ies")} for changes");
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
            app.StopAsync().GetAwaiter().GetResult();
        }
        return 0;
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
}
