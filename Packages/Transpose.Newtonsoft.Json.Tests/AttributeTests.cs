using System.Threading.Tasks;

namespace Transpose.Newtonsoft.Json.Tests;

/// <summary>
/// The attributes the binding library understands: [JsonProperty] (name, order, null/default/required
/// handling), [JsonIgnore], [JsonConstructor], [DefaultValue] and the
/// <c>System.Runtime.Serialization</c> serialization callbacks.
/// </summary>
[TestClass]
public class AttributeTests : JsonTestBase
{
    [TestMethod]
    public async Task JsonPropertyRenamesOnBothSides()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item
{
    [JsonProperty(""K"")]                    public string Key   { get; set; }
    [JsonProperty(PropertyName = ""V"")]     public string Value { get; set; }
    [JsonProperty(""F"")]                    public int    Field;
}
public class App
{
    public static void Main()
    {
        var json = JsonConvert.SerializeObject(new Item { Key = ""k"", Value = ""v"", Field = 3 });
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<Item>(json);
        Console.WriteLine(back.Key + ""|"" + back.Value + ""|"" + back.Field);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task JsonIgnoreSkipsMembersOnBothSides()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item
{
    public string Kept    { get; set; }
    [JsonIgnore] public string Skipped { get; set; }
    [JsonIgnore] public int    SkippedField;
}
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Item { Kept = ""a"", Skipped = ""b"", SkippedField = 1 }));

