using System.Threading.Tasks;

namespace H5.Translator.Roslyn.Tests;

[TestClass]
public class DateTimeRandomGuidTests : TranslatorTestBase
{
    [TestMethod]
    public async Task SeededRandomMatchesDotNet()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        var r = new Random(42);
        for (int i = 0; i < 8; i++) Console.Write(r.Next(100) + " ");
        Console.WriteLine();
        Console.WriteLine(r.Next(10, 20));
        var r2 = new Random(123);
        Console.WriteLine(r2.NextDouble().ToString("F5"));
        Console.WriteLine(r2.Next());
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task DateTimeComponentsAndArithmetic()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        var dt = new DateTime(2020, 3, 15, 10, 30, 45);
        Console.WriteLine(dt.Year + "-" + dt.Month + "-" + dt.Day);
        Console.WriteLine(dt.Hour + ":" + dt.Minute + ":" + dt.Second);
        Console.WriteLine(dt.ToString("yyyy-MM-dd HH:mm:ss"));
        Console.WriteLine(dt.AddDays(20).ToString("yyyy-MM-dd"));
        Console.WriteLine(dt.AddMonths(11).ToString("yyyy-MM-dd"));
        Console.WriteLine(dt.AddYears(1).Year);
        Console.WriteLine(dt < dt.AddDays(1));
        Console.WriteLine(DateTime.IsLeapYear(2020));
        Console.WriteLine(DateTime.DaysInMonth(2021, 2));
        Console.WriteLine((int)dt.DayOfWeek);
        Console.WriteLine(dt.DayOfYear);
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task TimeSpanUsage()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        var ts = TimeSpan.FromHours(25.5);
        Console.WriteLine(ts.Days + "d " + ts.Hours + "h " + ts.Minutes + "m");
        Console.WriteLine(ts.TotalMinutes);
        Console.WriteLine(TimeSpan.FromSeconds(90).ToString());
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task GuidParseAndEquals()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        var g = Guid.Parse("D9B2D63D-A233-4123-847B-9E1F1B2C3D4E");
        Console.WriteLine(g.ToString());
        Console.WriteLine(g.Equals(Guid.Parse("d9b2d63d-a233-4123-847b-9e1f1b2c3d4e")));
        Console.WriteLine(Guid.Empty);
    }
}
""";
        await RunTest(code);
    }
}
