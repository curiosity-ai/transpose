using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Covers the "clean output folder" prune (<see cref="OutputBuilder.PruneStaleFiles"/>). The prune is
/// manifest-based: its candidate set is the files <em>this project</em> wrote in a previous build (the
/// persisted manifest), not "every file in the folder". So a file the current build no longer writes
/// is removed, current outputs are always kept, directories emptied by the prune are cleaned up,
/// <c>cleanOutputFolderExclude</c> globs protect hand-placed files, and — crucially — a file this
/// project never authored (dropped by a sibling entry app, a package, or MSBuild into a shared output
/// folder) is never a candidate and always survives.
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

    private HashSet<string> Set(params string[] fullPaths)
        => new(fullPaths, OutputBuilder.PathComparer);

    [TestMethod]
    public void PrunesOnlyFilesThePreviousBuildWroteButThisOneDidNot()
    {
        var kept     = Touch("app.js");
        var alsoKept = Touch("index.html");
        var stale    = Touch("old-app.js");     // in the previous manifest, not re-written now
        var staleMin = Touch("app.min.js");     // variant no longer produced

        var written  = Set(kept, alsoKept);
        var previous = Set(kept, alsoKept, stale, staleMin);

        var removed = OutputBuilder.PruneStaleFiles(_dir, written, previous, Array.Empty<string>());

        Assert.IsTrue(File.Exists(kept), "current output app.js must survive");
        Assert.IsTrue(File.Exists(alsoKept), "current output index.html must survive");
        Assert.IsFalse(File.Exists(stale), "stale file must be pruned");
        Assert.IsFalse(File.Exists(staleMin), "stale variant must be pruned");
        Assert.AreEqual(2, removed.Count);
    }

    [TestMethod]
    public void FilesThisProjectNeverAuthoredAreNeverPruned()
    {
        // The reported bug: a shared output folder also holds assets a *sibling* project/package wrote
        // (here msk.chatview.css). They are absent from this project's manifest, so even though the
        // current build didn't write them they must not be touched.
        var kept    = Touch("app.js");
        var stale   = Touch("old-app.js");                  // this project's own stale output
        var foreign = Touch("assets/css/msk.chatview.css"); // authored by another project — must survive

        var written  = Set(kept);
        var previous = Set(kept, stale);                    // foreign file is NOT in this project's manifest

        var removed = OutputBuilder.PruneStaleFiles(_dir, written, previous, Array.Empty<string>());

        Assert.IsTrue(File.Exists(foreign), "a file this project never authored must never be pruned");
        Assert.IsFalse(File.Exists(stale), "this project's own stale output must still be pruned");
        Assert.AreEqual(1, removed.Count);
    }

    [TestMethod]
    public void RemovesDirectoriesLeftEmptyByThePrune()
    {
        var kept  = Touch("app.js");
        var stale = Touch("oldassets/old.css");     // stale, and its folder becomes empty

        OutputBuilder.PruneStaleFiles(_dir, Set(kept), Set(kept, stale), Array.Empty<string>());

        Assert.IsFalse(Directory.Exists(Path.Combine(_dir, "oldassets")), "emptied directory must be removed");
        Assert.IsTrue(Directory.Exists(_dir), "the output root itself must remain");
    }

    [TestMethod]
    public void KeepsNonEmptyDirectories()
    {
        var kept  = Touch("assets/app.js");
        var stale = Touch("assets/stale.js");       // stale sibling; folder still has app.js afterwards

        OutputBuilder.PruneStaleFiles(_dir, Set(kept), Set(kept, stale), Array.Empty<string>());

        Assert.IsTrue(File.Exists(kept));
        Assert.IsFalse(File.Exists(Path.Combine(_dir, "assets", "stale.js")));
        Assert.IsTrue(Directory.Exists(Path.Combine(_dir, "assets")), "a directory that still holds output must remain");
    }

    [TestMethod]
    public void ExcludeGlobProtectsMatchingFilesByName()
    {
        var kept   = Touch("app.js");
        var txt    = Touch("keepme.txt");            // stale but protected by name
        var bak    = Touch("data.bak");              // stale but protected by *.bak

        var removed = OutputBuilder.PruneStaleFiles(_dir, Set(kept), Set(kept, txt, bak), new[] { "keepme.txt", "*.bak" });

        Assert.IsTrue(File.Exists(Path.Combine(_dir, "keepme.txt")), "exact-name exclude must protect the file");
        Assert.IsTrue(File.Exists(Path.Combine(_dir, "data.bak")), "wildcard exclude must protect the file");
        Assert.AreEqual(0, removed.Count);
    }

    [TestMethod]
    public void ExcludeGlobMatchesByRelativePath()
    {
        var kept  = Touch("app.js");
        var stale = Touch("vendor/lib.js");         // stale but under a protected subtree

        OutputBuilder.PruneStaleFiles(_dir, Set(kept), Set(kept, stale), new[] { "vendor/*" });

        Assert.IsTrue(File.Exists(Path.Combine(_dir, "vendor", "lib.js")), "path-glob exclude must protect nested files");
    }

    [TestMethod]
    public void NoStaleFilesLeavesEverythingInPlace()
    {
        var a = Touch("app.js");
        var b = Touch("app.meta.js");

        var removed = OutputBuilder.PruneStaleFiles(_dir, Set(a, b), Set(a, b), Array.Empty<string>());

        Assert.AreEqual(0, removed.Count);
        Assert.IsTrue(File.Exists(a) && File.Exists(b));
    }

    [TestMethod]
    public void EmptyPreviousManifestPrunesNothing()
    {
        // First build after upgrading from a manifest-less compiler: no prior manifest, so nothing is a
        // candidate even though the folder holds files the current build didn't write.
        var kept    = Touch("app.js");
        var unknown = Touch("mystery.js");

        var removed = OutputBuilder.PruneStaleFiles(_dir, Set(kept), Set(), Array.Empty<string>());

        Assert.IsTrue(File.Exists(unknown), "with no previous manifest the prune must be a no-op");
        Assert.AreEqual(0, removed.Count);
    }
}
