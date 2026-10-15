using Mono.Cecil;
using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// What a site build may write and delete. A referenced package is untrusted input, a site directory
/// can be shared by several apps, and it can sit next to (or on top of) the project's own files: none
/// of that may cost the user a file the build does not own.
/// </summary>
[TestClass]
public sealed class SiteOutputSafetyTests
{
    private string _root = "";
    private string _appDir = "";
    private string _outputDir = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "tps-sitesafety-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A package resource whose output path climbs out of the site must not overwrite a file
    /// there; the build fails instead.</summary>
    [TestMethod]
    public void APackageResourceCannotBeWrittenOutsideTheSite()
    {
        var victim = Path.Combine(_root, "victim", "notes.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(victim)!);
        File.WriteAllText(victim, "the user's notes");

        var package = PackageDll("Evil", new EmbeddedItem("notes.txt", "payload"u8.ToArray(), "../victim"));

        Assert.ThrowsExactly<InvalidDataException>(() => BuildSite(package));
        Assert.AreEqual("the user's notes", File.ReadAllText(victim));
    }

    /// <summary>A manifest entry outside the site (written by an older compiler that did not check)
    /// is never a prune candidate.</summary>
    [TestMethod]
    public void AManifestEntryOutsideTheSiteIsNeverPruned()
    {
        var victim = Path.Combine(_root, "victim", "notes.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(victim)!);
        File.WriteAllText(victim, "the user's notes");
        File.WriteAllText(Path.Combine(_outputDir, ".tps-manifest.App.json"), "[ \"../victim/notes.txt\" ]");

        BuildSite(PackageDll("Lib", new EmbeddedItem("lib.css", "a{}"u8.ToArray(), null)));

        Assert.IsTrue(File.Exists(victim));
    }

    /// <summary>Two apps share a site folder and a library: one dropping the library must not delete
    /// what the other's manifest still lists.</summary>
    [TestMethod]
    public void AFileAnotherProjectStillListsIsKept()
    {
        BuildSite(PackageDll("Lib", new EmbeddedItem("shared.css", "a{}"u8.ToArray(), null)));
        Assert.IsTrue(File.Exists(Path.Combine(_outputDir, "shared.css")));
        File.WriteAllText(Path.Combine(_outputDir, ".tps-manifest.App2.json"), "[ \"shared.css\" ]");

        BuildSite(PackageDll("Other", new EmbeddedItem("other.css", "b{}"u8.ToArray(), null)));

        Assert.IsTrue(File.Exists(Path.Combine(_outputDir, "shared.css")));
    }

    /// <summary>An empty directory the build did not empty (an upload folder a server expects) is left
    /// alone; one the prune emptied still goes.</summary>
    [TestMethod]
    public void OnlyDirectoriesThePruneEmptiedAreRemoved()
    {
        Directory.CreateDirectory(Path.Combine(_outputDir, "uploads", "avatars"));
        var kept  = Touch("app.js");
        var stale = Touch("old/stale.css");

        OutputBuilder.PruneStaleFiles(_outputDir, Set(kept), Set(kept, stale), Array.Empty<string>());

        Assert.IsTrue(Directory.Exists(Path.Combine(_outputDir, "uploads", "avatars")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_outputDir, "old")));
    }

    /// <summary>A site directory that overlaps the sources must not rewrite an authored stylesheet in
    /// place (stripping its comments) or fail copying a file onto itself.</summary>
    [TestMethod]
    public void AResourceIsNeverWrittenOverItsOwnSource()
    {
        const string css = "/* keep me */\nbody { }\n";
        var source = Path.Combine(_appDir, "assets", "site.css");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, css);
        File.WriteAllBytes(Path.Combine(_appDir, "assets", "logo.png"), new byte[] { 1, 2, 3 });

        _outputDir = _appDir;
        BuildSite(PackageDll("Lib", new EmbeddedItem("lib.css", "a{}"u8.ToArray(), null)), @"{
            ""fileName"": ""app.js"",
            ""resources"": [ { ""files"": [ ""assets/site.css"", ""assets/logo.png"" ], ""output"": ""assets/"" } ]
        }");

        Assert.AreEqual(css, File.ReadAllText(source));
    }

    /// <summary>A relative tps.json <c>output</c> is relative to the project, not to the directory
    /// the process happens to run in.</summary>
    [TestMethod]
    public void ARelativeOutputResolvesAgainstTheProject()
    {
        File.WriteAllText(Path.Combine(_appDir, "tps.json"), @"{ ""fileName"": ""app.js"", ""output"": ""site/"" }");
        var config = TransposeJson.TryLoad(_appDir, "Debug")!;

        var resolved = ProjectBuild.ResolveOutputDir(config, _appDir, "Debug");

        Assert.AreEqual(Path.Combine(_appDir, "site") + Path.DirectorySeparatorChar, resolved);
    }

    /// <summary>The incremental cache fingerprints what a resource glob matches today, so an added or
    /// edited stylesheet is never "up to date".</summary>
    [TestMethod]
    public void ResourceFilesExpandGlobs()
    {
        Directory.CreateDirectory(Path.Combine(_appDir, "assets", "css"));
        File.WriteAllText(Path.Combine(_appDir, "assets", "css", "a.css"), "a{}");
        File.WriteAllText(Path.Combine(_appDir, "assets", "css", "b.css"), "b{}");
        File.WriteAllText(Path.Combine(_appDir, "tps.json"), @"{
            ""fileName"": ""app.js"",
            ""resources"": [ { ""name"": ""app.css"", ""files"": [ ""assets/css/*.css"" ] } ]
        }");
        var config = TransposeJson.TryLoad(_appDir, "Debug")!;

        var files = OutputBuilder.ResourceFiles(_appDir, config).Select(Path.GetFileName).ToList();

        CollectionAssert.AreEqual(new[] { "a.css", "b.css" }, files);
    }

    // ---------------------------------------------------------------- helpers

    private string Touch(string rel)
    {
        var full = Path.Combine(_outputDir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
        return Path.GetFullPath(full);
    }

    private static HashSet<string> Set(params string[] paths) => new(paths, OutputBuilder.PathComparer);

    private void BuildSite(string packageDll, string? appTpsJson = null)
    {
        File.WriteAllText(Path.Combine(_appDir, "tps.json"), appTpsJson ?? @"{ ""fileName"": ""app.js"" }");
        var config = TransposeJson.TryLoad(_appDir, "Debug");
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

        OutputBuilder.Build(project, config!, "var app = 1;", _outputDir, "Debug");
    }

    private string PackageDll(string assemblyName, params EmbeddedItem[] items)
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
