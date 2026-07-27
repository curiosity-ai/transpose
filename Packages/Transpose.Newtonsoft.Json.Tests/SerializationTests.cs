using System.Threading.Tasks;

namespace Transpose.Newtonsoft.Json.Tests;

/// <summary><c>JsonConvert.SerializeObject</c> — what the binding library writes for each shape of value.</summary>
[TestClass]
public class SerializationTests : JsonTestBase
{
    [TestMethod]
    public async Task PropertiesSerializeInDeclarationOrder()
    {
        var code = @"
using System;
using Newtonsoft.Json;
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
        Console.WriteLine(JsonConvert.SerializeObject(new Item { Name = ""a"", Value = 1, Flag = true }));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task PublicFieldsAreSerialized()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item
{
    public string Name;
    public int    Value;
}
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Item { Name = ""a"", Value = 1 }));
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// The one systematic divergence from Json.NET, and the reason every other test in this suite
    /// compares JSON canonically (see <see cref="TestOutput.CanonicalizeJson"/>): both write fields
    /// before properties, but Json.NET keeps each group in declaration order while the binding
    /// library walks the type's Transpose reflection metadata, which is alphabetical. Only matters
    /// when JSON is compared as *text* — a hash, a golden file, a server-side string comparison.
    /// </summary>
    [TestMethod]
    public async Task MemberOrderIsAlphabeticalFieldsFirst()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item
{
    public string Zebra { get; set; }
    public int    Field;
    public string Apple { get; set; }
}
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Item { Zebra = ""z"", Field = 1, Apple = ""a"" }));
    }
}";
        await RunJs(code,
            expected:     @"{""Field"":1,""Apple"":""a"",""Zebra"":""z""}",
            nativePrints: @"{""Field"":1,""Zebra"":""z"",""Apple"":""a""}");
    }

    [TestMethod]
    public async Task NonPublicMembersAreSkipped()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item
{
    public    string Public   { get; set; }
    internal  string Internal { get; set; }
    protected string Protected { get; set; }
    private   string Private  { get; set; }
    public    string PrivateSetter { get; private set; }
    public Item() { Internal = ""i""; Protected = ""p""; Private = ""v""; PrivateSetter = ""s""; }
}
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Item { Public = ""x"" }));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task StaticMembersAreNotSerialized()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item
{
    public static string Shared = ""static"";
    public static int    Count  { get; set; }
    public string        Name   { get; set; }
}
public class App
{
    public static void Main()
    {
        Item.Count = 3;
        Console.WriteLine(JsonConvert.SerializeObject(new Item { Name = ""x"" }));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task GetOnlyPropertyIsSerialized()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item
{
    public Item(string name) { Name = name; }
    public string Name     { get; }
    public string Upper    => Name.ToUpper();
}
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Item(""abc"")));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task NullValuesAreIncludedByDefault()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item { public string Name { get; set; } public int? Value { get; set; } public object Ref { get; set; } }
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Item()));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task NestedObjectsSerialize()
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
        Console.WriteLine(JsonConvert.SerializeObject(new Outer { Child = new Inner { V = 7 } }));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task InheritedMembersSerialize()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Base    { public int    Id   { get; set; } }
