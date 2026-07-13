using System.Threading.Tasks;

namespace H5.Translator.Roslyn.Tests;

[TestClass]
public class OperatorOverloadingTests : TranslatorTestBase
{
    [TestMethod]
    public async Task StructOperators()
    {
        var code = """
using System;
public struct Complex
{
    public double Re, Im;
    public Complex(double re, double im) { Re = re; Im = im; }
    public static Complex operator +(Complex a, Complex b) => new Complex(a.Re + b.Re, a.Im + b.Im);
    public static Complex operator *(Complex a, Complex b) => new Complex(a.Re * b.Re - a.Im * b.Im, a.Re * b.Im + a.Im * b.Re);
    public static Complex operator -(Complex a) => new Complex(-a.Re, -a.Im);
    public static bool operator ==(Complex a, Complex b) => a.Re == b.Re && a.Im == b.Im;
    public static bool operator !=(Complex a, Complex b) => !(a == b);
    public override string ToString() => Re + (Im >= 0 ? "+" : "") + Im + "i";
    public override bool Equals(object o) => o is Complex c && this == c;
    public override int GetHashCode() => 0;
}
public class Program
{
    public static void Main()
    {
        var a = new Complex(1, 2);
        var b = new Complex(3, -1);
        Console.WriteLine(a + b);
        Console.WriteLine(a * b);
        Console.WriteLine(-a);
        Console.WriteLine(a == new Complex(1, 2));
        Console.WriteLine(a != b);
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task VectorArithmetic()
    {
        var code = """
using System;
public class Vector2
{
    public double X, Y;
    public Vector2(double x, double y) { X = x; Y = y; }
    public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator *(Vector2 a, double s) => new Vector2(a.X * s, a.Y * s);
    public override string ToString() => $"<{X}, {Y}>";
}
public class Program
{
    public static void Main()
    {
        var v = new Vector2(1, 2) + new Vector2(3, 4);
        Console.WriteLine(v);
        Console.WriteLine(v * 2);
    }
}
""";
        await RunTest(code);
    }
}
