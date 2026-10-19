using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// A project built under MSBuild leaves its referenced projects' DLLs alone.
///
/// MSBuild builds every ProjectReference with its own <c>tps</c> run before the project that
/// references it, so a dependent that built the library again only rewrote a DLL MSBuild had just
/// produced. Tesserae's three theme projects did that in parallel and failed with TPS0004 (the DLL in
/// use by another process). The SDK now asks <c>tps</c> not to (<c>TRANSPOSE_NO_BUILD_REFERENCES=1</c>,
/// <see cref="BuildOptions.SkipReferenceBuilds"/>).
/// </summary>
[TestClass]
public sealed class DependencyBuildReuseTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "tps-depreuse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private sealed class CapturingLog : BuildLog
    {
        public readonly List<string> Lines = new();
        public override void Info(string message) { lock (Lines) Lines.Add(message); }
        public override void Error(string message) { lock (Lines) Lines.Add(message); }
        public override string ToString() { lock (Lines) return string.Join("\n", Lines); }
    }

    private string Project(string name, string source, string? projectReference = null)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".cs"), source);

        var reference = projectReference is null
            ? ""
            : $@"  <ItemGroup>
    <ProjectReference Include=""..\{projectReference}\{projectReference}.csproj"" />
  </ItemGroup>
";
        var csproj = Path.Combine(dir, name + ".csproj");
        File.WriteAllText(csproj, $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <AssemblyName>{name}</AssemblyName>
    <IsPackable>true</IsPackable>
  </PropertyGroup>
{reference}</Project>");
        return csproj;
    }

    private static (BuildOutcome outcome, CapturingLog log) Build(string csproj, string assemblyVersion, bool skipReferenceBuilds)
    {
        var log = new CapturingLog();
        var outcome = ProjectBuild.Run(ProjectBuild.ResolveOutputMode(new BuildOptions
        {
            CsprojPath = csproj,
            Configuration = "Debug",
            AssemblyVersion = assemblyVersion,
            Incremental = true,
            SkipReferenceBuilds = skipReferenceBuilds,
        }), log);
        return (outcome, log);
    }

    private string LibDll => Path.Combine(_root, "Lib", "bin", "Debug", "netstandard2.0", "Lib.dll");

    /// <summary>Builds Lib on its own, then App against it, and checks Lib's DLL is the one Lib's own
    /// build wrote. Returns App's log.</summary>
    private CapturingLog AssertLibraryLeftAlone(string libVersion, string appVersion, bool skipReferenceBuilds)
    {
        var lib = Project("Lib", "namespace Lib { public class Thing { public int N; } }");
        var app = Project("App", "namespace App { public class User { public Lib.Thing T; } }", projectReference: "Lib");

        var (libOutcome, libLog) = Build(lib, libVersion, skipReferenceBuilds);
        Assert.AreEqual(0, libOutcome.ExitCode, "the library's own build must succeed:\n" + libLog);
        var written = File.GetLastWriteTimeUtc(LibDll);
        var bytes = File.ReadAllBytes(LibDll);

        var (appOutcome, appLog) = Build(app, appVersion, skipReferenceBuilds);
        Assert.AreEqual(0, appOutcome.ExitCode, "the dependent's build must succeed:\n" + appLog);

        Assert.AreEqual(written, File.GetLastWriteTimeUtc(LibDll), "the library's DLL must not be rewritten:\n" + appLog);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(LibDll), "the library's DLL must be the one its own build wrote");
        return appLog;
    }

    [TestMethod]
    public void UnderMsBuildADependentDoesNotBuildItsReferences()
    {
        // A CI build passes /p:Version, so each project gets a version its csproj does not carry. The
        // dependent could never have rebuilt the library with the right one, so it must not rebuild it.
        var log = AssertLibraryLeftAlone(libVersion: "2.3.4.5", appVersion: "9.8.7.6", skipReferenceBuilds: true);
        Assert.IsFalse(log.Lines.Any(l => l.Contains("dependency")), "no reference may be checked or built:\n" + log);
    }

    [TestMethod]
    public void OnItsOwnADependentReusesTheLibrarysOwnBuild()
    {
        // Without MSBuild tps still builds the references, and a package build of a library has to
        // reach the settings key the library's own build wrote: --watch only counts for a site.
        var log = AssertLibraryLeftAlone(libVersion: "", appVersion: "", skipReferenceBuilds: false);
        var at = log.Lines.FindIndex(l => l.Contains("building dependency: Lib"));
        Assert.IsTrue(at >= 0, "the dependent must check its dependency:\n" + log);
        StringAssert.Contains(log.Lines[at + 1], "up to date", "the library's own build must be reused:\n" + log);
    }
}
