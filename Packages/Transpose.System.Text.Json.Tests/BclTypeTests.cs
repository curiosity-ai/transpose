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

    /// <summary>
    /// An UNASSIGNED 64-bit / decimal member. long, ulong and decimal are runtime OBJECTS here and
    /// the shape above is chosen by the DECLARED type, so a slot left holding a plain JavaScript
    /// number bypassed that shape entirely — this package fell through to writing the bare number,
    /// while Transpose.Newtonsoft.Json, which calls the object's toJSON(), died on
    /// "obj.toJSON is not a function" (see its UnassignedSixtyFourBitAndDecimalMembersSerialize).
    /// The compiler now defaults such a slot to the type's zero instance; this package also rebuilds
    /// the declared type from a bare number, so a value arriving from JavaScript takes the declared
    /// wire format rather than whichever one the value's runtime shape happens to select.
    /// </summary>
    [TestMethod]
    public async Task UnassignedSixtyFourBitAndDecimalMembersTakeTheDeclaredWireFormat() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public class Probe { public long V { get; set; } public ulong U { get; set; } public decimal M { get; set; } }

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(JsonSerializer.Serialize(new Probe()));
                Console.WriteLine(JsonSerializer.Serialize(new Probe { V = 0 }));

                var back = JsonSerializer.Deserialize<Probe>(JsonSerializer.Serialize(new Probe()));
                Console.WriteLine(back.V + "/" + back.U);
            }
        }
        """);

    /// <summary>
    /// The same unassigned 64-bit slot, reached through every shape an inheritance hierarchy puts it
    /// in: an abstract get-only property implemented by an override, a virtual auto-property
    /// overridden by a field-backed one (base and derived hold SEPARATE slots), an interface
    /// implementation, a `new`-shadowed property and a closed generic base. Each is a different
    /// slot-emission path in the compiler, and one of them — the abstract auto-property — had no slot
    /// at all yet was still being assigned a default, which is what broke System.IO.Stream.Length.
    /// </summary>
    [TestMethod]
    public async Task UnassignedSixtyFourBitMembersRoundTripThroughEveryInheritanceShape() => await RunAndCompare("""
        using System;
        using System.Text.Json;

        public abstract class Doc
        {
            public abstract long Size { get; }        // abstract: no slot of its own
            public long Version { get; set; }         // plain auto-property on an abstract base
            public virtual long Rank { get; set; }    // virtual: the derived override gets its own slot
        }

        public class Report : Doc
        {
            public override long Size => 3L;
            public override long Rank { get; set; }
            public ulong Pages { get; set; }
        }

        public interface IStamped { long Stamp { get; set; } }
        public class Stamped : IStamped { public long Stamp { get; set; } }

        public class ShadowBase { public long Key { get; set; } }
        public class Shadowed : ShadowBase { public new long Key { get; set; } }

        public abstract class Box<T> { public T Value { get; set; } }
        public class LongBox : Box<long> { }

        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine(JsonSerializer.Serialize(new Report()));
                Console.WriteLine(JsonSerializer.Serialize(new Stamped()));
                Console.WriteLine(JsonSerializer.Serialize(new Shadowed()));
                Console.WriteLine(JsonSerializer.Serialize(new LongBox()));

                var json = JsonSerializer.Serialize(new Report { Version = 4L, Rank = 5L, Pages = 6UL });
                var back = JsonSerializer.Deserialize<Report>(json);
                Console.WriteLine(back.Version + "|" + back.Rank + "|" + back.Pages + "|" + back.Size);

                // The base slot and the override slot are distinct; neither leaks into the other.
                var r = new Report { Rank = 7L };
                Console.WriteLine(((Doc)r).Rank + "|" + r.Rank);
            }
        }
        """);

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
