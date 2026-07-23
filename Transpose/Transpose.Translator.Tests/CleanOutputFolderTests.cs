using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Covers the "clean output folder" prune (<see cref="OutputBuilder.PruneStaleFiles"/>): after a
/// site build, files a previous build produced but this one did not re-write are removed, the
/// directories they empty are cleaned up, current outputs are always kept, and
/// <c>cleanOutputFolderExclude</c> globs protect hand-placed files.
/// </summary>
[TestClass]
public sealed class CleanOutputFolderTests
{
    private string _dir = "";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tps-clean-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Touch(string rel, string content = "x")
    {
        var full = Path.Combine(_dir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return Path.GetFullPath(full);
    }

    private HashSet<string> Written(params string[] fullPaths)
        => new(fullPaths, OutputBuilder.PathComparer);

    [TestMethod]
    public void PrunesOnlyFilesTheBuildDidNotWrite()
    {
        var kept = Touch("app.js");
        var alsoKept = Touch("index.html");
        Touch("old-app.js");            // stale — not in the written set
        Touch("app.min.js");            // stale variant no longer produced

        var removed = OutputBuilder.PruneStaleFiles(_dir, Written(kept, alsoKept), Array.Empty<string>());

        Assert.IsTrue(File.Exists(kept), "current output app.js must survive");
        Assert.IsTrue(File.Exists(alsoKept), "current output index.html must survive");
        Assert.IsFalse(File.Exists(Path.Combine(_dir, "old-app.js")), "stale file must be pruned");
        Assert.IsFalse(File.Exists(Path.Combine(_dir, "app.min.js")), "stale variant must be pruned");
        Assert.AreEqual(2, removed.Count);
    }

    [TestMethod]
    public void RemovesDirectoriesLeftEmptyByThePrune()
    {
        var kept = Touch("app.js");
        Touch("oldassets/old.css");     // stale, and its folder becomes empty

        OutputBuilder.PruneStaleFiles(_dir, Written(kept), Array.Empty<string>());

        Assert.IsFalse(Directory.Exists(Path.Combine(_dir, "oldassets")), "emptied directory must be removed");
        Assert.IsTrue(Directory.Exists(_dir), "the output root itself must remain");
    }

    [TestMethod]
    public void KeepsNonEmptyDirectories()
    {
        var kept = Touch("assets/app.js");
        Touch("assets/stale.js");       // stale sibling; folder still has app.js afterwards

        OutputBuilder.PruneStaleFiles(_dir, Written(kept), Array.Empty<string>());

        Assert.IsTrue(File.Exists(kept));
        Assert.IsFalse(File.Exists(Path.Combine(_dir, "assets", "stale.js")));
        Assert.IsTrue(Directory.Exists(Path.Combine(_dir, "assets")), "a directory that still holds output must remain");
    }

    [TestMethod]
    public void ExcludeGlobProtectsMatchingFilesByName()
    {
        var kept = Touch("app.js");
        Touch("keepme.txt");            // stale but protected by name
        Touch("data.bak");              // stale but protected by *.bak

        var removed = OutputBuilder.PruneStaleFiles(_dir, Written(kept), new[] { "keepme.txt", "*.bak" });

        Assert.IsTrue(File.Exists(Path.Combine(_dir, "keepme.txt")), "exact-name exclude must protect the file");
        Assert.IsTrue(File.Exists(Path.Combine(_dir, "data.bak")), "wildcard exclude must protect the file");
        Assert.AreEqual(0, removed.Count);
    }

    [TestMethod]
    public void ExcludeGlobMatchesByRelativePath()
    {
        var kept = Touch("app.js");
        Touch("vendor/lib.js");         // stale but under a protected subtree

        OutputBuilder.PruneStaleFiles(_dir, Written(kept), new[] { "vendor/*" });

        Assert.IsTrue(File.Exists(Path.Combine(_dir, "vendor", "lib.js")), "path-glob exclude must protect nested files");
    }

    [TestMethod]
    public void NoStaleFilesLeavesEverythingInPlace()
    {
        var a = Touch("app.js");
        var b = Touch("app.meta.js");

        var removed = OutputBuilder.PruneStaleFiles(_dir, Written(a, b), Array.Empty<string>());

        Assert.AreEqual(0, removed.Count);
        Assert.IsTrue(File.Exists(a) && File.Exists(b));
    }
}
