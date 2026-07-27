using System.Threading.Tasks;

namespace Transpose.Newtonsoft.Json.Tests;

/// <summary>Arrays, lists, sets, dictionaries and the interface types they are commonly declared as.</summary>
[TestClass]
public class CollectionTests : JsonTestBase
{
    private const string Header = @"
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
public class Item { public string Name { get; set; } public int V { get; set; } }
";

    [TestMethod]
    public async Task ListsRoundTrip()
    {
        var code = Header + @"
public class App
{
    public static void Main()
    {
        var list = new List<Item> { new Item { Name = ""a"", V = 1 }, new Item { Name = ""b"", V = 2 } };
        var json = JsonConvert.SerializeObject(list);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<List<Item>>(json);
        Console.WriteLine(back.Count + ""|"" + back[0].Name + ""|"" + back[1].V);

        Console.WriteLine(JsonConvert.SerializeObject(new List<int> { 1, 2, 3 }));
        Console.WriteLine(JsonConvert.DeserializeObject<List<int>>(""[4,5,6]"").Sum());
        Console.WriteLine(JsonConvert.SerializeObject(new List<string>()));
        Console.WriteLine(JsonConvert.DeserializeObject<List<string>>(""[]"").Count);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task ArraysRoundTrip()
    {
        var code = Header + @"
public class App
{
    public static void Main()
    {
        var arr = new[] { new Item { Name = ""a"", V = 1 }, new Item { Name = ""b"", V = 2 } };
        var json = JsonConvert.SerializeObject(arr);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<Item[]>(json);
        Console.WriteLine(back.Length + ""|"" + back[0].Name + ""|"" + back[1].V);

        Console.WriteLine(JsonConvert.SerializeObject(new int[0]));
        Console.WriteLine(JsonConvert.DeserializeObject<int[]>(""[1,2,3]"").Length);
        Console.WriteLine(JsonConvert.SerializeObject(new string[] { ""x"", null }));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task JaggedArraysRoundTrip()
    {
        var code = Header + @"
public class App
{
    public static void Main()
    {
        var jagged = new int[][] { new[] { 1, 2 }, new[] { 3 }, new int[0] };
        var json = JsonConvert.SerializeObject(jagged);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<int[][]>(json);
        Console.WriteLine(back.Length + ""|"" + back[0][1] + ""|"" + back[2].Length);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task NestedCollectionsRoundTrip()
    {
        var code = Header + @"
public class Bag
{
    public Dictionary<string, List<Item>> ByGroup { get; set; }
    public List<Dictionary<string, int>>  Rows    { get; set; }
    public Dictionary<string, Item[]>     Arrays  { get; set; }
}
public class App
{
    public static void Main()
    {
        var bag = new Bag
        {
            ByGroup = new Dictionary<string, List<Item>> { [""g""] = new List<Item> { new Item { Name = ""a"", V = 1 } } },
            Rows    = new List<Dictionary<string, int>> { new Dictionary<string, int> { [""x""] = 1 } },
            Arrays  = new Dictionary<string, Item[]> { [""k""] = new[] { new Item { Name = ""b"", V = 2 } } },
        };

        var json = JsonConvert.SerializeObject(bag);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<Bag>(json);
        Console.WriteLine(back.ByGroup[""g""][0].Name + ""|"" + back.Rows[0][""x""] + ""|"" + back.Arrays[""k""][0].V);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task DictionariesWithNonStringKeysRoundTrip()
    {
        var code = Header + @"
public enum Color { Red = 0, Green = 1 }
public class App
{
    public static void Main()
    {
        var byInt = new Dictionary<int, string> { [1] = ""one"", [2] = ""two"" };
        var json  = JsonConvert.SerializeObject(byInt);
        Console.WriteLine(json);
        Console.WriteLine(JsonConvert.DeserializeObject<Dictionary<int, string>>(json)[2]);

        var byEnum = new Dictionary<Color, int> { [Color.Red] = 10, [Color.Green] = 20 };
        var enumJson = JsonConvert.SerializeObject(byEnum);
        Console.WriteLine(enumJson);
        Console.WriteLine(JsonConvert.DeserializeObject<Dictionary<Color, int>>(enumJson)[Color.Green]);

        var byGuid = new Dictionary<Guid, int> { [new Guid(""11111111-1111-1111-1111-111111111111"")] = 1 };
        Console.WriteLine(JsonConvert.SerializeObject(byGuid));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task HashSetsRoundTrip()
    {
        var code = Header + @"
public class Holder { public HashSet<string> Tags { get; set; } }
public class App
{
    public static void Main()
    {
        var holder = new Holder { Tags = new HashSet<string> { ""a"", ""b"" } };
        var json = JsonConvert.SerializeObject(holder);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<Holder>(json);
        Console.WriteLine(back.Tags.Count + ""|"" + back.Tags.Contains(""a"") + ""|"" + back.Tags.Contains(""z""));

        var set = JsonConvert.DeserializeObject<HashSet<int>>(""[1,2,2,3]"");
        Console.WriteLine(set.Count);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task CollectionInterfaceMembersRoundTrip()
    {
        var code = Header + @"
public class Holder
{
    public IList<Item>              AsIList       { get; set; }
    public ICollection<string>      AsICollection { get; set; }
    public IEnumerable<int>         AsEnumerable  { get; set; }
    public IReadOnlyList<string>    AsReadOnly    { get; set; }
    public IDictionary<string, int> AsDictionary  { get; set; }
}
public class App
{
    public static void Main()
    {
        var holder = new Holder
        {
            AsIList       = new List<Item> { new Item { Name = ""a"", V = 1 } },
            AsICollection = new List<string> { ""x"", ""y"" },
            AsEnumerable  = new List<int> { 1, 2, 3 },
            AsReadOnly    = new List<string> { ""r"" },
            AsDictionary  = new Dictionary<string, int> { [""k""] = 9 },
        };

        var json = JsonConvert.SerializeObject(holder);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<Holder>(json);
        Console.WriteLine(back.AsIList.Count + ""|"" + back.AsIList[0].Name);
        Console.WriteLine(back.AsICollection.Count + ""|"" + string.Join("","", back.AsCollectionToArray()));
        Console.WriteLine(back.AsEnumerable.Sum() + ""|"" + back.AsReadOnly[0] + ""|"" + back.AsDictionary[""k""]);
    }
}
public static class Ext
{
    public static string[] AsCollectionToArray(this Holder h) => h.AsICollection.ToArray();
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task NullAndEmptyCollectionsRoundTrip()
    {
        var code = Header + @"
public class Holder
{
    public List<Item>              Items { get; set; }
    public Dictionary<string, int> Map   { get; set; }
    public int[]                   Arr   { get; set; }
}
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Holder()));
        Console.WriteLine(JsonConvert.SerializeObject(new Holder { Items = new List<Item>(), Map = new Dictionary<string, int>(), Arr = new int[0] }));

        var back = JsonConvert.DeserializeObject<Holder>(""{\""Items\"":[],\""Map\"":{},\""Arr\"":[]}"");
        Console.WriteLine(back.Items.Count + ""|"" + back.Map.Count + ""|"" + back.Arr.Length);

        var nulls = JsonConvert.DeserializeObject<Holder>(""{\""Items\"":null,\""Map\"":null,\""Arr\"":null}"");
        Console.WriteLine((nulls.Items == null) + ""|"" + (nulls.Map == null) + ""|"" + (nulls.Arr == null));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task CollectionsWithNullElementsRoundTrip()
    {
        var code = Header + @"
public class App
{
    public static void Main()
    {
        var list = new List<Item> { new Item { Name = ""a"", V = 1 }, null };
        var json = JsonConvert.SerializeObject(list);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<List<Item>>(json);
        Console.WriteLine(back.Count + ""|"" + (back[1] == null));

        var map = new Dictionary<string, Item> { [""a""] = null };
        Console.WriteLine(JsonConvert.SerializeObject(map));
        Console.WriteLine(JsonConvert.DeserializeObject<Dictionary<string, Item>>(""{\""a\"":null}"")[""a""] == null);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task ListOfObjectHoldsMixedValues()
    {
        var code = Header + @"
public class App
{
    public static void Main()
    {
        var back = JsonConvert.DeserializeObject<List<object>>(""[1,\""two\"",true,null]"");
        Console.WriteLine(back.Count + ""|"" + (back[3] == null));
        Console.WriteLine(JsonConvert.SerializeObject(back));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task DictionaryOfObjectValuesRoundTrips()
    {
        var code = Header + @"
public class App
{
    public static void Main()
    {
        var map = JsonConvert.DeserializeObject<Dictionary<string, object>>(""{\""n\"":1,\""s\"":\""x\"",\""b\"":true,\""nul\"":null}"");
        Console.WriteLine(map.Count + ""|"" + map[""n""] + ""|"" + (map[""nul""] == null));
        Console.WriteLine(JsonConvert.SerializeObject(map));
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// A pre-populated collection member: Json.NET's default ObjectCreationHandling.Auto reuses the
    /// instance the constructor created and <b>appends</b> to it, so the seeded entries survive.
    /// </summary>
    [TestMethod]
    public async Task ExistingCollectionMemberIsAppendedTo()
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
        var back = JsonConvert.DeserializeObject<Holder>(""{\""Items\"":[\""a\"",\""b\""]}"");
        Console.WriteLine(back.Items.Count + ""|"" + string.Join("","", back.Items));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task ArrayOfEnumsAndNullablesRoundTrip()
    {
        var code = Header + @"
public enum Color { Red = 0, Green = 1, Blue = 7 }
public class Holder { public Color[] Colors { get; set; } public int?[] Numbers { get; set; } }
public class App
{
    public static void Main()
    {
        var holder = new Holder { Colors = new[] { Color.Blue, Color.Red }, Numbers = new int?[] { 1, null, 3 } };
        var json = JsonConvert.SerializeObject(holder);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<Holder>(json);
        Console.WriteLine(back.Colors.Length + ""|"" + (back.Colors[0] == Color.Blue) + ""|"" + back.Numbers.Length + ""|"" + (back.Numbers[1] == null));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task LinqOverADeserializedCollectionWorks()
    {
        var code = Header + @"
public class App
{
    public static void Main()
    {
        var items = JsonConvert.DeserializeObject<List<Item>>(""[{\""Name\"":\""a\"",\""V\"":3},{\""Name\"":\""b\"",\""V\"":1},{\""Name\"":\""c\"",\""V\"":2}]"");
        Console.WriteLine(string.Join("","", items.OrderBy(i => i.V).Select(i => i.Name)));
        Console.WriteLine(items.Sum(i => i.V) + ""|"" + items.First(i => i.V == 2).Name);
    }
}";
        await RunAndCompare(code);
    }
}
