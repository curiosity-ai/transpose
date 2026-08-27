using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// The second chunking pass (<c>Emitter.ModuleChunks.cs</c>): merging the strongly-connected
    /// components back up into chunks worth fetching.
    ///
    /// The first pass emits one chunk per SCC, which is the smallest <em>sound</em> unit and far too
    /// fine to fetch — a real library lands around a 2 KB median, so a screen that needs twenty types
    /// pays twenty requests. This pass merges components that load together until each chunk is in a
    /// target size band.
    ///
    /// The band is set per test rather than left at the shipped 50–100 KB: these snippets are a few
    /// hundred bytes each, so the real band would collapse every one of them into a single chunk and
    /// there would be nothing to observe.
    /// </summary>
    [TestClass]
    public class ModuleChunkCoalescingTests
    {
        private static Emitter.ModuleOutput Emit(string source, int min, int max) =>
            ModuleEmitTests.Emit(source, minChunkBytes: min, maxChunkBytes: max);

        private static IEnumerable<string> ImportsOf(string js) =>
            Regex.Matches(js, @"^import '\./(c[0-9a-f]+(?:-\d+)?\.mjs)';$", RegexOptions.Multiline).Select(x => x.Groups[1].Value);

        /// <summary>Where a chunk sits in the emission order. A chunk file is named after the hash of
        /// its content, so the order is its position in <c>Chunks</c> — there is no index to read off
        /// the name.</summary>
        private static Func<string, int> PositionIn(Emitter.ModuleOutput m)
        {
            var byName = m.Chunks.Select((c, i) => (name: System.IO.Path.GetFileName(c.relPath), i))
                                 .ToDictionary(x => x.name, x => x.i);
            return relPath => byName[System.IO.Path.GetFileName(relPath)];
        }

        /// <summary>Ten independent types, each with a body big enough to be worth measuring.</summary>
        private static string Widgets(int count, int bodyLines, string prefix = "W")
        {
            var sb = new StringBuilder();
            for (var i = 0; i < count; i++)
            {
                sb.Append("public class ").Append(prefix).Append(i).Append(" { public int Run(int n) { var t = 0;\n");
                for (var line = 0; line < bodyLines; line++)
                    sb.Append("    t += n * ").Append(line).Append(" + (n / ").Append(line + 1).Append(");\n");
                sb.Append("    return t; } }\n");
            }
            return sb.ToString();
        }

        [TestMethod]
        public void ComponentsThatAlwaysLoadTogetherAreMergedIntoOneChunk()
        {
            // Root reaches every widget and nothing else does, so all of them are fetched exactly
            // when Root is: their load condition is identical and merging them costs nothing.
            var uses = string.Join(" + ", Enumerable.Range(0, 6).Select(i => $"new W{i}().Run(n)"));
            var m = Emit(Widgets(6, 12) + $@"
public class Root {{ public int All(int n) {{ return {uses}; }} }}
public class Program {{ public static void Main() {{ System.Console.WriteLine(new Root().All(1)); }} }}
", min: 8 * 1024, max: 16 * 1024);

            var chunks = Enumerable.Range(0, 6).Select(i => ModuleEmitTests.ChunkOf(m, "W" + i)).Distinct().ToList();
            Assert.AreEqual(1, chunks.Count,
                "six components with the same load condition should be one chunk, not six");
        }

        [TestMethod]
        public void ZeroTurnsThePassOffAndKeepsOneChunkPerComponent()
        {
            var source = Widgets(6, 12) + @"
public class Root { public int All(int n) { return new W0().Run(n); } }
public class Program { public static void Main() { System.Console.WriteLine(new Root().All(1)); } }
";
            var off = Emit(source, min: 0, max: 0);
            var on = Emit(source, min: 8 * 1024, max: 16 * 1024);
            Assert.IsTrue(off.Chunks.Count > on.Chunks.Count,
                $"min=0 must leave the components split ({off.Chunks.Count} chunks) — coalesced gave {on.Chunks.Count}");
            Assert.AreEqual(6, Enumerable.Range(0, 6).Select(i => ModuleEmitTests.ChunkOf(off, "W" + i)).Distinct().Count());
        }

        [TestMethod]
        public void AnEagerChunkIsNeverMergedWithADeferredOne()
        {
            // Program (eager) and Deferred (nothing reaches it, so it stays a stub) are both tiny and
            // adjacent, which is exactly the pair a size-only bucketer would fuse — and fusing them
            // would drag the deferred code into the initial payload, the opposite of the point.
            var m = Emit(Widgets(4, 12, "E") + Widgets(4, 12, "L") + @"
public class Shell { public int Boot(int n) { return new E0().Run(n) + new E1().Run(n) + new E2().Run(n) + new E3().Run(n); } }
public class Deferred { public int Later(int n) { return new L0().Run(n) + new L1().Run(n) + new L2().Run(n) + new L3().Run(n); } }
public class Program { public static void Main() { System.Console.WriteLine(new Shell().Boot(1)); } }
", min: 32 * 1024, max: 64 * 1024);

            var eagerChunk = ModuleEmitTests.ChunkOf(m, "Program");
            Assert.AreNotEqual(eagerChunk, ModuleEmitTests.ChunkOf(m, "Deferred"),
                "a deferred type must not end up in an eagerly imported chunk");
            foreach (var name in new[] { "L0", "L1", "L2", "L3" })
                Assert.AreNotEqual(eagerChunk, ModuleEmitTests.ChunkOf(m, name),
                    name + " is only reachable from the deferred type and must stay out of the eager chunk");
            Assert.IsTrue(m.LazyChunkCount > 0, "something has to still be deferred");
        }

        [TestMethod]
        public void EveryImportStillPointsAtAnEarlierChunk()
        {
            // The merged graph has to stay a DAG, and the emitter's invariant is stronger than that:
            // a chunk only ever imports chunks emitted before it. That is what lets a chunk be named
            // after the hash of its own text (its dependencies' names are already final), and —
            // because Transpose.define resolves `inherits` eagerly — what makes the evaluation order
            // sound. A merge is the one operation that could break it.
            var body = new StringBuilder(Widgets(24, 10));
            for (var i = 0; i < 8; i++)
                body.Append($"public class Mid{i} {{ public int Go(int n) {{ return new W{i}().Run(n) + new W{i + 8}().Run(n) + new W{i + 16}().Run(n); }} }}\n");
            body.Append("public class Program { public static void Main() { System.Console.WriteLine(new Mid0().Go(1) + new Mid1().Go(2)); } }\n");

            var m = Emit(body.ToString(), min: 6 * 1024, max: 12 * 1024);

            var position = PositionIn(m);
            foreach (var (relPath, js) in m.Chunks)
            {
                var self = position(relPath);
                foreach (var imported in ImportsOf(js))
                    Assert.IsTrue(position(imported) < self,
                        $"{relPath} imports {imported}, which is not an earlier chunk");
            }
        }

        [TestMethod]
        public void EveryEmittedTypeIsStillDefinedExactlyOnce()
        {
            var m = Emit(Widgets(20, 10) + @"
public class Program { public static void Main() { System.Console.WriteLine(new W0().Run(1)); } }
", min: 6 * 1024, max: 12 * 1024);

            var defined = m.Chunks
                .SelectMany(c => Regex.Matches(c.js, @"Transpose\.definei?\(""([^""]+)""").Select(x => x.Groups[1].Value))
                .ToList();
            CollectionAssert.AllItemsAreUnique(defined, "merging must not duplicate or drop a define");
            foreach (var i in Enumerable.Range(0, 20))
                CollectionAssert.Contains(defined, "W" + i);
        }

        [TestMethod]
        public void TheChunkMapAndTheManifestFollowTheMergedChunks()
        {
            var m = Emit(Widgets(12, 10) + @"
public class Program { public static void Main() { System.Console.WriteLine(new W0().Run(1)); } }
", min: 6 * 1024, max: 12 * 1024);

            foreach (var i in Enumerable.Range(0, 12))
            {
                var expected = ModuleEmitTests.ChunkOf(m, "W" + i);
                Assert.AreEqual(expected, m.TypeToChunk["W" + i],
                    "the published chunk map must name the chunk a type actually landed in");
                // Deferred types are registered with the same file the map names, so a stub resolves
                // to the module that really defines it.
                if (m.EntryJs.Contains($"\"W{i}\": {{ m: "))
                    StringAssert.Contains(m.EntryJs, $"\"W{i}\": {{ m: \"./{expected}\"");
            }
        }

        [TestMethod]
        public void AChunkStaysUnderTheCeilingUnlessOneComponentIsAlreadyOverIt()
        {
            const int max = 12 * 1024;
            var m = Emit(Widgets(30, 8) + @"
public class Program { public static void Main() { System.Console.WriteLine(new W0().Run(1)); } }
", min: 6 * 1024, max: max);

            foreach (var (relPath, js) in m.Chunks)
            {
                // The import prologue is not part of what the bucketer measures, so allow for it.
                var body = js.Length - js.Split('\n').Where(l => l.StartsWith("import ")).Sum(l => l.Length + 1);
                Assert.IsTrue(body <= max + 2048, $"{relPath} is {body} bytes, past the {max}-byte ceiling");
            }
            Assert.IsTrue(m.Chunks.Count > 1, "30 widgets at 8 lines each should not fit in one 12 KB chunk");
        }

        [TestMethod]
        public void TwoBuildsOfTheSameSourcesProduceTheSameChunks()
        {
            var source = Widgets(16, 10) + @"
public class Left { public int Go(int n) { return new W0().Run(n) + new W1().Run(n) + new W2().Run(n); } }
public class Right { public int Go(int n) { return new W3().Run(n) + new W4().Run(n) + new W2().Run(n); } }
public class Program { public static void Main() { System.Console.WriteLine(new Left().Go(1)); } }
";
            var a = Emit(source, min: 6 * 1024, max: 12 * 1024);
            var b = Emit(source, min: 6 * 1024, max: 12 * 1024);

            CollectionAssert.AreEqual(a.Chunks.Select(c => c.relPath).ToList(), b.Chunks.Select(c => c.relPath).ToList());
            for (var i = 0; i < a.Chunks.Count; i++)
                Assert.AreEqual(a.Chunks[i].js, b.Chunks[i].js, "chunk " + a.Chunks[i].relPath + " differs between builds");
            Assert.AreEqual(a.EntryJs, b.EntryJs);
        }

        [TestMethod]
        public void CodeExclusiveToOneDeferredRootDoesNotFollowAnother()
        {
            // Two independent deferred features, each with enough exclusive code to fill a chunk of
            // its own. Their load conditions are disjoint, so once a bucket is worth its request the
            // pass must stop rather than fuse the two features together.
            var m = Emit(Widgets(8, 14, "A") + Widgets(8, 14, "B") + @"
public class FeatureA { public int Go(int n) { return new A0().Run(n) + new A1().Run(n) + new A2().Run(n) + new A3().Run(n)
                                                    + new A4().Run(n) + new A5().Run(n) + new A6().Run(n) + new A7().Run(n); } }
public class FeatureB { public int Go(int n) { return new B0().Run(n) + new B1().Run(n) + new B2().Run(n) + new B3().Run(n)
                                                    + new B4().Run(n) + new B5().Run(n) + new B6().Run(n) + new B7().Run(n); } }
public class Program { public static void Main() { System.Console.WriteLine(42); } }
", min: 2 * 1024, max: 1024 * 1024);

            // Each feature's exclusive code is over the minimum on its own, so the bucket earns its
            // request before the load condition changes and the pass stops there rather than fusing
            // the two features. (Crossing that boundary is only allowed while the bucket is still
            // under the minimum — that is the one place the pass trades over-fetch for size.)
            var aChunks = Enumerable.Range(0, 8).Select(i => ModuleEmitTests.ChunkOf(m, "A" + i))
                .Append(ModuleEmitTests.ChunkOf(m, "FeatureA")).ToHashSet();
            var bChunks = Enumerable.Range(0, 8).Select(i => ModuleEmitTests.ChunkOf(m, "B" + i))
                .Append(ModuleEmitTests.ChunkOf(m, "FeatureB")).ToHashSet();
            CollectionAssert.AreEquivalent(Array.Empty<string>(), aChunks.Intersect(bChunks).ToList(),
                "two features that are never loaded together must not share a chunk");
            Assert.AreEqual(1, aChunks.Count, "the eight components exclusive to FeatureA should be one chunk");
        }
    }
}
