using Mono.Cecil;
using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Covers which name a referenced package's embedded JavaScript lands under in a site build — the
/// <c>.js</c> / <c>.min.js</c> switch in <c>OutputBuilder.RoutePackageJs</c>, driven by the build's
/// configuration (see <c>JsOutputProfile</c>).
///
/// The rule the reported bug broke: the switch applies only to a file the package ships in BOTH
/// variants (the compiled bundle, or an authored bundle a library deliberately declares twice). An
/// authored resource that exists in one variant only — Monaco's <c>editor.main.js</c>, a vendored
/// <c>d3.min.js</c> — has no other variant to switch to and must be copied through under its own name
/// in every configuration. It was instead renamed to <c>editor.main.min.js</c> in a Minified build
/// (and a standalone <c>d3.min.js</c> was dropped entirely from a Formatted one), so Monaco's loader —
/// which fetches <c>vs/editor/editor.main.js</c> by path — 404'd in Release.
/// </summary>
[TestClass]
public sealed class PackageResourceVariantTests
{
    private string _root = "";
    private string _libDir = "";
    private string _appDir = "";
    private string _outputDir = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "tps-pkgres-" + Guid.NewGuid().ToString("N"));
        _libDir = Path.Combine(_root, "lib");
        _appDir = Path.Combine(_root, "app");
        _outputDir = Path.Combine(_root, "site");
        Directory.CreateDirectory(_libDir);
        Directory.CreateDirectory(_appDir);
        Directory.CreateDirectory(_outputDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    // ---------------------------------------------------------------- the library package

    /// <summary>Builds the package a consumer references: a library whose tps.json embeds the same
    /// shapes the Curiosity front-end does — Monaco's single-variant <c>editor.main.js</c>, a
    /// single-variant vendored <c>d3.min.js</c>, and an authored bundle declared in both variants —
    /// alongside its own compiled bundle (which the compiler embeds formatted and pre-minified).</summary>
    private string LibraryPackage()
    {
        Asset(_libDir, "tps/assets/js/monaco/vs/editor/editor.main.js", "// monaco editor.main");
        Asset(_libDir, "tps/assets/js/d3.min.js", "// vendored d3, already minified");
        Asset(_libDir, "tps/assets/js/vendor-one.js", "// vendor one");

        File.WriteAllText(Path.Combine(_libDir, "tps.json"), @"{
            ""fileName"": ""Lib.js"",
            ""resources"": [
                {
                    ""name"": ""monaco#editor.main.js.dontload"",
                    ""files"": [ ""tps/assets/js/monaco/vs/editor/editor.main.js"" ],
                    ""output"": ""assets/js/monaco/vs/editor/""
                },
                {
                    ""name"": ""d3.min.js.dontload"",
                    ""files"": [ ""tps/assets/js/d3.min.js"" ],
                    ""output"": ""assets/js""
                },
                {
                    ""name"": ""Lib.ExternalBundle.js"",
                    ""files"": [ ""tps/assets/js/vendor-one.js"" ],
                    ""output"": ""assets/js""
                },
                {
                    ""name"": ""Lib.ExternalBundle.min.js"",
                    ""files"": [ ""tps/assets/js/vendor-one.js"" ],
                    ""output"": ""assets/js""
                }
            ]
        }");

        var config = TransposeJson.TryLoad(_libDir, "Release");
        Assert.IsNotNull(config, "the library's tps.json must load");

        return PackageDll("Lib", OutputBuilder.CollectEmbeddableItems(_libDir, config!, "Lib.js", "var lib = 1;", null));
    }

    // ---------------------------------------------------------------- the tests

    [TestMethod]
    public void AMinifiedBuildKeepsASingleVariantPackageResourceUnderItsAuthoredName()
    {
        var html = BuildSite(LibraryPackage(), "Release");

        AssertInOutput("assets/js/monaco/vs/editor/editor.main.js",
            "an authored package resource must keep the name it was authored with — Monaco's loader fetches it by path");
        AssertNotInOutput("assets/js/monaco/vs/editor/editor.main.min.js",
            "the compiler must not rename an authored resource it never minified");

        // The same file, unchanged: a resource is copied through, never run through the JS minifier.
        Assert.AreEqual("// monaco editor.main",
            File.ReadAllText(Path.Combine(_outputDir, "assets", "js", "monaco", "vs", "editor", "editor.main.js")));

        Assert.IsFalse(html.Contains("editor.main"), "a .dontload resource is on disk but never in index.html");
    }

    [TestMethod]
    public void AFormattedBuildStillWritesASingleVariantMinifiedPackageResource()
    {
        // The mirror image: a vendored library that only ever existed as `d3.min.js` is not a Release
        // variant of anything, so a Formatted build must still put it on disk — the app lazy-loads it
        // by that path in every configuration.
        BuildSite(LibraryPackage(), "Debug");

        AssertInOutput("assets/js/d3.min.js", "a standalone .min.js resource must be extracted in a Formatted build too");
        AssertNotInOutput("assets/js/d3.js", "and must not be renamed to a formatted variant that does not exist");
    }

    [TestMethod]
    public void AResourceDeclaredInBothVariantsStillSwitchesPerConfiguration()
    {
        // A library that deliberately ships both variants of an authored bundle keeps the legacy
        // behaviour: the minified build takes the .min.js, the formatted build takes the .js.
        var dll = LibraryPackage();

        BuildSite(dll, "Release");
        AssertInOutput("assets/js/Lib.ExternalBundle.min.js", "a declared .min.js variant is what a Minified build writes");
        AssertNotInOutput("assets/js/Lib.ExternalBundle.js", "…and the formatted variant is not materialised");

        Directory.Delete(_outputDir, recursive: true);
        Directory.CreateDirectory(_outputDir);

        BuildSite(dll, "Debug");
        AssertInOutput("assets/js/Lib.ExternalBundle.js", "a Formatted build writes the formatted variant");
        AssertNotInOutput("assets/js/Lib.ExternalBundle.min.js", "…and not the minified one");
    }

    [TestMethod]
    public void ThePackagesCompiledBundleStillTakesItsPreMinifiedVariant()
    {
        // Unchanged, and the reason the switch exists at all: the library's own compiled JS ships in
        // both variants, so a Minified consumer build links the pre-minified one.
        var html = BuildSite(LibraryPackage(), "Release");

        AssertInOutput("Lib.min.js", "the package's pre-minified bundle is what a Minified build writes");
        AssertNotInOutput("Lib.js", "…and the formatted bundle is not materialised");
        StringAssert.Contains(html, "src=\"Lib.min.js\"", "index.html must link the minified bundle in Release");
    }

    // ------------------------------------------- a package ships every variant; the consumer picks

    /// <summary>A module-mode library, i.e. one that emits chunks. It embeds all three shapes of its
    /// own compiled code, because it cannot know how it will be consumed.</summary>
    private string ModuleLibraryPackage()
    {
        File.WriteAllText(Path.Combine(_libDir, "tps.json"), @"{ ""fileName"": ""Lib.js"", ""outputBy"": ""Module"" }");
        var config = TransposeJson.TryLoad(_libDir, "Release");
        Assert.IsNotNull(config);

        var modules = new Emitter.ModuleOutput { EntryJs = "import './chunks/Lib/c0.mjs';\n" };
        modules.Chunks.Add(("chunks/Lib/c0.mjs", "/* chunk zero */"));

        return PackageDll("Lib", OutputBuilder.CollectEmbeddableItems(
            _libDir, config!, "Lib.js", "var lib = 1;", "var libMeta = 1;", modules: modules));
    }

    [TestMethod]
    public void APackageShipsEveryVariantOfItsOwnCompiledCode()
    {
        using var asm = AssemblyDefinition.ReadAssembly(ModuleLibraryPackage());
        var names = asm.MainModule.Resources.Select(r => r.Name).ToList();

        // A library is built once and consumed by applications that are not: an application being
        // debugged wants one readable bundle, a shipping one wants minified or chunked, and neither
        // should require the library to be rebuilt. So all of it travels.
        CollectionAssert.Contains(names, "Lib.js",          "the formatted bundle");
        CollectionAssert.Contains(names, "Lib.min.js",      "the minified bundle");
        CollectionAssert.Contains(names, "Lib.meta.js",     "the formatted reflection metadata");
        CollectionAssert.Contains(names, "Lib.meta.min.js", "the minified reflection metadata");
        CollectionAssert.Contains(names, "Lib.mjs",         "the module entry, under a name of its own");
        CollectionAssert.Contains(names, "c0.mjs",          "and its chunk files");
    }

    [TestMethod]
    public void ADebugConsumerTakesThePackagesFormattedBundleAndNoChunks()
    {
        var html = BuildSite(ModuleLibraryPackage(), "Debug");

        AssertInOutput("Lib.js", "a Debug build takes the readable bundle");
        Assert.AreEqual("var lib = 1;", File.ReadAllText(Path.Combine(_outputDir, "Lib.js")),
            "…the bundle itself, not the module entry that happens to land under the same name in Release");
        AssertNotInOutput("Lib.min.js", "…not the minified one");
        AssertNotInOutput("chunks/Lib/c0.mjs",
            "…and none of the chunks: a Debug build is not chunked, so they would be dead weight");
        StringAssert.Contains(html, "src=\"Lib.js\" defer", "and it is scripted as a classic bundle");
    }

    [TestMethod]
    public void AReleaseConsumerThatIsNotChunkedTakesThePackagesMinifiedBundle()
    {
        var html = BuildSite(ModuleLibraryPackage(), "Release");

        AssertInOutput("Lib.min.js", "an unchunked Release build takes the minified bundle");
        AssertNotInOutput("Lib.js", "…not the formatted one");
        AssertNotInOutput("chunks/Lib/c0.mjs", "…and not the chunks, which it has no entry module to reach");
        StringAssert.Contains(html, "src=\"Lib.min.js\"", "index.html links the minified bundle");
    }

    [TestMethod]
    public void AChunkedReleaseConsumerTakesThePackagesModuleEntryAndChunks()
    {
        var modules = new Emitter.ModuleOutput { EntryJs = "// app entry" };
        var html = BuildSite(ModuleLibraryPackage(), "Release", modules);

        AssertInOutput("chunks/Lib/c0.mjs", "a chunked consumer takes the package's chunks");
        // The entry lands under the bundle's name: three variants need three names inside the DLL and
        // one name in the site, because application code fetches it by that name.
        AssertInOutput("Lib.js", "…and its module entry, under the name a consumer's own code fetches");
        Assert.AreEqual("import './chunks/Lib/c0.mjs';\n", File.ReadAllText(Path.Combine(_outputDir, "Lib.js")));
        AssertNotInOutput("Lib.min.js", "…and neither classic bundle");
        AssertNotInOutput("Lib.mjs", "the in-DLL name of the entry is not a name the site uses");
        StringAssert.Contains(html, "<script type=\"module\" src=\"Lib.js\">",
            "the entry has to be scripted as a module — it carries import statements");
    }

    [TestMethod]
    public void AChunkedConsumerFallsBackToTheBundleOfAPackageThatHasNoChunks()
    {
        // Not every dependency is module-mode (a binding library, a vendored package). A chunked app
        // still has to load those, and the minified bundle is what it takes.
        var modules = new Emitter.ModuleOutput { EntryJs = "// app entry" };
        BuildSite(LibraryPackage(), "Release", modules);

        AssertInOutput("Lib.min.js", "a package with no module variant contributes its minified bundle");
    }

    // ---------------------------------------------------------------- helpers

    private static string Asset(string dir, string rel, string content)
    {
        var full = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    /// <summary>Builds a consuming project that has no resources of its own — everything in the site
    /// comes from <paramref name="packageDll"/> — and returns its index.html.</summary>
    private string BuildSite(string packageDll, string configuration, Emitter.ModuleOutput? modules = null)
    {
        File.WriteAllText(Path.Combine(_appDir, "tps.json"), @"{ ""fileName"": ""app.js"" }");

        var config = TransposeJson.TryLoad(_appDir, configuration);
        Assert.IsNotNull(config, "the app's tps.json must load");

        var project = new ResolvedProject
        {
            CsprojPath      = Path.Combine(_appDir, "App.csproj"),
            ProjectDir      = _appDir,
            AssemblyName    = "App",
            TargetFramework = "netstandard2.0",
            Sources         = new List<(string, string)>(),
            ReferencePaths  = new List<string> { packageDll },
            DefineConstants = new List<string>(),
            LanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest,
            ProjectDirs     = new List<string> { _appDir },
        };

        OutputBuilder.Build(project, config!, modules?.EntryJs ?? "var app = 1;", _outputDir, configuration,
                            modules: modules);

        var html = Path.Combine(_outputDir, "index.html");
        Assert.IsTrue(File.Exists(html), "the build must generate index.html");
        return File.ReadAllText(html);
    }

    private void AssertInOutput(string rel, string because)
        => Assert.IsTrue(File.Exists(Path.Combine(_outputDir, rel.Replace('/', Path.DirectorySeparatorChar))), because);

    private void AssertNotInOutput(string rel, string because)
        => Assert.IsFalse(File.Exists(Path.Combine(_outputDir, rel.Replace('/', Path.DirectorySeparatorChar))), because);

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
}
