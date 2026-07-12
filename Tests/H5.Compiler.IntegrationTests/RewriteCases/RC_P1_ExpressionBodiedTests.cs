using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Rewrite case P1 (ExpressionBodyToStatementRewriter) and S12 (throw expressions)
    [TestClass]
    public class RC_P1_ExpressionBodiedTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task ExpressionBodied_AllMemberKinds()
        {
            var code = """
using System;

public class Vec
{
    private double _x, _y;

    public Vec(double x, double y) => (_x, _y) = (x, y);   // ctor with tuple assignment body

    public double X => _x;                                  // getter-only property
    public double Y { get => _y; set => _y = value; }       // expr-bodied accessors

    public double this[int i] => i == 0 ? _x : _y;          // indexer

    public double Len() => Math.Sqrt(_x * _x + _y * _y);    // method
    public void Reset() => _x = _y = 0;                     // void method (chained assignment!)

    public static Vec operator +(Vec a, Vec b) => new Vec(a._x + b._x, a._y + b._y);
    public static implicit operator string(Vec v) => "(" + v._x + "," + v._y + ")";
}

public class Program
{
    public static void Main()
    {
        var v = new Vec(3, 4);
        Console.WriteLine(v.X);
        Console.WriteLine(v.Y);
        v.Y = 8;
        Console.WriteLine(v.Y);
        Console.WriteLine(v[0] + "," + v[1]);
        Console.WriteLine(new Vec(3, 4).Len());
        var sum = v + new Vec(1, 1);
        Console.WriteLine((string)sum);
        v.Reset();
        Console.WriteLine(v.X + "," + v.Y);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task ThrowExpressions_AllPositions()
        {
            var code = """
using System;

public class Program
{
    private static string _name;
    public static string Name
    {
        get => _name ?? throw new InvalidOperationException("unset");
        set => _name = value ?? throw new ArgumentNullException(nameof(value));
    }

    public static void Main()
    {
        // ?? throw
        try { var s = (string)null ?? throw new Exception("boom1"); }
        catch (Exception e) { Console.WriteLine(e.Message); }

        // ternary throw in one arm
        try { var n = "abc".Length == 3 ? throw new Exception("boom2") : 0; }
        catch (Exception e) { Console.WriteLine(e.Message); }

        // property getter/setter throw expressions
        try { Console.WriteLine(Name); }
        catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
        try { Name = null; }
        catch (ArgumentNullException) { Console.WriteLine("argnull"); }
        Name = "ok";
        Console.WriteLine(Name);

        // throw in expression-bodied method + argument position
        try { Console.WriteLine(Fail<int>()); }
        catch (Exception e) { Console.WriteLine(e.Message); }

        // throw inside lambda expression body
        Func<int, int> f = n => n > 0 ? n : throw new Exception("neg");
        Console.WriteLine(f(5));
        try { f(-1); }
        catch (Exception e) { Console.WriteLine(e.Message); }
    }

    private static T Fail<T>() => throw new Exception("fail<T>");
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task ExpressionBodied_LocalFunctionsAndNesting()
        {
            var code = """
using System;

public class Program
{
    public static void Main()
    {
        int Square(int n) => n * n;                       // expr-bodied local function
        int Cube(int n) => n * Square(n);                 // calling another local fn

        Console.WriteLine(Square(4));
        Console.WriteLine(Cube(3));

        Func<int, int> twice = x => x * 2;
        int Apply(Func<int, int> f, int v) => f(v);       // local fn taking lambda
        Console.WriteLine(Apply(twice, 5));

        // expression-bodied local function inside a lambda
        Func<int, int> outer = x =>
        {
            int Inner(int y) => y + 1;
            return Inner(x) * 10;
        };
        Console.WriteLine(outer(2));
    }
}
""";
            await RunTest(code);
        }
    }
}
