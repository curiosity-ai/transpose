using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Covers which assembly a <c>&lt;PackageReference&gt;</c> resolves to when the same package is reachable
/// more than once — directly and through another package's dependencies, or through two packages that
/// each want a different version.
///
/// <c>tps</c> reads the csproj itself instead of letting NuGet resolve the graph, so it has to apply
/// NuGet's precedence by hand: the version the project declares wins over any transitive one, and the
/// higher version wins between two transitive candidates. Resolving in document order instead silently
/// compiled a project against an older package — e.g. a csproj listing <c>Tesserae.GraphKit</c> above
/// <c>Tesserae</c> bound to the Tesserae version GraphKit's nuspec names rather than the one written in
/// the csproj, so types added in the newer Tesserae looked absent.
/// </summary>
[TestClass]
public sealed class PackageReferenceResolutionTests
{
    private string  _root     = "";
    private string  _cache    = "";
    private string? _previousCacheEnv;

    [TestInitialize]
    public void Setup()
    {
        _root  = Path.Combine(Path.GetTempPath(), "tps-pkgref-" + Guid.NewGuid().ToString("N"));
        _cache = Path.Combine(_root, "packages");
        Directory.CreateDirectory(_cache);

        // NuGetRoots() honours NUGET_PACKAGES first, so the fake cache below is what gets resolved.
        _previousCacheEnv = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        Environment.SetEnvironmentVariable("NUGET_PACKAGES", _cache);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("NUGET_PACKAGES", _previousCacheEnv);
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>Lays out one package in the fake cache the way NuGet extracts it: lowercase
    /// <c>&lt;id&gt;/&lt;version&gt;/</c> holding <c>lib/netstandard2.0/</c> and a lowercase nuspec.</summary>
    private void Package(string id, string version, string[] assemblies, params (string id, string version)[] dependencies)
    {
        var lib = Path.Combine(_cache, id.ToLowerInvariant(), version, "lib", "netstandard2.0");
        Directory.CreateDirectory(lib);
        foreach (var assembly in assemblies)
            File.WriteAllBytes(Path.Combine(lib, assembly + ".dll"), new byte[] { 0 });

        var deps = string.Join("\n", dependencies.Select(d => $"        <dependency id=\"{d.id}\" version=\"{d.version}\" />"));
        File.WriteAllText(Path.Combine(_cache, id.ToLowerInvariant(), version, id.ToLowerInvariant() + ".nuspec"), $@"<?xml version=""1.0""?>
<package>
  <metadata>
    <id>{id}</id>
    <version>{version}</version>
    <dependencies>
      <group targetFramework="".NETStandard2.0"">
{deps}
      </group>
    </dependencies>
  </metadata>
</package>");
    }

    private string Csproj(params string[] packageReferences)
    {
        var items = string.Join("\n", packageReferences.Select(r => "    " + r));
        var path  = Path.Combine(_root, "app", "app.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $@"<Project>
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
{items}
  </ItemGroup>
</Project>");
        return path;
    }

    private static string Reference(ResolvedProject project, string assemblyName)
        => project.ReferencePaths.Single(p => Path.GetFileNameWithoutExtension(p).Equals(assemblyName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Writes a csproj under <c>_root/relativeDir/fileName</c> with the given raw items
    /// (<c>&lt;PackageReference&gt;</c>/<c>&lt;ProjectReference&gt;</c>) and returns its path — used to
    /// build a small multi-project closure (unlike <see cref="Csproj"/>, which always writes a single
    /// "app" project).</summary>
    private string CsprojAt(string relativeDir, string fileName, params string[] items)
    {
        var path = Path.Combine(_root, relativeDir, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var itemsXml = string.Join("\n", items.Select(i => "    " + i));
        File.WriteAllText(path, $@"<Project>
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
{itemsXml}
  </ItemGroup>
</Project>");
        return path;
    }

    [TestMethod]
    public void AVersionRequiredThroughAProjectReferenceWinsOverTheConsumersOwnLowerDeclaration_Bundle()
    {
        Package("Lib", "1.1.0", new[] { "lib" });
        Package("Lib", "1.2.0", new[] { "lib" });

        CsprojAt("ProjectA", "ProjectA.csproj", @"<PackageReference Include=""Lib"" Version=""1.2.0"" />");
        var projectB = CsprojAt("ProjectB", "ProjectB.csproj",
            @"<PackageReference Include=""Lib"" Version=""1.1.0"" />",
            @"<ProjectReference Include=""..\ProjectA\ProjectA.csproj"" />");

        var project = ProjectResolver.Resolve(projectB); // default: bundle mode

        StringAssert.Contains(Reference(project, "lib"), "1.2.0",
            "a version required transitively through a ProjectReference must win over the consumer's own lower declared version");
    }

    [TestMethod]
    public void AVersionRequiredThroughAProjectReferenceWinsOverTheConsumersOwnLowerDeclaration_SeparateAssemblies()
    {
        Package("Lib", "1.1.0", new[] { "lib" });
        Package("Lib", "1.2.0", new[] { "lib" });

        CsprojAt("ProjectA", "ProjectA.csproj", @"<PackageReference Include=""Lib"" Version=""1.2.0"" />");
        var projectB = CsprojAt("ProjectB", "ProjectB.csproj",
            @"<PackageReference Include=""Lib"" Version=""1.1.0"" />",
            @"<ProjectReference Include=""..\ProjectA\ProjectA.csproj"" />");

        var project = ProjectResolver.Resolve(projectB, separateAssemblies: true);

        StringAssert.Contains(Reference(project, "lib"), "1.2.0",
            "separate-assembly mode must reconcile the ProjectReference closure the same way bundle mode does");
    }

    [TestMethod]
    public void AVersionRequiredThroughATransitiveProjectReferenceStillWins_SeparateAssemblies()
    {
        // B -> A -> C, where only C declares Lib. Separate-assembly mode used to gather package
        // references only one ProjectReference level deep, silently dropping C's.
        Package("Lib", "1.2.0", new[] { "lib" });

        CsprojAt("ProjectC", "ProjectC.csproj", @"<PackageReference Include=""Lib"" Version=""1.2.0"" />");
        CsprojAt("ProjectA", "ProjectA.csproj", @"<ProjectReference Include=""..\ProjectC\ProjectC.csproj"" />");
        var projectB = CsprojAt("ProjectB", "ProjectB.csproj",
            @"<ProjectReference Include=""..\ProjectA\ProjectA.csproj"" />");

        var project = ProjectResolver.Resolve(projectB, separateAssemblies: true);

        StringAssert.Contains(Reference(project, "lib"), "1.2.0",
            "a package declared two ProjectReference levels away must still resolve in separate-assembly mode");
    }

    [TestMethod]
    public void TheDeclaredVersionWinsOverATransitiveDependencyOnAnOlderOne()
    {
        Package("Toolkit", "2026.7.68568", new[] { "tk" });
        Package("Toolkit", "2026.7.68553", new[] { "tk" });
        Package("Toolkit.Charts", "26.7.3033", new[] { "Toolkit.Charts" }, ("Toolkit", "2026.7.68553"));

        // Charts first: under document-order resolution its older Toolkit dependency won.
        var project = ProjectResolver.Resolve(Csproj(
            @"<PackageReference Include=""Toolkit.Charts"" Version=""26.7.3033"" />",
            @"<PackageReference Include=""Toolkit"" Version=""2026.7.68568"" />"));

        StringAssert.Contains(Reference(project, "tk"), "2026.7.68568",
            "the version the csproj declares must win over a transitive dependency on an older one");
    }

    [TestMethod]
    public void TheDeclaredVersionWinsRegardlessOfWhereItIsListed()
    {
        Package("Toolkit", "2026.7.68568", new[] { "tk" });
        Package("Toolkit", "2026.7.68553", new[] { "tk" });
        Package("Toolkit.Charts", "26.7.3033", new[] { "Toolkit.Charts" }, ("Toolkit", "2026.7.68553"));

        var project = ProjectResolver.Resolve(Csproj(
            @"<PackageReference Include=""Toolkit"" Version=""2026.7.68568"" />",
            @"<PackageReference Include=""Toolkit.Charts"" Version=""26.7.3033"" />"));

        StringAssert.Contains(Reference(project, "tk"), "2026.7.68568");
    }

    [TestMethod]
    public void TheHighestVersionWinsBetweenTwoTransitiveCandidates()
    {
        Package("Shared", "1.0.0", new[] { "shared" });
        Package("Shared", "2.5.0", new[] { "shared" });
        Package("Left",  "1.0.0", new[] { "left" },  ("Shared", "1.0.0"));
        Package("Right", "1.0.0", new[] { "right" }, ("Shared", "2.5.0"));

        var project = ProjectResolver.Resolve(Csproj(
            @"<PackageReference Include=""Left"" Version=""1.0.0"" />",
            @"<PackageReference Include=""Right"" Version=""1.0.0"" />"));

        StringAssert.Contains(Reference(project, "shared"), "2.5.0",
            "with no declared version, the higher transitive candidate must win");
    }

    [TestMethod]
    public void TransitiveDependenciesStillResolveWhenNothingElseProvidesThem()
    {
        Package("Runtime", "26.7.3027", new[] { "Runtime" });
        Package("Widgets", "1.0.0", new[] { "widgets" }, ("Runtime", "26.7.3027"));

        var project = ProjectResolver.Resolve(Csproj(
            @"<PackageReference Include=""Widgets"" Version=""1.0.0"" />"));

        StringAssert.Contains(Reference(project, "Runtime"), "26.7.3027",
            "a transitively-referenced package must still be resolved");
    }
}
