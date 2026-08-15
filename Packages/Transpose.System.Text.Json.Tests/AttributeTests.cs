namespace Transpose.SystemTextJson.Tests;

/// <summary>
/// The <c>System.Text.Json.Serialization</c> attributes: renaming, ignoring, opting in, ordering and
/// per-member number handling.
/// </summary>
[TestClass]
public sealed class AttributeTests : JsonTestBase
{
    // ---------------------------------------------------------------------------------------------
    // [JsonPropertyName]
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task JsonPropertyNameRenamesOnWriteAndRead() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public class T
        {
            [JsonPropertyName("n")]    public string Name  { get; set; }
            [JsonPropertyName("cnt")]  public int    Count { get; set; }
                                       public bool   Plain { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var json = JsonSerializer.Serialize(new T { Name = "a", Count = 2, Plain = true });
                Console.WriteLine(json);

                var back = JsonSerializer.Deserialize<T>(json);
                Console.WriteLine(back.Name + "/" + back.Count + "/" + back.Plain);
            }
        }
        """);

    [TestMethod]
    public async Task JsonPropertyNameBeatsTheNamingPolicy() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public class T
        {
            [JsonPropertyName("KeepMe")] public string Renamed { get; set; }
                                         public string Other   { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                Console.WriteLine(JsonSerializer.Serialize(new T { Renamed = "a", Other = "b" }, options));
            }
        }
        """);

    [TestMethod]
    public async Task TheOriginalMemberNameNoLongerMatchesOnceRenamed() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public class T { [JsonPropertyName("n")] public string Name { get; set; } }

        public static class Program
        {
            public static void Main()
                => Console.WriteLine(JsonSerializer.Deserialize<T>("{\"Name\":\"x\"}").Name ?? "<null>");
        }
        """);

    // ---------------------------------------------------------------------------------------------
    // [JsonIgnore]
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task JsonIgnoreRemovesTheMemberEntirely() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public class T
        {
            public string Kept { get; set; } = "kept";
            [JsonIgnore] public string Dropped { get; set; } = "dropped";
        }

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(JsonSerializer.Serialize(new T()));
                Console.WriteLine(JsonSerializer.Deserialize<T>("{\"Dropped\":\"changed\"}").Dropped);
            }
        }
        """);

    [TestMethod]
    public async Task JsonIgnoreWhenWritingNull() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public class T
        {
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Maybe { get; set; }
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int?   MaybeInt { get; set; }
            public string Always { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(JsonSerializer.Serialize(new T()));
                Console.WriteLine(JsonSerializer.Serialize(new T { Maybe = "m", MaybeInt = 0, Always = "a" }));
            }
        }
        """);

    [TestMethod]
    public async Task JsonIgnoreWhenWritingDefault() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public class T
        {
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int    Count { get; set; }
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool   Flag  { get; set; }
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string Name  { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(JsonSerializer.Serialize(new T()));
                Console.WriteLine(JsonSerializer.Serialize(new T { Count = 1, Flag = true, Name = "n" }));
            }
        }
        """);

    [TestMethod]
    public async Task JsonIgnoreNeverOverridesTheOptionsWideCondition() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public class T
        {
            [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string Kept { get; set; }
            public string Dropped { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
                Console.WriteLine(JsonSerializer.Serialize(new T(), options));
            }
        }
        """);

    // ---------------------------------------------------------------------------------------------
    // [JsonInclude]
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task JsonIncludeOptsInAPublicField() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public class T
        {
            [JsonInclude] public string Included;
                          public string Excluded;
                          public string Prop { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var json = JsonSerializer.Serialize(new T { Included = "i", Excluded = "e", Prop = "p" });
                Console.WriteLine(json);

                var back = JsonSerializer.Deserialize<T>("{\"Included\":\"x\",\"Excluded\":\"y\"}");
                Console.WriteLine((back.Included ?? "<null>") + "/" + (back.Excluded ?? "<null>"));
            }
        }
        """);

    [TestMethod]
    public async Task JsonIncludeOptsInANonPublicSetter() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public class T
        {
            [JsonInclude] public string Opted  { get; private set; } = "original";
                          public string Closed { get; private set; } = "original";
        }

        public static class Program
        {
            public static void Main()
            {
                var back = JsonSerializer.Deserialize<T>("{\"Opted\":\"changed\",\"Closed\":\"changed\"}");
                Console.WriteLine(back.Opted + "/" + back.Closed);
            }
        }
        """);

    // ---------------------------------------------------------------------------------------------
    // [JsonPropertyOrder]
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task JsonPropertyOrderPositionsMembers() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public class T
        {
            [JsonPropertyOrder(3)]  public int Third  { get; set; }
            [JsonPropertyOrder(1)]  public int First  { get; set; }
            [JsonPropertyOrder(2)]  public int Second { get; set; }
        }

        public static class Program
        {
            public static void Main()
                => Console.WriteLine(JsonSerializer.Serialize(new T { First = 1, Second = 2, Third = 3 }));
        }
        """, exactMemberOrder: true);

    [TestMethod]
    public async Task ANegativeOrderSortsBeforeTheUnordered() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public class T
        {
            public int Unordered { get; set; }
            [JsonPropertyOrder(-1)] public int Early { get; set; }
            [JsonPropertyOrder(10)] public int Late  { get; set; }
        }

        public static class Program
        {
            public static void Main()
                => Console.WriteLine(JsonSerializer.Serialize(new T { Unordered = 1, Early = 2, Late = 3 }));
        }
        """, exactMemberOrder: true);

    // ---------------------------------------------------------------------------------------------
    // [JsonNumberHandling]
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task JsonNumberHandlingAllowsReadingAMemberFromAString() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public class T
        {
            [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int Lenient { get; set; }
            public int Strict { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(JsonSerializer.Deserialize<T>("{\"Lenient\":\"7\"}").Lenient);

                try
                {
                    Console.WriteLine(JsonSerializer.Deserialize<T>("{\"Strict\":\"7\"}").Strict);
                }
                catch (JsonException)
                {
                    Console.WriteLine("JsonException");
                }
            }
        }
        """);
}
