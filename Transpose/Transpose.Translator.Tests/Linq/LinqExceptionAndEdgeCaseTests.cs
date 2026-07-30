using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests.Linq
{
    /// <summary>
    /// What LINQ does at the edges: the exceptions it throws (which <b>type</b>, and with which
    /// <b>message</b> — user code catches on the type, and prints the message), the empty and
    /// single-element sequence for every operator, argument validation, null keys and null elements, and
    /// the observable evaluation order of the delegates an operator is given.
    ///
    /// The exception tests matter more than they look: the transpiled <c>catch (InvalidOperationException)</c>
    /// checks the thrown value against the BCL type, so an operator that raises a raw JavaScript
    /// <c>Error</c> escapes every C# catch clause and tears the program down instead of being handled.
    /// </summary>
    [TestClass]
    public class LinqExceptionAndEdgeCaseTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task ElementOperators_ThrowTheRightExceptionWithTheRightMessage()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var empty = new int[0];
        var xs = new[] { 1, 2, 3 };
        // .NET distinguishes "no elements" from "no matching element", and "more than one element" from
        // "more than one matching element".
        try { empty.First(); } catch (InvalidOperationException e) { Console.WriteLine("A " + e.Message); }
        try { xs.First(x => x > 9); } catch (InvalidOperationException e) { Console.WriteLine("B " + e.Message); }
        try { empty.Last(); } catch (InvalidOperationException e) { Console.WriteLine("C " + e.Message); }
        try { xs.Last(x => x > 9); } catch (InvalidOperationException e) { Console.WriteLine("D " + e.Message); }
        try { xs.Single(); } catch (InvalidOperationException e) { Console.WriteLine("E " + e.Message); }
        try { empty.Single(); } catch (InvalidOperationException e) { Console.WriteLine("F " + e.Message); }
        try { xs.Single(x => x > 9); } catch (InvalidOperationException e) { Console.WriteLine("G " + e.Message); }
        try { xs.Single(x => x > 1); } catch (InvalidOperationException e) { Console.WriteLine("H " + e.Message); }
        try { xs.SingleOrDefault(x => x > 1); } catch (InvalidOperationException e) { Console.WriteLine("I " + e.Message); }
        try { xs.ElementAt(9); } catch (ArgumentOutOfRangeException e) { Console.WriteLine("J " + e.ParamName); }
        try { xs.ElementAt(-1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine("K " + e.ParamName); }
        try { empty.Max(); } catch (InvalidOperationException e) { Console.WriteLine("L " + e.Message); }
        try { empty.Min(); } catch (InvalidOperationException e) { Console.WriteLine("M " + e.Message); }
        try { empty.Average(); } catch (InvalidOperationException e) { Console.WriteLine("N " + e.Message); }
        try { empty.Aggregate((a, b) => a + b); } catch (InvalidOperationException e) { Console.WriteLine("O " + e.Message); }
        // A base-class catch has to see them too.
        try { empty.First(); } catch (Exception e) { Console.WriteLine("P " + e.GetType().Name); }
        try { xs.ElementAt(9); } catch (ArgumentException e) { Console.WriteLine("Q " + e.GetType().Name); }
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task ArgumentValidation()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        // Range/Repeat validate EAGERLY — before anything is enumerated.
        try { Enumerable.Range(0, -1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine("range " + e.ParamName); }
        try { Enumerable.Repeat("a", -1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine("repeat " + e.ParamName); }
        try { new[] { 1 }.Chunk(0); } catch (ArgumentOutOfRangeException e) { Console.WriteLine("chunk " + e.ParamName); }
        try { new[] { 1 }.Chunk(-1); } catch (ArgumentOutOfRangeException e) { Console.WriteLine("chunk- " + e.ParamName); }
        // A negative count is simply clamped for Take/Skip, and ElementAtOrDefault never throws.
        Console.WriteLine("[" + string.Join(",", new[] { 1, 2 }.Take(-1)) + "]");
        Console.WriteLine(string.Join(",", new[] { 1, 2 }.Skip(-1)));
        Console.WriteLine(new[] { 1 }.ElementAtOrDefault(-1) + "," + new[] { 1 }.ElementAtOrDefault(9));
        // ToDictionary rejects a duplicate key, naming it.
        try { new[] { "a", "b", "a" }.ToDictionary(s => s); } catch (ArgumentException e) { Console.WriteLine("dupe " + e.Message); }
        try { new int[0].MinBy(x => x); } catch (InvalidOperationException e) { Console.WriteLine("minby " + e.Message); }
        try { new int[0].MaxBy(x => x); } catch (InvalidOperationException e) { Console.WriteLine("maxby " + e.Message); }
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task EmptySequence_ThroughEveryOperator()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var e = Enumerable.Empty<int>();
        Console.WriteLine(e.Count() + "," + e.Any() + "," + e.All(x => false) + "," + e.Sum());
        Console.WriteLine("[" + string.Join(",", e.Where(x => true)) + "]|[" + string.Join(",", e.Select(x => x)) + "]");
        Console.WriteLine("[" + string.Join(",", e.OrderBy(x => x)) + "]|[" + string.Join(",", e.Reverse()) + "]");
        Console.WriteLine(e.GroupBy(x => x).Count() + "," + e.ToLookup(x => x).Count + "," + e.ToDictionary(x => x).Count);
        Console.WriteLine(e.ToArray().Length + "," + e.ToList().Count + "," + e.Distinct().Count());
        Console.WriteLine(e.Concat(e).Count() + "," + e.Union(e).Count() + "," + e.Intersect(e).Count() + "," + e.Except(e).Count());
        Console.WriteLine(e.Take(3).Count() + "," + e.Skip(3).Count() + "," + e.TakeWhile(x => true).Count() + "," + e.SkipWhile(x => true).Count());
        Console.WriteLine(e.FirstOrDefault() + "," + e.LastOrDefault() + "," + e.SingleOrDefault() + "," + e.ElementAtOrDefault(0));
        Console.WriteLine(e.SequenceEqual(e) + "," + e.Contains(1) + "," + e.LongCount());
        Console.WriteLine(e.Zip(e, (a, b) => a).Count() + "," + e.SelectMany(x => e).Count() + "," + e.Aggregate(5, (a, b) => a + b));
        Console.WriteLine(string.Join(",", e.DefaultIfEmpty()) + "," + e.Chunk(2).Count());
        Console.WriteLine(e.Join(e, a => a, b => b, (a, b) => a).Count() + "," + e.GroupJoin(e, a => a, b => b, (a, b) => a).Count());
        Console.WriteLine(e.Cast<int>().Count() + "," + e.OfType<int>().Count() + "," + e.AsEnumerable().Count());
        Console.WriteLine(e.Sum(x => x) + "," + e.Count(x => true) + "," + e.LongCount(x => true));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task SingleElementSequence_ThroughEveryOperator()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var s = new[] { 42 };
        Console.WriteLine(s.Single() + "," + s.First() + "," + s.Last() + "," + s.ElementAt(0));
        Console.WriteLine(s.Sum() + "," + s.Average() + "," + s.Min() + "," + s.Max() + "," + s.Count());
        Console.WriteLine(string.Join(",", s.Reverse()) + "," + string.Join(",", s.OrderBy(x => x)));
        Console.WriteLine(s.Aggregate((a, b) => a + b) + "," + s.Aggregate(1, (a, b) => a + b));
        Console.WriteLine(string.Join(",", s.DefaultIfEmpty(9)) + "," + s.Chunk(5).Single().Length);
        Console.WriteLine(s.GroupBy(x => x).Single().Key + "," + s.ToLookup(x => x).Count);
        Console.WriteLine(s.Distinct().Single() + "," + s.Union(s).Count() + "," + s.Except(s).Count());
        Console.WriteLine(s.MinBy(x => x) + "," + s.MaxBy(x => x));
        Console.WriteLine(s.Zip(s, (a, b) => a + b).Single());
        Console.WriteLine(s.SelectMany(x => new[] { x, x }).Count());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task NullElementsAndNullKeys()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var withNulls = new[] { "a", null, "b", null };
        Console.WriteLine(withNulls.Count(s => s == null));
        Console.WriteLine(string.Join(",", withNulls.Where(s => s != null)));
        Console.WriteLine(withNulls.Distinct().Count() + "," + withNulls.Contains(null));
        Console.WriteLine(withNulls.GroupBy(s => s == null).Count());
        // A null KEY is a valid grouping key for GroupBy and ToLookup (but not for ToDictionary).
        Console.WriteLine(withNulls.GroupBy(s => s).Count() + "," + withNulls.ToLookup(s => s).Count);
        Console.WriteLine(withNulls.ToLookup(s => s)[null].Count());
        Console.WriteLine(string.Join(",", withNulls.OrderBy(s => s).Select(s => s ?? "<null>")));
        Console.WriteLine(string.Join(",", withNulls.Except(new string[] { null })));
        Console.WriteLine(string.Join(",", withNulls.Union(new string[] { null, "c" }).Select(s => s ?? "<null>")));
        Console.WriteLine(withNulls.FirstOrDefault(s => s == null) == null);
        Console.WriteLine(withNulls.Select(s => s?.Length).Sum());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task DelegateInvocationOrderIsObservable()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var log = new List<string>();
        var xs = new[] { 1, 2, 3 };

        // Where and Select are interleaved per element, not run as two passes.
        var r = xs.Where(x => { log.Add("w" + x); return x > 1; })
                  .Select(x => { log.Add("s" + x); return x * 2; })
                  .ToList();
        Console.WriteLine(string.Join(",", r));
        Console.WriteLine(string.Join(",", log));

        // First stops at the first match; Any at the first true; All at the first false.
        log.Clear();
        Console.WriteLine(xs.Where(x => { log.Add("W" + x); return x > 1; }).First() + " | " + string.Join(",", log));
        log.Clear();
        Console.WriteLine(xs.Any(x => { log.Add("A" + x); return x == 2; }) + " | " + string.Join(",", log));
        log.Clear();
        Console.WriteLine(xs.All(x => { log.Add("L" + x); return x == 1; }) + " | " + string.Join(",", log));
        log.Clear();
        Console.WriteLine(string.Join(",", xs.TakeWhile(x => { log.Add("T" + x); return x < 3; })) + " | " + string.Join(",", log));
        log.Clear();
        Console.WriteLine(xs.Contains(2) + " | " + log.Count);

        // A key selector runs once per element per operator, and OrderBy runs it before comparing.
        log.Clear();
        var ordered = xs.OrderByDescending(x => { log.Add("k" + x); return x; }).ToList();
        Console.WriteLine(string.Join(",", ordered) + " | " + log.Count);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task ChainedOperatorsOverAQueryReceiver()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { 1, 2, 3, 4, 5 };
        var q = xs.Where(x => x > 1);
        Console.WriteLine(q.Select(x => x * 2).Where(x => x > 4).Sum());
        Console.WriteLine(q.AsEnumerable().Count() + "," + q.OrderByDescending(x => x).First());
        Console.WriteLine(q.Skip(1).Take(2).Sum() + "," + q.Concat(xs).Count());
        Console.WriteLine(q.Reverse().First() + "," + q.DefaultIfEmpty().Count());
        Console.WriteLine(q.SelectMany(x => new[] { x, x }).Count() + "," + q.Distinct().Count());
        Console.WriteLine(q.GroupBy(x => x % 2).Count() + "," + q.ToLookup(x => x % 2).Count);
        Console.WriteLine(q.Zip(xs, (a, b) => a + b).Sum() + "," + q.Aggregate(0, (a, b) => a + b));
        Console.WriteLine(q.Contains(3) + "," + q.SequenceEqual(new[] { 2, 3, 4, 5 }));
        Console.WriteLine(q.ElementAt(0) + "," + q.LongCount() + "," + q.Last());
        Console.WriteLine(q.Chunk(2).Count() + "," + q.MinBy(x => -x));
        Console.WriteLine(q.ToArray().Length + "," + q.ToList().Count + "," + q.ToDictionary(x => x).Count);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task ArrayReverse_ResolvesToEnumerableReverse()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        // An instance method beats an extension method, so an instance `Reverse()` on Array would hide
        // Enumerable.Reverse<T>() and make this fail to compile.
        var a = new[] { 1, 2, 3 };
        Console.WriteLine(string.Join(",", a.Reverse()));
        // Enumerable.Reverse does NOT mutate its source.
        Console.WriteLine(string.Join(",", a));
        var walked = "";
        foreach (var x in a.Reverse()) walked += x;
        Console.WriteLine(walked);
        Console.WriteLine(a.Reverse().First() + "," + a.Reverse().Sum());
        Console.WriteLine(string.Join(",", a.Reverse().Select(x => x * 2)));

        // Array.Reverse(array) is the in-place BCL form and still works.
        Array.Reverse(a);
        Console.WriteLine(string.Join(",", a));

        // List<T>.Reverse() IS a real instance method in .NET and must stay in-place.
        var l = new List<int> { 1, 2, 3 };
        l.Reverse();
        Console.WriteLine(string.Join(",", l));
        Console.WriteLine(string.Join(",", Enumerable.Reverse(l)));
        Console.WriteLine(string.Join("", "abc".Reverse()));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task LinqInsideAsyncMethodsAndClosures()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class Program
{
    static async Task<int> SumAsync(IEnumerable<int> src) { await Task.Yield(); return src.Sum(); }

    public static async Task Main()
    {
        var xs = new[] { 1, 2, 3 };
        Console.WriteLine(await SumAsync(xs));
        var tasks = xs.Select(async x => { await Task.Yield(); return x * 2; });
        var results = await Task.WhenAll(tasks);
        Console.WriteLine(string.Join(",", results));
        Console.WriteLine(string.Join(",", results.OrderByDescending(r => r)));
        var collected = new List<int>();
        foreach (var x in xs.Where(v => v > 1)) { await Task.Yield(); collected.Add(x); }
        Console.WriteLine(string.Join(",", collected));

        // A captured local is read when the query RUNS, not when it is built.
        int threshold = 1;
        var q = xs.Where(x => x > threshold);
        Console.WriteLine(string.Join(",", q));
        threshold = 2;
        Console.WriteLine(string.Join(",", q));

        // A foreach variable captured per iteration.
        var fns = new List<Func<int>>();
        foreach (var x in xs) fns.Add(() => x * 10);
        Console.WriteLine(string.Join(",", fns.Select(f => f())));

        // Method groups and local functions as operator arguments.
        bool Odd(int v) => v % 2 == 1;
        Console.WriteLine(string.Join(",", xs.Where(Odd)));
        Console.WriteLine(string.Join(",", xs.Select(Describe)));
        Console.WriteLine(xs.Aggregate(0, Add));
    }

    static string Describe(int v) => "#" + v;
    static int Add(int a, int b) => a + b;
}
""";
            await RunTest(code);
        }
    }
}
