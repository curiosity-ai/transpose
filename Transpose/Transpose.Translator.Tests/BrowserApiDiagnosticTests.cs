using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// A .NET API from <c>System.IO</c>, <c>System.Threading</c> or <c>System.Net.Sockets</c> that the
    /// browser BCL does not declare at all is reported with the same <c>TransposeR0001</c> message the
    /// unsupported-feature scan gives a declared one — not as a Roslyn "type not found" error, which reads
    /// like a missing package reference (<c>BrowserApiDiagnostics</c>).
    /// </summary>
    [TestClass]
    public class BrowserApiDiagnosticTests
    {
        private static string[] Errors(string code)
        {
            var result = new RoslynTranslator().Translate(code);
            Assert.IsFalse(result.Success, "translation should fail");
            return result.Errors.Select(d => d.Id + ": " + d.GetMessage()).ToArray();
        }

        private static void AssertSingle(string code, string expected)
        {
            var errors = Errors(code);
            Assert.AreEqual(1, errors.Length, string.Join("\n", errors));
            Assert.AreEqual(expected, errors[0]);
        }

        [TestMethod]
        public void QualifiedMissingTypeInExpressionPosition()
            => AssertSingle(@"
using System;
public class Program { public static void Main() { Console.WriteLine(System.IO.Path.GetFileName(""a/b"")); } }",
                "TransposeR0001: File I/O (System.IO.Path) is not supported in the browser environment.");

        [TestMethod]
        public void QualifiedMissingTypeInTypePosition()
            => AssertSingle(@"
public class Program { public static void Main() { var s = new System.Threading.SemaphoreSlim(1); } }",
                "TransposeR0001: Threading primitives (System.Threading.SemaphoreSlim) are not supported in the browser environment.");

        [TestMethod]
        public void ImportedMissingType()
            => AssertSingle(@"
using System.Threading;
public class Program { public static void Main() { var s = new SemaphoreSlim(1); } }",
                "TransposeR0001: Threading primitives (System.Threading.SemaphoreSlim) are not supported in the browser environment.");

        [TestMethod]
        public void ImportedMissingTypeAsAStaticReceiver()
            => AssertSingle(@"
using System.Threading;
public class Program { public static void Main() { ThreadPool.QueueUserWorkItem(null); } }",
                "TransposeR0001: Threading primitives (System.Threading.ThreadPool) are not supported in the browser environment.");

        [TestMethod]
        public void MissingNamespaceAndItsTypes()
        {
            var errors = Errors(@"
using System.Net.Sockets;
public class Program { public static void Main() { var c = new TcpClient(); } }");
            CollectionAssert.AreEqual(new[]
            {
                "TransposeR0001: Sockets (System.Net.Sockets) are not supported in the browser environment.",
                "TransposeR0001: Sockets (System.Net.Sockets.TcpClient) are not supported in the browser environment.",
            }, errors, string.Join("\n", errors));
        }

        /// <summary>A name that .NET does not declare in a denied namespace is an ordinary error — a typo
        /// stays a typo.</summary>
        [TestMethod]
        public void UnknownNameIsLeftAlone()
        {
            var errors = Errors(@"
using System.Threading;
public class Program { public static void Main() { var s = new SemaphorSlim(1); } }");
            Assert.AreEqual(1, errors.Length, string.Join("\n", errors));
            StringAssert.StartsWith(errors[0], "CS0246:");
        }

        /// <summary>A known name without the import is not rewritten: C# resolved it against something
        /// else (or nothing), so the Roslyn error is the accurate one.</summary>
        [TestMethod]
        public void KnownNameWithoutTheImportIsLeftAlone()
        {
            var errors = Errors(@"
public class Program { public static void Main() { var s = new SemaphoreSlim(1); } }");
            Assert.AreEqual(1, errors.Length, string.Join("\n", errors));
            StringAssert.StartsWith(errors[0], "CS0246:");
        }

        /// <summary>A missing member of an allowed namespace is a real missing API, not a browser
        /// limitation, and keeps Roslyn's error.</summary>
        [TestMethod]
        public void AllowedNamespaceIsLeftAlone()
        {
            var errors = Errors(@"
public class Program { public static void Main() { var x = new System.Threading.Tasks.Parallel(); } }");
            Assert.AreEqual(1, errors.Length, string.Join("\n", errors));
            StringAssert.StartsWith(errors[0], "CS0234:");
        }
    }
}
