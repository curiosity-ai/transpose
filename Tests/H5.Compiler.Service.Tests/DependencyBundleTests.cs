using H5.Translator.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace H5.Compiler.Service.Tests
{
    /// <summary>
    /// Direct tests for the <see cref="DependencyBundle"/> helper that backs the
    /// new BundleDependencies / WithBundle features.
    /// </summary>
    [TestClass]
    public class DependencyBundleTests
    {
        [TestMethod]
        public void Create_PacksFilesAndHonoursExclude()
        {
            using var scratch = new ScratchDir();

            var source = Path.Combine(scratch.Path, "bin");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "MyAssembly.dll"), "fake dll bytes");
            File.WriteAllText(Path.Combine(source, "MyAssembly.pdb"), "fake pdb bytes");

            var deepDir = Path.Combine(source, "deps", "thirdparty");
            Directory.CreateDirectory(deepDir);
            File.WriteAllText(Path.Combine(deepDir, "Other.dll"), "another");

            var excluded = Path.Combine(source, "h5");
            Directory.CreateDirectory(excluded);
            File.WriteAllText(Path.Combine(excluded, "app.js"), "console.log('hi');");
            File.WriteAllText(Path.Combine(excluded, "app.html"), "<html></html>");

            var bundle = Path.Combine(scratch.Path, "output.h5");

            var count = DependencyBundle.Create(source, excluded, bundle);

            Assert.IsTrue(File.Exists(bundle), "Bundle file should exist");
            Assert.AreEqual(3, count, "Expected 3 entries (dll + pdb + nested dll), excluded h5 folder entries");

            using var fs = File.OpenRead(bundle);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
            var entries = archive.Entries.Select(e => e.FullName).OrderBy(s => s).ToList();

            CollectionAssert.Contains(entries, "MyAssembly.dll");
            CollectionAssert.Contains(entries, "MyAssembly.pdb");
            CollectionAssert.Contains(entries, "deps/thirdparty/Other.dll");
            Assert.IsFalse(entries.Any(e => e.StartsWith("h5/")), "h5/ contents should not be bundled");
        }

        [TestMethod]
        public void Create_DoesNotIncludeTheBundleItself()
        {
            using var scratch = new ScratchDir();

            var source = Path.Combine(scratch.Path, "bin");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "App.dll"), "x");

            var output = Path.Combine(source, "h5");
            Directory.CreateDirectory(output);
            var bundle = Path.Combine(output, "App.h5");

            DependencyBundle.Create(source, output, bundle);

            using var fs = File.OpenRead(bundle);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
            Assert.IsFalse(archive.Entries.Any(e => e.FullName.EndsWith(".h5", StringComparison.OrdinalIgnoreCase)),
                "Bundle file must not be included in itself");
        }

        [TestMethod]
        public void Extract_RoundTripsAllFiles()
        {
            using var scratch = new ScratchDir();

            var source = Path.Combine(scratch.Path, "bin");
            Directory.CreateDirectory(source);
            File.WriteAllBytes(Path.Combine(source, "A.dll"), new byte[] { 0x1, 0x2, 0x3, 0x4 });
            File.WriteAllText(Path.Combine(source, "manifest.txt"), "hello");

            var bundle = Path.Combine(scratch.Path, "out.h5");
            DependencyBundle.Create(source, null, bundle);

            var target = Path.Combine(scratch.Path, "extracted");
            var extracted = DependencyBundle.Extract(bundle, target);

            Assert.AreEqual(2, extracted.Count);
            CollectionAssert.AreEquivalent(new byte[] { 0x1, 0x2, 0x3, 0x4 },
                File.ReadAllBytes(Path.Combine(target, "A.dll")));
            Assert.AreEqual("hello", File.ReadAllText(Path.Combine(target, "manifest.txt")));

            var dlls = DependencyBundle.EnumerateAssemblies(extracted).ToList();
            Assert.AreEqual(1, dlls.Count);
            Assert.IsTrue(dlls[0].EndsWith("A.dll", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void Extract_RejectsPathTraversal()
        {
            using var scratch = new ScratchDir();
            var bundle = Path.Combine(scratch.Path, "evil.h5");

            using (var fs = File.Create(bundle))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../escape.txt");
                using var s = entry.Open();
                using var w = new StreamWriter(s);
                w.Write("should not be written");
            }

            var target = Path.Combine(scratch.Path, "out");

            var thrown = false;
            try
            {
                DependencyBundle.Extract(bundle, target);
            }
            catch (InvalidOperationException)
            {
                thrown = true;
            }
            Assert.IsTrue(thrown, "Path traversal extraction should be rejected with InvalidOperationException");
        }

        [TestMethod]
        public void ExtractBytes_RoundTrips()
        {
            using var scratch = new ScratchDir();

            var source = Path.Combine(scratch.Path, "bin");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "Lib.dll"), "binary-ish");

            var bundle = Path.Combine(scratch.Path, "out.h5");
            DependencyBundle.Create(source, null, bundle);

            var bytes = File.ReadAllBytes(bundle);

            var target = Path.Combine(scratch.Path, "from-bytes");
            var extracted = DependencyBundle.Extract(bytes, target);

            Assert.AreEqual(1, extracted.Count);
            Assert.AreEqual("binary-ish", File.ReadAllText(Path.Combine(target, "Lib.dll")));
        }

        private sealed class ScratchDir : IDisposable
        {
            public string Path { get; }

            public ScratchDir()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "H5_Bundle_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public void Dispose()
            {
                try { if (Directory.Exists(Path)) Directory.Delete(Path, true); } catch { }
            }
        }
    }
}
