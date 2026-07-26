using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// The emitted bundle must be reproducible: compiling unchanged sources twice has to produce
    /// byte-identical JavaScript, because that is the gate every compiler change is measured against
    /// (diff a baseline build vs. a new one) and because a churning bundle defeats HTTP caching.
    ///
    /// Synthesized variable names are where this is easy to lose. <c>SyntaxNode.GetHashCode()</c> is
    /// reference-based, so naming a temp from it gives a different name on every run — the enumerator
    /// name was fixed to use the statement's source position for exactly this reason, but the
    /// <c>using (expr)</c> resource variable still hashed the node and so renamed itself
    /// (<c>$using2871</c> → <c>$using3236</c>) on every compile of the same file.
    /// </summary>
    [TestClass]
    public class EmitDeterminismTests
    {
        [TestMethod]
        public void SynthesizedTempNamesAreStableAcrossCompilations()
        {
            var code = @"
using System;
using System.Collections.Generic;
using System.IO;

public class Program
{
    static IDisposable Open() => new MemoryStream();

    static void Run(List<int> items)
    {
        using (Open())
        {
            foreach (var i in items) Console.WriteLine(i);
        }

        using (Open())
        {
            using (Open())
            {
                foreach (var i in items) Console.WriteLine(i);
            }
        }

        using (Open()) Console.WriteLine(""last"");
    }

    public static void Main() { Run(new List<int>()); }
}";
            var first = new RoslynTranslator().Translate(code);
            var second = new RoslynTranslator().Translate(code);

            Assert.IsTrue(first.Success, "first translation should succeed");
            Assert.IsTrue(second.Success, "second translation should succeed");
            Assert.AreEqual(first.Javascript, second.Javascript,
                "translating unchanged sources twice must emit byte-identical JavaScript");
        }
    }
}
