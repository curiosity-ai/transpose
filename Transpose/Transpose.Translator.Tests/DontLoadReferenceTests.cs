using Mono.Cecil;
using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Covers the tps.json <c>dontLoadReferences</c> list: the consumer-side counterpart of a library's
/// <c>loadCompiledOutput: false</c>. An application names a referenced assembly, and everything that
/// assembly contributes is still extracted into the site — its bundle, its authored scripts, its
/// stylesheets — while none of it is referenced from the generated <c>index.html</c>. That is what
/// lets a heavy binding only one screen needs (a chart library, a map) be fetched by the application
/// itself the first time that screen opens instead of on every page load.
///
/// The library needs no cooperation: the flag lives entirely in the consuming project, which is the
/// whole point — a published package cannot know which of its consumers wants it lazily.
/// </summary>
[TestClass]
public sealed class DontLoadReferenceTests
{
    private string _root = "";
    private string _projectDir = "";
    private string _libDir = "";
    private string _outputDir = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "tps-dontload-ref-" + Guid.NewGuid().ToString("N"));
        _projectDir = Path.Combine(_root, "app");
        _libDir = Path.Combine(_root, "lib");
        _outputDir = Path.Combine(_root, "site");
        Directory.CreateDirectory(_projectDir);
        Directory.CreateDirectory(_libDir);
        Directory.CreateDirectory(_outputDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Writes <paramref name="tpsJson"/> as the application's tps.json and loads it.</summary>
    private TransposeJson AppConfig(string tpsJson)
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

    /// <summary>
    /// Builds a package DLL for <paramref name="assemblyName"/> the way <c>tps --emit-package</c> does:
    /// a compiled bundle (in both variants, tagged), one authored script and one stylesheet. That is
    /// the shape a real binding package has — Tesserae.Plotly ships its own compiled code plus the
    /// vendored library it binds to — so the test exercises every kind of file a reference can inject.
    /// </summary>
    private string PackageDll(string assemblyName)
    {
        var libProjectDir = Path.Combine(_libDir, assemblyName);
        Directory.CreateDirectory(libProjectDir);
        File.WriteAllText(Path.Combine(libProjectDir, "vendor.js"), "// the vendored library");
        File.WriteAllText(Path.Combine(libProjectDir, "lib.css"), ".lib { }");
        File.WriteAllText(Path.Combine(libProjectDir, "tps.json"), $@"{{
            ""fileName"": ""{assemblyName}.js"",
            ""resources"": [
                {{ ""name"": ""{assemblyName}-vendor.js"", ""files"": [ ""vendor.js"" ] }},
                {{ ""name"": ""{assemblyName}.css"",      ""files"": [ ""lib.css"" ] }}
            ]
        }}");
        var libConfig = TransposeJson.TryLoad(libProjectDir, "Release")!;

        var items = OutputBuilder.CollectEmbeddableItems(
            libProjectDir, libConfig, assemblyName + ".js", $"var {assemblyName.Replace('.', '_')} = 1;", null);

        return EmbedInto(assemblyName, items);
    }

    /// <summary>
    /// The same package built as ES modules (<c>outputBy: Module</c>): it ships the module entry and
    /// one chunk on top of the two bundle variants, which is what every current Tesserae-family
    /// package looks like.
    /// </summary>
    private string ModulePackageDll(string assemblyName)
    {
        var libProjectDir = Path.Combine(_libDir, assemblyName);
        Directory.CreateDirectory(libProjectDir);
        File.WriteAllText(Path.Combine(libProjectDir, "vendor.js"), "// the vendored library");
        File.WriteAllText(Path.Combine(libProjectDir, "tps.json"), $@"{{
            ""fileName"": ""{assemblyName}.js"",
            ""outputBy"": ""Module"",
            ""resources"": [ {{ ""name"": ""{assemblyName}-vendor.js"", ""files"": [ ""vendor.js"" ] }} ]
        }}");
        var libConfig = TransposeJson.TryLoad(libProjectDir, "Release")!;

        var modules = new Emitter.ModuleOutput { EntryJs = "Transpose.Modules.register({});" };
        modules.Chunks.Add(($"chunks/{assemblyName}/c0123456789abcdef.mjs", "var chunk = 1;"));

        var items = OutputBuilder.CollectEmbeddableItems(
            libProjectDir, libConfig, assemblyName + ".js", $"var {assemblyName.Replace('.', '_')} = 1;", null,
            modules: modules);

        return EmbedInto(assemblyName, items);
    }

    /// <summary>
    /// An assembly carrying nothing but authored assets — a vendored script, a stylesheet — and no
    /// compiled output of its own. This is the shape <c>dontLoadReferences</c> is unambiguously for,
    /// so the build must say nothing about it.
    /// </summary>
    private string AssetsOnlyDll(string assemblyName)
    {
        var items = new List<EmbeddedItem>
        {
            new(assemblyName + "-vendor.js", System.Text.Encoding.UTF8.GetBytes("// vendored"), null),
            new(assemblyName + ".css",       System.Text.Encoding.UTF8.GetBytes(".lib { }"),    null),
        };
        return EmbedInto(assemblyName, items);
    }

    private string EmbedInto(string assemblyName, List<EmbeddedItem> items)
    {
        byte[] bytes;
        using (var asm = AssemblyDefinition.CreateAssembly(
                   new AssemblyNameDefinition(assemblyName, new Version(1, 0, 0, 0)), assemblyName, ModuleKind.Dll))
        using (var ms = new MemoryStream())
        {
            asm.Write(ms);
            bytes = ms.ToArray();
        }

        var path = Path.Combine(_libDir, assemblyName + ".dll");
        ResourceEmbedder.Embed(path, bytes, items);
        return path;
    }

    private OutputBuilder.SiteBuildResult Build(TransposeJson config, ResolvedProject project)
        => OutputBuilder.Build(project, config, "var app = 1;", _outputDir, "Debug");

    private string IndexHtml()
    {
        var html = Path.Combine(_outputDir, "index.html");
        Assert.IsTrue(File.Exists(html), "the build must generate index.html");
        return File.ReadAllText(html);
    }

    private void AssertInOutput(string rel, string because)
        => Assert.IsTrue(File.Exists(Path.Combine(_outputDir, rel.Replace('/', Path.DirectorySeparatorChar))), because);

    // ---------------------------------------------------------------- parsing

    [TestMethod]
    public void TheListIsReadFromTpsJsonAndDefaultsToEmpty()
    {
        Assert.AreEqual(0, AppConfig(@"{ ""fileName"": ""app.js"" }").DontLoadReferences.Count,
            "a project that says nothing must load every reference, as it always did");

        var config = AppConfig(@"{
            ""fileName"": ""app.js"",
            ""dontLoadReferences"": [ ""Tesserae.Plotly"", ""Tesserae.GraphKit"" ]
        }");

        CollectionAssert.AreEqual(new[] { "Tesserae.Plotly", "Tesserae.GraphKit" }, config.DontLoadReferences);
    }

    [TestMethod]
    public void AConfigurationOverlayAddsToTheList()
    {
        // Same rule the other list settings follow (resources, cleanOutputFolderExclude): the overlay
        // extends the base rather than replacing it, so a Release-only exclusion is one line.
        File.WriteAllText(Path.Combine(_projectDir, "tps.json"),
            @"{ ""dontLoadReferences"": [ ""Tesserae.Plotly"" ] }");
        File.WriteAllText(Path.Combine(_projectDir, "tps.Release.json"),
            @"{ ""dontLoadReferences"": [ ""Tesserae.GraphKit"" ] }");

        var config = TransposeJson.TryLoad(_projectDir, "Release")!;
        CollectionAssert.AreEqual(new[] { "Tesserae.Plotly", "Tesserae.GraphKit" }, config.DontLoadReferences);
    }

    // ---------------------------------------------------------------- site build

    [TestMethod]
    public void ANamedReferenceIsExtractedButNeverReferencedFromIndexHtml()
    {
        var plotly = PackageDll("Tesserae.Plotly");
        var core = PackageDll("Tesserae");

        var result = Build(AppConfig(@"{
            ""fileName"": ""app.js"",
            ""dontLoadReferences"": [ ""Tesserae.Plotly"" ]
        }"), Project(plotly, core));

        var html = IndexHtml();

        // Every file the suppressed package carries is still on disk — that is what makes it
        // fetchable at run time — and none of it is in index.html.
        AssertInOutput("Tesserae.Plotly.js", "the suppressed package's bundle must still be extracted");
        AssertInOutput("Tesserae.Plotly-vendor.js", "its authored script must still be extracted");
        AssertInOutput("Tesserae.Plotly.css", "its stylesheet must still be extracted");
        Assert.IsFalse(html.Contains("Tesserae.Plotly"), "nothing from a dontLoadReferences assembly may appear in index.html");

        // …while the reference next to it is untouched.
        StringAssert.Contains(html, "src=\"Tesserae.js\"", "an unlisted reference must still be scripted");
        StringAssert.Contains(html, "src=\"Tesserae-vendor.js\"", "its authored script must still be scripted");
        StringAssert.Contains(html, "href=\"Tesserae.css\"", "its stylesheet must still be linked");

        CollectionAssert.AreEqual(new[] { "Tesserae.Plotly" }, result.UnscriptedReferences.ToArray(),
            "the build must report which references it left unscripted");
        Assert.AreEqual(0, result.UnmatchedDontLoadReferences.Count);
    }

    [TestMethod]
    public void TheApplicationsOwnBundleIsUnaffected()
    {
        var plotly = PackageDll("Tesserae.Plotly");
        Build(AppConfig(@"{
            ""fileName"": ""app.js"",
            ""dontLoadReferences"": [ ""Tesserae.Plotly"" ]
        }"), Project(plotly));

        StringAssert.Contains(IndexHtml(), "src=\"app.js\"",
            "the setting names references; the project's own compiled output is loadCompiledOutput's business");
    }

    [TestMethod]
    public void APatternMatchesByWildcardCaseAndTheDllSuffix()
    {
        var plotly = PackageDll("Tesserae.Plotly");
        var graphKit = PackageDll("Tesserae.GraphKit");
        var core = PackageDll("Tesserae");

        var result = Build(AppConfig(@"{
            ""fileName"": ""app.js"",
            ""dontLoadReferences"": [ ""tesserae.PLOTLY"", ""Tesserae.Graph*.dll"" ]
        }"), Project(plotly, graphKit, core));

        var html = IndexHtml();
        Assert.IsFalse(html.Contains("Tesserae.Plotly"), "an assembly name is a name, so matching is case-insensitive");
        Assert.IsFalse(html.Contains("Tesserae.GraphKit"), "a wildcard pattern must match, and a .dll suffix must be tolerated");
        StringAssert.Contains(html, "src=\"Tesserae.js\"", "a prefix must not match the whole namespace by accident");

        CollectionAssert.AreEqual(new[] { "Tesserae.GraphKit", "Tesserae.Plotly" }, result.UnscriptedReferences.ToArray());
    }

    [TestMethod]
    public void AnEntryThatMatchesNoReferenceIsReported()
    {
        // The silent failure mode this guards: a typo, or a dependency that has since been dropped,
        // leaves the library loading exactly as if the setting had never been written.
        var core = PackageDll("Tesserae");

        var result = Build(AppConfig(@"{
            ""fileName"": ""app.js"",
            ""dontLoadReferences"": [ ""Tesserae.Plotly"", ""Tesserae"" ]
        }"), Project(core));

        CollectionAssert.AreEqual(new[] { "Tesserae.Plotly" }, result.UnmatchedDontLoadReferences.ToArray());
        CollectionAssert.AreEqual(new[] { "Tesserae" }, result.UnscriptedReferences.ToArray());
    }

    // ------------------------------------------------- what the deferred assembly actually ships

    [TestMethod]
    public void DeferringAModuleBuiltPackageIsReported()
    {
        // The failure this exists to catch, end to end. A package built as ES modules already defers
        // its own code — its chunks are fetched on demand and its entry does not import them — so the
        // only thing this setting keeps off the page is the entry, which is what calls
        // Transpose.Modules.register. Without those stubs the namespace object the types are placed
        // on never exists, and the first reference to one dies on a bare ReferenceError.
        var graphKit = ModulePackageDll("Tesserae.GraphKit");

        var result = Build(AppConfig(@"{
            ""fileName"": ""app.js"",
            ""dontLoadReferences"": [ ""Tesserae.GraphKit"" ]
        }"), Project(graphKit));

        CollectionAssert.AreEqual(
            new[] { new OutputBuilder.DeferredReference("Tesserae.GraphKit", "Tesserae.GraphKit.js") },
            result.DeferredModuleReferences.ToArray(),
            "a deferred module package must be reported, named with the file the application has to load instead");
        Assert.AreEqual(0, result.DeferredCompiledReferences.Count,
            "it is reported once, as the module case — not also as a plain bundle");

        // Reported, not refused: the files are still extracted and the site still builds, because an
        // application that does load the entry itself is a working configuration.
        AssertInOutput("Tesserae.GraphKit.js", "the entry module must still be extracted, under its site name");
        Assert.IsFalse(IndexHtml().Contains("Tesserae.GraphKit"), "and still kept out of index.html");
    }

    [TestMethod]
    public void DeferringASingleBundlePackageIsReportedSeparately()
    {
        // The case the setting exists for (a chart binding one screen needs). Still worth saying once
        // per build: nothing loads the bundle automatically, and forgetting to fails the same way.
        var plotly = PackageDll("Tesserae.Plotly");

        var result = Build(AppConfig(@"{
            ""fileName"": ""app.js"",
            ""dontLoadReferences"": [ ""Tesserae.Plotly"" ]
        }"), Project(plotly));

        CollectionAssert.AreEqual(
            new[] { new OutputBuilder.DeferredReference("Tesserae.Plotly", "Tesserae.Plotly.js") },
            result.DeferredCompiledReferences.ToArray());
        Assert.AreEqual(0, result.DeferredModuleReferences.Count);
    }

    [TestMethod]
    public void DeferringAnAssetsOnlyAssemblySaysNothing()
    {
        // No compiled code of its own means nothing can reference one of its types, so there is no
        // failure to warn about — this is what the setting is unambiguously for.
        var icons = AssetsOnlyDll("Some.Icons");

        var result = Build(AppConfig(@"{
            ""fileName"": ""app.js"",
            ""dontLoadReferences"": [ ""Some.Icons"" ]
        }"), Project(icons));

        CollectionAssert.AreEqual(new[] { "Some.Icons" }, result.UnscriptedReferences.ToArray());
        Assert.AreEqual(0, result.DeferredModuleReferences.Count);
        Assert.AreEqual(0, result.DeferredCompiledReferences.Count);
    }

    [TestMethod]
    public void AScriptedReferenceIsNeverReportedAsDeferred()
    {
        var graphKit = ModulePackageDll("Tesserae.GraphKit");
        var plotly = PackageDll("Tesserae.Plotly");

        var result = Build(AppConfig(@"{ ""fileName"": ""app.js"" }"), Project(graphKit, plotly));

        Assert.AreEqual(0, result.DeferredModuleReferences.Count);
        Assert.AreEqual(0, result.DeferredCompiledReferences.Count);
    }

    [TestMethod]
    public void AReferencedProjectDllIsSuppressedTheSameWay()
    {
        // The same reference consumed as a built project output (--separate-assemblies, which is how
        // the SDK builds a multi-project app) goes through a different extractor. Both must obey.
        var plotly = PackageDll("Tesserae.Plotly");
        var project = Project(plotly);
        project.ReferencedProjectDlls.Add(plotly);

        Build(AppConfig(@"{
            ""fileName"": ""app.js"",
            ""dontLoadReferences"": [ ""Tesserae.Plotly"" ]
        }"), project);

        AssertInOutput("Tesserae.Plotly.js", "a suppressed project DLL's bundle must still be extracted");
        AssertInOutput("Tesserae.Plotly.css", "its stylesheet must still be extracted");
        Assert.IsFalse(IndexHtml().Contains("Tesserae.Plotly"), "and none of it may be referenced from index.html");
    }
}
