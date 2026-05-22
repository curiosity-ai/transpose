using H5.Compiler.Hosted;
using H5.Translator;
using H5.Translator.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace H5.Compiler.Service.Tests
{
    /// <summary>
    /// End-to-end tests for the BundleDependencies h5.json option (which writes
    /// an ".h5" archive into the H5 output folder containing every other file
    /// from the project's binary output directory) and for the CompilationRequest
    /// WithBundle injection path that exposes a bundle as a project reference.
    /// </summary>
    [TestClass]
    public class BundleInjectionTests
    {
        [TestMethod]
        public void BundleDependencies_WritesBundleIntoH5OutputFolder()
        {
            using var compiler = new TestCompiler();

            var sources = new Dictionary<string, string>
            {
                { "Lib.cs", "public static class Lib { public static int Answer() { return 42; } }" }
            };

            compiler.Compile(
                sources,
                rebuild: true,
                configureSettings: s => s.BundleDependencies = true,
                assemblyName: "BundledLib");

            var expectedBundle = Path.Combine(compiler.H5OutputDir, "BundledLib.h5");
            Assert.IsTrue(File.Exists(expectedBundle), $"Expected bundle file at {expectedBundle}");

            using var fs = File.OpenRead(expectedBundle);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
            var entryNames = archive.Entries.Select(e => e.FullName).ToList();

            Assert.IsTrue(entryNames.Any(n => n.EndsWith("BundledLib.dll", StringComparison.OrdinalIgnoreCase)),
                "Bundle must contain the compiled assembly");

            Assert.IsFalse(entryNames.Any(n => n.StartsWith("h5/", StringComparison.OrdinalIgnoreCase)),
                "Bundle must not include any file from the H5 output folder");

            Assert.IsFalse(entryNames.Any(n => n.EndsWith(".h5", StringComparison.OrdinalIgnoreCase)),
                "Bundle must not include itself");
        }

        [TestMethod]
        public void BundleDependencies_UsesCustomBundleFileName()
        {
            using var compiler = new TestCompiler();

            var sources = new Dictionary<string, string>
            {
                { "Lib.cs", "public static class Lib { public static int Answer() { return 7; } }" }
            };

            compiler.Compile(
                sources,
                rebuild: true,
                configureSettings: s =>
                {
                    s.BundleDependencies = true;
                    s.BundleFileName = "my-custom-name";
                },
                assemblyName: "BundledLib");

            var expectedBundle = Path.Combine(compiler.H5OutputDir, "my-custom-name.h5");
            Assert.IsTrue(File.Exists(expectedBundle),
                $"Expected bundle at custom path {expectedBundle}");
        }

        [TestMethod]
        public void BundleDependencies_DefaultIsDisabled()
        {
            using var compiler = new TestCompiler();

            var sources = new Dictionary<string, string>
            {
                { "Lib.cs", "public static class Lib { public static int Answer() { return 1; } }" }
            };

            compiler.Compile(sources, rebuild: true);

            var any = Directory.Exists(compiler.H5OutputDir)
                && Directory.EnumerateFiles(compiler.H5OutputDir, "*.h5", SearchOption.AllDirectories).Any();

            Assert.IsFalse(any, "No .h5 bundle should be produced when BundleDependencies is false");
        }

        [TestMethod]
        public void WithBundle_ExtractsBundleAndAddsAssembliesAsProjectReferences()
        {
            // Step 1: compile a small library and let the compiler bundle its
            // output directory (excluding the H5 output folder).
            using var libCompiler = new TestCompiler();

            var libSources = new Dictionary<string, string>
            {
                { "Lib.cs", "namespace Lib { public static class Math2 { public static int Double(int n) { return n + n; } } }" }
            };

            libCompiler.Compile(
                libSources,
                rebuild: true,
                configureSettings: s => s.BundleDependencies = true,
                assemblyName: "Lib");

            var bundlePath = Path.Combine(libCompiler.H5OutputDir, "Lib.h5");
            Assert.IsTrue(File.Exists(bundlePath), "Library compilation must produce a bundle");
            var bundleBytes = File.ReadAllBytes(bundlePath);

            // Step 2: drive the request-side injection path directly. We do not
            // run a full second compilation here because the C# compiler embeds
            // synthesized attributes into every test DLL that conflict with H5's
            // attribute-aware emit (a real h5.Target build suppresses them). The
            // contract we want to assert is the one we own: a WithBundle call
            // unpacks the archive into the source directory and rewrites the
            // generated csproj so every .dll in the bundle becomes a Reference.
            var request = new CompilationRequest("Consumer", new H5DotJson_AssemblySettings())
                .WithBundle("Lib", bundleBytes)
                .WithSourceFile("App.cs", "public static class App { public static int Run() { return Lib.Math2.Double(21); } }");

            var stagingSource = Path.Combine(libCompiler.WorkingDirectory, "consumer-src");
            Directory.CreateDirectory(stagingSource);

            var options = request.ToOptions(stagingSource, NuGetVersion.Parse("10.0.0"));

            var bundleStaging = Path.Combine(stagingSource, ".h5bundles", "Lib");
            Assert.IsTrue(Directory.Exists(bundleStaging), $"Bundle should be unpacked to {bundleStaging}");
            var extractedDlls = Directory.EnumerateFiles(bundleStaging, "Lib.dll", SearchOption.AllDirectories).ToList();
            Assert.AreEqual(1, extractedDlls.Count, "Lib.dll must be extracted from the injected bundle");

            var csprojContents = File.ReadAllText(options.ProjectLocation);
            Assert.IsTrue(csprojContents.Contains("Include=\"Lib\""),
                "Generated csproj should add the bundled DLL as a <Reference> include");
            Assert.IsTrue(csprojContents.Contains(extractedDlls[0]),
                "Generated csproj should HintPath the extracted DLL");
        }

        [TestMethod]
        public void WithBundleFile_LoadsBundleFromDisk()
        {
            using var libCompiler = new TestCompiler();
            var libSources = new Dictionary<string, string>
            {
                { "Lib.cs", "namespace LibDisk { public static class Helper { public static string Greet() { return \"hi\"; } } }" }
            };
            libCompiler.Compile(
                libSources,
                rebuild: true,
                configureSettings: s => s.BundleDependencies = true,
                assemblyName: "LibDisk");

            var bundlePath = Path.Combine(libCompiler.H5OutputDir, "LibDisk.h5");
            Assert.IsTrue(File.Exists(bundlePath));

            var request = new CompilationRequest("Consumer", new H5DotJson_AssemblySettings())
                .WithBundleFile(bundlePath)
                .WithSourceFile("App.cs", "public static class App { public static string Run() { return LibDisk.Helper.Greet(); } }");

            var stagingSource = Path.Combine(libCompiler.WorkingDirectory, "consumer-disk-src");
            Directory.CreateDirectory(stagingSource);

            var options = request.ToOptions(stagingSource, NuGetVersion.Parse("10.0.0"));

            var staging = Path.Combine(stagingSource, ".h5bundles", "LibDisk");
            Assert.IsTrue(Directory.Exists(staging), "WithBundleFile must extract bundle next to source");
            Assert.IsTrue(Directory.EnumerateFiles(staging, "LibDisk.dll", SearchOption.AllDirectories).Any(),
                "Bundle loaded from disk must yield the inner DLL");

            var csprojContents = File.ReadAllText(options.ProjectLocation);
            Assert.IsTrue(csprojContents.Contains("Include=\"LibDisk\""));
        }
    }
}
