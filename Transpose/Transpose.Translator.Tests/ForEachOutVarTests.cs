using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Regression tests for inline-declaration scoping in foreach bodies. An `out var` (or
    /// is-pattern) variable declared inside one foreach body must be re-declared in a sibling
    /// foreach body: the two bodies are separate JS block scopes, so tracking the name as
    /// "already declared" across them left the second use undeclared (ReferenceError) — the
    /// Tesserae Layers.CurrentZIndex crash ("zIndex is not defined").
    /// </summary>
    [TestClass]
    public class ForEachOutVarTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task TestOutVarInSiblingForeachAsync()
        {
            await RunTest(
                @"
using System;
using System.Collections.Generic;

public class Program
{
    public static void Main()
    {
        var a = new List<string> { ""1200"", ""2000"" };
        var b = new List<string> { ""3000"", ""x"" };
        int max = 1000;

        // out-var named the same as a member being parsed, in two sibling foreach scopes.
        foreach (var s in a)
        {
            if (int.TryParse(s, out var zIndex) && zIndex > max) max = zIndex;
        }
        foreach (var s in b)
        {
            if (int.TryParse(s, out var zIndex) && zIndex > max) max = zIndex;
        }

        Console.WriteLine(max);
    }
}
                ");
        }
    }
}
