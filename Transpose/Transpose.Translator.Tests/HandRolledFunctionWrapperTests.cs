using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Wherever the emitter wraps USER code in a synthesized JS function, that wrapper must be an
    /// ARROW, never a plain <c>function</c>. A plain function gets both properties wrong:
    ///  - it is not async, so an <c>await</c> inside is a bare await in a non-async function — a
    ///    SyntaxError that stops the WHOLE bundle from parsing, not just that expression;
    ///  - it rebinds <c>this</c> to undefined under the bundle's "use strict", so wrapped code that
    ///    reads an instance member throws "Cannot read properties of undefined".
    ///
    /// Two sites hand-rolled such a wrapper and hit both faults:
    ///  - the switch EXPRESSION (<c>k switch { … }</c>) wrapped its arms in
    ///    <c>(function ($sw0) { … })(k)</c>;
    ///  - LINQ QUERY syntax (<c>from x in xs where … select …</c>) wrapped each clause in
    ///    <c>.where(function (x) { return … })</c>.
    /// The iterator sites (<c>function*</c>, which cannot be an arrow) are correct because they
    /// <c>.bind(this)</c>; the is-pattern wrappers are safe because they evaluate the subject outside
    /// the wrapper and emit no user code inside.
    /// </summary>
    [TestClass]
    public class HandRolledFunctionWrapperTests : TranslatorTestBase
    {
        // ---- switch expression -------------------------------------------------

        [TestMethod]
        public async Task SwitchExpressionArmsCanAwaitAsync()
        {
            await RunTest(@"
using System;
using System.Threading.Tasks;

public class Program
{
    static async Task<int> Val(int v) { await Task.Yield(); return v; }

    static async Task Run(int k)
    {
        var v = k switch { 1 => await Val(11), 2 => await Val(22), _ => await Val(99) };
        Console.WriteLine(k + "" -> "" + v);
    }

    public static void Main() { Run(1); Run(2); Run(5); }
}", overrideRoslynCode: @"
using System;
using System.Threading.Tasks;

public class Program
{
    static async Task<int> Val(int v) { await Task.Yield(); return v; }

    static async Task Run(int k)
    {
        var v = k switch { 1 => await Val(11), 2 => await Val(22), _ => await Val(99) };
        Console.WriteLine(k + "" -> "" + v);
    }

    public static void Main() { Run(1).GetAwaiter().GetResult(); Run(2).GetAwaiter().GetResult(); Run(5).GetAwaiter().GetResult(); }
}");
        }

        [TestMethod]
        public async Task SwitchExpressionArmsCanReadInstanceMembersAsync()
        {
            await RunTest(@"
using System;

public class Holder
{
    private int    _min = 2;
    private string _tag = ""T"";

    public string Pick(int k) => k switch { 1 => _tag + ""one"", 2 => _tag + _min, _ => this._tag + ""other"" };
}

public class Program
{
    public static void Main()
    {
        var h = new Holder();
        Console.WriteLine(h.Pick(1) + "" "" + h.Pick(2) + "" "" + h.Pick(3));
    }
}");
        }

        [TestMethod]
        public void SwitchExpressionDoesNotEmitAPlainFunctionWrapper()
        {
            var code = @"
public class Holder
{
    private int _min = 2;
    public int Pick(int k) => k switch { 1 => _min, _ => 0 };
}

public class Program
{
    public static void Main() { }
}";
            var result = new RoslynTranslator().Translate(code);

            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsFalse(result.Javascript!.Contains("function ($sw"),
                "a switch expression must wrap its arms in an arrow, not a plain function (which loses `this`)\n"
                + result.Javascript);
        }

        // ---- LINQ query syntax -------------------------------------------------

        [TestMethod]
        public async Task QueryClauseLambdasCanReadInstanceMembersAsync()
        {
            await RunTest(@"
using System;
using System.Linq;

public class Holder
{
    private int    _min = 2;
    private string _tag = ""T"";

    public string Where(int[] xs)   => string.Join("","", from x in xs where x >= _min select x);
    public string Select(int[] xs)  => string.Join("","", from x in xs select x + _min);
    public string OrderBy(int[] xs) => string.Join("","", from x in xs orderby x * _min descending select x);
    public string Group(int[] xs)   => string.Join("";"", from x in xs group x by (x % _min) into g select g.Key + "":"" + g.Count());
    public string Tagged(int[] xs)  => string.Join("","", from x in xs where x > _min select _tag + x);
}

public class Program
{
    public static void Main()
    {
        var h = new Holder();
        var xs = new[] { 1, 2, 3, 4 };
        Console.WriteLine(h.Where(xs));
        Console.WriteLine(h.Select(xs));
        Console.WriteLine(h.OrderBy(xs));
        Console.WriteLine(h.Group(xs));
        Console.WriteLine(h.Tagged(xs));
    }
}");
        }

        [TestMethod]
        public void QueryClauseLambdasAreArrows()
        {
            var code = @"
using System.Linq;

public class Holder
{
    private int _min = 2;
    public string Run(int[] xs) => string.Join("","", from x in xs where x >= _min orderby x select x + _min);
}

public class Program
{
    public static void Main() { }
}";
            var result = new RoslynTranslator().Translate(code);

            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;

            foreach (var op in new[] { "where", "select", "orderBy" })
            {
                Assert.IsFalse(js.Contains($".{op}(function ("),
                    $"a query `{op}` clause must emit an arrow, not a plain function (which loses `this`)\n" + js);
            }
        }
    }
}
