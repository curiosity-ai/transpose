using System.Globalization;
using System.Text.RegularExpressions;
using Transpose.Compiler;

namespace Transpose.Translator.Tests;

/// <summary>
/// The size a package build reports is the size of the file it wrote.
///
/// It was not. The success line reported <c>result.AssemblyBytes.Length</c> — what Roslyn emitted —
/// but the JavaScript, stylesheets and fonts are injected into that assembly <em>afterwards</em>, by
/// <c>ResourceEmbedder</c>. For a library that ships its own assets they are most of what the package
/// weighs, so Tesserae's build announced a 1.8 MB package and left a 17.1 MB file on disk: a ninefold
/// understatement of the one number a developer reads to judge how big their library got.
///
/// The regression is invisible to every other test — the DLL was always written correctly, only the
/// report was wrong — so it is worth pinning directly against the file.
/// </summary>
[TestClass]
public sealed class PackageSizeReportTests
{
    private string _root = "";
    private string _projectDir = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "tps-pkgsize-" + Guid.NewGuid().ToString("N"));
        _projectDir = Path.Combine(_root, "proj");
        Directory.CreateDirectory(_projectDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>Collects the build's own progress output so the reported line can be read back.</summary>
    private sealed class CapturingLog : BuildLog
    {
        public readonly List<string> Lines = new();
        public override void Info(string message) => Lines.Add(message);
        public override void Error(string message) => Lines.Add(message);
    }

    [TestMethod]
    public void TheReportedPackageSizeIsTheSizeOfTheFileOnDisk()
    {
        // An asset big enough that embedding it cannot be lost in rounding: if the report were still
        // measuring the pre-injection assembly, it would miss all 512 KB of this.
        var assets = Path.Combine(_projectDir, "tps", "assets");
        Directory.CreateDirectory(assets);
        File.WriteAllText(Path.Combine(assets, "big.css"), new string('a', 512 * 1024));

        File.WriteAllText(Path.Combine(_projectDir, "tps.json"), @"{
            ""fileName"": ""Lib.js"",
            ""resources"": [
                { ""name"": ""big.css"", ""files"": [ ""tps/assets/big.css"" ], ""output"": ""assets"" }
            ]
        }");
        File.WriteAllText(Path.Combine(_projectDir, "Lib.cs"), "namespace Lib { public class Thing { public int N; } }");

        var csproj = Path.Combine(_projectDir, "Lib.csproj");
        File.WriteAllText(csproj, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <AssemblyName>Lib</AssemblyName>
    <IsPackable>true</IsPackable>
  </PropertyGroup>
</Project>");

        var log = new CapturingLog();
        var outcome = ProjectBuild.Run(ProjectBuild.ResolveOutputMode(new BuildOptions
        {
            CsprojPath = csproj,
            Configuration = "Release",
            EmitPackage = true,
            SeparateAssemblies = true,
        }), log);

        Assert.AreEqual(0, outcome.ExitCode, "the package build must succeed:\n" + string.Join("\n", log.Lines));

        var dll = Path.Combine(_projectDir, "bin", "Release", "netstandard2.0", "Lib.dll");
        Assert.IsTrue(File.Exists(dll), "the package DLL must have been written");
        var onDisk = new FileInfo(dll).Length;

        var line = log.Lines.FirstOrDefault(l => l.Contains("built package"));
        Assert.IsNotNull(line, "the build must report what it produced:\n" + string.Join("\n", log.Lines));

        var reported = Regex.Match(line!, @"\(([\d,]+) bytes");
        Assert.IsTrue(reported.Success, $"the report must name a byte count: {line}");
        var bytes = long.Parse(reported.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

        Assert.AreEqual(onDisk, bytes,
            $"the reported size must be the file's, not the pre-embedding assembly's: {line}");
        Assert.IsTrue(onDisk > 512 * 1024,
            "sanity: the asset really was embedded, so the two numbers could have disagreed");
    }
}
