namespace Transpose.SystemTextJson.Tests;

/// <summary>
/// <see cref="System.Text.Json.JsonSerializerOptions"/>: naming policies, case sensitivity, the
/// options-wide ignore condition, indentation, fields, and the lenient read switches.
/// </summary>
[TestClass]
public sealed class OptionsTests : JsonTestBase
{
    // ---------------------------------------------------------------------------------------------
    // Naming policies
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task CamelCaseLowersTheWholeLeadingRunOfCapitals() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public static class Program
        {
            public static void Main()
            {
                var policy = JsonNamingPolicy.CamelCase;

                Console.WriteLine(policy.ConvertName("Name"));
                Console.WriteLine(policy.ConvertName("ALLCAPS"));
                Console.WriteLine(policy.ConvertName("HTTPRequest"));
                Console.WriteLine(policy.ConvertName("A"));
                Console.WriteLine(policy.ConvertName("alreadyCamel"));
                Console.WriteLine(policy.ConvertName(""));
            }
        }
        """);

    [TestMethod]
    public async Task SnakeAndKebabPolicies() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(JsonNamingPolicy.SnakeCaseLower.ConvertName("HTTPRequestName"));
                Console.WriteLine(JsonNamingPolicy.SnakeCaseUpper.ConvertName("HTTPRequestName"));
                Console.WriteLine(JsonNamingPolicy.KebabCaseLower.ConvertName("HTTPRequestName"));
                Console.WriteLine(JsonNamingPolicy.KebabCaseUpper.ConvertName("HTTPRequestName"));
                Console.WriteLine(JsonNamingPolicy.SnakeCaseLower.ConvertName("Simple"));
            }
        }
        """);

    [TestMethod]
    public async Task ANamingPolicyAppliesOnWriteAndOnRead() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string FirstName { get; set; } public int ItemCount { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

                var json = JsonSerializer.Serialize(new T { FirstName = "Ada", ItemCount = 2 }, options);
                Console.WriteLine(json);

                var back = JsonSerializer.Deserialize<T>(json, options);
                Console.WriteLine(back.FirstName + "/" + back.ItemCount);

                // Without the policy the camel-cased payload no longer matches.
                Console.WriteLine(JsonSerializer.Deserialize<T>(json).FirstName ?? "<null>");
            }
        }
        """);

    [TestMethod]
    public async Task ACustomNamingPolicyIsUsed() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public sealed class PrefixPolicy : JsonNamingPolicy
        {
            public override string ConvertName(string name) => "x_" + name;
        }

        public class T { public string Name { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var options = new JsonSerializerOptions { PropertyNamingPolicy = new PrefixPolicy() };

                var json = JsonSerializer.Serialize(new T { Name = "n" }, options);
                Console.WriteLine(json);
                Console.WriteLine(JsonSerializer.Deserialize<T>(json, options).Name);
            }
        }
        """);

    [TestMethod]
    public async Task DictionaryKeyPolicyRenamesKeysOnly() => await RunAndCompare("""
        using System;
        using System.Collections.Generic;
        using System.Text.Json;

        public static class Program
        {
            public static void Main()
            {
                var options = new JsonSerializerOptions { DictionaryKeyPolicy = JsonNamingPolicy.CamelCase };
                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, int> { ["FirstKey"] = 1, ["SecondKey"] = 2 }, options));
            }
        }
        """);

    // ---------------------------------------------------------------------------------------------
    // Ignore conditions
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task DefaultIgnoreConditionWhenWritingNull() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public class T { public string A { get; set; } public int B { get; set; } public string C { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
                Console.WriteLine(JsonSerializer.Serialize(new T { C = "c" }, options));
            }
        }
        """);

    [TestMethod]
    public async Task DefaultIgnoreConditionWhenWritingDefaultAlsoDropsNulls() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public enum E { Zero = 0, One = 1 }
        public class T
        {
            public string A { get; set; }
            public int    B { get; set; }
            public bool   C { get; set; }
            public double D { get; set; }
            public E      Enum { get; set; }
            public int    Set  { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
                Console.WriteLine(JsonSerializer.Serialize(new T { Set = 5 }, options));
            }
        }
        """);

    // ---------------------------------------------------------------------------------------------
    // Read switches
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task ATrailingCommaIsRejectedUnlessAllowed() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string A { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                try   { Console.WriteLine(JsonSerializer.Deserialize<T>("{\"A\":\"a\",}").A); }
                catch (JsonException) { Console.WriteLine("JsonException"); }

                var options = new JsonSerializerOptions { AllowTrailingCommas = true };
                Console.WriteLine(JsonSerializer.Deserialize<T>("{\"A\":\"a\",}", options).A);
                Console.WriteLine(JsonSerializer.Deserialize<int[]>("[1,2,]", options).Length);
            }
        }
        """);

    [TestMethod]
    public async Task ACommentIsRejectedUnlessSkipped() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string A { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                try   { Console.WriteLine(JsonSerializer.Deserialize<T>("{/* c */\"A\":\"a\"}").A); }
                catch (JsonException) { Console.WriteLine("JsonException"); }

                var options = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip };
                Console.WriteLine(JsonSerializer.Deserialize<T>("{/* c */\"A\":\"a\" // trailing\n}", options).A);
            }
        }
        """);

    [TestMethod]
    public async Task SingleQuotesAreNeverAccepted() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string A { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var options = new JsonSerializerOptions { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };

                try   { Console.WriteLine(JsonSerializer.Deserialize<T>("{'A':'a'}", options).A); }
                catch (JsonException) { Console.WriteLine("JsonException"); }
            }
        }
        """);

    [TestMethod]
    public async Task NumberHandlingAllowReadingFromString() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public class T { public int I { get; set; } public double D { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowReadingFromString };
                var t = JsonSerializer.Deserialize<T>("{\"I\":\"7\",\"D\":\"1.5\"}", options);
                Console.WriteLine(t.I + "/" + t.D);

                try   { Console.WriteLine(JsonSerializer.Deserialize<T>("{\"I\":\"7\"}").I); }
                catch (JsonException) { Console.WriteLine("JsonException"); }
            }
        }
        """);

    // ---------------------------------------------------------------------------------------------
    // Presets
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task WebDefaultsAreCamelCaseCaseInsensitiveAndLenientNumbers() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string FirstName { get; set; } public int Count { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

                Console.WriteLine(JsonSerializer.Serialize(new T { FirstName = "Ada", Count = 1 }, options));

                var t = JsonSerializer.Deserialize<T>("{\"FIRSTNAME\":\"Ada\",\"count\":\"7\"}", options);
                Console.WriteLine(t.FirstName + "/" + t.Count);
            }
        }
        """);

    [TestMethod]
    public async Task CopyingOptionsCarriesEverySetting() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public class T { public string FirstName { get; set; } public string Empty { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var source = new JsonSerializerOptions
                {
                    PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    WriteIndented          = false
                };

                var copy = new JsonSerializerOptions(source);
                Console.WriteLine(JsonSerializer.Serialize(new T { FirstName = "Ada" }, copy));
            }
        }
        """);

    [TestMethod]
    public async Task OptionsAreReusableAcrossCalls() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class A { public string X { get; set; } }
        public class B { public int Y { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

                Console.WriteLine(JsonSerializer.Serialize(new A { X = "x" }, options));
                Console.WriteLine(JsonSerializer.Serialize(new B { Y = 1 }, options));
                Console.WriteLine(JsonSerializer.Serialize(new A { X = "z" }, options));
            }
        }
        """);
}
