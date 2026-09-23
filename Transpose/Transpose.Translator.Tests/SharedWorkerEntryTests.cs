using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Covers <c>[SharedWorkerEntry]</c>: the scan that finds the marked methods, the diagnostics for a
/// method that cannot be one, and the worker script the site build writes for each.
///
/// <para>
/// A shared worker needs a script URL of its own, with no document behind it, so it can be served
/// neither by index.html nor by the bundle. The build therefore writes a second entry beside the
/// bundle that brings the runtime up in worker scope and calls the marked method — which is what
/// lets the worker be ordinary C# in the ordinary project.
/// </para>
///
/// <para>
/// What a worker does once it is running is exercised in a real browser instead (the Tesserae
/// Playwright specs): Node has no <c>SharedWorker</c>, and the interesting properties — one instance
/// across several pages, fan-out, a page going away — are all properties of the browser's worker
/// model rather than of the emitted text.
/// </para>
/// </summary>
[TestClass]
public sealed class SharedWorkerEntryTests
{
    private string _root = "";
    private string _projectDir = "";
    private string _outputDir = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "tps-worker-" + Guid.NewGuid().ToString("N"));
        _projectDir = Path.Combine(_root, "proj");
        _outputDir = Path.Combine(_root, "site");
        Directory.CreateDirectory(_projectDir);
        Directory.CreateDirectory(_outputDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    // ---------------------------------------------------------------- the scan

    private static AssemblyBuildResult Build(string code) =>
        new RoslynTranslator().BuildAssembly(new[] { ("App.cs", code) },
            CompilationBuilder.DefaultAssemblyName, extraReferencePaths: null);

    private const string Worker = """
using Transpose;

public static class LiveWorker
{
    [SharedWorkerEntry("live")]
    public static void Main() { }
}
""";

    [TestMethod]
    public void AMarkedMethodIsFoundAndResolvedToItsJavaScriptCall()
    {
        var result = Build(Worker);

        Assert.IsTrue(result.Success, Errors(result));
        Assert.AreEqual(1, result.SharedWorkerEntries.Count);
        Assert.AreEqual("live", result.SharedWorkerEntries[0].Name);
        Assert.AreEqual("LiveWorker.Main()", result.SharedWorkerEntries[0].Call,
            "the output builder only writes the call down, so it is resolved here");
    }

    [TestMethod]
    public void AProjectWithNoMarkedMethodCarriesNoEntries()
    {
        var result = Build("public static class A { public static void Main() { } }");

        Assert.IsTrue(result.Success, Errors(result));
        Assert.AreEqual(0, result.SharedWorkerEntries.Count,
            "a project that declares none must be completely unaffected");
    }

    [TestMethod]
    public void EntriesComeBackInADeterministicOrder()
    {
        // GetMembers / GetTypeMembers order is not contractual, and emitted output has to be
        // reproducible, so the scan sorts.
        var result = Build("""
using Transpose;

public static class W
{
    [SharedWorkerEntry("zebra")] public static void C() { }
    [SharedWorkerEntry("apple")] public static void A() { }
    [SharedWorkerEntry("mango")] public static void B() { }
}
""");

        Assert.IsTrue(result.Success, Errors(result));
        CollectionAssert.AreEqual(
            new[] { "apple", "mango", "zebra" },
            result.SharedWorkerEntries.Select(e => e.Name).ToArray());
    }

    [TestMethod]
    public void AMarkedMethodOnANestedTypeResolvesThroughItsContainer()
    {
        var result = Build("""
using Transpose;

public static class Outer
{
    public static class Inner
    {
        [SharedWorkerEntry("live")]
        public static void Go() { }
    }
}
""");

        Assert.IsTrue(result.Success, Errors(result));
        Assert.AreEqual("Outer.Inner.Go()", result.SharedWorkerEntries[0].Call);
    }

    // ---------------------------------------------------------------- diagnostics

    [DataTestMethod]
    [DataRow("public void M() { }",                     "it is not static",     DisplayName = "instance method")]
    [DataRow("public static void M(int x) { }",         "it takes parameters",  DisplayName = "takes parameters")]
    [DataRow("public static int M() { return 0; }",     "it returns a value",   DisplayName = "returns a value")]
    [DataRow("public static void M<T>() { }",           "it is generic",        DisplayName = "generic")]
    public void AMethodThatCannotBeAnEntryPointIsAnError(string member, string reason)
    {
        var result = Build($$"""
using Transpose;

public class W
{
    [SharedWorkerEntry("live")]
    {{member}}
}
""");

        Assert.IsFalse(result.Success, "a malformed entry has to fail the build, not emit a script that cannot start");
        var diag = result.Errors.FirstOrDefault(d => d.Id == "TransposeR0004");
        Assert.IsNotNull(diag, "expected TransposeR0004\n" + Errors(result));
        StringAssert.Contains(diag!.GetMessage(), reason, "the message must say which rule was broken");
    }

    [TestMethod]
    public void AnEmptyNameIsAnError()
    {
        var result = Build("""
using Transpose;

public static class W
{
    [SharedWorkerEntry("")]
    public static void M() { }
}
""");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Errors.Any(d => d.Id == "TransposeR0004"), Errors(result));
    }

    [TestMethod]
    public void ANameThatIsNotAPlainFileNameIsAnError()
    {
        // The name becomes a file beside the bundle; it must not be able to reach out of the site.
        var result = Build("""
using Transpose;

public static class W
{
    [SharedWorkerEntry("../escape")]
    public static void M() { }
}
""");

        Assert.IsFalse(result.Success);
        var diag = result.Errors.FirstOrDefault(d => d.Id == "TransposeR0004");
        Assert.IsNotNull(diag, Errors(result));
        StringAssert.Contains(diag!.GetMessage(), "not a plain file name");
    }

    [TestMethod]
    public void TwoEntriesSharingANameIsAnError()
    {
        // A shared worker is identified by its name, so two would each emit the same script over the
        // other and a page asking for that name would get whichever won.
        var result = Build("""
using Transpose;

public static class W
{
    [SharedWorkerEntry("live")] public static void A() { }
    [SharedWorkerEntry("LIVE")] public static void B() { }
}
""");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Errors.Any(d => d.Id == "TransposeR0005"),
            "a name collision must be reported, case-insensitively (it is a file name)\n" + Errors(result));
    }

    // ---------------------------------------------------------------- the emitted script

    private ResolvedProject Project() => new()
    {
        CsprojPath = Path.Combine(_projectDir, "App.csproj"),
        ProjectDir = _projectDir,
        AssemblyName = "App",
        TargetFramework = "netstandard2.0",
        Sources = new List<(string, string)>(),
        ReferencePaths = new List<string>(),
        DefineConstants = new List<string>(),
        LanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest,
        ProjectDirs = new List<string> { _projectDir },
    };

    private TransposeJson Config(string tpsJson = "{}")
    {
        File.WriteAllText(Path.Combine(_projectDir, "tps.json"), tpsJson);
        var config = TransposeJson.TryLoad(_projectDir, "Debug");
        Assert.IsNotNull(config);
        return config!;
    }

    private string BuildSite(string configuration, params SharedWorkerEntry[] entries)
    {
        OutputBuilder.Build(Project(), Config(), "var app = 1;", _outputDir, configuration,
            sharedWorkerEntries: entries);

        var path = Path.Combine(_outputDir, "live.worker.js");
        Assert.IsTrue(File.Exists(path), "the build must write one worker script per entry");
        return File.ReadAllText(path);
    }

    [TestMethod]
    public void TheSiteBuildWritesAWorkerScriptThatStartsTheEntry()
    {
        var js = BuildSite("Debug", new SharedWorkerEntry("live", "LiveWorker.Main()"));

        StringAssert.Contains(js, "importScripts(", "a classic worker pulls its code in with importScripts");
        StringAssert.Contains(js, "\"./app.js\"", "the bundle the site produced has to be among them");
        StringAssert.Contains(js, "Transpose.init();",
            "the types have to be registered and static initializers run before the entry is called");
        StringAssert.Contains(js, "LiveWorker.Main();", "and then the entry itself");

        Assert.IsTrue(js.IndexOf("importScripts(") < js.IndexOf("Transpose.init();"),
            "the runtime cannot be initialized before it is loaded\n" + js);
        Assert.IsTrue(js.IndexOf("Transpose.init();") < js.IndexOf("LiveWorker.Main();"),
            "the entry runs last -- it installs an onconnect handler and returns\n" + js);
    }

    [TestMethod]
    public void OneScriptIsWrittenPerEntry()
    {
        OutputBuilder.Build(Project(), Config(), "var app = 1;", _outputDir, "Debug",
            sharedWorkerEntries: new[]
            {
                new SharedWorkerEntry("live",  "LiveWorker.Main()"),
                new SharedWorkerEntry("index", "Indexer.Main()"),
            });

        Assert.IsTrue(File.Exists(Path.Combine(_outputDir, "live.worker.js")));
        Assert.IsTrue(File.Exists(Path.Combine(_outputDir, "index.worker.js")));
        StringAssert.Contains(File.ReadAllText(Path.Combine(_outputDir, "index.worker.js")), "Indexer.Main();");
    }

    [TestMethod]
    public void NoEntriesWritesNoWorkerScript()
    {
        OutputBuilder.Build(Project(), Config(), "var app = 1;", _outputDir, "Debug");

        Assert.AreEqual(0, Directory.GetFiles(_outputDir, "*.worker.js").Length,
            "a project that declares no entry must produce no worker script");
    }

    [TestMethod]
    public void AReleaseWorkerLoadsTheMinifiedBundleTheReleaseSiteProduced()
    {
        // The worker has to run against the same code the page does, so it follows the same
        // formatted/minified choice index.html makes rather than pinning one spelling.
        var js = BuildSite("Release", new SharedWorkerEntry("live", "LiveWorker.Main()"));

        StringAssert.Contains(js, "\"./app.min.js\"", "a Release site scripts the minified bundle\n" + js);
        Assert.IsFalse(js.Contains("\"./app.js\""), "and not the formatted one\n" + js);
    }

    private static string Errors(AssemblyBuildResult result)
        => string.Join("\n", result.Diagnostics.Select(d => d.Id + ": " + d.GetMessage()));
}
