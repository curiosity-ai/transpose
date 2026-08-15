namespace Transpose.SystemTextJson.Tests;

/// <summary>
/// How a payload maps back onto a type: member matching (which is case-sensitive by default, unlike
/// Json.NET), constructor binding, missing and unknown members, and primitive conversions.
/// </summary>
[TestClass]
public sealed class DeserializationTests : JsonTestBase
{
    // ---------------------------------------------------------------------------------------------
    // Member matching
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task MembersMatchByExactName() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string Name { get; set; } public int Count { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var t = JsonSerializer.Deserialize<T>("{\"Name\":\"n\",\"Count\":7}");
                Console.WriteLine(t.Name + "/" + t.Count);
            }
        }
        """);

    [TestMethod]
    public async Task MatchingIsCaseSensitiveByDefault() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string Name { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(JsonSerializer.Deserialize<T>("{\"name\":\"n\"}").Name ?? "<null>");
                Console.WriteLine(JsonSerializer.Deserialize<T>("{\"NAME\":\"n\"}").Name ?? "<null>");
                Console.WriteLine(JsonSerializer.Deserialize<T>("{\"Name\":\"n\"}").Name ?? "<null>");
            }
        }
        """);

    [TestMethod]
    public async Task PropertyNameCaseInsensitiveRelaxesMatching() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string Name { get; set; } public int Count { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var t = JsonSerializer.Deserialize<T>("{\"name\":\"n\",\"COUNT\":3}", options);
                Console.WriteLine(t.Name + "/" + t.Count);
            }
        }
        """);

    [TestMethod]
    public async Task AnUnknownMemberIsIgnored() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string Name { get; set; } }

        public static class Program
        {
            public static void Main()
                => Console.WriteLine(JsonSerializer.Deserialize<T>("{\"Nope\":1,\"Name\":\"n\",\"Also\":{\"deep\":[1,2]}}").Name);
        }
        """);

    [TestMethod]
    public async Task AMissingMemberKeepsWhateverTheConstructorLeft() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T
        {
            public string Name  { get; set; } = "default";
            public int    Count { get; set; } = 42;
        }

        public static class Program
        {
            public static void Main()
            {
                var t = JsonSerializer.Deserialize<T>("{}");
                Console.WriteLine(t.Name + "/" + t.Count);
            }
        }
        """);

    [TestMethod]
    public async Task APropertyWithANonPublicSetterIsNotWritten() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string Value { get; private set; } = "original"; }

        public static class Program
        {
            public static void Main()
                => Console.WriteLine(JsonSerializer.Deserialize<T>("{\"Value\":\"changed\"}").Value);
        }
        """);

    [TestMethod]
    public async Task AGetOnlyPropertyIsNotWritten() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T
        {
            public string Stored { get; set; } = "s";
            public string Computed => "computed";
        }

        public static class Program
        {
            public static void Main()
            {
                var t = JsonSerializer.Deserialize<T>("{\"Stored\":\"x\",\"Computed\":\"ignored\"}");
                Console.WriteLine(t.Stored + "/" + t.Computed);
            }
        }
        """);

    // ---------------------------------------------------------------------------------------------
    // Constructors
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task AParameterlessConstructorIsPreferred() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T
        {
            public T()                { Which = "parameterless"; }
            public T(string name)     { Which = "parameterized"; Name = name; }
            public string Name  { get; set; }
            public string Which { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var t = JsonSerializer.Deserialize<T>("{\"Name\":\"n\"}");
                Console.WriteLine(t.Which + "/" + t.Name);
            }
        }
        """);

    [TestMethod]
    public async Task TheSinglePublicConstructorBindsItsParameters() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T
        {
            public T(string name, int count) { Name = name; Count = count; }
            public string Name  { get; }
            public int    Count { get; }
        }

        public static class Program
        {
            public static void Main()
            {
                var t = JsonSerializer.Deserialize<T>("{\"Name\":\"n\",\"Count\":5}");
                Console.WriteLine(t.Name + "/" + t.Count);
            }
        }
        """);

    [TestMethod]
    public async Task AConstructorParameterWithNoMatchingMemberGetsTheDefault() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T
        {
            public T(string name, int count) { Name = name; Count = count; }
            public string Name  { get; }
            public int    Count { get; }
        }

        public static class Program
        {
            public static void Main()
            {
                var t = JsonSerializer.Deserialize<T>("{\"Name\":\"n\"}");
                Console.WriteLine((t.Name ?? "<null>") + "/" + t.Count);
            }
        }
        """);

    [TestMethod]
    public async Task JsonConstructorSelectsTheConstructor() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public class T
        {
            public T() { Which = "parameterless"; }

            [JsonConstructor]
            public T(string name) { Which = "attributed"; Name = name; }

            public string Name  { get; set; }
            public string Which { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var t = JsonSerializer.Deserialize<T>("{\"Name\":\"n\"}");
                Console.WriteLine(t.Which + "/" + t.Name);
            }
        }
        """);

    [TestMethod]
    public async Task AConstructorParameterInheritsItsMembersJsonName() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public class T
        {
            public T(string name) { Name = name; }

            [JsonPropertyName("n")]
            public string Name { get; }
        }

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(JsonSerializer.Deserialize<T>("{\"n\":\"x\"}").Name ?? "<null>");
                Console.WriteLine(JsonSerializer.Serialize(new T("x")));
            }
        }
        """);

    [TestMethod]
    public async Task AMemberBoundThroughAConstructorIsNotWrittenTwice() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T
        {
            public T(string name) { Name = (name ?? "<null>") + "-viaCtor"; }
            public string Name { get; set; }
        }

        public static class Program
        {
            public static void Main()
                => Console.WriteLine(JsonSerializer.Deserialize<T>("{\"Name\":\"n\"}").Name);
        }
        """);

    // ---------------------------------------------------------------------------------------------
    // Values
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task PrimitiveMembersAreConverted() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T
        {
            public bool   B  { get; set; }
            public int    I  { get; set; }
            public double D  { get; set; }
            public string S  { get; set; }
            public int?   NI { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var t = JsonSerializer.Deserialize<T>("{\"B\":true,\"I\":-3,\"D\":1.5,\"S\":\"s\",\"NI\":null}");
                Console.WriteLine(t.B + "/" + t.I + "/" + t.D + "/" + t.S + "/" + (t.NI.HasValue ? t.NI.ToString() : "<null>"));
            }
        }
        """);

    [TestMethod]
    public async Task AnEnumIsReadFromItsNumber() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public enum Colour { Red = 0, Green = 5 }
        public class T { public Colour C { get; set; } public Colour? N { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var t = JsonSerializer.Deserialize<T>("{\"C\":5,\"N\":0}");
                Console.WriteLine(t.C + "/" + t.N);
            }
        }
        """);

    [TestMethod]
    public async Task AnEnumIsAlsoReadFromItsName() => await RunJs("""
        using System;
        using System.Text.Json;

        public enum Colour { Red = 0, Green = 5 }
        public class T { public Colour C { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                try
                {
                    Console.WriteLine(JsonSerializer.Deserialize<T>("{\"C\":\"Green\"}").C);
                }
                catch (JsonException)
                {
                    Console.WriteLine("JsonException");
                }
            }
        }
        """,
        expected:     "Green",
        nativePrints: "JsonException");

    [TestMethod]
    public async Task ALiteralNullDocumentReturnsNull() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string A { get; set; } }

        public static class Program
        {
            public static void Main() => Console.WriteLine(JsonSerializer.Deserialize<T>("null") is null ? "<null>" : "instance");
        }
        """);

    [TestMethod]
    public async Task ATopLevelScalarDocument() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(JsonSerializer.Deserialize<int>("42"));
                Console.WriteLine(JsonSerializer.Deserialize<string>("\"hello\""));
                Console.WriteLine(JsonSerializer.Deserialize<bool>("true"));
                Console.WriteLine(JsonSerializer.Deserialize<double>("1.25"));
            }
        }
        """);

    [TestMethod]
    public async Task DeserializingToObjectReturnsTheRawParsedValue() => await RunJs("""
        using System;
        using System.Text.Json;

        public static class Program
        {
            public static void Main()
            {
                var value = JsonSerializer.Deserialize<object>("{\"a\":1}");
                Console.WriteLine(value == null ? "<null>" : "value");
                Console.WriteLine(JsonSerializer.Serialize(value));
            }
        }
        """,
        expected:     "value\n{\"a\":1}",
        nativePrints: "value\n{\"a\":1}");

    [TestMethod]
    public async Task ARoundTripPreservesEveryMember() => await RunAndCompare("""
        using System;
        using System.Collections.Generic;
        using System.Text.Json;

        public enum Kind { One = 1, Two = 2 }
        public class Inner { public string S { get; set; } public int I { get; set; } }
        public class T
        {
            public string        Name   { get; set; }
            public int           Count  { get; set; }
            public bool          Flag   { get; set; }
            public double        Ratio  { get; set; }
            public Kind          Kind   { get; set; }
            public Inner         Inner  { get; set; }
            public List<string>  Tags   { get; set; }
            public int[]         Nums   { get; set; }
            public string        Absent { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var original = new T
                {
                    Name  = "n", Count = 3, Flag = true, Ratio = 1.5, Kind = Kind.Two,
                    Inner = new Inner { S = "s", I = 9 },
                    Tags  = new List<string> { "a", "b" },
                    Nums  = new[] { 1, 2, 3 }
                };

                var json = JsonSerializer.Serialize(original);
                var back = JsonSerializer.Deserialize<T>(json);

                Console.WriteLine(JsonSerializer.Serialize(back));
                Console.WriteLine(JsonSerializer.Serialize(back) == json);
            }
        }
        """);
}
