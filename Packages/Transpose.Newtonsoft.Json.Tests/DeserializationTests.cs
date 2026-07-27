using System.Threading.Tasks;

namespace Transpose.Newtonsoft.Json.Tests;

/// <summary><c>JsonConvert.DeserializeObject</c> — how JSON is mapped back onto C# types.</summary>
[TestClass]
public class DeserializationTests : JsonTestBase
{
    private const string Item = @"
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
public class Item
{
    public string Name  { get; set; }
    public int    Value { get; set; }
    public bool   Flag  { get; set; }
}
";

    [TestMethod]
    public async Task MembersMissingFromJsonKeepTheirDefaults()
    {
        var code = Item + @"
public class App
{
    public static void Main()
    {
        var item = JsonConvert.DeserializeObject<Item>(""{\""Name\"":\""a\""}"");
        Console.WriteLine(item.Name + ""|"" + item.Value + ""|"" + item.Flag);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task UnknownMembersAreIgnored()
    {
        var code = Item + @"
public class App
{
    public static void Main()
    {
        var item = JsonConvert.DeserializeObject<Item>(""{\""Name\"":\""a\"",\""Nope\"":123,\""Deep\"":{\""x\"":1}}"");
        Console.WriteLine(item.Name + ""|"" + item.Value);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task MemberNamesMatchCaseInsensitively()
    {
        var code = Item + @"
public class App
{
    public static void Main()
    {
        var item = JsonConvert.DeserializeObject<Item>(""{\""name\"":\""a\"",\""VALUE\"":3,\""flag\"":true}"");
        Console.WriteLine(item.Name + ""|"" + item.Value + ""|"" + item.Flag);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task WhitespaceAndIndentedInputParses()
    {
        var code = Item + @"
public class App
{
    public static void Main()
    {
        var item = JsonConvert.DeserializeObject<Item>(""{\n  \""Name\"" : \""a\"" ,\n  \""Value\"" : 5\n}"");
        Console.WriteLine(item.Name + ""|"" + item.Value);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task NestedObjectsDeserialize()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Inner { public int V { get; set; } }
public class Outer { public Inner Child { get; set; } public Inner Missing { get; set; } }
public class App
{
    public static void Main()
    {
        var o = JsonConvert.DeserializeObject<Outer>(""{\""Child\"":{\""V\"":7}}"");
        Console.WriteLine(o.Child.V + ""|"" + (o.Missing == null));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task NullsDeserializeIntoDefaults()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item
{
    public string Name  { get; set; }
    public int?   Maybe { get; set; }
    public Item   Child { get; set; }
}
public class App
{
    public static void Main()
    {
        var item = JsonConvert.DeserializeObject<Item>(""{\""Name\"":null,\""Maybe\"":null,\""Child\"":null}"");
        Console.WriteLine((item.Name == null) + ""|"" + (item.Maybe == null) + ""|"" + (item.Child == null));
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// A JSON null for a non-nullable value-type member: Json.NET refuses it with a
    /// JsonSerializationException ("Null object cannot be converted to a value type"), the binding
    /// library quietly leaves the member at its default. A server that emits null for an int is
    /// therefore a hard error on the .NET side and silent on the client.
    /// </summary>
    [TestMethod]
    public async Task NullIntoANonNullableValueTypeIsIgnoredInsteadOfThrowing()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item { public int Value { get; set; } public bool Flag { get; set; } }
public class App
{
    public static void Main()
    {
        try
        {
            var item = JsonConvert.DeserializeObject<Item>(""{\""Value\"":null,\""Flag\"":null}"");
            Console.WriteLine(item.Value + ""|"" + item.Flag);
        }
        catch (Exception ex) { Console.WriteLine(ex.GetType().Name); }
    }
}";
        await RunJs(code,
            expected: "0|False",
            nativePrints: "JsonSerializationException");
    }

    [TestMethod]
    public async Task NumericTypesDeserialize()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Nums
{
    public byte    B  { get; set; }
    public short   S  { get; set; }
    public int     I  { get; set; }
    public long    L  { get; set; }
    public float   F  { get; set; }
    public double  D  { get; set; }
    public decimal M  { get; set; }
    public uint    UI { get; set; }
}
public class App
{
    public static void Main()
    {
        var json = ""{\""B\"":200,\""S\"":-30000,\""I\"":-2147483648,\""L\"":9007199254740991,\""F\"":1.5,\""D\"":-0.25,\""M\"":12.34,\""UI\"":4294967295}"";
        var n = JsonConvert.DeserializeObject<Nums>(json);
        Console.WriteLine(n.B + ""|"" + n.S + ""|"" + n.I + ""|"" + n.L);
        Console.WriteLine(n.F + ""|"" + n.D + ""|"" + n.M + ""|"" + n.UI);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task NumbersWrittenAsStringsAreConverted()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Nums { public int I { get; set; } public double D { get; set; } public long L { get; set; } public bool Flag { get; set; } }
public class App
{
    public static void Main()
    {
        var n = JsonConvert.DeserializeObject<Nums>(""{\""I\"":\""42\"",\""D\"":\""1.25\"",\""L\"":\""99\"",\""Flag\"":\""true\""}"");
        Console.WriteLine(n.I + ""|"" + n.D + ""|"" + n.L + ""|"" + n.Flag);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task NonGenericOverloadWithTypeArgument()
    {
        var code = Item + @"
public class App
{
    public static void Main()
    {
        var item = (Item)JsonConvert.DeserializeObject(""{\""Name\"":\""a\"",\""Value\"":2}"", typeof(Item));
        Console.WriteLine(item.Name + ""|"" + item.Value);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task AnonymousTypeDeserialization()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class App
{
    public static void Main()
    {
        var shape = new { Name = """", Value = 0 };
        var v = JsonConvert.DeserializeAnonymousType(""{\""Name\"":\""a\"",\""Value\"":9}"", shape);
        Console.WriteLine(v.Name + ""|"" + v.Value);
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// A property with a private setter: Json.NET only writes to it when the property opts in with
    /// [JsonProperty], so by default it stays null; the binding library writes to it regardless
    /// (the setter is in the type's reflection metadata either way).
    /// </summary>
    [TestMethod]
    public async Task PrivateSetterIsPopulatedUnlikeJsonNet()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item
{
    public string Name { get; private set; }
    public int    Value { get; set; }
}
public class App
{
    public static void Main()
    {
        var item = JsonConvert.DeserializeObject<Item>(""{\""Name\"":\""a\"",\""Value\"":1}"");
        Console.WriteLine((item.Name ?? ""<null>"") + ""|"" + item.Value);
    }
}";
        await RunJs(code,
            expected: "a|1",
            nativePrints: "<null>|1");
    }

    [TestMethod]
    public async Task GetOnlyPropertyIsLeftAlone()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item
{
    public string Name  { get; set; }
    public int    Length => Name == null ? -1 : Name.Length;
}
public class App
{
    public static void Main()
    {
        var item = JsonConvert.DeserializeObject<Item>(""{\""Name\"":\""abc\"",\""Length\"":99}"");
        Console.WriteLine(item.Name + ""|"" + item.Length);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task SingleParameterizedConstructorIsUsed()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item
{
    public Item(string name, int value) { Name = name; Value = value; }
    public string Name  { get; }
    public int    Value { get; }
}
public class App
{
    public static void Main()
    {
        var item = JsonConvert.DeserializeObject<Item>(""{\""name\"":\""a\"",\""value\"":4}"");
        Console.WriteLine(item.Name + ""|"" + item.Value);

        // The JSON member names match the *parameter* names case-insensitively.
        var pascal = JsonConvert.DeserializeObject<Item>(""{\""Name\"":\""b\"",\""Value\"":5}"");
        Console.WriteLine(pascal.Name + ""|"" + pascal.Value);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task ConstructorParameterMissingFromJsonGetsItsDefault()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item
{
    public Item(string name, int value) { Name = name; Value = value; }
    public string Name  { get; }
    public int    Value { get; }
}
public class App
{
    public static void Main()
    {
        var item = JsonConvert.DeserializeObject<Item>(""{\""Name\"":\""a\""}"");
        Console.WriteLine((item.Name ?? ""<null>"") + ""|"" + item.Value);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task StructsDeserialize()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public struct Point { public int X { get; set; } public int Y { get; set; } }
public class Holder { public Point P { get; set; } public Point? Q { get; set; } }
public class App
{
    public static void Main()
    {
        var p = JsonConvert.DeserializeObject<Point>(""{\""X\"":1,\""Y\"":2}"");
        Console.WriteLine(p.X + ""|"" + p.Y);

        var h = JsonConvert.DeserializeObject<Holder>(""{\""P\"":{\""X\"":3,\""Y\"":4},\""Q\"":{\""X\"":5,\""Y\"":6}}"");
        Console.WriteLine(h.P.X + ""|"" + h.P.Y + ""|"" + h.Q.Value.X + ""|"" + h.Q.Value.Y);

        var empty = JsonConvert.DeserializeObject<Holder>(""{}"");
        Console.WriteLine(empty.P.X + ""|"" + (empty.Q == null));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task EnumsDeserializeFromNumberAndName()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public enum Color { Red = 0, Green = 1, Blue = 7 }
public class Item { public Color C { get; set; } public Color? Maybe { get; set; } }
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(""{\""C\"":7}"").C);
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(""{\""C\"":\""Green\""}"").C);
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(""{\""C\"":\""Blue\""}"").C == Color.Blue);
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(""{\""Maybe\"":1}"").Maybe == Color.Green);
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(""{\""Maybe\"":null}"").Maybe == null);
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// A deserialized <c>Nullable&lt;TEnum&gt;</c> holds a bare number rather than a boxed enum, so
    /// printing it (or otherwise calling ToString on the boxed value) yields the numeric value where
    /// Json.NET yields the member name. Comparing it against an enum member is unaffected — only
    /// the string conversion differs.
    /// </summary>
    [TestMethod]
    public async Task NullableEnumPrintsItsNumberInsteadOfItsName()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public enum Color { Red = 0, Green = 1, Blue = 7 }
public class Item { public Color? Maybe { get; set; } }
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(""{\""Maybe\"":1}"").Maybe);
    }
}";
        await RunJs(code,
            expected: "1",
            nativePrints: "Green");
    }

    [TestMethod]
    public async Task RootLevelPrimitivesDeserialize()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.DeserializeObject<int>(""42""));
        Console.WriteLine(JsonConvert.DeserializeObject<string>(""\""text\""""));
        Console.WriteLine(JsonConvert.DeserializeObject<bool>(""true""));
        Console.WriteLine(JsonConvert.DeserializeObject<double>(""1.5""));
        Console.WriteLine(JsonConvert.DeserializeObject<string>(""null"") == null);
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// Deserializing to <c>object</c>: Json.NET materialises the Linq-to-JSON model (a
    /// <c>JObject</c>), which the binding library does not implement — it hands back the raw parsed
    /// JavaScript value. A JSON string is handed back with its quotes still on and a JSON boolean
    /// stringifies JavaScript-style ("true"), so front-end code that wants a loose bag should
    /// deserialize to <c>Dictionary&lt;string, object&gt;</c> and read typed members out of it
    /// rather than printing an <c>object</c> straight.
    /// </summary>
    [TestMethod]
    public async Task DeserializingToObjectReturnsTheRawParsedValue()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class App
{
    public static void Main()
    {
        object o = JsonConvert.DeserializeObject<object>(""{\""a\"":1}"");
        Console.WriteLine(o.GetType().Name);
        Console.WriteLine(JsonConvert.DeserializeObject<object>(""42""));
        Console.WriteLine(JsonConvert.DeserializeObject<object>(""\""s\""""));
        Console.WriteLine(JsonConvert.DeserializeObject<object>(""true""));
    }
}";
        await RunJs(code,
            expected: "Object\n42\n\"s\"\ntrue",
            nativePrints: "JObject\n42\ns\nTrue");
    }

    /// <summary>
    /// An empty or whitespace-only document deserializes to the target's default rather than
    /// throwing — the case a front-end hits reading a local-storage slot that was never written.
    /// (It used to throw a JsonException out of <c>JSON.parse("")</c>.)
    /// </summary>
    [TestMethod]
    public async Task EmptyInputReturnsTheDefault()
    {
        var code = Item + @"
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.DeserializeObject<Item>("""") == null);
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(""   "") == null);
        Console.WriteLine(JsonConvert.DeserializeObject<List<string>>("""") == null);
        Console.WriteLine(JsonConvert.DeserializeObject<int?>("""") == null);
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// …with one difference for a non-nullable value type: Json.NET refuses ("No JSON content found
    /// and type 'System.Int32' is not nullable"), the binding library returns the type's default.
    /// </summary>
    [TestMethod]
    public async Task EmptyInputForAValueTypeReturnsZeroInsteadOfThrowing()
    {
        var code = Item + @"
public class App
{
    public static void Main()
    {
        try   { Console.WriteLine(JsonConvert.DeserializeObject<int>("""")); }
        catch (Exception ex) { Console.WriteLine(ex.GetType().Name); }
    }
}";
        await RunJs(code,
            expected: "0",
            nativePrints: "JsonSerializationException");
    }

    [TestMethod]
    public async Task DeserializeThenReserializeRoundTripsAPopulatedGraph()
    {
        var code = @"
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
public class Address { public string City { get; set; } public string Zip { get; set; } }
public class Person
{
    public string             Name      { get; set; }
    public int                Age       { get; set; }
    public Address            Home      { get; set; }
    public List<string>       Nicknames { get; set; }
    public Dictionary<string, int> Scores { get; set; }
}
public class App
{
    public static void Main()
    {
        var person = new Person
        {
            Name = ""Ada"",
            Age = 36,
            Home = new Address { City = ""London"", Zip = ""NW1"" },
            Nicknames = new List<string> { ""A"", ""Countess"" },
            Scores = new Dictionary<string, int> { [""math""] = 10, [""logic""] = 9 },
        };

        var json  = JsonConvert.SerializeObject(person);
        var back  = JsonConvert.DeserializeObject<Person>(json);
        var again = JsonConvert.SerializeObject(back);

        Console.WriteLine(json == again);
        Console.WriteLine(back.Name + ""|"" + back.Age + ""|"" + back.Home.City + ""|"" + back.Home.Zip);
        Console.WriteLine(back.Nicknames.Count + ""|"" + back.Nicknames[1] + ""|"" + back.Scores[""math""]);
    }
}";
        await RunAndCompare(code);
    }
}
