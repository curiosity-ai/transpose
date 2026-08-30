using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mono.Cecil;
using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// The cross-assembly half of <c>outputBy: Module</c>: what a module-mode <em>package</em> embeds,
/// and what a consuming site build does with it.
///
/// A library has no entry point to be lazy relative to, so it defers everything and publishes a chunk
/// map — emitted type name → the chunk file that defines it. Its consumer reads that map and, for
/// every library type its own code reaches into, imports the chunk behind it. Without that the
/// reference would resolve to the library's stub, and a stub cannot be resolved synchronously.
/// </summary>
[TestClass]
public sealed class ModulePackageTests
{
    private string _root = "", _appDir = "", _outputDir = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "tps-modpkg-" + Guid.NewGuid().ToString("N"));
        _appDir = Path.Combine(_root, "app");
        _outputDir = Path.Combine(_root, "site");
        Directory.CreateDirectory(_appDir);
        Directory.CreateDirectory(_outputDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [TestMethod]
    public void APackagesChunksAreCopiedThroughAndItsEntryIsScriptedAsAModule()
    {
        var dll = LibraryPackage(out _);
        var html = BuildSite(dll);

        // The entry is the only thing index.html scripts, and it is scripted as a module because it
        // carries `import` statements.
        StringAssert.Contains(html, "<script type=\"module\" src=\"Lib.js\"></script>");
        // The chunks are on disk for those imports (and for Transpose.Modules) to fetch...
        AssertInOutput("chunks/Lib/c0.mjs");
        AssertInOutput("chunks/Lib/c1.mjs");
        // ...but are never scripted: a chunk that loaded itself would defeat the split.
        Assert.IsFalse(html.Contains("c0.mjs"), "a chunk file must not be referenced from index.html");
        Assert.IsFalse(html.Contains("c1.mjs"), "a chunk file must not be referenced from index.html");
    }

    [TestMethod]
    public void APackagePublishesItsChunkMapForConsumersToImportFrom()
    {
        var dll = LibraryPackage(out var map);

        // Read back exactly the way a consuming build does.
        var read = ModuleMap.ReadOne(dll)!;
        CollectionAssert.AreEquivalent(map.Keys.ToList(), read.Keys.ToList());
        foreach (var kv in map) Assert.AreEqual(kv.Value, read[kv.Key]);
    }

    [TestMethod]
    public void TheChunkMapIsNotExtractedIntoTheSite()
    {
        var dll = LibraryPackage(out _);
        BuildSite(dll);

        // It is build metadata for the next compiler, not a web resource — like the BuildStamp, it is
        // embedded but deliberately absent from Transpose.Resources.json, so nothing extracts it.
        AssertNotInOutput("Transpose.Modules.json");
        AssertNotInOutput("chunks/Lib/Transpose.Modules.json");
    }

    [TestMethod]
    public void AReferenceWithNoChunkMapContributesNothing()
    {
        // An ordinary single-bundle package: reading its (absent) map must be empty rather than
        // throwing, so a site can mix module-mode and single-bundle references.
        var plain = PackageDll("Plain", new List<EmbeddedItem> { new("Plain.js", System.Text.Encoding.UTF8.GetBytes("var p = 1;"), null) }, moduleMap: null);
        Assert.IsNull(ModuleMap.ReadOne(plain));
    }

    [TestMethod]
    public void MapsFromSeveralReferencesMerge()
    {
        // The site build accumulates one lookup as it places each reference in dependency order, which
        // is what lets a package's placeholder name a type from any of them.
        var linker = new ModuleLinker();
        linker.LinkAssembly(new Dictionary<string, string> { ["A.One"] = "chunks/A/c0.mjs" }, Array.Empty<ModuleFile>());
        linker.LinkAssembly(new Dictionary<string, string> { ["B.Two"] = "chunks/B/c3.mjs" }, Array.Empty<ModuleFile>());

        Assert.AreEqual(2, linker.TypeToChunk.Count);
        Assert.AreEqual("chunks/A/c0.mjs", linker.TypeToChunk["A.One"]);
        Assert.AreEqual("chunks/B/c3.mjs", linker.TypeToChunk["B.Two"]);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A library package shaped exactly like one <c>tps --emit-package</c> produces in module
    /// mode: two chunk files under its own per-assembly folder (never scripted), a module entry, and
    /// the chunk map.</summary>
    private string LibraryPackage(out Dictionary<string, string> map)
    {
        var utf8 = new System.Text.UTF8Encoding(false);
        map = new Dictionary<string, string>
        {
            ["Lib.Widget"] = "chunks/Lib/c0.mjs",
            ["Lib.Gadget"] = "chunks/Lib/c1.mjs",
        };
        var items = new List<EmbeddedItem>
        {
            new("c0.mjs", utf8.GetBytes("Transpose.$useAssembly(\"Lib\");\nTranspose.define(\"Lib.Widget\", {});\n"), "chunks/Lib", Load: false),
            new("c1.mjs", utf8.GetBytes("import './c0.mjs';\nTranspose.$useAssembly(\"Lib\");\nTranspose.define(\"Lib.Gadget\", {});\n"), "chunks/Lib", Load: false),
            new("Lib.js", utf8.GetBytes("Transpose.Modules.register({});\nTranspose.init();\n"), null, Load: true, Module: true),
        };
        return PackageDll("Lib", items, map);
    }

    private string PackageDll(string assemblyName, IReadOnlyList<EmbeddedItem> items, IReadOnlyDictionary<string, string>? moduleMap)
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
        ResourceEmbedder.Embed(path, bytes, items, referencePaths: null, moduleMap: moduleMap);
        return path;
    }

    private string BuildSite(string packageDll)
    {
        File.WriteAllText(Path.Combine(_appDir, "tps.json"), @"{ ""fileName"": ""app.js"" }");
        var config = TransposeJson.TryLoad(_appDir, "Debug");
        Assert.IsNotNull(config);

        var project = new ResolvedProject
        {
            CsprojPath = Path.Combine(_appDir, "App.csproj"),
            ProjectDir = _appDir,
            AssemblyName = "App",
            TargetFramework = "netstandard2.0",
            Sources = new List<(string, string)>(),
            ReferencePaths = new List<string> { packageDll },
            DefineConstants = new List<string>(),
            LanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest,
            ProjectDirs = new List<string> { _appDir },
        };

        OutputBuilder.Build(project, config!, "var app = 1;", _outputDir, "Debug");
        var html = Path.Combine(_outputDir, "index.html");
        Assert.IsTrue(File.Exists(html), "the build must generate index.html");
        return File.ReadAllText(html);
    }

    private void AssertInOutput(string rel) => Assert.IsTrue(
        File.Exists(Path.Combine(_outputDir, rel.Replace('/', Path.DirectorySeparatorChar))), rel + " should be in the site");

    private void AssertNotInOutput(string rel) => Assert.IsFalse(
        File.Exists(Path.Combine(_outputDir, rel.Replace('/', Path.DirectorySeparatorChar))), rel + " should NOT be in the site");
}
