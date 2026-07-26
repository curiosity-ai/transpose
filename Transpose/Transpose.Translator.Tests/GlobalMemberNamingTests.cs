using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// A static member whose name collides with a Function own-property (<c>name</c>, <c>length</c>,
    /// <c>caller</c>, …) is emitted as <c>$name</c>, because a normal static lives on the type's
    /// constructor function and <c>Class.name</c> is read-only. A <c>[GlobalMethods]</c> binding is
    /// different: its members ARE the ambient JS globals and are emitted bare, so escaping invents an
    /// identifier that was never declared — <c>window.name</c> came out as <c>$name</c>, a strict-mode
    /// ReferenceError. Found in the Curiosity front-end: `Tooltip(name, …)` in EmojiSelector emitted
    /// `$name` and threw, where h5 emitted the working `name`.
    /// </summary>
    [TestClass]
    public class GlobalMemberNamingTests
    {
        private const string GLOBALS = @"
using Transpose;

[External]
[GlobalMethods]
public static class Globals
{
    public static extern string name   { get; set; }
    public static extern int    length { get; }
    public static extern string other  { get; set; }
}
";

        [TestMethod]
        public void GlobalMethodsMembersAreNotDollarEscaped()
        {
            var code = GLOBALS + @"
public class Program
{
    public static string Read() => Globals.name + Globals.length + Globals.other;
    public static void Main() { }
}";
            var result = new RoslynTranslator().Translate(code);

            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;

            Assert.IsFalse(js.Contains("$name"),
                "a [GlobalMethods] member named `name` is a bare global and must not be $-escaped\n" + js);
            Assert.IsFalse(js.Contains("$length"),
                "a [GlobalMethods] member named `length` is a bare global and must not be $-escaped\n" + js);
        }

        /// <summary>The escape must stay for a normal static, where the member really does live on the
        /// type's constructor function and would otherwise collide with Function.name / Function.length.</summary>
        [TestMethod]
        public void OrdinaryStaticMembersKeepTheDollarEscape()
        {
            var code = @"
public class Config
{
    public static string name   = ""x"";
    public static int    length = 3;
    public static string other  = ""y"";
}

public class Program
{
    public static string Read() => Config.name + Config.length + Config.other;
    public static void Main() { }
}";
            var result = new RoslynTranslator().Translate(code);

            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;

            Assert.IsTrue(js.Contains("$name"),
                "an ordinary static named `name` must stay $-escaped (Function.name is read-only)\n" + js);
            Assert.IsTrue(js.Contains("$length"),
                "an ordinary static named `length` must stay $-escaped\n" + js);
        }
    }
}
