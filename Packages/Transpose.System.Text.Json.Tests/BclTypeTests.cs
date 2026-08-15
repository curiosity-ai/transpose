namespace Transpose.SystemTextJson.Tests;

/// <summary>
/// The BCL values with a fixed JSON shape — dates, times, GUIDs, URIs, versions, byte arrays,
/// characters, nullables — plus the numeric types JavaScript cannot represent exactly.
/// </summary>
[TestClass]
public sealed class BclTypeTests : JsonTestBase
{
    [TestMethod]
    public async Task GuidUriAndVersion() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public Guid G { get; set; } public Uri U { get; set; } public Version V { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var value = new T
                {
                    G = new Guid("11111111-2222-3333-4444-555555555555"),
                    U = new Uri("https://example.org/path?q=1"),
                    V = new Version(1, 2, 3, 4)
                };

                var json = JsonSerializer.Serialize(value);
                Console.WriteLine(json);

                var back = JsonSerializer.Deserialize<T>(json);
                Console.WriteLine(back.G + "/" + back.U + "/" + back.V);
            }
        }
        """);

    [TestMethod]
    public async Task DateTimeIsIso8601() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public DateTime Utc { get; set; } public DateTime Unspecified { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var value = new T
                {
                    Utc         = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                    Unspecified = new DateTime(2021, 6, 7, 8, 9, 10, DateTimeKind.Unspecified)
                };

                var json = JsonSerializer.Serialize(value);
                Console.WriteLine(json);

                var back = JsonSerializer.Deserialize<T>(json);
                Console.WriteLine(back.Utc.ToString("yyyy-MM-dd HH:mm:ss") + "/" + back.Unspecified.ToString("yyyy-MM-dd HH:mm:ss"));
            }
        }
        """);

    [TestMethod]
    public async Task DateTimeWithFractionalSeconds() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public DateTime D { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var json = JsonSerializer.Serialize(new T { D = new DateTime(2020, 1, 2, 3, 4, 5, 250, DateTimeKind.Utc) });
                Console.WriteLine(json);
                Console.WriteLine(JsonSerializer.Deserialize<T>(json).D.Millisecond);
            }
        }
        """);

    [TestMethod]
    public async Task DateTimeOffsetRoundTrips() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public DateTimeOffset D { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var json = JsonSerializer.Serialize(new T { D = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero) });
                Console.WriteLine(json);
                Console.WriteLine(JsonSerializer.Deserialize<T>(json).D.ToString("yyyy-MM-dd HH:mm:ss"));
            }
        }
        """);

    [TestMethod]
    public async Task TimeSpanIsItsConstantString() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public TimeSpan Ts { get; set; } public TimeSpan? Nullable { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var json = JsonSerializer.Serialize(new T { Ts = TimeSpan.FromSeconds(90) });
                Console.WriteLine(json);
                Console.WriteLine(JsonSerializer.Deserialize<T>(json).Ts.TotalSeconds);
            }
        }
        """);

    [TestMethod]
    public async Task AByteArrayIsBase64() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public byte[] Data { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var json = JsonSerializer.Serialize(new T { Data = new byte[] { 1, 2, 3, 250 } });
                Console.WriteLine(json);

                var back = JsonSerializer.Deserialize<T>(json);
                Console.WriteLine(back.Data.Length + "/" + back.Data[3]);

                Console.WriteLine(JsonSerializer.Serialize(new T { Data = new byte[0] }));
            }
        }
        """);

    [TestMethod]
    public async Task ACharIsASingleCharacterString() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public char C { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var json = JsonSerializer.Serialize(new T { C = 'q' });
                Console.WriteLine(json);
                Console.WriteLine(JsonSerializer.Deserialize<T>(json).C);
            }
        }
        """);

    [TestMethod]
    public async Task NullablesRoundTrip() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public enum E { A = 1 }
        public class T
        {
            public int?      I  { get; set; }
            public bool?     B  { get; set; }
            public double?   D  { get; set; }
            public DateTime? Dt { get; set; }
            public E?        En { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(JsonSerializer.Serialize(new T()));

                var set  = new T { I = 1, B = true, D = 1.5, Dt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), En = E.A };
                var json = JsonSerializer.Serialize(set);
                Console.WriteLine(json);

                var back = JsonSerializer.Deserialize<T>(json);
                Console.WriteLine(back.I + "/" + back.B + "/" + back.D + "/" + back.En);

                var empty = JsonSerializer.Deserialize<T>("{}");
                Console.WriteLine(empty.I.HasValue + "/" + empty.Dt.HasValue);
            }
        }
        """);

    // ---------------------------------------------------------------------------------------------
    // Numerics JavaScript cannot hold exactly — the package's one deliberate wire difference
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task SixtyFourBitIntegersAreWrittenAsStringsWhileDecimalsStayNumbers() => await RunJs("""
        using System;
        using System.Text.Json;

        public class T { public long L { get; set; } public ulong U { get; set; } public decimal D { get; set; } }

        public static class Program
        {
            public static void Main()
                => Console.WriteLine(JsonSerializer.Serialize(new T { L = 9007199254740993L, U = 18446744073709551615UL, D = 1.5m }));
        }
        """,
        expected:     """{"D":1.5,"L":"9007199254740993","U":"18446744073709551615"}""",
        nativePrints: """{"L":9007199254740993,"U":18446744073709551615,"D":1.5}""");

    [TestMethod]
    public async Task SixtyFourBitIntegersRoundTripThroughTheirStringForm() => await RunJs("""
        using System;
        using System.Text.Json;

        public class T { public long L { get; set; } public decimal D { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var json = JsonSerializer.Serialize(new T { L = 9007199254740993L, D = 0.1m });
                var back = JsonSerializer.Deserialize<T>(json);

                Console.WriteLine(back.L);
                Console.WriteLine(back.D);
            }
        }
        """,
        expected:     "9007199254740993\n0.1",
        nativePrints: "9007199254740993\n0.1");

    [TestMethod]
    public async Task SixtyFourBitIntegersAreAlsoReadFromANumber() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public long L { get; set; } public decimal D { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                var back = JsonSerializer.Deserialize<T>("{\"L\":12,\"D\":1.5}");
                Console.WriteLine(back.L + "/" + back.D);
            }
        }
        """);

    [TestMethod]
    public async Task WholeNumberDoublesLoseTheirTrailingZero() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T { public double A { get; set; } public double B { get; set; } public float F { get; set; } }

        public static class Program
        {
            public static void Main()
                => Console.WriteLine(JsonSerializer.Serialize(new T { A = 1.0, B = 1.25, F = 0.5f }));
        }
        """);

    [TestMethod]
    public async Task IntegerBoundaries() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class T
        {
            public byte   B  { get; set; }
            public sbyte  Sb { get; set; }
            public short  S  { get; set; }
            public ushort Us { get; set; }
            public int    I  { get; set; }
            public uint   Ui { get; set; }
        }

        public static class Program
        {
            public static void Main()
            {
                var value = new T { B = 255, Sb = -128, S = -32768, Us = 65535, I = -2147483648, Ui = 4294967295 };
                var json  = JsonSerializer.Serialize(value);
                Console.WriteLine(json);

                var back = JsonSerializer.Deserialize<T>(json);
                Console.WriteLine(back.B + "/" + back.Sb + "/" + back.S + "/" + back.Us + "/" + back.I + "/" + back.Ui);
            }
        }
        """);
}
