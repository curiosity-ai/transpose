using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Playwright;

namespace Transpose.Translator.Tests;

/// <summary>
/// End-to-end coverage of <c>tps --watch</c>: a real <c>tps</c> process compiles a small fixture (a
/// root site project plus a project it references), serves it over its Kestrel dev server, and a
/// real headless Chromium (via Playwright) loads the page and is left running while the fixture's
/// source is edited on disk. The assertions never call <c>page.ReloadAsync()</c> themselves — the
/// whole point is that the page reloads on its own, driven by the websocket script <c>WatchMode</c>
/// injects into index.html, once a rebuild completes.
///
/// Both watched inputs are exercised: editing the root project's own source, and editing the
/// separate project it references (<c>ProjectReference</c>) — the "referenced projects being
/// imported" half of watch mode that a root-only glob would miss.
/// </summary>
[TestClass]
public sealed class WatchModeTests
{
    private static string RepoRoot = "";
    private static string TpsDllPath = "";
    private static string TransposeDllPath = "";
    private static IPlaywright? PlaywrightDriver;
    private static IBrowser? Browser;

    private string _root = "";
    private Process? _watchProcess;
    private readonly List<string> _log = new();

    [ClassInitialize]
    public static void ClassSetup(TestContext _)
    {
        RepoRoot = FindRepoRoot();

        // The test project references Transpose.Compiler (ProjectReference), so its build output —
        // including tps.dll, the AssemblyName of Transpose.Compiler — is copied right next to this
        // test assembly; no separate build/publish step is needed to get a runnable tps.
        TpsDllPath = Path.Combine(AppContext.BaseDirectory, "tps.dll");
        Assert.IsTrue(File.Exists(TpsDllPath), $"tps.dll was not found next to the test binary at {TpsDllPath}");

        // A site build always needs the Transpose.dll runtime (the sole BCL reference every
        // compilation gets). Build it once, self-contained, exactly like
        // transpose-debugging/scripts/setup-toolkit.sh does, if a previous run hasn't already.
        TransposeDllPath = Path.Combine(RepoRoot, "BCL", "Transpose.BCL", "bin", "Debug", "netstandard2.0", "Transpose.dll");
        if (!File.Exists(TransposeDllPath))
        {
            var bclCsproj = Path.Combine(RepoRoot, "BCL", "Transpose.BCL", "Transpose.BCL.csproj");
            var psi = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add(TpsDllPath);
            psi.ArgumentList.Add(bclCsproj);
            psi.ArgumentList.Add("--build-runtime");
            using var proc = Process.Start(psi)!;
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            Assert.AreEqual(0, proc.ExitCode, $"Failed to build the Transpose runtime before running watch-mode tests.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
            Assert.IsTrue(File.Exists(TransposeDllPath), "Transpose.dll still missing after --build-runtime");
        }

        PlaywrightDriver = Microsoft.Playwright.Playwright.CreateAsync().GetAwaiter().GetResult();
        Browser = PlaywrightDriver.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }).GetAwaiter().GetResult();
    }

    [ClassCleanup]
    public static void ClassTeardown()
    {
        try { Browser?.CloseAsync().GetAwaiter().GetResult(); } catch { /* best-effort */ }
        PlaywrightDriver?.Dispose();
    }

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "tps-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { _watchProcess?.Kill(entireProcessTree: true); } catch { /* already gone */ }
        _watchProcess?.Dispose();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    private string Write(string rel, string content)
    {
        var full = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task EditingRootOrReferencedProjectAutoReloadsTheBrowser()
    {
        // Lib: the referenced project. App: the root site project (ProjectReference to Lib, a
        // tps.json, and no DOM bindings package — it renders by replacing the whole page body via
        // Transpose's [Script] escape hatch, which needs nothing beyond the base runtime).
        Write("Lib/Lib.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <AssemblyName>Lib</AssemblyName>
              </PropertyGroup>
            </Project>
            """);
        Write("Lib/Greeter.cs", """
            namespace Lib
            {
                public static class Greeter
                {
                    public const string Message = "v1";
                }
            }
            """);

        Write("App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <AssemblyName>App</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../Lib/Lib.csproj" />
              </ItemGroup>
            </Project>
            """);
        Write("App/tps.json", """{ "fileName": "app.js" }""");
        var programPath = Write("App/Program.cs", """
            using Transpose;
            using Lib;

            public class Program
            {
                private const string Suffix = "-r1";

                [Script("document.body.innerHTML = html;")]
                public static extern void SetBody(string html);

                public static void Main()
                {
                    SetBody(Greeter.Message + Suffix);
                }
            }
            """);
        var greeterPath = Path.Combine(_root, "Lib", "Greeter.cs");
        var appCsproj = Path.Combine(_root, "App", "App.csproj");
        var siteDir = Path.Combine(_root, "site");

        var port = GetFreePort();
        _watchProcess = StartWatch(appCsproj, siteDir, port);
        await WaitForLogAsync(msg => msg.Contains("tps: serving http://localhost:"), TimeSpan.FromSeconds(60));

        var page = await Browser!.NewPageAsync();
        try
        {
            await page.GotoAsync($"http://localhost:{port}/", new PageGotoOptions { WaitUntil = WaitUntilState.Load });
            await AssertBodyEventuallyContainsAsync(page, "v1-r1", "the initial build");

            // Edit the REFERENCED project (Lib) — proves watch mode follows ProjectReferences, not
            // just the root project's own glob.
            File.WriteAllText(greeterPath, File.ReadAllText(greeterPath).Replace("v1", "v2"));
            await AssertBodyEventuallyContainsAsync(page, "v2-r1", "after editing the referenced project (Lib)");

            // Edit the ROOT project's own source.
            File.WriteAllText(programPath, File.ReadAllText(programPath).Replace("-r1", "-r2"));
            await AssertBodyEventuallyContainsAsync(page, "v2-r2", "after editing the root project (App)");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>Waits for the page's own auto-reload (never calls ReloadAsync) to bring the body's
    /// content in line with a rebuild — the reload is driven entirely by the websocket message
    /// WatchMode's ReloadHub broadcasts once the rebuild it triggered completes.</summary>
    private async Task AssertBodyEventuallyContainsAsync(IPage page, string expected, string because)
    {
        try
        {
            await page.WaitForFunctionAsync(
                "expected => document.body.innerHTML.includes(expected)", expected,
                new PageWaitForFunctionOptions { Timeout = 30_000 });
        }
        catch (TimeoutException)
        {
            var actual = await page.EvaluateAsync<string>("() => document.body.innerHTML");
            Assert.Fail($"Timed out waiting for the browser to auto-reload with '{expected}' ({because}). "
                + $"Last body content: '{actual}'.\n--- tps --watch log ---\n{string.Join('\n', SnapshotLog())}");
        }

        var text = await page.EvaluateAsync<string>("() => document.body.innerHTML");
        Assert.IsTrue(text.Contains(expected), $"expected '{expected}' in body ({because}), got '{text}'");
    }

    private Process StartWatch(string appCsproj, string siteDir, int port)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(appCsproj)!,
        };
        psi.ArgumentList.Add(TpsDllPath);
        psi.ArgumentList.Add(appCsproj);
        psi.ArgumentList.Add("--watch");
        psi.ArgumentList.Add("--watch-port");
        psi.ArgumentList.Add(port.ToString());
        psi.ArgumentList.Add("--site-dir");
        psi.ArgumentList.Add(siteDir);
        // The fixture has no PackageReference that would resolve Transpose.dll into
        // ResolvedProject.ReferencePaths (a real project gets one from its Transpose.BCL/Transpose.Core
        // package references) — without it, OutputBuilder never finds the runtime assembly to extract
        // tps.js/tps.shim.js from, and the page would load with no Transpose runtime at all. --reference
        // is exactly the escape hatch for an assembly outside the NuGet cache (see --reference's help
        // text), so use it to add the runtime the same way a real project's package reference would.
        psi.ArgumentList.Add("--reference");
        psi.ArgumentList.Add(TransposeDllPath);
        psi.Environment["TRANSPOSE_DLL_PATH"] = TransposeDllPath;

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (_log) _log.Add(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (_log) _log.Add("[stderr] " + e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private List<string> SnapshotLog() { lock (_log) return new List<string>(_log); }

    private async Task WaitForLogAsync(Func<string, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (SnapshotLog().Any(predicate)) return;
            if (_watchProcess!.HasExited)
                Assert.Fail("tps --watch exited before becoming ready.\n" + string.Join('\n', SnapshotLog()));
            await Task.Delay(100);
        }
        Assert.Fail("Timed out waiting for tps --watch to report readiness.\n" + string.Join('\n', SnapshotLog()));
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Transpose.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Could not locate the repo root (Transpose.slnx) above " + AppContext.BaseDirectory);
    }
}
