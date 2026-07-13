using System.Threading.Tasks;

namespace H5.Translator.Roslyn.Tests;

[TestClass]
public class EnumTests : TranslatorTestBase
{
    [TestMethod]
    public async Task EnumValuesAndCasts()
    {
        var code = """
using System;

public enum Color { Red, Green = 5, Blue }

public class Program
{
    public static void Main()
    {
        Console.WriteLine((int)Color.Red);
        Console.WriteLine((int)Color.Green);
        Console.WriteLine((int)Color.Blue);
        Color c = Color.Blue;
        Console.WriteLine((int)c);
        if (c == Color.Blue) { Console.WriteLine("is blue"); }
    }
}
""";
        await RunTest(code);
    }
}
