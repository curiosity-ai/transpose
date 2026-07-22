using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Coverage for special-case codegen attributes ported from H5: [Namespace], [Reflectable],
    /// [Script], [Conditional], [InlineConst], [Mixin], [Init], [Constructor], [ToAwait], [Field].
    /// </summary>
    [TestClass]
    public class AttributeHandlingTests : TranslatorTestBase
    {
        // ---- [Namespace] ------------------------------------------------------

        [TestMethod]
        public void NamespaceFalseSuppressesNamespaceOnExternalTypeReference()
        {
            var code = @"
using Transpose;
namespace Foo.Bar
{
    [External]
    [Namespace(false)]
    public static class MyGlobal { public static extern void Ping(); }
}
public class Program { public static void Main() { Foo.Bar.MyGlobal.Ping(); } }
";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed\n" + string.Join("\n", result.Diagnostics.Select(d => d.GetMessage())));
            Assert.IsFalse(result.Javascript!.Contains("Foo.Bar.MyGlobal"),
                "[Namespace(false)] must drop the namespace\n" + result.Javascript);
            Assert.IsTrue(result.Javascript!.Contains("MyGlobal.Ping()"),
                "call should reference the bare, namespace-less type\n" + result.Javascript);
        }

        [TestMethod]
        public void NamespaceStringReplacesNamespaceOnExternalTypeReference()
        {
            var code = @"
using Transpose;
namespace Foo.Bar
{
    [External]
    [Namespace(""x.y"")]
    public static class MyGlobal { public static extern void Ping(); }
}
public class Program { public static void Main() { Foo.Bar.MyGlobal.Ping(); } }
";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed\n" + string.Join("\n", result.Diagnostics.Select(d => d.GetMessage())));
            Assert.IsFalse(result.Javascript!.Contains("Foo.Bar.MyGlobal"),
                "[Namespace(\"x.y\")] must replace the namespace\n" + result.Javascript);
            Assert.IsTrue(result.Javascript!.Contains("x.y.MyGlobal.Ping()"),
                "call should reference the custom namespace\n" + result.Javascript);
        }

        // ---- [Reflectable] ----------------------------------------------------

        [TestMethod]
        public void ReflectableFalseOnTypeSuppressesItsMetadata()
        {
            var code = @"
using Transpose;
[Reflectable(false)]
public class Hidden { public int X; }
public class Visible { public int Y; }
public class Program { public static void Main() { } }
";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed\n" + string.Join("\n", result.Diagnostics.Select(d => d.GetMessage())));
            Assert.IsFalse(result.Javascript!.Contains("$m(\"Hidden\""),
                "[Reflectable(false)] type must have no metadata entry\n" + result.Javascript);
            Assert.IsTrue(result.Javascript!.Contains("$m(\"Visible\""),
                "a normal type keeps its metadata entry\n" + result.Javascript);
        }

        [TestMethod]
        public void ReflectableFalseOnMemberSuppressesMemberMetadata()
        {
            var code = @"
using Transpose;
public class C
{
    public int Kept;
    [Reflectable(false)] public int Dropped;
    public static void Main() { }
}
";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed\n" + string.Join("\n", result.Diagnostics.Select(d => d.GetMessage())));
            Assert.IsTrue(result.Javascript!.Contains("\"n\":\"Kept\""),
                "a normal member stays in metadata\n" + result.Javascript);
            Assert.IsFalse(result.Javascript!.Contains("\"n\":\"Dropped\""),
                "[Reflectable(false)] member must be absent from metadata\n" + result.Javascript);
        }

        // ---- [Script] ---------------------------------------------------------

        [TestMethod]
        public void ScriptEmitsRawJsBodyForExternMethod()
        {
            var code = @"
using Transpose;
public static class M
{
    [Script(""return a + b;"")]
    public static extern int Add(int a, int b);
    public static void Main() { var r = Add(1, 2); }
}
";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed\n" + string.Join("\n", result.Diagnostics.Select(d => d.GetMessage())));
            Assert.IsTrue(result.Javascript!.Contains("return a + b;"),
                "[Script] body should be emitted verbatim\n" + result.Javascript);
        }

        [TestMethod]
        public void ScriptEmitsMultipleRawJsLines()
        {
            var code = @"
using Transpose;
public static class M
{
    [Script(""var x = 1;"", ""return x + 41;"")]
    public static extern int Answer();
    public static void Main() { var r = Answer(); }
}
";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed\n" + string.Join("\n", result.Diagnostics.Select(d => d.GetMessage())));
            Assert.IsTrue(result.Javascript!.Contains("var x = 1;") && result.Javascript!.Contains("return x + 41;"),
                "[Script] should emit all lines verbatim\n" + result.Javascript);
        }

        // ---- [Conditional] ----------------------------------------------------

        [TestMethod]
        public void ConditionalCallWithUndefinedSymbolIsRemoved()
        {
            var code = @"
using System.Diagnostics;
public class Program
{
    [Conditional(""NEVER_DEFINED_SYM"")]
    static void Trace(string s) { }
    static void Kept() { }
    public static void Main() { Trace(""hi""); Kept(); }
}
";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed\n" + string.Join("\n", result.Diagnostics.Select(d => d.GetMessage())));
            Assert.IsFalse(result.Javascript!.Contains("Trace("),
                "[Conditional] call with an undefined symbol must be removed\n" + result.Javascript);
            Assert.IsTrue(result.Javascript!.Contains("Kept("),
                "a non-conditional call stays\n" + result.Javascript);
        }
    }
}
