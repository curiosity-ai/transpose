using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests;

/// <summary>
/// <see cref="CompilationBuilder"/> reads each reference assembly into one
/// <see cref="MetadataReference"/> and reuses it across compilations.
///
/// <para>
/// Roslyn decodes a reference's metadata — and caches the symbols it builds from it — against the
/// <see cref="MetadataReference"/> <b>instance</b>, and holds the result in native memory the GC
/// cannot account for. A fresh instance per compilation therefore leaked ~12 MB per compile for the
/// 10.4 MB base library alone: measured over 300 translations, RSS grew to 3.7 GB while the managed
/// heap stayed at 337 MB, and an aggressive full collect gave back almost none of it. That is what
/// walked this suite's own test host up to the container's 13 GB limit and had it OOM-killed with
/// ~80 tests still to run. It costs a one-shot <c>tps</c> build nothing (it compiles once) and
/// matters wherever a process compiles more than once: the suites, <c>tps --watch</c>, and
/// <c>Transpose.Compiler.Library</c> hosts.
/// </para>
///
/// <para>
/// The reuse is only sound while a reference that changed <em>on disk</em> is re-read, which is not a
/// hypothetical: watch mode rebuilds a referenced project's DLL between two compilations and the
/// second must bind against the new one. That is what the last two tests are about.
/// </para>
/// </summary>
[TestClass]
public sealed class MetadataReferenceCacheTests
{
    private string _dir = "";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tps-refcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>Writes a one-method library to <paramref name="path"/>, overwriting whatever is
    /// there — which is what a rebuild of a referenced project does.</summary>
    private static void WriteLibrary(string path, string methodName)
    {
        var result = new RoslynTranslator().BuildAssembly(
            new[] { ("Lib.cs", $"public static class Lib {{ public static int {methodName}() => 1; }}") },
            "Lib", extraReferencePaths: null, emitAssembly: true);

        Assert.IsNotNull(result.AssemblyBytes,
            "building the stand-in library failed: "
            + string.Join("\n", result.Diagnostics.Select(d => d.GetMessage())));
        File.WriteAllBytes(path, result.AssemblyBytes!);
    }

    private static string? TranslateAgainst(string libraryPath, string methodName)
    {
        var result = new RoslynTranslator().Translate(
            new[] { ("App.cs", $"public class P {{ public static int M() => Lib.{methodName}(); }}") },
            "App", new[] { libraryPath });
        return result.Success ? result.Javascript : null;
    }

    [TestMethod]
    public void TheSameReferenceFileIsReadOnceAcrossCompilations()
    {
        var first = CompilationBuilder.Build(new[] { ("App.cs", "public class P { }") }, "App");
        var second = CompilationBuilder.Build(new[] { ("App.cs", "public class Q { }") }, "App");

        var a = first.References.OfType<PortableExecutableReference>().Single();
        var b = second.References.OfType<PortableExecutableReference>().Single();

        Assert.AreSame(a, b,
            "two compilations must bind against the SAME MetadataReference for the base library — a "
            + "fresh one per compilation makes Roslyn re-decode 10 MB of metadata into native memory "
            + "that nothing reclaims");
    }

    [TestMethod]
    public void ARebuiltReferenceIsReReadNotServedStale()
    {
        var lib = Path.Combine(_dir, "Lib.dll");

        WriteLibrary(lib, "One");
        Assert.IsNotNull(TranslateAgainst(lib, "One"), "sanity: the first build's member binds");

        WriteLibrary(lib, "Two");

        Assert.IsNotNull(TranslateAgainst(lib, "Two"),
            "the rebuilt library's member must bind — a cache that answered from the first read would "
            + "report it as undefined, which is exactly what watch mode does between two compilations");
        Assert.IsNull(TranslateAgainst(lib, "One"),
            "and the member the rebuild removed must be gone, so this is a re-read and not both "
            + "assemblies at once");
    }

    [TestMethod]
    public void ARebuiltReferenceOfIdenticalSizeIsReReadToo()
    {
        var lib = Path.Combine(_dir, "Lib.dll");

        WriteLibrary(lib, "One");
        var size = new FileInfo(lib).Length;
        Assert.IsNotNull(TranslateAgainst(lib, "One"), "sanity: the first build's member binds");

        // A rename of the same length, so the file's SIZE cannot tell the two builds apart and only
        // the timestamp is left to. Stamped a second on: a real rebuild is seconds later, and pinning
        // it here keeps the test off the filesystem's timestamp granularity.
        WriteLibrary(lib, "Ona");
        Assert.AreEqual(size, new FileInfo(lib).Length, "the two builds must be the same size for this "
            + "test to be about the timestamp at all");
        File.SetLastWriteTimeUtc(lib, DateTime.UtcNow.AddSeconds(1));

        Assert.IsNotNull(TranslateAgainst(lib, "Ona"),
            "a rebuild the file size cannot distinguish must still be re-read");
    }
}
