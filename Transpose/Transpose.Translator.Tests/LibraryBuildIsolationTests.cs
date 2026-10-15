using Transpose.Compiler.Library;

namespace Transpose.Translator.Tests;

/// <summary>
/// A host of <see cref="TransposeCompilerLibrary"/> runs many builds in one process. One build's
/// failure must say why on its own result, and must not leave its callbacks installed for the next.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LibraryBuildIsolationTests
{
    private string _dir = "";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tps-libiso-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    [TestMethod]
    public void AFailedBuildListsItsErrorAndClearsItsProgressSink()
    {
        var csproj = Path.Combine(_dir, "App.csproj");
        File.WriteAllText(csproj, "<Project><PropertyGroup><TargetFramework>netstandard2.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(_dir, "App.cs"), "public class App { }");
        // The site directory is blocked by a file, so writing the site fails after a clean compile:
        // an error with no Roslyn diagnostic behind it.
        File.WriteAllText(Path.Combine(_dir, "tps.json"), @"{ ""fileName"": ""app.js"", ""output"": ""blocked/site/"" }");
        File.WriteAllText(Path.Combine(_dir, "blocked"), "a file, not a directory");
        var progress = new List<string>();

        var result = TransposeCompilerLibrary.BuildProject(new ProjectBuildRequest(csproj) { OnProgress = progress.Add });

        Assert.IsFalse(result.Success, string.Join("\n", result.Output));
        Assert.AreNotEqual(0, result.Errors.Count, string.Join("\n", result.Output));
        Assert.IsNull(CompileProgress.Sink, "the next compile must not report into this request's callback");
    }
}
