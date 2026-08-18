using System.Text.Json;
using Mono.Cecil;
using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Covers recursive <c>tps.json</c> resource globs — a <c>**</c> path segment — and the folder layout
/// they imply.
///
/// A copy-through group of assets (images, fonts) is the case that matters: its files routinely sit in
/// sub-folders, and both halves of the protocol have to agree on where each one lands. The site build
/// reading the group from disk, and a consuming project extracting the same group out of a package DLL,
/// must produce the identical tree — otherwise a stylesheet's <c>url(../webfonts/x/y.woff2)</c> or an
/// <c>&lt;img src="assets/img/icons/logos/x.svg"&gt;</c> resolves in the library's own site and 404s in
/// every application that consumes it as a package.
///
/// Two properties are load-bearing beyond "the file gets copied":
/// <list type="bullet">
/// <item>the sub-directory reaches the manifest entry's <c>Path</c>, so extraction reproduces it;</item>
/// <item>the manifest <em>key</em> is qualified by that sub-directory, so two files sharing a leaf name
/// in different folders stay two resources (the manifest is keyed by name, and the embedder replaces
/// same-named resources — an unqualified key silently drops one of them).</item>
/// </list>
/// </summary>
[TestClass]
public sealed class RecursiveResourceGlobTests
{
    private string _root = "";
    private string _projectDir = "";
    private string _outputDir = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "tps-glob-" + Guid.NewGuid().ToString("N"));
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

    // ---------------------------------------------------------------- helpers

    private void Asset(string rel, string content)
    {
        var full = Path.Combine(_projectDir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    /// <summary>The asset tree the tests below share: files at the top level and at two further
    /// depths, plus a leaf name deliberately repeated in two different folders.</summary>
    private void NestedAssets()
    {
        Asset("tps/assets/img/logo.svg", "<svg id='logo'/>");
        Asset("tps/assets/img/icons/api.svg", "<svg id='icons-api'/>");
        Asset("tps/assets/img/icons/logos/api.svg", "<svg id='logos-api'/>");
        Asset("tps/assets/img/icons/logos/aws.svg", "<svg id='aws'/>");
        Asset("tps/assets/img/illustrations/empty.png", "png-bytes");
    }

    private TransposeJson Config(string tpsJson)
    {
        File.WriteAllText(Path.Combine(_projectDir, "tps.json"), tpsJson);
        var config = TransposeJson.TryLoad(_projectDir, "Debug");
        Assert.IsNotNull(config, "tps.json must load");
        return config!;
    }

    private ResolvedProject Project(params string[] referencePaths) => new()
    {
        CsprojPath = Path.Combine(_projectDir, "App.csproj"),
        ProjectDir = _projectDir,
        AssemblyName = "App",
        TargetFramework = "netstandard2.0",
        Sources = new List<(string, string)>(),
        ReferencePaths = referencePaths.ToList(),
        DefineConstants = new List<string>(),
        LanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest,
        ProjectDirs = new List<string> { _projectDir },
    };

    private void BuildSite(TransposeJson config, ResolvedProject? project = null)
        => OutputBuilder.Build(project ?? Project(), config, "var app = 1;", _outputDir, "Debug");

    private string SiteFile(string rel)
    {
        var full = Path.Combine(_outputDir, rel.Replace('/', Path.DirectorySeparatorChar));
        Assert.IsTrue(File.Exists(full), $"the site must contain {rel}");
        return File.ReadAllText(full);
    }

    private void AssertNotInSite(string rel)
        => Assert.IsFalse(File.Exists(Path.Combine(_outputDir, rel.Replace('/', Path.DirectorySeparatorChar))),
            $"the site must not contain {rel}");

    private const string ImagesRecursively = @"{
        ""fileName"": ""app.js"",
        ""resources"": [
            { ""name"": ""images"", ""files"": [ ""tps/assets/img/**"" ], ""output"": ""assets/img/"" }
        ]
    }";

    // ---------------------------------------------------------------- site build, from disk

    [TestMethod]
    public void ANonRecursiveGlobStillMatchesOnlyItsOwnDirectory()
    {
        // The shape every existing tps.json uses. Widening it silently would change what a published
        // package contains, so "*" has to keep meaning one directory.
        NestedAssets();

        BuildSite(Config(@"{
            ""fileName"": ""app.js"",
            ""resources"": [
                { ""name"": ""images"", ""files"": [ ""tps/assets/img/*"" ], ""output"": ""assets/img/"" }
            ]
        }"));

        SiteFile("assets/img/logo.svg");
        AssertNotInSite("assets/img/icons/api.svg");
        AssertNotInSite("assets/img/illustrations/empty.png");
    }

    [TestMethod]
    public void ARecursiveGlobCopiesEveryDepthAndKeepsTheFolderLayout()
    {
        NestedAssets();

        BuildSite(Config(ImagesRecursively));

        Assert.AreEqual("<svg id='logo'/>", SiteFile("assets/img/logo.svg"));
        Assert.AreEqual("<svg id='icons-api'/>", SiteFile("assets/img/icons/api.svg"));
        Assert.AreEqual("<svg id='logos-api'/>", SiteFile("assets/img/icons/logos/api.svg"),
            "a repeated leaf name in a deeper folder must stay its own file");
        Assert.AreEqual("<svg id='aws'/>", SiteFile("assets/img/icons/logos/aws.svg"));
        Assert.AreEqual("png-bytes", SiteFile("assets/img/illustrations/empty.png"));
    }

    [TestMethod]
    public void ARecursiveGlobCanNarrowToAFilePattern()
    {
        NestedAssets();

        BuildSite(Config(@"{
            ""fileName"": ""app.js"",
            ""resources"": [
                { ""name"": ""images"", ""files"": [ ""tps/assets/img/**/*.svg"" ], ""output"": ""assets/img/"" }
            ]
        }"));

        SiteFile("assets/img/logo.svg");
        SiteFile("assets/img/icons/logos/aws.svg");
        AssertNotInSite("assets/img/illustrations/empty.png");
    }

    [TestMethod]
    public void ARecursiveGlobWithoutAnOutputDirectorySitsAtTheSiteRoot()
    {
        NestedAssets();

        BuildSite(Config(@"{
            ""fileName"": ""app.js"",
            ""resources"": [
                { ""name"": ""images"", ""files"": [ ""tps/assets/img/**"" ] }
            ]
        }"));

        SiteFile("logo.svg");
        SiteFile("icons/logos/aws.svg");
    }

    [TestMethod]
    public void ARecursiveGlobUnderABundleGroupStillProducesOneFile()
    {
        // A group named for a .js/.css file concatenates whatever it matched — depth included — into
        // that single output; only copy-through groups reproduce a folder layout.
        Asset("tps/assets/css/a.css", ".a { }");
        Asset("tps/assets/css/theme/b.css", ".b { }");

        BuildSite(Config(@"{
            ""fileName"": ""app.js"",
            ""resources"": [
                { ""name"": ""bundle.css"", ""files"": [ ""tps/assets/css/**/*.css"" ], ""output"": ""assets/css"" }
            ]
        }"));

        var bundle = SiteFile("assets/css/bundle.css");
        StringAssert.Contains(bundle, ".a { }");
        StringAssert.Contains(bundle, ".b { }");
        AssertNotInSite("assets/css/theme/b.css");
    }

    // ---------------------------------------------------------------- packing

    [TestMethod]
    public void TheSubDirectoryReachesTheResourceManifestAsThePathAndQualifiesTheKey()
    {
        NestedAssets();

        var items = OutputBuilder.CollectEmbeddableItems(_projectDir, Config(ImagesRecursively), "Lib.js", "var lib = 1;", null);
        var manifest = ManifestEntries(PackageDll("Lib", items));

        Assert.AreEqual("assets/img", manifest["logo.svg"]);
        Assert.AreEqual("assets/img/icons", manifest["icons/api.svg"]);
        Assert.AreEqual("assets/img/icons/logos", manifest["icons/logos/api.svg"]);
        Assert.AreEqual("assets/img/illustrations", manifest["illustrations/empty.png"]);
    }

    [TestMethod]
    public void TwoFilesSharingALeafNameInDifferentFoldersStayTwoResources()
    {
        // The collision the unqualified key produced: the manifest is keyed by name and the embedder
        // replaces a same-named resource, so both api.svg files collapsed onto one entry — the site
        // then served one folder's icon from the other's path.
        NestedAssets();

        var items = OutputBuilder.CollectEmbeddableItems(_projectDir, Config(ImagesRecursively), "Lib.js", "var lib = 1;", null);

        Assert.AreEqual(2, items.Count(i => Path.GetFileName(i.Name) == "api.svg"), "both api.svg files must be embedded");

        using var asm = AssemblyDefinition.ReadAssembly(PackageDll("Lib", items));
        var resources = asm.MainModule.Resources.OfType<EmbeddedResource>().ToDictionary(r => r.Name);
        CollectionAssert.AreEqual("<svg id='icons-api'/>"u8.ToArray(), resources["icons/api.svg"].GetResourceData());
        CollectionAssert.AreEqual("<svg id='logos-api'/>"u8.ToArray(), resources["icons/logos/api.svg"].GetResourceData());
    }

    [TestMethod]
    public void AGroupWithoutAnOutputKeepsANullManifestPath()
    {
        // Guards the manifest bytes for every existing package: a group with no `output` and no
        // sub-directory must still serialize Path as null, not "".
        Asset("tps/assets/img/logo.svg", "<svg/>");

        var items = OutputBuilder.CollectEmbeddableItems(_projectDir, Config(@"{
            ""fileName"": ""Lib.js"",
            ""resources"": [
                { ""name"": ""images"", ""files"": [ ""tps/assets/img/*"" ] }
            ]
        }"), "Lib.js", "var lib = 1;", null);

        Assert.IsNull(items.Single(i => i.Name == "logo.svg").Output);
    }

    // ---------------------------------------------------------------- the whole chain

    [TestMethod]
    public void AReferencingSiteBuildExtractsAPackagesNestedAssetsToTheSamePaths()
    {
        // The bug this fixes, end to end: inside the library's own repository these files reached the
        // site through MSBuild's copy-to-output, which a NuGet consumer never runs — so the package's
        // embedded resources are the only channel, and they have to carry the whole tree.
        NestedAssets();

        var libConfig = Config(@"{
            ""fileName"": ""Lib.js"",
            ""resources"": [
                { ""name"": ""images"", ""files"": [ ""tps/assets/img/**"" ], ""output"": ""assets/img/"" }
            ]
        }");
        var dll = PackageDll("Lib", OutputBuilder.CollectEmbeddableItems(_projectDir, libConfig, "Lib.js", "var lib = 1;", null));

        // The consuming project declares no resources of its own — everything below comes from the package.
        BuildSite(Config(@"{ ""fileName"": ""app.js"" }"), Project(dll));

        Assert.AreEqual("<svg id='logo'/>", SiteFile("assets/img/logo.svg"));
        Assert.AreEqual("<svg id='icons-api'/>", SiteFile("assets/img/icons/api.svg"));
        Assert.AreEqual("<svg id='logos-api'/>", SiteFile("assets/img/icons/logos/api.svg"));
        Assert.AreEqual("<svg id='aws'/>", SiteFile("assets/img/icons/logos/aws.svg"));
        Assert.AreEqual("png-bytes", SiteFile("assets/img/illustrations/empty.png"));
    }

    [TestMethod]
    public void ANestedStylesheetIsLinkedFromIndexHtmlAtItsNestedPath()
    {
        // CSS found by a recursive glob still gets a <link>, and it has to point at the path the file
        // was actually written to.
        Asset("tps/assets/css/theme/dark.css", ".dark { }");

        BuildSite(Config(@"{
            ""fileName"": ""app.js"",
            ""resources"": [
                { ""name"": ""styles"", ""files"": [ ""tps/assets/css/**/*.css"" ], ""output"": ""assets/css"" }
            ]
        }"));

        SiteFile("assets/css/theme/dark.css");
        StringAssert.Contains(SiteFile("index.html"), "href=\"assets/css/theme/dark.css\"");
    }

    // ---------------------------------------------------------------- determinism

    [TestMethod]
    public void GlobResultsAreOrderedIndependentlyOfTheFileSystem()
    {
        // The resource manifest records the order the group matched in, so a build must not inherit
        // the enumeration order of whatever file system it runs on.
        foreach (var name in new[] { "zeta.svg", "alpha.svg", "mid.svg" })
            Asset("tps/assets/img/icons/" + name, "<svg/>");
        Asset("tps/assets/img/aaa.svg", "<svg/>");

        var items = OutputBuilder.CollectEmbeddableItems(_projectDir, Config(ImagesRecursively), "Lib.js", "var lib = 1;", null)
            .Where(i => i.Name.EndsWith(".svg", StringComparison.Ordinal))
            .Select(i => i.Name)
            .ToList();

        CollectionAssert.AreEqual(
            new[] { "aaa.svg", "icons/alpha.svg", "icons/mid.svg", "icons/zeta.svg" },
            items);
    }

    // ---------------------------------------------------------------- infrastructure

    /// <summary>Embeds <paramref name="items"/> into an assembly through the real
    /// <see cref="ResourceEmbedder"/> — the same call <c>tps --emit-package</c> makes — and returns the
    /// package DLL's path.</summary>
    private string PackageDll(string assemblyName, IReadOnlyList<EmbeddedItem> items)
    {
        byte[] bytes;
        using (var asm = AssemblyDefinition.CreateAssembly(
                   new AssemblyNameDefinition(assemblyName, new Version(1, 0, 0, 0)), assemblyName, ModuleKind.Dll))
        using (var ms = new MemoryStream())
        {
            asm.Write(ms);
            bytes = ms.ToArray();
        }

        var path = Path.Combine(_root, "packages", assemblyName + ".dll");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ResourceEmbedder.Embed(path, bytes, items);
        return path;
    }

    /// <summary>The package's <c>Transpose.Resources.json</c> manifest, as file name → output path.</summary>
    private static Dictionary<string, string?> ManifestEntries(string dllPath)
    {
        using var asm = AssemblyDefinition.ReadAssembly(dllPath);
        var manifest = asm.MainModule.Resources.OfType<EmbeddedResource>().Single(r => r.Name == "Transpose.Resources.json");
        using var doc = JsonDocument.Parse(manifest.GetResourceData());
        return doc.RootElement.EnumerateArray().ToDictionary(
            e => e.GetProperty("FileName").GetString()!,
            e => e.GetProperty("Path").GetString());
    }
}
