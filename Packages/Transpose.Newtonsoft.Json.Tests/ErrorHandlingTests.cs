using System.Threading.Tasks;

namespace Transpose.Newtonsoft.Json.Tests;

/// <summary>What happens when the JSON is malformed, or does not fit the target type.</summary>
[TestClass]
public class ErrorHandlingTests : JsonTestBase
{
    private const string Header = @"
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
public class Item { public string Name { get; set; } public int Value { get; set; } }
";

    [TestMethod]
    public async Task MalformedJsonThrowsAJsonException()
    {
        var code = Header + @"
public class App
{
    public static void Main()
    {
        foreach (var bad in new[] { ""{ this is not json"", ""{\""Name\"": }"", ""[1,2"" })
        {
            try   { JsonConvert.DeserializeObject<Item>(bad); Console.WriteLine(""no throw""); }
            catch (JsonException) { Console.WriteLine(""JsonException""); }
        }
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task NonNumericStringIntoANumberThrows()
    {
        var code = Header + @"
public class App
{
    public static void Main()
    {
        try   { JsonConvert.DeserializeObject<Item>(""{\""Value\"":\""abc\""}""); Console.WriteLine(""no throw""); }
        catch (JsonException) { Console.WriteLine(""JsonException""); }
        catch (Exception ex)  { Console.WriteLine(ex.GetType().Name); }
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// A JSON array where an object is expected: Json.NET fails with a JsonSerializationException,
    /// the binding library returns an empty instance of the target type instead. Client code that
    /// receives an unexpected shape therefore sees empty data rather than an error.
    /// </summary>
    [TestMethod]
    public async Task ArrayIntoAnObjectTargetYieldsAnEmptyInstance()
    {
        var code = Header + @"
public class App
{
    public static void Main()
    {
        try
        {
            var item = JsonConvert.DeserializeObject<Item>(""[1,2,3]"");
            Console.WriteLine(""got: "" + (item == null ? ""<null>"" : (item.Name ?? ""<null name>"") + ""|"" + item.Value));
        }
        catch (Exception ex) { Console.WriteLine(ex.GetType().Name); }
    }
}";
        await RunJs(code,
            expected:     "got: <null name>|0",
            nativePrints: "JsonSerializationException");
    }

    /// <summary>
    /// An object where an array is expected: Json.NET throws, the binding library returns an empty
    /// collection. Same shape of leniency as the array-into-object case above.
    /// </summary>
    [TestMethod]
    public async Task ObjectIntoACollectionTargetYieldsAnEmptyCollection()
    {
        var code = Header + @"
public class App
{
    public static void Main()
    {
        try   { Console.WriteLine(""list: "" + JsonConvert.DeserializeObject<List<int>>(""{\""a\"":1}"").Count); }
        catch (Exception ex) { Console.WriteLine(ex.GetType().Name); }

        try   { Console.WriteLine(""array: "" + JsonConvert.DeserializeObject<int[]>(""{\""a\"":1}"").Length); }
        catch (Exception ex) { Console.WriteLine(ex.GetType().Name); }
    }
}";
        await RunJs(code,
            expected:     "list: 0\narray: 0",
            nativePrints: "JsonSerializationException\nJsonSerializationException");
    }

    /// <summary>
    /// Both reject an unknown enum member name, but the binding library surfaces the underlying
    /// ArgumentException from Enum.Parse where Json.NET wraps it in a JsonSerializationException —
    /// so a <c>catch (JsonException)</c> around a deserialize call does not catch this one.
    /// </summary>
    [TestMethod]
    public async Task UnknownEnumNameThrowsArgumentExceptionNotJsonException()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public enum Color { Red = 0, Green = 1 }
public class Item { public Color C { get; set; } }
public class App
{
    public static void Main()
    {
        try   { JsonConvert.DeserializeObject<Item>(""{\""C\"":\""Mauve\""}""); Console.WriteLine(""no throw""); }
        catch (Exception ex) { Console.WriteLine(ex.GetType().Name); }
    }
}";
        await RunJs(code,
            expected:     "ArgumentException",
            nativePrints: "JsonSerializationException");
    }

    [TestMethod]
    public async Task ThrownExceptionsCarryAMessage()
    {
        var code = Header + @"
public class App
{
    public static void Main()
    {
        try
        {
            JsonConvert.DeserializeObject<Item>(""}{"");
            Console.WriteLine(""no throw"");
        }
        catch (JsonException ex)
        {
            Console.WriteLine(ex.Message.Length > 0);
            Console.WriteLine(ex is JsonException);
        }
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task JsonSerializationExceptionIsAJsonException()
    {
        var code = @"
using System;
using Newtonsoft.Json;
public class Item { [JsonProperty(Required = Required.Always)] public string Name { get; set; } }
public class App
{
    public static void Main()
    {
        try   { JsonConvert.DeserializeObject<Item>(""{}""); Console.WriteLine(""no throw""); }
        catch (JsonException ex) { Console.WriteLine(ex.GetType().Name + ""|"" + (ex is JsonSerializationException)); }
    }
}";
        await RunAndCompare(code);
    }
}
