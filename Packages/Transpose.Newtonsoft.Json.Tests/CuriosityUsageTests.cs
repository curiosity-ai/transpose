using System.Threading.Tasks;

namespace Transpose.Newtonsoft.Json.Tests;

/// <summary>
/// The shapes the Curiosity front-end (the <c>mosaik</c> repository) actually puts through this
/// library, kept here so a change to the package is checked against real usage rather than only
/// against synthetic types:
/// <list type="bullet">
///   <item>a typed <c>REQ</c>/<c>API.*</c> response DTO — nested objects, arrays, enums, dictionaries;</item>
///   <item><c>LocalStorage.Get&lt;T&gt;</c> — deserialization behind a generic type parameter;</item>
///   <item>a UID128-style value type that is a plain string at runtime and converts through an
///     explicit cast operator;</item>
///   <item>a server payload whose members are short <c>[JsonProperty]</c> names on internal members;</item>
///   <item>polymorphic renderer definitions (in <see cref="TypeNameHandlingTests"/>).</item>
/// </list>
/// </summary>
[TestClass]
public class CuriosityUsageTests : JsonTestBase
{
    private const string Header = @"
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
";

    /// <summary>A search-response shaped DTO round-tripping the way an <c>API.*</c> call does.</summary>
    [TestMethod]
    public async Task TypedApiResponseRoundTrips()
    {
        var code = Header + @"
public enum ResultKind { Node = 0, Edge = 1, Snippet = 2 }

public class EmittedNode
{
    public string                     UID    { get; set; }
    public string                     Type   { get; set; }
    public Dictionary<string, string> Fields { get; set; }
}

public class SearchResult
{
    public string                             Query     { get; set; }
    public ResultKind                         Kind      { get; set; }
    public int                                Total     { get; set; }
    public float                              ElapsedMS { get; set; }
    public Dictionary<string, EmittedNode[]>  Results   { get; set; }
    public Dictionary<string, int>            Counts    { get; set; }
    public string[]                           Warnings  { get; set; }
}

public class App
{
    public static void Main()
    {
        var response = new SearchResult
        {
            Query     = ""curiosity"",
            Kind      = ResultKind.Snippet,
            Total     = 2,
            ElapsedMS = 12.5f,
            Results   = new Dictionary<string, EmittedNode[]>
            {
                [""Person""] = new[]
                {
                    new EmittedNode { UID = ""u1"", Type = ""Person"", Fields = new Dictionary<string, string> { [""Name""] = ""Ada"" } },
                    new EmittedNode { UID = ""u2"", Type = ""Person"", Fields = new Dictionary<string, string>() },
                },
            },
            Counts   = new Dictionary<string, int> { [""Person""] = 2 },
            Warnings = new string[0],
        };

        var json = JsonConvert.SerializeObject(response);
        var back = JsonConvert.DeserializeObject<SearchResult>(json);

        Console.WriteLine(back.Query + ""|"" + back.Kind + ""|"" + back.Total + ""|"" + back.ElapsedMS);
        Console.WriteLine(back.Results[""Person""].Length + ""|"" + back.Results[""Person""][0].Fields[""Name""]);
        Console.WriteLine(back.Counts[""Person""] + ""|"" + back.Warnings.Length);
        Console.WriteLine(JsonConvert.SerializeObject(back) == json);
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// <c>LocalStorage.Get&lt;T&gt;(key)</c> forwards a type parameter straight into
    /// DeserializeObject. The array case is a regression: a generic method reifying
    /// <c>T = Item[]</c> emitted the bare <c>System.Array</c> base with no element type, so the
    /// deserializer missed the array branch and produced <c>{"0":…,"1":…}</c> instead of a JS array.
    /// </summary>
    [TestMethod]
    public async Task DeserializationBehindAGenericTypeParameter()
    {
        var code = Header + @"
public class AdminSettingsItem { public string Name { get; set; } public int Value { get; set; } }

public static class LocalStorage
{
    private static readonly Dictionary<string, string> _store = new Dictionary<string, string>();
    public static void Set(string key, object value) => _store[key] = JsonConvert.SerializeObject(value);
    public static T    Get<T>(string key)            => JsonConvert.DeserializeObject<T>(_store[key]);
    public static bool TryGet<T>(string key, out T value)
    {
        if (!_store.ContainsKey(key)) { value = default(T); return false; }
        value = JsonConvert.DeserializeObject<T>(_store[key]);
        return true;
    }
}

public class App
{
    public static void Main()
    {
        LocalStorage.Set(""items"", new[]
        {
            new AdminSettingsItem { Name = ""a"", Value = 1 },
            new AdminSettingsItem { Name = ""b"", Value = 2 },
        });

        var items = LocalStorage.Get<AdminSettingsItem[]>(""items"");
        Console.WriteLine((items is AdminSettingsItem[]) + ""|"" + items.Length + ""|"" + items[0].Name + ""|"" + items[1].Value);

        LocalStorage.Set(""one"", new AdminSettingsItem { Name = ""solo"", Value = 9 });
        AdminSettingsItem single;
        Console.WriteLine(LocalStorage.TryGet(""one"", out single) + ""|"" + single.Name);

        LocalStorage.Set(""list"", new List<string> { ""x"", ""y"" });
        Console.WriteLine(string.Join("","", LocalStorage.Get<List<string>>(""list"")));

        AdminSettingsItem missing;
        Console.WriteLine(LocalStorage.TryGet(""nope"", out missing) + ""|"" + (missing == null));
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// The UID128 pattern: a type the server sends as a plain string, modelled client-side as a class
    /// with an explicit string conversion. The deserializer's cast-operator lookup is what makes a
    /// bare JSON string land in such a member.
    /// </summary>
    [TestMethod]
    public async Task ValueTypeWithACastOperatorFromString()
    {
        var code = Header + @"
public sealed class Uid
{
    private readonly string _value;
    public Uid(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length != 4) throw new ArgumentException(""bad uid: "" + value);
        _value = value;
    }
    public string Value => _value;
    public override string ToString() => _value;

    public static explicit operator Uid(string value)  => string.IsNullOrEmpty(value) ? null : new Uid(value);
    public static implicit operator string(Uid value)  => value == null ? null : value.Value;
}

public class Holder { public Uid Id { get; set; } public string Name { get; set; } }

public class App
{
    public static void Main()
    {
        var back = JsonConvert.DeserializeObject<Holder>(""{\""Id\"":\""ab12\"",\""Name\"":\""n\""}"");
        Console.WriteLine(back.Id + ""|"" + back.Name);

        var nulled = JsonConvert.DeserializeObject<Holder>(""{\""Id\"":null}"");
        Console.WriteLine(nulled.Id == null);
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// The Curiosity library's wire DTOs: short <c>[JsonProperty]</c> names on internal members, with
    /// [JsonIgnore] on the computed ones.
    /// </summary>
    [TestMethod]
    public async Task ShortNamedWireDtoRoundTrips()
    {
        var code = Header + @"
public class Edge
{
    [JsonProperty(""N"")] internal string NodeType { get; set; }
    [JsonProperty(""U"")] internal string Uid      { get; set; }
    [JsonProperty(""T"")] internal string EdgeType { get; set; }

    [JsonIgnore] public string Display => NodeType + "":"" + Uid;

    public static Edge Make(string n, string u, string t) => new Edge { NodeType = n, Uid = u, EdgeType = t };
    public string Describe() => NodeType + ""/"" + Uid + ""/"" + EdgeType;
}

public class QueryResults
{
    [JsonProperty(""R"")]  public Dictionary<string, Edge[]> Results   { get; set; }
    [JsonProperty(""C"")]  public Dictionary<string, int>    Counts    { get; set; }
    [JsonProperty(""MS"")] public float                      ElapsedMS { get; set; }
}

public class App
{
    public static void Main()
    {
        var results = new QueryResults
        {
            Results   = new Dictionary<string, Edge[]> { [""k""] = new[] { Edge.Make(""Person"", ""u1"", ""knows"") } },
            Counts    = new Dictionary<string, int> { [""k""] = 1 },
            ElapsedMS = 1.5f,
        };

        var json = JsonConvert.SerializeObject(results);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<QueryResults>(json);
        Console.WriteLine(back.Results[""k""][0].Describe() + ""|"" + back.Counts[""k""] + ""|"" + back.ElapsedMS);
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// A chat/stream payload discriminated by an enum member, deserialized in two steps (envelope
    /// first, then the typed metadata) — how the front-end reads <c>ChatStreamPart</c> and the
    /// <c>*CommandMetadata</c> payloads.
    /// </summary>
    [TestMethod]
    public async Task TwoStepEnvelopeThenPayloadDeserialization()
    {
        var code = Header + @"
public enum PartKind { Text = 0, Error = 1, Completed = 2 }

public class ChatStreamPart
{
    public PartKind Kind    { get; set; }
    public string   Payload { get; set; }
}

public class ErrorPayload     { public string Message { get; set; } public int Code { get; set; } }
public class CompletedPayload { public int    Tokens  { get; set; } public double Seconds { get; set; } }

public class App
{
    public static void Main()
    {
        var parts = new[]
        {
            new ChatStreamPart { Kind = PartKind.Error,     Payload = JsonConvert.SerializeObject(new ErrorPayload { Message = ""nope"", Code = 42 }) },
            new ChatStreamPart { Kind = PartKind.Completed, Payload = JsonConvert.SerializeObject(new CompletedPayload { Tokens = 10, Seconds = 1.5 }) },
            new ChatStreamPart { Kind = PartKind.Text,      Payload = ""hello"" },
        };

        foreach (var part in JsonConvert.DeserializeObject<ChatStreamPart[]>(JsonConvert.SerializeObject(parts)))
        {
            switch (part.Kind)
            {
                case PartKind.Error:
                    var error = JsonConvert.DeserializeObject<ErrorPayload>(part.Payload);
                    Console.WriteLine(""error "" + error.Code + "" "" + error.Message);
                    break;
                case PartKind.Completed:
                    var done = JsonConvert.DeserializeObject<CompletedPayload>(part.Payload);
                    Console.WriteLine(""done "" + done.Tokens + "" "" + done.Seconds);
                    break;
                default:
                    Console.WriteLine(""text "" + part.Payload);
                    break;
            }
        }
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// A settings/definition object that is stored as JSON and edited in the admin UI: nullable
    /// members, an enum, a nested list of definitions and a dictionary of loose values.
    /// </summary>
    [TestMethod]
    public async Task AdminDefinitionRoundTrips()
    {
        var code = Header + @"
public enum FieldKind { Text = 0, Number = 1, Date = 2 }

public class FieldDefinition
{
    public string    Name     { get; set; }
    public FieldKind Kind     { get; set; }
    public bool      Required { get; set; }
    public string    Default  { get; set; }
}

public class SchemaDefinition
{
    public string                  Name              { get; set; }
    public string                  Type              { get; set; }
    public string                  Key               { get; set; }
    public bool                    HideOnDataHub     { get; set; }
    public List<FieldDefinition>   Fields            { get; set; }
    public List<string>            DeletedFieldNames { get; set; }
    public Dictionary<string, string> Metadata       { get; set; }
}

public class App
{
    public static void Main()
    {
        var schema = new SchemaDefinition
        {
            Name = ""Person"",
            Type = ""Person"",
            Key  = ""UID"",
            Fields = new List<FieldDefinition>
            {
                new FieldDefinition { Name = ""Name"", Kind = FieldKind.Text,   Required = true },
                new FieldDefinition { Name = ""Age"",  Kind = FieldKind.Number, Default = ""0"" },
            },
            DeletedFieldNames = new List<string>(),
            Metadata = new Dictionary<string, string> { [""owner""] = ""admin"" },
        };

        var json = JsonConvert.SerializeObject(schema);
        var back = JsonConvert.DeserializeObject<SchemaDefinition>(json);

        Console.WriteLine(back.Name + ""|"" + back.Fields.Count + ""|"" + back.Fields[1].Kind + ""|"" + back.Fields[0].Required);
        Console.WriteLine(back.DeletedFieldNames.Count + ""|"" + back.Metadata[""owner""] + ""|"" + (back.Fields[0].Default == null));
        Console.WriteLine(JsonConvert.SerializeObject(back) == json);
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// Serializing the request body of a POST the way <c>REQ.WithBody(obj)</c> does, including the
    /// null-dropping settings the graph client uses.
    /// </summary>
    [TestMethod]
    public async Task RequestBodySerializationWithNullDropping()
    {
        var code = Header + @"
public class GraphOperation
{
    public string                     Type    { get; set; }
    public string                     Uid     { get; set; }
    public Dictionary<string, string> Fields  { get; set; }
    public List<string>               Labels  { get; set; }
    public int?                       Version { get; set; }
}

public class App
{
    static readonly JsonSerializerSettings IgnoreNulls = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

    public static void Main()
    {
        var operation = new GraphOperation { Type = ""Person"", Uid = ""u1"" };
        Console.WriteLine(JsonConvert.SerializeObject(operation, IgnoreNulls));
        Console.WriteLine(JsonConvert.SerializeObject(operation));

        var full = new GraphOperation
        {
            Type    = ""Person"",
            Uid     = ""u1"",
            Fields  = new Dictionary<string, string> { [""Name""] = ""Ada"" },
            Labels  = new List<string> { ""a"" },
            Version = 3,
        };
        Console.WriteLine(JsonConvert.SerializeObject(full, IgnoreNulls));
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// A DTO whose members are read-only and set through a single constructor, the way the shared
    /// schema types are written — combined with a collection member so the constructor path and the
    /// collection path are exercised together.
    /// </summary>
    [TestMethod]
    public async Task ImmutableDtoWithCollectionMemberRoundTrips()
    {
        var code = Header + @"
public sealed class FieldDefinition
{
    [JsonConstructor]
    public FieldDefinition(string name, string type) { Name = name; Type = type; }
    public string Name { get; }
    public string Type { get; }
}

public sealed class SchemaDefinition
{
    public SchemaDefinition(string name, string type) : this(name, type, new List<FieldDefinition>()) { }

    [JsonConstructor]
    public SchemaDefinition(string name, string type, List<FieldDefinition> fields)
    {
        Name   = name;
        Type   = type;
        Fields = fields;
    }

    public string                Name   { get; }
    public string                Type   { get; }
    public List<FieldDefinition> Fields { get; }
}

public class App
{
    public static void Main()
    {
        var schema = new SchemaDefinition(""Person"", ""Person"", new List<FieldDefinition>
        {
            new FieldDefinition(""Name"", ""text""),
            new FieldDefinition(""Age"", ""number""),
        });

        var json = JsonConvert.SerializeObject(schema);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<SchemaDefinition>(json);
        Console.WriteLine(back.Name + ""|"" + back.Fields.Count + ""|"" + back.Fields[1].Name + ""|"" + back.Fields[1].Type);
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// Nested generic payloads the graph API returns: a dictionary of lists of dictionaries, plus a
    /// list of key/value pairs modelled as a DTO with one-letter names.
    /// </summary>
    [TestMethod]
    public async Task NestedGenericPayloadRoundTrips()
    {
        var code = Header + @"
public class KeyValue
{
    [JsonProperty(""K"")] public string Key   { get; set; }
    [JsonProperty(""V"")] public string Value { get; set; }
}

public class App
{
    public static void Main()
    {
        var payload = new Dictionary<string, List<Dictionary<string, string>>>
        {
            [""rows""] = new List<Dictionary<string, string>>
            {
                new Dictionary<string, string> { [""a""] = ""1"" },
                new Dictionary<string, string> { [""b""] = ""2"" },
            },
        };

        var json = JsonConvert.SerializeObject(payload);
        var back = JsonConvert.DeserializeObject<Dictionary<string, List<Dictionary<string, string>>>>(json);
        Console.WriteLine(back[""rows""].Count + ""|"" + back[""rows""][1][""b""]);

        var pairs = new List<KeyValue> { new KeyValue { Key = ""k"", Value = ""v"" } };
        var pairsJson = JsonConvert.SerializeObject(pairs);
        Console.WriteLine(pairsJson);
        Console.WriteLine(JsonConvert.DeserializeObject<List<KeyValue>>(pairsJson)[0].Value);
    }
}";
        await RunAndCompare(code);
    }
}
