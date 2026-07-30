using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests.Linq
{
    /// <summary>
    /// The LINQ operators <c>EnumerableExtras</c> adds on top of the runtime-backed
    /// <c>Enumerable</c> binding — the ones <c>System.Linq</c> gained after the <c>linq.js</c> API the
    /// binding maps onto: <c>Append</c>/<c>Prepend</c>, <c>ToHashSet</c>, <c>DistinctBy</c>/
    /// <c>UnionBy</c>/<c>IntersectBy</c>/<c>ExceptBy</c>, <c>SkipLast</c>/<c>TakeLast</c>,
    /// <c>Order</c>/<c>OrderDescending</c>, <c>Index</c>, <c>CountBy</c>/<c>AggregateBy</c>,
    /// <c>TryGetNonEnumeratedCount</c>, the tuple-returning <c>Zip</c> overloads, and indexing by
    /// <see cref="System.Index"/> / <see cref="System.Range"/>.
    ///
    /// Each is covered in every declared overload, over an array/List receiver and over a query receiver
    /// (which matters: an instance method on <c>EnumerableInstance</c> beats an extension method, so
    /// <c>query.Take(1..3)</c> and <c>query.ElementAt(^1)</c> have to keep reaching the extension while
    /// <c>query.Take(2)</c> and <c>query.ElementAt(1)</c> keep reaching the instance member), and across
    /// the element types whose equality or ordering the operator depends on.
    ///
    /// <para><b>One documented difference</b>, spelled out on <c>EnumerableExtras</c>:
    /// <c>TryGetNonEnumeratedCount</c> answers true only for a real collection, where .NET also answers
    /// true for some lazy operators whose count it can work out cheaply. False is a correct answer to
    /// "can I have the count without enumerating?", so the tests below only assert the collection cases
    /// and the cases where .NET answers false too.</para>
    /// </summary>
    [TestClass]
    public class LinqModernOperatorTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task AppendPrependAndToHashSet()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new List<int> { 1, 2, 3 };
        Console.WriteLine(string.Join(",", xs.Append(4)));
        Console.WriteLine(string.Join(",", xs.Prepend(0)));
        Console.WriteLine(string.Join(",", xs.Append(4).Prepend(0)));
        Console.WriteLine("[" + string.Join(",", new int[0].Append(1)) + "]");
        Console.WriteLine("[" + string.Join(",", new int[0].Prepend(1)) + "]");
        Console.WriteLine(string.Join(",", xs.Where(x => x > 1).Append(9)));
        Console.WriteLine(string.Join(",", new[] { "a" }.Append(null).Select(s => s ?? "<null>")));
        // Append does not mutate its source.
        Console.WriteLine(string.Join(",", xs));

        Console.WriteLine(xs.ToHashSet().Count);
        Console.WriteLine(string.Join(",", new[] { 1, 1, 2 }.ToHashSet()));
        Console.WriteLine(new[] { "A", "a" }.ToHashSet().Count);
        Console.WriteLine(new[] { "A", "a" }.ToHashSet(StringComparer.OrdinalIgnoreCase).Count);
        Console.WriteLine(new int[0].ToHashSet().Count);
        var hs = xs.ToHashSet();
        Console.WriteLine(hs.Contains(2) + "," + hs.Contains(9));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task DistinctByAndUnionBy()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public record Person(string Name, int Age);

public class Program
{
    public static void Main()
    {
        var ps = new[] { new Person("amy", 30), new Person("bob", 30), new Person("cal", 25) };
        // DistinctBy keeps the FIRST element of each key.
        Console.WriteLine(string.Join(",", ps.DistinctBy(p => p.Age).Select(p => p.Name)));
        Console.WriteLine(string.Join(",", ps.DistinctBy(p => p.Name).Select(p => p.Name)));
        Console.WriteLine(string.Join(",", new[] { "A", "a", "b" }.DistinctBy(s => s, StringComparer.OrdinalIgnoreCase)));
        Console.WriteLine(string.Join(",", new[] { 1, 2, 3, 4 }.DistinctBy(x => x % 2)));
        Console.WriteLine(new int[0].DistinctBy(x => x).Count());

        var more = new[] { new Person("dan", 30), new Person("eve", 40) };
        Console.WriteLine(string.Join(",", ps.UnionBy(more, p => p.Age).Select(p => p.Name)));
        Console.WriteLine(string.Join(",", ps.UnionBy(more, p => p.Name).Select(p => p.Name)));
        Console.WriteLine(string.Join(",", new[] { "A" }.UnionBy(new[] { "a", "b" }, s => s, StringComparer.OrdinalIgnoreCase)));
        Console.WriteLine(string.Join(",", new int[0].UnionBy(new[] { 1 }, x => x)));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task IntersectByAndExceptBy_TakeASequenceOfKeys()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public record Person(string Name, int Age);

public class Program
{
    public static void Main()
    {
        var ps = new[] { new Person("amy", 30), new Person("bob", 30), new Person("cal", 25) };
        // The second sequence is one of KEYS, not of elements — and the result is distinct BY key, so
        // only the first of the two 30-year-olds survives.
        Console.WriteLine(string.Join(",", ps.IntersectBy(new[] { 30 }, p => p.Age).Select(p => p.Name)));
        Console.WriteLine(string.Join(",", ps.IntersectBy(new[] { 25, 30 }, p => p.Age).Select(p => p.Name)));
        Console.WriteLine("[" + string.Join(",", ps.IntersectBy(new int[0], p => p.Age).Select(p => p.Name)) + "]");
        Console.WriteLine(string.Join(",", ps.ExceptBy(new[] { 30 }, p => p.Age).Select(p => p.Name)));
        Console.WriteLine(string.Join(",", ps.ExceptBy(new int[0], p => p.Age).Select(p => p.Name)));
        Console.WriteLine(string.Join(",", ps.ExceptBy(new[] { "amy" }, p => p.Name).Select(p => p.Name)));
        Console.WriteLine(string.Join(",", new[] { "A", "a" }.IntersectBy(new[] { "a" }, s => s, StringComparer.OrdinalIgnoreCase)));
        Console.WriteLine(string.Join(",", new[] { "A", "a", "b" }.ExceptBy(new[] { "a" }, s => s, StringComparer.OrdinalIgnoreCase)));
        Console.WriteLine(string.Join(",", new[] { 1, 1, 2 }.IntersectBy(new[] { 1 }, x => x)));
        Console.WriteLine(string.Join(",", new[] { 1, 1, 2 }.ExceptBy(new[] { 9 }, x => x)));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task SkipLastAndTakeLast()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { 1, 2, 3, 4, 5 };
        Console.WriteLine("[" + string.Join(",", xs.SkipLast(2)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.SkipLast(0)) + "]");
        // A negative count is clamped rather than rejected.
        Console.WriteLine("[" + string.Join(",", xs.SkipLast(-1)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.SkipLast(99)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.TakeLast(2)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.TakeLast(0)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.TakeLast(-1)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.TakeLast(99)) + "]");
        Console.WriteLine("[" + string.Join(",", new int[0].SkipLast(1)) + "]|[" + string.Join(",", new int[0].TakeLast(1)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.Where(x => x > 1).SkipLast(1)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.Where(x => x > 1).TakeLast(2)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.TakeLast(3).SkipLast(1)) + "]");
        Console.WriteLine("[" + string.Join("", "hello".SkipLast(2)) + "]|[" + string.Join("", "hello".TakeLast(2)) + "]");
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task OrderAndOrderDescending()
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
        Console.WriteLine(string.Join(",", xs.Order()));
        Console.WriteLine(string.Join(",", xs.OrderDescending()));
        var reverse = Comparer<int>.Create((a, b) => b - a);
        Console.WriteLine(string.Join(",", xs.Order(reverse)));
        Console.WriteLine(string.Join(",", xs.OrderDescending(reverse)));
        Console.WriteLine(string.Join(",", new[] { "b", "a", "c" }.Order()));
        Console.WriteLine(string.Join(",", new[] { "b", "a", "c" }.OrderDescending()));
        Console.WriteLine(string.Join(",", new[] { 2.5, 1.5 }.Order()));
        Console.WriteLine(string.Join(",", new[] { 'c', 'a' }.Order()));
        Console.WriteLine(string.Join(",", new[] { 3L, 1L }.Order()));
        Console.WriteLine(string.Join(",", new[] { 3m, 1m }.OrderDescending()));
        // Order returns an IOrderedEnumerable, so ThenBy chains onto it.
        var words = new[] { "bb", "a", "cc" };
        Console.WriteLine(string.Join(",", words.Order().ThenByDescending(w => w.Length)));
        Console.WriteLine(string.Join(",", words.OrderDescending().ThenBy(w => w)));
        Console.WriteLine("[" + string.Join(",", new int[0].Order()) + "]");
        Console.WriteLine(xs.Order().First() + "," + xs.Order().Last() + "," + xs.Order().Count());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task IndexCountByAndAggregateBy()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { "a", "b", "c" };
        foreach (var (i, s) in xs.Index()) Console.WriteLine(i + ":" + s);
        Console.WriteLine(string.Join(",", xs.Index().Select(t => t.Index + t.Item)));
        Console.WriteLine(xs.Index().Count() + "," + new string[0].Index().Count());
        Console.WriteLine(xs.Index().Last().Index);
        // The index counts the elements the operator actually yields, not their source positions.
        Console.WriteLine(string.Join(",", xs.Where(s => s != "b").Index().Select(t => t.Index + t.Item)));

        var words = new[] { "apple", "avocado", "banana", "cherry", "apricot" };
        // CountBy/AggregateBy yield their keys in first-seen order.
        Console.WriteLine(string.Join(";", words.CountBy(w => w[0]).Select(kv => kv.Key + "=" + kv.Value)));
        Console.WriteLine(string.Join(";", words.CountBy(w => w.Length).Select(kv => kv.Key + "=" + kv.Value)));
        Console.WriteLine(string.Join(";", new[] { "A", "a", "b" }.CountBy(s => s, StringComparer.OrdinalIgnoreCase).Select(kv => kv.Key + "=" + kv.Value)));
        Console.WriteLine(new string[0].CountBy(s => s).Count());

        Console.WriteLine(string.Join(";", words.AggregateBy(w => w[0], 0, (a, w) => a + w.Length).Select(kv => kv.Key + "=" + kv.Value)));
        Console.WriteLine(string.Join(";", words.AggregateBy(w => w[0], "", (a, w) => a + w[1]).Select(kv => kv.Key + "=" + kv.Value)));
        // The seed-selector overload computes a fresh seed per key.
        Console.WriteLine(string.Join(";", words.AggregateBy(w => w[0], k => k.ToString(), (a, w) => a + "/" + w.Length).Select(kv => kv.Key + "=" + kv.Value)));
        Console.WriteLine(string.Join(";", new[] { "A", "a" }.AggregateBy(s => s, 0, (a, s) => a + 1, StringComparer.OrdinalIgnoreCase).Select(kv => kv.Key + "=" + kv.Value)));
        Console.WriteLine(new string[0].AggregateBy(s => s, 0, (a, s) => a + 1).Count());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task TryGetNonEnumeratedCountAndTupleZip()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        int c;
        Console.WriteLine(new[] { 1, 2, 3 }.TryGetNonEnumeratedCount(out c) + ":" + c);
        Console.WriteLine(new List<int> { 1, 2 }.TryGetNonEnumeratedCount(out c) + ":" + c);
        Console.WriteLine(new HashSet<int> { 1 }.TryGetNonEnumeratedCount(out c) + ":" + c);
        Console.WriteLine(new Dictionary<int, int> { { 1, 1 } }.TryGetNonEnumeratedCount(out c) + ":" + c);
        Console.WriteLine(new int[0].TryGetNonEnumeratedCount(out c) + ":" + c);
        IEnumerable<int> asInterface = new List<int> { 1, 2, 3 };
        Console.WriteLine(asInterface.TryGetNonEnumeratedCount(out c) + ":" + c);
        // A filtered query has no cheap count in .NET either.
        Console.WriteLine(new[] { 1, 2, 3 }.Where(x => x > 1).TryGetNonEnumeratedCount(out c) + ":" + c);
        Console.WriteLine(new[] { 1, 2, 3 }.Distinct().TryGetNonEnumeratedCount(out c) + ":" + c);

        var a = new[] { 1, 2, 3 };
        var b = new[] { "x", "y" };
        // The selectorless Zip pairs into tuples and stops at the shorter sequence.
        Console.WriteLine(string.Join(";", a.Zip(b).Select(t => t.First + t.Second)));
        Console.WriteLine(string.Join(";", a.Zip(b).Select(t => t.Item1 + "/" + t.Item2)));
        Console.WriteLine(a.Zip(new int[0]).Count());
        foreach (var (n, s) in a.Zip(b)) Console.WriteLine(n + "-" + s);
        Console.WriteLine(string.Join(";", a.Zip(b, new[] { true, false }).Select(t => t.First + t.Second + t.Third)));
        Console.WriteLine(a.Zip(b, new bool[0]).Count());
        // The selector overload still resolves.
        Console.WriteLine(string.Join(";", a.Zip(b, (x, y) => x + y)));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task IndexingByIndexAndRange()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { 10, 20, 30, 40, 50 };
        Console.WriteLine(xs.ElementAt(1) + "," + xs.ElementAt(^1) + "," + xs.ElementAt(^5));
        Console.WriteLine(xs.ElementAtOrDefault(^1) + "," + xs.ElementAtOrDefault(^9) + "," + xs.ElementAtOrDefault(^0));
        Console.WriteLine(xs.ElementAt(new Index(2)) + "," + xs.ElementAt(Index.FromEnd(2)));
        // ^0 is one past the last element, so it is never in range.
        try { xs.ElementAt(^0); } catch (ArgumentOutOfRangeException) { Console.WriteLine("^0 throws"); }
        try { xs.ElementAt(^9); } catch (ArgumentOutOfRangeException) { Console.WriteLine("^9 throws"); }
        Console.WriteLine(xs.Where(x => x > 15).ElementAt(^1));

        Console.WriteLine("[" + string.Join(",", xs.Take(1..3)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.Take(..2)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.Take(3..)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.Take(..)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.Take(1..^1)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.Take(^2..)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.Take(^3..^1)) + "]");
        // An inverted, empty or over-long range yields nothing rather than throwing.
        Console.WriteLine("[" + string.Join(",", xs.Take(3..1)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.Take(2..2)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.Take(0..99)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.Take(^9..)) + "]");
        Console.WriteLine("[" + string.Join(",", new int[0].Take(0..2)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.Where(x => x > 15).Take(1..3)) + "]");
        Console.WriteLine("[" + string.Join(",", xs.Take(1..^1).Take(1..)) + "]");
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task RangeExpressionsOutsideAnIndexer()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    static int Describe(Range r) { var (offset, length) = r.GetOffsetAndLength(10); return offset * 100 + length; }
    static Range Field = 2..5;

    public static void Main()
    {
        // A range expression is a System.Range value in any position, not just inside an indexer.
        Range r = 1..^1;
        Console.WriteLine(r.Start.Value + "," + r.End.IsFromEnd + "," + r.End.Value);
        Console.WriteLine(Describe(1..3) + "," + Describe(..2) + "," + Describe(3..) + "," + Describe(..) + "," + Describe(^3..^1));
        Console.WriteLine(Field.Start.Value + "," + Field.End.Value);
        var ranges = new[] { 0..1, 1..^1, ^2.. };
        Console.WriteLine(string.Join(";", ranges.Select(x => x.ToString())));
        Console.WriteLine((1..3).Equals(1..3) + "," + (1..3).Equals(1..4));

        // Array and string slicing (which lowers a range inline) still works.
        var xs = new[] { 1, 2, 3, 4, 5 };
        Console.WriteLine(string.Join(",", xs[1..3]));
        Console.WriteLine(string.Join(",", xs[..2]) + "|" + string.Join(",", xs[3..]));
        Console.WriteLine("hello"[1..3] + "," + "hello"[^2..]);
        Console.WriteLine(xs[^1] + "," + xs[^2]);
        Console.WriteLine(string.Join(",", xs.Take(r)));
        Range fromVariable = ^3..;
        Console.WriteLine(string.Join(",", xs.Take(fromVariable)));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task EveryNewOperatorOverAQueryReceiver()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var q = new[] { 1, 2, 3, 4, 5 }.Where(x => x > 1);
        Console.WriteLine(string.Join(",", q.Append(9)));
        Console.WriteLine(string.Join(",", q.Prepend(0)));
        Console.WriteLine(q.ToHashSet().Count);
        Console.WriteLine(string.Join(",", q.DistinctBy(x => x % 2)));
        Console.WriteLine(string.Join(",", q.SkipLast(1)) + "|" + string.Join(",", q.TakeLast(2)));
        Console.WriteLine(string.Join(",", q.Order()) + "|" + string.Join(",", q.OrderDescending()));
        Console.WriteLine(string.Join(",", q.Index().Select(t => t.Index + ":" + t.Item)));
        Console.WriteLine(string.Join(";", q.CountBy(x => x % 2).Select(kv => kv.Key + "=" + kv.Value)));
        Console.WriteLine(string.Join(";", q.AggregateBy(x => x % 2, 0, (a, x) => a + x).Select(kv => kv.Key + "=" + kv.Value)));
        Console.WriteLine(string.Join(";", q.Zip(new[] { "a", "b" }).Select(t => t.First + t.Second)));
        Console.WriteLine(string.Join(",", q.UnionBy(new[] { 9 }, x => x % 2)));
        Console.WriteLine(string.Join(",", q.IntersectBy(new[] { 0 }, x => x % 2)));
        Console.WriteLine(string.Join(",", q.ExceptBy(new[] { 0 }, x => x % 2)));
        // EnumerableInstance declares instance ElementAt(int) and Take(int), which beat an extension
        // method — so these four calls have to split cleanly between the instance and extension forms.
        Console.WriteLine(q.ElementAt(^1) + "," + q.ElementAtOrDefault(^9));
        Console.WriteLine(string.Join(",", q.Take(1..3)));
        Console.WriteLine(string.Join(",", q.Take(2)));
        Console.WriteLine(q.ElementAt(1));
        int c;
        Console.WriteLine(q.TryGetNonEnumeratedCount(out c) + ":" + c);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task NewOperatorsAcrossElementTypes()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public record Item(string Sku, int Qty);
public readonly record struct RS(int A, string B);
public struct Pt : IComparable<Pt> { public int X; public int CompareTo(Pt o) => X.CompareTo(o.X); public override string ToString() => "P" + X; }
public enum Colour { Red = 1, Green = 2, Blue = 3 }

public class Program
{
    public static void Main()
    {
        var items = new[] { new Item("a", 2), new Item("b", 1), new Item("a", 3) };
        Console.WriteLine(string.Join(",", items.DistinctBy(i => i.Sku).Select(i => i.Sku + i.Qty)));
        Console.WriteLine(string.Join(";", items.CountBy(i => i.Sku).Select(kv => kv.Key + "=" + kv.Value)));
        Console.WriteLine(string.Join(";", items.AggregateBy(i => i.Sku, 0, (a, i) => a + i.Qty).Select(kv => kv.Key + "=" + kv.Value)));
        Console.WriteLine(string.Join(",", items.ExceptBy(new[] { "a" }, i => i.Sku).Select(i => i.Sku)));
        Console.WriteLine(string.Join(",", items.Append(new Item("z", 0)).Select(i => i.Sku)));
        Console.WriteLine(items.ToHashSet().Count);

        var rs = new[] { new RS(2, "x"), new RS(1, "y"), new RS(2, "x") };
        Console.WriteLine(rs.ToHashSet().Count + "," + rs.DistinctBy(r => r.A).Count());
        // A record does not implement IComparable, so it is ordered by a key rather than by Order().
        Console.WriteLine(string.Join(",", rs.OrderBy(r => r.A).ThenBy(r => r.B).Select(r => r.A + r.B)));

        var pts = new[] { new Pt { X = 3 }, new Pt { X = 1 } };
        Console.WriteLine(string.Join(",", pts.Order()) + "|" + string.Join(",", pts.OrderDescending()));

        var cs = new[] { Colour.Blue, Colour.Red, Colour.Green, Colour.Red };
        Console.WriteLine(string.Join(",", cs.Order()) + "|" + string.Join(",", cs.OrderDescending()));
        Console.WriteLine(string.Join(";", cs.CountBy(c => c).Select(kv => kv.Key + "=" + kv.Value)));
        Console.WriteLine(cs.ToHashSet().Count + "," + string.Join(",", cs.DistinctBy(c => (int)c % 2)));

        var ns = new int?[] { 3, null, 1 };
        Console.WriteLine(string.Join(",", ns.Order().Select(x => x == null ? "null" : x.ToString())));
        Console.WriteLine(ns.ToHashSet().Count + "," + ns.DistinctBy(x => x).Count());

        var ts = new[] { (1, "a"), (2, "b"), (1, "a") };
        Console.WriteLine(ts.ToHashSet().Count + "," + string.Join(",", ts.DistinctBy(t => t.Item1).Select(t => t.Item2)));
        Console.WriteLine(string.Join(",", ts.Order().Select(t => t.Item1 + t.Item2)));

        var anon = new[] { new { A = 1 }, new { A = 1 }, new { A = 2 } };
        Console.WriteLine(anon.ToHashSet().Count + "," + anon.DistinctBy(x => x.A).Count());
        Console.WriteLine(string.Join(";", anon.CountBy(x => x.A).Select(kv => kv.Key + "=" + kv.Value)));

        Console.WriteLine(string.Join("", "hello".Order()) + "," + string.Join("", "hello".DistinctBy(ch => ch)));
        Console.WriteLine(string.Join(",", new[] { 2.5, 1.5 }.Order()) + "," + string.Join(",", new[] { 3m, 1m }.Order()));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task ArgumentValidationOfTheNewOperators()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        IEnumerable<int> nothing = null;
        // Every one of these validates its arguments EAGERLY: the exception surfaces at the call, before
        // anything is enumerated.
        try { nothing.Append(1); } catch (ArgumentNullException e) { Console.WriteLine("append " + e.ParamName); }
        try { nothing.Prepend(1); } catch (ArgumentNullException e) { Console.WriteLine("prepend " + e.ParamName); }
        try { nothing.ToHashSet(); } catch (ArgumentNullException e) { Console.WriteLine("tohashset " + e.ParamName); }
        try { nothing.DistinctBy(x => x); } catch (ArgumentNullException e) { Console.WriteLine("distinctby " + e.ParamName); }
        try { new[] { 1 }.DistinctBy((Func<int, int>)null); } catch (ArgumentNullException e) { Console.WriteLine("distinctby-key " + e.ParamName); }
        try { nothing.UnionBy(new[] { 1 }, x => x); } catch (ArgumentNullException e) { Console.WriteLine("unionby " + e.ParamName); }
        try { new[] { 1 }.UnionBy(null, x => x); } catch (ArgumentNullException e) { Console.WriteLine("unionby-2nd " + e.ParamName); }
        try { new[] { 1 }.IntersectBy(null, x => x); } catch (ArgumentNullException e) { Console.WriteLine("intersectby-2nd " + e.ParamName); }
        try { new[] { 1 }.ExceptBy(null, x => x); } catch (ArgumentNullException e) { Console.WriteLine("exceptby-2nd " + e.ParamName); }
        try { nothing.SkipLast(1); } catch (ArgumentNullException e) { Console.WriteLine("skiplast " + e.ParamName); }
        try { nothing.TakeLast(1); } catch (ArgumentNullException e) { Console.WriteLine("takelast " + e.ParamName); }
        try { nothing.Order(); } catch (ArgumentNullException e) { Console.WriteLine("order " + e.ParamName); }
        try { nothing.OrderDescending(); } catch (ArgumentNullException e) { Console.WriteLine("orderdesc " + e.ParamName); }
        try { nothing.Index(); } catch (ArgumentNullException e) { Console.WriteLine("index " + e.ParamName); }
        try { nothing.CountBy(x => x); } catch (ArgumentNullException e) { Console.WriteLine("countby " + e.ParamName); }
        try { new[] { 1 }.AggregateBy(x => x, 0, null); } catch (ArgumentNullException e) { Console.WriteLine("aggregateby-func " + e.ParamName); }
        try { new[] { 1 }.Zip((int[])null); } catch (ArgumentNullException e) { Console.WriteLine("zip " + e.ParamName); }
        try { nothing.Take(0..1); } catch (ArgumentNullException e) { Console.WriteLine("take-range " + e.ParamName); }
        try { nothing.ElementAt(^1); } catch (ArgumentNullException e) { Console.WriteLine("elementat " + e.ParamName); }
        int c;
        try { nothing.TryGetNonEnumeratedCount(out c); } catch (ArgumentNullException e) { Console.WriteLine("trygetcount " + e.ParamName); }
    }
}
""";
            await RunTest(code);
        }
    }
}
