using System.Text.Json;
using Mono.Cecil;
using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Covers the <c>tps.json</c> resource <c>load</c> flag: a resource group declaring
/// <c>"load": false</c> is copied into the output (and embedded into the package DLL) but never
/// referenced from the generated <c>index.html</c> — for every resource kind the HTML can load, i.e.
/// scripts and stylesheets alike.
///
/// It is the declarative form of the legacy <c>.dontload</c> name suffix, which keeps working; both
/// resolve in one place (<see cref="OutputBuilder.ResolveResource"/>) and AND together. Crucially the
/// resolved flag is what gets written into the package DLL's <c>Transpose.Resources.json</c> manifest,
/// so it survives packing: a project *referencing* the package extracts the file but does not inject
/// it into its own index.html either.
/// </summary>
[TestClass]
public sealed class ResourceLoadFlagTests
{
    private string _root = "";
    private string _projectDir = "";
    private string _outputDir = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "tps-load-" + Guid.NewGuid().ToString("N"));
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

    private string Asset(string rel, string content)
    {
        var full = Path.Combine(_projectDir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
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

    /// <summary>Assembles the site for <paramref name="config"/> and returns the generated
    /// index.html.</summary>
    private string BuildSite(TransposeJson config, ResolvedProject? project = null)
    {
        OutputBuilder.Build(project ?? Project(), config, "var app = 1;", _outputDir, "Debug");
        var html = Path.Combine(_outputDir, "index.html");
        Assert.IsTrue(File.Exists(html), "the build must generate index.html");
        return File.ReadAllText(html);
    }

    private void AssertInOutput(string rel, string because)
        => Assert.IsTrue(File.Exists(Path.Combine(_outputDir, rel.Replace('/', Path.DirectorySeparatorChar))), because);

    // ---------------------------------------------------------------- parsing

    [TestMethod]
    public void TheLoadFlagIsReadFromTpsJsonAndDefaultsToTrue()
    {
        var config = Config(@"{
            ""resources"": [
                { ""name"": ""loaded.js"",  ""files"": [ ""a.js"" ] },
                { ""name"": ""quiet.js"",   ""files"": [ ""a.js"" ], ""load"": false },
                { ""name"": ""spelled.js"", ""files"": [ ""a.js"" ], ""load"": true }
            ]
        }");

        Assert.IsTrue(config.Resources[0].Load, "an absent load flag must default to true");
        Assert.IsFalse(config.Resources[1].Load);
        Assert.IsTrue(config.Resources[2].Load);
    }

    [TestMethod]
    public void ResolveResourceCombinesTheFlagWithTheLegacyDontloadSuffix()
    {
        // Either spelling alone suppresses the injection, and they compose; the output name never
        // carries the suffix or the "module#" grouping prefix.
        Assert.AreEqual(("x.js", true), Resolve("x.js", load: true));
        Assert.AreEqual(("x.js", false), Resolve("x.js", load: false));
        Assert.AreEqual(("x.js", false), Resolve("x.js.dontload", load: true));
        Assert.AreEqual(("x.js", false), Resolve("x.js.dontload", load: false));
        Assert.AreEqual(("x.js", false), Resolve("mod#x.js", load: false));

        static (string, bool) Resolve(string name, bool load)
            => OutputBuilder.ResolveResource(new TransposeJson.ResourceGroup { Name = name, Load = load });
    }

    // ---------------------------------------------------------------- site build

    [TestMethod]
    public void ANonLoadedScriptIsWrittenToTheOutputButNotReferencedFromIndexHtml()
    {
        Asset("assets/lazy.js", "// lazy module");
        Asset("assets/eager.js", "// eager module");

        var html = BuildSite(Config(@"{
            ""fileName"": ""app.js"",
            ""resources"": [
                { ""name"": ""eager.js"", ""files"": [ ""assets/eager.js"" ] },
                { ""name"": ""lazy.js"",  ""files"": [ ""assets/lazy.js"" ], ""load"": false }
            ]
        }"));

        AssertInOutput("lazy.js", "a non-loaded resource must still be copied to the output");
        AssertInOutput("eager.js", "a loaded resource must be copied to the output");
        StringAssert.Contains(html, "src=\"eager.js\"", "a loaded script must be injected");
        Assert.IsFalse(html.Contains("lazy.js"), "a non-loaded script must not appear in index.html");
    }

    [TestMethod]
    public void ANonLoadedStylesheetIsWrittenToTheOutputButNotLinkedFromIndexHtml()
    {
        Asset("assets/theme-dark.css", "body { color: #fff }");
        Asset("assets/site.css", "body { color: #000 }");

        var html = BuildSite(Config(@"{
            ""fileName"": ""app.js"",
            ""resources"": [
                { ""name"": ""site.css"",       ""files"": [ ""assets/site.css"" ] },
                { ""name"": ""theme-dark.css"", ""files"": [ ""assets/theme-dark.css"" ], ""load"": false }
            ]
        }"));

        AssertInOutput("theme-dark.css", "a non-loaded stylesheet must still be copied to the output");
        StringAssert.Contains(html, "href=\"site.css\"", "a loaded stylesheet must be linked");
        Assert.IsFalse(html.Contains("theme-dark.css"), "a non-loaded stylesheet must not be linked from index.html");
    }

    [TestMethod]
    public void ANonLoadedCopyThroughGroupCopiesEveryFileWithoutReferencingAny()
    {
        // A group whose name is not a bundle name copies each matched file under its own name — the
        // shape used for globbed assets. The flag has to hold for every file the glob produced,
        // whatever its type.
        Asset("vendor/one.js", "// one");
        Asset("vendor/two.css", ".two { }");

        var html = BuildSite(Config(@"{
            ""fileName"": ""app.js"",
            ""resources"": [
                { ""name"": ""vendor"", ""files"": [ ""vendor/*.js"", ""vendor/*.css"" ], ""output"": ""vendor"", ""load"": false }
            ]
        }"));

        AssertInOutput("vendor/one.js", "a non-loaded copy-through file must still be copied");
        AssertInOutput("vendor/two.css", "a non-loaded copy-through file must still be copied");
        Assert.IsFalse(html.Contains("one.js"), "a non-loaded script must not appear in index.html");
        Assert.IsFalse(html.Contains("two.css"), "a non-loaded stylesheet must not appear in index.html");
    }

    [TestMethod]
    public void TheLegacyDontloadSuffixStillSuppressesTheInjection()
    {
        Asset("assets/lazy.js", "// lazy module");

        var html = BuildSite(Config(@"{
            ""fileName"": ""app.js"",
            ""resources"": [
                { ""name"": ""lazy.js.dontload"", ""files"": [ ""assets/lazy.js"" ] }
            ]
        }"));

        AssertInOutput("lazy.js", "the .dontload suffix must be stripped from the output name");
        Assert.IsFalse(html.Contains("lazy.js"), "a .dontload script must not appear in index.html");
    }

    // ---------------------------------------------------------------- packing

    [TestMethod]
    public void TheFlagIsRecordedInTheEmbeddedResourceManifest()
    {
        Asset("assets/lazy.js", "// lazy module");
        Asset("assets/site.css", "body { }");

        var config = Config(@"{
            ""fileName"": ""Lib.js"",
            ""resources"": [
                { ""name"": ""site.css"", ""files"": [ ""assets/site.css"" ] },
                { ""name"": ""lazy.js"",  ""files"": [ ""assets/lazy.js"" ], ""load"": false }
            ]
        }");

        var items = OutputBuilder.CollectEmbeddableItems(_projectDir, config, "Lib.js", "var lib = 1;", null);

        Assert.IsFalse(items.Single(i => i.Name == "lazy.js").Load, "the flag must reach the embedded item");
        Assert.IsTrue(items.Single(i => i.Name == "site.css").Load);
        Assert.IsTrue(items.Single(i => i.Name == "Lib.js").Load, "the compiled bundle itself is always loaded");

        // …and out the other side, in the manifest a consumer reads.
        var dll = PackageDll("Lib", items);
        var manifest = ManifestEntries(dll);
        Assert.IsFalse(manifest["lazy.js"], "the manifest must record Load: false");
        Assert.IsTrue(manifest["site.css"]);
    }

    [TestMethod]
    public void AReferencingSiteBuildExtractsANonLoadedPackageResourceWithoutReferencingIt()
    {
        // The end of the chain: the library's tps.json says load: false, the package DLL carries that,
        // and a *different* project referencing the package copies the files into its site but leaves
        // them out of its own index.html — for a script and a stylesheet alike.
        Asset("assets/lazy.js", "// lazy module");
        Asset("assets/theme-dark.css", "body { color: #fff }");
        Asset("assets/site.css", "body { color: #000 }");

        var libConfig = Config(@"{
            ""fileName"": ""Lib.js"",
            ""resources"": [
                { ""name"": ""site.css"",       ""files"": [ ""assets/site.css"" ] },
                { ""name"": ""lazy.js"",        ""files"": [ ""assets/lazy.js"" ], ""load"": false },
                { ""name"": ""theme-dark.css"", ""files"": [ ""assets/theme-dark.css"" ], ""load"": false }
            ]
        }");

        var dll = PackageDll("Lib", OutputBuilder.CollectEmbeddableItems(_projectDir, libConfig, "Lib.js", "var lib = 1;", null));

        // The consuming project has no resources of its own — everything below comes from the package.
        var appConfig = Config(@"{ ""fileName"": ""app.js"" }");
        var html = BuildSite(appConfig, Project(dll));

        AssertInOutput("lazy.js", "a non-loaded package script must still be extracted");
        AssertInOutput("theme-dark.css", "a non-loaded package stylesheet must still be extracted");
        AssertInOutput("site.css", "a loaded package stylesheet must be extracted");

        StringAssert.Contains(html, "src=\"Lib.js\"", "the package's compiled bundle must load");
        StringAssert.Contains(html, "href=\"site.css\"", "a loaded package stylesheet must be linked");
        Assert.IsFalse(html.Contains("lazy.js"), "a non-loaded package script must not appear in index.html");
        Assert.IsFalse(html.Contains("theme-dark.css"), "a non-loaded package stylesheet must not be linked");
    }

    /// <summary>Embeds <paramref name="items"/> into an assembly through the real
    /// <see cref="ResourceEmbedder"/> — the same call <c>tps --emit-package</c> makes — and returns the
    /// package DLL's path. The assembly itself is an empty one built here rather than a compiled
    /// snippet: what is under test is the resource manifest, so the test stays independent of a
    /// bootstrapped <c>Transpose.dll</c>.</summary>
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

    /// <summary>The package's <c>Transpose.Resources.json</c> manifest, as file name → Load.</summary>
    private static Dictionary<string, bool> ManifestEntries(string dllPath)
    {
        using var asm = AssemblyDefinition.ReadAssembly(dllPath);
        var manifest = asm.MainModule.Resources.OfType<EmbeddedResource>().Single(r => r.Name == "Transpose.Resources.json");
        using var doc = JsonDocument.Parse(manifest.GetResourceData());
        return doc.RootElement.EnumerateArray().ToDictionary(
            e => e.GetProperty("FileName").GetString()!,
            e => e.GetProperty("Load").GetBoolean());
    }
}
