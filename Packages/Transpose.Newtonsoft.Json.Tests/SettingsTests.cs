using System.Threading.Tasks;

namespace Transpose.Newtonsoft.Json.Tests;

/// <summary>
/// <c>JsonSerializerSettings</c>: null/default handling, the camel-case contract resolver, formatting
/// and object-creation handling. (TypeNameHandling and SerializationBinder have their own class.)
/// </summary>
[TestClass]
public class SettingsTests : JsonTestBase
{
    private const string Header = @"
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
";

    [TestMethod]
    public async Task NullValueHandlingIgnoreDropsNullMembers()
    {
        var code = Header + @"
public class Item
{
    public string             Name  { get; set; }
    public int?               Maybe { get; set; }
    public List<string>       Items { get; set; }
    public int                Value { get; set; }
}
public class App
{
    public static void Main()
    {
        var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
        Console.WriteLine(JsonConvert.SerializeObject(new Item(), settings));
        Console.WriteLine(JsonConvert.SerializeObject(new Item { Name = ""a"", Maybe = 1, Items = new List<string>(), Value = 2 }, settings));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task DefaultValueHandlingIgnoreOnTheSettingsDropsDefaults()
    {
        var code = Header + @"
using System.ComponentModel;
public class Item
{
    public int    Count { get; set; }
    public bool   Flag  { get; set; }
    [DefaultValue(3)] public int Three { get; set; }
}
public class App
{
    public static void Main()
    {
        var settings = new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Ignore };
        Console.WriteLine(JsonConvert.SerializeObject(new Item { Three = 3 }, settings));
        Console.WriteLine(JsonConvert.SerializeObject(new Item { Count = 1, Flag = true, Three = 4 }, settings));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task CamelCaseContractResolverRenamesOnBothSides()
    {
        var code = Header + @"
public class Inner { public int InnerValue { get; set; } }
public class Item
{
    public string FirstName { get; set; }
    public int    ItemCount { get; set; }
    public Inner  ChildNode { get; set; }
}
public class App
{
    public static void Main()
    {
        var settings = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        var json = JsonConvert.SerializeObject(new Item { FirstName = ""a"", ItemCount = 2, ChildNode = new Inner { InnerValue = 3 } }, settings);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<Item>(json, settings);
        Console.WriteLine(back.FirstName + ""|"" + back.ItemCount + ""|"" + back.ChildNode.InnerValue);
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// The camel-case resolver lowercases a leading run of capitals the way Json.NET's does
    /// ("ALLCAPS" → "allcaps", "HTTPRequest" → "httpRequest"), not just the first character — which
    /// is what a camel-casing .NET server on the other end expects.
    /// </summary>
    [TestMethod]
    public async Task CamelCaseResolverLowercasesALeadingRunOfCapitals()
    {
        var code = Header + @"
public class Item
{
    public string ALLCAPS     { get; set; }
    public string HTTPRequest { get; set; }
}
public class App
{
    public static void Main()
    {
        var settings = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        var json = JsonConvert.SerializeObject(new Item { ALLCAPS = ""x"", HTTPRequest = ""y"" }, settings);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<Item>(json, settings);
        Console.WriteLine(back.ALLCAPS + ""|"" + back.HTTPRequest);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task FormattingIndentedThroughSettingsOverload()
    {
        var code = Header + @"
public class Item { public string Name { get; set; } public int Value { get; set; } }
public class App
{
    public static void Main()
    {
        var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
        Console.WriteLine(JsonConvert.SerializeObject(new Item { Name = ""a"", Value = 1 }, Formatting.Indented, settings));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task NullSettingsBehaveLikeTheDefaults()
    {
        var code = Header + @"
public class Item { public string Name { get; set; } public int Value { get; set; } }
public class App
{
    public static void Main()
    {
        JsonSerializerSettings settings = null;
        Console.WriteLine(JsonConvert.SerializeObject(new Item { Value = 1 }, settings));
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(""{\""Name\"":\""a\""}"", settings).Name);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task SettingsAreReusableAcrossCalls()
    {
        var code = Header + @"
public class Item { public string Name { get; set; } public string Other { get; set; } }
public class App
{
    static readonly JsonSerializerSettings Settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

    public static void Main()
    {
        for (var i = 0; i < 3; i++)
        {
            Console.WriteLine(JsonConvert.SerializeObject(new Item { Name = ""n"" + i }, Settings));
        }
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// ObjectCreationHandling.Replace makes the deserializer build a fresh collection instead of
    /// appending to the one the constructor seeded (the default, Auto, reuses it).
    /// </summary>
    [TestMethod]
    public async Task ObjectCreationHandlingReplaceDiscardsSeededValues()
    {
        var code = Header + @"
public class Holder
{
    public List<string> Items { get; set; } = new List<string> { ""seed"" };
}
public class App
{
    public static void Main()
    {
        var auto    = JsonConvert.DeserializeObject<Holder>(""{\""Items\"":[\""a\""]}"");
        var replace = JsonConvert.DeserializeObject<Holder>(""{\""Items\"":[\""a\""]}"",
            new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });

        Console.WriteLine(string.Join("","", auto.Items));
        Console.WriteLine(string.Join("","", replace.Items));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task PerPropertyObjectCreationHandlingReplaceWins()
    {
        var code = Header + @"
public class Holder
{
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<string> Replaced { get; set; } = new List<string> { ""seed"" };

    public List<string> Reused { get; set; } = new List<string> { ""seed"" };
}
public class App
{
    public static void Main()
    {
        var back = JsonConvert.DeserializeObject<Holder>(""{\""Replaced\"":[\""a\""],\""Reused\"":[\""a\""]}"");
        Console.WriteLine(string.Join("","", back.Replaced));
        Console.WriteLine(string.Join("","", back.Reused));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task NestedObjectsInheritTheSettings()
    {
        var code = Header + @"
public class Inner { public string A { get; set; } public string B { get; set; } }
public class Outer { public Inner Child { get; set; } public string Name { get; set; } }
public class App
{
    public static void Main()
    {
        var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
        Console.WriteLine(JsonConvert.SerializeObject(new Outer { Child = new Inner { A = ""a"" } }, settings));
    }
}";
        await RunAndCompare(code);
    }
}
