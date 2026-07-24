using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Covers <c>&lt;Import Project="…"/&gt;</c> resolution — the mechanism **shared projects** rely on: a
/// <c>.shproj</c>'s companion <c>.projitems</c> holds the <c>&lt;Compile&gt;</c> items and every
/// consuming project imports it. <c>tps</c> reads the csproj as raw XML rather than evaluating it with
/// MSBuild, so before <see cref="ProjectXml"/> an import was invisible and all of that source silently
/// vanished from the compilation (the build failed with "type or namespace not found" for code that
/// plainly existed).
///
/// The two directory rules a <c>.projitems</c> depends on are what these tests pin down:
/// <c>$(MSBuildThisFileDirectory)</c> means the *declaring* file's directory, while a plain relative
/// include is resolved against the *project* directory.
/// </summary>
[TestClass]
public sealed class ProjectImportTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "tps-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Write(string rel, string content)
    {
        var full = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    /// <summary>A project that opts out of the default glob, so the only sources are the ones the
    /// import contributes — which makes a missed import unmissable in the assertion.</summary>
    private const string AppCsprojTemplate = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>netstandard2.0</TargetFramework>
            <AssemblyName>app</AssemblyName>
            <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
          </PropertyGroup>
        {IMPORTS}
        </Project>
        """;

    private string WriteApp(params string[] imports)
        => Write("App/App.csproj", AppCsprojTemplate.Replace("{IMPORTS}",
            string.Join("\n", imports.Select(i => "  " + i))));

    private static IReadOnlyList<string> SourceNames(string csproj)
        => ProjectResolver.Resolve(csproj).Sources
            .Select(s => Path.GetFileName(s.path))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    [TestMethod]
    public void ProjItemsCompileItemsAreImported()
    {
        // $(MSBuildThisFileDirectory) — the form every real .projitems uses — must resolve against the
        // .projitems' own folder, including for a nested path.
        Write("Shared/Shared.projitems", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup><HasSharedItems>true</HasSharedItems></PropertyGroup>
              <ItemGroup>
                <Compile Include="$(MSBuildThisFileDirectory)Greeter.cs" />
                <Compile Include="$(MSBuildThisFileDirectory)Nested\Helper.cs" />
              </ItemGroup>
            </Project>
            """);
        Write("Shared/Greeter.cs", "namespace Sh { public static class Greeter { } }");
        Write("Shared/Nested/Helper.cs", "namespace Sh { public static class Helper { } }");
        var csproj = WriteApp("""<Import Project="..\Shared\Shared.projitems" Label="Shared" />""");

        CollectionAssert.AreEqual(new[] { "Greeter.cs", "Helper.cs" }, SourceNames(csproj).ToArray());
    }

    [TestMethod]
    public void ImportsAreFollowedTransitively()
    {
        Write("Deep/Deep.projitems", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup><Compile Include="$(MSBuildThisFileDirectory)Deep.cs" /></ItemGroup>
            </Project>
            """);
        Write("Deep/Deep.cs", "namespace Sh { public static class Deep { } }");
        Write("Shared/Shared.projitems", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup><Compile Include="$(MSBuildThisFileDirectory)Greeter.cs" /></ItemGroup>
              <Import Project="$(MSBuildThisFileDirectory)..\Deep\Deep.projitems" />
            </Project>
            """);
        Write("Shared/Greeter.cs", "namespace Sh { public static class Greeter { } }");
        var csproj = WriteApp("""<Import Project="..\Shared\Shared.projitems" />""");

        CollectionAssert.AreEqual(new[] { "Deep.cs", "Greeter.cs" }, SourceNames(csproj).ToArray());
    }

    [TestMethod]
    public void RelativeIncludeInAnImportResolvesAgainstTheProjectDirectory()
    {
        // MSBuild resolves a *relative* item include against the project being built, not the file that
        // declared it. That asymmetry with $(MSBuildThisFileDirectory) is exactly why .projitems files
        // are written the way they are, so both halves need pinning down.
        Write("Shared/Shared.projitems", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup><Compile Include="Local.cs" /></ItemGroup>
            </Project>
            """);
        Write("App/Local.cs", "public static class Local { }");
        Write("Shared/Local.cs", "public static class WrongLocal { }");
        var csproj = WriteApp("""<Import Project="..\Shared\Shared.projitems" />""");

        var resolved = ProjectResolver.Resolve(csproj).Sources.Single().path.Replace('\\', '/');
        StringAssert.Contains(resolved, "/App/Local.cs");
    }

    [TestMethod]
    public void CompileRemoveInAnImportIsHonoured()
    {
        Write("Shared/Shared.projitems", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup>
                <Compile Include="$(MSBuildThisFileDirectory)Keep.cs" />
                <Compile Include="$(MSBuildThisFileDirectory)Drop.cs" />
                <Compile Remove="$(MSBuildThisFileDirectory)Drop.cs" />
              </ItemGroup>
            </Project>
            """);
        Write("Shared/Keep.cs", "public static class Keep { }");
        Write("Shared/Drop.cs", "public static class Drop { }");
        var csproj = WriteApp("""<Import Project="..\Shared\Shared.projitems" />""");

        CollectionAssert.AreEqual(new[] { "Keep.cs" }, SourceNames(csproj).ToArray());
    }

    [TestMethod]
    public void UnresolvableAndMissingImportsAreSkippedWithoutFailing()
    {
        // A conditioned import whose file is absent, and an import written in terms of a property this
        // resolver never evaluates, must both be ignored rather than throw — an SDK-internal import
        // (`$(MSBuildToolsPath)…`) is exactly the second case, and every project has those.
        Write("Shared/Shared.projitems", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup><Compile Include="$(MSBuildThisFileDirectory)Greeter.cs" /></ItemGroup>
            </Project>
            """);
        Write("Shared/Greeter.cs", "namespace Sh { public static class Greeter { } }");
        var csproj = WriteApp(
            """<Import Project="..\Shared\Shared.projitems" />""",
            """<Import Project="..\NotThere\Nope.projitems" Condition="false" />""",
            """<Import Project="$(SomePropertyWeCannotExpand)Other.props" />""");

        CollectionAssert.AreEqual(new[] { "Greeter.cs" }, SourceNames(csproj).ToArray());
    }

    [TestMethod]
    public void PropertiesAndPackageReferencesComeThroughImports()
    {
        // Sources are not the only thing an import can carry: a shared .props commonly sets properties
        // and package references, and the flattened view has to surface those too. The project's own
        // value wins over an imported one.
        Write("Shared/Common.props", """
            <Project>
              <PropertyGroup>
                <AssemblyName>from-import</AssemblyName>
                <DefineConstants>FROM_IMPORT</DefineConstants>
              </PropertyGroup>
            </Project>
            """);
        var csproj = Write("App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netstandard2.0</TargetFramework>
                <AssemblyName>from-project</AssemblyName>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <Import Project="..\Shared\Common.props" />
            </Project>
            """);

        var resolved = ProjectResolver.Resolve(csproj);
        Assert.AreEqual("from-project", resolved.AssemblyName, "the project's own property must win");
        Assert.IsTrue(resolved.DefineConstants.Contains("FROM_IMPORT"),
            "a define declared only in an imported file must still reach the compilation");
    }
}

