namespace Transpose.SystemTextJson.Tests;

/// <summary>
/// What ends up in the payload and in what shape: which members are picked, how they are ordered,
/// nesting, indentation, enums, and the depth guard.
/// </summary>
[TestClass]
public sealed class SerializationTests : JsonTestBase
{
    // ---------------------------------------------------------------------------------------------
    // Member selection
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task PublicPropertiesAreWritten() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string A { get; set; } public int B { get; set; } public bool C { get; set; } }

        public static class Program
        {
            public static void Main() => Console.WriteLine(JsonSerializer.Serialize(new T { A = "a", B = 2, C = true }));
        }
        """);

    [TestMethod]
    public async Task PublicFieldsAreSkippedUnlessIncludeFieldsIsSet() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string Field; public string Prop { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var value = new T { Field = "f", Prop = "p" };
                Console.WriteLine(JsonSerializer.Serialize(value));
                Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { IncludeFields = true }));
            }
        }
        """);

    [TestMethod]
    public async Task PrivateAndInternalMembersAreSkipped() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T
        {
            public   string Public   { get; set; } = "yes";
            internal string Internal { get; set; } = "no";
            private  string Private  { get; set; } = "no";
            protected string Protected { get; set; } = "no";
        }

        public static class Program
        {
            public static void Main() => Console.WriteLine(JsonSerializer.Serialize(new T()));
        }
        """);

    [TestMethod]
    public async Task AGetOnlyPropertyIsWritten() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T
        {
            public string Stored { get; set; } = "s";
            public string Computed => Stored + "!";
        }

        public static class Program
        {
            public static void Main() => Console.WriteLine(JsonSerializer.Serialize(new T()));
        }
        """);

    [TestMethod]
    public async Task APropertyWithANonPublicSetterIsStillWritten() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string Value { get; private set; } = "v"; }

        public static class Program
        {
            public static void Main() => Console.WriteLine(JsonSerializer.Serialize(new T()));
        }
        """);

    [TestMethod]
    public async Task StaticMembersAreSkipped() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T
        {
            public static string Shared { get; set; } = "no";
            public        string Own    { get; set; } = "yes";
        }

        public static class Program
        {
            public static void Main() => Console.WriteLine(JsonSerializer.Serialize(new T()));
        }
        """);

    [TestMethod]
    public async Task AnIndexerIsSkipped() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T
        {
            public string Name { get; set; } = "n";
            public string this[int i] => i.ToString();
        }

        public static class Program
        {
            public static void Main() => Console.WriteLine(JsonSerializer.Serialize(new T()));
        }
        """);

    // ---------------------------------------------------------------------------------------------
    // Order — the one systematic divergence, pinned once here so every other test can canonicalize
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task MemberOrderIsAlphabetical() => await RunJs("""
        using System;
        using System.Text.Json;

        public class T
        {
            public int Zebra  { get; set; }
            public int Apple  { get; set; }
            public int Middle { get; set; }
        }

        public static class Program
        {
            public static void Main() => Console.WriteLine(JsonSerializer.Serialize(new T { Zebra = 1, Apple = 2, Middle = 3 }));
        }
        """,
        expected:     """{"Apple":2,"Middle":3,"Zebra":1}""",
        nativePrints: """{"Zebra":1,"Apple":2,"Middle":3}""");

    [TestMethod]
    public async Task NullsAreWrittenByDefault() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string A { get; set; } public string B { get; set; } }

        public static class Program
        {
            public static void Main() => Console.WriteLine(JsonSerializer.Serialize(new T { A = null, B = "b" }));
        }
        """);

    [TestMethod]
    public async Task ANullRootIsWrittenAsNull() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string A { get; set; } }

        public static class Program
        {
            public static void Main() => Console.WriteLine(JsonSerializer.Serialize<T>(null));
        }
        """);

    // ---------------------------------------------------------------------------------------------
    // Nesting and formatting
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task NestedObjectsAndArrays() => await RunAndCompare("""
        using System;
        using System.Collections.Generic;
        using System.Text.Json;

        public class Inner { public string S { get; set; } }
        public class Outer
        {
            public Inner        One  { get; set; }
            public List<Inner>  Many { get; set; }
            public int[]        Nums { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var value = new Outer
                {
                    One  = new Inner { S = "a" },
                    Many = new List<Inner> { new Inner { S = "b" }, new Inner { S = "c" } },
                    Nums = new[] { 1, 2, 3 }
                };

                Console.WriteLine(JsonSerializer.Serialize(value));
            }
        }
        """);

    [TestMethod]
    public async Task WriteIndentedUsesTwoSpacesAndAColonSpace() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class Inner { public string S { get; set; } }
        public class Outer { public Inner In { get; set; } public int[] Arr { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var value = new Outer { In = new Inner { S = "v" }, Arr = new[] { 1, 2 } };
                Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        """);

    [TestMethod]
    public async Task AnEmptyArrayAndAnEmptyObjectStayOnOneLineWhenIndented() => await RunAndCompare("""
        using System;
        using System.Collections.Generic;
        using System.Text.Json;

        public class Empty { }
        public class T { public int[] Arr { get; set; } public List<int> List { get; set; } public Empty Obj { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var value = new T { Arr = new int[0], List = new List<int>(), Obj = new Empty() };
                Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        """);

    // ---------------------------------------------------------------------------------------------
    // Enums
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task EnumsAreWrittenAsNumbers() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public enum Colour { Red = 0, Green = 5, Blue = 9 }
        public class T { public Colour C { get; set; } public Colour? N { get; set; } public Colour? Missing { get; set; } }

        public static class Program
        {
            public static void Main() => Console.WriteLine(JsonSerializer.Serialize(new T { C = Colour.Green, N = Colour.Blue }));
        }
        """);

    [TestMethod]
    public async Task AnUndeclaredEnumValueIsWrittenAsItsNumber() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public enum Colour { Red = 0, Green = 5 }
        public class T { public Colour C { get; set; } }

        public static class Program
        {
            public static void Main() => Console.WriteLine(JsonSerializer.Serialize(new T { C = (Colour)42 }));
        }
        """);

    // ---------------------------------------------------------------------------------------------
    // Polymorphic-shaped writes without a declared hierarchy
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task ADerivedValueBoxedAsObjectIsWrittenWithItsOwnMembers() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public string A { get; set; } public int B { get; set; } }

        public static class Program
        {
            public static void Main() => Console.WriteLine(JsonSerializer.Serialize<object>(new T { A = "a", B = 1 }));
        }
        """);

    [TestMethod]
    public async Task InheritedMembersAreWritten() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class Base    { public int FromBase { get; set; } }
        public class Derived : Base { public int FromDerived { get; set; } }

        public static class Program
        {
            public static void Main() => Console.WriteLine(JsonSerializer.Serialize(new Derived { FromBase = 1, FromDerived = 2 }));
        }
        """);

    [TestMethod]
    public async Task AnAnonymousTypeIsWritten() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public static class Program
        {
            public static void Main() => Console.WriteLine(JsonSerializer.Serialize(new { X = 1, Y = "s", Z = true }));
        }
        """);

    // ---------------------------------------------------------------------------------------------
    // Depth / cycles
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task ASelfReferencingObjectFailsRatherThanLooping() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class Node { public string Name { get; set; } public Node Next { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var node = new Node { Name = "a" };
                node.Next = node;

                try
                {
                    Console.WriteLine(JsonSerializer.Serialize(node));
                }
                catch (JsonException)
                {
                    Console.WriteLine("JsonException");
                }
            }
        }
        """);
}
