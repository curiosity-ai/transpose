using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Cross-case composition tests: the rewrite pipeline's passes interact
    // (pre-passes feed the main visit which feeds post-pass replacers), so
    // removals must keep combinations working, not just isolated features.
    [TestClass]
    public class RC_CompositionTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task Interpolation_Nameof_UsingStatic_Combined()
        {
            var code = """
using System;
using static System.Math;

public class Program
{
    public static void Main()
    {
        int radius = 3;
        // interpolation hole containing nameof + using-static call + format
        Console.WriteLine($"{nameof(radius)}={radius}, area={Round(PI * radius * radius, 2)}");

        // expression-bodied local function using interpolation and using-static
        string Fmt(double v) => $"[{Min(v, 100):F1}]";
        Console.WriteLine(Fmt(42.55));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Patterns_LocalFunctions_Tuples_Combined()
        {
            var code = """
using System;

public class Program
{
    public static void Main()
    {
        // local function returning tuple, deconstructed, patterns on results
        (int q, int r) DivMod(int a, int b) => (a / b, a % b);

        var (q, r) = DivMod(17, 5);
        Console.WriteLine(q + "," + r);

        object o = r;
        if (o is int n && n > 0)
        {
            Console.WriteLine($"remainder {n}");
        }

        // switch expression over tuple from local function
        string Desc(int a, int b) => DivMod(a, b) switch
        {
            (_, 0) => "divides",
            (0, _) => "smaller",
            _ => "other",
        };
        Console.WriteLine(Desc(10, 5));
        Console.WriteLine(Desc(3, 10));
        Console.WriteLine(Desc(17, 5));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task SwitchExpr_VarDeconstructionArm_MinimalPassing()
        {
            var code = """
using System;

public class Program
{
    public static void Main()
    {
        var t = (3, 2);
        // positional patterns with per-element var bindings
        string r = t switch
        {
            (0, 0) => "origin",
            (var x, var y) => "q" + x + "r" + y,
        };
        Console.WriteLine(r);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task SwitchExpr_VarDeconstructionArm_MinimalFailing()
        {
            // Minimal repro of the parse failure seen when a switch-expression arm
            // uses a var *deconstruction* pattern (`var (x, y)`); see FINDINGS.md.
            var code = """
using System;

public class Program
{
    public static void Main()
    {
        var t = (3, 2);
        string r = t switch
        {
            (0, 0) => "origin",
            var (x, y) => "q" + x + "r" + y,
        };
        Console.WriteLine(r);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task ExceptionFilters_Interpolation_ThrowExpr_Combined()
        {
            var code = """
using System;

public class Program
{
    public static void Main()
    {
        string val = null;
        try
        {
            var s = val ?? throw new ArgumentException($"missing {nameof(val)}");
            Console.WriteLine(s);
        }
        catch (ArgumentException e) when (e.Message.Contains(nameof(val)))
        {
            Console.WriteLine($"caught: {e.Message}");
        }
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task AutoProps_Records_With_Init_Combined()
        {
            var code = """
using System;

public record Point(int X, int Y)
{
    public string Label { get; init; } = "pt";
    public int Sum => X + Y;
}

public class Program
{
    public static void Main()
    {
        var p = new Point(1, 2) { Label = "start" };
        Console.WriteLine(p.X + "," + p.Y + "," + p.Label + "," + p.Sum);

        var q = p with { X = 10 };
        Console.WriteLine(q.X + "," + q.Y + "," + q.Label);
        Console.WriteLine(p == q);
        Console.WriteLine(p == new Point(1, 2) { Label = "start" });

        var (x, y) = q;
        Console.WriteLine(x + ";" + y);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task NullConditional_Chained_WithInterpolationAndDefault()
        {
            var code = """
using System;
using System.Collections.Generic;

public class Node
{
    public Node Next { get; set; }
    public string Name { get; set; }
    public List<int> Data { get; set; }
}

public class Program
{
    public static void Main()
    {
        var head = new Node { Name = "a", Next = new Node { Name = "b", Data = new List<int> { 5 } } };

        Console.WriteLine(head?.Next?.Name ?? "none");
        Console.WriteLine(head?.Next?.Next?.Name ?? "none");
        Console.WriteLine(head?.Next?.Data?[0] ?? default);
        Console.WriteLine(head?.Next?.Next?.Data?[0] ?? default);
        Console.WriteLine($"chain: {head?.Next?.Name ?? "-"}/{head?.Next?.Data?.Count ?? 0}");

        // ?. on method call result + is pattern
        if (Find(head)?.Name is string s)
        {
            Console.WriteLine("found " + s);
        }
    }

    private static Node Find(Node n) => n?.Next;
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task LocalFunctions_Async_Interpolation_Combined()
        {
            var code = """
using System;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        async Task<int> GetAsync(int seed)
        {
            await Task.Delay(1);
            return seed * 2;
        }

        int first = await GetAsync(5);
        Console.WriteLine($"first={first}");

        // local fn calling local fn, awaited inside interpolation-bound value
        async Task<string> DescribeAsync(int s) => $"value={await GetAsync(s)}";
        Console.WriteLine(await DescribeAsync(7));

        Console.WriteLine("<<DONE>>");
    }
}
""";
            await RunTest(code, waitForOutput: "<<DONE>>");
        }
    }
}