/// <summary>
/// Covers error reporting: a failing build must surface **every** error, not a truncated prefix. The
/// compiler used to stop at 40, which made a broken project take several compile cycles to fix and
/// left the user unable to tell whether the errors they could not see were the same problem or a
/// different one.
/// </summary>
[TestClass]
public sealed class ErrorReportingTests
{
    /// <summary>Compiles two files with a known number of body errors each and returns the diagnostics
    /// the translator produced. B.cs is passed first so the ordering assertion has something to do.</summary>
    private static IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> Errors(int perFile)
    {
        string Body(string cls) => "public class " + cls + " {\n" + string.Concat(
            Enumerable.Range(0, perFile).Select(i => $"    public int M{i}() {{ return Missing{i}(); }}\n")) + "}\n";

        var result = new Transpose.Translator.RoslynTranslator().Translate(
            new[] { ("B.cs", Body("B")), ("A.cs", Body("A")) }, "ErrTest");
        return result.Errors.ToList();
    }

    [TestMethod]
    public void EveryErrorIsReported()
    {
        // 60 per file = 120 total: comfortably past the 40-error cap the compiler used to apply.
        const int perFile = 60;
        var errors = Program.OrderErrorsForReport(Errors(perFile));
        Assert.AreEqual(perFile * 2, errors.Count(d => d.Id == "CS0103"),
            "every error from both files must survive to the report");
    }

    [TestMethod]
    public void ErrorsAreOrderedByFileThenPosition()
    {
        var errors = Program.OrderErrorsForReport(Errors(5));

        var files = errors.Select(d => Path.GetFileName(d.Location.SourceTree?.FilePath ?? "")).ToList();
        CollectionAssert.AreEqual(files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray(), files.ToArray(),
            "files must be grouped and ordered — A.cs before B.cs even though B.cs was compiled first");

        foreach (var group in errors.GroupBy(d => d.Location.SourceTree?.FilePath))
        {
            var lines = group.Select(d => d.Location.GetLineSpan().StartLinePosition.Line).ToList();
            CollectionAssert.AreEqual(lines.OrderBy(l => l).ToArray(), lines.ToArray(),
                "within a file, errors must ascend by line");
        }
    }
}
