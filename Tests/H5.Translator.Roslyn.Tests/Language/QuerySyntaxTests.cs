using System.Threading.Tasks;

namespace H5.Translator.Roslyn.Tests;

[TestClass]
public class QuerySyntaxTests : TranslatorTestBase
{
    [TestMethod]
    public async Task WhereOrderBySelect()
    {
        var code = """
using System;
using System.Linq;
public class Program
{
    public static void Main()
    {
        int[] nums = { 5, 3, 8, 1, 9, 2, 7, 4 };
        var q = from n in nums
                where n % 2 == 0
                orderby n
                select n * 10;
        Console.WriteLine(string.Join(",", q));
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task MultiKeyOrdering()
    {
        var code = """
using System;
using System.Linq;
public class Program
{
    public static void Main()
    {
        int[] nums = { 5, 3, 8, 1, 9, 2, 7, 4 };
        var q = from n in nums
                orderby n % 3, n descending
                select n;
        Console.WriteLine(string.Join(",", q));
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task GroupByQuery()
    {
        var code = """
using System;
using System.Linq;
public class Program
{
    public static void Main()
    {
        var words = new[] { "apple", "banana", "cherry", "avocado", "blueberry" };
        var groups = from w in words group w by w[0];
        foreach (var g in groups)
            Console.WriteLine((char)g.Key + ":" + string.Join("/", g));
        var count = (from n in new[] { 1, 2, 3, 4, 5 } where n > 3 select n).Count();
        Console.WriteLine("count=" + count);
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task DateTimeTimeSpanArithmetic()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        var d1 = new DateTime(2020, 1, 1);
        var d2 = new DateTime(2020, 3, 1);
        TimeSpan diff = d2 - d1;
        Console.WriteLine(diff.Days);
        Console.WriteLine((d1 + TimeSpan.FromDays(60)).ToString("yyyy-MM-dd"));
        Console.WriteLine((d2 - TimeSpan.FromDays(29)).ToString("yyyy-MM-dd"));
        var total = TimeSpan.FromHours(2) + TimeSpan.FromMinutes(30);
        Console.WriteLine(total.TotalMinutes);
    }
}
""";
        await RunTest(code);
    }
}
