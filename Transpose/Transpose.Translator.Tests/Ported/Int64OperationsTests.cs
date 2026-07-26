using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests.Ported
{
    /// <summary>
    /// System.Int64 / System.UInt64 beyond the arithmetic already covered by
    /// <see cref="NumericTypesTests"/> and <see cref="PrimitiveCastTests"/>.
    ///
    /// long/ulong are the awkward numeric types in a JS target: tps.js models them as
    /// System.Int64/UInt64 *objects*, not plain numbers. Everything that assumes "a number is a
    /// number" therefore has to be taught about them — JS `===` compares object identity, JS `+`
    /// string-concatenates an object, and `typeof x === "number"` is false. Each test below pins one
    /// place where that mattered.
    /// </summary>
    [TestClass]
    public class Int64OperationsTests : TranslatorTestBase
    {
        /// <summary>
        /// <c>switch</c> and constant patterns. A JS <c>switch</c> matches with <c>===</c>, so a
        /// long subject never equalled any <c>case</c> constant and every switch silently fell
        /// through to <c>default</c>.
        /// </summary>
        [TestMethod]
        public async Task SwitchAndConstantPatterns_Tests()
        {
            var code = """
using System;
public class Program
{
    public static void Main()
    {
        long l = 2L;
        ulong u = 2UL;
        decimal m = 2m;

        switch (l)
        {
            case 1L: Console.WriteLine("long one"); break;
            case 2L: Console.WriteLine("long two"); break;
            default: Console.WriteLine("long default"); break;
        }

        switch (u)
        {
            case 1UL: Console.WriteLine("ulong one"); break;
            case 2UL: Console.WriteLine("ulong two"); break;
            default: Console.WriteLine("ulong default"); break;
        }

        switch (m)
        {
            case 2m: Console.WriteLine("decimal two"); break;
            default: Console.WriteLine("decimal default"); break;
        }

        // A case that must NOT match.
        switch (l)
        {
            case 3L: Console.WriteLine("wrong"); break;
            default: Console.WriteLine("correct default"); break;
        }

        // Switch expressions and `is` patterns over the same values.
        Console.WriteLine(l switch { 1L => "a", 2L => "b", _ => "z" });
        Console.WriteLine(u switch { 2UL => "u2", _ => "z" });
        Console.WriteLine(m switch { 2m => "m2", _ => "z" });
        Console.WriteLine(l is 2L);
        Console.WriteLine(l is 3L);
        Console.WriteLine(l is 1L or 2L);
        Console.WriteLine(l is not 3L);
        Console.WriteLine(l is > 1L and < 5L);
        Console.WriteLine(l is > 5L);

        // Relational patterns compared the two Int64 objects with a JS `>`, which coerces both to
        // STRINGS — so `9L is > 10L` was true ("9" > "10" lexicographically). Digit counts must differ
        // here for the test to have any teeth.
        long nine = 9L;
        Console.WriteLine(nine is > 10L);
        Console.WriteLine(nine is < 10L);
        Console.WriteLine(nine is >= 10L);
        Console.WriteLine(nine is <= 10L);
        Console.WriteLine(nine switch { > 10L => "gt", < 10L => "lt", _ => "eq" });

        long negative = -5L;
        Console.WriteLine(negative is > 3L);
        Console.WriteLine(negative is < 3L);
        Console.WriteLine(negative is > (-10L));

        ulong un = 9UL;
        Console.WriteLine(un is < 10UL);
        Console.WriteLine(un is > 10UL);

        decimal dm = 9m;
        Console.WriteLine(dm is < 10m);
        Console.WriteLine(dm is > 10m);

        // Near the 64-bit boundary, where a string compare of equal-length digits happens to agree.
        long huge = 9223372036854775806L;
        Console.WriteLine(huge is > 9223372036854775805L);
        Console.WriteLine(huge is < 9223372036854775807L);
        Console.WriteLine(huge is >= 9223372036854775806L);

        // A null Nullable matches neither a constant nor a relational pattern.
        long? nul = null;
        Console.WriteLine(nul is 2L);
        Console.WriteLine(nul is > 1L);

        // Nullable subjects.
        long? n = 2L;
        Console.WriteLine(n switch { 2L => "n2", null => "null", _ => "z" });
        long? nn = null;
        Console.WriteLine(nn switch { 2L => "n2", null => "null", _ => "z" });

        // Edge values.
        long big = long.MaxValue;
        switch (big)
        {
            case long.MaxValue: Console.WriteLine("max"); break;
            default: Console.WriteLine("not max"); break;
        }

        long min = long.MinValue;
        Console.WriteLine(min is long.MinValue);
    }
}
""";
            await RunTest(code);
        }

        /// <summary>
        /// Type tests and patterns against a BOXED long. The pattern emitter used
        /// <c>typeof x === "number" &amp;&amp; Number.isInteger(x)</c> for every integer type, which
        /// neither recognised the boxed Int64 object nor told long from int — <c>case long l</c>
        /// matched a boxed int and the binding then threw "l.gt is not a function".
        /// </summary>
        [TestMethod]
        public async Task BoxedTypeTestsAndPatterns_Tests()
        {
            var code = """
using System;
public class Program
{
    static string Describe(object o) => o switch
    {
        long l when l > 100L => "big long " + l,
        long l => "long " + l,
        ulong ul => "ulong " + ul,
        int i => "int " + i,
        decimal m => "decimal " + m,
        _ => "other"
    };

    public static void Main()
    {
        long l = 5L;
        ulong u = 5UL;
        int i = 5;
        decimal m = 5m;

        // Boxing a variable and boxing a LITERAL must produce the same runtime type: `object o = 5L`
        // used to emit a bare `5`, so o.GetType() reported Int32 and `o is long` was false.
        object fromVariable = l;
        object fromLiteral = 5L;
        object uFromLiteral = 5UL;
        object mFromLiteral = 5m;

        Console.WriteLine(fromVariable.GetType().FullName);
        Console.WriteLine(fromLiteral.GetType().FullName);
        Console.WriteLine(uFromLiteral.GetType().FullName);
        Console.WriteLine(mFromLiteral.GetType().FullName);

        Console.WriteLine(fromVariable is long);
        Console.WriteLine(fromLiteral is long);
        Console.WriteLine(fromLiteral is int);
        Console.WriteLine(uFromLiteral is ulong);
        Console.WriteLine(uFromLiteral is long);
        Console.WriteLine(mFromLiteral is decimal);

        Console.WriteLine((long)fromLiteral);
        Console.WriteLine((ulong)uFromLiteral);
        Console.WriteLine(fromLiteral.ToString());

        Console.WriteLine(Describe(l));
        Console.WriteLine(Describe(200L));
        Console.WriteLine(Describe(u));
        Console.WriteLine(Describe(i));
        Console.WriteLine(Describe(m));
        Console.WriteLine(Describe("s"));

        // Declaration patterns bind a usable Int64, so 64-bit operators work on the bound value.
        if (fromLiteral is long bound) Console.WriteLine(bound * 3L);

        // Arrays / collections of object keep the boxed representation.
        object[] boxes = { 1L, 2UL, 3, 4m };
        foreach (var b in boxes) Console.WriteLine(b.GetType().Name + " " + b);
    }
}
""";
            await RunTest(code);
        }

        /// <summary>
        /// Lifted operators on <c>long?</c>/<c>ulong?</c>. The 64-bit branch matched on the type with
        /// Nullable stripped and so claimed these first, emitting <c>System.Int64(null).add(1)</c> —
        /// which is 1, not null, while <c>int? + 1</c> propagated correctly.
        /// </summary>
        [TestMethod]
        public async Task NullableLiftedOperators_Tests()
        {
            var code = """
using System;
public class Program
{
    public static void Main()
    {
        long? l = null;
        ulong? u = null;
        decimal? m = null;
        int? i = null;

        // Arithmetic on a null operand yields null...
        Console.WriteLine((l + 1L) == null);
        Console.WriteLine((l - 1L) == null);
        Console.WriteLine((l * 2L) == null);
        Console.WriteLine((l / 2L) == null);
        Console.WriteLine((l % 2L) == null);
        Console.WriteLine((l & 2L) == null);
        Console.WriteLine((l | 2L) == null);
        Console.WriteLine((l ^ 2L) == null);
        Console.WriteLine((l << 2) == null);
        Console.WriteLine((l >> 2) == null);
        Console.WriteLine((u + 1UL) == null);
        Console.WriteLine((m + 1m) == null);
        Console.WriteLine((i + 1) == null);
        Console.WriteLine((i / 2) == null);

        // ...and a relational comparison against null yields false, both ways round.
        Console.WriteLine(l > 1L);
        Console.WriteLine(l < 1L);
        Console.WriteLine(l >= 1L);
        Console.WriteLine(l <= 1L);

        // With a value present the operators compute normally (integer division truncating).
        long? k = 7L;
        ulong? uk = 7UL;
        int? ik = 7;

        Console.WriteLine(k + 1L);
        Console.WriteLine(k - 1L);
        Console.WriteLine(k * 2L);
        Console.WriteLine(k / 2L);
        Console.WriteLine(k % 3L);
        Console.WriteLine(k & 3L);
        Console.WriteLine(k | 8L);
        Console.WriteLine(k ^ 1L);
        Console.WriteLine(k << 1);
        Console.WriteLine(k >> 1);
        Console.WriteLine(uk / 2UL);
        Console.WriteLine(ik / 2);
        Console.WriteLine(ik * 3);
        Console.WriteLine(k > 1L);
        Console.WriteLine(k < 1L);
        Console.WriteLine(k == 7L);
        Console.WriteLine(k != 7L);

        // Promotion to floating point still applies through the lift.
        Console.WriteLine(k * 0.5);
        Console.WriteLine(l * 0.5 == null);
    }
}
""";
            await RunTest(code);
        }

        /// <summary>The rest of the <c>long?</c> surface: members, defaults, conversions, printing.</summary>
        [TestMethod]
        public async Task NullableMembers_Tests()
        {
            var code = """
using System;
public class Program
{
    static long? Get(bool b) => b ? 5L : (long?)null;

    public static void Main()
    {
        // `long? x = 7L` needs the same Int64 instance a plain long gets, or a later x.add(…) throws.
        long? a = 7L;
        long? b = null;
        long? c = 7;          // int literal widened into long?
        ulong? u = 7UL;

        Console.WriteLine(a + "|" + b + "|" + c + "|" + u + "|");
        Console.WriteLine(a.HasValue + " " + b.HasValue);
        Console.WriteLine(a.Value);
        Console.WriteLine(a.GetValueOrDefault() + " " + b.GetValueOrDefault() + " " + b.GetValueOrDefault(3L));
        Console.WriteLine((a ?? 0L) + " " + (b ?? 0L));
        Console.WriteLine(a == 7L);
        Console.WriteLine(a != 7L);
        Console.WriteLine(b == null);

        // Lifted == between two Nullables compared the two Int64 OBJECTS with `===`, i.e. by
        // identity, so equal values came out unequal.
        long? sameAsA = 7L;
        long? other = 8L;
        long? alsoNull = null;
        Console.WriteLine(a == sameAsA);
        Console.WriteLine(a != sameAsA);
        Console.WriteLine(a == other);
        Console.WriteLine(a == b);
        Console.WriteLine(b == alsoNull);
        Console.WriteLine(b != alsoNull);

        ulong? ua = 7UL, ub = 7UL;
        Console.WriteLine(ua == ub);

        decimal? da = 2.5m, db = 2.5m;
        Console.WriteLine(da == db);
        Console.WriteLine(a.Equals(7L));
        Console.WriteLine(a.Equals(3L));
        Console.WriteLine(a.ToString() + "|" + b.ToString() + "|");
        Console.WriteLine((long)a);
        Console.WriteLine(Get(true) + " " + Get(false) + "|");
        Console.WriteLine(a + a);
        Console.WriteLine(a * a);

        long?[] arr = { 1L, null, 3L };
        Console.WriteLine(string.Join(",", arr));

        try { Console.WriteLine(b.Value); }
        catch (InvalidOperationException) { Console.WriteLine("InvalidOperationException"); }
    }
}
""";
            await RunTest(code);
        }

        /// <summary>
        /// Compound assignment where the TARGET is floating point and the source is 64-bit. The
        /// implicit long→double widening was not applied, so `d += someLong` string-concatenated the
        /// Int64 object ("42" + "4" → "424") instead of adding.
        /// </summary>
        [TestMethod]
        public async Task CompoundAssignmentAcrossTypes_Tests()
        {
            var code = """
using System;
public class Program
{
    public static void Main()
    {
        long l = 4L;
        ulong ul = 4UL;

        double d = 10.5;
        d *= l; Console.WriteLine(d);
        d += l; Console.WriteLine(d);
        d -= l; Console.WriteLine(d);
        d /= l; Console.WriteLine(d);
        d %= l; Console.WriteLine(d);

        float f = 10.5f;
        f += l; Console.WriteLine(f);

        double du = 10.5;
        du += ul; Console.WriteLine(du);

        decimal m = 10.5m;
        m *= l; Console.WriteLine(m);

        // 64-bit target with narrower sources.
        long p = 10L; p *= 3;          Console.WriteLine(p);
        long q = 10L; q += 'a';        Console.WriteLine(q);
        long r = 10L; r -= (short)2;   Console.WriteLine(r);
        ulong s = 10UL; s *= 3u;       Console.WriteLine(s);
        long t = 10L; t >>= 2;         Console.WriteLine(t);
        long v = -8L; v >>= 1;         Console.WriteLine(v);
        ulong w = ulong.MaxValue; w >>= 1; Console.WriteLine(w);
        long x = 1L; x <<= 40;         Console.WriteLine(x);
    }
}
""";
            await RunTest(code);
        }

        /// <summary>
        /// Implicit long→double widening at every conversion site the emitter routes through
        /// <c>EmitExpressionConverted</c>: arguments, returns, ternary branches, initializers.
        /// </summary>
        [TestMethod]
        public async Task ImplicitWideningToFloating_Tests()
        {
            var code = """
using System;
using System.Collections.Generic;
public class Program
{
    static double TakeDouble(double d) => d * 2;
    static float TakeFloat(float f) => f * 2;
    static decimal TakeDecimal(decimal m) => m * 2;
    static double Widen(long v) => v;

    public static void Main()
    {
        long l = 21L;
        ulong u = 21UL;

        Console.WriteLine(TakeDouble(l));
        Console.WriteLine(TakeDouble(u));
        Console.WriteLine(TakeFloat(l));
        Console.WriteLine(TakeDecimal(l));
        Console.WriteLine(Widen(l));

        double local = l;
        Console.WriteLine(local + 0.5);

        bool cond = true;
        double t1 = cond ? l : 1.5;
        double t2 = cond ? 1.5 : l;
        Console.WriteLine(t1 + " " + t2);

        double[] arr = { l, 2.5 };
        Console.WriteLine(arr[0] + " " + arr[1]);

        var list = new List<double> { l };
        Console.WriteLine(list[0]);

        Console.WriteLine(Math.Max(1.5, (double)l));
        Console.WriteLine(Math.Sqrt(l));
    }
}
""";
            await RunTest(code);
        }

        /// <summary>Division and modulo by zero must raise the .NET exceptions, catchably.</summary>
        [TestMethod]
        public async Task DivisionEdgeCases_Tests()
        {
            var code = """
using System;
public class Program
{
    public static void Main()
    {
        long zero = 0L;
        ulong uzero = 0UL;
        long min = long.MinValue;

        // The long runtime used to throw a bare JS Error, which no catch clause could see.
        try { Console.WriteLine(1L / zero); }
        catch (DivideByZeroException) { Console.WriteLine("DivideByZeroException"); }

        try { Console.WriteLine(1L % zero); }
        catch (DivideByZeroException) { Console.WriteLine("DivideByZeroException"); }

        try { Console.WriteLine(1UL / uzero); }
        catch (DivideByZeroException) { Console.WriteLine("DivideByZeroException"); }

        // long.MinValue / -1 overflows rather than wrapping.
        try { Console.WriteLine(min / -1L); }
        catch (OverflowException) { Console.WriteLine("OverflowException"); }

        // Ordinary division truncates toward zero.
        Console.WriteLine(7L / 2L);
        Console.WriteLine(-7L / 2L);
        Console.WriteLine(7L % 3L);
        Console.WriteLine(-7L % 3L);
        Console.WriteLine(ulong.MaxValue / 2UL);
    }
}
""";
            await RunTest(code);
        }

        /// <summary>Shifts, bitwise operators and unchecked overflow wrapping across the full range.</summary>
        [TestMethod]
        public async Task ShiftsBitwiseAndWrapping_Tests()
        {
            var code = """
using System;
public class Program
{
    public static void Main()
    {
        long l = 1L;
        ulong u = 1UL;
        long max = long.MaxValue, min = long.MinValue;
        ulong umax = ulong.MaxValue;
        int n = 40;

        Console.WriteLine(l << 40);
        Console.WriteLine(l << 63);
        Console.WriteLine(l << 64);   // the shift count is masked to & 63
        Console.WriteLine(l << 65);
        Console.WriteLine(l << n);
        Console.WriteLine(min >> 1);
        Console.WriteLine(-8L >> 2);
        Console.WriteLine(umax >> 1);
        Console.WriteLine(umax >> 63);
        Console.WriteLine(u << 63);
        Console.WriteLine(~l);
        Console.WriteLine(~u);
        Console.WriteLine(-l);

        Console.WriteLine(0xFF00FF00FF00FF00UL & 0x00FF00FF00FF00FFUL);
        Console.WriteLine(0xFF00FF00FF00FF00UL | 0x00FF00FF00FF00FFUL);
        Console.WriteLine(0xFF00FF00FF00FF00UL ^ 0xFFFFFFFFFFFFFFFFUL);

        // Unchecked wrapping at the range boundaries.
        Console.WriteLine(max + 1L);
        Console.WriteLine(min - 1L);
        Console.WriteLine(umax + 1UL);
        Console.WriteLine(max * 2L);
    }
}
""";
            await RunTest(code);
        }

        /// <summary>Instance members, parsing and formatting on long/ulong.</summary>
        [TestMethod]
        public async Task MemberResolution_Tests()
        {
            var code = """
using System;
using System.Globalization;
public class Program
{
    public static void Main()
    {
        long l = 1234567890123L;
        ulong u = 12345678901234567890UL;

        Console.WriteLine(l.ToString());
        Console.WriteLine(l.ToString("N0", CultureInfo.InvariantCulture));
        Console.WriteLine(l.ToString("X"));
        Console.WriteLine(l.ToString("D20"));
        Console.WriteLine(u.ToString());
        Console.WriteLine(l.CompareTo(5L));
        Console.WriteLine(l.CompareTo(l));
        Console.WriteLine(5L.CompareTo(l));
        Console.WriteLine(l.Equals(l));
        Console.WriteLine(l.Equals(5L));
        Console.WriteLine(l.GetHashCode() == l.GetHashCode());
        Console.WriteLine(long.MinValue + " " + long.MaxValue);
        Console.WriteLine(ulong.MinValue + " " + ulong.MaxValue);
        Console.WriteLine(long.Parse("-9223372036854775808"));
        Console.WriteLine(ulong.Parse("18446744073709551615"));
        Console.WriteLine(long.TryParse("123", out long p) + " " + p);
        Console.WriteLine(long.TryParse("bad", out long q) + " " + q);
        Console.WriteLine(Convert.ToInt64("42"));
        Console.WriteLine(Convert.ToUInt64("42"));
        Console.WriteLine(Convert.ToDouble(l));
        Console.WriteLine(Convert.ToInt32(5L));
        Console.WriteLine(Convert.ToString(l));
        Console.WriteLine(BitConverter.GetBytes(l).Length);
        Console.WriteLine(BitConverter.ToInt64(BitConverter.GetBytes(l), 0));
        Console.WriteLine(BitConverter.DoubleToInt64Bits(1.5));
        Console.WriteLine(BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(1.5)));
        Console.WriteLine(l.GetType().FullName);
        Console.WriteLine(u.GetType().FullName);

        // Interface dispatch.
        IComparable<long> c = 5L;
        Console.WriteLine(c.CompareTo(6L));
        IEquatable<long> e = 5L;
        Console.WriteLine(e.Equals(5L));
        IComparable c2 = 5L;
        Console.WriteLine(c2.CompareTo(6L));

        // Interpolation and string.Format.
        Console.WriteLine($"{l}");
        Console.WriteLine($"{l:N0}");
        Console.WriteLine(string.Format("{0} {1}", l, u));
    }
}
""";
            await RunTest(code);
        }

        /// <summary>System.Math with long/ulong arguments — including the overloads that used to be
        /// missing (so a long argument was ambiguous between the double and decimal ones).</summary>
        [TestMethod]
        public async Task MathWithInt64_Tests()
        {
            var code = """
using System;
public class Program
{
    public static void Main()
    {
        long l = 21L;
        ulong u = 21UL;

        Console.WriteLine(Math.Abs(l));
        Console.WriteLine(Math.Abs(-l));
        Console.WriteLine(Math.Min(l, 5L));
        Console.WriteLine(Math.Max(l, 5L));
        Console.WriteLine(Math.Min(u, 5UL));
        Console.WriteLine(Math.Max(u, 5UL));
        Console.WriteLine(Math.Sign(l));
        Console.WriteLine(Math.Sign(-l));
        Console.WriteLine(Math.Sign(0L));
        Console.WriteLine(Math.Sqrt(l));
        Console.WriteLine(Math.Pow(l, 2L));
        Console.WriteLine(Math.Pow(u, 2UL));
        Console.WriteLine(Math.Clamp(l, 1L, 10L));
        Console.WriteLine(Math.Clamp(l, 30L, 40L));
        Console.WriteLine(Math.DivRem(17L, 5L, out long rem) + " r" + rem);

        // BigMul keeps the full 64-bit product where int multiplication would wrap.
        int i32 = int.MaxValue;
        uint u32 = uint.MaxValue;
        Console.WriteLine(Math.BigMul(i32, i32));
        Console.WriteLine(Math.BigMul(u32, u32));
        Console.WriteLine(i32 * i32);   // the wrapped 32-bit product, for contrast
    }
}
""";
            await RunTest(code);
        }

        /// <summary>Collections and LINQ keyed on / aggregating long values.</summary>
        [TestMethod]
        public async Task CollectionsAndLinq_Tests()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;
public class Program
{
    public static void Main()
    {
        var list = new List<long> { 3L, 1L, 2L, 1L };

        list.Sort();
        Console.WriteLine(string.Join(",", list));
        Console.WriteLine(list.Contains(2L) + " " + list.IndexOf(2L) + " " + list.Contains(9L));

        // Average over long returns DOUBLE — Int64 division would truncate 7/4 to 1.
        Console.WriteLine(list.Sum());
        Console.WriteLine(list.Min());
        Console.WriteLine(list.Max());
        Console.WriteLine(list.Average());
        Console.WriteLine(list.Average(x => x));
        Console.WriteLine(new List<long> { 1L, 2L }.Average());

        Console.WriteLine(string.Join(",", list.Distinct()));
        Console.WriteLine(string.Join(",", list.OrderByDescending(x => x)));
        Console.WriteLine(string.Join(",", list.GroupBy(x => x).Select(g => g.Key + ":" + g.Count())));
        Console.WriteLine(list.Where(x => x > 1L).Count());
        Console.WriteLine(list.Aggregate(0L, (a, b) => a + b));
        Console.WriteLine(Enumerable.Range(1, 5).Select(i => (long)i).Sum());
        Console.WriteLine(Enumerable.Range(1, 5).Sum(i => (long)i));

        var dict = new Dictionary<long, string> { { 1L, "a" }, { 2L, "b" } };
        Console.WriteLine(dict[1L] + dict[2L] + dict.ContainsKey(2L) + dict.ContainsKey(9L) + dict.Count);
        dict[1L] = "z";
        Console.WriteLine(dict[1L] + " " + dict.Count);

        var set = new HashSet<long> { 1L, 2L, 1L };
        Console.WriteLine(set.Count + " " + set.Contains(2L));

        Console.WriteLine(Comparer<long>.Default.Compare(1L, 2L));
        Console.WriteLine(EqualityComparer<long>.Default.Equals(1L, 1L));
        Console.WriteLine(EqualityComparer<long>.Default.Equals(1L, 2L));

        long[] arr = { 5L, 3L, 9L };
        Array.Sort(arr);
        Console.WriteLine(string.Join(",", arr));
        Console.WriteLine(Array.IndexOf(arr, 9L));

        var ulist = new List<ulong> { 3UL, 1UL };
        ulist.Sort();
        Console.WriteLine(string.Join(",", ulist));

        var udict = new Dictionary<ulong, int> { { 18446744073709551615UL, 7 } };
        Console.WriteLine(udict[18446744073709551615UL]);

        // A long used as an array index / loop variable.
        var strs = new[] { "a", "b", "c" };
        long idx = 1L;
        Console.WriteLine(strs[idx]);

        var sb = new System.Text.StringBuilder();
        for (long i = 0L; i < 3L; i++) sb.Append(strs[i]);
        Console.WriteLine(sb.ToString());
    }
}
""";
            await RunTest(code);
        }

        /// <summary>
        /// An enum with a 64-bit backing type. Its ordinals are emitted as plain JS numbers, so a
        /// <c>(Big)someLong</c> cast has to read the magnitude out of the Int64 instance — otherwise
        /// no member matched and ToString() printed the raw number.
        ///
        /// Ordinals are limited to JavaScript's exact-integer range (2^53); a member above that loses
        /// precision, which is why <c>long.MaxValue</c> is not used here.
        /// </summary>
        [TestMethod]
        public async Task Int64BackedEnum_Tests()
        {
            var code = """
using System;

public enum Big : long { A = 1L, B = 5000000000L }
public enum UBig : ulong { X = 1UL, Y = 5000000000UL }

public class Program
{
    public static void Main()
    {
        Console.WriteLine(Big.B);
        Console.WriteLine((long)Big.B);
        Console.WriteLine(Big.B.ToString());
        Console.WriteLine((Big)5000000000L);
        Console.WriteLine(Enum.Parse<Big>("B"));
        Console.WriteLine(UBig.Y);
        Console.WriteLine((ulong)UBig.Y);

        long raw = 5000000000L;
        Console.WriteLine((Big)raw);
        Console.WriteLine((Big)raw == Big.B);

        Big v = Big.B;
        switch (v)
        {
            case Big.A: Console.WriteLine("A"); break;
            case Big.B: Console.WriteLine("B"); break;
            default: Console.WriteLine("other"); break;
        }

        Console.WriteLine(v == Big.B);
        Console.WriteLine(v is Big.B);
        Console.WriteLine(v is Big.A);
        Console.WriteLine(v is not Big.A);
        Console.WriteLine(v is Big.A or Big.B);
        Console.WriteLine(v switch { Big.A => "A", Big.B => "B", _ => "?" });

        object boxed = Big.B;
        Console.WriteLine(boxed.GetType().Name + " " + boxed);
        Console.WriteLine(boxed is Big);
    }
}
""";
            await RunTest(code);
        }

        /// <summary>
        /// `x is SomeType.Member` parses as an is-EXPRESSION rather than a pattern, and the emitter
        /// read only the right side's type — so it emitted a TYPE test and `s is Small.A` was true for
        /// every value of the enum. Not 64-bit-specific, but found while auditing this area.
        /// </summary>
        [TestMethod]
        public async Task IsExpressionAgainstAConstant_Tests()
        {
            var code = """
using System;

public enum Small { A = 1, B = 2 }
public enum Big : long { A = 1L, B = 5000000000L }

public static class Limits
{
    public const int MaxInt = 7;
    public const long MaxLong = 5000000000L;
}

public class Program
{
    public static void Main()
    {
        Small s = Small.B;
        Console.WriteLine(s is Small.B);
        Console.WriteLine(s is Small.A);
        Console.WriteLine(s is not Small.A);
        Console.WriteLine(s is Small.A or Small.B);

        Big b = Big.A;
        Console.WriteLine(b is Big.A);
        Console.WriteLine(b is Big.B);

        int i = 7;
        Console.WriteLine(i is Limits.MaxInt);
        Console.WriteLine(i is 8);

        long l = 5000000000L;
        Console.WriteLine(l is Limits.MaxLong);
        Console.WriteLine(l is 5000000001L);

        // A genuine type test on the same syntax shape must still be a type test.
        object o = "text";
        Console.WriteLine(o is System.String);
        Console.WriteLine(o is System.Int32);
    }
}
""";
            await RunTest(code);
        }
    }
}
