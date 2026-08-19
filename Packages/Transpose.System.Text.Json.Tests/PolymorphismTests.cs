namespace Transpose.SystemTextJson.Tests;

/// <summary>
/// <c>[JsonPolymorphic]</c> / <c>[JsonDerivedType]</c>: the discriminator is written first, matched on
/// read, and the hierarchy is declared on the base type rather than inferred from a CLR type name the
/// way Json.NET's <c>TypeNameHandling</c> did.
/// </summary>
[TestClass]
public sealed class PolymorphismTests : JsonTestBase
{
    private const string Hierarchy = """
        using System;
        using System.Collections.Generic;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        [JsonDerivedType(typeof(Dog), "dog")]
        [JsonDerivedType(typeof(Cat), "cat")]
        public abstract class Animal { public string Name { get; set; } }

        public class Dog : Animal { public bool Fetches { get; set; } }
        public class Cat : Animal { public int Lives { get; set; } }

        public class Shelter { public Animal Resident { get; set; } public List<Animal> All { get; set; } }
        """;

    [TestMethod]
    public async Task TheDiscriminatorIsWrittenFirst() => await RunAndCompare(Hierarchy + """

        public static class Program
        {
            public static void Main()
                => Console.WriteLine(JsonSerializer.Serialize<Animal>(new Dog { Name = "Rex", Fetches = true }));
        }
        """, exactMemberOrder: true);

    [TestMethod]
    public async Task ADerivedValueIsReadBackAsItsOwnType() => await RunAndCompare(Hierarchy + """

        public static class Program
        {
            public static void Main()
            {
                var json = JsonSerializer.Serialize<Animal>(new Cat { Name = "Tom", Lives = 9 });
                Console.WriteLine(json);

                var back = JsonSerializer.Deserialize<Animal>(json);
                Console.WriteLine(back.GetType().Name + "/" + back.Name + "/" + ((Cat)back).Lives);
            }
        }
        """);

    [TestMethod]
    public async Task APolymorphicMemberRoundTrips() => await RunAndCompare(Hierarchy + """

        public static class Program
        {
            public static void Main()
            {
                var shelter = new Shelter
                {
                    Resident = new Dog { Name = "Rex", Fetches = true },
                    All      = new List<Animal> { new Dog { Name = "Rex" }, new Cat { Name = "Tom", Lives = 7 } }
                };

                var json = JsonSerializer.Serialize(shelter);
                Console.WriteLine(json);

                var back = JsonSerializer.Deserialize<Shelter>(json);
                Console.WriteLine(back.Resident.GetType().Name + "/" + back.All.Count + "/" + back.All[1].GetType().Name);
            }
        }
        """);

    [TestMethod]
    public async Task AnUnrecognisedDiscriminatorFails() => await RunAndCompare(Hierarchy + """

        public static class Program
        {
            public static void Main()
            {
                try
                {
                    var back = JsonSerializer.Deserialize<Animal>("{\"$type\":\"fox\",\"Name\":\"n\"}");
                    Console.WriteLine(back.GetType().Name);
                }
                catch (JsonException)
                {
                    Console.WriteLine("JsonException");
                }
            }
        }
        """);

