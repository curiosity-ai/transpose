using System.Linq;
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

        /// <summary>
        /// A [Template] whose text is itself a binary or ternary expression has to be parenthesized
        /// where it lands. The C# node it stands in for is an invocation — a primary expression — so
        /// the emitter around it will happily make it an operand, and the template's own operators
        /// then bind to the wrong thing. <c>Script.IsUndefined(x)</c> is <c>{0} === undefined</c>, so
        /// <c>!Script.IsUndefined(x)</c> emitted <c>!x === undefined</c>, i.e. <c>(!x) === undefined</c>
        /// — <c>false</c> for every possible x, with nothing to say so. Same family as the <c>??</c>
        /// operands above: an operand whose emission is opaque text gets wrapped.
        /// </summary>
        [TestMethod]
        public void NonPrimaryTemplateIsParenthesizedAsAnOperand()
        {
            var code = """
using System;
using Transpose;

public class Program
{
    public static void Main()
    {
        object set = "x";
        object nil = null;

        Console.WriteLine(!Script.IsUndefined(set));
        Console.WriteLine(Script.IsNull(nil) && Script.IsNull(set));
        Console.WriteLine(Script.IsNull(nil) == false);
        Console.WriteLine(Script.IsNull(nil) ? "was null" : "had a value");
    }
}
""";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed\n"
                + string.Join("\n", result.Diagnostics.Select(d => d.GetMessage())));

            var js = result.Javascript!;

            // `!x === undefined` is the bug; `!(x === undefined)` is the fix.
            StringAssert.Contains(js, "!(set === undefined)", "a negated template must be wrapped\n" + js);
            Assert.IsFalse(js.Contains("!set === undefined"), "the unwrapped form is always false\n" + js);

            StringAssert.Contains(js, "(nil === null) && (set === null)", "both operands wrapped\n" + js);
            StringAssert.Contains(js, "(nil === null) === false", "a compared template is wrapped\n" + js);
        }

        /// <summary>
        /// The same thing, run: the negation has to actually answer <c>False</c> for a value that is
        /// defined. Transpose-only, since <c>Script</c> has no native counterpart to diff against.
        /// </summary>
        [TestMethod]
        public async Task NonPrimaryTemplateOperandEvaluatesCorrectly()
        {
            var output = await RunTest("""
using System;
using Transpose;

public class Program
{
    public static void Main()
    {
        object set = "x";
        object nil = null;

        Console.WriteLine(!Script.IsUndefined(set));            // True  - it IS defined
        Console.WriteLine(!Script.IsNull(nil));                 // False - it IS null
        Console.WriteLine(!Script.IsNull(set));                 // True  - it is not null
        Console.WriteLine(Script.IsNull(nil) && !Script.IsNull(set));
        Console.WriteLine(Script.IsNull(nil) ? "was null" : "had a value");
    }
}
""", skipRoslyn: true);

            // Unparenthesized, the first line emitted `(!"x") === undefined` -> False, the exact wrong
            // answer this pins. The rest hold either way and guard the operands around it.
            Assert.AreEqual("True\nFalse\nTrue\nTrue\nwas null", output.Trim().Replace("\r\n", "\n"),
                "a negated non-primary template must evaluate as written\n" + output);
        }
    }
}
