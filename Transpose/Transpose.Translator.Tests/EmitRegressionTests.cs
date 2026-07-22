using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Regression tests for three emit bugs found compiling the Curiosity FrontEnd:
    ///  - an out/ref call whose argument contains an `await` (e.g. bool.TryParse(await t, out var s))
    ///    must run its holder IIFE as an `async` arrow and be awaited — a bare `await` inside a plain
    ///    arrow is a syntax error ("Unexpected identifier 'Transpose'");
    ///  - a generic method threading its type argument (WithBody&lt;T&gt;(T)) called with an anonymous
    ///    type must pass System.Object for T, not an empty slot (which produced `WithBody$1(, {...})`);
    ///  - an iterator LOCAL FUNCTION (a nested `IEnumerable&lt;T&gt;` with `yield return`) must compile
    ///    to a `function*` generator like an iterator method, not a plain arrow with a bare `yield`
    ///    ("Unexpected strict mode reserved word").
    /// </summary>
    [TestClass]
    public class EmitRegressionTests : TranslatorTestBase
    {
        // ---- await inside an out/ref call --------------------------------------

        [TestMethod]
        public async Task TryParseWithAwaitedArgumentRunsAsync()
        {
            await RunTest(@"
using System;
using System.Threading.Tasks;
public class Program
{
    static async Task Run()
    {
        if (int.TryParse(await Task.FromResult(""42""), out var n))
            Console.WriteLine(""parsed:"" + n);
        else
            Console.WriteLine(""failed"");
        Console.WriteLine(""<<DONE>>"");
    }
    public static void Main() { Run(); }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public void AwaitInsideOutCallEmitsAsyncIife()
        {
            var code = @"
using System;
using System.Threading.Tasks;
public class Program
{
    static async Task Run()
    {
        if (bool.TryParse(await Task.FromResult(""true""), out var b)) Console.WriteLine(b);
    }
    public static void Main() { }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsTrue(result.Javascript!.Contains("await (async () => {"),
                "an out/ref call with an awaited argument should run its IIFE as an awaited async arrow\n" + result.Javascript);
        }

        // ---- generic method threading + anonymous type -------------------------

        [TestMethod]
        public async Task GenericMethodWithAnonymousTypeArgumentRunsAsync()
        {
            await RunTest(@"
using System;
public class Box
{
    public object Held;
    public Box Put<T>(T item) where T : class { Held = item; return this; }
}
public class Program
{
    public static void Main()
    {
        var b = new Box().Put(new { Name = ""x"", Age = 5 });
        Console.WriteLine(b.Held != null ? ""held"" : ""null"");
    }
}");
        }

        [TestMethod]
        public void GenericTypeArgumentForAnonymousTypeIsObject()
        {
            var code = @"
using System;
public class Box
{
    public Box Put<T>(T item) where T : class => this;
}
public class Program
{
    public static void Main() { new Box().Put(new { Name = ""x"" }); }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsTrue(result.Javascript!.Contains("Put(System.Object,"),
                "a generic call with an anonymous-type argument should thread System.Object as the type arg\n" + result.Javascript);
            Assert.IsFalse(result.Javascript!.Contains("Put(,"),
                "the threaded type argument must never be emitted as an empty slot\n" + result.Javascript);
        }

        // ---- iterator local function -------------------------------------------

        [TestMethod]
        public async Task IteratorLocalFunctionRunsAsync()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;
public class Program
{
    static IEnumerable<string> Build()
    {
        IEnumerable<int> Nums()
        {
            for (int i = 0; i < 3; i++) yield return i * 10;
        }
        foreach (var n in Nums()) yield return ""v"" + n;
    }
    public static void Main()
    {
        foreach (var s in Build()) Console.WriteLine(s);
    }
}");
        }

        [TestMethod]
        public void IteratorLocalFunctionEmitsGenerator()
        {
            var code = @"
using System;
using System.Collections.Generic;
public class Program
{
    static IEnumerable<int> Outer()
    {
        IEnumerable<int> Inner() { yield return 1; yield return 2; }
        foreach (var x in Inner()) yield return x;
    }
    public static void Main() { foreach (var v in Outer()) Console.WriteLine(v); }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            // The local function must wrap its body in a generator, not emit a bare `yield` in an arrow.
            Assert.IsTrue(result.Javascript!.Contains("Inner = () =>")
                          && result.Javascript!.Contains("TransposeR.iter((function* ()"),
                "an iterator local function should compile to a TransposeR.iter(function*(){...}) generator\n" + result.Javascript);
        }
    }
}
