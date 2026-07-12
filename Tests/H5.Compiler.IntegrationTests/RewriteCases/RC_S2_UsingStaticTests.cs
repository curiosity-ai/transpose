using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Rewrite case S2/R4 (using static + using aliases removed, usages re-qualified)
    [TestClass]
    public class RC_S2_UsingStaticTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task UsingStatic_BclAndUserTypes()
        {
            var code = """
using System;
using static System.Math;
using static Helper;

public static class Helper
{
    public const int Answer = 42;
    public static string Tag = "helper";
    public static int Triple(int x) => x * 3;
    public static string Name { get; set; } = "prop";
}

public class Program
{
    public static void Main()
    {
        // static methods via using static
        Console.WriteLine(Max(3, 9));
        Console.WriteLine(Abs(-5));
        Console.WriteLine(Sqrt(16));

        // user static class: const, field, method, property
        Console.WriteLine(Answer);
        Console.WriteLine(Tag);
        Console.WriteLine(Triple(7));
        Console.WriteLine(Name);
        Name = "changed";
        Console.WriteLine(Name);

        // qualified access still works alongside
        Console.WriteLine(Math.Min(1, 2));
        Console.WriteLine(Helper.Triple(2));

        // using-static member in nested expressions/lambdas
        Func<int, int> f = x => Triple(Max(x, 2));
        Console.WriteLine(f(1));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task UsingAliases_TypesGenericsAndNested()
        {
            var code = """
using System;
using IntList = System.Collections.Generic.List<int>;
using StrMap = System.Collections.Generic.Dictionary<string, string>;
using Con = System.Console;
using Outer = Container.Nested;

public static class Container
{
    public static class Nested
    {
        public static string Where() => "nested";
    }
}

public class Program
{
    public static void Main()
    {
        var list = new IntList { 1, 2, 3 };
        list.Add(4);
        Con.WriteLine(string.Join(",", list));

        var map = new StrMap { ["k"] = "v" };
        Con.WriteLine(map["k"]);

        Con.WriteLine(Outer.Where());

        // alias in generic argument, param and return positions
        Con.WriteLine(Sum(list));
        IntList Make() => new IntList { 9 };
        Con.WriteLine(Make()[0]);
    }

    private static int Sum(IntList xs)
    {
        int total = 0;
        foreach (var x in xs) total += x;
        return total;
    }
}
""";
            // The Roslyn scripting host used for the reference run does not support
            // using-alias directives, so assert the H5 output directly.
            var output = await RunTest(code, skipRoslyn: true);
            Assert.AreEqual("1,2,3,4\nv\nnested\n10\n9", output);
        }

        [TestMethod]
        public async Task UsingStatic_EnumMembersAndMixedScopes()
        {
            var code = """
using System;
using static Color;
using static System.String;

public enum Color { Red, Green, Blue }

public class Program
{
    public static void Main()
    {
        // enum members via using static
        Color c = Green;
        Console.WriteLine(c == Color.Green);
        Console.WriteLine(Pick(Blue) );

        // using static System.String
        Console.WriteLine(IsNullOrEmpty(""));
        Console.WriteLine(Join("-", new[] { "a", "b" }));
        Console.WriteLine(Concat("x", "y"));
    }

    private static string Pick(Color c)
    {
        switch (c)
        {
            case Red: return "r";
            case Green: return "g";
            case Blue: return "b";
            default: return "?";
        }
    }
}
""";
            await RunTest(code);
        }
    }
}
