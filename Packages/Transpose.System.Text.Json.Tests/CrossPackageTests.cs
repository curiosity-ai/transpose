namespace Transpose.SystemTextJson.Tests;

/// <summary>
/// <c>Transpose.Newtonsoft.Json</c> versus <c>Transpose.System.Text.Json</c>, both running in the
/// browser. This is the migration's risk register: an <c>AssertSame</c> is a shape that can be
/// swapped over without thinking about it, and an <c>AssertDiffers</c> is a shape whose payload or
/// behaviour changes, recorded on both sides so it cannot drift.
/// </summary>
[TestClass]
public sealed class CrossPackageTests : CrossPackageTestBase
{
    // =============================================================================================
    // Transparent — the payload is identical
    // =============================================================================================

    [TestMethod]
    public async Task PlainObjects() => await AssertSame("""
        using System;
        #USINGS#

        public class T { public string Name { get; set; } public int Count { get; set; } public bool Flag { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(Json.Write(new T { Name = "Ada", Count = 3, Flag = true }));
                Console.WriteLine(Json.Write(new T()));
            }
        }
        """);

    [TestMethod]
    public async Task NestedObjectsAndCollections() => await AssertSame("""
        using System;
        using System.Collections.Generic;
        #USINGS#

        public class Inner { public string S { get; set; } }
        public class T
        {
            public Inner                    One  { get; set; }
            public List<Inner>              Many { get; set; }
            public int[]                    Nums { get; set; }
            public Dictionary<string, int>  Map  { get; set; }
        }

        public static class Program
        {
            public static void Main()
                => Console.WriteLine(Json.Write(new T
                {
                    One  = new Inner { S = "a" },
                    Many = new List<Inner> { new Inner { S = "b" } },
                    Nums = new[] { 1, 2 },
                    Map  = new Dictionary<string, int> { ["k"] = 1 }
                }));
        }
        """);

    [TestMethod]
    public async Task EnumsAreNumbersInBoth() => await AssertSame("""
        using System;
        using System.Collections.Generic;
        #USINGS#

        public enum Colour { Red = 0, Green = 5, Blue = 9 }
        public class T { public Colour C { get; set; } public Colour? N { get; set; } public Colour? Missing { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(Json.Write(new T { C = Colour.Green, N = Colour.Blue }));
                Console.WriteLine(Json.Write(new Dictionary<Colour, string> { [Colour.Blue] = "x" }));

                // Both read a number, and both also accept the name the Curiosity server writes.
                Console.WriteLine(Json.Read<T>("{\"C\":9}").C);
                Console.WriteLine(Json.Read<T>("{\"C\":\"Green\"}").C);
            }
        }
        """);

    [TestMethod]
    public async Task TheRenameAttribute() => await AssertSame("""
        using System;
        #USINGS#

        public class T
        {
            [#PROP("n")]   public string Name  { get; set; }
            [#PROP("cnt")] public int    Count { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var json = Json.Write(new T { Name = "a", Count = 1 });
                Console.WriteLine(json);

                var back = Json.Read<T>(json);
                Console.WriteLine(back.Name + "/" + back.Count);
            }
        }
        """);

    [TestMethod]
    public async Task SixtyFourBitIntegersAndDecimalsAreStringsInBoth() => await AssertSame("""
        using System;
        #USINGS#

        public class T { public long L { get; set; } public ulong U { get; set; } public decimal D { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var json = Json.Write(new T { L = 9007199254740993L, U = 18446744073709551615UL, D = 1.5m });
                Console.WriteLine(json);

                var back = Json.Read<T>(json);
                Console.WriteLine(back.L + "/" + back.U + "/" + back.D);
            }
        }
        """);

    [TestMethod]
    public async Task DatesGuidsUrisAndByteArrays() => await AssertSame("""
        using System;
        #USINGS#

        public class T
        {
            public DateTime       Dt    { get; set; }
            public DateTimeOffset Dto   { get; set; }
            public TimeSpan       Ts    { get; set; }
            public Guid           G     { get; set; }
            public Uri            U     { get; set; }
            public Version        V     { get; set; }
            public byte[]         Bytes { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var value = new T
                {
                    Dt    = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                    Dto   = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero),
                    Ts    = TimeSpan.FromSeconds(90),
                    G     = new Guid("11111111-2222-3333-4444-555555555555"),
                    U     = new Uri("https://example.org/p"),
                    V     = new Version(1, 2, 3),
                    Bytes = new byte[] { 1, 2, 3 }
                };

                var json = Json.Write(value);
                Console.WriteLine(json);

                var back = Json.Read<T>(json);
                Console.WriteLine(back.Dt.ToString("yyyy-MM-dd HH:mm:ss") + "/" + back.Ts.TotalSeconds + "/" + back.G + "/" + back.V + "/" + back.Bytes.Length);
            }
        }
        """);

    [TestMethod]
    public async Task DeserializingToObjectYieldsTheRawParsedValueInBoth() => await AssertSame("""
        using System;
        #USINGS#

        public static class Program
        {
            public static void Main()
            {
                var value = Json.Read<object>("{\"a\":1,\"b\":[1,2]}");
                Console.WriteLine(Json.Write(value));
            }
        }
        """);

    [TestMethod]
    public async Task UnknownMembersAreIgnoredInBoth() => await AssertSame("""
        using System;
        #USINGS#

        public class T { public string Name { get; set; } }

        public static class Program
        {
            public static void Main()
                => Console.WriteLine(Json.Read<T>("{\"Nope\":1,\"Name\":\"n\",\"Deep\":{\"x\":[1]}}").Name);
        }
        """);

