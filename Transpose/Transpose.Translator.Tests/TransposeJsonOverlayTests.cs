using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Covers the <c>tps.&lt;Configuration&gt;.json</c> overlay: a per-configuration file merged on top of
/// <c>tps.json</c>, where a scalar the overlay sets wins and everything it says nothing about is
/// inherited from the base.
///
/// The merge is written field by field, so the failure mode a new setting invites is a silent one —
/// forget to list it and an overlay does not override it, it <em>erases</em> it, because the merged
/// result starts from a blank draft. That is what happened to <c>html.meta</c>: any project with an
/// overlay lost the meta tags its base tps.json declared, and index.html simply came out without them.
/// <see cref="AnOverlayInheritsEverySettingItDoesNotItselfSet"/> is the guard: it declares every
/// setting in the base, overlays a single unrelated one, and asserts the rest survive.
/// </summary>
[TestClass]
public sealed class TransposeJsonOverlayTests
{
    private string _projectDir = "";

    [TestInitialize]
    public void Setup()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), "tps-overlay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_projectDir)) Directory.Delete(_projectDir, recursive: true); } catch { }
    }

    private void Write(string name, string content) => File.WriteAllText(Path.Combine(_projectDir, name), content);

    [TestMethod]
    public void AnOverlayInheritsEverySettingItDoesNotItselfSet()
    {
        Write("tps.json", @"{
            ""output"": ""$(OutDir)/tps/"",
            ""outputBy"": ""Module"",
            ""fileName"": ""app.js"",
            ""loadCompiledOutput"": false,
            ""cleanOutputFolder"": false,
            ""cleanOutputFolderExclude"": [ ""favicon.ico"" ],
            ""dontLoadReferences"": [ ""Tesserae.Plotly"" ],
            ""modules"": { ""minChunkSize"": 1234, ""maxChunkSize"": 5678 },
            ""reflection"": { ""disabled"": true, ""target"": ""inline"" },
            ""html"": {
                ""disabled"": true,
                ""title"": ""Base title"",
                ""head"": ""<link rel=\""icon\"" href=\""favicon.ico\"">"",
                ""meta"": ""<meta name=\""viewport\"" content=\""width=device-width\"">"",
                ""body"": ""<div id=\""app\""></div>""
            },
            ""resources"": [ { ""name"": ""site.css"", ""files"": [ ""assets/site.css"" ] } ]
        }");

        // The overlay speaks about one thing only. Everything else must come through untouched.
        Write("tps.Release.json", @"{ ""fileName"": ""app.release.js"" }");

        var config = TransposeJson.TryLoad(_projectDir, "Release");
        Assert.IsNotNull(config);

        Assert.AreEqual("app.release.js", config!.FileName, "the overlay's own setting must win");

        Assert.AreEqual("$(OutDir)/tps/", config.Output);
        Assert.AreEqual("Module", config.OutputBy);
        Assert.IsFalse(config.LoadCompiledOutput);
        Assert.IsFalse(config.CleanOutputFolder);
        CollectionAssert.AreEqual(new[] { "favicon.ico" }, config.CleanOutputFolderExclude);
        CollectionAssert.AreEqual(new[] { "Tesserae.Plotly" }, config.DontLoadReferences);
        Assert.AreEqual(1234, config.ModuleMinChunkBytes);
        Assert.AreEqual(5678, config.ModuleMaxChunkBytes);
        Assert.IsTrue(config.ReflectionDisabled);
        Assert.AreEqual("inline", config.ReflectionTarget);
        Assert.IsTrue(config.HtmlDisabled);
        Assert.AreEqual("Base title", config.HtmlTitle);
        StringAssert.Contains(config.HtmlHead, "favicon.ico");
        StringAssert.Contains(config.HtmlMeta, "viewport", "html.meta must survive an overlay");
        StringAssert.Contains(config.HtmlBody, "id=\"app\"");
        Assert.AreEqual(1, config.Resources.Count);
    }

    [TestMethod]
    public void AnOverlayOverridesTheHtmlSettingsItDoesSet()
    {
        Write("tps.json", @"{
            ""html"": { ""title"": ""Base"", ""meta"": ""<meta name=\""env\"" content=\""base\"">"" }
        }");
        Write("tps.Release.json", @"{
            ""html"": { ""meta"": ""<meta name=\""env\"" content=\""release\"">"" }
        }");

        var config = TransposeJson.TryLoad(_projectDir, "Release")!;

        StringAssert.Contains(config.HtmlMeta, "release");
        Assert.AreEqual("Base", config.HtmlTitle, "a setting the overlay is silent about stays as the base wrote it");
    }

    [TestMethod]
    public void WithNoOverlayForTheConfigurationTheBaseIsUsedAsIs()
    {
        Write("tps.json", @"{ ""html"": { ""meta"": ""<meta charset=\""utf-8\"">"" } }");
        Write("tps.Release.json", @"{ ""fileName"": ""app.release.js"" }");

        var debug = TransposeJson.TryLoad(_projectDir, "Debug")!;
        StringAssert.Contains(debug.HtmlMeta, "charset");
        Assert.AreEqual("app.js", debug.FileName, "the Release overlay must not reach a Debug build");
    }
}
