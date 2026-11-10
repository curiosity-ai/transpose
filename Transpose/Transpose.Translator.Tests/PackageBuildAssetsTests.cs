using Mono.Cecil;
using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// A package can ship a folder of assets and an <c>AfterTargets="Build"</c> target that copies it into
/// the site (Tesserae.Monaco, Tesserae.Lauschwerk and Tesserae.ThreeD do). MSBuild runs that target;
/// the in-process compiler library does not run MSBuild, so a workspace compiling its own front-end
/// shipped a site with no <c>assets/js/monaco</c> and every code editor 404'd. <see cref="PackageBuildAssets"/>
/// performs the targets that have that shape, and only those.
/// </summary>
[TestClass]
public sealed class PackageBuildAssetsTests
{
    private string _root = "";
    private string _outputDir = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "tps-pkgassets-" + Guid.NewGuid().ToString("N"));
        _outputDir = Path.Combine(_root, "site");
        Directory.CreateDirectory(_outputDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>The shape every real one has, including the parts that must be understood: a repo
    /// fallback guarded by <c>Exists</c>, the package-folder <c>NormalizeDirectory</c>, an overridable
    /// output property, an <c>Error</c> guard, a <c>ReadLinesFromFile</c>, an <c>Exclude</c>, and the
    /// recursive <c>Copy</c>.</summary>
    private const string FakeTargets = @"<Project>
  <PropertyGroup>
    <_FakePackagedDir>$([MSBuild]::NormalizeDirectory('$(MSBuildThisFileDirectory)..', 'fake'))</_FakePackagedDir>
    <_FakeRepoBundleDir>$([MSBuild]::NormalizeDirectory('$(MSBuildThisFileDirectory)..', 'assets', 'js', 'fake'))</_FakeRepoBundleDir>
  </PropertyGroup>
  <Target Name=""_CopyFakeAssets"" AfterTargets=""Build"" Condition=""'$(DesignTimeBuild)' != 'true'"">
    <PropertyGroup>
      <_FakeSourceDir Condition=""Exists('$(_FakePackagedDir)')"">$(_FakePackagedDir)</_FakeSourceDir>
      <_FakeSourceDir Condition=""'$(_FakeSourceDir)' == '' and Exists('$(_FakeRepoBundleDir)')"">$(_FakeRepoBundleDir)</_FakeSourceDir>
      <FakeAssetsOutputDir Condition=""'$(FakeAssetsOutputDir)' == ''"">$([MSBuild]::NormalizeDirectory('$(MSBuildProjectDirectory)', '$(OutDir)', 'tps', 'assets', 'js', 'fake'))</FakeAssetsOutputDir>
    </PropertyGroup>
    <Error Condition=""'$(_FakeSourceDir)' == ''"" Text=""no distribution"" />
    <ReadLinesFromFile File=""$(_FakeSourceDir)version.txt"">
      <Output TaskParameter=""Lines"" PropertyName=""_FakeVersion"" />
    </ReadLinesFromFile>
    <ItemGroup>
      <_FakeFiles Include=""$(_FakeSourceDir)**\*"" Exclude=""$(_FakeSourceDir)**\cli.js"" />
    </ItemGroup>
    <Copy SourceFiles=""@(_FakeFiles)""
          DestinationFiles=""@(_FakeFiles->'$(FakeAssetsOutputDir)%(RecursiveDir)%(Filename)%(Extension)')""
          SkipUnchangedFiles=""true"" OverwriteReadOnlyFiles=""true"" />
    <Message Importance=""normal"" Text=""copied $(_FakeVersion)"" />
  </Target>
</Project>";

    /// <summary>Lays out <c>pkgs/&lt;id&gt;/1.0.0/</c> as NuGet does and returns the DLL a project
    /// would reference. <paramref name="payload"/> is written under <c>fake/</c>.</summary>
    private string Package(string id, string? targets, string targetsFolder = "buildTransitive",
                            params (string rel, string content)[] payload)
    {
        var dir = Path.Combine(_root, "pkgs", id.ToLowerInvariant(), "1.0.0");
        Directory.CreateDirectory(Path.Combine(dir, "lib", "netstandard2.0"));
        File.WriteAllText(Path.Combine(dir, id.ToLowerInvariant() + ".nuspec"), "<package/>");

        if (targets is not null)
        {
            Directory.CreateDirectory(Path.Combine(dir, targetsFolder));
            File.WriteAllText(Path.Combine(dir, targetsFolder, id + ".targets"), targets);
        }

        foreach (var (rel, content) in payload)
        {
            var full = Path.Combine(dir, "fake", rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        var dll = Path.Combine(dir, "lib", "netstandard2.0", id + ".dll");
        using (var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(id, new Version(1, 0, 0, 0)), id, ModuleKind.Dll))
            asm.Write(dll);
        return dll;
    }

    private static readonly (string, string)[] Bundle =
    {
        ("fake.js", "// entry"),
        ("v1/chunks/a.js", "// chunk a"),
        ("v1/chunks/deep/b.js", "// chunk b"),
        ("cli.js", "// a node script, never served"),
        ("version.txt", "1.2.3"),
    };

    private PackageBuildAssets.Outcome Run(params string[] dlls)
        => PackageBuildAssets.CopyInto(dlls, _outputDir, new HashSet<string>());

    private string Out(string rel) => Path.Combine(_outputDir, rel.Replace('/', Path.DirectorySeparatorChar));

    [TestMethod]
    public void ACopyTargetOfAPackageShipsItsFolderIntoTheSiteKeepingSubfolders()
    {
        var written = new HashSet<string>();
        var dll = Package("Fake", FakeTargets, payload: Bundle);

        var outcome = PackageBuildAssets.CopyInto(new[] { dll }, _outputDir, written);

        Assert.AreEqual("// entry",    File.ReadAllText(Out("assets/js/fake/fake.js")));
        Assert.AreEqual("// chunk a",  File.ReadAllText(Out("assets/js/fake/v1/chunks/a.js")));
        Assert.AreEqual("// chunk b",  File.ReadAllText(Out("assets/js/fake/v1/chunks/deep/b.js")),
            "%(RecursiveDir) must carry every level of the folder, not just the first");
        Assert.IsTrue(File.Exists(Out("assets/js/fake/version.txt")));
        Assert.IsFalse(File.Exists(Out("assets/js/fake/cli.js")), "an Exclude on the item group is honoured");

        Assert.AreEqual(4, written.Count, "every copied file is recorded, so the build's stale-file pruning knows it wrote them");
        Assert.AreEqual(0, outcome.Skipped.Count, string.Join("; ", outcome.Skipped));
        StringAssert.Contains(outcome.Copied.Single(), "fake: 4 file(s) to assets/js/fake/");
    }

    [TestMethod]
    public void ATargetInBuildInsteadOfBuildTransitiveIsFoundToo()
    {
        Run(Package("Fake", FakeTargets, targetsFolder: "build", payload: Bundle));

        Assert.IsTrue(File.Exists(Out("assets/js/fake/fake.js")));
    }

    [TestMethod]
    public void ARepeatedBuildLeavesAnUpToDateFileAloneAndReplacesAChangedOne()
    {
        var dll = Package("Fake", FakeTargets, payload: Bundle);
        Run(dll);

        var copy = Out("assets/js/fake/fake.js");
        var stamp = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(copy, stamp);

        Run(dll);
        Assert.AreEqual(stamp, File.GetLastWriteTimeUtc(copy), "SkipUnchangedFiles: an identical, newer file is not rewritten");

        File.WriteAllText(copy, "// locally scribbled over, a different length");
        File.SetLastWriteTimeUtc(copy, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Run(dll);
        Assert.AreEqual("// entry", File.ReadAllText(copy), "a file that differs is replaced");
    }

    [TestMethod]
    public void APackageWhosePayloadIsMissingContributesNothingAndDoesNotFailTheBuild()
    {
        // The Error guard fails an MSBuild build. A compiler that is only asked for the site has
        // nothing to copy, and refusing to build over a missing optional folder helps nobody.
        var outcome = Run(Package("Fake", FakeTargets));

        Assert.AreEqual(0, outcome.Copied.Count);
        Assert.AreEqual(0, Directory.EnumerateFiles(_outputDir, "*", SearchOption.AllDirectories).Count());
    }

    [TestMethod]
    public void ATargetThatWritesOutsideTheSiteIsNotPerformed()
    {
        // The shape of a skill-install target: a Copy after Build, into the consuming repository.
        const string targets = @"<Project>
  <Target Name=""_InstallSkills"" AfterTargets=""Build"">
    <ItemGroup><_SkillFiles Include=""$(MSBuildThisFileDirectory)..\fake\**\*"" /></ItemGroup>
    <Copy SourceFiles=""@(_SkillFiles)"" DestinationFiles=""@(_SkillFiles->'$(MSBuildProjectDirectory)/.claude/skills/x/%(RecursiveDir)%(Filename)%(Extension)')"" />
  </Target>
</Project>";

        var outcome = Run(Package("Skills", targets, payload: Bundle));

        Assert.AreEqual(0, Directory.EnumerateFiles(_outputDir, "*", SearchOption.AllDirectories).Count(), "nothing may be written for a target that is not about the site");
        Assert.AreEqual(1, outcome.Skipped.Count, "…and it is said out loud");
        StringAssert.Contains(outcome.Skipped[0], "not inside the site's tps folder");
    }

    [TestMethod]
    public void ATargetThatUsesAnUnsupportedFunctionIsSkippedWholeAndReported()
    {
        const string targets = @"<Project>
  <Target Name=""_Copy"" AfterTargets=""Build"">
    <PropertyGroup><_Dir>$([System.IO.Path]::GetFullPath('x'))</_Dir></PropertyGroup>
    <ItemGroup><_F Include=""$(_Dir)**\*"" /></ItemGroup>
    <Copy SourceFiles=""@(_F)"" DestinationFiles=""@(_F->'$(_Dir)%(RecursiveDir)%(Filename)%(Extension)')"" />
  </Target>
</Project>";

        var outcome = Run(Package("Odd", targets, payload: Bundle));

        Assert.AreEqual(0, outcome.Copied.Count);
        Assert.AreEqual(1, outcome.Skipped.Count);
        StringAssert.Contains(outcome.Skipped[0], "outside what is evaluated");
    }

    [TestMethod]
    public void ATargetNotRunAfterBuildIsLeftAlone()
    {
        Run(Package("Fake", FakeTargets.Replace("AfterTargets=\"Build\"", "AfterTargets=\"Pack\""), payload: Bundle));

        Assert.AreEqual(0, Directory.EnumerateFiles(_outputDir, "*", SearchOption.AllDirectories).Count());
    }

    [TestMethod]
    public void AReferenceThatIsNotInAPackageContributesNothing()
    {
        // A referenced project's own output has no nuspec above it.
        var dll = Path.Combine(_root, "proj", "bin", "Debug", "netstandard2.0", "Proj.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
        using (var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Proj", new Version(1, 0)), "Proj", ModuleKind.Dll))
            asm.Write(dll);

        Assert.AreEqual(0, Run(dll).Copied.Count);
    }

    [TestMethod]
    public void TheSiteBuildPerformsPackageTargetsAndRecordsThem()
    {
        var dll = Package("Fake", FakeTargets, payload: Bundle);
        var appDir = Path.Combine(_root, "app");
        Directory.CreateDirectory(appDir);
        File.WriteAllText(Path.Combine(appDir, "tps.json"), @"{ ""fileName"": ""app.js"" }");

        var config = TransposeJson.TryLoad(appDir, "Release");
        Assert.IsNotNull(config);

        var project = new ResolvedProject
        {
            CsprojPath      = Path.Combine(appDir, "App.csproj"),
            ProjectDir      = appDir,
            AssemblyName    = "App",
            TargetFramework = "netstandard2.0",
            Sources         = new List<(string, string)>(),
            ReferencePaths  = new List<string> { dll },
            DefineConstants = new List<string>(),
            LanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest,
            ProjectDirs     = new List<string> { appDir },
        };

        var result = OutputBuilder.Build(project, config!, "var app = 1;", _outputDir, "Release");

        Assert.IsTrue(File.Exists(Out("assets/js/fake/v1/chunks/deep/b.js")), "the site build, not just the helper, ships the package's folder");
        Assert.AreEqual(1, result.PackageAssets.Count);
        Assert.IsTrue(File.Exists(Out("index.html")));

        // Recorded in the project's manifest, so deleting the package reference prunes them next build.
        var manifest = Directory.EnumerateFiles(_outputDir, ".tps-manifest.*.json").Single();
        StringAssert.Contains(File.ReadAllText(manifest), "assets/js/fake/fake.js");
    }
}
