using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Rewrite case S14 (switch expressions lowered to ternary chains)
    [TestClass]
    public class RC_S14_SwitchExpressionTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task SwitchExpr_GovernorEvaluatedOnce()
        {
            var code = """
using System;

public class Program
{
    private static int _calls;

    private static int Next()
    {
        _calls++;
        return _calls;
    }

    public static void Main()
    {
        // governing expression with side effects must evaluate exactly once
        var r = Next() switch
        {
            1 => "one",
            2 => "two",
            _ => "many",
        };
        Console.WriteLine(r);
        Console.WriteLine(_calls);

        // again with method-call + member access governor
        var s = (Next() + 10) switch
        {
            12 => "twelve",
            _ => "other",
        };
        Console.WriteLine(s);
        Console.WriteLine(_calls);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task SwitchExpr_PatternsGuardsAndNesting()
        {
            var code = """
using System;

public class Program
{
    public static void Main()
    {
        for (int i = -2; i <= 3; i++)
        {
            string desc = i switch
            {
                < 0 => "neg",
                0 => "zero",
                1 or 2 => "small",
                int n when n % 2 == 1 => $"odd {n}",
                var n => $"even {n}",
            };
            Console.WriteLine(desc);
        }

        // nested switch expressions
        string Classify(int a, int b) => a switch
        {
            0 => b switch { 0 => "origin", _ => "y-axis" },
            _ => b switch { 0 => "x-axis", _ => "plane" },
        };
        Console.WriteLine(Classify(0, 0));
        Console.WriteLine(Classify(0, 5));
        Console.WriteLine(Classify(5, 0));
        Console.WriteLine(Classify(5, 5));

        // switch expression over tuples with declaration patterns
        object o = "text";
        var kind = o switch
        {
            int v => "int:" + v,
            string s when s.Length > 2 => "str:" + s,
            string s => "short:" + s,
            null => "null",
            _ => "other",
        };
        Console.WriteLine(kind);

        // no-match must throw
        try
        {
            int x = 99;
            var bad = x switch { 1 => "a" };
            Console.WriteLine(bad);
        }
        catch (Exception)
        {
            Console.WriteLine("unmatched threw");
        }
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task SwitchExpr_InVariousContexts()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    private static string Name(int n) => n switch { 1 => "one", _ => "n" + n };  // expr-bodied member

    public static void Main()
    {
        Console.WriteLine(Name(1));
        Console.WriteLine(Name(7));

        // as argument
        Console.WriteLine(string.Concat("got:", 2 switch { 2 => "two", _ => "?" }));

        // inside interpolation
        int k = 3;
        Console.WriteLine($"k is {k switch { 3 => "three", _ => "?" }}");

        // in LINQ projection
        var labels = new[] { 0, 1, 2 }.Select(v => v switch { 0 => "z", 1 => "o", _ => "m" });
        Console.WriteLine(string.Join(",", labels));

        // switch producing lambdas
        Func<int, int> op = "+" switch
        {
            "+" => (Func<int, int>)(x => x + 1),
            _ => x => x - 1,
        };
        Console.WriteLine(op(10));
    }
}
""";
            await RunTest(code);
        }
    }
}
