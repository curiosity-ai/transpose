namespace Transpose.SystemTextJson.Tests;

/// <summary>
/// What happens on bad input. System.Text.Json is markedly stricter than Json.NET here — a null into a
/// non-nullable value type, a string where a number is expected, and a shape mismatch are all errors
/// rather than silently-defaulted members — so these are the cases most likely to surface during a
/// migration.
/// </summary>
[TestClass]
public sealed class ErrorHandlingTests : JsonTestBase
{
    private const string Model = """
        using System;
        using System.Collections.Generic;
        using System.Text.Json;

        public class T { public string Name { get; set; } public int Count { get; set; } }

        public static class Program
        {
            static void Try(string label, Func<string> f)
            {
                try                   { Console.WriteLine(label + ": " + f()); }
                catch (JsonException) { Console.WriteLine(label + ": JsonException"); }
            }

            public static void Main()
            {
        __BODY__
            }
        }
        """;

    private static string With(string body) => Model.Replace("__BODY__", body);

    [TestMethod]
    public async Task MalformedInput() => await RunAndCompare(With("""
                Try("unclosed object", () => JsonSerializer.Deserialize<T>("{").Name);
                Try("unclosed array",  () => JsonSerializer.Deserialize<int[]>("[1,2").Length.ToString());
                Try("garbage",         () => JsonSerializer.Deserialize<T>("not json").Name);
                Try("empty",           () => JsonSerializer.Deserialize<T>("").Name);
                Try("whitespace",      () => JsonSerializer.Deserialize<T>("   ").Name);
                Try("bare comma",      () => JsonSerializer.Deserialize<T>("{,}").Name);
                Try("missing value",   () => JsonSerializer.Deserialize<T>("{\"Name\":}").Name);
                Try("trailing junk",   () => JsonSerializer.Deserialize<T>("{} extra").Name);
        """));

    [TestMethod]
    public async Task NullIntoANonNullableValueTypeIsAnError() => await RunAndCompare(With("""
                Try("int",      () => JsonSerializer.Deserialize<T>("{\"Count\":null}").Count.ToString());
                Try("string",   () => JsonSerializer.Deserialize<T>("{\"Name\":null}").Name ?? "<null>");
        """));

    [TestMethod]
    public async Task ATypeMismatchIsAnError() => await RunAndCompare(With("""
                Try("string into int",  () => JsonSerializer.Deserialize<T>("{\"Count\":\"7\"}").Count.ToString());
                Try("object into int",  () => JsonSerializer.Deserialize<T>("{\"Count\":{}}").Count.ToString());
                Try("array into int",   () => JsonSerializer.Deserialize<T>("{\"Count\":[1]}").Count.ToString());
                Try("bool into int",    () => JsonSerializer.Deserialize<T>("{\"Count\":true}").Count.ToString());
                Try("number into obj",  () => JsonSerializer.Deserialize<T>("5").Name ?? "<null>");
        """));

    [TestMethod]
    public async Task AShapeMismatchIsAnError() => await RunAndCompare(With("""
                Try("array into object", () => JsonSerializer.Deserialize<T>("[1,2]").Name ?? "<null>");
                Try("object into list",  () => JsonSerializer.Deserialize<List<int>>("{\"a\":1}").Count.ToString());
                Try("object into array", () => JsonSerializer.Deserialize<int[]>("{\"a\":1}").Length.ToString());
        """));

    [TestMethod]
    public async Task AnInvalidEnumValueIsAnError() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public enum Colour { Red = 0, Green = 5 }
        public class T { public Colour C { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                try                   { Console.WriteLine(JsonSerializer.Deserialize<T>("{\"C\":\"Nope\"}").C); }
                catch (JsonException) { Console.WriteLine("JsonException"); }
                catch (ArgumentException) { Console.WriteLine("ArgumentException"); }
            }
        }
        """);

    [TestMethod]
    public async Task AnUndeclaredEnumNumberIsAccepted() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public enum Colour { Red = 0, Green = 5 }
        public class T { public Colour C { get; set; } }

        public static class Program
        {
            public static void Main() => Console.WriteLine((int)JsonSerializer.Deserialize<T>("{\"C\":77}").C);
        }
        """);

    [TestMethod]
    public async Task ADeeplyNestedPayloadHitsTheDepthLimit() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class Node { public Node Next { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var deep = new string('x', 0);
                for (int i = 0; i < 70; i++) deep += "{\"Next\":";
                deep += "null";
                for (int i = 0; i < 70; i++) deep += "}";

                try                   { Console.WriteLine(JsonSerializer.Deserialize<Node>(deep) is null ? "<null>" : "ok"); }
                catch (JsonException) { Console.WriteLine("JsonException"); }
            }
        }
        """);

    [TestMethod]
    public async Task AJsonExceptionIsCatchableAsAnException() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string Name { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                try
                {
                    JsonSerializer.Deserialize<T>("{");
                    Console.WriteLine("no throw");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e is JsonException);
                }
            }
        }
        """);
}
