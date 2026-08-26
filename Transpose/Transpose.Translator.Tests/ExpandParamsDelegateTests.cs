using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// [ExpandParams] targets delegates as well as methods: it declares the native variadic calling
    /// convention for a JS callback such as <c>String.replaceFn</c>, where the engine passes the
    /// tail positionally — (match, group1…groupN, offset, source). A lambda bound to such a
    /// delegate therefore emits its trailing <c>params</c> parameter as a JS rest parameter (the
    /// positional tail packs back into the array the C# body expects), and a C#-side invocation of
    /// the delegate spreads its packed array so both directions agree. Without this, the params
    /// array never materialized and the callback read <c>undefined</c>.
    /// </summary>
    [TestClass]
    public class ExpandParamsDelegateTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task LambdaOnExpandParamsDelegateReceivesPositionalTail()
        {
            var code = """
using System;
using Transpose;

[ExpandParams]
public delegate string ReplacerFn(string substring, params object[] args);

public class Program
{
    public static void Main()
    {
        Script.Write("globalThis.callPositionally = (fn) => fn('match', 7, 'source')");

        ReplacerFn replacer = (substring, args) => substring + "|" + args[0] + "|" + args[1] + "|" + args.Length;

        // The JS engine's positional invocation packs the tail into the params array...
        Console.WriteLine(Script.Write<string>("globalThis.callPositionally({0})", replacer));

        // ...and a C#-side invocation spreads its packed array, so the body sees the same shape.
        Console.WriteLine(replacer("x", new object[] { 1, 2 }));
        Console.WriteLine(replacer("y", 3, 4));
    }
}
""";
            var output = await RunTest(code, skipRoslyn: true);
            StringAssert.Contains(output, "match|7|source|2");
            StringAssert.Contains(output, "x|1|2|2");
            StringAssert.Contains(output, "y|3|4|2");
        }
    }
}
