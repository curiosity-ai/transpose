using System.Threading.Tasks;

namespace H5.Translator.Roslyn.Tests;

[TestClass]
public class RecordsTests : TranslatorTestBase
{
    [TestMethod]
    public async Task PositionalRecordValueSemantics()
    {
        var code = """
using System;
public record Point(int X, int Y);
public class Program
{
    public static void Main()
    {
        var p = new Point(3, 4);
        Console.WriteLine(p);
        Console.WriteLine(p.X + "," + p.Y);
        var p2 = new Point(3, 4);
        var p3 = new Point(5, 6);
        Console.WriteLine(p == p2);
        Console.WriteLine(p == p3);
        Console.WriteLine(p.Equals(p2));
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task WithExpression()
    {
        var code = """
using System;
public record Point(int X, int Y);
public class Program
{
    public static void Main()
    {
        var p = new Point(3, 4);
        var p4 = p with { Y = 10 };
        Console.WriteLine(p4);
        Console.WriteLine(p);
        Console.WriteLine(p == p4);
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task RecordDeconstruction()
    {
        var code = """
using System;
public record Person(string Name, int Age);
public class Program
{
    public static void Main()
    {
        var person = new Person("Alice", 30);
        Console.WriteLine(person);
        var (name, age) = person;
        Console.WriteLine($"{name} is {age}");
    }
}
""";
        await RunTest(code);
    }
}
