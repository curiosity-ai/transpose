using System.Threading.Tasks;

namespace Transpose.Translator.Tests;

/// <summary>
/// End-to-end tests for the <c>Transpose.Newtonsoft.Json</c> binding library: the C# is translated
/// and run on Node against the package's real runtime (JsonConvert.js). See
/// <see cref="NewtonsoftJsonRunner"/> for how the package is compiled and loaded.
/// </summary>
[TestClass]
public class NewtonsoftJsonTests
{
    // A serialization binder that mirrors the one the mosaik front-end uses with
    // NodeRendererDefinition: TypeNameHandling.Objects + a custom ISerializationBinder that strips
    // the assembly names from generic type parameters (so a $type emitted by a .NET Core server —
    // where the assembly is System.Private.CoreLib — resolves on the client) and restricts
    // deserialization to an allow-list of types.
    private const string Binder = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Repro.Models
{
    public sealed class Item
    {
        public string Name { get; set; }
        public int Value { get; set; }
    }
}

public sealed class AllowListBinder : ISerializationBinder
{
    private readonly Type[] _allowed;
    private AllowListBinder(Type[] allowed) { _allowed = allowed; }
    public static AllowListBinder ForTypes(params Type[] types) => new AllowListBinder(types);

    public void BindToName(Type serializedType, out string assemblyName, out string typeName)
    {
        assemblyName = serializedType.Assembly.FullName;
        typeName = serializedType.FullName;
    }

    public Type BindToType(string assemblyName, string typeName)
    {
        var cleaned = RemoveAssemblyNamesFromGenericTypeParams(typeName);
        var type = Type.GetType(cleaned);
        if (type != null && !_allowed.Any(t => t.IsAssignableFrom(type)))
            throw new JsonSerializationException(""Type '"" + type.FullName + ""' is not allowed"");
        return type;
    }

