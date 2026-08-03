using System.Text;
using Mono.Cecil;
using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Covers the CSS comment pass (<see cref="CssProcessor"/>) and its wiring: every stylesheet the
/// compiler produces is comment-free, whether it is written into the site from a <c>tps.json</c>
/// resource group (bundled or copied through), embedded into a package DLL, or extracted back out of
/// a referenced package. Only comments go — the declarations themselves are untouched.
/// </summary>
[TestClass]
public sealed class CssCommentStrippingTests
{
    private string _root = "";
    private string _projectDir = "";
    private string _outputDir = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "tps-css-" + Guid.NewGuid().ToString("N"));
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

    // ---------------------------------------------------------------- the pass itself

    [TestMethod]
    public void ABlockCommentIsRemoved()
    {
        Assert.AreEqual("body { color: red }", CssProcessor.StripComments("body { color: red }/* trailing */"));
        Assert.AreEqual("a { }", CssProcessor.StripComments("/* leading */a { }"));
    }

    [TestMethod]
    public void AWholeLineCommentTakesItsLineWithIt()
    {
        var stripped = CssProcessor.StripComments("/* banner */\nbody { color: red }\n    /* indented */\na { }\n");
        Assert.AreEqual("body { color: red }\na { }\n", stripped);
    }

    [TestMethod]
    public void AMultiLineCommentIsRemovedWhole()
    {
        var stripped = CssProcessor.StripComments("/*\n * a banner\n */\nbody { }\n");
        Assert.AreEqual("body { }\n", stripped);
    }

    [TestMethod]
    public void ACommentBetweenTwoTokensBecomesASpace()
    {
        // `a/**/b` must not collapse into the single type selector `ab`.
        Assert.AreEqual("a b { }", CssProcessor.StripComments("a/**/b { }"));
        Assert.AreEqual("color: red ;", CssProcessor.StripComments("color: red/* why */;"));
    }

    [TestMethod]
    public void ACommentInsideAStringIsNotAComment()
    {
        const string css = "a::after { content: \"/* not a comment */\" }";
        Assert.AreEqual(css, CssProcessor.StripComments(css));

        const string single = "a::after { content: '/* nor this */' }";
        Assert.AreEqual(single, CssProcessor.StripComments(single));
    }

    [TestMethod]
    public void AnEscapedQuoteDoesNotEndTheString()
    {
        const string css = "a::after { content: \"he said \\\" /* still a string */\" }";
        Assert.AreEqual(css, CssProcessor.StripComments(css));
    }

    [TestMethod]
    public void ACommentInsideAnUnquotedUrlIsNotAComment()
    {
        const string css = "a { background: url(/img/*.png) }";
        Assert.AreEqual(css, CssProcessor.StripComments(css));

        // …while a real comment right after the url token still goes.
        Assert.AreEqual(
            "a { background: url(/img/x.png) }",
            CssProcessor.StripComments("a { background: url(/img/x.png)/* c */ }"));
    }

    [TestMethod]
    public void AnIdentifierEndingInUrlIsNotAUrlToken()
    {
        // `--my-url(` must not be mistaken for a url token — the comment after it is a real comment.
        Assert.AreEqual("a { b: --my-url(x) }", CssProcessor.StripComments("a { b: --my-url(x)/* c */ }"));
    }

    [TestMethod]
    public void AnUnterminatedCommentIsConsumedToTheEndOfTheFile()
    {
        Assert.AreEqual("a { }\n", CssProcessor.StripComments("a { }\n/* never closed\nb { }\n"));
    }

    [TestMethod]
    public void AStylesheetWithoutCommentsIsReturnedUnchanged()
    {
        const string css = "body { color: red }\n";
        Assert.AreSame(css, CssProcessor.StripComments(css), "no comments must mean no work and no rewrite");

        var bytes = Encoding.UTF8.GetBytes(css);
        Assert.AreSame(bytes, CssProcessor.StripComments(bytes));
    }

    [TestMethod]
    public void ACommentEndingALineTakesTheTrailingSpaceWithIt()
    {
        Assert.AreEqual(".two { color: blue }", CssProcessor.StripComments(".two { color: blue } /* two */"));
        Assert.AreEqual("a { }\nb { }\n", CssProcessor.StripComments("a { }   /* c */\nb { }\n"));
    }

    [TestMethod]
    public void AUtf8BomAndNonAsciiContentSurvive()
    {
        var encoder = new UTF8Encoding(true);
        var bytes = encoder.GetPreamble().Concat(encoder.GetBytes("/* héllo */\na::after { content: \"—\" }\n")).ToArray();
        var stripped = CssProcessor.StripComments(bytes);

        Assert.IsTrue(stripped.Length >= 3 && stripped[0] == 0xEF && stripped[1] == 0xBB && stripped[2] == 0xBF,
            "the BOM must be preserved");
        Assert.AreEqual("a::after { content: \"—\" }\n", new UTF8Encoding(true).GetString(stripped, 3, stripped.Length - 3));
    }

    [TestMethod]
    public void BytesThatAreNotValidUtf8AreLeftAlone()
    {
        // A legacy single-byte-encoded stylesheet: a byte-for-byte copy beats a mangled re-encode.
        var bytes = new byte[] { (byte)'/', (byte)'*', (byte)'x', 0xFF, (byte)'*', (byte)'/', (byte)'a', (byte)'{', (byte)'}' };
        CollectionAssert.AreEqual(bytes, CssProcessor.StripComments(bytes));
    }

    // ---------------------------------------------------------------- the site build

    [TestMethod]
    public void ABundledStylesheetIsWrittenToTheSiteWithoutComments()
    {
        Asset("css/one.css", "/* one */\n.one { color: red }\n");
        Asset("css/two.css", ".two { color: blue } /* two */");

        BuildSite(Config(@"{
            ""fileName"": ""app.js"",
            ""outputFormatting"": ""Formatted"",
            ""resources"": [
                { ""name"": ""site.css"", ""files"": [ ""css/one.css"", ""css/two.css"" ] }
            ]
        }"));

        var written = OutputText("site.css");
        Assert.AreEqual(".one { color: red }\n\n.two { color: blue }", written);
    }

    [TestMethod]
    public void ACopiedThroughStylesheetIsWrittenToTheSiteWithoutComments()
    {
        Asset("vendor/theme.css", "/* vendor banner */\n.theme { color: red }\n");
        Asset("vendor/logo.svg", "<svg><!-- kept --></svg>");

        BuildSite(Config(@"{
            ""fileName"": ""app.js"",
            ""outputFormatting"": ""Formatted"",
            ""resources"": [
                { ""name"": ""vendor"", ""files"": [ ""vendor/*"" ], ""output"": ""vendor"" }
            ]
        }"));

        Assert.AreEqual(".theme { color: red }\n", OutputText("vendor/theme.css"));
        Assert.AreEqual("<svg><!-- kept --></svg>", OutputText("vendor/logo.svg"),
            "a non-CSS resource must be copied through byte for byte");
    }

    // ---------------------------------------------------------------- packages

    [TestMethod]
    public void AnEmbeddedStylesheetIsStrippedBeforeItReachesThePackage()
    {
        Asset("css/site.css", "/* banner */\n.site { color: red }\n");
        Asset("css/extra.css", "/* extra */\n.extra { }\n");

        var config = Config(@"{
            ""fileName"": ""Lib.js"",
            ""outputFormatting"": ""Formatted"",
            ""resources"": [
                { ""name"": ""site.css"", ""files"": [ ""css/site.css"" ] },
                { ""name"": ""assets"",   ""files"": [ ""css/extra.css"" ], ""output"": ""assets"" }
            ]
        }");

        var items = OutputBuilder.CollectEmbeddableItems(_projectDir, config, "Lib.js", "var lib = 1;", null);

        Assert.AreEqual(".site { color: red }\n", Text(items.Single(i => i.Name == "site.css")),
            "a bundled stylesheet must be embedded comment-free");
        Assert.AreEqual(".extra { }\n", Text(items.Single(i => i.Name == "extra.css")),
            "a copy-through stylesheet must be embedded comment-free");

        static string Text(EmbeddedItem item) => new UTF8Encoding(false).GetString(item.Content);
    }

    [TestMethod]
    public void AStylesheetExtractedFromAPackageBuiltByAnOlderCompilerIsStrippedOnTheWayOut()
    {
        // The embed side strips, but a package published before this pass existed still carries its
        // comments — so extraction strips too. The package here is built by hand for exactly that reason.
        var utf8 = new UTF8Encoding(false);
        var dll = PackageDll("Lib", new List<EmbeddedItem>
        {
            new("Lib.js", utf8.GetBytes("var lib = 1;"), null),
            new("legacy.css", utf8.GetBytes("/* shipped with comments */\n.legacy { color: red }\n"), null),
            new("nested.css", utf8.GetBytes("/* also */\n.nested { }\n"), "assets"),
        });

        var html = BuildSite(Config(@"{ ""fileName"": ""app.js"", ""outputFormatting"": ""Formatted"" }"), Project(dll));

        Assert.AreEqual(".legacy { color: red }\n", OutputText("legacy.css"));
        Assert.AreEqual(".nested { }\n", OutputText("assets/nested.css"));
        StringAssert.Contains(html, "href=\"legacy.css\"", "the stylesheet must still be linked");
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

    private string BuildSite(TransposeJson config, ResolvedProject? project = null)
    {
        OutputBuilder.Build(project ?? Project(), config, "var app = 1;", _outputDir, "Debug");
        var html = Path.Combine(_outputDir, "index.html");
        return File.Exists(html) ? File.ReadAllText(html) : "";
    }

    private string OutputText(string rel)
    {
        var full = Path.Combine(_outputDir, rel.Replace('/', Path.DirectorySeparatorChar));
        Assert.IsTrue(File.Exists(full), $"'{rel}' must be written to the site");
        return File.ReadAllText(full);
    }

    /// <summary>Embeds <paramref name="items"/> into an otherwise empty assembly through the real
    /// <see cref="ResourceEmbedder"/>, standing in for a published package.</summary>
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