    [TestMethod]
    public async Task ACustomDiscriminatorPropertyName() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
        [JsonDerivedType(typeof(Square), "square")]
        public abstract class Shape { public string Label { get; set; } }
        public class Square : Shape { public int Side { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var json = JsonSerializer.Serialize<Shape>(new Square { Label = "s", Side = 2 });
                Console.WriteLine(json);
                Console.WriteLine(JsonSerializer.Deserialize<Shape>(json).GetType().Name);
            }
        }
        """, exactMemberOrder: true);

    [TestMethod]
    public async Task AnInterfaceRootedHierarchy() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        [JsonDerivedType(typeof(TextNote), "text")]
        [JsonDerivedType(typeof(LinkNote), "link")]
        public interface INote { }

        public class TextNote : INote { public string Body { get; set; } }
        public class LinkNote : INote { public string Url  { get; set; } }

        public class Holder { public INote Note { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var json = JsonSerializer.Serialize(new Holder { Note = new LinkNote { Url = "https://x/" } });
                Console.WriteLine(json);

                var back = JsonSerializer.Deserialize<Holder>(json);
                Console.WriteLine(back.Note.GetType().Name + "/" + ((LinkNote)back.Note).Url);
            }
        }
        """);

    [TestMethod]
    public async Task ADiscriminatorCanBeAFullTypeName() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        // Json.NET's TypeNameHandling wrote the CLR type name into $type. Declaring the same string as
        // the discriminator is what lets a payload written that way keep deserializing.
        [JsonDerivedType(typeof(FieldContent), "Mosaik.Components.NodeRendering.FieldContent")]
        public interface INodeRendererItem { }

        public class FieldContent : INodeRendererItem { public string Field { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var json = JsonSerializer.Serialize<INodeRendererItem>(new FieldContent { Field = "Title" });
                Console.WriteLine(json);
                Console.WriteLine(JsonSerializer.Deserialize<INodeRendererItem>(json).GetType().Name);
            }
        }
        """, exactMemberOrder: true);

    [TestMethod]
    public async Task AnAssemblyQualifiedDiscriminatorStillMatchesTheBareName() => await RunJs("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        [JsonDerivedType(typeof(FieldContent), "Mosaik.Components.NodeRendering.FieldContent")]
        public interface INodeRendererItem { }

        public class FieldContent : INodeRendererItem { public string Field { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                // Exactly what a store written by Json.NET with TypeNameHandling.Objects contains.
                var legacy = "{\"$type\":\"Mosaik.Components.NodeRendering.FieldContent, Mosaik.Graph\",\"Field\":\"Title\"}";

                try
                {
                    var back = JsonSerializer.Deserialize<INodeRendererItem>(legacy);
                    Console.WriteLine(back.GetType().Name + "/" + ((FieldContent)back).Field);
                }
                catch (JsonException)
                {
                    Console.WriteLine("JsonException");
                }
            }
        }
        """,
        expected:     "FieldContent/Title",
        nativePrints: "JsonException");

    [TestMethod]
    public async Task ANonPolymorphicTypeIsUnaffected() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class Plain { public string A { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var json = JsonSerializer.Serialize(new Plain { A = "a" });
                Console.WriteLine(json);
                Console.WriteLine(JsonSerializer.Deserialize<Plain>("{\"$type\":\"ignored\",\"A\":\"a\"}").A);
            }
        }
        """);

    // ---------------------------------------------------------------------------------------------
    // Registering a hierarchy at run time
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task AHierarchyCanBeDeclaredAtRunTime() => await RunJs("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        // No [JsonDerivedType] anywhere: this is the shape a layered application is forced into when
        // the base type sits in a project *below* the one holding its implementations, so it cannot
        // name them at compile time.
        public interface INote { }

        public class TextNote : INote { public string Body { get; set; } }
        public class LinkNote : INote { public string Url  { get; set; } }

        public class Holder { public INote Note { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                JsonPolymorphicTypes.Register<INote>(typeof(TextNote), "text");
                JsonPolymorphicTypes.Register<INote>(typeof(LinkNote), "link");

                var json = JsonSerializer.Serialize(new Holder { Note = new LinkNote { Url = "https://x/" } });
                Console.WriteLine(json);

                var back = JsonSerializer.Deserialize<Holder>(json);
                Console.WriteLine(back.Note.GetType().Name + "/" + ((LinkNote)back.Note).Url);
            }
        }
        """,
        expected: "{\"Note\":{\"$type\":\"link\",\"Url\":\"https://x/\"}}\nLinkNote/https://x/");

    [TestMethod]
    public async Task ARunTimeHierarchyAcceptsAnAssemblyQualifiedDiscriminatorToo() => await RunJs("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public interface INodeRendererItem { }
        public class FieldContent : INodeRendererItem { public string Field { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                JsonPolymorphicTypes.Register<INodeRendererItem>(typeof(FieldContent), "Mosaik.Components.NodeRendering.FieldContent");

                var legacy = "{\"$type\":\"Mosaik.Components.NodeRendering.FieldContent, Mosaik.Graph\",\"Field\":\"Title\"}";
                var back   = JsonSerializer.Deserialize<INodeRendererItem>(legacy);

                Console.WriteLine(back.GetType().Name + "/" + ((FieldContent)back).Field);
            }
        }
        """,
        expected: "FieldContent/Title");

    // A base shared with a server carries [JsonDerivedType] for the types the server can see, and the
    // front-end registers the ones only it can — the mixed case this escape hatch exists for. A
    // registration used to REPLACE the attribute-declared set rather than add to it, so the very
    // first Register call made every attribute-declared type unserializable ("Runtime type 'Dog' is
    // not supported by polymorphic type 'Animal'").
    [TestMethod]
    public async Task ARunTimeRegistrationAddsToTheAttributeDeclaredTypes() => await RunAndCompare("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        [JsonPolymorphic]
        [JsonDerivedType(typeof(Dog), "dog")]
        [JsonDerivedType(typeof(Cat), "cat")]
        public abstract class Animal { public string Name { get; set; } }
        public sealed class Dog : Animal { }
        public sealed class Cat : Animal { }

        public static class Program
        {
            public static void Main()
            {
                Register();

                Animal d = new Dog { Name = "Rex" };
                Animal c = new Cat { Name = "Tom" };
                Console.WriteLine(JsonSerializer.Serialize(d));
                Console.WriteLine(JsonSerializer.Serialize(c));
                Console.WriteLine(JsonSerializer.Deserialize<Animal>("{\"$type\":\"dog\",\"Name\":\"Rex\"}").GetType().Name);
                Console.WriteLine(JsonSerializer.Deserialize<Animal>("{\"$type\":\"cat\",\"Name\":\"Tom\"}").GetType().Name);
            }

            // Native System.Text.Json has no run-time registration; the attributes above already
            // declare both types, so the oracle simply does nothing here.
        #if TRANSPOSE
            static void Register() => JsonPolymorphicTypes.Register<Animal>(typeof(Cat), "cat");
        #else
            static void Register() { }
        #endif
        }
        """);

    // A [JsonPolymorphic(TypeDiscriminatorPropertyName = ...)] on the base must survive a
    // registration that does not name a discriminator member of its own.
    [TestMethod]
    public async Task ARegistrationKeepsTheAttributeDiscriminatorMemberName() => await RunJs("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
        [JsonDerivedType(typeof(Square), "square")]
        public abstract class Shape { public int Size { get; set; } }
        public sealed class Square : Shape { }
        public sealed class Circle : Shape { }

        public static class Program
        {
            public static void Main()
            {
                JsonPolymorphicTypes.Register<Shape>(typeof(Circle), "circle");

                Shape s = new Square { Size = 1 };
                Shape c = new Circle { Size = 2 };
                Console.WriteLine(JsonSerializer.Serialize(s));
                Console.WriteLine(JsonSerializer.Serialize(c));
                Console.WriteLine(JsonSerializer.Deserialize<Shape>("{\"kind\":\"square\",\"Size\":1}").GetType().Name);
                Console.WriteLine(JsonSerializer.Deserialize<Shape>("{\"kind\":\"circle\",\"Size\":2}").GetType().Name);
            }
        }
        """, """
        {"kind":"square","Size":1}
        {"kind":"circle","Size":2}
        Square
        Circle
        """);

    [TestMethod]
    public async Task ARunTimeRegistrationCanNameItsDiscriminatorMember() => await RunJs("""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        public interface IShape { }
        public class Square : IShape { public int Side { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                JsonPolymorphicTypes.Register<IShape>(typeof(Square), "square", "kind");

                var json = JsonSerializer.Serialize<IShape>(new Square { Side = 2 });
                Console.WriteLine(json);
                Console.WriteLine(JsonSerializer.Deserialize<IShape>(json).GetType().Name);
            }
        }
        """,
        expected: "{\"kind\":\"square\",\"Side\":2}\nSquare");
}
