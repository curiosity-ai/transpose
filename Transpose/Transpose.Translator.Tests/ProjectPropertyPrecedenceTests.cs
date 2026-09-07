using Microsoft.CodeAnalysis.CSharp;
using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Which declaration of a repeated MSBuild property <c>tps</c> reads. MSBuild is last-write-wins, and
/// <see cref="ProjectXml.Property"/> used to answer with the first — so a csproj that pinned
/// <c>&lt;LangVersion&gt;</c> twice compiled at one version and was *analysed* by the IDE at another,
/// and an editor reported errors ("Feature 'default literal' is not available…") on code that built
/// cleanly. Anything the compiler and the IDE both read out of the csproj has to be read the same way.
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

    [TestMethod]
    public void LastDeclarationOfARepeatedPropertyWins()
    {
        // Curiosity.FrontEnd.API's shape exactly: 7.2 at the top of the file, 7 further down. MSBuild
        // (and so the IDE) compiles it as C# 7, which rejects the `default` literal the project uses.
        var project = ProjectResolver.Resolve(Csproj("""
              <PropertyGroup>
                <LangVersion>7.2</LangVersion>
              </PropertyGroup>
              <PropertyGroup>
                <LangVersion>7</LangVersion>
              </PropertyGroup>
            """));

        Assert.AreEqual(LanguageVersion.CSharp7, project.LanguageVersion);
    }

    [TestMethod]
    public void AConditionalDeclarationLosesToAnUnconditionalOne()
    {
        // Conditions are evaluated nowhere in this resolver, so a guarded value is not a value it can
        // trust — whichever side of the file it is written on.
        var project = ProjectResolver.Resolve(Csproj("""
              <PropertyGroup>
                <LangVersion>9</LangVersion>
              </PropertyGroup>
              <PropertyGroup Condition="'$(Configuration)' == 'Release'">
                <LangVersion>7.3</LangVersion>
              </PropertyGroup>
            """));

        Assert.AreEqual(LanguageVersion.CSharp9, project.LanguageVersion);
    }

    [TestMethod]
    public void AConditionOnThePropertyItselfCountsToo()
    {
        var project = ProjectResolver.Resolve(Csproj("""
              <PropertyGroup>
                <LangVersion Condition="'$(LangVersion)' == ''">7.3</LangVersion>
                <LangVersion>9</LangVersion>
              </PropertyGroup>
            """));

        Assert.AreEqual(LanguageVersion.CSharp9, project.LanguageVersion);
    }

    [TestMethod]
    public void WhenEveryDeclarationIsConditionalTheLastOneIsUsed()
    {
        // Nothing better to go on: answering null instead would silently drop a value the project
        // plainly states.
        var project = ProjectResolver.Resolve(Csproj("""
              <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
                <LangVersion>7.3</LangVersion>
              </PropertyGroup>
              <PropertyGroup Condition="'$(Configuration)' == 'Release'">
                <LangVersion>8</LangVersion>
              </PropertyGroup>
            """));

        Assert.AreEqual(LanguageVersion.CSharp8, project.LanguageVersion);
    }

    [TestMethod]
    public void AProjectThatSaysNothingCompilesAtTheLatestVersion()
    {
        // What the Transpose SDK's Sdk.props defaults an unset <LangVersion> to, so that an IDE
        // analyses the project at the version tps compiles it at.
        var project = ProjectResolver.Resolve(Csproj(""));

        Assert.AreEqual(LanguageVersion.Latest, project.LanguageVersion);
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
