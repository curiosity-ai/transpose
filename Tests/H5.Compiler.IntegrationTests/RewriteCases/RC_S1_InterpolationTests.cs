using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Rewrite case S1 (string interpolation lowering to string.Format)
    [TestClass]
    public class RC_S1_InterpolationTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task Interpolation_AlignmentFormatAndEscapes()
        {
            var code = """
using System;

public class Program
{
    public static void Main()
    {
        int x = 42;
        double d = 3.14159;

        // alignment (positive/negative), format, both
        Console.WriteLine($"[{x,6}]");
        Console.WriteLine($"[{x,-6}]");
        Console.WriteLine($"[{d:F2}]");
        Console.WriteLine($"[{d,10:F3}]");
        Console.WriteLine($"[{d,-10:F1}]");

        // escaped braces around and inside holes
        Console.WriteLine($"{{literal}} {x} {{{x}}}");

        // adjacent holes and empty text parts
        Console.WriteLine($"{x}{x}{x}");

        // quotes and colon inside expression (parenthesized)
        Console.WriteLine($"{(x > 0 ? "pos" : "neg")}");

        // verbatim interpolated
        string path = $@"a\b\{x}";
        Console.WriteLine(path);

        // interpolation of null and of bool
        string s = null;
        bool b = true;
        Console.WriteLine($"[{s}] [{b}]");
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Interpolation_NestedAndComposed()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        int x = 5;

        // nested interpolated string inside a hole
        Console.WriteLine($"outer {$"inner {x}"} end");

        // method calls and LINQ inside holes
        var nums = new[] { 1, 2, 3 };
        Console.WriteLine($"sum={nums.Sum()} max={nums.Max()}");

        // interpolation inside lambda inside interpolation argument
        Func<int, string> f = n => $"n={n}";
        Console.WriteLine($"{f(x)}!");

        // string.Format-style index characters in the text must survive
        Console.WriteLine($"literal {{0}} and value {x}");

        // deeply nested ternaries
        int y = 2;
        Console.WriteLine($"{(x > y ? $"{x}>{y}" : $"{x}<={y}")}");

        // concatenation of interpolated parts
        string combined = $"a{x}" + $"b{y}";
        Console.WriteLine(combined);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Interpolation_InVariousMemberContexts()
        {
            var code = """
using System;

public class Item
{
    public int Id { get; set; }
    public string Label => $"item-{Id}";           // expression-bodied property
    public override string ToString() => $"Item({Id})";
}

public class Program
{
    private static string _greeting = $"hello {1 + 1}";  // field initializer

    public static void Main()
    {
        Console.WriteLine(_greeting);
        var it = new Item { Id = 7 };
        Console.WriteLine(it.Label);
        Console.WriteLine(it);
        Console.WriteLine($"{it}");           // hole calling ToString
        Console.WriteLine(Describe(3));
    }

    private static string Describe(int n) => $"n={n},sq={n * n}";
}
""";
            await RunTest(code);
        }
    }
}
