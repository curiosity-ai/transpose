using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// An [External] type's members default to camelCase so .NET PascalCase lands on the native JS
    /// name (Length → length). A member with no lowercase letter anywhere is not PascalCase but a
    /// platform constant — NodeFilter.SHOW_TEXT, Node.ELEMENT_NODE, document.URL, the WebGL GL_*
    /// constants — whose JS name is the all-caps name itself: lowercasing its first letter
    /// fabricated identifiers (sHOW_TEXT, uRL) that exist nowhere, so every such binding constant
    /// read undefined at run time.
    /// </summary>
    [TestClass]
    public class ExternalMemberCasingTests
    {
        [TestMethod]
        public void AllCapsExternalMembersKeepTheirName()
        {
            var code = @"
using Transpose;

[External]
[Name(""NodeFilterFixture"")]
public static class NodeFilterFixture
{
    public static extern uint   SHOW_TEXT { get; }
    public static extern ushort FILTER_ACCEPT { get; }
    public static extern string URL { get; }
    public static extern int    Duration { get; }
}

public class Program
{
    public static void Main()
    {
        System.Console.WriteLine(NodeFilterFixture.SHOW_TEXT + NodeFilterFixture.FILTER_ACCEPT + NodeFilterFixture.URL + NodeFilterFixture.Duration);
    }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, string.Join("\n", result.Errors));

            var js = result.Javascript!;
            Assert.IsTrue(js.Contains("NodeFilterFixture.SHOW_TEXT"), "an ALL_CAPS constant must keep its name\n" + js);
            Assert.IsTrue(js.Contains("NodeFilterFixture.FILTER_ACCEPT"), "an ALL_CAPS constant must keep its name\n" + js);
            Assert.IsTrue(js.Contains("NodeFilterFixture.URL"), "an all-caps acronym member (document.URL) must keep its name\n" + js);
            Assert.IsTrue(js.Contains("NodeFilterFixture.duration"), "a PascalCase member still camelCases onto the native name\n" + js);
            Assert.IsFalse(js.Contains("sHOW_TEXT") || js.Contains("fILTER_ACCEPT") || js.Contains("uRL"),
                "no first-letter-lowercased all-caps names\n" + js);
        }
    }
}
