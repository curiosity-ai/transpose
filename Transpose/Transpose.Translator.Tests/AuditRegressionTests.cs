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
    }
}
