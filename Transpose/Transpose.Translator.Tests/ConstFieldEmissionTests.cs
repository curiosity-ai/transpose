using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// A <c>const</c> is emitted as a real static slot holding its value, and every read goes through
    /// that slot rather than being inlined. Inlining baked the value into each consumer, so updating a
    /// base package without rebuilding the packages between it and the app left those carrying the OLD
    /// value; reading the slot takes the value from whichever build of the declaring package is loaded.
    ///
    /// The exception is an <c>[External]</c> type — Transpose never emits a definition for it, so there
    /// is no slot and its consts still inline (<c>int.MaxValue</c> is <c>[External] System.Int32</c>).
    /// </summary>
    [TestClass]
    public class ConstFieldEmissionTests : TranslatorTestBase
    {
        [TestMethod]
        public void ConstsAreEmittedAsStaticFieldsAndReadThroughThem()
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

            // The use site reads the slot, so a rebuilt declaring package changes the value everywhere.
            Assert.IsTrue(js.Contains("return Holder.COUNT;"),
                "a const use site must read the emitted slot, not an inlined literal\n" + js);
            Assert.IsFalse(js.Contains("return 42;"),
                "the const literal must not be inlined at the use site\n" + js);
        }

        /// <summary>An <c>[External]</c> type has no emitted definition, so there is no slot to read —
        /// its consts must still inline. <c>int.MaxValue</c> is the canonical case.</summary>
        [TestMethod]
        public void ExternalTypeConstsStayInlined()
        {
            var code = @"
public class Program
{
    public static int Max() => int.MaxValue;
    public static void Main() { }
}";
            var result = new RoslynTranslator().Translate(code);

            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsTrue(result.Javascript!.Contains("return 2147483647;"),
                "an [External] type's const has no emitted slot and must stay inlined\n" + result.Javascript);
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
