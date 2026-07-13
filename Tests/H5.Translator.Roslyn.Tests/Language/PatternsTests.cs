using System.Threading.Tasks;

namespace H5.Translator.Roslyn.Tests;

[TestClass]
public class PatternsTests : TranslatorTestBase
{
    [TestMethod]
    public async Task SwitchExpressionWithPatterns()
    {
        var code = """
using System;
public class Program
{
    static string Classify(object o) => o switch
    {
        int n when n < 0 => "negative int",
        int n => "int " + n,
        string s => "string len " + s.Length,
        null => "null",
        _ => "other"
    };
    public static void Main()
    {
        Console.WriteLine(Classify(5));
        Console.WriteLine(Classify(-3));
        Console.WriteLine(Classify("hello"));
        Console.WriteLine(Classify(null));
        Console.WriteLine(Classify(3.14));
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task IsPatternWithBinding()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        object x = 42;
        if (x is int v && v > 10) Console.WriteLine("big int " + v);
        object s = "hi";
        if (s is string str) Console.WriteLine("got " + str);
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task RelationalAndLogicalPatterns()
    {
        var code = """
using System;
public class Program
{
    static string Grade(int g) => g switch
    {
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        _ => "F"
    };
    public static void Main()
    {
        Console.WriteLine(Grade(95));
        Console.WriteLine(Grade(85));
        Console.WriteLine(Grade(50));
        int day = 3;
        switch (day)
        {
            case 0: case 6: Console.WriteLine("weekend"); break;
            case >= 1 and <= 5: Console.WriteLine("weekday"); break;
            default: Console.WriteLine("?"); break;
        }
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task TuplesAndDeconstruction()
    {
        var code = """
using System;
public class Program
{
    static (int, int) MinMax(int a, int b) => a < b ? (a, b) : (b, a);
    public static void Main()
    {
        var (lo, hi) = MinMax(8, 3);
        Console.WriteLine($"lo={lo} hi={hi}");
        (int a, string b) t = (1, "one");
        Console.WriteLine(t.a + "=" + t.b + " item1=" + t.Item1);
        int x = 10, y = 20;
        (x, y) = (y, x);
        Console.WriteLine($"x={x} y={y}");
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task PropertyPattern()
    {
        var code = """
using System;
public class Person
{
    public string Name { get; set; }
    public int Age { get; set; }
}
public class Program
{
    static string Describe(Person p) => p switch
    {
        { Age: >= 18 } => p.Name + " is an adult",
        { Age: > 0 } => p.Name + " is a minor",
        _ => "unknown"
    };
    public static void Main()
    {
        Console.WriteLine(Describe(new Person { Name = "Alice", Age = 30 }));
        Console.WriteLine(Describe(new Person { Name = "Bob", Age = 10 }));
    }
}
""";
        await RunTest(code);
    }
}
