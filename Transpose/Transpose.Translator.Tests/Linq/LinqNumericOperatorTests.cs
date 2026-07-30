using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests.Linq
{
    /// <summary>
    /// The numeric LINQ operators — <c>Sum</c>, <c>Average</c>, <c>Min</c>, <c>Max</c>, <c>Aggregate</c>,
    /// <c>Count</c>/<c>LongCount</c> — across <b>every element type they are declared for</b> and
    /// <b>every overload shape</b>: the bare form, the <c>Func&lt;TSource, N&gt;</c> selector form, the
    /// nullable element type, and the generic <c>Min/Max&lt;TSource&gt;</c> /
    /// <c>Min/Max&lt;TSource, TResult&gt;</c> forms that work off <c>IComparable</c>.
    ///
    /// <c>Enumerable</c> declares these once per numeric type (int, long, float, double, decimal) and
    /// again for each nullable counterpart, plus a second copy of each taking
    /// <c>EnumerableInstance&lt;T&gt;</c> so a chained query keeps the same overload set — 100+ signatures
    /// in total. The receiver therefore matters as much as the element type, and every test below exercises
    /// both an array/List receiver (binds the <c>IEnumerable&lt;T&gt;</c> overload) and a query receiver
    /// (binds the <c>EnumerableInstance&lt;T&gt;</c> one).
    ///
    /// Each test transpiles the snippet, runs it on Node and diffs the output against the same C# run
    /// natively, so what is pinned is observable .NET behaviour: the *result type* of an aggregate
    /// (<c>Average</c> of an int sequence is a double, of a long sequence also a double, of a decimal
    /// sequence a decimal), how nulls are skipped, what an empty sequence does, and how the result is
    /// rendered — <c>Average()</c> of {1,2,4} must print 2.3333333333333335, the shortest
    /// round-trippable form .NET Core uses, not a 15-digit truncation.
    /// </summary>
    [TestClass]
    public class LinqNumericOperatorTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task Sum_Int_AllOverloads()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        int[] xs = { 1, 2, 3, 4 };
        Console.WriteLine(xs.Sum());
        Console.WriteLine(xs.Sum(x => x * 2));
        Console.WriteLine(Enumerable.Empty<int>().Sum());
        Console.WriteLine(Enumerable.Empty<int>().Sum(x => x));

        List<int> negatives = new List<int> { -5, 10, -1 };
        Console.WriteLine(negatives.Sum());
        Console.WriteLine(negatives.AsEnumerable().Sum());
        Console.WriteLine(negatives.Where(x => x > 0).Sum());
        Console.WriteLine(negatives.Select(x => x).Sum(x => x));
        Console.WriteLine(Enumerable.Range(1, 100).Sum());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Sum_NullableInt_SkipsNulls()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        int?[] xs = { 1, null, 3 };
        Console.WriteLine(xs.Sum());
        Console.WriteLine(xs.Sum(x => x));
        Console.WriteLine(new int?[0].Sum());
        Console.WriteLine(new int?[] { null, null }.Sum());
        Console.WriteLine(xs.Where(x => x != null).Sum());
        Console.WriteLine(new[] { 1, 2, 3 }.Sum(x => x == 2 ? (int?)null : x));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Sum_Long_AndNullableLong()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        long[] xs = { 1L, 2L, 3000000000L };
        Console.WriteLine(xs.Sum());
        Console.WriteLine(xs.Sum(x => x));
        long?[] ns = { 1L, null, 5L };
        Console.WriteLine(ns.Sum());
        Console.WriteLine(ns.Sum(x => x));
        Console.WriteLine(new[] { 1, 2, 3 }.Sum(x => (long)x));
        Console.WriteLine(new[] { 1, 2, 3 }.Select(x => (long)x).Sum());
        Console.WriteLine(new[] { long.MaxValue / 2, 1L }.Sum());
        Console.WriteLine(new long[0].Sum());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Sum_FloatAndDouble_AndTheirNullables()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        float[] fs = { 1.5f, 2.25f, -0.75f };
        Console.WriteLine(fs.Sum());
        Console.WriteLine(fs.Sum(x => x));
        double[] ds = { 1.5, 2.25, -0.75 };
        Console.WriteLine(ds.Sum());
        Console.WriteLine(ds.Sum(x => x));
        float?[] nfs = { 1.5f, null, 0.25f };
        Console.WriteLine(nfs.Sum());
        Console.WriteLine(nfs.Sum(x => x));
        double?[] nds = { 1.5, null, 0.25 };
        Console.WriteLine(nds.Sum());
        Console.WriteLine(nds.Sum(x => x));
        Console.WriteLine(new float[0].Sum());
        Console.WriteLine(new double[0].Sum());
        Console.WriteLine(new[] { 1, 2 }.Sum(x => (double)x / 4));
        Console.WriteLine(new[] { 0.1, 0.2 }.Sum());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Sum_Decimal_AndNullableDecimal()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        decimal[] xs = { 1.5m, 2.25m, 0.25m };
        Console.WriteLine(xs.Sum());
        Console.WriteLine(xs.Sum(x => x));
        decimal?[] ns = { 1.5m, null, 2m };
        Console.WriteLine(ns.Sum());
        Console.WriteLine(ns.Sum(x => x));
        Console.WriteLine(new decimal[0].Sum());
        // 0.1 + 0.2 + 0.3 is exact in decimal and is NOT in double.
        Console.WriteLine(new[] { 0.1m, 0.2m, 0.3m }.Sum());
        Console.WriteLine(new[] { 1, 2, 3 }.Sum(x => (decimal)x / 2));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Average_IntAndLong_ReturnDouble()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        int[] xs = { 1, 2, 3, 4 };
        Console.WriteLine(xs.Average());
        Console.WriteLine(xs.Average(x => x));
        // 7/3 does not divide evenly: the result must render as the shortest round-trippable double.
        Console.WriteLine(new[] { 1, 2, 4 }.Average());
        Console.WriteLine(new[] { 1L, 2L, 4L }.Average());
        Console.WriteLine(new[] { 1L, 2L, 4L }.Average(x => x));
        int?[] ns = { 1, null, 3 };
        Console.WriteLine(ns.Average());
        Console.WriteLine(ns.Average(x => x));
        long?[] nls = { 1L, null, 3L };
        Console.WriteLine(nls.Average());
        Console.WriteLine(nls.Average(x => x));
        Console.WriteLine(Enumerable.Range(1, 10).Average());
        Console.WriteLine(xs.Where(x => x > 1).Average());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Average_FloatDoubleDecimal_KeepTheirOwnResultType()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        float[] fs = { 1.5f, 2.5f };
        Console.WriteLine(fs.Average());
        Console.WriteLine(fs.Average(x => x));
        Console.WriteLine(new[] { 1.0f, 2.0f, 4.0f }.Average());
        double[] ds = { 1.5, 2.0 };
        Console.WriteLine(ds.Average());
        Console.WriteLine(ds.Average(x => x));
        Console.WriteLine(new[] { 1.0, 2.0, 4.0 }.Average());
        decimal[] ms = { 1.5m, 2.0m };
        Console.WriteLine(ms.Average());
        Console.WriteLine(ms.Average(x => x));
        Console.WriteLine(new[] { 1m, 2m, 4m }.Average());
        float?[] nfs = { 1.5f, null, 2.5f };
        Console.WriteLine(nfs.Average());
        Console.WriteLine(nfs.Average(x => x));
        double?[] nds = { 1.5, null };
        Console.WriteLine(nds.Average());
        decimal?[] nms = { 1.5m, null };
        Console.WriteLine(nms.Average());
        Console.WriteLine(new int?[] { null }.Average() == null);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task MinMax_EveryNumericType()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new[] { 3, 1, 2 }.Min() + "," + new[] { 3, 1, 2 }.Max());
        Console.WriteLine(new[] { 3L, 1L }.Min() + "," + new[] { 3L, 1L }.Max());
        Console.WriteLine(new[] { 3.5f, 1.5f }.Min() + "," + new[] { 3.5f, 1.5f }.Max());
        Console.WriteLine(new[] { 3.5, 1.5 }.Min() + "," + new[] { 3.5, 1.5 }.Max());
        Console.WriteLine(new[] { 3.5m, 1.5m }.Min() + "," + new[] { 3.5m, 1.5m }.Max());
        Console.WriteLine(new[] { long.MinValue, 0L }.Min());
        Console.WriteLine(new[] { int.MinValue, int.MaxValue }.Min() + "," + new[] { int.MinValue, int.MaxValue }.Max());
        Console.WriteLine(new[] { 1 }.Min() + "," + new[] { 1 }.Max());
        Console.WriteLine(new[] { 1, 2, 3 }.Where(x => x > 1).Min());
        Console.WriteLine(new[] { 1, 2, 3 }.Select(x => x * 2).Max());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task MinMax_NullableNumerics_SkipNullsAndReturnNullWhenEmpty()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new int?[] { 3, null, 1 }.Min() + "," + new int?[] { 3, null, 1 }.Max());
        Console.WriteLine(new int?[] { null, null }.Min() == null);
        Console.WriteLine(new int?[0].Min() == null);
        Console.WriteLine(new int?[0].Max() == null);
        Console.WriteLine(new long?[] { 3L, null }.Min() + "," + new long?[] { 3L, null }.Max());
        Console.WriteLine(new float?[] { 3.5f, null }.Min() + "," + new float?[] { 3.5f, null }.Max());
        Console.WriteLine(new double?[] { 3.5, null }.Min() + "," + new double?[] { 3.5, null }.Max());
        Console.WriteLine(new decimal?[] { 3.5m, null }.Min() + "," + new decimal?[] { 3.5m, null }.Max());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task MinMax_SelectorOverloads_ForEveryNumericResult()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { "a", "bbb", "cc" };
        Console.WriteLine(xs.Min(s => s.Length) + "," + xs.Max(s => s.Length));
        Console.WriteLine(xs.Min(s => (long)s.Length) + "," + xs.Max(s => (long)s.Length));
        Console.WriteLine(xs.Min(s => (float)s.Length) + "," + xs.Max(s => (float)s.Length));
        Console.WriteLine(xs.Min(s => (double)s.Length) + "," + xs.Max(s => (double)s.Length));
        Console.WriteLine(xs.Min(s => (decimal)s.Length) + "," + xs.Max(s => (decimal)s.Length));
        Console.WriteLine(xs.Min(s => (int?)s.Length) + "," + xs.Max(s => (int?)s.Length));
        Console.WriteLine(xs.Min(s => s.Length == 1 ? (int?)null : s.Length));
        // The generic Min<TSource>/Max<TSource, TResult> forms, over IComparable rather than a number.
        Console.WriteLine(xs.Min() + "," + xs.Max());
        Console.WriteLine(xs.Min(s => s + "!") + "," + xs.Max(s => s + "!"));
        Console.WriteLine(new[] { 'c', 'a' }.Min() + "," + new[] { 'c', 'a' }.Max());
        var dates = new[] { new DateTime(2020, 1, 2), new DateTime(2019, 5, 5) };
        Console.WriteLine(dates.Min().ToString("yyyy-MM-dd") + "," + dates.Max().ToString("yyyy-MM-dd"));
        Console.WriteLine(dates.Min(d => d.Year) + "," + dates.Max(d => d.Year));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Aggregate_AllThreeOverloads()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { 1, 2, 3, 4 };
        Console.WriteLine(xs.Aggregate((a, b) => a + b));
        Console.WriteLine(xs.Aggregate(10, (a, b) => a + b));
        Console.WriteLine(xs.Aggregate(10, (a, b) => a + b, r => "r=" + r));
        var ss = new[] { "a", "b" };
        Console.WriteLine(ss.Aggregate((a, b) => a + "|" + b));
        Console.WriteLine(ss.Aggregate("", (a, b) => a + b.ToUpper()));
        Console.WriteLine(ss.Aggregate(0, (a, b) => a + b.Length, n => n * 2));
        Console.WriteLine(xs.Where(x => x > 2).Aggregate(0, (a, b) => a + b));
        Console.WriteLine(new int[0].Aggregate(7, (a, b) => a + b));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Aggregate_AccumulatorOfEveryShape()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public class Program
{
    public static void Main()
    {
        var xs = new[] { 1, 2, 3 };
        Console.WriteLine(xs.Aggregate(new List<int>(), (acc, x) => { acc.Add(x * 2); return acc; }).Count);
        Console.WriteLine(xs.Aggregate("", (acc, x) => acc + x));
        Console.WriteLine(xs.Aggregate(1L, (acc, x) => acc * x));
        Console.WriteLine(xs.Aggregate(0.5, (acc, x) => acc + x));
        Console.WriteLine(xs.Aggregate(0m, (acc, x) => acc + x));
        Console.WriteLine(xs.Aggregate(new StringBuilder(), (acc, x) => acc.Append(x), sb => sb.ToString()));
        Console.WriteLine(xs.Aggregate(new Dictionary<int, int>(), (acc, x) => { acc[x] = x * x; return acc; }, d => d.Count));
        Console.WriteLine(xs.Aggregate((int?)0, (acc, x) => acc + x));
        Console.WriteLine(xs.Aggregate(new[] { 0 }, (acc, x) => acc.Concat(new[] { x }).ToArray()).Length);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task CountAndLongCount_BothOverloads()
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
        Console.WriteLine(xs.Count() + "," + xs.Count(x => x > 1));
        Console.WriteLine(xs.LongCount() + "," + xs.LongCount(x => x > 1));
        Console.WriteLine(new int[0].Count() + "," + new int[0].LongCount());
        Console.WriteLine(xs.Where(x => x > 1).Count() + "," + xs.Where(x => x > 1).LongCount());
        Console.WriteLine(new List<string> { "a", "bb" }.Count(s => s.Length > 1));
        Console.WriteLine("hello".Count(c => c == 'l'));
        Console.WriteLine(Enumerable.Range(0, 1000).Count(x => x % 7 == 0));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task NumericAggregates_OverAQueryReceiver_BindTheEnumerableInstanceOverloads()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { 1, 2, 3, 4 };
        var q = xs.Where(x => x > 1);
        Console.WriteLine(q.Sum() + "," + q.Average() + "," + q.Min() + "," + q.Max() + "," + q.Count());
        var ls = xs.Select(x => (long)x).Where(x => x > 1);
        Console.WriteLine(ls.Sum() + "," + ls.Min() + "," + ls.Max());
        var fs = xs.Select(x => (float)x / 2).Where(x => x > 0.5f);
        Console.WriteLine(fs.Sum() + "," + fs.Min() + "," + fs.Max());
        var ds = xs.Select(x => (double)x / 4).Where(x => x > 0.25);
        Console.WriteLine(ds.Sum() + "," + ds.Min() + "," + ds.Max());
        var ms = xs.Select(x => (decimal)x / 2).Where(x => x > 0.5m);
        Console.WriteLine(ms.Sum() + "," + ms.Average() + "," + ms.Min() + "," + ms.Max());
        var ns = xs.Select(x => x == 2 ? (int?)null : x);
        Console.WriteLine(ns.Sum() + "," + ns.Min() + "," + ns.Max());
    }
}
""";
            await RunTest(code);
        }
    }
}
