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

    /// <summary>
    /// The one deliberate deviation: a null document reads back as the target's default instead of
    /// raising <c>ArgumentNullException</c>. See the "Known divergences" table in the README — this
    /// matches <c>Transpose.Newtonsoft.Json</c>, which every front-end moving onto this package was
    /// written against. An empty or malformed document still throws (asserted by
    /// <see cref="MalformedInput"/>), so only "no document at all" is affected.
    /// </summary>
    [TestMethod]
    public async Task ANullDocumentReadsBackAsTheDefault() => await RunJs("""
        using System;
        using System.Text.Json;

        public static class Program
        {
            static void Try(string label, Func<string> f)
            {
                try                           { Console.WriteLine(label + ": " + f()); }
                catch (ArgumentNullException) { Console.WriteLine(label + ": ArgumentNullException"); }
            }

            public static void Main()
            {
                Try("string", () => JsonSerializer.Deserialize<string>((string)null) ?? "<null>");
                Try("int",    () => JsonSerializer.Deserialize<int>((string)null).ToString());
                Try("array",  () => JsonSerializer.Deserialize<int[]>((string)null) is null ? "<null>" : "not null");
            }
        }
        """,
        expected:     "string: <null>\nint: 0\narray: <null>",
        nativePrints: "string: ArgumentNullException\nint: ArgumentNullException\narray: ArgumentNullException");

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

    // ---------------------------------------------------------------------------------------------
    // A number outside the target's range
    // ---------------------------------------------------------------------------------------------

    // The integer branches of readNumber used to coerce with `raw | 0` / `raw >>> 0`, which wraps
    // instead of rejecting: a byte read from -1 came back as 4294967295 and a ushort read from 70000
    // stayed 70000 — values outside the member's own type, handed to the application as if they had
    // been valid. Every one of these throws in System.Text.Json.
    [TestMethod]
    public async Task AnIntegerOutsideTheTargetsRangeThrows() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T
        {
            public byte   B  { get; set; }
            public sbyte  SB { get; set; }
            public short  S  { get; set; }
            public ushort US { get; set; }
            public int    I  { get; set; }
            public uint   UI { get; set; }
        }

        public static class Program
        {
            static void Read(string json)
            {
                try { Console.WriteLine(JsonSerializer.Serialize(JsonSerializer.Deserialize<T>(json))); }
                catch (JsonException) { Console.WriteLine("JsonException"); }
            }

            public static void Main()
            {
                Read("{\"B\":-1}");
                Read("{\"B\":300}");
                Read("{\"SB\":200}");
                Read("{\"S\":40000}");
                Read("{\"US\":70000}");
                Read("{\"US\":-1}");
                Read("{\"I\":3000000000}");
                Read("{\"UI\":-1}");
            }
        }
        """);

    // A value with a real fraction is not an integer.
    [TestMethod]
    public async Task AFractionalNumberIntoAnIntegerThrows() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public int I { get; set; } }

        public static class Program
        {
            static void Read(string json)
            {
                try { Console.WriteLine(JsonSerializer.Deserialize<T>(json).I); }
                catch (JsonException) { Console.WriteLine("JsonException"); }
            }

            public static void Main()
            {
                Read("{\"I\":1.5}");
                Read("{\"I\":-1.5}");
                Read("{\"I\":7}");
            }
        }
        """);

    // `1.0` into an int is where the two part company, and it cannot be helped at this layer:
    // System.Text.Json reads the token's TEXT and rejects it for carrying a decimal point, while the
    // document here has already been through JSON.parse, which resolves `1.0` and `1` to the same
    // JavaScript number. The value is integral, so it is accepted. Recorded rather than fixed.
    [TestMethod]
    public async Task AnIntegralNumberWrittenWithADecimalPointIsAccepted() => await RunJs("""
        using System;
        using System.Text.Json;

        public class T { public int I { get; set; } }

        public static class Program
        {
            static void Read(string json)
            {
                try { Console.WriteLine(JsonSerializer.Deserialize<T>(json).I); }
                catch (JsonException) { Console.WriteLine("JsonException"); }
            }

            public static void Main()
            {
                Read("{\"I\":1.0}");
                Read("{\"I\":2e0}");
            }
        }
        """,
        expected:      "1\n2",
        nativePrints:  "JsonException\nJsonException");

    // The boundary values themselves stay valid — the guard must not be off by one.
    [TestMethod]
    public async Task TheRangeBoundariesThemselvesAreAccepted() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T
        {
            public byte   B  { get; set; }
            public sbyte  SB { get; set; }
            public short  S  { get; set; }
            public ushort US { get; set; }
            public int    I  { get; set; }
            public uint   UI { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var json = "{\"B\":255,\"SB\":-128,\"S\":-32768,\"US\":65535,\"I\":-2147483648,\"UI\":4294967295}";
                var t = JsonSerializer.Deserialize<T>(json);
                Console.WriteLine(t.B + "/" + t.SB + "/" + t.S + "/" + t.US + "/" + t.I + "/" + t.UI);
                Console.WriteLine(JsonSerializer.Serialize(t));
            }
        }
        """);
}
