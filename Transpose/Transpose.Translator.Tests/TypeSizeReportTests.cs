using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// <c>--type-sizes</c> / <c>TRANSPOSE_TYPE_SIZES</c> — the per-type JavaScript size report.
    ///
    /// Three properties carry the whole feature. It costs nothing when it is off, because the
    /// recording call sits inside the parallel emit of every type. Its order is total and
    /// deterministic, because a report whose ties shuffle between runs cannot be diffed against an
    /// earlier one. And a type emitted twice — which is what a package build does, once as the
    /// single bundle and once as the module chunks — is one row rather than a doubled one.
    /// </summary>
    [TestClass]
    public class TypeSizeReportTests
    {
        [TestInitialize]
        public void Reset()
        {
            TypeSizeReport.Reset();
            TypeSizeReport.Enabled = false;
        }

        [TestCleanup]
        public void Cleanup() => Reset();

        [TestMethod]
        public void RecordsNothingWhileDisabled()
        {
            TypeSizeReport.Record("asm", "A", new string('x', 100));
            Assert.AreEqual(0, TypeSizeReport.Snapshot().Count);
        }

        [TestMethod]
        public void ReportsBytesLargestFirst()
        {
            TypeSizeReport.Enabled = true;
            TypeSizeReport.Record("asm", "Small", new string('x', 10));
            TypeSizeReport.Record("asm", "Large", new string('x', 1000));
            TypeSizeReport.Record("asm", "Medium", new string('x', 100));

            CollectionAssert.AreEqual(
                new[] { "Large", "Medium", "Small" },
                TypeSizeReport.Snapshot().Select(x => x.type).ToArray());
            Assert.AreEqual(1000, TypeSizeReport.Snapshot()[0].bytes);
        }

        [TestMethod]
        public void TiesAreBrokenByNameSoTwoBuildsAgree()
        {
            TypeSizeReport.Enabled = true;
            TypeSizeReport.Record("asm", "Zeta", new string('x', 50));
            TypeSizeReport.Record("asm", "Alpha", new string('x', 50));

            CollectionAssert.AreEqual(
                new[] { "Alpha", "Zeta" },
                TypeSizeReport.Snapshot().Select(x => x.type).ToArray());
        }

        [TestMethod]
        public void TwoEmitPassesOverOneTypeStayOneRow()
        {
            TypeSizeReport.Enabled = true;
            // A package emits every type twice — the single bundle and the module chunks — and the
            // two texts differ only in indentation. Summing them would report a type at twice its
            // real weight, and take the whole table with it.
            TypeSizeReport.Record("asm", "A", new string('x', 100));
            TypeSizeReport.Record("asm", "A", new string('x', 90));

            var snapshot = TypeSizeReport.Snapshot();
            Assert.AreEqual(1, snapshot.Count);
            Assert.AreEqual(100, snapshot[0].bytes);
        }

        [TestMethod]
        public void SameTypeNameInTwoAssembliesAreSeparateRows()
        {
            TypeSizeReport.Enabled = true;
            TypeSizeReport.Record("one", "Shared.Thing", new string('x', 100));
            TypeSizeReport.Record("two", "Shared.Thing", new string('x', 200));

            var snapshot = TypeSizeReport.Snapshot();
            Assert.AreEqual(2, snapshot.Count);
            Assert.AreEqual("two", snapshot[0].assembly);
        }

        [TestMethod]
        public void SizeIsCountedInUtf8Bytes()
        {
            TypeSizeReport.Enabled = true;
            // The report is about payload, and the payload is UTF-8 — a string literal outside the
            // ASCII range costs more bytes than it has chars.
            TypeSizeReport.Record("asm", "A", "€");

            Assert.AreEqual(3, TypeSizeReport.Snapshot()[0].bytes);
        }
    }
}
