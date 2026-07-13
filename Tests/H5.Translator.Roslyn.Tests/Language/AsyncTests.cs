using System.Threading.Tasks;

namespace H5.Translator.Roslyn.Tests;

[TestClass]
public class AsyncTests : TranslatorTestBase
{
    [TestMethod]
    public async Task AwaitAndReturn()
    {
        var code = """
using System;
using System.Threading.Tasks;
public class Program
{
    static async Task<int> AddAsync(int a, int b)
    {
        await Task.Delay(5);
        return a + b;
    }
    static async Task Main()
    {
        int r = await AddAsync(3, 4);
        Console.WriteLine("r=" + r);
        var fromResult = await Task.FromResult(99);
        Console.WriteLine("fromResult=" + fromResult);
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task WhenAllAndLoop()
    {
        var code = """
using System;
using System.Threading.Tasks;
public class Program
{
    static async Task<int> SquareAsync(int x)
    {
        await Task.Delay(1);
        return x * x;
    }
    static async Task Main()
    {
        var results = await Task.WhenAll(SquareAsync(2), SquareAsync(3), SquareAsync(4));
        Console.WriteLine(string.Join(",", results));
        int total = 0;
        for (int i = 1; i <= 5; i++) total += await SquareAsync(i);
        Console.WriteLine("total=" + total);
    }
}
""";
        await RunTest(code);
    }

    [TestMethod]
    public async Task AsyncException()
    {
        var code = """
using System;
using System.Threading.Tasks;
public class Program
{
    static async Task ThrowsAsync()
    {
        await Task.Delay(1);
        throw new InvalidOperationException("boom");
    }
    static async Task Main()
    {
        try { await ThrowsAsync(); }
        catch (InvalidOperationException ex) { Console.WriteLine("caught: " + ex.Message); }
    }
}
""";
        await RunTest(code);
    }
}
