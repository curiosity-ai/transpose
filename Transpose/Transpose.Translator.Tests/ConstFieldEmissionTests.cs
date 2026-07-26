using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// A <c>const</c> is inlined at every use site, but it must ALSO be emitted as a real static slot
    /// holding its value, so the member exists for reflection, for a debugger, and for hand-written JS
    /// reaching into the type. Transpose previously only inlined, so a type's consts were absent from
    /// the emitted class entirely (the reference runtime emits them as static fields).
    /// </summary>
    [TestClass]
    public class ConstFieldEmissionTests : TranslatorTestBase
    {
        [TestMethod]
        public void ConstsAreEmittedAsStaticFieldsAndStillInlined()
        {
            var code = @"
public class Holder
{
    private const int    COUNT   = 42;
    public  const string MARKER  = ""mark"";
    public  const double RATIO   = 1.5;
    public  const bool   ENABLED = true;
    public  const string NOTHING = null;

    public static int Use() => COUNT;
}

public class Program
{
    public static void Main() { }
}";
            var result = new RoslynTranslator().Translate(code);

            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;

            Assert.IsTrue(js.Contains("COUNT: 42"), "a const must be emitted as a static field with its value\n" + js);
            Assert.IsTrue(js.Contains("MARKER: \"mark\""), "string const missing from the emitted fields\n" + js);
            Assert.IsTrue(js.Contains("RATIO: 1.5"), "double const missing from the emitted fields\n" + js);
            Assert.IsTrue(js.Contains("ENABLED: true"), "bool const missing from the emitted fields\n" + js);
            Assert.IsTrue(js.Contains("NOTHING: null"), "null string const missing from the emitted fields\n" + js);

            // Still inlined at the use site — emitting the slot must not turn reads into field access.
            Assert.IsTrue(js.Contains("return 42;"),
                "a const use site must remain inlined, not become a field read\n" + js);
        }

        /// <summary>An enum's members are consts too, but the enum has its own emit path — the const
        /// path must not emit them a second time.</summary>
        [TestMethod]
        public void EnumMembersAreNotDuplicatedAsStaticFields()
        {
            var code = @"
public enum Color { Red, Green, Blue }

public class Program
{
    public static Color Pick() => Color.Green;
    public static void Main() { }
}";
            var result = new RoslynTranslator().Translate(code);

            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;

            Assert.AreEqual(1, CountOccurrences(js, "Green: 1"),
                "the enum's own emit path already declares its members; the const path must not repeat them\n" + js);
            Assert.AreEqual(1, CountOccurrences(js, "fields: {"),
                "an enum should emit exactly one fields block\n" + js);
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            var n = 0;
            for (var i = haystack.IndexOf(needle); i >= 0; i = haystack.IndexOf(needle, i + needle.Length)) n++;
            return n;
        }

        [TestMethod]
        public async Task ConstBehaviourMatchesNativeAsync()
        {
            await RunTest(@"
using System;

public class Holder
{
    public const int    COUNT   = 42;
    public const string MARKER  = ""mark"";
    public const long   BIG     = 9007199254740993L;
    public const Color  FAVOUR  = Color.Green;
}

public enum Color { Red, Green, Blue }

public class Program
{
    public static void Main()
    {
        Console.WriteLine(Holder.COUNT);
        Console.WriteLine(Holder.MARKER);
        Console.WriteLine(Holder.BIG);
        Console.WriteLine(Holder.FAVOUR);
    }
}");
        }
    }
}
