using System.Text.RegularExpressions;
using Mono.Cecil;
using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// Covers the minimum-compiler-version stamp (<see cref="BuildStamp"/>): every assembly tps builds
/// records the compiler that produced it, and a build refuses to bind against an assembly that was
/// built by a *newer* compiler than the one running — telling the user to update the tool rather than
/// producing a bundle that is subtly wrong at runtime.
///
/// The gate itself is off in this test run (a Debug build of the compiler carries no version; see
/// <see cref="CompilerVersion.EnforceMinimum"/>), so the checks below drive the explicit-version
/// overload, which is the same code path with the "does this compiler enforce anything" question
/// already answered.
/// </summary>
[TestClass]
public sealed class MinimumCompilerVersionTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "tps-minver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    // ---- the stamp -------------------------------------------------------------------------------

    [TestMethod]
    public void EveryPackageAssemblyCarriesTheStampOfTheCompilerThatBuiltIt()
    {
        var dll = EmbedPackage("lib.dll");

        var stamp = BuildStamp.TryRead(dll);
        Assert.IsNotNull(stamp, "a package DLL must carry its Transpose.Build.json stamp");
        Assert.AreEqual(CompilerVersion.Text, stamp!.CompilerVersion);
        // The minimum a package declares is the compiler that built it: consumable by that compiler
        // and by anything newer.
        Assert.AreEqual(CompilerVersion.Text, stamp.MinimumCompilerVersion);
    }

    [TestMethod]
    public void TheStampIsNotListedAsAWebResource()
    {
        // It is compiler metadata, not JavaScript: if it appeared in Transpose.Resources.json, every
        // consuming site build would extract Transpose.Build.json into its output folder.
        var dll = EmbedPackage("lib.dll");

        using var assembly = AssemblyDefinition.ReadAssembly(dll);
        var manifest = assembly.MainModule.Resources.OfType<EmbeddedResource>()
            .Single(r => r.Name == "Transpose.Resources.json");
        var json = System.Text.Encoding.UTF8.GetString(manifest.GetResourceData());

        Assert.IsTrue(assembly.MainModule.Resources.Any(r => r.Name == BuildStamp.ResourceName),
            "the stamp must be embedded");
        Assert.IsFalse(json.Contains(BuildStamp.ResourceName, StringComparison.OrdinalIgnoreCase),
            $"the stamp must not be listed in the resource manifest: {json}");
    }

    [TestMethod]
    public void ReStampingReplacesTheExistingStampInsteadOfAddingASecond()
    {
        // A package assembly can go through the embedder more than once (a site build writes the DLL
        // and the package DLL from one compilation); two stamps would make the resource ambiguous.
        var dll = EmbedPackage("lib.dll");
        ResourceEmbedder.Embed(dll, File.ReadAllBytes(dll), OneScript());

        using var assembly = AssemblyDefinition.ReadAssembly(dll);
        Assert.AreEqual(1, assembly.MainModule.Resources.Count(r => r.Name == BuildStamp.ResourceName));
    }

    [TestMethod]
    public void AnAssemblyWithNoStampReadsBackAsUnstamped()
    {
        // Every .NET assembly that is not a Transpose package — and every Transpose package built
        // before the stamp existed — must simply be skipped by the check.
        var plain = BuildAssembly("plain.dll", "plain", "public class C { }");
        Assert.IsNull(BuildStamp.TryRead(plain));
        Assert.IsNull(BuildStamp.CheckReferences(new[] { plain }, new Version("26.7.500")));
    }

    // ---- the check -------------------------------------------------------------------------------

    [TestMethod]
    public void AReferenceBuiltByANewerCompilerFailsTheBuildWithTheUpdateCommand()
    {
        var dll = StampedPackage("Transpose.Core.dll", "26.8.100");

        var diagnostic = BuildStamp.CheckReferences(new[] { dll }, new Version("26.7.500"));

        Assert.IsNotNull(diagnostic, "a reference that needs a newer compiler must fail the build");
        Assert.AreEqual(MsBuildDiagnostic.CodeCompilerTooOld, diagnostic!.Id);

        var line = MsBuildDiagnostic.Format(diagnostic);
        StringAssert.Contains(line, "'Transpose.Core' (26.8.100)");
        StringAssert.Contains(line, "26.7.500");
        StringAssert.Contains(line, "dotnet tool install --global Transpose.Compiler");

        // The message has to survive MSBuild's line-based parser to reach the IDE at all.
        var m = Regex.Match(line, Canonical, RegexOptions.IgnoreCase);
        Assert.IsTrue(m.Success, $"MSBuild would not recognise this as a diagnostic: {line}");
        Assert.AreEqual("error", m.Groups["CATEGORY"].Value);
        Assert.AreEqual(MsBuildDiagnostic.CodeCompilerTooOld, m.Groups["CODE"].Value);
    }

    [TestMethod]
    public void TheSameOrAnOlderRequirementPassesUntouched()
    {
        var same = StampedPackage("same.dll", "26.7.500");
        var older = StampedPackage("older.dll", "26.1.1");

        Assert.IsNull(BuildStamp.CheckReferences(new[] { same, older }, new Version("26.7.500")));
    }

    [TestMethod]
    public void AThreePartRequirementIsNotTreatedAsNewerThanTheSameFourPartVersion()
    {
        // Version compares an unspecified component as less than zero, so 26.7.500 would sort before
        // 26.7.500.0 without normalisation — and both spellings occur (a package version is three-part,
        // an AssemblyVersion four-part).
        var dll = StampedPackage("lib.dll", "26.7.500");

        Assert.IsNull(BuildStamp.CheckReferences(new[] { dll }, new Version("26.7.500.0")));
    }

    [TestMethod]
    public void ADevTreeStampNeverFailsAnyCompiler()
    {
        // Packages built in a dev tree carry the 0.0.0 placeholder; they must stay consumable by every
        // released compiler, otherwise locally-built libraries could not be tested against one.
        var dll = StampedPackage("dev.dll", CompilerVersion.Unversioned);

        Assert.IsNull(BuildStamp.CheckReferences(new[] { dll }, new Version("26.7.500")));
    }

    [TestMethod]
    public void TheMessageNamesTheHighestRequirementAndCountsTheRest()
    {
        var dlls = new[]
        {
            StampedPackage("a.dll", "26.8.1"),
            StampedPackage("b.dll", "27.1.9"),
            StampedPackage("c.dll", "26.8.2"),
            StampedPackage("d.dll", "26.8.3"),
            StampedPackage("e.dll", "26.8.4"),
            StampedPackage("f.dll", "26.8.5"),
        };

        var text = MsBuildDiagnostic.Format(BuildStamp.CheckReferences(dlls, new Version("26.7.500"))!);

        // Highest first (it is the version the user has to install), four named, the tail counted.
        StringAssert.Contains(text, "'b' (27.1.9)");
        StringAssert.Contains(text, "and 2 more");
        StringAssert.Contains(text, "27.1.9 or newer is required");
        Assert.IsFalse(text.Contains('\n'), "an MSBuild diagnostic must be one line");
    }

    [TestMethod]
    public void ADuplicatedReferenceIsReportedOnce()
    {
        var dll = StampedPackage("lib.dll", "26.8.100");

        var text = MsBuildDiagnostic.Format(
            BuildStamp.CheckReferences(new[] { dll, dll, Path.Combine(_root, "..", Path.GetFileName(_root), "lib.dll") },
                new Version("26.7.500"))!);

        Assert.AreEqual(1, Regex.Matches(text, Regex.Escape("'lib'")).Count, text);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    /// <summary>Verbatim from MSBuild's <c>CanonicalError</c>; see <see cref="MsBuildDiagnosticFormatTests"/>.</summary>
    private const string Canonical =
        @"^\s*(((?<ORIGIN>(((\d+>)?[a-zA-Z]?:[^:]*)|([^:]*))):)|())(?<SUBCATEGORY>(()|([^:]*? )))(?<CATEGORY>(error|warning))( \s*(?<CODE>[^: ]*))?\s*:(?<TEXT>.*)$";

    private static IReadOnlyList<EmbeddedItem> OneScript()
        => new[] { new EmbeddedItem("app.js", System.Text.Encoding.UTF8.GetBytes("// js"), null) };

    /// <summary>Compiles a trivial assembly and writes it to <paramref name="rel"/>.</summary>
    private string BuildAssembly(string rel, string assemblyName, string source)
    {
        var result = new RoslynTranslator().BuildAssembly(
            new[] { (assemblyName + ".cs", source) }, assemblyName, Array.Empty<string>(),
            emitAssembly: true, emitDebugInformation: false);
        Assert.IsTrue(result.Success, $"{assemblyName} failed to compile: " +
            string.Join("\n", result.Diagnostics.Select(d => d.GetMessage())));

        var path = Path.Combine(_root, rel);
        File.WriteAllBytes(path, result.AssemblyBytes!);
        return path;
    }

    /// <summary>A package DLL as <c>tps --emit-package</c> writes it: the emitted assembly with its JS
    /// and the current compiler's stamp embedded.</summary>
    private string EmbedPackage(string fileName)
    {
        var bytes = File.ReadAllBytes(BuildAssembly("_raw_" + fileName, Path.GetFileNameWithoutExtension(fileName), "public class C { }"));
        var path = Path.Combine(_root, fileName);
        ResourceEmbedder.Embed(path, bytes, OneScript());
        return path;
    }

    /// <summary>A package DLL stamped as if built by <paramref name="version"/> — what a package
    /// downloaded from a feed and built by some other compiler looks like.</summary>
    private string StampedPackage(string fileName, string version)
    {
        var path = EmbedPackage(fileName);
        using (var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadWrite = true }))
        {
            var resources = assembly.MainModule.Resources;
            for (var i = resources.Count - 1; i >= 0; i--)
                if (resources[i].Name == BuildStamp.ResourceName) resources.RemoveAt(i);
            resources.Add(new EmbeddedResource(BuildStamp.ResourceName, ManifestResourceAttributes.Private,
                new BuildStamp(version, version).ToJsonBytes()));
            assembly.Write();
        }
        Assert.AreEqual(version, BuildStamp.TryRead(path)!.MinimumCompilerVersion);
        return path;
    }
}