    private static string RemoveAssemblyNamesFromGenericTypeParams(string typeName)
        => ReadToEndOfBracketedContent(typeName).Replace("" "", """");

    private static string ReadToEndOfBracketedContent(string value)
    {
        var content = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == ']') return content.ToString();
            else if (c == '[')
            {
                content.Append(c);
                var nestedTypeName = ReadToEndOfBracketedContent(value.Substring(i + 1));
                if (!nestedTypeName.StartsWith(""[""))
                {
                    var without = RemoveSingleAssemblyName(nestedTypeName);
                    content.Append(without);
                    content.Append(' ', nestedTypeName.Length - without.Length);
                }
                else content.Append(nestedTypeName);
                i += nestedTypeName.Length;
                content.Append(']');
                i += 1;
            }
            else content.Append(c);
        }
        return content.ToString();
    }

    private static string RemoveSingleAssemblyName(string typeName)
    {
        var segments = typeName.Split(',').ToArray();
        return string.Join("","", segments.Take(segments.Length - 1));
    }
}
";

    /// <summary>
    /// Regression for the DictionaryFromJson bug: a custom ISerializationBinder was never invoked
    /// (JsonConvert.js looked for the member only under the legacy h5 interface-mangled slot, which
    /// Transpose does not emit for an implicit implementation), so a server-produced dictionary
    /// $type fell through to raw Type.GetType(fullName) and failed with
    /// "Type specified in JSON '…' was not resolved."
    /// </summary>
    [TestMethod]
    public async Task DictionaryFromServerStyleJsonResolvesThroughBinder()
    {
        var code = Binder + @"
public class App
{
    static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
    {
        TypeNameHandling = TypeNameHandling.Objects,
        SerializationBinder = AllowListBinder.ForTypes(typeof(Repro.Models.Item), typeof(Dictionary<string, Repro.Models.Item>))
    };

    public static void Main()
    {
        // $type exactly as a .NET Core server (System.Private.CoreLib) emits it.
        var json =
            ""{\""$type\"":\""System.Collections.Generic.Dictionary`2[[System.String, System.Private.CoreLib],[Repro.Models.Item, SomeServerAssembly]], System.Private.CoreLib\"","" +
            ""\""a\"":{\""$type\"":\""Repro.Models.Item, SomeServerAssembly\"",\""Name\"":\""Alpha\"",\""Value\"":1},"" +
            ""\""b\"":{\""$type\"":\""Repro.Models.Item, SomeServerAssembly\"",\""Name\"":\""Beta\"",\""Value\"":2}}"";

        var back = JsonConvert.DeserializeObject<Dictionary<string, Repro.Models.Item>>(json, Settings);
        Console.WriteLine(""Count: "" + back.Count);
        Console.WriteLine(""a.Name: "" + back[""a""].Name);
        Console.WriteLine(""b.Value: "" + back[""b""].Value);
    }
}";
        var output = await NewtonsoftJsonRunner.RunAsync(code);
        Assert.AreEqual("Count: 2\na.Name: Alpha\nb.Value: 2", output);
    }

    /// <summary>A full client-side round trip through the binder (serialize then deserialize) keeps
    /// the dictionary shape and its element types.</summary>
    [TestMethod]
    public async Task DictionaryRoundTripsThroughBinder()
    {
        var code = Binder + @"
public class App
{
    static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
    {
        TypeNameHandling = TypeNameHandling.Objects,
        SerializationBinder = AllowListBinder.ForTypes(typeof(Repro.Models.Item), typeof(Dictionary<string, Repro.Models.Item>))
    };

    public static void Main()
    {
        var dict = new Dictionary<string, Repro.Models.Item>
        {
            [""a""] = new Repro.Models.Item { Name = ""Alpha"", Value = 1 },
            [""b""] = new Repro.Models.Item { Name = ""Beta"", Value = 2 },
        };

        var json = JsonConvert.SerializeObject(dict, Settings);
        Console.WriteLine(""has $type: "" + json.Contains(""$type""));

        var back = JsonConvert.DeserializeObject<Dictionary<string, Repro.Models.Item>>(json, Settings);
        Console.WriteLine(""Count: "" + back.Count);
        Console.WriteLine(""a.Name: "" + back[""a""].Name);
        Console.WriteLine(""b.Value: "" + back[""b""].Value);
    }
}";
        var output = await NewtonsoftJsonRunner.RunAsync(code);
        Assert.AreEqual("has $type: True\nCount: 2\na.Name: Alpha\nb.Value: 2", output);
    }

    /// <summary>
    /// Regression for the "array deserialized into an object with numeric keys" bug: a generic
    /// method reifies its <c>T = Item[]</c> type argument, and Transpose emitted the bare
    /// <c>System.Array</c> base (no element type) instead of <c>System.Array.type(Item)</c>. The
    /// deserializer then failed the array branch and produced <c>{"0":…,"1":…}</c> rather than a
    /// JS array. This mirrors LocalStorage.Get&lt;AdminSettingsItem[]&gt;(…) in the front-end.
    /// </summary>
    [TestMethod]
    public async Task GenericDeserializeIntoArrayTypeArgumentProducesArray()
    {
        var code = Binder + @"
public class App
{
    static T Get<T>(string json) => JsonConvert.DeserializeObject<T>(json);

    public static void Main()
    {
        var json = ""[{\""Name\"":\""Alpha\"",\""Value\"":1},{\""Name\"":\""Beta\"",\""Value\"":2},{\""Name\"":\""Gamma\"",\""Value\"":3}]"";

        var arr = Get<Repro.Models.Item[]>(json);
        Console.WriteLine(""IsArray: "" + (arr is Repro.Models.Item[]));
        Console.WriteLine(""Length: "" + arr.Length);
        Console.WriteLine(""[0].Name: "" + arr[0].Name);
        Console.WriteLine(""[2].Value: "" + arr[2].Value);
    }
}";
        var output = await NewtonsoftJsonRunner.RunAsync(code);
        Assert.AreEqual("IsArray: True\nLength: 3\n[0].Name: Alpha\n[2].Value: 3", output);
    }

    /// <summary>
    /// A byte[] serializes to a base64 string (not a JSON number array) and round-trips — this needs
    /// the array VALUE to carry its element type so the serializer recognises System.Byte. Regression
    /// for array literals emitting as bare JS arrays with no element type.
    /// </summary>
    [TestMethod]
    public async Task ByteArraySerializesAsBase64()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class M { public byte[] Data { get; set; } }
public class App
{
    public static void Main()
    {
        var j = JsonConvert.SerializeObject(new M { Data = new byte[]{ 1, 2, 3, 255 } });
        Console.WriteLine(j);
        var b = JsonConvert.DeserializeObject<M>(j);
        Console.WriteLine(b.Data.Length + ""|"" + b.Data[3]);
        // top-level byte[] too
        Console.WriteLine(JsonConvert.SerializeObject(new byte[]{ 0, 16, 255 }));
    }
}";
        var output = await NewtonsoftJsonRunner.RunAsync(code);
        Assert.AreEqual("{\"Data\":\"AQID/w==\"}\n4|255\n\"ABD/\"", output);
    }

    /// <summary>An array of complex objects round-trips (element type preserved on the value).</summary>
    [TestMethod]
    public async Task ArrayOfObjectsRoundTrips()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item { public int V { get; set; } }
public class App
{
    public static void Main()
    {
        var a = new Item[]{ new Item{V=1}, new Item{V=2}, new Item{V=3} };
        var j = JsonConvert.SerializeObject(a);
        var b = JsonConvert.DeserializeObject<Item[]>(j);
        Console.WriteLine((b is Item[]) + ""|"" + b.Length + ""|"" + b[0].V + ""|"" + b[2].V);
    }
}";
        var output = await NewtonsoftJsonRunner.RunAsync(code);
        Assert.AreEqual("True|3|1|3", output);
    }

    /// <summary>Plain (no settings) serialize/deserialize of a POCO works.</summary>
    [TestMethod]
    public async Task SimpleObjectRoundTrips()
    {
        var code = Binder + @"
public class App
{
    public static void Main()
    {
        var item = new Repro.Models.Item { Name = ""Hello"", Value = 42 };
        var json = JsonConvert.SerializeObject(item);
        var back = JsonConvert.DeserializeObject<Repro.Models.Item>(json);
        Console.WriteLine(back.Name + "" "" + back.Value);
    }
}";
        var output = await NewtonsoftJsonRunner.RunAsync(code);
        Assert.AreEqual("Hello 42", output);
    }

    /// <summary>
    /// Regression for the unsafe <c>eval</c> in <c>JsonConvert.parse</c>: on a JSON.parse SyntaxError
    /// the old code fell back to <c>eval('(' + value + ')')</c>, which both executed arbitrary JS and
    /// threw a raw <c>ReferenceError</c> for a bare identifier (e.g. a string-backed enum value that
    /// reached parse un-quoted surfaced as "ReferenceError: type is not defined"). Malformed input must
    /// now surface as a <c>JsonException</c>, and the eval must not run — a bare identifier that would
    /// have thrown ReferenceError under eval must not do so here.
    /// </summary>
    [TestMethod]
    public async Task InvalidJsonThrowsJsonExceptionInsteadOfEval()
    {
        var code = Binder + @"
public class App
{
    static string Try(string raw)
    {
        try { JsonConvert.DeserializeObject<Repro.Models.Item>(raw); return ""parsed""; }
        catch (JsonException) { return ""JsonException""; }
        catch (Exception e) { return ""other:"" + e.GetType().Name; }
    }
    public static void Main()
    {
        // A bare identifier: eval('(nonExistentGlobal)') would throw ReferenceError; JSON.parse rejects it.
        Console.WriteLine(Try(""nonExistentGlobal""));
        // A JS object literal with unquoted keys: eval would have accepted it, JSON.parse must reject it.
        Console.WriteLine(Try(""{ unquoted: 1 }""));
        // Valid JSON still works.
        Console.WriteLine(Try(""{\""Name\"":\""Ok\"",\""Value\"":1}""));
    }
}";
        var output = await NewtonsoftJsonRunner.RunAsync(code);
        Assert.AreEqual("JsonException\nJsonException\nparsed", output);
    }
}
