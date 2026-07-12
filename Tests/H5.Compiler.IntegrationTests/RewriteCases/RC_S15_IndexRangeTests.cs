using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Rewrite case S15 (Index/Range lowering: ^n, a..b, slices)
    [TestClass]
    public class RC_S15_IndexRangeTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task IndexRange_ArraysStringsAndLists()
        {
            var code = """
using System;
using System.Collections.Generic;

public class Program
{
    public static void Main()
    {
        var arr = new[] { 10, 20, 30, 40, 50 };

        // from-end indices
        Console.WriteLine(arr[^1]);
        Console.WriteLine(arr[^5]);

        // ranges over arrays
        Console.WriteLine(string.Join(",", arr[1..3]));
        Console.WriteLine(string.Join(",", arr[..2]));
        Console.WriteLine(string.Join(",", arr[3..]));
        Console.WriteLine(string.Join(",", arr[..]));
        Console.WriteLine(string.Join(",", arr[^3..^1]));

        // strings
        var s = "hello world";
        Console.WriteLine(s[^1]);
        Console.WriteLine(s[0..5]);
        Console.WriteLine(s[6..]);
        Console.WriteLine(s[^5..]);

        // List indexing with ^ (uses Count)
        var list = new List<int> { 1, 2, 3 };
        Console.WriteLine(list[^1]);
        Console.WriteLine(list[^3]);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task IndexRange_VariablesAndSingleEvaluation()
        {
            var code = """
using System;

public class Program
{
    private static int _calls;

    private static int[] GetArr()
    {
        _calls++;
        return new[] { 1, 2, 3, 4 };
    }

    public static void Main()
    {
        // Index/Range stored in variables and reused
        Index i = ^2;
        Range r = 1..^1;
        var arr = new[] { 5, 6, 7, 8 };
        Console.WriteLine(arr[i]);
        Console.WriteLine(string.Join(",", arr[r]));
        Console.WriteLine(i.Value + "," + i.IsFromEnd);
        Console.WriteLine(r.Start.Value + "-" + r.End.Value + "/" + r.End.IsFromEnd);

        // receiver with side effects must evaluate once per access
        Console.WriteLine(GetArr()[^1]);
        Console.WriteLine(_calls);
        Console.WriteLine(string.Join(",", GetArr()[1..3]));
        Console.WriteLine(_calls);

        // computed index expressions
        int n = 2;
        Console.WriteLine(arr[^n]);
        Console.WriteLine(string.Join(",", arr[(n - 1)..(n + 1)]));
    }
}
""";
            await RunTest(code);
        }
    }
}