public class Derived : Base { public string Name { get; set; } }
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Derived { Id = 1, Name = ""n"" }));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task IndentedFormattingMatchesJsonNet()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Inner { public int V { get; set; } }
public class Item  { public string Name { get; set; } public Inner Child { get; set; } }
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Item { Name = ""a"", Child = new Inner { V = 1 } }, Formatting.Indented));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task EnumsSerializeAsTheirNumericValue()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public enum Color { Red = 0, Green = 1, Blue = 7 }
[Flags] public enum Access { None = 0, Read = 1, Write = 2, All = 3 }
public class Item { public Color C { get; set; } public Access A { get; set; } public Color? Maybe { get; set; } }
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Item { C = Color.Blue, A = Access.Read | Access.Write }));
        Console.WriteLine(JsonConvert.SerializeObject(new Item { C = Color.Red, A = Access.None, Maybe = Color.Green }));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task StringsAreEscaped()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item { public string S { get; set; } }
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Item { S = ""quote:\"" backslash:\\ tab:\t newline:\n"" }));
        Console.WriteLine(JsonConvert.SerializeObject(new Item { S = ""unicode: é 中"" }));
        Console.WriteLine(JsonConvert.SerializeObject(new Item { S = """" }));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task RootLevelPrimitivesSerialize()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(null));
        Console.WriteLine(JsonConvert.SerializeObject(""text""));
        Console.WriteLine(JsonConvert.SerializeObject(42));
        Console.WriteLine(JsonConvert.SerializeObject(true));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task StructsSerialize()
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
        Console.WriteLine(JsonConvert.SerializeObject(new Point { X = 1, Y = 2 }));
        Console.WriteLine(JsonConvert.SerializeObject(new Holder { P = new Point { X = 3, Y = 4 } }));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task ObjectTypedPropertyUsesTheRuntimeType()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Inner  { public int V { get; set; } }
public class Holder { public object Any { get; set; } }
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Holder { Any = new Inner { V = 5 } }));
        Console.WriteLine(JsonConvert.SerializeObject(new Holder { Any = ""text"" }));
        Console.WriteLine(JsonConvert.SerializeObject(new Holder { Any = 12 }));
        Console.WriteLine(JsonConvert.SerializeObject(new Holder { Any = true }));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task DerivedInstanceInBaseTypedPropertyWritesItsOwnMembers()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Animal { public string Name { get; set; } }
public class Dog : Animal { public bool GoodBoy { get; set; } }
public class Holder { public Animal Pet { get; set; } }
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Holder { Pet = new Dog { Name = ""Rex"", GoodBoy = true } }));
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// A reference cycle: Json.NET throws a JsonSerializationException naming the looping property,
    /// while the binding library's cycle guard silently drops the back-reference (an undefined value,
    /// which JSON.stringify omits). Serializing a cyclic graph therefore succeeds here.
    /// </summary>
    [TestMethod]
    public async Task SelfReferencingLoopIsDroppedInsteadOfThrowing()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Node
{
    public string Name   { get; set; }
    public Node   Parent { get; set; }
    public Node   Child  { get; set; }
}
public class App
{
    public static void Main()
    {
        var parent = new Node { Name = ""p"" };
        var child  = new Node { Name = ""c"", Parent = parent };
        parent.Child = child;

        try   { Console.WriteLine(JsonConvert.SerializeObject(parent)); }
        catch (Exception ex) { Console.WriteLine(ex.GetType().Name); }
    }
}";
        await RunJs(code,
            expected:     @"{""Child"":{""Child"":null,""Name"":""c""},""Name"":""p"",""Parent"":null}",
            nativePrints: "JsonSerializationException");
    }

    [TestMethod]
    public async Task AnonymousTypesSerialize()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new { Name = ""a"", Value = 1, Nested = new { Flag = true } }));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task DictionaryRootSerializes()
    {
        var code = @"
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
public class App
{
    public static void Main()
    {
        var d = new Dictionary<string, int> { [""a""] = 1, [""b""] = 2 };
        Console.WriteLine(JsonConvert.SerializeObject(d));
        Console.WriteLine(JsonConvert.SerializeObject(new Dictionary<string, int>()));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task SerializeObjectWithExplicitTypeArgument()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Animal { public string Name { get; set; } }
public class Dog : Animal { public bool GoodBoy { get; set; } }
public class App
{
    public static void Main()
    {
        Animal pet = new Dog { Name = ""Rex"", GoodBoy = true };
        Console.WriteLine(JsonConvert.SerializeObject(pet, typeof(Animal), null));
    }
}";
        await RunAndCompare(code);
    }
}
