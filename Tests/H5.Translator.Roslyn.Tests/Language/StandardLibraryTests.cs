using System.Threading.Tasks;

namespace H5.Translator.Roslyn.Tests;

[TestClass]
public class StandardLibraryTests : TranslatorTestBase
{
    [TestMethod]
    public async Task StringMembers()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        string s = "Hello, World";
        Console.WriteLine(s.Length);
        Console.WriteLine(s.Substring(7));
        Console.WriteLine(s.Substring(0, 5));
        Console.WriteLine(s.ToUpper());
        Console.WriteLine(s.ToLower());
        Console.WriteLine(s.Replace("l", "L"));
        Console.WriteLine(s.IndexOf("World"));
        Console.WriteLine(s.Contains("World"));
        Console.WriteLine(s.StartsWith("Hello"));
        Console.WriteLine(s.EndsWith("World"));
        Console.WriteLine(s[1]);
        Console.WriteLine("  trim  ".Trim() + "!");
        Console.WriteLine("a,b,c".Split(',').Length);
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task StringStaticMembers()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        Console.WriteLine(string.Format("{0} + {1} = {2}", 2, 3, 5));
        Console.WriteLine(string.Join(", ", new int[] { 1, 2, 3 }));
        Console.WriteLine(string.IsNullOrEmpty(""));
        Console.WriteLine(string.IsNullOrEmpty("x"));
        Console.WriteLine(string.Concat("a", "b", "c"));
        Console.WriteLine(string.Format("{0:D3} {1:F2}", 7, 3.14159));
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task StringBuilderUsage()
    {
        var code = """
using System;
using System.Text;
public class Program
{
    public static void Main()
    {
        var sb = new StringBuilder();
        sb.Append("x=").Append(42).AppendLine();
        sb.Append("done");
        Console.WriteLine(sb.ToString());
        Console.WriteLine(sb.Length);
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task MathMembers()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        Console.WriteLine(Math.Max(3, 7));
        Console.WriteLine(Math.Min(3, 7));
        Console.WriteLine(Math.Sqrt(16.0));
        Console.WriteLine(Math.Abs(-5));
        Console.WriteLine(Math.Round(2.5));
        Console.WriteLine(Math.Round(3.5));
        Console.WriteLine(Math.Floor(2.9));
        Console.WriteLine(Math.Ceiling(2.1));
        Console.WriteLine(Math.Pow(2, 10));
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task ParsingAndConvert()
    {
        var code = """
using System;
public class Program
{
    public static void Main()
    {
        Console.WriteLine(int.Parse("123") + 1);
        Console.WriteLine(double.Parse("3.5") * 2);
        Console.WriteLine(Convert.ToInt32("42"));
        Console.WriteLine(Convert.ToInt32(3.7));
        Console.WriteLine(char.IsDigit('5'));
        Console.WriteLine(char.IsLetter('a'));
        Console.WriteLine(char.ToUpper('x'));
    }
}
""";
        await RunTest(code);
    }
}
