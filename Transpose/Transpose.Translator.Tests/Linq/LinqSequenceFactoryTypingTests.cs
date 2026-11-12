using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests.Linq
{
    /// <summary>
    /// <c>Enumerable.Empty</c>, <c>Range</c> and <c>Repeat</c> return <c>IEnumerable&lt;T&gt;</c>, as in
    /// .NET. They were declared as returning the binding's <c>EnumerableInstance&lt;T&gt;</c>, which no
    /// other sequence type converts to, so valid C# did not compile: <c>ordered ?? Enumerable.Empty&lt;int&gt;()</c>
    /// (an <c>IOrderedEnumerable</c> does not convert to <c>EnumerableInstance</c>) and
    /// <c>var r = flag ? Enumerable.Range(1, 3) : list</c> (no conversion either way between the arms).
    /// Nothing changes at run time: a call on the result now binds to the extension method, which goes
    /// through <c>Enumerable.from</c>, and that hands a linq.js sequence back unchanged.
    /// </summary>
    [TestClass]
    public class LinqSequenceFactoryTypingTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task EmptyRangeAndRepeatAreTypedAsIEnumerable()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    static IEnumerable<int> OrEmpty(IEnumerable<int> xs) => xs ?? Enumerable.Empty<int>();

    public static void Main()
    {
        var xs = new[] { 3, 1, 2 };
        IOrderedEnumerable<int> o = xs.OrderBy(x => x), none = null;
        Console.WriteLine(string.Join(",", o?.ThenBy(x => x) ?? Enumerable.Empty<int>()) + "|" + (none?.ThenBy(x => x) ?? Enumerable.Empty<int>()).Count());

        var list = new List<int> { 9 };
        bool flag = xs.Length > 2;
        var a = flag ? Enumerable.Range(1, 3) : list;
        var b = !flag ? list : Enumerable.Repeat(7, 2);
        var c = flag ? Enumerable.Empty<int>() : xs;
        Console.WriteLine(string.Join(",", a) + "|" + string.Join(",", b) + "|" + c.Count());
        Console.WriteLine(OrEmpty(null).Any() + "|" + OrEmpty(xs).Sum());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task OperatorsChainedOffEmptyRangeAndRepeatRunLikeNative()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        Console.WriteLine(string.Join(",", Enumerable.Range(0, 5).Where(i => i % 2 == 0).Select(i => i * 10)) + "|" + Enumerable.Range(1, 4).Sum() + "|" + Enumerable.Repeat("ab", 3).Aggregate((p, q) => p + q));
        Console.WriteLine(string.Join(",", Enumerable.Empty<string>().DefaultIfEmpty("none")) + "|" + Enumerable.Empty<int>().Count() + "|" + Enumerable.Range(1, 3).Concat(Enumerable.Repeat(0, 2)).ToList().Count);
        Console.WriteLine(Enumerable.Range(1, 3).ToArray().Length + "|" + Enumerable.Repeat(1, 3).Select((x, i) => i).ToDictionary(i => i).Count + "|" + Enumerable.Range(0, 3).Reverse().First());
        int[] arr = Enumerable.Empty<int>().ToArray();
        Console.WriteLine(arr.Length + "|" + Enumerable.Range(0, 10).Skip(2).Take(3).Last() + "|" + Enumerable.Range(0, 4).OrderByDescending(x => x).First());
        IEnumerable<IGrouping<int, int>> g = Enumerable.Range(0, 6).GroupBy(x => x % 3);
        Console.WriteLine(g.Count() + "|" + string.Join(",", g.Select(x => x.Key + ":" + x.Sum())));
        List<string> words = Enumerable.Repeat("w", 2).ToList();
        words.AddRange(Enumerable.Empty<string>());
        Console.WriteLine(words.Count + "|" + Enumerable.Range(1, 3).SequenceEqual(new[] { 1, 2, 3 }) + "|" + Enumerable.Empty<int>().Max(x => (int?)x));
        foreach (var i in Enumerable.Range(5, 2)) Console.WriteLine(i);
    }
}
""";
            await RunTest(code);
        }
    }
}
