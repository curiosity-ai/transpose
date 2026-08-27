using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// An <c>[External]</c> type declared outside any namespace.
    ///
    /// <para>
    /// A binding names the JS global it stands for, so a type in the global namespace is emitted as
    /// the bare name — <c>Thing</c>, the way <c>Transpose.Core.dom</c>'s <c>HTMLElement</c> is.
    /// <c>INamespaceSymbol.ToDisplayString()</c> renders the global namespace as the literal
    /// <c>"&lt;global namespace&gt;"</c> though, which is a description and not a name, so the
    /// reference came out as <c>&lt;global namespace&gt;.Thing</c>. That is a JavaScript syntax
    /// error, and reflection metadata is emitted as one expression per assembly, so a single such
    /// type stopped the WHOLE bundle from parsing rather than failing where it was used.
    /// </para>
    /// </summary>
    [TestClass]
    public class GlobalNamespaceExternalTypeTests : TranslatorTestBase
    {
        [TestMethod]
        public void AGlobalNamespaceExternalTypeIsNamedBare()
        {
            var code = """
using System;
using Transpose;

[External]
public class Thing
{
    public extern int n { get; }
}

public class Program
{
    public static void Main() { }
    static Thing Make() => Script.Write<Thing>("({ n: 1 })");
    static bool Test(object o) => o is Thing;
    static string Named() => typeof(Thing).Name;
}
""";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, string.Join("\n", result.Errors));
            var js = result.Javascript!;

            Assert.IsFalse(js.Contains("<global namespace>"),
                "the global namespace is not a name — it must never reach the output\n" + js);
            StringAssert.Contains(js, "TransposeR.is(o, Thing)",
                "a global-namespace [External] type is the bare JS global");
            // The return type of Make() in the reflection metadata — the position that broke the
            // whole bundle, because the metadata is one expression per assembly.
            StringAssert.Contains(js, "\"sn\":\"Make\",\"rt\":Thing",
                "…including in the reflection metadata");
        }

        /// <summary>
        /// And it works end to end: the fixture installs the real JS global the binding names, so
        /// member access, <c>is</c>, and <c>typeof</c> all resolve against it.
        /// </summary>
        [TestMethod]
        public async Task AGlobalNamespaceExternalTypeResolvesAtRuntime()
        {
            var output = await RunTest("""
using System;
using Transpose;

[External]
public class Thing
{
    public extern int n { get; }
    public extern ulong size { get; }
}

public class Program
{
    static Thing Make() => Script.Write<Thing>(
        "(globalThis.Thing = globalThis.Thing || function Thing() { this.n = 1; this.size = 7; }, new globalThis.Thing())");

    public static void Main()
    {
        var t = Make();
        Console.WriteLine(t.n);
        Console.WriteLine(t.size + 1);
        Console.WriteLine(t is Thing);
        Console.WriteLine(typeof(Thing).Name);
        object o = t;
        Console.WriteLine(o is Thing);
    }
}
""", skipRoslyn: true);

            Assert.AreEqual("1\n8\nTrue\nThing\nTrue", output.Trim().Replace("\r\n", "\n"));
        }
    }
}
