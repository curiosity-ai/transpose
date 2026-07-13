using System.Threading.Tasks;

namespace H5.Translator.Roslyn.Tests;

[TestClass]
public class GenericsAndNullableTests : TranslatorTestBase
{
    [TestMethod]
    public async Task GenericClassesAndMethods()
    {
        var code = """
using System;
public class Box<T>
{
    private T value;
    public Box(T v) { value = v; }
    public T Value => value;
    public string Describe() => "Box of " + value;
}
public class Pair<TK, TV>
{
    public TK Key { get; }
    public TV Val { get; }
    public Pair(TK k, TV v) { Key = k; Val = v; }
}
public static class Util
{
    public static T Max<T>(T a, T b) where T : IComparable<T> => a.CompareTo(b) >= 0 ? a : b;
    public static void Swap<T>(ref T a, ref T b) { T t = a; a = b; b = t; }
}
public class Program
{
    public static void Main()
    {
        var b = new Box<int>(42);
        Console.WriteLine(b.Value);
        Console.WriteLine(b.Describe());
        Console.WriteLine(new Box<string>("hi").Value);
        var p = new Pair<string, int>("age", 30);
        Console.WriteLine(p.Key + "=" + p.Val);
        Console.WriteLine(Util.Max(3, 7));
        Console.WriteLine(Util.Max("apple", "banana"));
        int x = 1, y = 2;
        Util.Swap(ref x, ref y);
        Console.WriteLine($"x={x} y={y}");
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task NullableValueTypes()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        int? n = null;
        Console.WriteLine(n.HasValue);
        Console.WriteLine(n ?? -1);
        n = 5;
        Console.WriteLine(n.HasValue + " " + n.Value);
        Console.WriteLine(n.GetValueOrDefault());
        int? m = null;
        Console.WriteLine(m.GetValueOrDefault(99));
    }
}
""";
        await RunTest(code);
    }
}
