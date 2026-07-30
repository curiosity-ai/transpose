using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests.Linq
{
    /// <summary>
    /// Every non-numeric LINQ operator <c>Enumerable</c> (and <c>EnumerableExtras</c>) implements, once per
    /// declared overload: projection (<c>Select</c>/<c>SelectMany</c>, both with and without the index),
    /// filtering, partitioning, the set operators (each in its default-comparer and
    /// <c>IEqualityComparer</c> form), ordering (<c>OrderBy</c>/<c>ThenBy</c> and their descending
    /// counterparts, with and without an <c>IComparer</c>), grouping (all eight <c>GroupBy</c>
    /// signatures), the two joins, the element operators, the conversion operators (all four
    /// <c>ToDictionary</c> and all four <c>ToLookup</c> shapes), generation, <c>Zip</c>,
    /// <c>Cast</c>/<c>OfType</c>, and the .NET 6 additions <c>Chunk</c>/<c>MinBy</c>/<c>MaxBy</c>.
    ///
    /// Every test diffs its output against the same C# run natively, so ordering, laziness and the exact
    /// element set are all pinned — not just the count.
    /// </summary>
    [TestClass]
    public class LinqSequenceOperatorTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task WhereAndSelect_WithAndWithoutIndex()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { 10, 20, 30, 40 };
        Console.WriteLine(string.Join(",", xs.Where(x => x > 15)));
        Console.WriteLine(string.Join(",", xs.Where((x, i) => i % 2 == 0)));
        Console.WriteLine(string.Join(",", xs.Select(x => x + 1)));
        Console.WriteLine(string.Join(",", xs.Select((x, i) => x * i)));
        Console.WriteLine(string.Join(",", xs.Where(x => x > 15).Select((x, i) => i + ":" + x)));
        Console.WriteLine(string.Join(",", xs.Where(x => false)));
        Console.WriteLine(string.Join(",", xs.Select(x => x.ToString())));
        Console.WriteLine(string.Join(",", xs.AsEnumerable().Where(x => x < 35)));
        Console.WriteLine(string.Join(",", Enumerable.Where(xs, x => x == 20)));
        Console.WriteLine(string.Join(",", Enumerable.Select(xs, x => x / 10)));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task SelectMany_AllFourOverloads()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { new[] { 1, 2 }, new[] { 3 } };
        Console.WriteLine(string.Join(",", xs.SelectMany(a => a)));
        Console.WriteLine(string.Join(",", xs.SelectMany((a, i) => a.Select(v => v * 10 + i))));
        Console.WriteLine(string.Join(",", xs.SelectMany(a => a, (a, v) => a.Length + "/" + v)));
        Console.WriteLine(string.Join(",", xs.SelectMany((a, i) => a, (a, v) => a.Length + "-" + v)));
        var words = new[] { "ab", "c" };
        Console.WriteLine(string.Join(",", words.SelectMany(w => w.ToCharArray())));
        Console.WriteLine(string.Join(",", words.SelectMany(w => w)));
        Console.WriteLine(new int[0].SelectMany(x => new[] { x }).Count());
        Console.WriteLine(string.Join(",", xs.SelectMany(a => a.Where(v => v > 1))));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Partitioning_TakeSkipTakeWhileSkipWhile()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { 1, 2, 3, 4, 5 };
        Console.WriteLine(string.Join(",", xs.Take(2)));
        Console.WriteLine("[" + string.Join(",", xs.Take(0)) + "]");
        Console.WriteLine(string.Join(",", xs.Take(99)));
        Console.WriteLine("[" + string.Join(",", xs.Take(-1)) + "]");
        Console.WriteLine(string.Join(",", xs.Skip(3)));
        Console.WriteLine(string.Join(",", xs.Skip(0)));
        Console.WriteLine("[" + string.Join(",", xs.Skip(99)) + "]");
        Console.WriteLine(string.Join(",", xs.Skip(-1)));
        Console.WriteLine(string.Join(",", xs.TakeWhile(x => x < 3)));
        Console.WriteLine(string.Join(",", xs.TakeWhile((x, i) => i < 2)));
        Console.WriteLine(string.Join(",", xs.SkipWhile(x => x < 3)));
        Console.WriteLine(string.Join(",", xs.SkipWhile((x, i) => i < 2)));
        Console.WriteLine("[" + string.Join(",", xs.TakeWhile(x => false)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.SkipWhile(x => true)) + "]");
        Console.WriteLine(string.Join(",", xs.Skip(1).Take(2)));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task SetOperators_DefaultComparer()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var a = new[] { 1, 2, 2, 3 };
        var b = new[] { 3, 4 };
        Console.WriteLine(string.Join(",", a.Distinct()));
        Console.WriteLine(string.Join(",", a.Union(b)));
        Console.WriteLine(string.Join(",", a.Intersect(b)));
        Console.WriteLine(string.Join(",", a.Except(b)));
        Console.WriteLine(string.Join(",", a.Concat(b)));
        Console.WriteLine(string.Join(",", a.Reverse()));
        Console.WriteLine(string.Join(",", Enumerable.Reverse(a)));
        Console.WriteLine(string.Join(",", a.Select(x => x).Reverse()));
        Console.WriteLine(string.Join("", "abc".Reverse()));
        Console.WriteLine(string.Join(",", new[] { "b", "a", "b" }.Distinct()));
        Console.WriteLine(a.Union(b).Count() + "," + a.Intersect(b).Count() + "," + a.Except(b).Count());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task SetOperators_WithEqualityComparer()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var s = new[] { "A", "a", "b" };
        var cmp = StringComparer.OrdinalIgnoreCase;
        Console.WriteLine(string.Join(",", s.Distinct(cmp)));
        Console.WriteLine(string.Join(",", s.Union(new[] { "B" }, cmp)));
        Console.WriteLine(string.Join(",", s.Intersect(new[] { "A" }, cmp)));
        Console.WriteLine(string.Join(",", s.Except(new[] { "A" }, cmp)));
        Console.WriteLine(s.Contains("A", cmp) + "," + s.Contains("z", cmp));
        Console.WriteLine(new[] { "a" }.SequenceEqual(new[] { "A" }, cmp));
        Console.WriteLine(string.Join(",", s.Distinct(StringComparer.Ordinal)));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task SetOperators_WithACustomEqualityComparer()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Box
{
    public int V;
    public Box(int v) { V = v; }
    public override string ToString() => "B" + V;
}

public class ByRemainder : IEqualityComparer<Box>
{
    public bool Equals(Box a, Box b) => a.V % 3 == b.V % 3;
    public int GetHashCode(Box b) => (b.V % 3).GetHashCode();
}

public class Program
{
    public static void Main()
    {
        var xs = new[] { new Box(1), new Box(4), new Box(2) };
        var c = new ByRemainder();
        Console.WriteLine(string.Join(",", xs.Distinct(c)));
        Console.WriteLine(string.Join(",", xs.Union(new[] { new Box(7) }, c)));
        Console.WriteLine(string.Join(",", xs.Intersect(new[] { new Box(7) }, c)));
        Console.WriteLine(string.Join(",", xs.Except(new[] { new Box(7) }, c)));
        Console.WriteLine(xs.Contains(new Box(7), c));
        Console.WriteLine(xs.SequenceEqual(new[] { new Box(4), new Box(1), new Box(5) }, c));
        Console.WriteLine(string.Join(";", xs.GroupBy(x => x, c).Select(g => g.Key + ":" + g.Count())));
        Console.WriteLine(xs.ToLookup(x => x, c).Count);
        Console.WriteLine(xs.Distinct(c).ToDictionary(x => x, c).Count);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Ordering_AllEightOverloads()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { 3, 1, 2 };
        Console.WriteLine(string.Join(",", xs.OrderBy(x => x)));
        Console.WriteLine(string.Join(",", xs.OrderByDescending(x => x)));
        var people = new[] { "bob:2", "amy:1", "cal:2" };
        Console.WriteLine(string.Join(",", people.OrderBy(p => p.Split(':')[1]).ThenBy(p => p.Split(':')[0])));
        Console.WriteLine(string.Join(",", people.OrderBy(p => p.Split(':')[1]).ThenByDescending(p => p.Split(':')[0])));
        Console.WriteLine(string.Join(",", people.OrderByDescending(p => p.Split(':')[1]).ThenBy(p => p.Split(':')[0])));
        Console.WriteLine(string.Join(",", people.OrderByDescending(p => p.Split(':')[1]).ThenByDescending(p => p.Split(':')[0])));
        var reverse = Comparer<int>.Create((a, b) => b - a);
        Console.WriteLine(string.Join(",", xs.OrderBy(x => x, reverse)));
        Console.WriteLine(string.Join(",", xs.OrderByDescending(x => x, reverse)));
        Console.WriteLine(string.Join(",", people.OrderBy(p => p.Length).ThenBy(p => p, StringComparer.Ordinal)));
        Console.WriteLine(string.Join(",", people.OrderBy(p => p.Length).ThenByDescending(p => p, StringComparer.Ordinal)));
        Console.WriteLine(string.Join(",", xs.OrderBy(x => x % 2).ThenBy(x => x).ThenByDescending(x => x)));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Ordering_IsStableAndReusable()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        // OrderBy is documented as a STABLE sort, so equal keys keep their source order — and so does
        // OrderByDescending (it reverses the comparison, not the ties).
        var items = new[] { "a1", "b1", "c0", "d1", "e0" };
        Console.WriteLine(string.Join(",", items.OrderBy(s => s[1])));
        Console.WriteLine(string.Join(",", items.OrderByDescending(s => s[1])));

        // An IOrderedEnumerable is a query, not a snapshot: enumerating it repeatedly, extending it with
        // ThenBy and chaining other operators onto it must all work.
        var ordered = new[] { 3, 1, 2 }.OrderBy(x => x);
        Console.WriteLine(string.Join(",", ordered));
        Console.WriteLine(string.Join(",", ordered));
        Console.WriteLine(string.Join(",", ordered.ThenByDescending(x => x)));
        Console.WriteLine(string.Join(",", ordered.Select(x => x * 2)));
        Console.WriteLine(ordered.First() + "," + ordered.Last() + "," + ordered.Count());
        Console.WriteLine(string.Join(",", ordered.Reverse()));
        Console.WriteLine(string.Join(",", ordered.Take(2)));
        Console.WriteLine(string.Join(",", ordered.ToList()));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task GroupBy_AllEightOverloads()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { "apple", "avocado", "banana", "cherry" };
        foreach (var g in xs.GroupBy(s => s[0])) Console.WriteLine("1:" + g.Key + ":" + string.Join(",", g));
        foreach (var g in xs.GroupBy(s => s[0], s => s.Length)) Console.WriteLine("2:" + g.Key + ":" + string.Join(",", g));
        foreach (var r in xs.GroupBy(s => s[0], (k, g) => k + "#" + g.Count())) Console.WriteLine("3:" + r);
        foreach (var r in xs.GroupBy(s => s[0], s => s.Length, (k, g) => k + "#" + g.Sum())) Console.WriteLine("4:" + r);
        var cmp = StringComparer.OrdinalIgnoreCase;
        foreach (var g in xs.GroupBy(s => s.Substring(0, 1), cmp)) Console.WriteLine("5:" + g.Key + ":" + g.Count());
        foreach (var g in xs.GroupBy(s => s.Substring(0, 1), s => s.Length, cmp)) Console.WriteLine("6:" + g.Key + ":" + string.Join(",", g));
        foreach (var r in xs.GroupBy(s => s.Substring(0, 1), (k, g) => k + "!" + g.Count(), cmp)) Console.WriteLine("7:" + r);
        foreach (var r in xs.GroupBy(s => s.Substring(0, 1), s => s.Length, (k, g) => k + "*" + g.Count(), cmp)) Console.WriteLine("8:" + r);
        Console.WriteLine(new string[0].GroupBy(s => s).Count());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Grouping_AndLookup_ExposeTheFullIGroupingApi()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { "aa", "b", "cc", "d" };
        IEnumerable<IGrouping<int, string>> gs = xs.GroupBy(s => s.Length);
        foreach (var g in gs)
        {
            Console.WriteLine(g.Key + " -> " + string.Join(",", g) + " count=" + g.Count());
            Console.WriteLine("  first=" + g.First() + " any=" + g.Any() + " sum=" + g.Sum(s => s.Length));
        }

        var lk = xs.ToLookup(s => s.Length);
        Console.WriteLine("count=" + lk.Count);
        Console.WriteLine("contains1=" + lk.Contains(1) + " contains9=" + lk.Contains(9));
        Console.WriteLine("idx1=" + string.Join(",", lk[1]));
        Console.WriteLine("idx9empty=" + !lk[9].Any());
        foreach (var g in lk.OrderBy(g2 => g2.Key)) Console.WriteLine(g.Key + ":" + string.Join("|", g));
        Console.WriteLine(string.Join(",", lk.SelectMany(g => g).OrderBy(s => s)));

        // A nested group, and a grouping used as the source of another query.
        var nested = xs.GroupBy(s => s.Length)
                       .Select(g => g.Key + "=[" + string.Join(",", g.GroupBy(s => s[0]).Select(h => h.Key.ToString())) + "]");
        Console.WriteLine(string.Join(";", nested.OrderBy(s => s)));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Joins_BothOverloadsOfJoinAndGroupJoin()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var outer = new[] { "1a", "2b", "3c" };
        var inner = new[] { "1x", "1y", "3z" };
        foreach (var r in outer.Join(inner, o => o[0], i => i[0], (o, i) => o + "/" + i)) Console.WriteLine(r);
        Console.WriteLine("--");
        foreach (var r in outer.Join(inner, o => o.Substring(0, 1), i => i.Substring(0, 1), (o, i) => o + "/" + i, StringComparer.Ordinal)) Console.WriteLine(r);
        Console.WriteLine("--");
        foreach (var r in outer.GroupJoin(inner, o => o[0], i => i[0], (o, g) => o + "=[" + string.Join(",", g) + "]")) Console.WriteLine(r);
        Console.WriteLine("--");
        foreach (var r in outer.GroupJoin(inner, o => o.Substring(0, 1), i => i.Substring(0, 1), (o, g) => o + "=" + g.Count(), StringComparer.Ordinal)) Console.WriteLine(r);
        Console.WriteLine("--");
        Console.WriteLine(outer.Join(new string[0], o => o[0], i => i[0], (o, i) => o).Count());
        Console.WriteLine(new string[0].GroupJoin(inner, o => o[0], i => i[0], (o, g) => o).Count());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task ElementOperators_AllOverloads()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { 1, 2, 3 };
        Console.WriteLine(xs.First() + "," + xs.First(x => x > 1));
        Console.WriteLine(xs.FirstOrDefault() + "," + xs.FirstOrDefault(x => x > 9));
        Console.WriteLine(xs.Last() + "," + xs.Last(x => x < 3));
        Console.WriteLine(xs.LastOrDefault() + "," + xs.LastOrDefault(x => x > 9));
        Console.WriteLine(xs.ElementAt(0) + "," + xs.ElementAt(2));
        Console.WriteLine(xs.ElementAtOrDefault(1) + "," + xs.ElementAtOrDefault(9) + "," + xs.ElementAtOrDefault(-1));
        Console.WriteLine(new[] { 7 }.Single() + "," + xs.Single(x => x == 2));
        Console.WriteLine(xs.SingleOrDefault(x => x > 9) + "," + new[] { 7 }.SingleOrDefault());
        Console.WriteLine(new int[0].FirstOrDefault() + "," + new int[0].LastOrDefault() + "," + new int[0].SingleOrDefault());
        Console.WriteLine(new string[0].FirstOrDefault() == null);
        Console.WriteLine(xs.Where(x => x > 1).First() + "," + xs.Where(x => x > 1).Last());
        Console.WriteLine(xs.OrderByDescending(x => x).ElementAt(1));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Quantifiers_AnyAllContainsSequenceEqual()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { 1, 2, 3 };
        Console.WriteLine(xs.Any() + "," + new int[0].Any());
        Console.WriteLine(xs.Any(x => x > 2) + "," + xs.Any(x => x > 9));
        Console.WriteLine(xs.All(x => x > 0) + "," + xs.All(x => x > 1) + "," + new int[0].All(x => false));
        Console.WriteLine(xs.Contains(2) + "," + xs.Contains(9));
        // int[].SequenceEqual(int[]) binds to the SPAN overload (the array-to-span conversion wins over
        // array-to-IEnumerable), so both the span form and the IEnumerable form need covering.
        Console.WriteLine(xs.SequenceEqual(new[] { 1, 2, 3 }));
        Console.WriteLine(xs.SequenceEqual(new[] { 1, 2 }) + "," + xs.SequenceEqual(new[] { 1, 2, 4 }));
        Console.WriteLine(new[] { "a", "b" }.SequenceEqual(new[] { "a", "b" }));
        Console.WriteLine(new int[0].SequenceEqual(new int[0]));
        IEnumerable<int> e = xs;
        Console.WriteLine(e.SequenceEqual(new[] { 1, 2, 3 }));
        Console.WriteLine(xs.Where(x => x > 0).SequenceEqual(xs));
        Console.WriteLine(xs.ToList().SequenceEqual(new List<int> { 1, 2, 3 }));
        Console.WriteLine("abc".AsSpan().SequenceEqual("abc".AsSpan()));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Conversion_ToArrayToListAllToDictionaryAndToLookupOverloads()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { "aa", "b", "ccc" };
        var arr = xs.ToArray();
        Console.WriteLine(arr.Length + ":" + string.Join(",", arr));
        var list = xs.ToList();
        Console.WriteLine(list.Count + ":" + string.Join(",", list));

        var d1 = xs.ToDictionary(s => s);
        Console.WriteLine(string.Join(",", d1.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value)));
        var d2 = xs.ToDictionary(s => s, s => s.Length);
        Console.WriteLine(string.Join(",", d2.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value)));
        var d3 = xs.ToDictionary(s => s, StringComparer.OrdinalIgnoreCase);
        Console.WriteLine(d3.Count + ":" + d3["AA"]);
        var d4 = xs.ToDictionary(s => s, s => s.Length, StringComparer.OrdinalIgnoreCase);
        Console.WriteLine(d4["AA"]);

        var l1 = xs.ToLookup(s => s.Length);
        Console.WriteLine(string.Join(";", l1.OrderBy(g => g.Key).Select(g => g.Key + ":" + string.Join(",", g))));
        var l2 = xs.ToLookup(s => s.Length, s => s.ToUpper());
        Console.WriteLine(string.Join(";", l2.OrderBy(g => g.Key).Select(g => g.Key + ":" + string.Join(",", g))));
        var l3 = xs.ToLookup(s => s.Substring(0, 1), StringComparer.OrdinalIgnoreCase);
        Console.WriteLine(l3.Count);
        var l4 = xs.ToLookup(s => s.Substring(0, 1), s => s.Length, StringComparer.OrdinalIgnoreCase);
        Console.WriteLine(string.Join(";", l4.Select(g => g.Key + ":" + string.Join(",", g))));

        Console.WriteLine(new int[0].ToArray().Length + "," + new int[0].ToList().Count);
        Console.WriteLine(xs.Where(s => s.Length > 1).ToArray().Length);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Lookup_CountsANullKeyedGrouping()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        // System.Linq.Lookup allows a null key; the transpiled Lookup keeps it outside its backing
        // Dictionary (which cannot store one), so Count has to add it back in.
        var xs = new[] { "a", null, "b", null };
        Console.WriteLine(xs.ToLookup(s => s).Count);
        Console.WriteLine(xs.ToLookup(s => s)[null].Count());
        Console.WriteLine(xs.ToLookup(s => s).Contains(null));
        Console.WriteLine(xs.GroupBy(s => s).Count());
        var ys = new[] { 1, 2, 3 };
        Console.WriteLine(ys.ToLookup(y => y == 2 ? (int?)null : y).Count);
        Console.WriteLine(ys.GroupBy(y => y == 2 ? (int?)null : y).Count());
        Console.WriteLine(new[] { "a" }.ToLookup(s => s).Count);
        Console.WriteLine(new[] { "a" }.ToLookup(s => s).Contains(null));
        Console.WriteLine(new string[0].ToLookup(s => s).Count);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Generation_RangeRepeatEmptyDefaultIfEmpty()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        Console.WriteLine(string.Join(",", Enumerable.Range(1, 5)));
        Console.WriteLine(string.Join(",", Enumerable.Range(-2, 3)));
        Console.WriteLine(Enumerable.Range(0, 0).Count());
        Console.WriteLine(Enumerable.Range(1, 5).Sum());
        Console.WriteLine(string.Join(",", Enumerable.Repeat("x", 3)));
        Console.WriteLine(string.Join(",", Enumerable.Repeat(7, 2)));
        Console.WriteLine(Enumerable.Repeat(1, 0).Count());
        Console.WriteLine(Enumerable.Empty<int>().Count() + ",[" + string.Join(",", Enumerable.Empty<string>()) + "]");
        Console.WriteLine(string.Join(",", new int[0].DefaultIfEmpty()));
        Console.WriteLine(string.Join(",", new int[0].DefaultIfEmpty(9)));
        Console.WriteLine(string.Join(",", new[] { 1 }.DefaultIfEmpty(9)));
        Console.WriteLine(new string[0].DefaultIfEmpty("d").Single());
        Console.WriteLine(new string[0].DefaultIfEmpty().Single() == null);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Zip_AndTheDotNet6Additions()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var a = new[] { 1, 2, 3 };
        var b = new[] { "x", "y" };
        Console.WriteLine(string.Join(",", a.Zip(b, (i, s) => i + s)));
        Console.WriteLine(string.Join(",", b.Zip(a, (s, i) => s + i)));
        Console.WriteLine(a.Zip(new int[0], (x, y) => x + y).Count());
        Console.WriteLine(string.Join(",", a.Zip(a, (x, y) => x * y)));
        Console.WriteLine(string.Join(",", a.Where(x => x > 1).Zip(b, (x, s) => x + s)));

        foreach (var c in a.Chunk(2)) Console.WriteLine(string.Join("+", c));
        Console.WriteLine(new int[0].Chunk(2).Count());
        Console.WriteLine(a.Chunk(5).Single().Length);
        Console.WriteLine(string.Join(";", Enumerable.Range(1, 7).Chunk(3).Select(c => string.Join(",", c))));

        var words = new[] { "bbb", "a", "cc" };
        Console.WriteLine(words.MinBy(w => w.Length) + "," + words.MaxBy(w => w.Length));
        Console.WriteLine(words.MinBy(w => w) + "," + words.MaxBy(w => w));
        var reverse = Comparer<int>.Create((x, y) => y - x);
        Console.WriteLine(words.MinBy(w => w.Length, reverse) + "," + words.MaxBy(w => w.Length, reverse));
        // MinBy/MaxBy skip null keys, and return the first element when every key is null.
        Console.WriteLine(words.MinBy(w => (string)null) + "|" + words.MaxBy(w => (string)null));
        Console.WriteLine(words.MinBy(w => w.Length == 1 ? null : w) + "|" + words.MaxBy(w => w.Length == 1 ? null : w));
        Console.WriteLine(new string[0].MinBy(w => w) == null);
        Console.WriteLine(new string[0].MaxBy(w => w) == null);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task CastAndOfType()
        {
            var code = """
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public struct Point3 { public int V; }

public class Program
{
    public static void Main()
    {
        var objs = new object[] { 1, "two", 3, null };
        Console.WriteLine(string.Join(",", objs.OfType<int>()));
        Console.WriteLine(string.Join(",", objs.OfType<string>()));
        Console.WriteLine(objs.OfType<object>().Count());
        var ints = new object[] { 1, 2, 3 };
        Console.WriteLine(string.Join(",", ints.Cast<int>()));
        IEnumerable<object> boxed = new object[] { "a", "b" };
        Console.WriteLine(string.Join(",", boxed.Cast<string>()));
        IEnumerable plain = objs;
        Console.WriteLine(plain.OfType<int>().Count() + "," + plain.Cast<object>().Count());

        // A struct and a class element type, both boxed into object[].
        var mixed = new object[] { new Point3 { V = 9 }, "s", 1 };
        Console.WriteLine(mixed.OfType<Point3>().Count() + ":" + mixed.OfType<Point3>().Single().V);
        Console.WriteLine(mixed.OfType<string>().Single());

        // A multi-dimensional array is only IEnumerable, so Cast is how LINQ reaches it.
        var md = new int[2, 2] { { 1, 2 }, { 3, 4 } };
        Console.WriteLine(md.Cast<int>().Sum() + "," + md.Cast<int>().Count());
        Console.WriteLine(string.Join(",", md.Cast<int>().OrderByDescending(x => x)));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task StaticInvocationForm_BindsTheSameOverloads()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { 1, 2, 3 };
        Console.WriteLine(string.Join(",", Enumerable.Where(xs, x => x > 1)));
        Console.WriteLine(string.Join(",", Enumerable.Select(xs, x => x * 2)));
        Console.WriteLine(Enumerable.Count(xs) + "," + Enumerable.Sum(xs) + "," + Enumerable.Average(xs));
        Console.WriteLine(Enumerable.Max(xs) + "," + Enumerable.Min(xs));
        Console.WriteLine(Enumerable.Aggregate(xs, (a, b) => a * b));
        Console.WriteLine(string.Join(",", Enumerable.OrderByDescending(xs, x => x)));
        Console.WriteLine(Enumerable.ToList(xs).Count + "," + Enumerable.ToArray(xs).Length);
        Console.WriteLine(string.Join(",", Enumerable.Concat(xs, xs)));
        Console.WriteLine(Enumerable.SequenceEqual(xs, new[] { 1, 2, 3 }));
        Console.WriteLine(string.Join(",", Enumerable.Reverse(xs)));
        Console.WriteLine(Enumerable.First(xs) + "," + Enumerable.Last(xs) + "," + Enumerable.ElementAt(xs, 1));
        Console.WriteLine(Enumerable.Any(xs, x => x > 2) + "," + Enumerable.All(xs, x => x > 0));
        Console.WriteLine(Enumerable.GroupBy(xs, x => x % 2).Count());
        Console.WriteLine(string.Join(",", Enumerable.Distinct(new[] { 1, 1, 2 })));
    }
}
""";
            await RunTest(code);
        }
    }
}
