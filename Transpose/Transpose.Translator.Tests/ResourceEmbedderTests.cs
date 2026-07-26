using Mono.Cecil;
using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Covers how <see cref="ResourceEmbedder"/> resolves the assemblies a package DLL references.
///
/// Embedding the compiled JavaScript re-serializes the emitted assembly's metadata through Mono.Cecil,
/// and Cecil resolves a referenced assembly whenever it has to encode a constant whose type lives there
/// — a <c>const</c> field or a parameter's default value of a referenced enum, because the constant
/// blob stores the enum's *underlying* type. Cecil's own resolver searches directories by simple name
/// and takes the first <c>&lt;name&gt;.dll</c> it finds, starting with the folder the DLL is written to,
/// where a copy-local copy of a reference from an earlier build routinely sits. Binding there instead of
/// to the reference the compilation used made a type that plainly exists look missing:
/// <c>Mono.Cecil.ResolutionException: Failed to resolve Tesserae.PixelAvatarDesign</c>, from a project
/// whose C# compiled cleanly against a package that does define the enum.
/// </summary>
[TestClass]
public sealed class ResourceEmbedderTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "tps-embed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>Compiles one source file into a real assembly at <paramref name="rel"/>.</summary>
    private string BuildAssembly(string rel, string assemblyName, string source, params string[] references)
    {
        var result = new RoslynTranslator().BuildAssembly(
            new[] { (assemblyName + ".cs", source) }, assemblyName, references,
            emitAssembly: true, emitDebugInformation: false);

        Assert.IsTrue(result.Success, $"{assemblyName} failed to compile: " +
            string.Join("\n", result.Diagnostics.Select(d => d.GetMessage())));

        var path = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, result.AssemblyBytes!);
        return path;
    }

    private static IReadOnlyList<EmbeddedItem> OneScript()
        => new[] { new EmbeddedItem("app.js", System.Text.Encoding.UTF8.GetBytes("// js"), null) };

    [TestMethod]
    public void ResolvesAReferencedEnumConstantThroughTheReferenceTheCompilationUsed()
    {
        // The reported bug. `lib` exists twice: the real reference the app compiled against, and an
        // older copy — same file name, no `Design` enum — left in the output folder by an earlier build.
        var lib = BuildAssembly("packages/lib/lib.dll", "lib",
            "namespace Pkg { public enum Design { Black, Sudo } }");
        BuildAssembly("bin/lib.dll", "lib",
            "namespace Pkg { public enum SomethingElse { One } }");

        var appBytes = CompileApp(lib);
        var appPath  = Path.Combine(_root, "bin", "app.dll");

        ResourceEmbedder.Embed(appPath, appBytes, OneScript(), new[] { lib });

        Assert.IsTrue(ResourceEmbedder.HasManifest(appPath), "the package must carry its resource manifest");
        using var written = AssemblyDefinition.ReadAssembly(appPath);
        var design = written.MainModule.GetType("App.Avatar").Fields.Single(f => f.Name == "DESIGN");
        Assert.AreEqual(1, design.Constant, "the enum constant must survive the round-trip");
    }

    [TestMethod]
    public void FallsBackToADirectorySearchForAnAssemblyOutsideTheReferenceSet()
    {
        // A reference the caller didn't pass still resolves the way it always did — from the output
        // folder next to the DLL being written.
        var lib = BuildAssembly("bin/lib.dll", "lib",
            "namespace Pkg { public enum Design { Black, Sudo } }");

        var appBytes = CompileApp(lib);
        var appPath  = Path.Combine(_root, "bin", "app.dll");

        ResourceEmbedder.Embed(appPath, appBytes, OneScript(), referencePaths: null);

        Assert.IsTrue(ResourceEmbedder.HasManifest(appPath));
    }

    /// <summary>An assembly holding both constant shapes that make Cecil resolve a referenced type:
    /// a <c>const</c> field (the reported crash) and a parameter default value.</summary>
    private byte[] CompileApp(string libPath)
    {
        var result = new RoslynTranslator().BuildAssembly(
            new[] { ("app.cs", @"
using Pkg;
namespace App
{
    public static class Avatar
    {
        public const Design DESIGN = Design.Sudo;
        public static int New(Design design = Design.Sudo) => (int)design;
    }
}") }, "app", new[] { libPath }, emitAssembly: true, emitDebugInformation: false);

        Assert.IsTrue(result.Success, "app failed to compile: " +
            string.Join("\n", result.Diagnostics.Select(d => d.GetMessage())));
        return result.AssemblyBytes!;
    }
}
