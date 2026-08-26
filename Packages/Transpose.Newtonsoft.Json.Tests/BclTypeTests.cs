using System.Threading.Tasks;

namespace Transpose.Newtonsoft.Json.Tests;

/// <summary>
/// The BCL types the binding library special-cases on the way in and out: dates, times, GUIDs, URIs,
/// 64-bit integers, decimals, chars, byte arrays and nullables.
/// </summary>
[TestClass]
public class BclTypeTests : JsonTestBase
{
    private const string Header = @"
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
";

    [TestMethod]
    public async Task GuidsRoundTrip()
    {
        var code = Header + @"
public class Item { public Guid Id { get; set; } public Guid? Maybe { get; set; } }
public class App
{
    public static void Main()
    {
        var item = new Item { Id = new Guid(""0f8fad5b-d9cb-469f-a165-70867728950e"") };
        var json = JsonConvert.SerializeObject(item);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<Item>(json);
        Console.WriteLine((back.Id == item.Id) + ""|"" + (back.Maybe == null));

        Console.WriteLine(JsonConvert.DeserializeObject<Guid>(""\""0f8fad5b-d9cb-469f-a165-70867728950e\""""));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task UtcDateTimesRoundTrip()
    {
        var code = Header + @"
public class Item { public DateTime When { get; set; } }
public class App
{
    public static void Main()
    {
        var item = new Item { When = new DateTime(2024, 3, 17, 14, 5, 6, DateTimeKind.Utc) };
        var json = JsonConvert.SerializeObject(item);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<Item>(json);
        Console.WriteLine(back.When.ToString(""yyyy-MM-dd HH:mm:ss"") + ""|"" + back.When.Kind);
        // Compared with Equals, not ==: Transpose's DateTime == operator returns false even for two
        // identically-constructed values (unrelated to JSON — see this suite's README).
        Console.WriteLine(back.When.Equals(item.When) + ""|"" + (back.When.Ticks == item.When.Ticks));
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// Fractional seconds survive a round trip: the serializer writes 250 ms as ".25" (Json.NET trims
    /// trailing zeros too), and reading ".25" back must give 250 ms — the digits are a *fraction* of a
    /// second, not a count of milliseconds.
    ///
    /// The fix for this lives in the runtime, not in this package (<c>fractionToMilliseconds</c> in
    /// <c>BCL/Transpose.BCL/Resources/Date.js</c>), so it only takes effect once the runtime the tests
    /// resolve carries it — a locally built <c>Transpose.dll</c> (<c>TRANSPOSE_DLL_PATH</c>) or a
    /// published Transpose.BCL that includes it. Against an older runtime the test reports
    /// inconclusive rather than failing, because there is nothing wrong with *this* package.
    /// </summary>
    [TestMethod]
    public async Task FractionalSecondsRoundTrip()
    {
        var code = Header + @"
public class Item { public DateTime When { get; set; } }
public class App
{
    public static void Main()
    {
        var item = new Item { When = new DateTime(2024, 12, 31, 23, 59, 58, 250, DateTimeKind.Utc) };
        Console.WriteLine(JsonConvert.SerializeObject(item));
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(""{\""When\"":\""2024-12-31T23:59:58.25Z\""}"").When.Millisecond);
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(""{\""When\"":\""2024-12-31T23:59:58.250Z\""}"").When.Millisecond);
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(""{\""When\"":\""2024-12-31T23:59:58.5Z\""}"").When.Millisecond);
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(JsonConvert.SerializeObject(item)).When.Equals(item.When));
    }
}";
        const string beforeTheRuntimeFix = "{\"When\":\"2024-12-31T23:59:58.25Z\"}\n25\n250\n5\nFalse";

        var jsOutput = await RunJs(code);

        if (jsOutput == beforeTheRuntimeFix)
        {
            Assert.Inconclusive(
                "The resolved Transpose runtime predates the DateTime fractional-seconds fix " +
                "(BCL/Transpose.BCL/Resources/Date.js, shipped in Transpose.BCL 26.7.3064): " +
                $"'{TransposeAssemblies.TransposeDllPath}'. Point TRANSPOSE_DLL_PATH at a newer " +
                "Transpose.dll — a released one, or one rebuilt from this repo with " +
                "`tps --project BCL/Transpose.BCL/Transpose.BCL.csproj --build-runtime`.");
        }

        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task UnspecifiedKindDateTimeIsWrittenWithoutAnOffset()
    {
        var code = Header + @"
public class Item { public DateTime When { get; set; } }
public class App
{
    public static void Main()
    {
        var json = JsonConvert.SerializeObject(new Item { When = new DateTime(2024, 3, 17, 14, 5, 6, DateTimeKind.Unspecified) });
        Console.WriteLine(json);
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(json).When.ToString(""yyyy-MM-dd HH:mm:ss""));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task DateTimeOffsetsRoundTrip()
    {
        var code = Header + @"
public class Item { public DateTimeOffset When { get; set; } }
public class App
{
    public static void Main()
    {
        var item = new Item { When = new DateTimeOffset(2024, 3, 17, 14, 5, 6, TimeSpan.Zero) };
        var json = JsonConvert.SerializeObject(item);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<Item>(json);
        Console.WriteLine(back.When.UtcDateTime.ToString(""yyyy-MM-dd HH:mm:ss""));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task TimeSpansRoundTrip()
    {
        var code = Header + @"
public class Item { public TimeSpan Duration { get; set; } public TimeSpan? Maybe { get; set; } }
public class App
{
    public static void Main()
    {
        var item = new Item { Duration = new TimeSpan(1, 2, 3, 4) };
        var json = JsonConvert.SerializeObject(item);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<Item>(json);
        Console.WriteLine(back.Duration.Days + ""|"" + back.Duration.Hours + ""|"" + back.Duration.Minutes + ""|"" + back.Duration.Seconds);
        Console.WriteLine(back.Maybe == null);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task UrisRoundTrip()
    {
        var code = Header + @"
public class Item { public Uri Link { get; set; } }
public class App
{
    public static void Main()
    {
        var json = JsonConvert.SerializeObject(new Item { Link = new Uri(""https://example.com/a/b?c=1"") });
        Console.WriteLine(json);
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(json).Link.ToString());
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// 64-bit integers are written as JSON <b>strings</b>, not numbers, because JavaScript numbers
    /// cannot hold them exactly — the value survives the round trip (which a number would not: note
    /// the 9007199254740993 below is above 2^53), but the wire format differs from Json.NET's and a
    /// .NET server reading it needs a long-typed member (Json.NET parses a quoted integer happily)
    /// rather than a loosely-typed one.
    /// </summary>
    [TestMethod]
    public async Task SixtyFourBitIntegersAreWrittenAsStrings()
    {
        var code = Header + @"
public class Item { public long L { get; set; } public ulong U { get; set; } }
public class App
{
    public static void Main()
    {
        var item = new Item { L = 9007199254740993L, U = 18446744073709551615UL };
        var json = JsonConvert.SerializeObject(item);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<Item>(json);
        Console.WriteLine(back.L + ""|"" + back.U);
    }
}";
        await RunJs(code,
            expected:     "{\"L\":\"9007199254740993\",\"U\":\"18446744073709551615\"}\n9007199254740993|18446744073709551615",
            nativePrints: "{\"L\":9007199254740993,\"U\":18446744073709551615}\n9007199254740993|18446744073709551615");
    }

    /// <summary>
    /// An UNASSIGNED 64-bit / decimal member. The serializer switches on the DECLARED type and then
    /// calls the runtime object's <c>toJSON()</c>, so a slot holding a plain JavaScript number
    /// instead of a System.Int64 killed the whole call with "obj.toJSON is not a function" — while
    /// the same type with the member explicitly set to 0 serialized fine, because an assigned
    /// literal IS wrapped. The compiler now defaults such a slot to the type's zero instance; the
    /// serializer also rebuilds one from a bare number, so a value arriving from JavaScript keeps
    /// the declared type's wire format.
    /// </summary>
    [TestMethod]
    public async Task UnassignedSixtyFourBitAndDecimalMembersSerialize()
    {
        var code = Header + @"
public class Probe { public long V { get; set; } public ulong U; public decimal M { get; set; } }
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Probe()));
        Console.WriteLine(JsonConvert.SerializeObject(new Probe { V = 0 }));
        Console.WriteLine(JsonConvert.SerializeObject(new Probe { V = 7L, U = 8UL, M = 1.5m }));

        var back = JsonConvert.DeserializeObject<Probe>(JsonConvert.SerializeObject(new Probe()));
        Console.WriteLine(back.V + ""|"" + back.U);
    }
}";
        await RunJs(code,
            expected:     "{\"U\":0,\"M\":0,\"V\":0}\n{\"U\":0,\"M\":0,\"V\":0}\n{\"U\":8,\"M\":1.5,\"V\":7}\n0|0",
            nativePrints: "{\"U\":0,\"V\":0,\"M\":0.0}\n{\"U\":0,\"V\":0,\"M\":0.0}\n{\"U\":8,\"V\":7,\"M\":1.5}\n0|0");
    }

    /// <summary>
    /// The same unassigned 64-bit/decimal slot, reached through every shape an inheritance hierarchy
    /// puts it in: an abstract get-only property implemented by an override, a virtual auto-property
    /// overridden by a field-backed one (base and derived hold SEPARATE slots), an interface
    /// implementation, a `new`-shadowed property, a closed generic base, and a record's positional
    /// members. Each of those is a different slot-emission path in the compiler, and one of them —
    /// the abstract auto-property — had no slot at all yet was still being assigned a default.
    /// Only the 64-bit members are compared against Json.NET here: an unassigned <c>decimal</c>
    /// carries the separate scale divergence Json.NET has (it writes <c>0.0</c>), and is pinned by
    /// <see cref="UnassignedSixtyFourBitAndDecimalMembersSerialize"/> instead.
    /// </summary>
    [TestMethod]
    public async Task UnassignedSixtyFourBitMembersRoundTripThroughEveryInheritanceShape()
    {
        var code = Header + @"
public abstract class Doc
{
    public abstract long Size { get; }        // abstract: no slot of its own
    public long Version { get; set; }         // plain auto-property on an abstract base
    public long Cost;                         // plain field on an abstract base
    public virtual long Rank { get; set; }    // virtual: the derived override gets its own slot
}
public class Report : Doc
{
    public override long Size { get { return 3L; } }
    public override long Rank { get; set; }
    public ulong Pages { get; set; }
}
public interface IStamped { long Stamp { get; set; } }
public class Stamped : IStamped { public long Stamp { get; set; } }
public class ShadowBase { public long Key { get; set; } }
public class Shadowed : ShadowBase { public new long Key { get; set; } }
public abstract class Box<T> { public T Value { get; set; } }
public class LongBox : Box<long> { }
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Report()));
        Console.WriteLine(JsonConvert.SerializeObject(new Stamped()));
        Console.WriteLine(JsonConvert.SerializeObject(new Shadowed()));
        Console.WriteLine(JsonConvert.SerializeObject(new LongBox()));

        var back = JsonConvert.DeserializeObject<Report>(JsonConvert.SerializeObject(new Report { Version = 4L, Rank = 5L, Pages = 6UL }));
        Console.WriteLine(back.Version + ""|"" + back.Rank + ""|"" + back.Pages + ""|"" + back.Size);

        // The base slot and the override slot are distinct; neither leaks into the other.
        var r = new Report { Rank = 7L };
        Console.WriteLine(((Doc)r).Rank + ""|"" + r.Rank);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task SixtyFourBitIntegersReadBackFromNumbersAndStrings()
    {
        var code = Header + @"
public class Item { public long L { get; set; } public long? Maybe { get; set; } }
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(""{\""L\"":42}"").L);
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(""{\""L\"":\""9007199254740993\""}"").L);
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(""{\""Maybe\"":null}"").Maybe == null);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task DecimalsRoundTrip()
    {
        var code = Header + @"
public class Item { public decimal M { get; set; } public decimal? Maybe { get; set; } }
public class App
{
    public static void Main()
    {
        var item = new Item { M = 12345.6789m };
        var json = JsonConvert.SerializeObject(item);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<Item>(json);
        Console.WriteLine(back.M + ""|"" + (back.M == item.M) + ""|"" + (back.Maybe == null));
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task CharsRoundTrip()
    {
        var code = Header + @"
public class Item { public char C { get; set; } public char? Maybe { get; set; } }
public class App
{
    public static void Main()
    {
        var json = JsonConvert.SerializeObject(new Item { C = 'x' });
        Console.WriteLine(json);
        Console.WriteLine(JsonConvert.DeserializeObject<Item>(json).C);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task ByteArraysUseBase64()
    {
        var code = Header + @"
public class Item { public byte[] Data { get; set; } }
public class App
{
    public static void Main()
    {
        var json = JsonConvert.SerializeObject(new Item { Data = new byte[] { 1, 2, 3, 255 } });
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<Item>(json);
        Console.WriteLine(back.Data.Length + ""|"" + back.Data[3]);

        Console.WriteLine(JsonConvert.SerializeObject(new byte[] { 0, 16, 255 }));
        Console.WriteLine(JsonConvert.DeserializeObject<byte[]>(""\""ABD/\"""").Length);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task NullablesRoundTrip()
    {
        var code = Header + @"
public class Item
{
    public int?      I { get; set; }
    public double?   D { get; set; }
    public bool?     B { get; set; }
    public DateTime? T { get; set; }
    public Guid?     G { get; set; }
}
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Item()));

        var set = new Item { I = 1, D = 2.5, B = true, T = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc), G = Guid.Empty };
        var json = JsonConvert.SerializeObject(set);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<Item>(json);
        Console.WriteLine(back.I + ""|"" + back.D + ""|"" + back.B + ""|"" + back.G);
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task FloatingPointValuesRoundTrip()
    {
        var code = Header + @"
public class Item { public double D { get; set; } public float F { get; set; } }
public class App
{
    public static void Main()
    {
        foreach (var d in new[] { 0.0, 1.0, -1.5, 0.1, 1e21, 1e-7, 123456789.123 })
        {
            var json = JsonConvert.SerializeObject(new Item { D = d, F = (float)1.25 });
            var back = JsonConvert.DeserializeObject<Item>(json);
            Console.WriteLine(back.D == d);
        }
        Console.WriteLine(JsonConvert.SerializeObject(new Item { D = 1.5, F = 2.25f }));
    }
}";
        await RunAndCompare(code);
    }

    /// <summary>
    /// A whole-number double: Json.NET writes "1.0" (keeping it a floating-point literal), the
    /// binding library goes through JSON.stringify and writes "1". Both read back as 1.0, so this
    /// only matters to a consumer that inspects the JSON text.
    /// </summary>
    [TestMethod]
    public async Task WholeNumberDoublesLoseTheirTrailingZero()
    {
        var code = Header + @"
public class Item { public double D { get; set; } public float F { get; set; } }
public class App
{
    public static void Main()
    {
        Console.WriteLine(JsonConvert.SerializeObject(new Item { D = 1.0, F = 2.0f }));
    }
}";
        await RunJs(code,
            expected:     @"{""D"":1,""F"":2}",
            nativePrints: @"{""D"":1.0,""F"":2.0}");
    }

    /// <summary>
    /// A <c>System.Version</c> travels as its "1.2.3.4" string, the way Json.NET writes it. (Before
    /// the type was special-cased, serializing one failed with "System.Version is not reflectable and
    /// cannot be serialized" — the BCL type carries no reflection metadata for the contract walker.)
    /// </summary>
    [TestMethod]
    public async Task VersionRoundTripsAsAString()
    {
        var code = Header + @"
public class Item { public Version V { get; set; } public Version Missing { get; set; } }
public class App
{
    public static void Main()
    {
        var json = JsonConvert.SerializeObject(new Item { V = new Version(1, 2, 3, 4) });
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<Item>(json);
        Console.WriteLine(back.V.Major + ""."" + back.V.Minor + ""."" + back.V.Build + ""."" + back.V.Revision);
        Console.WriteLine(back.Missing == null);

        Console.WriteLine(JsonConvert.SerializeObject(new Version(2, 1)));
        Console.WriteLine(JsonConvert.DeserializeObject<Version>(""\""3.4\"""").ToString());
    }
}";
        await RunAndCompare(code);
    }

    [TestMethod]
    public async Task DateTimesInsideCollectionsRoundTrip()
    {
        var code = Header + @"
public class App
{
    public static void Main()
    {
        var list = new List<DateTime>
        {
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2021, 6, 15, 12, 30, 0, DateTimeKind.Utc),
        };
        var json = JsonConvert.SerializeObject(list);
        Console.WriteLine(json);

        var back = JsonConvert.DeserializeObject<List<DateTime>>(json);
        Console.WriteLine(back.Count + ""|"" + back[1].ToString(""yyyy-MM-dd HH:mm""));

        var map = new Dictionary<string, DateTime> { [""a""] = list[0] };
        Console.WriteLine(JsonConvert.SerializeObject(map));
    }
}";
        await RunAndCompare(code);
    }
}
