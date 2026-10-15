using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Regression tests for silent mis-compilations found by a correctness audit: each snippet is
    /// transpiled, run on Node and its output diffed against native .NET.
    /// </summary>
    [TestClass]
    public class AuditRegressionTests : TranslatorTestBase
    {
        // ---- an expression-bodied lambda / local function converts to its return type ----

        [TestMethod]
        public async Task ExpressionBodiedLambdaAndLocalFunctionConvertToTheReturnType()
        {
            await RunTest(@"
using System;
using System.Threading.Tasks;
public enum Status { A, B, C }
public class Row { public Status Status = Status.B; }
public struct P { public int X; public P(int x) { X = x; } }
public class Program
{
    static P sf = new P(1);
    public static async Task Main()
    {
        Func<Row, object> col = r => r.Status; Console.WriteLine(""enum->object "" + col(new Row()));
        Func<object> g = () => 'a'; Console.WriteLine(""char->object "" + g());
        int i = 7;
        Func<long> fl = () => i; Console.WriteLine(""int->long "" + (fl() * 3000000000L));
        Func<decimal> fd = () => i; Console.WriteLine(""int->decimal "" + (fd() / 2));
        Func<P> fp = () => sf; var p = fp(); p.X = 9; Console.WriteLine(""struct copy "" + sf.X);
        Func<Task<long>> al = async () => i; Console.WriteLine(""async long "" + ((await al()) * 3000000000L));
        object Lo() => Status.C; Console.WriteLine(""local enum->object "" + Lo());
        long Ll() => i; Console.WriteLine(""local int->long "" + (Ll() * 3000000000L));
        async Task<decimal> Ad() => i; Console.WriteLine(""local async decimal "" + ((await Ad()) / 2));
        P Lp() => sf; var q = Lp(); q.X = 5; Console.WriteLine(""local struct "" + sf.X);
        Action act = () => i++; act(); Console.WriteLine(""void "" + i);
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- patterns against a boxed enum, NaN, and a struct designation ----

        [TestMethod]
        public async Task PatternsMatchBoxedEnumsAndNaNAndCopyBoundStructs()
        {
            await RunTest(@"
using System;
public enum E { A, B, C }
public struct P { public int X; }
public class Program
{
    static string Sw(object o) { switch (o) { case E.B: return ""B""; case E.C: return ""C""; case 5: return ""five""; default: return ""other""; } }
    static string Gen<T>(T t) { switch (t) { case E.B: return ""gB""; default: return ""gother""; } }
    public static void Main()
    {
        object o = E.B;
        Console.WriteLine(""switch "" + Sw(o) + "" "" + Sw(E.C) + "" "" + Sw(5) + "" "" + Sw(E.A));
        Console.WriteLine(""is "" + (o is E.B) + "" "" + (o is E.A) + "" "" + (o is not E.B));
        Console.WriteLine(""swexpr "" + (o switch { E.A => ""a"", E.B => ""b"", _ => ""?"" }));
        Console.WriteLine(""bind "" + (o is E e && e == E.B));
        if (o is E e2) Console.WriteLine(""bind2 "" + (e2 == E.B) + "" "" + (int)e2 + "" "" + e2);
        Console.WriteLine(""generic "" + Gen(E.B) + "" "" + Gen(""x""));
        double nan = double.NaN;
        Console.WriteLine(""nan switch "" + (nan switch { double.NaN => ""nan"", _ => ""num"" }));
        switch (nan) { case double.NaN: Console.WriteLine(""nan case""); break; default: Console.WriteLine(""num case""); break; }
        Console.WriteLine(""nan is "" + (nan is double.NaN) + "" "" + (1.0 is double.NaN));
        object onan = double.NaN;
        Console.WriteLine(""onan "" + (onan is double.NaN));
        float fn = float.NaN; Console.WriteLine(""fnan "" + (fn is float.NaN));
        object op = new P { X = 1 };
        if (op is P p) { p.X = 5; Console.WriteLine(""struct copy "" + ((P)op).X + "" "" + p.X); }
        switch (op) { case P q: q.X = 7; Console.WriteLine(""case struct copy "" + ((P)op).X); break; }
        E? ne = E.C;
        Console.WriteLine(""nullable enum "" + (ne is E.C) + "" "" + (ne switch { E.C => ""c"", _ => ""?"" }));
        E plain = E.B;
        Console.WriteLine(""plain "" + (plain is E.B) + (plain switch { E.B => ""b"", _ => ""?"" }));
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- compound assignment on a Nullable<numeric> is lifted and wraps ----

        [TestMethod]
        public async Task NullableCompoundAssignmentIsLifted()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;
public class Box { public int? N; public long? L; }
public class Program {
  static string S(object o) => o == null ? ""null"" : o.ToString();
  static int? Maybe(bool b) { Console.WriteLine(""maybe""); return b ? 2 : null; }
  public static void Main() {
    int? n = null; n += 1; Console.WriteLine(""int+= "" + S(n));
    int? m = null; m *= 3; Console.WriteLine(""int*= "" + S(m));
    int? k = null; k <<= 3; Console.WriteLine(""int<<= "" + S(k));
    double? dd = null; dd += 1.5; Console.WriteLine(""double+= "" + S(dd));
    double? de = 2; de += 1.5; Console.WriteLine(""double+= v "" + S(de));
    int? big = int.MaxValue; big += 1; Console.WriteLine(""int overflow "" + S(big));
    int? mul = 100000; mul *= 100000; Console.WriteLine(""int mul "" + S(mul));
    int? dv = 7; dv /= 2; Console.WriteLine(""int div "" + S(dv));
    uint? u = 0; u -= 1; Console.WriteLine(""uint wrap "" + S(u));
    byte? b = 250; b += 10; Console.WriteLine(""byte wrap "" + S(b));
    long? l = null; l += 1; Console.WriteLine(""long null "" + S(l));
    long? l2 = 5; l2 *= 3000000000L; Console.WriteLine(""long "" + S(l2));
    decimal? dc = null; dc += 1; Console.WriteLine(""dec null "" + S(dc));
    decimal? dc2 = 7; dc2 /= 2; Console.WriteLine(""dec "" + S(dc2));
    int? a = 5; int? nn = null; a += nn; Console.WriteLine(""rhs null "" + S(a));
    int? c = 5; c += Maybe(true); Console.WriteLine(""rhs call "" + S(c));
    int? c2 = 5; c2 += Maybe(false); Console.WriteLine(""rhs call null "" + S(c2));
    var box = new Box(); box.N += 4; box.L -= 2; Console.WriteLine(""field "" + S(box.N) + "" "" + S(box.L));
    box.N = 1; box.N += 4; Console.WriteLine(""field v "" + S(box.N));
    var list = new List<int?> { null, 3 }; list[0] += 1; list[1] += 1; Console.WriteLine(""list "" + S(list[0]) + "" "" + S(list[1]));
    int?[] arr = { null, int.MaxValue }; arr[0] += 1; arr[1] += 1; Console.WriteLine(""arr "" + S(arr[0]) + "" "" + S(arr[1]));
    int? r = 3; var res = (r += 2); Console.WriteLine(""value "" + S(res));
    Console.WriteLine(""<<DONE>>"");
  }
}", waitForOutput: "<<DONE>>");
        }

        // ---- a side-effecting receiver or index of a compound / ++ / ref target runs once ----

        [TestMethod]
        public async Task SideEffectingTargetIsEvaluatedOnce()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
public struct P { public int X; }
public class Box
{
    public int X; public string S = ""a""; public long L; public decimal M; public int? N; public Action A;
    public List<string> List = new List<string> { null, null };
    public P[] Ps = new P[2];
    public int this[int i] { get { Program.Log += ""get"" + i + "";""; return i; } set { Program.Log += ""set"" + i + ""="" + value + "";""; } }
}
public class Program
{
    public static string Log = """";
    static int calls; static Box bx = new Box();
    static Box Get() { calls++; return bx; }
    static int k; static int Next() => k++;
    static async Task<Box> GetAsync() { await Task.Yield(); calls++; return bx; }
    static void Inc(ref int v) { v += 10; }
    static void Set(out long v) { v = 7; }
    public static async Task Main()
    {
        Get().X += 5; Get().S += ""b""; Get().L += 5; Get().M *= 3; Get().N += 1; Get().A += () => { };
        Console.WriteLine(""compound calls="" + calls + "" "" + bx.X + bx.S + bx.L + bx.M + (bx.N == null)); calls = 0;
        var v = (Get().X -= 2); Console.WriteLine(""value "" + v + "" calls="" + calls); calls = 0;
        Get().L++; ++Get().L; var post = Get().L--; var pre = --Get().M;
        Console.WriteLine(""incdec calls="" + calls + "" "" + bx.L + "" "" + post + "" "" + pre); calls = 0;
        long[] la = new long[3]; int i = 0; la[i++]++; la[i++] += 4; ++la[i++];
        Console.WriteLine(""long[] i="" + i + "" "" + string.Join("","", la));
        decimal[] da = new decimal[2]; i = 0; da[i++]++; da[i++] -= 2.5m;
        Console.WriteLine(""decimal[] i="" + i + "" "" + string.Join("","", da));
        bx.List[Next()] ??= ""v""; Get().List[1] ??= ""w"";
        Console.WriteLine(""??= k="" + k + "" calls="" + calls + "" "" + bx.List[0] + "","" + bx.List[1]); calls = 0;
        bx[Next()] += 5; Console.WriteLine(""indexer "" + Log); Log = """";
        bx.Ps[Next() - 2].X += 3; Console.WriteLine(""struct elem k="" + k + "" "" + bx.Ps[0].X + "","" + bx.Ps[1].X); calls = 0;
        (await GetAsync()).L += 2; Console.WriteLine(""await recv calls="" + calls + "" "" + bx.L); calls = 0;
        Get().L += await Task.FromResult(3L); Console.WriteLine(""await rhs calls="" + calls + "" "" + bx.L); calls = 0;
        int[] arr = new int[3]; i = 0;
        Inc(ref arr[i++]); Console.WriteLine(""ref i="" + i + "" "" + string.Join("","", arr));
        Inc(ref Get().X); Console.WriteLine(""ref calls="" + calls + "" "" + bx.X); calls = 0;
        i = 0; Set(out la[i++]); Console.WriteLine(""out i="" + i + "" "" + string.Join("","", la));
      Console.WriteLine(""<<DONE>>"");
  }
}", waitForOutput: "<<DONE>>");
        }
    }
}
