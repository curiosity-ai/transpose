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
    }
}
