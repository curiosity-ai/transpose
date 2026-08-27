using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// <c>long</c>/<c>ulong</c> that live in FOREIGN JavaScript — an <c>[External]</c> binding's
    /// member, an <c>[ObjectLiteral]</c>'s field.
    ///
    /// <para>
    /// tps.js models a 64-bit integer as a System.Int64/UInt64 OBJECT, and that is right for every
    /// value Transpose itself produces and for the base library, which defines those two types. It
    /// is wrong for a slot backed by real JavaScript: a binding declares <c>Blob.size</c> as
    /// <c>ulong</c> because that is the nearest C# type for the spec's <i>unsigned long long</i>,
    /// but the browser hands back a plain <c>number</c>. Reading it as a box made
    /// <c>file.Size &gt; 0</c> emit <c>file.size.gt(…)</c> — "gt is not a function" — and writing one
    /// passed an Int64 object into <c>blob.slice(…)</c>, which coerces to NaN.
    /// </para>
    ///
    /// <para>
    /// So such a value stays a plain number, and the representation changes at the boundary: lifted
    /// with <c>System.Int64(…)</c> when it enters a managed <c>long</c> slot, read back with
    /// <c>.toNumber()</c> when a managed value is written into a foreign one. The behaviour tests
    /// below run the whole surface against native .NET; the emit tests pin which side of the
    /// boundary each construct landed on. See <c>Emitter.Foreign64.cs</c>.
    /// </para>
    /// </summary>
    [TestClass]
    public class ForeignJs64BitTests : TranslatorTestBase
    {
        // ---- [ObjectLiteral] ---------------------------------------------------

        /// <summary>
        /// An <c>[ObjectLiteral]</c> instance IS a plain JS object — that is the point of the
        /// attribute, since it crosses into JSON and into hand-written JavaScript — so a
        /// <c>long</c>/<c>ulong</c> member of one holds a plain number. The whole operator surface
        /// runs against the same program with the attribute removed, which is what .NET does.
        /// </summary>
        [TestMethod]
        public async Task ObjectLiteral64BitMembersBehaveLikeNativeDotNet()
        {
            const string body = """
    public static void Main()
    {
        var i = new Info { Id = 7L, Bytes = 3000000000UL, Maybe = 12L };

        // Reads and the operators that used to call Int64 methods on a bare number.
        Console.WriteLine(i.Id);
        Console.WriteLine(i.Bytes);
        Console.WriteLine(i.Id + 1);
        Console.WriteLine(i.Id - 10);
        Console.WriteLine(i.Id * 3);
        Console.WriteLine(i.Bytes / 1024);
        Console.WriteLine(i.Id % 4);
        Console.WriteLine(i.Id > 3);
        Console.WriteLine(i.Id >= 7);
        Console.WriteLine(i.Id < 3);
        Console.WriteLine(i.Id == 7);
        Console.WriteLine(i.Id != 7);
        Console.WriteLine(i.Bytes >= 3000000000UL);

        // Conversions out of the foreign representation.
        Console.WriteLine(i.Id.ToString());
        Console.WriteLine((double)i.Bytes);
        Console.WriteLine((int)i.Id);
        Console.WriteLine((short)i.Id);
        Console.WriteLine(-i.Id);
        Console.WriteLine($"interp {i.Id} {i.Bytes}");

        // Into a managed slot, where the Int64 methods are real again.
        long managed = i.Id;
        Console.WriteLine(managed + 1L);
        Console.WriteLine(managed.ToString());
        Console.WriteLine(managed * managed);

        // Boxing: the box has to carry a real Int64, or `is long` and ToString are wrong.
        object boxed = i.Id;
        Console.WriteLine(boxed is long);
        Console.WriteLine(boxed.ToString());

        // Writes back into the literal: a managed Int64 must not be stored as an object.
        i.Id = managed * 2L;
        Console.WriteLine(i.Id);
        i.Id += 5;
        Console.WriteLine(i.Id);
        i.Id -= 2;
        Console.WriteLine(i.Id);
        i.Id *= 3;
        Console.WriteLine(i.Id);
        i.Id++;
        Console.WriteLine(i.Id);
        --i.Id;
        Console.WriteLine(i.Id);
        i.Bytes /= 3;
        Console.WriteLine(i.Bytes);

        // Nullable members.
        Console.WriteLine(i.Maybe + 1);
        Console.WriteLine(i.Maybe.HasValue);
        Console.WriteLine(i.Maybe.Value + 2);
        Console.WriteLine((i.Maybe ?? 0L) * 2);
        Console.WriteLine(i.Maybe > 5L);

        // Into a managed nullable: null has to survive the lift, not become a zero instance.
        long? liftedNullable = i.Maybe;
        Console.WriteLine(liftedNullable.HasValue);
        Console.WriteLine(liftedNullable + 3L);

        // A ternary boxes both branches, so the result is usable either way round.
        Console.WriteLine((i.Id > 0 ? i.Id : i.Id + 1) + 1L);

        i.Maybe = null;
        Console.WriteLine(i.Maybe.HasValue);
        Console.WriteLine(i.Maybe ?? -1L);
        Console.WriteLine(i.Maybe + 1 == null);
        long? nulled = i.Maybe;
        Console.WriteLine(nulled.HasValue);
        Console.WriteLine(nulled ?? -2L);

        // Bitwise and shifts keep the full 64-bit width, which JavaScript's own operators do not.
        var w = new Info { Id = 4294967296L };
        Console.WriteLine(w.Id & 4294967296L);
        Console.WriteLine(w.Id | 1L);
        Console.WriteLine(w.Id ^ 1L);
        Console.WriteLine(w.Id >> 2);
        Console.WriteLine(w.Id << 2);
        Console.WriteLine(~w.Id);

        // A METHOD on the literal type is ordinary transpiled C#: its long parameter is managed,
        // even though the type's own slots are not, so a plain slot is lifted on the way in.
        Console.WriteLine(Info.Doubled(i.Id));
        Console.WriteLine(Info.Doubled(3L));
        Console.WriteLine(i.Describe());

        // Patterns: the subject is lifted once, so constants and relationals compare by value.
        Console.WriteLine(w.Id switch { > 4000000000L => "big", _ => "small" });
        Console.WriteLine(w.Id is 4294967296L);
        Console.WriteLine(w.Id is > 1L and < 9223372036854775807L);
        switch (w.Id)
        {
            case 4294967296L: Console.WriteLine("case hit"); break;
            default: Console.WriteLine("case missed"); break;
        }

        // Collections and the BCL take the managed representation.
        var list = new List<long> { i.Id, w.Id };
        Console.WriteLine(list[0] + list[1]);
        Console.WriteLine(Math.Max(i.Id, w.Id));
        Console.WriteLine(i.Id.CompareTo(w.Id));
        Console.WriteLine(i.Id.Equals(20L));
    }
""";

            var code = $$"""
using System;
using System.Collections.Generic;
using Transpose;
using Fixture;

namespace Fixture
{
    [ObjectLiteral]
    public class Info
    {
        public long Id { get; set; }
        public ulong Bytes { get; set; }
        public long? Maybe { get; set; }

        public static long Doubled(long n) => n * 2L + 1L;
        public string Describe() => (Id + 1L).ToString();
    }
}

public class Program
{
{{body}}
}
""";

            // Natively the same program with a plain class: [ObjectLiteral] changes the JavaScript
            // representation, never the C# semantics, so .NET is the oracle for every line above.
            var native = $$"""
using System;
using System.Collections.Generic;

public class Info
{
    public long Id { get; set; }
    public ulong Bytes { get; set; }
    public long? Maybe { get; set; }

    public static long Doubled(long n) => n * 2L + 1L;
    public string Describe() => (Id + 1L).ToString();
}

public class Program
{
{{body}}
}
""";

            await RunTest(code, overrideRoslynCode: native);
        }

        // ---- [External] --------------------------------------------------------

        /// <summary>
        /// The reported case: a binding library's <c>ulong</c> property (Blob/File's <c>size</c>) is a
        /// plain number in the browser. The fixture is a real JavaScript object built by
        /// <c>Script.Write</c>, so a mis-typed read fails exactly as it does in a page; its
        /// <c>slice</c> reports the <c>typeof</c> of each argument, which is how the write direction
        /// is pinned — an Int64 object arriving there was the second half of the bug.
        /// </summary>
        [TestMethod]
        public async Task External64BitMembersBehaveLikeNativeDotNet()
        {
            const string body = """
    public static void Main()
    {
        var b = Make();

        Console.WriteLine(b.size);
        Console.WriteLine(b.size > 1000);
        Console.WriteLine(b.size / 1024);
        Console.WriteLine(b.size + 1);
        Console.WriteLine(b.size * 2);
        Console.WriteLine(b.size % 7);
        Console.WriteLine(b.size == 3000000000UL);
        Console.WriteLine(b.size.ToString());
        Console.WriteLine((double)b.size / 1024.0);
        Console.WriteLine((int)b.lastModified);
        Console.WriteLine((long)b.size);
        Console.WriteLine($"{b.size} bytes");

        // Into managed slots.
        ulong managed = b.size;
        Console.WriteLine(managed + 1);
        Console.WriteLine(managed.ToString());
        object boxed = b.size;
        Console.WriteLine(boxed is ulong);
        Console.WriteLine(boxed.ToString());

        var list = new List<ulong>();
        list.Add(b.size);
        Console.WriteLine(list[0] + 2);
        Console.WriteLine(Math.Max(b.size, 5UL));
        Console.WriteLine(b.size.CompareTo(1UL));
        Console.WriteLine(b.size.Equals(3000000000UL));

        // Patterns.
        Console.WriteLine(b.size switch { > 2000000000UL => "big", _ => "small" });
        Console.WriteLine(b.size is > 1000UL);
        Console.WriteLine(b.size is 3000000000UL);

        // Written back OUT to JavaScript: every argument must arrive as a number.
        Console.WriteLine(b.slice(0, 10));
        long start = 5L;
        Console.WriteLine(b.slice(start, start + 2));
        Console.WriteLine(b.slice((long)b.size, 1));

        // An extension method's receiver is its first argument, so it crosses the same boundary.
        Console.WriteLine(b.size.Half());
        Console.WriteLine(managed.Half());

        var sum = 0UL;
        foreach (var x in new[] { b.size, 1UL }) sum += x;
        Console.WriteLine(sum);
    }
""";

            var code = $$"""
using System;
using System.Collections.Generic;
using Transpose;
using Fixture;

namespace Fixture
{
    [External]
    public class Blob
    {
        public extern ulong size { get; }
        public extern long lastModified { get; }
        public extern string slice(long start, long end);
    }
}

public static class Extras
{
    public static ulong Half(this ulong n) => n / 2UL;
}

public class Program
{
    static Blob Make() => Script.Write<Blob>(
        "({ size: 3000000000, lastModified: 1700000000000, slice: function (a, b) { return typeof a + ':' + a + '/' + typeof b + ':' + b; } })");

{{body}}
}
""";

            // The native oracle is the same program over a real object with the same values; `slice`
            // reproduces what the JavaScript fixture prints when both arguments arrive as numbers.
            var native = $$"""
using System;
using System.Collections.Generic;

public class Blob
{
    public ulong size => 3000000000UL;
    public long lastModified => 1700000000000L;
    public string slice(long start, long end) => "number:" + start + "/number:" + end;
}

public static class Extras
{
    public static ulong Half(this ulong n) => n / 2UL;
}

public class Program
{
    static Blob Make() => new Blob();

{{body}}
}
""";

            await RunTest(code, overrideRoslynCode: native);
        }

        /// <summary>
        /// A member bound to hand-written JavaScript by <c>[Template]</c> — rather than by its type
        /// being <c>[External]</c> — hands back a plain number for the same reason.
        /// </summary>
        [TestMethod]
        public async Task TemplateBound64BitMemberIsPlain()
        {
            var code = """
using System;
using Transpose;

public class Sizes
{
    [Template("({ n: 3000000000 }).n")]
    public static extern ulong Bytes();

    [Template("Math.round({v})")]
    public static extern long Round(double v);
}

public class Program
{
    public static void Main()
    {
        Console.WriteLine(Sizes.Bytes());
        Console.WriteLine(Sizes.Bytes() > 1000);
        Console.WriteLine(Sizes.Bytes() / 1024);
        Console.WriteLine(Sizes.Round(2.6));
        Console.WriteLine(Sizes.Round(2.6) + 1);
        ulong managed = Sizes.Bytes();
        Console.WriteLine(managed + 1);
    }
}
""";

            var native = """
using System;

public class Sizes
{
    public static ulong Bytes() => 3000000000UL;
    public static long Round(double v) => (long)Math.Round(v, MidpointRounding.AwayFromZero);
}

public class Program
{
    public static void Main()
    {
        Console.WriteLine(Sizes.Bytes());
        Console.WriteLine(Sizes.Bytes() > 1000);
        Console.WriteLine(Sizes.Bytes() / 1024);
        Console.WriteLine(Sizes.Round(2.6));
        Console.WriteLine(Sizes.Round(2.6) + 1);
        ulong managed = Sizes.Bytes();
        Console.WriteLine(managed + 1);
    }
}
""";

            await RunTest(code, overrideRoslynCode: native);
        }

        // ---- emit shape --------------------------------------------------------

        /// <summary>
        /// Pins which side of the boundary each construct lands on. The behaviour tests above prove
        /// the results are right; these prove they are right for the intended reason — a regression
        /// that boxed every read would still print the same numbers, only slower and with an Int64
        /// object crossing into JavaScript.
        /// </summary>
        [TestMethod]
        public void ExternalReadsStayPlainAndConvertAtTheBoundary()
        {
            var js = Translate("""
using System;
using Transpose;
using Fixture;

namespace Fixture
{
    [External]
    public class Blob
    {
        public extern ulong size { get; }
        public extern void take(long n);
    }
}

public class Program
{
    public static void Main() { }

    public static void Use(Blob b)
    {
        var cmp   = b.size > 1000;
        var add   = b.size + 1;
        var div   = b.size / 1024;
        var dbl   = (double)b.size;
        ulong mgd = b.size;
        long  own = 5L;
        b.take(own);
        b.take(7);
        b.take(own + 1);
        Console.WriteLine(cmp.ToString() + add + div + dbl + mgd + own);
    }
}
""");

            // Reads stay plain …
            Assert.IsTrue(js.Contains("b.size > 1000"), "a comparison against an external ulong is a plain JS comparison\n" + js);
            Assert.IsTrue(js.Contains("b.size + 1"), "addition stays plain\n" + js);
            Assert.IsTrue(js.Contains("TransposeR.idiv(b.size, 1024)"),
                "division truncates through the shared helper, not Int64.div and not JS `/`\n" + js);
            Assert.IsFalse(js.Contains("b.size.gt(") || js.Contains("b.size.add(") || js.Contains("b.size.div("),
                "no Int64 method is ever called on a plain number\n" + js);
            Assert.IsFalse(js.Contains("(b.size).toNumber()"), "a plain number needs no .toNumber()\n" + js);

            // … and are lifted only where a managed long slot needs a real instance.
            Assert.IsTrue(js.Contains("System.UInt64(b.size)"), "a managed ulong local is lifted at the boundary\n" + js);

            // Writes into the foreign slot unwrap instead.
            Assert.IsTrue(js.Contains("b.take((own).toNumber())"),
                "a managed Int64 argument is unwrapped for an external parameter\n" + js);
            Assert.IsTrue(js.Contains("b.take(7)"),
                "a literal argument to an external long parameter is a plain number, not System.Int64(7)\n" + js);
            Assert.IsFalse(js.Contains("b.take(System.Int64("),
                "no Int64 instance is ever passed into hand-written JavaScript\n" + js);
        }

        /// <summary>
        /// The other half of the same rule: an <c>[ObjectLiteral]</c>'s 64-bit members are plain
        /// numbers in the object it builds, including its declared defaults.
        /// </summary>
        [TestMethod]
        public void ObjectLiteral64BitMembersAreWrittenAsPlainNumbers()
        {
            var js = Translate("""
using Transpose;
using Fixture;

namespace Fixture
{
    [ObjectLiteral]
    public class Info
    {
        public long Id { get; set; }
        public ulong Bytes { get; set; }
    }
}

public class Program
{
    public static void Main()
    {
        long managed = 5L;
        var a = new Info { Id = 7L, Bytes = 3000000000UL };
        var b = new Info { Id = managed };
        System.Console.WriteLine(a.Id + b.Id + (long)a.Bytes);
    }
}
""");

            Assert.IsTrue(js.Contains("Id = 7") || js.Contains("Id: 7"), "a long literal member is a plain number\n" + js);
            Assert.IsTrue(js.Contains("Bytes = 3000000000") || js.Contains("Bytes: 3000000000"),
                "a ulong literal member is a plain number\n" + js);
            Assert.IsTrue(js.Contains("(managed).toNumber()"),
                "a managed Int64 written into a literal member is unwrapped\n" + js);
            Assert.IsFalse(js.Contains("Id = System.Int64(") || js.Contains("Bytes = System.UInt64("),
                "no Int64 instance is stored in a plain JS object\n" + js);
        }

        /// <summary>
        /// The guard that keeps the rest of the world boxed. The base library DEFINES
        /// System.Int64/UInt64, so its own externs — <c>DateTime.Ticks</c>, <c>long.MaxValue</c>,
        /// <c>long.Parse</c>, <c>TimeSpan.Ticks</c> — really do hand back instances, and must go on
        /// using the Int64 methods however the boundary rule is spelled.
        /// </summary>
        [TestMethod]
        public void BaseLibrary64BitMembersStayBoxed()
        {
            var js = Translate("""
using System;

public class Program
{
    public static void Main()
    {
        var d = new DateTime(2024, 1, 2);
        Console.WriteLine(d.Ticks > 0);
        Console.WriteLine(d.Ticks / 10000000L);
        Console.WriteLine(TimeSpan.FromSeconds(90).Ticks + 1);
        Console.WriteLine(long.Parse("123") + 1);
        Console.WriteLine(long.MaxValue - 1);
    }
}
""");

            Assert.IsTrue(js.Contains(".gt(System.Int64("), "DateTime.Ticks is a real Int64 and compares with .gt\n" + js);
            Assert.IsTrue(js.Contains(".div(System.Int64("), "and divides with Int64.div\n" + js);
            Assert.IsTrue(js.Contains(".add(System.Int64("), "TimeSpan.Ticks and long.Parse likewise\n" + js);
            Assert.IsFalse(js.Contains("TransposeR.idiv(System.DateTime.getTicks"),
                "the base library must not be treated as foreign JavaScript\n" + js);
        }

        /// <summary>
        /// The cost of the rule, pinned so nobody rediscovers it as a mystery. A slot in a plain JS
        /// object holds a JS number, and a JS number counts in ones only up to 2^53 — so a
        /// <c>long</c> above that rounds when it is stored in an <c>[External]</c> or
        /// <c>[ObjectLiteral]</c> member. For an external slot nothing is lost: the browser gave a
        /// number in the first place. For an object literal it is a real trade, taken deliberately —
        /// the alternative stored a <c>{low, high}</c> Int64 object in an object whose entire purpose
        /// is to be read by hand-written JavaScript and serialized to JSON. Managed <c>long</c>s,
        /// which is everything else, keep their full 64 bits (see
        /// <see cref="BaseLibrary64BitMembersStayBoxed"/>).
        /// </summary>
        [TestMethod]
        public async Task ObjectLiteralAbove2To53RoundsLikeAnyJsNumber()
        {
            var output = await RunTest("""
using System;
using Transpose;
using Fixture;

namespace Fixture
{
    [ObjectLiteral]
    public class Info { public long Id { get; set; } }
}

public class Program
{
    public static void Main()
    {
        // Exact: inside the safe-integer range.
        var ok = new Info { Id = 9007199254740991L };
        Console.WriteLine(ok.Id);
        Console.WriteLine(ok.Id == 9007199254740991L);

        // Rounded: past it. .NET would print 9223372036854775807 for both lines.
        var big = new Info { Id = long.MaxValue };
        Console.WriteLine(big.Id);

        // A managed long is unaffected — it never leaves the Int64 representation.
        long managed = long.MaxValue;
        Console.WriteLine(managed);
        Console.WriteLine(managed == long.MaxValue);
    }
}
""", skipRoslyn: true);

            StringAssert.Contains(output, "9007199254740991", "the safe-integer range is exact");
            StringAssert.Contains(output, "9223372036854775807", "a managed long keeps all 64 bits");
        }

        private static string Translate(string code)
        {
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, string.Join("\n", result.Errors));
            return result.Javascript!;
        }
    }
}
