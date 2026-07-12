using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Rewrite case S35 (collection expressions incl. spreads) and S33 (params collections)
    [TestClass]
    public class RC_S35_CollectionExpressionTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task CollectionExpr_TargetTypesAndSpreads()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        int[] arr = [1, 2, 3];
        List<int> list = [4, 5];
        IEnumerable<int> seq = [6];
        IList<int> ilist = [7, 8];
        int[] empty = [];

        Console.WriteLine(string.Join(",", arr));
        Console.WriteLine(string.Join(",", list));
        Console.WriteLine(string.Join(",", seq));
        Console.WriteLine(string.Join(",", ilist));
        Console.WriteLine(empty.Length);

        // spreads combining arrays/lists/expressions
        int[] combined = [0, .. arr, .. list, 9];
        Console.WriteLine(string.Join(",", combined));

        List<int> viaSpread = [.. arr.Where(x => x > 1), 100];
        Console.WriteLine(string.Join(",", viaSpread));

        // nested in object initializer and argument position
        Console.WriteLine(Sum([1, 2, 3, 4]));

        var holder = new Holder { Values = [.. arr, .. arr] };
        Console.WriteLine(string.Join(",", holder.Values));
    }

    private static int Sum(int[] xs) => xs.Sum();
}

public class Holder
{
    public int[] Values { get; set; }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task CollectionExpr_SpreadEvaluationOrder()
        {
            var code = """
using System;
using System.Collections.Generic;

public class Program
{
    private static List<int> Make(string tag, params int[] xs)
    {
        Console.WriteLine("make:" + tag);
        return new List<int>(xs);
    }

    public static void Main()
    {
        // spread sources evaluated in order, exactly once
        int[] all = [.. Make("a", 1, 2), 0, .. Make("b", 3)];
        Console.WriteLine(string.Join(",", all));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task ParamsCollections_NonArrayTargets()
        {
            var code = """
using System;
using System.Collections.Generic;

public class Program
{
    private static int SumEnumerable(params IEnumerable<int> xs)
    {
        int t = 0;
        foreach (var x in xs) t += x;
        return t;
    }

    private static int CountList(params List<int> xs) => xs.Count;

    public static void Main()
    {
        Console.WriteLine(SumEnumerable(1, 2, 3));
        Console.WriteLine(SumEnumerable());
        Console.WriteLine(CountList(1, 2, 3, 4));
        // explicit collection argument still works
        Console.WriteLine(SumEnumerable(new List<int> { 5, 6 }.ToArray()));
    }
}
""";
            await RunTest(code);
        }
    }
}
