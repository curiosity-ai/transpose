using System.Threading.Tasks;

namespace H5.Translator.Roslyn.Tests;

[TestClass]
public class LinqAndIteratorsTests : TranslatorTestBase
{
    [TestMethod]
    public async Task IteratorYield()
    {
        var code = """
using System;
using System.Collections.Generic;
public class Program
{
    static IEnumerable<int> Fib(int n)
    {
        int a = 0, b = 1;
        for (int i = 0; i < n; i++)
        {
            yield return a;
            int t = a + b; a = b; b = t;
        }
    }
    public static void Main()
    {
        foreach (var f in Fib(8)) Console.Write(f + " ");
        Console.WriteLine();
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task LinqAggregatesAndFilters()
    {
        var code = """
using System;
using System.Collections.Generic;
using System.Linq;
public class Program
{
    public static void Main()
    {
        var nums = new List<int> { 5, 3, 8, 1, 9, 2, 7 };
        var evens = nums.Where(x => x % 2 == 0).OrderBy(x => x).ToList();
        Console.WriteLine(string.Join(",", evens));
        Console.WriteLine("sum=" + nums.Sum());
        Console.WriteLine("max=" + nums.Max());
        Console.WriteLine("min=" + nums.Min());
        Console.WriteLine("count>4=" + nums.Count(x => x > 4));
        Console.WriteLine("any>8=" + nums.Any(x => x > 8));
        Console.WriteLine("all>0=" + nums.All(x => x > 0));
        Console.WriteLine("first even=" + nums.First(x => x % 2 == 0));
        Console.WriteLine(string.Join(",", nums.Select(x => x * 2).Take(3)));
        Console.WriteLine(string.Join(",", nums.Distinct().Skip(2)));
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task LinqGroupingAndOrdering()
    {
        var code = """
using System;
using System.Linq;
public class Program
{
    public static void Main()
    {
        var words = new[] { "apple", "banana", "cherry", "avocado" };
        foreach (var g in words.GroupBy(w => w[0]))
            Console.WriteLine((char)g.Key + ": " + g.Count());
        var byLen = words.OrderBy(w => w.Length).ThenBy(w => w).ToList();
        Console.WriteLine(string.Join(",", byLen));
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task LinqRangeAndAggregate()
    {
        var code = """
using System;
using System.Linq;
public class Program
{
    public static void Main()
    {
        var squares = Enumerable.Range(1, 5).Select(x => x * x);
        Console.WriteLine(string.Join(",", squares));
        Console.WriteLine(Enumerable.Range(1, 10).Aggregate(0, (acc, x) => acc + x));
        Console.WriteLine(Enumerable.Range(1, 100).Where(x => x % 7 == 0).Count());
        var dict = Enumerable.Range(1, 3).ToDictionary(x => x, x => x * x);
        Console.WriteLine(dict[2] + "," + dict[3]);
    }
}
""";
        await RunTest(code);
    }
}
