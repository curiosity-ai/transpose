using System.Threading.Tasks;

namespace Transpose.Newtonsoft.Json.Tests;

/// <summary><c>JsonConvert.PopulateObject</c> — merging JSON into an already-constructed instance.</summary>
[TestClass]
public class PopulateObjectTests : JsonTestBase
{
    private const string Header = @"
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
";

    [TestMethod]
    public async Task PopulatesMembersAndLeavesTheRestAlone()
    {
        var code = Header + @"
public class Item
{
    public string Name  { get; set; }
    public int    Value { get; set; }
    public bool   Flag  { get; set; }
}
public class App
{
    public static void Main()
    {
        var item = new Item { Name = ""original"", Value = 1, Flag = true };
        JsonConvert.PopulateObject(""{\""Value\"":42}"", item);
        Console.WriteLine(item.Name + ""|"" + item.Value + ""|"" + item.Flag);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task PopulatesNestedObjectsInPlace()
    {
        var code = Header + @"
public class Inner { public int A { get; set; } public int B { get; set; } }
public class Outer { public Inner Child { get; set; } public string Name { get; set; } }
public class App
{
    public static void Main()
    {
        var outer = new Outer { Child = new Inner { A = 1, B = 2 }, Name = ""n"" };
        JsonConvert.PopulateObject(""{\""Child\"":{\""B\"":20}}"", outer);
        Console.WriteLine(outer.Name + ""|"" + outer.Child.A + ""|"" + outer.Child.B);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task PopulatesAList()
    {
        var code = Header + @"
public class App
{
    public static void Main()
    {
        var list = new List<string> { ""seed"" };
        JsonConvert.PopulateObject(""[\""a\"",\""b\""]"", list);
        Console.WriteLine(list.Count + ""|"" + string.Join("","", list));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task PopulatesADictionary()
    {
        var code = Header + @"
public class App
{
    public static void Main()
    {
        var map = new Dictionary<string, int> { [""seed""] = 1 };
        JsonConvert.PopulateObject(""{\""a\"":2,\""seed\"":9}"", map);
        Console.WriteLine(map.Count + ""|"" + map[""a""] + ""|"" + map[""seed""]);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task PopulatesWithSettings()
    {
        var code = Header + @"
public class Item
{
    [JsonProperty(""n"")] public string Name  { get; set; }
    [JsonProperty(""v"")] public int    Value { get; set; }
}
public class App
{
    public static void Main()
    {
        var item = new Item { Name = ""original"", Value = 1 };
        JsonConvert.PopulateObject(""{\""v\"":7}"", item, new JsonSerializerSettings());
        Console.WriteLine(item.Name + ""|"" + item.Value);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task PopulatingWithAnEmptyDocumentChangesNothing()
    {
        var code = Header + @"
public class Item { public string Name { get; set; } public int Value { get; set; } }
public class App
{
    public static void Main()
    {
        var item = new Item { Name = ""a"", Value = 1 };
        JsonConvert.PopulateObject(""{}"", item);
        Console.WriteLine(item.Name + ""|"" + item.Value);
    }
}";
        await RunAndCompare(code);
    }
}
