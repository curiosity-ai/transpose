using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Two emission fixes aligning the Roslyn translator with the legacy compiler:
    ///  - [GlobalTarget(name)] on an extern method maps the call to the global JS function `name`;
    ///    an EMPTY name compiles the call away to `void 0` (the `LazyLoad()` marker pattern whose only
    ///    purpose is to force the assembly to be referenced), rather than emitting a real
    ///    `Type.Method()` call that would fail at runtime.
    ///  - an omitted optional argument that a positional JS call cannot skip (it precedes a provided
    ///    one, e.g. via a named argument) is passed as `void 0` (undefined), NOT `null`, so the callee's
    ///    `if (arg === undefined) arg = <default>` supplies the real default. Passing `null` would
    ///    defeat that check whenever the default is non-null.
    /// </summary>
    [TestClass]
    public class GlobalTargetAndDefaultsTests : TranslatorTestBase
    {
        // ---- [GlobalTarget] ---------------------------------------------------

        [TestMethod]
        public void GlobalTargetEmptyNameCompilesToNoOp()
        {
            // [GlobalTarget("")] is a marker whose call must compile away to `void 0` — never a real
            // `Native.LazyLoad()` invocation (there is no such JS function).
            var code = @"
using Transpose;
public static class Native
{
    [GlobalTarget("""")]
    public static extern void LazyLoad();
}
public class Program { public static void Main() { Native.LazyLoad(); } }
";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsTrue(result.Javascript!.Contains("void 0"),
                "[GlobalTarget(\"\")] call should emit `void 0`\n" + result.Javascript);
            // The type metadata still lists the method name, but there must be no actual
            // `LazyLoad(...)` invocation emitted.
            Assert.IsFalse(result.Javascript!.Contains("LazyLoad("),
                "[GlobalTarget(\"\")] call must NOT emit a real Native.LazyLoad() invocation\n" + result.Javascript);
        }

        [TestMethod]
        public void GlobalTargetNamedFunctionMapsToGlobalCall()
        {
            // [GlobalTarget("myGlobalFn")] maps the call to a bare global function invocation, dropping
            // the declaring type entirely.
            var code = @"
using Transpose;
public static class Native
{
    [GlobalTarget(""myGlobalFn"")]
    public static extern void Ping(int x);
}
public class Program { public static void Main() { Native.Ping(5); } }
";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsTrue(result.Javascript!.Contains("myGlobalFn(5)"),
                "[GlobalTarget(\"myGlobalFn\")] call should emit `myGlobalFn(5)`\n" + result.Javascript);
            Assert.IsFalse(result.Javascript!.Contains("Native.Ping"),
                "the declaring type must be dropped for a [GlobalTarget] call\n" + result.Javascript);
        }

        // ---- omitted optional -> void 0 --------------------------------------

        [TestMethod]
        public void OmittedOptionalBeforeNamedArgEmitsVoid0()
        {
            // F(a: 10, c: 30) leaves b omitted but b precedes the provided c, so the positional JS call
            // must fill the gap. It must be `void 0` (so the callee applies its own default 2), not `null`.
            var code = @"
using System;
public class Program
{
    static string F(int a = 1, int b = 2, int c = 3) => a + "","" + b + "","" + c;
    public static void Main() { Console.WriteLine(F(a: 10, c: 30)); }
}
";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsTrue(result.Javascript!.Contains("void 0"),
                "the omitted middle optional should be filled with `void 0`\n" + result.Javascript);
        }

        [TestMethod]
        public async Task OmittedOptionalBeforeNamedArgUsesCalleeDefaultAsync()
        {
            // Behavioral: passing `void 0` for the omitted `b` must let the callee apply its default (2),
            // yielding "10,2,30". Emitting `null` here would print "10,,30".
            await RunTest(@"
using System;
public class Program
{
    static string F(int a = 1, int b = 2, int c = 3) => a + "","" + b + "","" + c;
    public static void Main()
    {
        Console.WriteLine(F(a: 10, c: 30));
        Console.WriteLine(F(1, c: 30));
        Console.WriteLine(F());
    }
}");
        }
    }
}