    [TestMethod]
    public async Task IndentedOutput() => await AssertSame("""
        using System;
        #USINGS#

        public class Inner { public string S { get; set; } }
        public class T { public Inner In { get; set; } public int[] Arr { get; set; } }

        public static class Program
        {
            public static void Main()
                => Console.WriteLine(Json.WriteIndented(new T { In = new Inner { S = "v" }, Arr = new[] { 1, 2 } }));
        }
        """);

    // =============================================================================================
    // Divergent — these are the migration's real work
    // =============================================================================================

    [TestMethod]
    public async Task MemberMatchingBecomesCaseSensitive() => await AssertDiffers("""
        using System;
        #USINGS#

        public class T { public string Name { get; set; } public int ItemCount { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var t = Json.Read<T>("{\"name\":\"n\",\"itemcount\":3}");
                Console.WriteLine((t.Name ?? "<null>") + "/" + t.ItemCount);
            }
        }
        """,
        newtonsoft:     "n/3",
        systemTextJson: "<null>/0");

    [TestMethod]
    public async Task PublicFieldsStopBeingSerialized() => await AssertDiffers("""
        using System;
        #USINGS#

        public class T
        {
            public string Field;
            public string Prop { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(Json.Write(new T { Field = "f", Prop = "p" }));
                Console.WriteLine(Json.Read<T>("{\"Field\":\"f\",\"Prop\":\"p\"}").Field ?? "<null>");
            }
        }
        """,
        newtonsoft:     "{\"Field\":\"f\",\"Prop\":\"p\"}\nf",
        systemTextJson: "{\"Prop\":\"p\"}\n<null>");

    [TestMethod]
    public async Task NonPublicSettersStopBeingPopulated() => await AssertDiffers("""
        using System;
        #USINGS#

        public class T { public string Value { get; private set; } = "original"; }

        public static class Program
        {
            public static void Main() => Console.WriteLine(Json.Read<T>("{\"Value\":\"changed\"}").Value);
        }
        """,
        newtonsoft:     "changed",
        systemTextJson: "original");

    [TestMethod]
    public async Task StringsAreEscapedMuchMoreAggressively() => await AssertDiffers("""
        using System;
        #USINGS#

        public class T { public string V { get; set; } }

        public static class Program
        {
            public static void Main() => Console.WriteLine(Json.Write(new T { V = "a+b <tag> & 'q' café" }));
        }
        """,
        newtonsoft:     "{\"V\":\"a+b <tag> & 'q' café\"}",
        systemTextJson: "{\"V\":\"a\\u002Bb \\u003Ctag\\u003E \\u0026 \\u0027q\\u0027 caf\\u00E9\"}");

    [TestMethod]
    public async Task ANullIntoANonNullableValueMemberNowThrows() => await AssertDiffers("""
        using System;
        #USINGS#

        public class T { public int Count { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                try                    { Console.WriteLine(Json.Read<T>("{\"Count\":null}").Count); }
                catch (#JSONEX#)       { Console.WriteLine("JsonException"); }
            }
        }
        """,
        newtonsoft:     "0",
        systemTextJson: "JsonException");

    [TestMethod]
    public async Task AShapeMismatchNowThrows() => await AssertDiffers("""
        using System;
        using System.Collections.Generic;
        #USINGS#

        public class T { public string Name { get; set; } }

        public static class Program
        {
            static void Try(string label, Func<string> f)
            {
                try              { Console.WriteLine(label + ": " + f()); }
                catch (#JSONEX#) { Console.WriteLine(label + ": JsonException"); }
            }

            public static void Main()
            {
                Try("array into object", () => Json.Read<T>("[1,2]").Name ?? "<null>");
                Try("object into list",  () => Json.Read<List<int>>("{\"a\":1}").Count.ToString());
            }
        }
        """,
        newtonsoft:     "array into object: <null>\nobject into list: 0",
        systemTextJson: "array into object: JsonException\nobject into list: JsonException");

    [TestMethod]
    public async Task ALenientPayloadIsNoLongerAccepted() => await AssertDiffers("""
        using System;
        #USINGS#

        public class T { public string Name { get; set; } }

        public static class Program
        {
            static void Try(string label, Func<string> f)
            {
                try              { Console.WriteLine(label + ": " + f()); }
                catch (#JSONEX#) { Console.WriteLine(label + ": JsonException"); }
            }

            public static void Main()
            {
                Try("single quotes",  () => Json.Read<T>("{'Name':'n'}").Name);
                Try("unquoted name",  () => Json.Read<T>("{Name:\"n\"}").Name);
                Try("trailing comma", () => Json.Read<T>("{\"Name\":\"n\",}").Name);
                Try("comment",        () => Json.Read<T>("{/*c*/\"Name\":\"n\"}").Name);
            }
        }
        """,
        newtonsoft:     "single quotes: n\nunquoted name: n\ntrailing comma: n\ncomment: n",
        systemTextJson: "single quotes: JsonException\nunquoted name: JsonException\ntrailing comma: JsonException\ncomment: JsonException");

    [TestMethod]
    public async Task AnEmptyDocumentNowThrowsInsteadOfReturningDefault() => await AssertDiffers("""
        using System;
        #USINGS#

        public class T { public string Name { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                try              { Console.WriteLine(Json.Read<T>("") is null ? "<null>" : "instance"); }
                catch (#JSONEX#) { Console.WriteLine("JsonException"); }
            }
        }
        """,
        newtonsoft:     "<null>",
        systemTextJson: "JsonException");
}
