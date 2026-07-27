using System.Threading.Tasks;

namespace Transpose.Newtonsoft.Json.Tests;

/// <summary>
/// <c>TypeNameHandling</c> and <c>ISerializationBinder</c> — the polymorphic-payload machinery the
/// Curiosity front-end leans on (NodeRendererDefinition, the comment and notification schemas), where
/// a .NET server writes <c>$type</c> values whose assembly names the client cannot resolve.
/// </summary>
[TestClass]
public class TypeNameHandlingTests : JsonTestBase
{
    /// <summary>
    /// The shape of Curiosity's <c>AssemblyNameIgnoringSerializationBinder</c>: strip the assembly
    /// name from a <c>$type</c> (including from every generic argument) and restrict what may be
    /// materialised to an allow-list.
    /// </summary>
    private const string Model = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Repro.Models
{
    public interface IShape { string Describe(); }

    public sealed class Circle : IShape
    {
        public double Radius { get; set; }
        public string Describe() => ""circle "" + Radius;
    }

    public sealed class Square : IShape
    {
        public double Side { get; set; }
        public string Describe() => ""square "" + Side;
    }

    public sealed class Item
    {
        public string Name  { get; set; }
        public int    Value { get; set; }
    }

    public sealed class Drawing
    {
        public IShape   Main   { get; set; }
        public IShape[] Others { get; set; }
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

public static class Settings
{
    public static readonly JsonSerializerSettings Objects = new JsonSerializerSettings
    {
        TypeNameHandling = TypeNameHandling.Objects,
        SerializationBinder = AllowListBinder.ForTypes(
            typeof(Repro.Models.IShape),
            typeof(Repro.Models.Drawing),
            typeof(Repro.Models.Item),
            typeof(Dictionary<string, Repro.Models.Item>),
            typeof(List<Repro.Models.IShape>),
            typeof(Repro.Models.IShape[])),
    };
}
";

    /// <summary>
    /// A reduction of Curiosity's <c>NodeRendererDefinition</c> graph, reusing the same
    /// assembly-name-stripping <see cref="AllowListBinder"/>. Kept structurally faithful where it
    /// matters: the narrowing interfaces, the interface-typed arrays, <c>TabContent</c> as a non-item
    /// container, a member-less item, and outer types with no parameterless constructor.
    /// </summary>
    /// <remarks>Appended after <see cref="Model"/>, which supplies the usings and the binder.</remarks>
    private const string RendererModel = @"
namespace Repro.Render
{
    public interface IItem       { string Name { get; } }
    public interface IHeaderItem : IItem { }
    public interface IFooterItem : IItem { }

    public enum FieldKind { Unknown, String, Number, Time }

    public sealed class Field : IHeaderItem, IFooterItem
    {
        public Field() { }
        public string    FieldName { get; set; }
        public FieldKind Kind      { get; set; }
        public string    Name      { get { return ""Field""; } }
    }

    public sealed class PlainText : IHeaderItem
    {
        public PlainText() { }
        public string Text { get; set; }
        public string Name { get { return ""Text""; } }
    }

    public sealed class LabelAndContent : IHeaderItem, IFooterItem
    {
        public LabelAndContent() { }
        public string Text    { get; set; }
        public IItem  Content { get; set; }
        public string Name    { get { return ""Label""; } }
    }

    public sealed class Stack : IItem
    {
        public Stack() { }
        public IItem[] Content { get; set; }
        public string  Name    { get { return ""Stack""; } }
    }

    // Not an IItem itself, but carries a polymorphic member.
    public sealed class TabContent
    {
        public TabContent() { }
        public string Title   { get; set; }
        public IItem  Content { get; set; }
    }

    public sealed class TabsContent : IItem
    {
        public TabsContent() { }
        public TabContent[] Tabs { get; set; }
        public string       Name { get { return ""Tabs""; } }
    }

    // No serializable members at all.
    public sealed class SimilarSearch : IItem
    {
        public SimilarSearch() { }
        public string Name { get { return ""Similar""; } }
    }

    // Only an argument-taking constructor, like NodeRendererDefinition.
    public sealed class RendererDefinition
    {
        public RendererDefinition(IHeaderItem[] header, IItem contentCard, IItem contentView, IFooterItem[] footer)
        {
            Header      = header;
            ContentCard = contentCard;
            ContentView = contentView;
            Footer      = footer;
        }

        public IHeaderItem[] Header      { get; set; }
        public IItem         ContentCard { get; set; }
        public IItem         ContentView { get; set; }
        public IFooterItem[] Footer      { get; set; }
    }
}

public static class RenderSettings
{
    public static readonly JsonSerializerSettings Objects = new JsonSerializerSettings
    {
        TypeNameHandling = TypeNameHandling.Objects,
        SerializationBinder = AllowListBinder.ForTypes(
            typeof(Repro.Render.IItem),
            typeof(Repro.Render.IHeaderItem),
            typeof(Repro.Render.IFooterItem),
            typeof(Repro.Render.TabContent),
            typeof(Repro.Render.RendererDefinition),
            typeof(Dictionary<string, Repro.Render.RendererDefinition>)),
    };
}
";

    [TestMethod]
    public async Task PolymorphicMemberRoundTripsThroughTheBinder()
    {
        var code = Model + @"
public class App
{
    public static void Main()
    {
        var drawing = new Repro.Models.Drawing
        {
            Main   = new Repro.Models.Circle { Radius = 2 },
            Others = new Repro.Models.IShape[] { new Repro.Models.Square { Side = 3 } },
        };

        var json = JsonConvert.SerializeObject(drawing, Settings.Objects);
        Console.WriteLine(json.Contains(""$type""));

        var back = JsonConvert.DeserializeObject<Repro.Models.Drawing>(json, Settings.Objects);
        Console.WriteLine(back.Main.Describe());
        Console.WriteLine(back.Others.Length + ""|"" + back.Others[0].Describe());
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// The DictionaryFromJson regression: a custom ISerializationBinder was never invoked, because
    /// JsonConvert.js looked for the member only under the legacy h5 interface-mangled slot, which
    /// Transpose does not emit for an implicit implementation. A server-produced dictionary $type
    /// then fell through to a raw Type.GetType(fullName) and failed with "Type specified in JSON
    /// '…' was not resolved."
    /// </summary>
    [TestMethod]
    public async Task ServerProducedDictionaryTypeResolvesThroughTheBinder()
    {
        var code = Model + @"
public class App
{
    public static void Main()
    {
        // $type exactly as a .NET Core server (System.Private.CoreLib) emits it.
        var json =
            ""{\""$type\"":\""System.Collections.Generic.Dictionary`2[[System.String, System.Private.CoreLib],[Repro.Models.Item, SomeServerAssembly]], System.Private.CoreLib\"","" +
            ""\""a\"":{\""$type\"":\""Repro.Models.Item, SomeServerAssembly\"",\""Name\"":\""Alpha\"",\""Value\"":1},"" +
            ""\""b\"":{\""$type\"":\""Repro.Models.Item, SomeServerAssembly\"",\""Name\"":\""Beta\"",\""Value\"":2}}"";

        var back = JsonConvert.DeserializeObject<Dictionary<string, Repro.Models.Item>>(json, Settings.Objects);
        Console.WriteLine(""Count: "" + back.Count);
        Console.WriteLine(""a.Name: "" + back[""a""].Name);
        Console.WriteLine(""b.Value: "" + back[""b""].Value);
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// The full <c>GET api/schema/renderers</c> payload shape, which the flat-<c>Item</c> binder tests
    /// above do not reach: a narrowing interface hierarchy (<c>IHeaderItem</c> / <c>IFooterItem</c> both
    /// extending <c>INodeRendererItem</c>), interface-typed <b>arrays</b>, an interface-typed member
    /// nested inside an element of one of those arrays, a container that is <b>not</b> itself an item
    /// (<c>TabContent</c>) sitting in a polymorphic graph, a member-less item (<c>SimilarSearch</c>),
    /// a null polymorphic slot, and outer types whose only constructor takes arguments. Every
    /// <c>$type</c> names an assembly the client does not have, so all of it has to come back through
    /// the binder.
    /// </summary>
    [TestMethod]
    public async Task ServerProducedRendererDefinitionResolvesEveryPolymorphicSlot()
    {
        var code = Model + RendererModel + @"
public class App
{
    public static void Main()
    {
        // $type values as the server writes them: assembly names the client cannot resolve.
        var json =
            ""{\""$type\"":\""System.Collections.Generic.Dictionary`2[[System.String, System.Private.CoreLib],[Repro.Render.RendererDefinition, ServerAsm]], System.Private.CoreLib\"","" +
            ""\""_User\"":{\""$type\"":\""Repro.Render.RendererDefinition, ServerAsm\"","" +
              ""\""Header\"":["" +
                ""{\""$type\"":\""Repro.Render.PlainText, ServerAsm\"",\""Text\"":\""Title\""},"" +
                ""{\""$type\"":\""Repro.Render.LabelAndContent, ServerAsm\"",\""Text\"":\""L\"",\""Content\"":{\""$type\"":\""Repro.Render.Field, ServerAsm\"",\""FieldName\"":\""nested\"",\""Kind\"":2}}"" +
              ""],"" +
              ""\""ContentCard\"":{\""$type\"":\""Repro.Render.Stack, ServerAsm\"",\""Content\"":["" +
                ""{\""$type\"":\""Repro.Render.SimilarSearch, ServerAsm\""},"" +
                ""{\""$type\"":\""Repro.Render.Stack, ServerAsm\"",\""Content\"":[{\""$type\"":\""Repro.Render.PlainText, ServerAsm\"",\""Text\"":\""deep\""}]}"" +
              ""]},"" +
              ""\""ContentView\"":{\""$type\"":\""Repro.Render.TabsContent, ServerAsm\"",\""Tabs\"":["" +
                ""{\""$type\"":\""Repro.Render.TabContent, ServerAsm\"",\""Title\"":\""A\"",\""Content\"":{\""$type\"":\""Repro.Render.Field, ServerAsm\"",\""FieldName\"":\""tab\"",\""Kind\"":1}},"" +
                ""{\""$type\"":\""Repro.Render.TabContent, ServerAsm\"",\""Title\"":\""B\"",\""Content\"":null}"" +
              ""]},"" +
              ""\""Footer\"":[{\""$type\"":\""Repro.Render.Field, ServerAsm\"",\""FieldName\"":\""when\"",\""Kind\"":3}]}}"";

        var back = JsonConvert.DeserializeObject<Dictionary<string, Repro.Render.RendererDefinition>>(json, RenderSettings.Objects);
        var def  = back[""_User""];

        Console.WriteLine(""Count: "" + back.Count);
        Console.WriteLine(""Header[0]: "" + def.Header[0].GetType().Name + "" "" + ((Repro.Render.PlainText)def.Header[0]).Text);
        var lab = (Repro.Render.LabelAndContent)def.Header[1];
        Console.WriteLine(""Header[1]: "" + lab.GetType().Name + "" -> "" + lab.Content.GetType().Name + "" "" + ((Repro.Render.Field)lab.Content).FieldName);

        var card = (Repro.Render.Stack)def.ContentCard;
        Console.WriteLine(""Card: "" + card.GetType().Name + "" len="" + card.Content.Length);
        Console.WriteLine(""Card[0]: "" + card.Content[0].GetType().Name);
        var inner = (Repro.Render.Stack)card.Content[1];
        Console.WriteLine(""Card[1]: "" + inner.GetType().Name + "" -> "" + inner.Content[0].GetType().Name);

        var tabs = (Repro.Render.TabsContent)def.ContentView;
        Console.WriteLine(""Tabs len="" + tabs.Tabs.Length);
        Console.WriteLine(""Tab0: "" + tabs.Tabs[0].GetType().Name + "" "" + tabs.Tabs[0].Title + "" -> "" + tabs.Tabs[0].Content.GetType().Name);
        Console.WriteLine(""Tab1 content null: "" + (tabs.Tabs[1].Content == null));

        Console.WriteLine(""Footer[0]: "" + def.Footer[0].GetType().Name + "" Kind="" + ((Repro.Render.Field)def.Footer[0]).Kind);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task DictionaryRoundTripsThroughTheBinder()
    {
        var code = Model + @"
public class App
{
    public static void Main()
    {
        var dict = new Dictionary<string, Repro.Models.Item>
        {
            [""a""] = new Repro.Models.Item { Name = ""Alpha"", Value = 1 },
            [""b""] = new Repro.Models.Item { Name = ""Beta"",  Value = 2 },
        };

        var json = JsonConvert.SerializeObject(dict, Settings.Objects);
        Console.WriteLine(""has $type: "" + json.Contains(""$type""));

        var back = JsonConvert.DeserializeObject<Dictionary<string, Repro.Models.Item>>(json, Settings.Objects);
        Console.WriteLine(""Count: "" + back.Count);
        Console.WriteLine(""a.Name: "" + back[""a""].Name);
        Console.WriteLine(""b.Value: "" + back[""b""].Value);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task BinderRejectsATypeOutsideTheAllowList()
    {
        var code = Model + @"
public class Sneaky { public string Payload { get; set; } }
public class App
{
    public static void Main()
    {
        var json = ""{\""$type\"":\""Sneaky, SomeAssembly\"",\""Payload\"":\""x\""}"";
        try
        {
            var back = JsonConvert.DeserializeObject<object>(json, Settings.Objects);
            Console.WriteLine(""no throw"");
        }
        // Only the exception type is compared: Json.NET re-wraps the binder's exception with a
        // message of its own, the binding library rethrows the binder's message as-is.
        catch (JsonSerializationException ex) { Console.WriteLine(""threw "" + ex.GetType().Name); }
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task UnresolvableTypeNameThrows()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Holder { public object Any { get; set; } }
public class App
{
    public static void Main()
    {
        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Objects };
        try
        {
            JsonConvert.DeserializeObject<Holder>(""{\""Any\"":{\""$type\"":\""No.Such.Type, Nowhere\"",\""x\"":1}}"", settings);
            Console.WriteLine(""no throw"");
        }
        catch (JsonSerializationException) { Console.WriteLine(""threw""); }
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task TypeNameHandlingAllWritesTypeOnCollectionsAndTheirItems()
    {
        var code = Model + @"
public class App
{
    public static void Main()
    {
        var settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.All,
            SerializationBinder = AllowListBinder.ForTypes(typeof(Repro.Models.IShape), typeof(List<Repro.Models.IShape>)),
        };

        var shapes = new List<Repro.Models.IShape> { new Repro.Models.Circle { Radius = 1 } };
        var json = JsonConvert.SerializeObject(shapes, settings);
        Console.WriteLine(json.Contains(""$values""));

        var back = JsonConvert.DeserializeObject<List<Repro.Models.IShape>>(json, settings);
        Console.WriteLine(back.Count + ""|"" + back[0].Describe());
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task TypeNameHandlingAutoOnlyWritesTypeWhenItDiffersFromTheDeclaredOne()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Animal { public string Name { get; set; } }
public class Dog : Animal { public bool GoodBoy { get; set; } }
public class Holder { public Animal Pet { get; set; } public string Note { get; set; } }
public class App
{
    public static void Main()
    {
        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };

        Console.WriteLine(JsonConvert.SerializeObject(new Holder { Pet = new Animal  { Name = ""a"" } }, settings).Contains(""$type""));
        Console.WriteLine(JsonConvert.SerializeObject(new Holder { Pet = new Dog     { Name = ""b"" } }, settings).Contains(""$type""));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task PerPropertyTypeNameHandlingIsScopedToThatProperty()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Animal { public string Name { get; set; } }
public class Dog : Animal { public bool GoodBoy { get; set; } }
public class Holder
{
    [JsonProperty(TypeNameHandling = TypeNameHandling.Objects)] public Animal Tagged   { get; set; }
    public Animal Untagged { get; set; }
}
public class App
{
    public static void Main()
    {
        var json = JsonConvert.SerializeObject(new Holder { Tagged = new Dog { Name = ""a"" }, Untagged = new Dog { Name = ""b"" } });
        Console.WriteLine(json.Contains(""\""$type\"""") + ""|"" + json.Contains(""GoodBoy""));
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// With TypeNameHandling left at None a <c>$type</c> in the payload is inert — it neither selects
    /// a type nor breaks the parse.
    /// </summary>
    [TestMethod]
    public async Task TypeNameInPayloadIsIgnoredWhenHandlingIsNone()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item { public string Name { get; set; } }
public class App
{
    public static void Main()
    {
        var back = JsonConvert.DeserializeObject<Item>(""{\""$type\"":\""Whatever, Nowhere\"",\""Name\"":\""a\""}"");
        Console.WriteLine(back.Name);
    }
}";
        await RunAndCompare(code);
    }
}
