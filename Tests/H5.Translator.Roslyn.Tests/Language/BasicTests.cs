using System.Threading.Tasks;

namespace H5.Translator.Roslyn.Tests;

[TestClass]
public class BasicTests : TranslatorTestBase
{
    [TestMethod]
    public async Task HelloWorld()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        Console.WriteLine("Hello World");
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task SimpleArithmetic()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        int a = 10;
        int b = 20;
        Console.WriteLine(a + b);
        Console.WriteLine(a * b);
        Console.WriteLine(a - b);
        Console.WriteLine(b / a);
        Console.WriteLine(b % a);
        Console.WriteLine(7 / 2);
        Console.WriteLine(7.0 / 2);
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task ForLoop()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        for (int i = 0; i < 5; i++)
        {
            Console.WriteLine("Iter: " + i);
        }
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task UnaryNotWrapsBinaryOperand()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        ushort nodeType = 1;
        if (!(nodeType == 1)) { Console.WriteLine("a"); } else { Console.WriteLine("b"); }
        nodeType = 3;
        if (!(nodeType == 1)) { Console.WriteLine("c"); } else { Console.WriteLine("d"); }
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task BooleanAndStringFormatting()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        bool t = true, f = false;
        Console.WriteLine(t);
        Console.WriteLine(f);
        Console.WriteLine("t=" + t + ", f=" + f);
        char c = 'A';
        Console.WriteLine(c);
        Console.WriteLine("{0} + {1} = {2}", 2, 3, 5);
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task StringInterpolation()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        int x = 5;
        string s = $"x={x}, x*2={x * 2}, half={x / 2}";
        Console.WriteLine(s);
    }
}
""";
        await RunTest(code);
    }
}
