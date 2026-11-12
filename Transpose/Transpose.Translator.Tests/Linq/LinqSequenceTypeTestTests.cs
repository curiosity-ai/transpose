using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests.Linq
{
    /// <summary>
    /// <c>is</c> / <c>as</c> / a type pattern against <c>IEnumerable&lt;T&gt;</c> on a LINQ result.
    ///
    /// One linq.js constructor serves every element type, so a sequence only registered the
    /// non-generic <c>IEnumerable</c>: every <c>is IEnumerable&lt;T&gt;</c> of a LINQ result answered
    /// false and <c>as IEnumerable&lt;T&gt;</c> handed back null. The emitter now records the element type
    /// on the outermost LINQ call of a chain (<c>TransposeR.seqOf</c>), and <c>Transpose.is</c> compares it
    /// with .NET's rules — exact for a value type, covariant for a reference type.
    ///
    /// <para><b>Known limitation:</b> a sequence nothing stamped — one built by hand-written JavaScript,
    /// or a single group out of <c>GroupBy</c> (linq.js builds those itself) — answers true for every
    /// <c>IEnumerable&lt;T&gt;</c>, as an untyped JavaScript array does: a lazy sequence cannot be run to
    /// look at its elements.</para>
    /// </summary>
    [TestClass]
    public class LinqSequenceTypeTestTests : TranslatorTestBase
    {
        private const string Code = """
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class Animal { public string Name; public override string ToString() => Name; }
public class Dog : Animal { }

public class Program
{
    static IEnumerable<int> Evens(int[] xs) => xs.Where(x => x % 2 == 0);
    static object AsObject<T>(IEnumerable<T> s) => s.Select(x => x);
    static string Kind(object o) => o switch
    {
        IEnumerable<string> s => "strings:" + string.Join("", s),
        IEnumerable<int> i => "ints:" + i.Sum(),
        IEnumerable<Animal> a => "animals:" + string.Join("/", a),
        IEnumerable e => "other",
        _ => "not a sequence",
    };

    public static void Main()
    {
        var xs = new[] { 1, 2, 3, 4 };
        object a = xs.Where(x => x > 1), b = Enumerable.Empty<int>(), c = Enumerable.Range(0, 2),
               d = xs.OrderBy(x => x), e = xs.Select(x => "s" + x), f = from x in xs where x > 2 select x * 2,
               g = Evens(xs), h = xs.GroupBy(x => x % 2), k = xs.ToLookup(x => x % 2), l = xs.OrderBy(x => x).ThenBy(x => -x),
               m = xs.Order();
        foreach (var o in new[] { a, b, c, d, e, f, g, h, k, l, m })
            Console.WriteLine((o is IEnumerable<int>) + " " + (o is IEnumerable<string>) + " " + (o is IEnumerable<object>) + " " + (o is IEnumerable) + " " + ((o as IEnumerable<int>)?.Count()));

        Console.WriteLine((Enumerable.Empty<int>() is IEnumerable<int>) + " " + (Enumerable.Repeat("x", 1) is IEnumerable<object>));
        Console.WriteLine(Kind(xs.Where(x => x < 3)) + " " + Kind(new[] { "a", "b" }.Select(x => x.ToUpper())) + " " + Kind(new List<Dog> { new Dog { Name = "rex" } }.Where(x => true)) + " " + Kind(new[] { 1.5 }.Select(x => x)) + " " + Kind(3));
        IEnumerable<Dog> dogs = new[] { new Dog { Name = "fido" } }.Where(x => x.Name.Length > 0);
        object od = dogs;
        Console.WriteLine((od is IEnumerable<Animal>) + " " + (od is IEnumerable<Dog>) + " " + (od is IEnumerable<string>));
        Console.WriteLine((AsObject(xs) is IEnumerable<int>) + " " + (AsObject(new[] { "q" }) is IEnumerable<string>) + " " + (AsObject(xs) is IEnumerable<string>));
        object h2 = xs.GroupBy(x => x % 2);
        Console.WriteLine((h2 is IEnumerable<IGrouping<int, int>>) + " " + (k is IEnumerable<IGrouping<int, int>>) + " " + (xs.ToLookup(x => x) is IEnumerable<int>));
        var anon = xs.Select(x => new { X = x });
        object oa = anon;
        Console.WriteLine((oa is IEnumerable<object>) + " " + (oa is IEnumerable<int>));
    }
}
""";

        /// <summary>Every kind of LINQ result — method and query syntax, ordered, grouped, a lookup,
        /// Empty/Range/Repeat/Order, through a method returning IEnumerable&lt;T&gt;, through a generic
        /// method, of an anonymous type — tested against IEnumerable&lt;int/string/object&gt;, with
        /// covariance and in a switch.</summary>
        [TestMethod]
        public async Task GenericEnumerableTestsOnLinqResultsRunLikeNative() => await RunTest(Code);

        /// <summary>Only the outermost call of a chain is stamped: an intermediate result is never
        /// observable as a value.</summary>
        [TestMethod]
        public void OnlyTheOutermostCallOfAChainIsStamped()
        {
            var code = """
using System;
using System.Linq;
public class Program
{
    public static void Main()
    {
        var xs = new[] { 1, 2, 3 };
        var r = xs.Where(x => x > 1).Select(x => "v" + x);
        Console.WriteLine(xs.Where(x => x > 1).Count() + " " + r.First());
    }
}
""";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;
            Assert.AreEqual(1, Regex.Matches(js, @"TransposeR\.seqOf\(").Count, js);
            StringAssert.Contains(js, "), System.String);");
        }

        /// <summary>The documented limitation, pinned: an unstamped sequence answers true for any
        /// IEnumerable&lt;T&gt;. (Native .NET says False for the string test of an int group.)</summary>
        [TestMethod]
        public async Task UnstampedSequencesAnswerPermissively()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;
public class Program
{
    public static void Main()
    {
        object group = new[] { 1, 2, 3 }.GroupBy(x => x % 2).First();
        Console.WriteLine((group is IEnumerable<int>) + " " + (group is IEnumerable<string>));
        object raw = Transpose.Script.Write<object>("System.Linq.Enumerable.from([1, 2])");
        Console.WriteLine((raw is IEnumerable<int>) + " " + (raw is IEnumerable<string>) + " " + ((raw as IEnumerable<int>)?.Count()));
    }
}
""";
            var output = await RunTest(code, skipRoslyn: true);
            Assert.AreEqual("True True\nTrue True 2", output.Replace("\r\n", "\n").Trim());
        }
    }
}
