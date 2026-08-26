using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// C#'s <c>??</c> binds looser than <c>&amp;&amp;</c> / <c>||</c>, but JavaScript refuses
    /// <c>??</c> mixed bare with either (an early SyntaxError) — so <c>a ?? b &amp;&amp; c</c> is
    /// valid C# whose naive emission does not even parse (the whole bundle dies at load). The
    /// emitter parenthesizes the <c>??</c> operands, which also shields operands whose emission is
    /// opaque text (Script.Write, [Template] members).
    /// </summary>
    [TestClass]
    public class CoalescePrecedenceTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task CoalesceWithLogicalOperandParses()
        {
            var code = """
using System;

public class Program
{
    public static void Main()
    {
        bool? maybe = null;
        bool y = true;
        bool z = false;

        Console.WriteLine(maybe ?? y && z);
        Console.WriteLine(maybe ?? y || z);

        maybe = true;
        Console.WriteLine(maybe ?? y && z);

        bool? a = null;
        bool? b = null;
        Console.WriteLine(a ?? b ?? y || z);

        string s = null;
        Console.WriteLine(s ?? (y ? "left" : "right"));
    }
}
""";
            await RunTest(code);
        }
    }
}
