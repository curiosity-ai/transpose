using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Cross-assembly module imports: a package names the <em>type</em> it needs, and the site build
/// resolves that to the chunk defining it in the build of the library it is actually assembling.
///
/// <para>The bug this exists for: a chunk's file name is the hash of its own text, so every rebuild of
/// a library renames the chunks whose JavaScript changed. When a package wrote its dependency's file
/// names into its own chunks, updating that dependency alone — Tesserae under an unchanged
/// Tesserae.GraphKit — left the package importing chunks that no longer existed. Nothing about the
/// package had changed, so nothing rebuilt it and nothing warned; the application 404'd on the first
/// screen that needed it.</para>
/// </summary>
[TestClass]
public sealed class ModuleLinkTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "tps-modlink-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    // ------------------------------------------------------------------ the import block itself

    [TestMethod]
    public void TheLeadingImportBlockIsReadFromFormattedAndMinifiedText()
    {
        var formatted = "/** banner */\nimport './c1.mjs';\nimport 'tps-type:Lib.Widget';\nTranspose.define(\"A\", {});\n";
        CollectionAssert.AreEqual(new[] { "./c1.mjs", "tps-type:Lib.Widget" },
            ModuleSpecifier.ReadLeading(formatted).Select(i => i.Specifier).ToArray());

        // A package embeds its chunks already minified, so the parser has to read that shape too.
        var minified = "import\"./c1.mjs\";import\"tps-type:Lib.Widget\";Transpose.define(\"A\",{});";
        CollectionAssert.AreEqual(new[] { "./c1.mjs", "tps-type:Lib.Widget" },
            ModuleSpecifier.ReadLeading(minified).Select(i => i.Specifier).ToArray());
    }

    [TestMethod]
    public void ReadingStopsAtTheFirstStatementThatIsNotAnImport()
    {
        // Anything after the import block is code, and a string in it must never be mistaken for a
        // specifier — which is the whole reason only the LEADING block is read.
        var js = "import './c1.mjs';\nvar s = \"import './evil.mjs';\";\n";
        CollectionAssert.AreEqual(new[] { "./c1.mjs" },
            ModuleSpecifier.ReadLeading(js).Select(i => i.Specifier).ToArray());
    }

    [TestMethod]
    public void RewritingLeavesAFileThatChangesNothingByteIdentical()
    {
        // What lets a package built before placeholders existed — real paths already in it — pass
        // through the linker untouched.
        var js = "import './c1.mjs';\nTranspose.define(\"A\", {});\n";
        Assert.AreEqual(js, ModuleSpecifier.RewriteLeadingImports(js, s => s));
    }

    [TestMethod]
    public void RewritingDropsAnUnresolvedImportAndDeduplicatesTheRest()
    {
        var js = "import 'tps-type:A';\nimport 'tps-type:B';\nimport 'tps-type:C';\nTranspose.define(\"X\", {});\n";
        var linked = ModuleSpecifier.RewriteLeadingImports(js, s => s switch
        {
            "tps-type:A" => "../lib/c1.mjs",
            "tps-type:B" => "../lib/c1.mjs",     // two types, one chunk
            _ => null,                            // nothing in the site defines C
        });
        Assert.AreEqual("import '../lib/c1.mjs';\nTranspose.define(\"X\", {});\n", linked);
    }

    // ------------------------------------------------------------------ what the emitter writes

    [TestMethod]
    public void APackageImportsALibraryTypeByNameNotByChunkFile()
    {
        var lib = BuildLibraryPackage("Widget", "public int N;");
        var mid = EmitAgainst(lib.Dll, "public class Panel { public Lib.Widget W = new Lib.Widget(); }", packageModules: true);

        var chunk = mid.Chunks.Single(c => c.js.Contains("Panel"));
        StringAssert.Contains(chunk.js, "import 'tps-type:Lib.Widget';");
        // The library's own chunk names appear nowhere: that is what makes the package independent of
        // which build of the library it happened to compile against.
        foreach (var (_, js) in mid.Chunks)
            Assert.IsFalse(Regex.IsMatch(js, @"import\s*'\.\./Lib/"),
                "a package must not write its dependency's chunk paths:\n" + js);
    }

    [TestMethod]
    public void APackagesJavaScriptDoesNotChangeWhenOnlyTheLibraryIsRebuilt()
    {
        // The regression itself. Two builds of Lib whose chunk file names differ; the package compiled
        // against each must come out byte-identical, so nothing about it goes stale when Lib ships.
        var v1 = BuildLibraryPackage("Widget", "public int N;");
        var v2 = BuildLibraryPackage("Widget", "public int N; public string S; public double D;");
        CollectionAssert.AreNotEquivalent(
            v1.Modules.Chunks.Select(c => c.relPath).ToList(),
            v2.Modules.Chunks.Select(c => c.relPath).ToList(),
            "the two library builds are supposed to differ in their chunk names");

        const string source = "public class Panel { public Lib.Widget W = new Lib.Widget(); }";
        var against1 = EmitAgainst(v1.Dll, source, packageModules: true);
        var against2 = EmitAgainst(v2.Dll, source, packageModules: true);

        CollectionAssert.AreEqual(
            against1.Chunks.Select(c => c.relPath + "\n" + c.js).ToList(),
            against2.Chunks.Select(c => c.relPath + "\n" + c.js).ToList());
        Assert.AreEqual(against1.EntryJs, against2.EntryJs);
    }

    // ------------------------------------------------------------------ what the site build resolves

    [TestMethod]
    public void TheSiteResolvesAPlaceholderToTheChunkThatDefinesTheTypeNow()
    {
        var lib = BuildLibraryPackage("Widget", "public int N;");
        var mid = BuildConsumerPackage("Mid", lib.Dll, "public class Panel { public Lib.Widget W = new Lib.Widget(); }");

        var site = BuildSite(new[] { lib.Dll, mid.Dll });

        var widgetChunk = lib.Modules.TypeToChunk["Lib.Widget"];
        Assert.IsTrue(File.Exists(Path.Combine(site, widgetChunk.Replace('/', Path.DirectorySeparatorChar))));

        var panel = FindChunkDefining(site, "Mid.Panel");
        StringAssert.Contains(File.ReadAllText(panel.path),
            ModuleSpecifier.Relative(panel.rel, widgetChunk),
            "the package's placeholder should have become an import of the library chunk in this site");
        Assert.IsFalse(File.ReadAllText(panel.path).Contains(ModuleSpecifier.TypePrefix),
            "no placeholder may survive into the site");
    }

    [TestMethod]
    public void APackageBuiltAgainstAnOlderLibraryStillLinksAgainstTheNewOne()
    {
        // The scenario reported: Mid is compiled against Lib v1 and never rebuilt; the site is then
        // assembled with Lib v2, whose chunks have different names.
        var v1 = BuildLibraryPackage("Widget", "public int N;");
        var mid = BuildConsumerPackage("Mid", v1.Dll, "public class Panel { public Lib.Widget W = new Lib.Widget(); }");
        var v2 = BuildLibraryPackage("Widget", "public int N; public string S; public double D;");

        var site = BuildSite(new[] { v2.Dll, mid.Dll });

        // Every import in every module the site wrote points at a file that is actually there. Before
        // the placeholder indirection this is precisely what failed: Mid's chunk still named a v1 file.
        AssertEveryImportResolves(site);

        var panel = FindChunkDefining(site, "Mid.Panel");
        StringAssert.Contains(File.ReadAllText(panel.path),
            ModuleSpecifier.Relative(panel.rel, v2.Modules.TypeToChunk["Lib.Widget"]));
    }

    [TestMethod]
    public void AnApplicationsOwnChunksImportIntoBothLibraries()
    {
        var lib = BuildLibraryPackage("Widget", "public int N;");
        var mid = BuildConsumerPackage("Mid", lib.Dll, "public class Panel { public Lib.Widget W = new Lib.Widget(); }");

        var app = EmitAgainst(new[] { lib.Dll, mid.Dll },
            "public class Screen { public Mid.Panel P = new Mid.Panel(); }\n" +
            "public class Program { public static void Main() { System.Console.WriteLine(new Screen()); } }",
            packageModules: false);

        var site = BuildSite(new[] { lib.Dll, mid.Dll }, app);
        AssertEveryImportResolves(site);

        var screen = FindChunkDefining(site, "Screen");
        StringAssert.Contains(File.ReadAllText(screen.path),
            ModuleSpecifier.Relative(screen.rel, FindChunkDefining(site, "Mid.Panel").rel));
    }

    [TestMethod]
    public void RenamingALinkedChunkFollowsThroughToItsImportersAndTheManifest()
    {
        // Resolving a placeholder changes a chunk's text, so it is renamed to the hash of what it now
        // contains — otherwise one URL would serve different bytes in two deployments and a browser
        // holding the first would import a chunk that is gone. The rename has to reach the chunks that
        // import it and the entry module's Transpose.Modules manifest, or the site breaks at load.
        var lib = BuildLibraryPackage("Widget", "public int N;");
        var mid = BuildConsumerPackage("Mid", lib.Dll,
            "public class Panel { public Lib.Widget W = new Lib.Widget(); }\n" +
            "public class Board { public Panel P = new Panel(); }");

        var site = BuildSite(new[] { lib.Dll, mid.Dll });

        var panel = FindChunkDefining(site, "Mid.Panel");
        Assert.AreNotEqual(mid.Modules.TypeToChunk["Mid.Panel"], panel.rel,
            "a chunk whose text the link changed must be renamed to match its new contents");

        AssertEveryImportResolves(site);
        // The manifest the entry registers names chunk files too, and it is read at run time.
        foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(site, "Mid.js")), @"m:\s*""\./([^""]+)"""))
            Assert.IsTrue(File.Exists(Path.Combine(site, m.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar))),
                "the entry module registers " + m.Groups[1].Value + ", which is not in the site");
    }

    [TestMethod]
    public void APlaceholderNothingInTheSiteDefinesIsDropped()
    {
        // A library the site took as a single bundle has no chunk to import — its code is already on
        // the page — so the placeholder simply goes away rather than becoming a dangling import.
        var linker = new ModuleLinker();
        var linked = linker.LinkAssembly(
            new Dictionary<string, string> { ["Mid.Panel"] = "chunks/Mid/c0.mjs" },
            new[]
            {
                new ModuleFile("chunks/Mid/c0.mjs", "import 'tps-type:Lib.Widget';\nTranspose.define(\"Mid.Panel\", {});\n", IsChunk: true),
            });

        Assert.AreEqual("Transpose.define(\"Mid.Panel\", {});\n", linked[0].Text);
    }

    [TestMethod]
    public void AnImportNamingAFileTheSiteDoesNotHaveIsReported()
    {
        // What a package built by a compiler that wrote chunk FILE names leaves behind once its
        // dependency ships a new version. Nothing can repair it here — the file name says nothing
        // about which type was wanted — but the build should not stay silent about a guaranteed 404.
        var utf8 = new UTF8Encoding(false);
        var stale = PackageDll("Stale", new[]
        {
            new EmbeddedItem("c0.mjs", utf8.GetBytes("import '../Gone/c9c9c9c9c9c9c9c9.mjs';\nTranspose.define(\"Stale.X\", {});\n"),
                             "chunks/Stale", Load: false, Variant: JsVariant.ModuleChunk),
            new EmbeddedItem("Stale.mjs", utf8.GetBytes("Transpose.Modules.register({});\n"), null,
                             Load: true, Module: true, Variant: JsVariant.ModuleEntry, SiteName: "Stale.js"),
        }, new Dictionary<string, string> { ["Stale.X"] = "chunks/Stale/c0.mjs" });

        BuildSite(new[] { stale }, out var result);

        Assert.AreEqual(1, result.DanglingModuleImports.Count,
            "expected exactly one report, got: " + string.Join("; ", result.DanglingModuleImports));
        StringAssert.Contains(result.DanglingModuleImports[0], "../Gone/c9c9c9c9c9c9c9c9.mjs");
    }

    // ---------------------------------------------------------------------------------- helpers

    private sealed record Package(string Dll, Emitter.ModuleOutput Modules);

    private static Emitter.ModuleOutput EmitAgainst(string reference, string source, bool packageModules)
        => EmitAgainst(new[] { reference }, source, packageModules);

    private static Emitter.ModuleOutput EmitAgainst(IReadOnlyList<string>? references, string source, bool packageModules)
        => Build("App", references, source, packageModules).Modules!;

    private static AssemblyBuildResult Build(string assemblyName, IReadOnlyList<string>? references, string source, bool packageModules)
    {
        var result = new RoslynTranslator().BuildAssembly(
            new[] { (assemblyName + ".cs", source) }, assemblyName, references,
            preprocessorSymbols: new[] { "DEBUG", "TRACE" },
            emitAssembly: true, emitModules: true,
            chunkDirectory: "chunks/" + assemblyName,
            packageModules: packageModules,
            minChunkBytes: 0, maxChunkBytes: 0,
            alsoEmitBundle: packageModules);
        if (!result.Success)
            Assert.Fail("translation failed:\n" + string.Join("\n", result.Errors.Select(d => d.GetMessage())));
        return result;
    }

    /// <summary>A module-mode package on disk, exactly as <c>tps --emit-package</c> writes one: every
    /// variant of its compiled JavaScript embedded and tagged, plus its published chunk map.</summary>
    private Package BuildLibraryPackage(string typeName, string members)
        => Pack("Lib", null, $"namespace Lib {{ public class {typeName} {{ {members} }} }}");

    private Package BuildConsumerPackage(string assemblyName, string reference, string source)
        => Pack(assemblyName, new[] { reference }, "namespace " + assemblyName + " {\n" + source + "\n}");

    /// <summary>A package DLL assembled by hand, for a shape the current compiler no longer emits.</summary>
    private string PackageDll(string assemblyName, IReadOnlyList<EmbeddedItem> items, IReadOnlyDictionary<string, string> moduleMap)
    {
        var result = Build(assemblyName, null, $"namespace {assemblyName} {{ public class X {{ }} }}", packageModules: true);
        var dir = Path.Combine(_root, assemblyName);
        Directory.CreateDirectory(dir);
        var dll = Path.Combine(dir, assemblyName + ".dll");
        ResourceEmbedder.Embed(dll, result.AssemblyBytes!, items, referencePaths: null, moduleMap: moduleMap);
        return dll;
    }

    private Package Pack(string assemblyName, IReadOnlyList<string>? references, string source)
    {
        var result = Build(assemblyName, references, source, packageModules: true);
        var dir = Path.Combine(_root, assemblyName + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "tps.json"), $@"{{ ""fileName"": ""{assemblyName}.js"", ""outputBy"": ""Module"" }}");
        var config = TransposeJson.TryLoad(dir, "Release")!;

        var items = OutputBuilder.CollectEmbeddableItems(
            dir, config, assemblyName + ".js", result.Javascript!, result.MetadataJavascript,
            minifyLocalVariables: false, modules: result.Modules);

        var dll = Path.Combine(dir, assemblyName + ".dll");
        ResourceEmbedder.Embed(dll, result.AssemblyBytes!, items, referencePaths: references,
                               moduleMap: result.Modules!.TypeToChunk, skipClusterMap: result.Modules.SkipClusterDeps);
        return new Package(dll, result.Modules);
    }

    private string BuildSite(IReadOnlyList<string> references, Emitter.ModuleOutput? app = null)
        => BuildSite(references, out _, app);

    /// <summary>Assembles a Release site — the only shape that takes a package's module variant.</summary>
    private string BuildSite(IReadOnlyList<string> references, out OutputBuilder.SiteBuildResult result, Emitter.ModuleOutput? app = null)
    {
        app ??= EmitAgainst(references, "public class Program { public static void Main() { } }", packageModules: false);

        var appDir = Path.Combine(_root, "app");
        var outDir = Path.Combine(_root, "site-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(appDir);
        File.WriteAllText(Path.Combine(appDir, "tps.json"), @"{ ""fileName"": ""app.js"", ""outputBy"": ""Module"" }");
        var config = TransposeJson.TryLoad(appDir, "Release")!;

        var project = new ResolvedProject
        {
            CsprojPath = Path.Combine(appDir, "App.csproj"),
            ProjectDir = appDir,
            AssemblyName = "App",
            TargetFramework = "netstandard2.0",
            Sources = new List<(string, string)>(),
            ReferencePaths = references.ToList(),
            DefineConstants = new List<string>(),
            LanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest,
            ProjectDirs = new List<string> { appDir },
        };

        result = OutputBuilder.Build(project, config, app.EntryJs, outDir, "Release", modules: app);
        return outDir;
    }

    /// <summary>The site file whose JavaScript defines a type, and its site-relative path.</summary>
    private static (string path, string rel) FindChunkDefining(string site, string defineName)
    {
        var define = new Regex(@"Transpose\.definei?\(""" + Regex.Escape(defineName) + @"""");
        foreach (var path in Directory.EnumerateFiles(site, "*.mjs", SearchOption.AllDirectories))
            if (define.IsMatch(File.ReadAllText(path)))
                return (path, Path.GetRelativePath(site, path).Replace(Path.DirectorySeparatorChar, '/'));
        Assert.Fail("no chunk in the site defines " + defineName);
        return default;
    }

    /// <summary>Every <c>import</c> in every module the site wrote points at a file that exists.</summary>
    private static void AssertEveryImportResolves(string site)
    {
        var checkedAny = false;
        foreach (var path in Directory.EnumerateFiles(site, "*.*", SearchOption.AllDirectories))
        {
            if (!path.EndsWith(".mjs", StringComparison.Ordinal) && !path.EndsWith(".js", StringComparison.Ordinal)) continue;
            var rel = Path.GetRelativePath(site, path).Replace(Path.DirectorySeparatorChar, '/');
            foreach (var import in ModuleSpecifier.ReadLeading(File.ReadAllText(path)))
            {
                checkedAny = true;
                var target = ModuleSpecifier.Resolve(rel, import.Specifier);
                Assert.IsNotNull(target, rel + " imports '" + import.Specifier + "', which is not a path");
                Assert.IsTrue(File.Exists(Path.Combine(site, target!.Replace('/', Path.DirectorySeparatorChar))),
                              rel + " imports '" + import.Specifier + "', which is not in the site");
            }
        }
        Assert.IsTrue(checkedAny, "the site has no module imports at all — the scenario did not build");
    }
}
