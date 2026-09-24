using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// A JavaScript array reaching C# as a <c>T[]</c>: <c>JSON.parse</c>'d, handed over by a binding,
    /// built by hand-written JS. It is a real JS Array, so the runtime's array helpers duck-type over
    /// it happily — indexing, Length, foreach, LINQ, CopyTo, Sort all work — but it carries no
    /// <c>$type</c>, and that is where Transpose used to stop asking.
    ///
    /// <para>
    /// Two things followed. <b>The type test was unsound</b>: with no element type recorded,
    /// <c>System.Array.is</c> dropped the requested one and answered "it is an array", so a
    /// <c>[1,2,3]</c> off the wire matched <c>string[]</c>, <c>bool[]</c> and <c>DateTime[]</c> alike.
    /// The first arm of an <c>is int[] / is string[]</c> chain therefore always won, and the variable
    /// it bound failed on its first real use — a wrong answer that surfaced far from the test.
    /// And <b>the array had no C# identity</b>: <c>GetType()</c> said "Array" (not even a C# type
    /// name), <c>GetElementType()</c> was null and <c>ToString()</c> said "System.Array".
    /// </para>
    ///
    /// <para>
    /// The element type is not recorded anywhere, but it IS observable, so the fix reads it off the
    /// elements and — having proved it — adopts the array, which settles both halves at once. See
    /// <c>System.Array.matchesUntyped</c>/<c>adopt</c> in <c>Resources/Array.js</c>.
    /// </para>
    /// </summary>
    [TestClass]
    public class JsArrayTypeIdentityTests : TranslatorTestBase
    {
        // ---- the test is sound -------------------------------------------------

        /// <summary>
        /// The bug itself: every one of these answered true before, because the requested element type
        /// was thrown away. A real C# array answers exactly the same way, which is the point.
        /// </summary>
        [TestMethod]
        public async Task AJsArrayMatchesOnlyTheElementTypeItActuallyHolds()
        {
            var output = await RunTest("""
using System;
using Transpose;
public class Program
{
    static object Js(string js) => Script.Write<object>("{0}", Script.Write<object>(js));
    public static void Main()
    {
        Console.WriteLine("numbers  is int[]    : " + (Script.Write<object>("[1,2,3]")   is int[]));
        Console.WriteLine("numbers  is string[] : " + (Script.Write<object>("[1,2,3]")   is string[]));
        Console.WriteLine("numbers  is bool[]   : " + (Script.Write<object>("[1,2,3]")   is bool[]));
        Console.WriteLine("strings  is string[] : " + (Script.Write<object>("['a','b']") is string[]));
        Console.WriteLine("strings  is int[]    : " + (Script.Write<object>("['a','b']") is int[]));
        Console.WriteLine("bools    is bool[]   : " + (Script.Write<object>("[true]")    is bool[]));
        Console.WriteLine("bools    is int[]    : " + (Script.Write<object>("[true]")    is int[]));
        Console.WriteLine("mixed    is int[]    : " + (Script.Write<object>("[1,'a']")   is int[]));
        Console.WriteLine("mixed    is string[] : " + (Script.Write<object>("[1,'a']")   is string[]));
        Console.WriteLine("mixed    is object[] : " + (Script.Write<object>("[1,'a']")   is object[]));
        Console.WriteLine("nested   is int[][]  : " + (Script.Write<object>("[[1],[2]]") is int[][]));
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

            StringAssert.Contains(output, "numbers  is int[]    : True", output);
            StringAssert.Contains(output, "numbers  is string[] : False", output);
            StringAssert.Contains(output, "numbers  is bool[]   : False", output);
            StringAssert.Contains(output, "strings  is string[] : True", output);
            StringAssert.Contains(output, "strings  is int[]    : False", output);
            StringAssert.Contains(output, "bools    is bool[]   : True", output);
            StringAssert.Contains(output, "bools    is int[]    : False", output);
            StringAssert.Contains(output, "mixed    is int[]    : False", output);
            StringAssert.Contains(output, "mixed    is string[] : False", output);
            StringAssert.Contains(output, "mixed    is object[] : True", output);
            StringAssert.Contains(output, "nested   is int[][]  : True", output);
        }

        /// <summary>
        /// The shape the bug was reported as, and the one that made it expensive: the chain used to
        /// take whichever arm was written first and then throw inside it, a page away from the test.
        /// </summary>
        [TestMethod]
        public async Task APatternChainTakesTheArmThatActuallyMatches()
        {
            var output = await RunTest("""
using System;
using Transpose;
public class Program
{
    static string Describe(object o)
    {
        if (o is string[] s) return "string[] of " + s.Length;
        if (o is int[] i)    return "int[] summing " + (i[0] + i[1]);
        if (o is bool[] b)   return "bool[] first " + b[0];
        return "no match";
    }
    public static void Main()
    {
        Console.WriteLine(Describe(Script.Write<object>("[1,2]")));
        Console.WriteLine(Describe(Script.Write<object>("['a','b']")));
        Console.WriteLine(Describe(Script.Write<object>("[true,false]")));
        Console.WriteLine(Describe(Script.Write<object>("[{},{}]")));
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

            StringAssert.Contains(output, "int[] summing 3", output);
            StringAssert.Contains(output, "string[] of 2", output);
            StringAssert.Contains(output, "bool[] first True", output);
            StringAssert.Contains(output, "no match", output);
        }

        /// <summary>A null rules out a value-typed element and is at home in the other two.</summary>
        [TestMethod]
        public async Task ANullElementIsWeighedAgainstTheElementType()
        {
            var output = await RunTest("""
using System;
using Transpose;
public class Program
{
    public static void Main()
    {
        Console.WriteLine("[1,null] is int[]    : " + (Script.Write<object>("[1,null]")     is int[]));
        Console.WriteLine("[1,null] is int?[]   : " + (Script.Write<object>("[1,null]")     is int?[]));
        Console.WriteLine("['a',null] is str[]  : " + (Script.Write<object>("['a',null]")   is string[]));
        Console.WriteLine("[null] is object[]   : " + (Script.Write<object>("[null]")       is object[]));
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

            StringAssert.Contains(output, "[1,null] is int[]    : False", output);
            StringAssert.Contains(output, "[1,null] is int?[]   : True", output);
            StringAssert.Contains(output, "['a',null] is str[]  : True", output);
            StringAssert.Contains(output, "[null] is object[]   : True", output);
        }

        /// <summary>
        /// An empty array matches any <c>T[]</c> — nothing in it contradicts the claim and it is
        /// genuinely usable as one — and is deliberately NOT adopted, because there was no element to
        /// read a type off. Pinning it to whichever T asked first would be a guess, and would make the
        /// very next question about the same array answer false.
        /// </summary>
        [TestMethod]
        public async Task AnEmptyJsArrayMatchesAnyElementTypeAndIsNotPinned()
        {
            var output = await RunTest("""
using System;
using Transpose;
public class Program
{
    public static void Main()
    {
        object empty = Script.Write<object>("[]");
        Console.WriteLine("is int[]    : " + (empty is int[]));
        Console.WriteLine("is string[] : " + (empty is string[]));
        Console.WriteLine("is int[]    : " + (empty is int[]));
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

            StringAssert.Contains(output, "is int[]    : True", output);
            StringAssert.Contains(output, "is string[] : True", output);
        }

        // ---- the narrowed variable is a real C# array --------------------------

        /// <summary>
        /// The other half of the report: the bound variable "is not exactly a C# int array". Every one
        /// of these read as a bare "Array" (or null) before, and every assertion here is the answer a
        /// real <c>new int[]</c> gives — the program runs natively too, so .NET is the oracle.
        /// </summary>
        [TestMethod]
        public async Task TheNarrowedVariableHasTheFullTypeIdentityOfACsharpArray()
        {
            const string body = """
        if (source is int[] a)
        {
            Console.WriteLine("FullName    : " + a.GetType().FullName);
            Console.WriteLine("ElementType : " + a.GetType().GetElementType().FullName);
            Console.WriteLine("IsArray     : " + a.GetType().IsArray);
            Console.WriteLine("ToString    : " + a.ToString());
            Console.WriteLine("Clone type  : " + a.Clone().GetType().FullName);
            Console.WriteLine("Length      : " + a.Length);
            Console.WriteLine("Sum         : " + a.Sum());
            Console.WriteLine("Rank        : " + a.Rank);
        }
        else Console.WriteLine("did not match");
""";

            var output = await RunTest($$"""
using System;
using System.Linq;
using Transpose;
public class Program
{
    public static void Main()
    {
        object source = Script.Write<object>("[1, 2, 3]");
{{body}}
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

            // The same program over a real C# array: .NET decides what every line above should say.
            var native = await RunTest($$"""
using System;
using System.Linq;
using Transpose;
public class Program
{
    public static void Main()
    {
        object source = new int[] { 1, 2, 3 };
{{body}}
        Console.WriteLine("<<DONE>>");
    }
}
""");

            Assert.AreEqual(native, output,
                "a JS array narrowed to int[] should carry the same type identity as a real one\n" + output);
        }

        /// <summary>
        /// Adoption must not COPY. The bound variable has to be the very array the JavaScript still
        /// holds, or a write on either side stops being seen by the other — which is the whole reason
        /// the value came over as an array rather than a list.
        /// </summary>
        [TestMethod]
        public async Task AdoptionKeepsTheIdentityOfTheJavaScriptArray()
        {
            var output = await RunTest("""
using System;
using Transpose;
public class Program
{
    public static void Main()
    {
        object source = Script.Write<object>("globalThis.shared = [1, 2, 3]");
        if (source is int[] a)
        {
            Console.WriteLine("same reference : " + ReferenceEquals(source, a));
            a[0] = 99;
            Console.WriteLine("JS sees write  : " + Script.Write<int>("globalThis.shared[0]"));
            Script.Write("globalThis.shared[1] = 77");
            Console.WriteLine("C# sees write  : " + a[1]);
        }
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

            StringAssert.Contains(output, "same reference : True", output);
            StringAssert.Contains(output, "JS sees write  : 99", output);
            StringAssert.Contains(output, "C# sees write  : 77", output);
        }

        /// <summary>
        /// Adopting mutates an array the application got from somewhere else and may well hand back,
        /// so the stamp is non-enumerable. <c>System.Array.type</c> assigns <c>$type</c> plainly, and
        /// an own enumerable function property makes <c>structuredClone</c> refuse the array outright
        /// — so merely TESTING a value would have broken a later <c>postMessage</c> of it.
        /// </summary>
        [TestMethod]
        public async Task AdoptionLeavesTheArrayUsableFromJavaScript()
        {
            var output = await RunTest("""
using System;
using Transpose;
public class Program
{
    public static void Main()
    {
        object source = Script.Write<object>("[1, 2, 3]");
        if (source is int[] a)
        {
            Console.WriteLine("JSON.stringify  : " + Script.Write<string>("JSON.stringify({0})", a));
            Console.WriteLine("Object.keys     : " + Script.Write<string>("JSON.stringify(Object.keys({0}))", a));
            Console.WriteLine("spread          : " + Script.Write<string>("JSON.stringify([...{0}])", a));
            Console.WriteLine("structuredClone : " + Script.Write<bool>("(function(v){try{structuredClone(v);return true;}catch(e){return false;}})({0})", a));
            Console.WriteLine("for-in keys     : " + Script.Write<string>("(function(v){var k=[];for(var p in v)k.push(p);return k.join(',');})({0})", a));
        }
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

            StringAssert.Contains(output, "JSON.stringify  : [1,2,3]", output);
            StringAssert.Contains(output, """Object.keys     : ["0","1","2"]""", output);
            StringAssert.Contains(output, "spread          : [1,2,3]", output);
            StringAssert.Contains(output, "structuredClone : True", output);
            StringAssert.Contains(output, "for-in keys     : 0,1,2", output);
        }

        /// <summary><c>as</c> runs the same test, so it adopts on the same terms.</summary>
        [TestMethod]
        public async Task AsAdoptsAndAFailedAsIsStillNull()
        {
            var output = await RunTest("""
using System;
using Transpose;
public class Program
{
    public static void Main()
    {
        var ok = Script.Write<object>("[4,5]") as int[];
        Console.WriteLine("as int[]    : " + (ok == null ? "null" : ok.GetType().FullName + " len=" + ok.Length));
        var no = Script.Write<object>("['a']") as int[];
        Console.WriteLine("as int[] bad: " + (no == null ? "null" : "NOT NULL"));
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

            StringAssert.Contains(output, "as int[]    : System.Int32[] len=2", output);
            StringAssert.Contains(output, "as int[] bad: null", output);
        }

        /// <summary>
        /// The documented cost of giving an untyped array an identity at all. Every JS number is a
        /// double, so a fresh <c>[1,2,3]</c> answers true to both <c>int[]</c> and <c>double[]</c> —
        /// whichever is asked first adopts it, and the other then answers false, exactly as it would
        /// for an array Transpose built. Confined to the numeric types JavaScript cannot tell apart,
        /// and the same ambiguity the boxed-numeric rule already carries.
        /// </summary>
        [TestMethod]
        public async Task WhicheverNumericTypeAsksFirstSettlesTheArray()
        {
            var output = await RunTest("""
using System;
using Transpose;
public class Program
{
    public static void Main()
    {
        object a = Script.Write<object>("[1,2,3]");
        Console.WriteLine("a: int[] first    -> " + (a is int[]) + " then double[] -> " + (a is double[]));
        object b = Script.Write<object>("[1,2,3]");
        Console.WriteLine("b: double[] first -> " + (b is double[]) + " then int[] -> " + (b is int[]));
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

            StringAssert.Contains(output, "a: int[] first    -> True then double[] -> False", output);
            StringAssert.Contains(output, "b: double[] first -> True then int[] -> False", output);
        }

        /// <summary>
        /// A real C# array must not pay for any of this: it answers from its own <c>$type</c> and
        /// never reaches the element walk. Pinned by behaviour — an array whose elements do NOT match
        /// its declared type still answers by the declaration.
        /// </summary>
        [TestMethod]
        public async Task ARealArrayStillAnswersFromItsDeclaredType()
        {
            var output = await RunTest("""
using System;
using Transpose;
public class Program
{
    public static void Main()
    {
        object a = new object[] { 1, "two", null };
        Console.WriteLine("object[] is object[] : " + (a is object[]));
        Console.WriteLine("object[] is int[]    : " + (a is int[]));
        Console.WriteLine("object[] is string[] : " + (a is string[]));
        object e = new string[0];
        Console.WriteLine("empty string[] is string[] : " + (e is string[]));
        Console.WriteLine("empty string[] is int[]    : " + (e is int[]));
        Console.WriteLine("<<DONE>>");
    }
}
""");

            StringAssert.Contains(output, "object[] is object[] : True", output);
            StringAssert.Contains(output, "object[] is int[]    : False", output);
            StringAssert.Contains(output, "empty string[] is string[] : True", output);
            StringAssert.Contains(output, "empty string[] is int[]    : False", output);
        }
    }
}
