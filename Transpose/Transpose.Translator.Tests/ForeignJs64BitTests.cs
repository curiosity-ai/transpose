using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// <c>long</c>/<c>ulong</c> that live in FOREIGN JavaScript — an <c>[External]</c> binding's
    /// member, an external <c>[ObjectLiteral]</c>'s field. (A literal declared in SOURCE may no longer
    /// have a 64-bit member at all: see <see cref="SourceObjectLiteral64BitMemberIsRejected"/>.)
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
        /// A <c>long</c>/<c>ulong</c> slot in a literal declared IN SOURCE is now rejected outright
        /// (TransposeR0004, <c>ObjectLiteralMemberScanner</c>) rather than unwrapped.
        ///
        /// <para>
        /// It used to be the sharpest illustration of the rule this file is about: an
        /// <c>[ObjectLiteral]</c> instance IS a plain JS object, so its 64-bit members were emitted as
        /// plain numbers — representable, but silently lossy above 2^53, since a JS number counts in
        /// ones only that far. That was a deliberate trade (the alternative put a <c>{low, high}</c>
        /// Int64 object into an object whose entire purpose is to be read by hand-written JavaScript
        /// and serialized to JSON), and it is now an error instead: a type Transpose itself
        /// materialises should not have a slot that quietly rounds. <c>ObjectLiteralMemberTypeTests</c>
        /// covers the check; this pins that the rule reaches the case this file documented.
        /// </para>
        ///
        /// <para>
        /// The unwrapping itself stays — see <see cref="ObjectLiteral64BitMembersAreWrittenAsPlainNumbers"/>
        /// for the shape that still reaches it, a binding library's literal, whose slots are the
        /// browser's plain numbers and are not this compiler's to declare.
        /// </para>
        /// </summary>
        [TestMethod]
        public void SourceObjectLiteral64BitMemberIsRejected()
        {
            var result = new RoslynTranslator().Translate("""
using System;
using Transpose;

[ObjectLiteral]
public class Info
{
    public long Id { get; set; }
    public ulong Bytes { get; set; }
    public long? Maybe { get; set; }
}

public class Program { public static void Main() { Console.WriteLine(new Info().Id); } }
""");

            Assert.IsFalse(result.Success, "a 64-bit slot in a source [ObjectLiteral] must not compile");
            var errors = string.Join("\n", result.Errors.Select(d => d.GetMessage()));
            StringAssert.Contains(errors, "'Id' is a 'long'", errors);
            StringAssert.Contains(errors, "'Bytes' is a 'ulong'", errors);
            StringAssert.Contains(errors, "'Maybe' is a 'long?'", errors);
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
        /// The other half of the same rule, on the literal that can still declare a 64-bit slot: a
        /// binding library's. Such a type is <c>[External]</c> — it describes an option bag that
        /// already exists in JavaScript, whose <c>size</c>-style members are the browser's plain
        /// numbers — so it is exempt from the check above, and the object Transpose builds for it must
        /// hold plain numbers rather than Int64 instances.
        /// </summary>
        [TestMethod]
        public void ObjectLiteral64BitMembersAreWrittenAsPlainNumbers()
        {
            var js = Translate("""
using Transpose;
using Fixture;

namespace Fixture
{
    [External]
    [ObjectLiteral]
    public class Info
    {
        public long id { get; set; }
        public ulong bytes { get; set; }
    }
}

public class Program
{
    public static void Main()
    {
        long managed = 5L;
        var a = new Info { id = 7L, bytes = 3000000000UL };
        var b = new Info { id = managed };
        System.Console.WriteLine(a.id + b.id + (long)a.bytes);
    }
}
""");

            Assert.IsTrue(js.Contains("id = 7") || js.Contains("id: 7"), "a long literal member is a plain number\n" + js);
            Assert.IsTrue(js.Contains("bytes = 3000000000") || js.Contains("bytes: 3000000000"),
                "a ulong literal member is a plain number\n" + js);
            Assert.IsTrue(js.Contains("(managed).toNumber()"),
                "a managed Int64 written into a literal member is unwrapped\n" + js);
            Assert.IsFalse(js.Contains("id = System.Int64(") || js.Contains("bytes = System.UInt64("),
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
        /// <c>long</c> above that rounds when it is stored in an <c>[External]</c> member — a
        /// binding's own literal included. Nothing is lost there: the browser gave a number in the
        /// first place. It is a source <c>[ObjectLiteral]</c>, where the value really is a managed
        /// <c>long</c> the compiler chose to flatten, that is now rejected instead of rounded (see
        /// <see cref="SourceObjectLiteral64BitMemberIsRejected"/>). Managed <c>long</c>s, which is
        /// everything else, keep their full 64 bits (see
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
    [External]
    [ObjectLiteral]
    public class Info { public long id { get; set; } }
}

public class Program
{
    public static void Main()
    {
        // Exact: inside the safe-integer range.
        var ok = new Info { id = 9007199254740991L };
        Console.WriteLine(ok.id);
        Console.WriteLine(ok.id == 9007199254740991L);

        // Rounded: past it. .NET would print 9223372036854775807 for both lines.
        var big = new Info { id = long.MaxValue };
        Console.WriteLine(big.id);

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
