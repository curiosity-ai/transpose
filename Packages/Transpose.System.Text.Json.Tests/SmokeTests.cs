namespace Transpose.SystemTextJson.Tests;

/// <summary>
/// The narrowest end-to-end checks: if these fail, the harness itself (package build, runtime glue,
/// Node) is broken rather than any particular behaviour.
/// </summary>
[TestClass]
public sealed class SmokeTests : JsonTestBase
{
    [TestMethod]
    public async Task SerializeASimpleObject() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class Person
        {
            public string Name { get; set; }
            public int Age { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(JsonSerializer.Serialize(new Person { Name = "Ada", Age = 36 }));
            }
        }
        """);

    [TestMethod]
    public async Task DeserializeASimpleObject() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class Person
        {
            public string Name { get; set; }
            public int Age { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var p = JsonSerializer.Deserialize<Person>("{\"Name\":\"Ada\",\"Age\":36}");
                Console.WriteLine(p.Name + " / " + p.Age);
            }
        }
        """);

    [TestMethod]
    public async Task RoundTrip() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class Person
        {
            public string Name { get; set; }
            public int Age { get; set; }
            public bool Active { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var json = JsonSerializer.Serialize(new Person { Name = "Ada", Age = 36, Active = true });
                var back = JsonSerializer.Deserialize<Person>(json);
                Console.WriteLine(JsonSerializer.Serialize(back));
            }
        }
        """);
}
