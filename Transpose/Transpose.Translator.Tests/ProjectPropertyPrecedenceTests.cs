using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Which declaration of a repeated MSBuild property <c>tps</c> reads, and the one property it does not
/// read at all. MSBuild is last-write-wins, and <see cref="ProjectXml.Property"/> used to answer with
/// the first — so a csproj that pinned <c>&lt;LangVersion&gt;</c> twice compiled at one version and was
/// *analysed* by the IDE at another, and an editor reported errors ("Feature 'default literal' is not
/// available…") on code that built cleanly. Anything the compiler and the IDE both read out of the
/// csproj has to be read the same way.
///
/// <c>&lt;LangVersion&gt;</c> itself is now settled differently: the SDK overwrites it with the one
/// version this compiler supports and <c>tps</c> ignores the project's value, so there is nothing left
/// for the two to disagree about. The last-write-wins rule still governs every other property.
/// </summary>
[TestClass]
public sealed class ProjectPropertyPrecedenceTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "tps-props-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>A project with no sources of its own, so nothing but the property groups is in play.</summary>
    private string Csproj(string propertyGroups)
    {
        var path = Path.Combine(_root, "App.csproj");
        File.WriteAllText(path, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
            {propertyGroups}
            </Project>
            """);
        return path;
    }

    /// <summary>The rule itself, on the property whose two spellings started it. Note that
    /// <c>&lt;LangVersion&gt;</c> no longer decides anything — the SDK overwrites it and
    /// <c>ProjectResolver</c> ignores it (see <see cref="LangVersionIsNotTheProjectsToChoose"/>) —
    /// but every other property tps reads out of a csproj is read through this one method.</summary>
    [TestMethod]
    public void LastDeclarationOfARepeatedPropertyWins()
    {
        // Curiosity.FrontEnd.API's shape exactly: 7.2 at the top of the file, 7 further down. MSBuild
        // (and so the IDE) evaluates it as C# 7, which rejects the `default` literal the project uses.
        var doc = ProjectXml.Load(Csproj("""
              <PropertyGroup>
                <LangVersion>7.2</LangVersion>
              </PropertyGroup>
              <PropertyGroup>
                <LangVersion>7</LangVersion>
              </PropertyGroup>
            """));

        Assert.AreEqual("7", doc.Property("LangVersion"));
    }

    [TestMethod]
    public void LastDeclarationWinsForEveryPropertyTheResolverReads()
    {
        // The same rule, seen through a property that really does change what tps builds.
        var project = ProjectResolver.Resolve(Csproj("""
              <PropertyGroup>
                <AssemblyName>First</AssemblyName>
              </PropertyGroup>
              <PropertyGroup>
                <AssemblyName>Second</AssemblyName>
              </PropertyGroup>
            """));

        Assert.AreEqual("Second", project.AssemblyName);
    }

    [TestMethod]
    public void AConditionalDeclarationLosesToAnUnconditionalOne()
    {
        // Conditions are evaluated nowhere in this resolver, so a guarded value is not a value it can
        // trust — whichever side of the file it is written on.
        var project = ProjectResolver.Resolve(Csproj("""
              <PropertyGroup>
                <AssemblyName>Unguarded</AssemblyName>
              </PropertyGroup>
              <PropertyGroup Condition="'$(Configuration)' == 'Release'">
                <AssemblyName>Guarded</AssemblyName>
              </PropertyGroup>
            """));

        Assert.AreEqual("Unguarded", project.AssemblyName);
    }

    [TestMethod]
    public void AConditionOnThePropertyItselfCountsToo()
    {
        var project = ProjectResolver.Resolve(Csproj("""
              <PropertyGroup>
                <AssemblyName Condition="'$(AssemblyName)' == ''">Guarded</AssemblyName>
                <AssemblyName>Unguarded</AssemblyName>
              </PropertyGroup>
            """));

        Assert.AreEqual("Unguarded", project.AssemblyName);
    }

    [TestMethod]
    public void WhenEveryDeclarationIsConditionalTheLastOneIsUsed()
    {
        // Nothing better to go on: answering null instead would silently drop a value the project
        // plainly states.
        var project = ProjectResolver.Resolve(Csproj("""
              <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
                <AssemblyName>DebugName</AssemblyName>
              </PropertyGroup>
              <PropertyGroup Condition="'$(Configuration)' == 'Release'">
                <AssemblyName>ReleaseName</AssemblyName>
              </PropertyGroup>
            """));

        Assert.AreEqual("ReleaseName", project.AssemblyName);
    }

    [TestMethod]
    public void LangVersionIsNotTheProjectsToChoose()
    {
        // A Transpose project compiles at the one version this compiler supports, whatever the csproj
        // says: the SDK overwrites <LangVersion> unconditionally (Sdk.targets), so honouring a pin
        // here would put the compiler and the IDE back on different languages — the editor accepting
        // C# 14 that tps then rejects. A lower pin could never do anything but reject working code.
        foreach (var pinned in new[] { "", "<PropertyGroup><LangVersion>7.2</LangVersion></PropertyGroup>" })
        {
            var project = ProjectResolver.Resolve(Csproj(pinned));

            Assert.AreEqual(ProjectResolver.SupportedLanguageVersion, project.LanguageVersion, pinned);
        }
    }

    [TestMethod]
    public void LangVersionMatchesTheCompiler()
    {
        // The SDK writes the number, tps compiles at whatever its own Roslyn calls "latest", and the
        // two have to be the same language — which is the whole reason for writing a number instead
        // of `latest` (that would mean "whatever the *reader's* Roslyn supports"). So a bump of
        // Microsoft.CodeAnalysis.CSharp that raises the newest C# has to be followed by an edit to
        // Sdk.targets, and this is what says so.
        var sdkTargets = Path.Combine(RepositoryRoot(), "Transpose", "Transpose.Build.Target", "Sdk", "Sdk.targets");
        Assert.IsTrue(File.Exists(sdkTargets), sdkTargets);

        var declared = Regex.Matches(File.ReadAllText(sdkTargets), "<LangVersion[^>]*>([^<]+)</LangVersion>")
            .Select(m => m.Groups[1].Value.Trim())
            .ToList();

        Assert.AreEqual(1, declared.Count, "the SDK writes <LangVersion> exactly once, and unconditionally");
        Assert.IsTrue(LanguageVersionFacts.TryParse(declared[0], out var sdkVersion),
            $"'{declared[0]}' is not a language version this Roslyn knows — an IDE would fail the project with CS1617");

        var compiler = LanguageVersionFacts.MapSpecifiedToEffectiveVersion(ProjectResolver.SupportedLanguageVersion);
        Assert.AreEqual(compiler, LanguageVersionFacts.MapSpecifiedToEffectiveVersion(sdkVersion),
            $"Sdk.targets pins C# {declared[0]}, but the compiler's Roslyn compiles at {compiler} — update Sdk.targets");
    }

    /// <summary>The repository root, found by walking up to the solution file.</summary>
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Transpose.slnx")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not find Transpose.slnx above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    [TestMethod]
    public void DefineConstantsFollowsTheSameRule()
    {
        var project = ProjectResolver.Resolve(Csproj("""
              <PropertyGroup>
                <DefineConstants>FIRST</DefineConstants>
              </PropertyGroup>
              <PropertyGroup>
                <DefineConstants>SECOND</DefineConstants>
              </PropertyGroup>
            """));

        Assert.IsTrue(project.DefineConstants.Contains("SECOND"), "the last <DefineConstants> is the one MSBuild keeps");
        Assert.IsFalse(project.DefineConstants.Contains("FIRST"), "the overwritten value must not survive");
    }
}
