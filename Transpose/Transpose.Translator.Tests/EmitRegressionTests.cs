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

        // ---- compound assignment to a collection indexer ----------------------

        [TestMethod]
        public async Task CompoundAssignmentToDictionaryIndexerRunsAsync()
        {
            // `d[k] += v` on an int dictionary must route through setItem — emitting the getItem read as
            // the write target (`d.getItem(k) = …`) is an "assignment to rvalue" (invalid JS).
            await RunTest(@"
using System;
using System.Collections.Generic;
public class Program
{
    public static void Main()
    {
        var d = new Dictionary<string,int>();
        d[""a""] = 1;
        d[""a""] += 5;
        d[""a""] *= 3;
        d[""a""] -= 2;
        Console.WriteLine(d[""a""]);            // (1+5)*3-2 = 16

        var s = new Dictionary<string,string>();
        s[""x""] = ""a"";
        s[""x""] += ""b"";
        Console.WriteLine(s[""x""]);            // ab

        var list = new List<int> { 10, 20 };
        list[0] += 5;
        Console.WriteLine(list[0]);            // 15
    }
}");
        }

        [TestMethod]
        public void CompoundAssignmentToDictionaryIndexerEmitsSetItem()
        {
            var code = @"
using System;
using System.Collections.Generic;
public class Program
{
    public static void Main()
    {
        var d = new Dictionary<string,int>();
        d[""a""] += 5;
    }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsTrue(result.Javascript!.Contains(".setItem(\"a\", Transpose.Int.clip32("),
                "d[k] += v should store through setItem with the clipped new value\n" + result.Javascript);
            Assert.IsFalse(result.Javascript!.Contains(".getItem(\"a\") ="),
                "the write target must never be an indexer getItem read (assignment to rvalue)\n" + result.Javascript);
        }

        [TestMethod]
        public async Task CompoundAssignmentToTemplateSetterPropertyRunsAsync()
        {
            // `sb.Length -= 2` targets a [Template]-setter property (getLength/setLength); the write must
            // go through setLength, not emit `sb.getLength() = …` (assignment to rvalue).
            await RunTest(@"
using System;
using System.Text;
public class Program
{
    public static void Main()
    {
        var sb = new StringBuilder();
        sb.Append(""hello world"");
        sb.Length -= 6;
        Console.WriteLine(sb.ToString());   // hello
        sb.Length = 3;
        Console.WriteLine(sb.ToString());   // hel
    }
}");
        }

        // ---- typeof(open generic) ---------------------------------------------

        [TestMethod]
        public async Task TypeOfOpenGenericComparesAgainstGenericTypeDefinitionAsync()
        {
            // typeof(IObs<>) is an UNBOUND generic — it must emit the type definition (IObs$1), not
            // IObs$1(T) which references an undefined `T` (ReferenceError at runtime). This is exactly
            // Tesserae's PossibleObservableHelpers.IsObservable pattern (ObservableDictionary<,> ctor).
            await RunTest(@"
using System;
public interface IObs<T> { T Value { get; } }
public class Box<T> : IObs<T> { public T Value { get; set; } }
public class Program
{
    static bool IsObs(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IObs<>)) return true;
        foreach (var i in type.GetInterfaces())
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IObs<>)) return true;
        return false;
    }
    public static void Main()
    {
        Console.WriteLine(IsObs(typeof(IObs<string>)));   // True
        Console.WriteLine(IsObs(typeof(Box<int>)));        // True (implements IObs<int>)
        Console.WriteLine(IsObs(typeof(string)));          // False
    }
}");
        }

        [TestMethod]
        public void TypeOfOpenGenericEmitsDefinitionWithoutArgs()
        {
            var code = @"
using System;
public interface IObs<T> { T Value { get; } }
public class Program
{
    static bool Check(Type t) => t.GetGenericTypeDefinition() == typeof(IObs<>);
    public static void Main() { Console.WriteLine(Check(typeof(IObs<int>))); }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsTrue(result.Javascript!.Contains("=== IObs$1;"),
                "typeof(IObs<>) should emit the definition IObs$1, not IObs$1(T)\n" + result.Javascript);
            Assert.IsFalse(result.Javascript!.Contains("IObs$1(T)"),
                "typeof(IObs<>) must not apply the unbound type parameter as an argument\n" + result.Javascript);
        }

        // ---- interpolated string -> FormattableString -------------------------

        [TestMethod]
        public async Task InterpolatedStringConvertedToFormattableStringRunsAsync()
        {
            // `$"…"` converted to FormattableString must become FormattableStringFactory.Create(...),
            // carrying Format / GetArguments() — not a plain concatenated string (whose .GetArguments
            // "is not a function"). Compare only culture-invariant surface (Format, count, raw args).
            await RunTest(@"
using System;
public class Program
{
    static string Describe(FormattableString fs)
        => fs.Format + "" :: "" + fs.ArgumentCount + "" :: "" + string.Join("","", Array.ConvertAll(fs.GetArguments(), x => x == null ? ""null"" : x.ToString()));
    public static void Main()
    {
        int a = 7, b = 9;
        FormattableString fs = $""X {a} Y {b} Z"";
        Console.WriteLine(Describe(fs));
        Console.WriteLine(Describe($""just {a}""));
        Console.WriteLine(Describe($""none""));
        Console.WriteLine(Describe($""fmt {a:D3} and brace {{lit}}""));
    }
}");
        }

        [TestMethod]
        public void InterpolatedStringToFormattableStringEmitsFactory()
        {
            var code = @"
using System;
public class Program
{
    static void Use(FormattableString fs) { }
    public static void Main() { int a = 1, b = 2; Use($""Hello {a} and {b}""); }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsTrue(result.Javascript!.Contains("FormattableStringFactory.Create(\"Hello {0} and {1}\", ["),
                "an interpolated string converted to FormattableString should build a factory call\n" + result.Javascript);
        }

        [TestMethod]
        public void InterpolatedStringToStringStaysConcatenation()
        {
            // The common case — target type string — must remain plain concatenation, not a factory call.
            var code = @"
using System;
public class Program
{
    public static void Main() { int a = 1; string s = $""v={a}""; Console.WriteLine(s); }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsFalse(result.Javascript!.Contains("FormattableStringFactory"),
                "a string-typed interpolation must not use the FormattableString factory\n" + result.Javascript);
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
