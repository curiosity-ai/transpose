using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// A <c>Script.Write</c> template is opaque text, so where an operator binds to it the emitter
    /// has to bracket it. Unbracketed, <c>!Script.Write&lt;bool&gt;("typeof {0} === 'object'", v)</c>
    /// emitted <c>!typeof v === 'object'</c>: the <c>!</c> negates the typeof string, so the test is
    /// always false. Statement positions keep the text verbatim, since a template there can be a
    /// statement that brackets would break.
    /// </summary>
    [TestClass]
    public class ScriptWriteOperandTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task ScriptWriteIsBracketedAsAnOperand()
        {
            var code = """
using System;
using Transpose;

public class Program
{
    static bool NotObject(object v) => !Script.Write<bool>("typeof {0} === 'object'", v);

    public static void Main()
    {
        object o = new object();
        object n = 0;
        Console.WriteLine(NotObject(o));
        Console.WriteLine(NotObject(n));
        Console.WriteLine(NotObject(true));

        int a = 2, b = 3;
        Console.WriteLine(Script.Write<int>("{0} + {1}", a, b) * 10);
        Console.WriteLine(-Script.Write<int>("{0} - {1}", a, b));
        Console.WriteLine(Script.Write<bool>("{0} > {1}", a, b) ? "gt" : "le");
        Console.WriteLine(Script.Write<string>("'x' + {0}", a).Length);
        Console.WriteLine(((string)Script.Write<object>("'ab' + 'c'")).Length);

        Script.Write("var t = {0} + 1", a);
        Console.WriteLine(Script.Write<int>("t"));
    }
}
""";
            var output = await RunTest(code, skipRoslyn: true);
            Assert.AreEqual("False\nTrue\nTrue\n50\n1\nle\n2\n3\n3", output);
        }
    }
}
