using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// <c>tps.chunks.json</c> — feeding a measurement of the running application back into the chunker.
    ///
    /// Two halves are tested here and they fail in very different ways. The parser must never throw:
    /// the file is generated from a capture and checked in beside code that keeps moving, so a stale
    /// or hand-broken one has to cost the oracle a hint rather than cost everyone a build. The
    /// chunker must actually use it: membership becomes an extra dimension of the load signature, so
    /// two components the reference graph cannot tell apart end up in one chunk when the application
    /// really fetched them together, and in different chunks when it did not.
    /// </summary>
    [TestClass]
    public class ChunkOracleTests
    {
        // ------------------------------------------------------------------ parsing never throws

        [TestMethod]
        public void AMalformedFileIsIgnoredRatherThanFailingTheBuild()
        {
            foreach (var text in new[]
            {
                "",
                "{",                                            // truncated
                "not json at all",
                "null",
                "42",
                "{ \"groups\": \"not an array\" }",
                "{ \"groups\": [ 1, 2, 3 ] }",                  // entries of the wrong kind
                "{ \"groups\": [ { \"types\": \"nope\" } ] }",
                "{ \"groups\": [ { \"name\": 7, \"types\": [] } ] }",
            })
            {
                Assert.IsTrue(ChunkOracle.Parse(text).IsEmpty, $"must parse to nothing, not throw: {text}");
            }
        }

        [TestMethod]
        public void AGroupIsReadWithItsNameAndEagerFlag()
        {
            var oracle = ChunkOracle.Parse(@"{
                ""version"": 1,
                ""groups"": [
                    { ""name"": ""boot"", ""eager"": true, ""types"": [ ""tss.UI"", ""tss.Stack"", ""tss.UI"" ] },
                    { ""name"": ""#/search"", ""types"": [ ""App.SearchView"" ] }
                ]
            }");

            Assert.AreEqual(2, oracle.Groups.Count);
            Assert.AreEqual("boot", oracle.Groups[0].Name);
            Assert.IsTrue(oracle.Groups[0].Eager);
            CollectionAssert.AreEqual(new[] { "tss.UI", "tss.Stack" }, oracle.Groups[0].Types.ToArray(),
                "a repeated name is one hint, not two");
            Assert.IsFalse(oracle.Groups[1].Eager, "absent 'eager' means lazy");
        }

        [TestMethod]
        public void ABareArrayOfTypeNamesIsAcceptedAsAGroup()
        {
            // A generator that writes the simplest thing that could work should not be a build break.
            var oracle = ChunkOracle.Parse(@"[ [ ""A"", ""B"" ], { ""types"": [ ""C"" ] } ]");
            Assert.AreEqual(2, oracle.Groups.Count);
            CollectionAssert.AreEqual(new[] { "A", "B" }, oracle.Groups[0].Types.ToArray());
        }

        [TestMethod]
        public void CommentsAndTrailingCommasAreTolerated()
        {
            var oracle = ChunkOracle.Parse(@"{
                // captured 2026-08-18
                ""groups"": [ { ""name"": ""boot"", ""types"": [ ""A"", ] }, ]
            }");
            Assert.AreEqual(1, oracle.Groups.Count);
        }

        // ------------------------------------------------------------------ the chunker uses it

        /// <summary>Six independent widgets and a root that touches all of them. Main does not
        /// reach any of it, so all seven components are deferred and every widget has the same load
        /// signature — the set of roots that reach it is just {Root}. The coalescer therefore has no
        /// reason of its own to separate them, which is exactly the situation an oracle improves.
        ///
        /// The bodies are padded because the bucketer only respects a group boundary once the bucket
        /// it would end is worth a request of its own; three widgets have to add up to more than the
        /// minimum or the split would be traded away for size, which is the correct behaviour and not
        /// what this is measuring.</summary>
        private static string Source()
        {
            var sb = new StringBuilder();
            for (var i = 0; i < 6; i++)
            {
                sb.Append("public class W").Append(i).Append(" { public int Run(int n) { var t = 0;\n");
                for (var line = 0; line < 12; line++)
                    sb.Append("    t += n * ").Append(line).Append(" + (n / ").Append(line + 1).Append(");\n");
                sb.Append("    return t; } }\n");
            }
            sb.Append(@"
public class Root {
    public int A(int n) { return new W0().Run(n) + new W1().Run(n) + new W2().Run(n); }
    public int B(int n) { return new W3().Run(n) + new W4().Run(n) + new W5().Run(n); }
}
public class Program { public static void Main() { System.Console.WriteLine(""up""); } }
");
            return sb.ToString();
        }

        private const int Min = 2 * 1024;
        private const int Max = 16 * 1024;

        [TestMethod]
        public void WithoutAnOracleTheWidgetsAllShareOneChunk()
        {
            // The baseline the next test is measured against: nothing in the graph distinguishes
            // these six, so the size band packs them together.
            var m = ModuleEmitTests.Emit(Source(), minChunkBytes: Min, maxChunkBytes: Max);
            var distinct = Enumerable.Range(0, 6).Select(i => ModuleEmitTests.ChunkOf(m, "W" + i)).Distinct().Count();
            Assert.AreEqual(1, distinct, "with no measurement to go on the coalescer packs them by size alone");
        }

        [TestMethod]
        public void AnOracleSplitsComponentsTheGraphCannotTellApart()
        {
            // The measurement says the application fetches W0..W2 on one screen and W3..W5 on
            // another. Nothing in the source says so, and that is the point.
            var oracle = ChunkOracle.Parse(@"{ ""groups"": [
                { ""name"": ""screen-a"", ""types"": [ ""W0"", ""W1"", ""W2"" ] },
                { ""name"": ""screen-b"", ""types"": [ ""W3"", ""W4"", ""W5"" ] }
            ] }");

            var m = ModuleEmitTests.Emit(Source(), minChunkBytes: Min, maxChunkBytes: Max, chunkOracle: oracle);

            var a = new[] { "W0", "W1", "W2" }.Select(t => ModuleEmitTests.ChunkOf(m, t)).Distinct().ToList();
            var b = new[] { "W3", "W4", "W5" }.Select(t => ModuleEmitTests.ChunkOf(m, t)).Distinct().ToList();

            Assert.AreEqual(1, a.Count, "the three the capture saw together belong in one chunk");
            Assert.AreEqual(1, b.Count, "…and so do the other three");
            Assert.AreNotEqual(a[0], b[0], "…but the two screens must not be fetched as one");
        }

        [TestMethod]
        public void AStaleTypeNameIsSkippedAndTheRestStillApplies()
        {
            // A renamed or deleted type is the normal fate of a checked-in capture.
            var oracle = ChunkOracle.Parse(@"{ ""groups"": [
                { ""name"": ""screen-a"", ""types"": [ ""W0"", ""W1"", ""W2"", ""TypeThatNoLongerExists"" ] },
                { ""name"": ""gone"", ""types"": [ ""AlsoGone"" ] },
                { ""name"": ""screen-b"", ""types"": [ ""W3"", ""W4"", ""W5"" ] }
            ] }");

            var m = ModuleEmitTests.Emit(Source(), minChunkBytes: Min, maxChunkBytes: Max, chunkOracle: oracle);

            Assert.AreNotEqual(ModuleEmitTests.ChunkOf(m, "W0"), ModuleEmitTests.ChunkOf(m, "W3"),
                "the groups that still match must be honoured");
        }

        [TestMethod]
        public void AnEagerGroupPutsALibrarysStartUpSetInItsInitialPayload()
        {
            // A package has no entry point to be lazy relative to, so it defers everything and the
            // consumer pays for the start-up set in round trips. A capture knows what that set is.
            var plain = ModuleEmitTests.Emit(Source(), packageModules: true);
            Assert.AreEqual(0, plain.EagerChunkCount, "a library defers everything on its own");

            var oracle = ChunkOracle.Parse(@"{ ""groups"": [
                { ""name"": ""boot"", ""eager"": true, ""types"": [ ""W0"" ] }
            ] }");
            var measured = ModuleEmitTests.Emit(Source(), packageModules: true, chunkOracle: oracle);

            Assert.IsTrue(measured.EagerChunkCount > 0,
                "an eager group is the one thing a library cannot work out for itself");
        }
    }
}
