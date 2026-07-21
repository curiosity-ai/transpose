using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Exhaustive coverage of primitive-type conversions (C# `(T)x` casts and implicit numeric
    /// conversions). C# and JavaScript disagree on integer width and overflow, so every narrowing
    /// or reinterpreting conversion needs explicit handling in the emitter:
    ///  - integer → narrower integer wraps (mask + sign-extend): `(short)70000` == 4464, `(sbyte)200` == -56;
    ///  - signed ⇄ unsigned reinterpretation: `(uint)-1` == 4294967295, `(int)uint` wraps, `(long)ulong` reinterprets;
    ///  - long/ulong → narrower reads the low bits (a plain `.toNumber()` loses precision above 2^53);
    ///  - float/double → integer SATURATES to the target range (NaN → 0), unlike an integer cast which wraps;
    ///  - char boxing: a boxed / ToString()'d char must render as its character, not its code point.
    /// Each test diffs Transpose's JS output against native .NET, so the assertions are the CLR's.
    /// </summary>
    [TestClass]
    public class PrimitiveCastTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task IntToNarrowerSignedAndUnsignedAsync()
        {
            await RunTest(@"
using System;
public class Program
{
    static void P(object o) => Console.WriteLine(o);
    public static void Main()
    {
        int big = 70000;
        P((short)big);   P((ushort)big);  P((byte)big);   P((sbyte)big);
        int neg = -123;
        P((byte)neg);    P((sbyte)neg);   P((short)neg);  P((ushort)neg);  P((uint)neg);
        int p300 = 300, n200 = -200;   // via variables: a constant narrowing overflow is a compile error
        P((sbyte)p300);  P((byte)p300);  P((sbyte)n200); P((byte)n200);
        // re-wrapping an int overflow the C# way (JS + does not overflow on its own)
        int a = 2000000000;
        P((int)(a + a)); P((uint)(a + a));
    }
}");
        }

        [TestMethod]
        public async Task LongAndUlongNarrowingAsync()
        {
            await RunTest(@"
using System;
public class Program
{
    static void P(object o) => Console.WriteLine(o);
    public static void Main()
    {
        long L = 0x1_2233_4455L;
        P((int)L);  P((uint)L);  P((short)L); P((ushort)L); P((byte)L); P((sbyte)L);
        long Ln = -5000000000L;
        P((int)Ln); P((uint)Ln); P((short)Ln); P((byte)Ln);
        // signed/unsigned 64-bit reinterpretation
        ulong ul = 18000000000000000000UL;
        P((long)ul);
        long neg1 = -1L;
        P((ulong)neg1);
        uint u = 4000000000u;
        P((int)u);  P((short)u); P((byte)u);
        // int widened to long via arithmetic must not overflow like a 32-bit add
        int x = 2000000000;
        P((long)x + x);
    }
}");
        }

        [TestMethod]
        public async Task FloatToIntegerSaturatesAsync()
        {
            // The CLR saturates an out-of-range float→integer conversion to the target's Min/Max
            // (NaN → 0) — it does NOT wrap. Sub-word targets saturate to int32 first, then mask.
            await RunTest(@"
using System;
public class Program
{
    static void P(object o) => Console.WriteLine(o);
    public static void Main()
    {
        double[] vals = { 300.9, -3.9, 70000.5, 5e9, -5e9, 1e20, -1e20,
                          double.NaN, double.PositiveInfinity, double.NegativeInfinity,
                          2147483647.9, 2147483648.9, 4294967296.5 };
        foreach (var d in vals)
        {
            P((sbyte)d); P((byte)d); P((short)d); P((ushort)d);
            P((int)d);   P((uint)d); P((long)d);  P((ulong)d);
        }
    }
}");
        }

        [TestMethod]
        public async Task FloatToIntegerInRangeTruncatesTowardZeroAsync()
        {
            await RunTest(@"
using System;
public class Program
{
    static void P(object o) => Console.WriteLine(o);
    public static void Main()
    {
        P((int)3.9);    P((int)-3.9);   P((int)3.2);   P((int)-3.2);
        P((long)3.9);   P((long)-3.9);
        float f = 2.75f;
        P((int)f);      P((byte)f);
        double huge = 12345.678;
        P((int)huge);   P((short)huge); P((byte)huge);
    }
}");
        }

        [TestMethod]
        public async Task DecimalConversionsAsync()
        {
            await RunTest(@"
using System;
public class Program
{
    static void P(object o) => Console.WriteLine(o);
    public static void Main()
    {
        // decimal → integer is a CHECKED conversion in C# (throws on overflow), so stay in range
        // and verify truncation toward zero.
        decimal m = 200.9m;
        P((int)m);   P((byte)m);  P((short)m);  P((long)m);
        decimal neg = -3.9m;
        P((int)neg); P((sbyte)neg);
        int i = 300;
        P((decimal)i);
        double d = 2.5;
        P((decimal)d);
        decimal back = 42.5m;
        P((double)back); P((float)back);
    }
}");
        }

        [TestMethod]
        public async Task CharIntConversionsAsync()
        {
            await RunTest(@"
using System;
public class Program
{
    public static void Main()
    {
        char c = 'A';
        Console.WriteLine((int)c);            // 65
        int ci = 66;
        Console.WriteLine((char)ci);          // B
        Console.WriteLine((char)(c + 1));     // B
        int cbig = 65601;                     // 0x10041 -> low 16 = 'A'
        Console.WriteLine((int)(char)cbig);   // 65
        Console.WriteLine((char)97);          // a
        char z = (char)(90);
        Console.WriteLine(z);                 // Z
    }
}");
        }

        [TestMethod]
        public async Task CharBoxingAndToStringAsync()
        {
            // A char is a bare code-point number at runtime; boxing / ToString() must still render
            // it as its character.
            await RunTest(@"
using System;
public class Program
{
    public static void Main()
    {
        char c = 'A';
        Console.WriteLine(c);                       // A
        Console.WriteLine(c.ToString());            // A
        object o = c;                               // boxed
        Console.WriteLine(o);                       // A
        Console.WriteLine(o.ToString());            // A
        Console.WriteLine(""v="" + c);              // v=A
        object[] arr = { 'X', 'Y', 'Z' };
        Console.WriteLine(arr[0] + "","" + arr[1] + "","" + arr[2]);  // X,Y,Z
        Console.WriteLine(string.Format(""{0}-{1}"", 'p', 'q'));      // p-q
    }
}");
        }

        [TestMethod]
        public async Task ImplicitWideningConversionsAsync()
        {
            await RunTest(@"
using System;
public class Program
{
    static void P(object o) => Console.WriteLine(o);
    public static void Main()
    {
        byte b = 200;
        int i = b;          P(i);           // 200
        long l = i;         P(l);           // 200
        double d = l;       P(d);           // 200
        short s = -5;
        int si = s;         P(si);          // -5
        long sl = si;       P(sl);          // -5
        float f = 3;        P(f);           // 3
        decimal m = 42;     P(m);           // 42
        char c = 'A';
        int cc = c;         P(cc);          // 65
    }
}");
        }

        [TestMethod]
        public async Task CompoundAssignmentNarrowingAsync()
        {
            // `b += x` on a narrow type compiles to `b = (T)(b + x)` and must wrap to the type width.
            await RunTest(@"
using System;
public class Program
{
    static void P(object o) => Console.WriteLine(o);
    public static void Main()
    {
        byte b = 250; b += 10;   P(b);        // 4
        sbyte s = 120; s += 20;  P(s);        // -116
        short sh = 32000; sh += 1000; P(sh);  // -32536
        ushort us = 65530; us += 10; P(us);   // 4
        byte c = 5; c -= 10;     P(c);        // 251
        byte m = 30; m *= 10;    P(m);        // 44 (300 & 0xff)
        short shl = 20000; shl <<= 2; P(shl); // 14464 (80000 & 0xffff, sign-extended)
        byte bm = 200; bm %= 150; P(bm);      // 50
        char ch = 'A'; ch += (char)1; P((int)ch); // 66
    }
}");
        }

        [TestMethod]
        public async Task IntegerArithmeticOverflowWrapsAsync()
        {
            // "Managed" integer arithmetic (H5 default, matching Tesserae's h5.json `integer: Managed`):
            // 32-bit +/-/*/ / and unary/bitwise/shift wrap on overflow like .NET unchecked, rather
            // than growing as JS Numbers. Non-compound and compound forms must agree.
            await RunTest(@"
using System;
public class Program
{
    static void P(object o) => Console.WriteLine(o);
    public static void Main()
    {
        int a = 2000000000, b = 2000000000, c = -2000000000;
        P(a + b);          // -294967296
        P(a - c);          // -294967296
        P(a * b);          // -1651507200
        P(a + b + b);      // 1705032704
        int big = 1500000000; P(big + big); P(big * 3);
        int imin = int.MinValue; P(imin - 1);     // 2147483647
        P(-imin);                                  // -2147483648
        // compound must agree with the expanded form
        int s = a; s += b;  P(s);                  // -294967296
        int t = a; t *= 2;  P(t);                  // -294967296
        // unsigned
        uint ua = 3000000000, ub = 2000000000;
        P(ua + ub);        // 705032704
        P(ub - ua);        // 3294967296
        P(ua * 2u);        // 1705032704
        P(ua << 1);        // 1705032704
        P(0xF0000000u | 0xFu); // 4026531855
        uint big2 = 0xFF000000u; int four = 4; P(big2 >> four); // 267386880
        uint uc = ua; uc += ub; P(uc);             // 705032704
        // sub-word arithmetic promotes to int
        byte p = 200, q = 100; P(p + q);           // 300
    }
}");
        }

        [TestMethod]
        public async Task IncrementDecrementNarrowingAsync()
        {
            // ++/-- on a sub-word integer wraps at the type boundary (`byte 255 + 1` == 0), and a
            // normal-range loop counter is unaffected.
            await RunTest(@"
using System;
public class Program
{
    static void P(object o) => Console.WriteLine(o);
    public static void Main()
    {
        byte b = 255; b++;  P(b);          // 0
        sbyte s = 127; s++; P(s);          // -128
        byte d = 0; d--;    P(d);          // 255
        ushort u = 65535; ++u; P(u);       // 0
        short sh = -32768; --sh; P(sh);    // 32767
        // in-range increments and a byte-counted loop behave normally
        int sum = 0;
        for (byte i = 0; i < 10; i++) sum += i;
        P(sum);                            // 45
        byte n = 40; n++; P(n);            // 41
    }
}");
        }

        [TestMethod]
        public async Task ArrayBoundsCheckingAsync()
        {
            // ArrayIndex = Managed (H5/Tesserae default): out-of-range access throws
            // IndexOutOfRangeException instead of reading/writing `undefined`.
            await RunTest(@"
using System;
public class Program
{
    public static void Main()
    {
        int[] a = { 10, 20, 30 };
        Console.WriteLine(a[1]);                 // 20
        a[2] = 99; Console.WriteLine(a[2]);      // 99
        try { int x = a[5]; Console.WriteLine(x); } catch (IndexOutOfRangeException) { Console.WriteLine(""IOR read""); }
        try { a[10] = 1; } catch (IndexOutOfRangeException) { Console.WriteLine(""IOR write""); }
        try { int y = a[-1]; } catch (IndexOutOfRangeException) { Console.WriteLine(""IOR neg""); }
        int sum = 0; for (int i = 0; i < a.Length; i++) sum += a[i];
        Console.WriteLine(sum);                  // 129
        int[][] j = new int[2][]; j[0] = new[] { 1, 2 };
        Console.WriteLine(j[0][1]);              // 2
        string[] s = { ""x"", ""y"" }; Console.WriteLine(s[1]);   // y
    }
}");
        }

        [TestMethod]
        public async Task CheckedReferenceAndUnboxCastsAsync()
        {
            // IgnoreCast = false (H5/Tesserae default): an invalid reference downcast or unboxing
            // throws InvalidCastException; upcasts / valid casts / null pass through.
            await RunTest(@"
using System;
public class Animal { public virtual string S() => ""a""; }
public class Dog : Animal { public override string S() => ""dog""; }
public class Cat : Animal { public override string S() => ""cat""; }
interface IThing { int V(); }
public class Thing : IThing { public int V() => 7; }
public class Program
{
    public static void Main()
    {
        Animal a = new Cat();
        try { Dog d = (Dog)a; Console.WriteLine(d.S()); } catch (InvalidCastException) { Console.WriteLine(""IC down""); }
        Dog ok = (Dog)(Animal)new Dog(); Console.WriteLine(ok.S());        // dog
        object o = ""hi"";
        try { int n = (int)o; Console.WriteLine(n); } catch (InvalidCastException) { Console.WriteLine(""IC unbox""); }
        object bi = 42; Console.WriteLine((int)bi);                        // 42
        object t = new Thing(); Console.WriteLine(((IThing)t).V());        // 7
        Animal up = new Dog(); Console.WriteLine(up.S());                  // dog (upcast)
        object n2 = null; string sn = (string)n2; Console.WriteLine(sn == null); // True
        object so = ""world""; Console.WriteLine((string)so);              // world
        try { object io = 5; Console.WriteLine((string)io); } catch (InvalidCastException) { Console.WriteLine(""IC str""); }
    }
}");
        }

        [TestMethod]
        public async Task EnumBoxedToObjectKeepsTypeAsync()
        {
            await RunTest(@"
using System;
enum Color { Red, Green, Blue }
public class Program
{
    public static void Main()
    {
        object e = DayOfWeek.Monday;
        Console.WriteLine(e.GetType().Name);   // DayOfWeek
        Console.WriteLine(e.ToString());       // Monday
        Console.WriteLine(e);                  // Monday
        object c = Color.Green;
        Console.WriteLine(c.GetType().Name);   // Color
        Console.WriteLine(c.ToString());       // Green
        Console.WriteLine(c.Equals(Color.Green)); // True
        Console.WriteLine((int)Color.Blue);    // 2
    }
}");
        }

        [TestMethod]
        public async Task UnsignedFormattingRoundTripAsync()
        {
            // The original Guid.ToString() breakage: a negative int cast to uint kept its sign and
            // formatted as "-7b…", breaking hex formatting.
            await RunTest(@"
using System;
public class Program
{
    public static void Main()
    {
        int neg = -123;
        Console.WriteLine((uint)neg);                     // 4294967173
        Console.WriteLine(((uint)neg).ToString(""x8""));  // ffffff85
        Console.WriteLine(((byte)200).ToString(""x2""));  // c8
        uint u = 0xDEADBEEF;
        Console.WriteLine(u.ToString(""X8""));            // DEADBEEF
    }
}");
        }
    }
}
