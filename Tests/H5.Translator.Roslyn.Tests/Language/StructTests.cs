using System.Threading.Tasks;

namespace H5.Translator.Roslyn.Tests;

[TestClass]
public class StructTests : TranslatorTestBase
{
    [TestMethod]
    public async Task StructValueCopySemantics()
    {
        var code = """
using System;
public struct Vec
{
    public int X, Y;
    public Vec(int x, int y) { X = x; Y = y; }
    public override string ToString() => $"({X}, {Y})";
}
public class Program
{
    static void Mutate(Vec v) { v.X = 999; }
    public static void Main()
    {
        Vec a = new Vec(1, 2);
        Vec b = a;
        b.X = 100;
        Console.WriteLine(a);
        Console.WriteLine(b);
        Mutate(a);
        Console.WriteLine(a);
        Vec d = default;
        Console.WriteLine(d);
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task StructInArrayAndReturn()
    {
        var code = """
using System;
public struct Point
{
    public int X, Y;
    public Point(int x, int y) { X = x; Y = y; }
    public override string ToString() => $"[{X},{Y}]";
}
public class Program
{
    static Point Origin() { Point p = new Point(0, 0); return p; }
    public static void Main()
    {
        Point[] arr = new Point[2];
        Point a = new Point(1, 2);
        arr[0] = a;
        arr[0].X = 55;
        Console.WriteLine(arr[0]);
        Console.WriteLine(a);

        Point o1 = Origin();
        Point o2 = Origin();
        o1.X = 9;
        Console.WriteLine(o1 + " " + o2);
    }
}
""";
        await RunTest(code);
    }
}
