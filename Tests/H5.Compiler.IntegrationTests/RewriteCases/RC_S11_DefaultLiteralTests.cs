using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Rewrite case S11 (default literal -> default(T)) and S43 (`is` with non-type RHS)
    [TestClass]
    public class RC_S11_DefaultLiteralTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task DefaultLiteral_AllPositions()
        {
            var code = """
using System;

public struct Pt { public int X; public int Y; }

public class Program
{
    public static void Main()
    {
        // locals
        int i = default;
        string s = default;
        double d = default;
        bool b = default;
        Pt p = default;
        Console.WriteLine(i + "|" + (s == null) + "|" + d + "|" + b + "|" + p.X + "," + p.Y);

        // arguments and named arguments
        Console.WriteLine(Fmt(default, default));
        Console.WriteLine(Fmt(y: default, x: 5));

        // return position
        Console.WriteLine(GetDefault<int>());
        Console.WriteLine(GetDefault<string>() == null);

        // ternary and comparisons
        int k = b ? 1 : default;
        Console.WriteLine(k);
        Console.WriteLine(i == default);
        Console.WriteLine(5 == default(int) + 5);

        // default in generic context with constraint
        Console.WriteLine(MakeNew<Pt>().X);
    }

    private static string Fmt(int x, string y) => x + ":" + (y ?? "<null>");
    private static T GetDefault<T>() => default;
    private static T MakeNew<T>() where T : new() => default(T) is object ? default : new T();
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task IsOperator_NonTypeRhsAndConstants()
        {
            var code = """
using System;

public class Program
{
    public static void Main()
    {
        object o = 5;
        Console.WriteLine(o is int);
        Console.WriteLine(o is 5);           // constant pattern
        Console.WriteLine(o is 6);
        object s = "hi";
        Console.WriteLine(s is "hi");
        Console.WriteLine(s is null);
        object n = null;
        Console.WriteLine(n is null);
    }
}
""";
            await RunTest(code);
        }
    }
}