        var back = JsonConvert.DeserializeObject<Item>(""{\""Kept\"":\""a\"",\""Skipped\"":\""b\"",\""SkippedField\"":9}"");
        Console.WriteLine(back.Kept + ""|"" + (back.Skipped ?? ""<null>"") + ""|"" + back.SkippedField);
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// An internal member is invisible to both serializers unless it opts in with [JsonProperty] —
    /// the pattern the Curiosity library's <c>Edge</c> / <c>QueryResults</c> DTOs use to keep their
    /// wire names short while the members stay internal.
    /// </summary>
    [TestMethod]
    public async Task InternalMemberWithJsonPropertyIsSerialized()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Edge
{
    [JsonProperty(""N"")] internal string NodeType { get; set; }
    [JsonProperty(""U"")] internal string Uid      { get; set; }
    internal string                      Hidden   { get; set; }

    public static Edge Make() => new Edge { NodeType = ""Person"", Uid = ""abc"", Hidden = ""x"" };
    public string Describe() => NodeType + ""/"" + Uid + ""/"" + (Hidden ?? ""<null>"");
}
public class App
{
    public static void Main()
    {
        var json = JsonConvert.SerializeObject(Edge.Make());
        Console.WriteLine(json);
        Console.WriteLine(JsonConvert.DeserializeObject<Edge>(json).Describe());
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task JsonPropertyOrderControlsMemberOrder()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item
{
    [JsonProperty(Order = 3)] public string Third  { get; set; }
    [JsonProperty(Order = 1)] public string First  { get; set; }
    [JsonProperty(Order = 2)] public string Second { get; set; }
}
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Item { First = ""1"", Second = ""2"", Third = ""3"" }));
    }
}";
        await RunAndCompare(code, exactMemberOrder: true);
    }

    [TestMethod]
    public async Task JsonPropertyNullValueHandlingIgnoreDropsNulls()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public string Optional { get; set; }
    public string Always { get; set; }
}
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Item()));
        Console.WriteLine(JsonConvert.SerializeObject(new Item { Optional = ""set"", Always = ""a"" }));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task RequiredAlwaysRejectsMissingAndNullMembers()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item
{
    [JsonProperty(Required = Required.Always)] public string Name { get; set; }
    public int Value { get; set; }
}
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(""{\""Name\"":\""a\"",\""Value\"":1}"").Name);

        try   { JsonConvert.DeserializeObject<Item>(""{\""Value\"":1}""); Console.WriteLine(""no throw (missing)""); }
        catch (JsonSerializationException) { Console.WriteLine(""threw (missing)""); }

        try   { JsonConvert.DeserializeObject<Item>(""{\""Name\"":null}""); Console.WriteLine(""no throw (null)""); }
        catch (JsonSerializationException) { Console.WriteLine(""threw (null)""); }
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task RequiredDisallowNullRejectsOnlyNull()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item
{
    [JsonProperty(Required = Required.DisallowNull)] public string Name { get; set; }
}
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(""{}"").Name == null);

        try   { JsonConvert.DeserializeObject<Item>(""{\""Name\"":null}""); Console.WriteLine(""no throw""); }
        catch (JsonSerializationException) { Console.WriteLine(""threw""); }
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task RequiredAllowNullRejectsOnlyMissing()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item
{
    [JsonProperty(Required = Required.AllowNull)] public string Name { get; set; }
}
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(""{\""Name\"":null}"").Name == null);

        try   { JsonConvert.DeserializeObject<Item>(""{}""); Console.WriteLine(""no throw""); }
        catch (JsonSerializationException) { Console.WriteLine(""threw""); }
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// [JsonConstructor] picks the constructor to deserialize through when a type has several — the
    /// shape <c>SchemaDefinition</c> uses in the Curiosity front-end (four public constructors, one
    /// of them marked).
    /// </summary>
    [TestMethod]
    public async Task JsonConstructorSelectsTheConstructorToUse()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class SchemaDefinition
{
    public SchemaDefinition(string name, string type) : this(name, type, """") { }

    [JsonConstructor]
    public SchemaDefinition(string name, string type, string key)
    {
        Name = name;
        Type = type;
        Key  = key;
    }

    public string Name { get; }
    public string Type { get; }
    public string Key  { get; }
}
public class App
{
    public static void Main()
    {
        var json = JsonConvert.SerializeObject(new SchemaDefinition(""n"", ""t"", ""k""));
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<SchemaDefinition>(json);
        Console.WriteLine(back.Name + ""|"" + back.Type + ""|"" + back.Key);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task DefaultValueHandlingIgnoreSkipsDefaults()
    {
        var code = @"
using System;
using System.ComponentModel;
using Newtonsoft.Json;
public class Item
{
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)] public int  Count { get; set; }
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)] public bool Flag  { get; set; }
    [DefaultValue(7)]
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)] public int  Seven { get; set; }
}
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Item { Seven = 7 }));
        Console.WriteLine(JsonConvert.SerializeObject(new Item { Count = 1, Flag = true, Seven = 8 }));
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// A null is the default of a reference or nullable member, so DefaultValueHandling.Ignore drops
    /// it too. (It used to be written regardless: <c>preProcess</c>'s "is this the default value"
    /// guard short-circuited whenever either side was null.)
    /// </summary>
    [TestMethod]
    public async Task DefaultValueHandlingIgnoreDropsNullMembers()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item
{
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)] public string Name  { get; set; }
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)] public int?   Maybe { get; set; }
}
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Item()));
        Console.WriteLine(JsonConvert.SerializeObject(new Item { Name = ""n"", Maybe = 1 }));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task DefaultValueHandlingPopulateFillsMissingMembers()
    {
        var code = @"
using System;
using System.ComponentModel;
using Newtonsoft.Json;
public class Item
{
    [DefaultValue(42)]
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)] public int    Count { get; set; }
    [DefaultValue(""fallback"")]
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)] public string Name  { get; set; }
}
public class App
{
    public static void Main()
    {
        var back = JsonConvert.DeserializeObject<Item>(""{}"");
        Console.WriteLine(back.Count + ""|"" + back.Name);

        var given = JsonConvert.DeserializeObject<Item>(""{\""Count\"":1,\""Name\"":\""n\""}"");
        Console.WriteLine(given.Count + ""|"" + given.Name);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task SerializationCallbacksRun()
    {
        var code = @"
using System;
using System.Runtime.Serialization;
using Newtonsoft.Json;
public class Item
{
    public string Name { get; set; }
    [JsonIgnore] public string Trace { get; set; } = """";

    [OnSerializing]   internal void OnSerializing(StreamingContext c)   { Console.WriteLine(""onSerializing""); }
    [OnSerialized]    internal void OnSerialized(StreamingContext c)    { Console.WriteLine(""onSerialized""); }
    [OnDeserializing] internal void OnDeserializing(StreamingContext c) { Console.WriteLine(""onDeserializing""); }
    [OnDeserialized]  internal void OnDeserialized(StreamingContext c)  { Console.WriteLine(""onDeserialized: "" + Name); }
}
public class App
{
    public static void Main()
    {
        var json = JsonConvert.SerializeObject(new Item { Name = ""a"" });
        Console.WriteLine(json);
        JsonConvert.DeserializeObject<Item>(json);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task JsonPropertyOnAFieldRenamesIt()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item
{
    [JsonProperty(""n"")] public string Name;
    [JsonProperty(""v"")] public int    Value;
}
public class App
{
    public static void Main()
    {
        var json = JsonConvert.SerializeObject(new Item { Name = ""a"", Value = 1 });
        Console.WriteLine(json);
        var back = JsonConvert.DeserializeObject<Item>(json);
        Console.WriteLine(back.Name + ""|"" + back.Value);
    }
}";
        await RunAndCompare(code);
    }
}
