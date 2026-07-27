using System.Threading.Tasks;

namespace Transpose.Newtonsoft.Json.Tests;

[TestClass]
public class SmokeTests : JsonTestBase
{
    [TestMethod]
    public async Task SimpleRoundTrip()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item { public string Name { get; set; } public int Value { get; set; } }
public class App
{
    public static void Main()
    {
        var json = JsonConvert.SerializeObject(new Item { Name = ""Hello"", Value = 42 });
        Console.WriteLine(json);
        var back = JsonConvert.DeserializeObject<Item>(json);
        Console.WriteLine(back.Name + "" "" + back.Value);
    }
}";
        await RunAndCompare(code);
    }
}
