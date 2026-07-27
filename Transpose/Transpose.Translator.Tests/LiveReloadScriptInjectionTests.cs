using Microsoft.CodeAnalysis.CSharp;
using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Covers <c>OutputBuilder.Build</c>'s <c>liveReloadScript</c> parameter (used by <c>--watch</c>,
/// see WatchMode.cs): a normal build passes <c>null</c> and must produce byte-for-byte the same
/// index.html it always did, while watch mode's inline reconnect-and-reload script is appended
/// right before <c>&lt;/body&gt;</c> when one is supplied. The end-to-end websocket/rebuild
/// behaviour itself is covered by WatchModeTests (a real server + browser); this only checks the
/// HTML generation is wired correctly and does not regress an ordinary build.
/// </summary>
[TestClass]
public sealed class LiveReloadScriptInjectionTests
{
    private string _dir = "";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tps-livereload-html-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static ResolvedProject MakeProject(string dir) => new()
    {
        CsprojPath = Path.Combine(dir, "App.csproj"),
        ProjectDir = dir,
        AssemblyName = "App",
        TargetFramework = "netstandard2.0",
        Sources = new List<(string, string)>(),
        ReferencePaths = new List<string>(),
        DefineConstants = new List<string>(),
        LanguageVersion = LanguageVersion.Latest,
        ProjectDirs = new List<string> { dir },
    };

    [TestMethod]
    public void OrdinaryBuildInjectsNoLiveReloadScript()
    {
        var project = MakeProject(_dir);
        var config = new TransposeJson { FileName = "app.js" };

        OutputBuilder.Build(project, config, "console.log('hi');", _dir, "Debug", liveReloadScript: null);

        var html = File.ReadAllText(Path.Combine(_dir, "index.html"));
        Assert.IsFalse(html.Contains("tps-livereload"), "a normal build must not carry the watch-mode script");
    }

    [TestMethod]
    public void WatchBuildInjectsTheLiveReloadScriptBeforeClosingBody()
    {
        var project = MakeProject(_dir);
        var config = new TransposeJson { FileName = "app.js" };
        const string script = "<script>/* tps-livereload marker */</script>";

        OutputBuilder.Build(project, config, "console.log('hi');", _dir, "Debug", liveReloadScript: script);

        var html = File.ReadAllText(Path.Combine(_dir, "index.html"));
        Assert.IsTrue(html.Contains(script), "the live-reload script must be present in the generated HTML");

        var bodyClose = html.IndexOf("</body>", StringComparison.Ordinal);
        var scriptIndex = html.IndexOf(script, StringComparison.Ordinal);
        Assert.IsTrue(scriptIndex >= 0 && scriptIndex < bodyClose, "the script must be injected before </body>");
    }

    [TestMethod]
    public void HtmlDisabledSkipsGenerationEvenInWatchMode()
    {
        var project = MakeProject(_dir);
        var config = new TransposeJson { FileName = "app.js", HtmlDisabled = true };

        OutputBuilder.Build(project, config, "console.log('hi');", _dir, "Debug", liveReloadScript: "<script>x</script>");

        Assert.IsFalse(File.Exists(Path.Combine(_dir, "index.html")), "html.disabled must still suppress index.html under --watch");
    }
}
