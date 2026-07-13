using System.Threading.Tasks;

namespace H5.Translator.Roslyn.Tests;

[TestClass]
public class ControlFlowTests : TranslatorTestBase
{
    [TestMethod]
    public async Task WhileAndDoWhile()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        int i = 0;
        while (i < 3) { Console.WriteLine("w" + i); i++; }
        int j = 0;
        do { Console.WriteLine("d" + j); j++; } while (j < 3);
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task BreakAndContinue()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        for (int i = 0; i < 10; i++)
        {
            if (i == 3) continue;
            if (i == 6) break;
            Console.WriteLine(i);
        }
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task SwitchStatement()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        for (int i = 0; i < 4; i++)
        {
            switch (i)
            {
                case 0: Console.WriteLine("zero"); break;
                case 1: Console.WriteLine("one"); break;
                default: Console.WriteLine("many"); break;
            }
        }
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task ForeachOverArray()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        int[] nums = new int[] { 10, 20, 30 };
        int sum = 0;
        foreach (int n in nums) { sum += n; }
        Console.WriteLine(sum);
        foreach (char c in "abc") { Console.WriteLine(c); }
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task TernaryAndNullCoalescing()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        int x = 5;
        Console.WriteLine(x > 3 ? "big" : "small");
        string s = null;
        Console.WriteLine(s ?? "default");
    }
}
""";
        await RunTest(code);
    }
}
