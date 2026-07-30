using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Regression tests for emit bugs found compiling the Curiosity FrontEnd and Mosaik:
    ///  - an expression IIFE that wraps an `await` must be an `async` arrow and be awaited — a bare
    ///    `await` inside a plain arrow is a syntax error ("Unexpected identifier 'Transpose'") that
    ///    breaks the whole bundle, not just that call. This applies to every wrapper the emitter
    ///    produces (out/ref holders, object initializers, reordered named arguments, concrete
    ///    collections, throw expressions), and to an `await` anywhere inside them — including in a
    ///    call's RECEIVER, e.g. `(await Query()).Nodes.TryGetFirst(out var n)`;
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

        // ---- await anywhere inside an expression IIFE --------------------------
        //
        // The emitter wraps a C# expression in an IIFE wherever emitting it needs statements: out/ref
        // holders, an object initializer, argument temporaries for reordered named arguments, building a
        // concrete collection, a throw expression. Every one of those has to become an `async` arrow —
        // and be awaited — once the syntax it wraps contains an `await`, because a bare `await` inside a
        // plain arrow is a *syntax* error, so the whole bundle fails to parse rather than just that call.
        //
        // Originally only an awaited *argument* of an out/ref call was handled, which left every other
        // wrapper (and even the same call awaiting in its RECEIVER, the shape reported from Mosaik:
        // `(await Query()).Nodes.TryGetFirst(out var n)`) emitting unparsable JavaScript.

        [TestMethod]
        public async Task AwaitInTheReceiverOfAnOutCallRunsAsync()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
public sealed class Box { public List<string> Nodes = new List<string>(); }
public static class Ext
{
    public static bool TryGetFirst<T>(this IEnumerable<T> src, out T value)
    {
        foreach (var v in src) { value = v; return true; }
        value = default(T);
        return false;
    }
}
public class Program
{
    static Task<Box> QueryAsync()
    {
        var b = new Box();
        b.Nodes.Add(""first"");
        return Task.FromResult(b);
    }
    static async Task Run()
    {
        // The await is in the receiver, not in an argument — it still lands inside the holder IIFE.
        if ((await QueryAsync()).Nodes.TryGetFirst(out var n)) Console.WriteLine(""got:"" + n);
        else Console.WriteLine(""none"");
    }
    public static void Main() { Run(); }
}");
        }

        [TestMethod]
        public async Task AwaitInReorderedNamedArgumentsRunsAsync()
        {
            await RunTest(@"
using System;
using System.Threading.Tasks;
public class Program
{
    static string Join(string a = ""a"", string b = ""b"", string c = ""c"") => a + ""|"" + b + ""|"" + c;
    static Task<string> TextAsync(string s) => Task.FromResult(s);
    static async Task Run()
    {
        // Named arguments out of parameter order are evaluated into temps inside an IIFE to keep C#'s
        // source-order evaluation; the awaits go with them.
        Console.WriteLine(Join(c: await TextAsync(""C""), a: await TextAsync(""A"")));
    }
    public static void Main() { Run(); }
}");
        }

        [TestMethod]
        public async Task AwaitInAnObjectInitializerRunsAsync()
        {
            await RunTest(@"
using System;
using System.Threading.Tasks;
public sealed class Bag
{
    public string Name;
    public int Count;
    public Bag() { }
    public Bag(string seed) { Name = seed; }
}
public class Program
{
    static Task<string> TextAsync(string s) => Task.FromResult(s);
    static Task<int> NumAsync(int i) => Task.FromResult(i);
    static async Task Run()
    {
        var bag = new Bag(await TextAsync(""seed"")) { Count = await NumAsync(7) };
        Console.WriteLine(bag.Name + "":"" + bag.Count);
    }
    public static void Main() { Run(); }
}");
        }

        [TestMethod]
        public async Task AwaitInACollectionExpressionRunsAsync()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
public class Program
{
    static Task<int> NumAsync(int i) => Task.FromResult(i);
    static async Task Run()
    {
        // A concrete collection target is built and filled inside an IIFE.
        List<int> xs = [await NumAsync(1), 2, await NumAsync(3)];
        var total = 0;
        foreach (var x in xs) total += x;
        Console.WriteLine(""total:"" + total);
    }
    public static void Main() { Run(); }
}");
        }

        [TestMethod]
        public async Task AwaitInAParamsCollectionRunsAsync()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
public class Program
{
    static int Sum(params List<int> xs) { var t = 0; foreach (var x in xs) t += x; return t; }
    static Task<int> NumAsync(int i) => Task.FromResult(i);
    static async Task Run()
    {
        Console.WriteLine(""sum:"" + Sum(await NumAsync(4), 5, await NumAsync(6)));
    }
    public static void Main() { Run(); }
}");
        }

        [TestMethod]
        public async Task AwaitInAThrowExpressionRunsAsync()
        {
            await RunTest(@"
using System;
using System.Threading.Tasks;
public class Program
{
    static Task<string> TextAsync(string s) => Task.FromResult(s);
    static async Task Run()
    {
        string missing = null;
        try { Console.WriteLine(missing ?? throw new InvalidOperationException(await TextAsync(""boom""))); }
        catch (InvalidOperationException e) { Console.WriteLine(""caught:"" + e.Message); }
    }
    public static void Main() { Run(); }
}");
        }

        /// <summary>
        /// The invariant behind all of the above, checked on the emitted JavaScript so it also catches a
        /// *new* IIFE site added later without the async form: no plain <c>(() =&gt; {</c> wrapper may
        /// contain an <c>await</c>. Each wrapper is emitted on a single line, so scanning per line
        /// finds the whole wrapper.
        /// </summary>
        [TestMethod]
        public void NoPlainArrowIifeWrapsAnAwait()
        {
            var code = @"
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
public sealed class Bag { public string Name; public int Count; public Bag(string s) { Name = s; } }
public sealed class Box { public List<string> Nodes = new List<string>(); }
public static class Ext
{
    public static bool TryGetFirst<T>(this IEnumerable<T> src, out T value)
    {
        foreach (var v in src) { value = v; return true; }
        value = default(T); return false;
    }
}
public class Program
{
    static string Join(string a = ""a"", string b = ""b"", string c = ""c"") => a + b + c;
    static int Sum(params List<int> xs) => xs.Count;
    static Task<string> TextAsync() => Task.FromResult(""x"");
    static Task<int> NumAsync() => Task.FromResult(1);
    static Task<Box> BoxAsync() => Task.FromResult(new Box());
    static async Task Run()
    {
        if ((await BoxAsync()).Nodes.TryGetFirst(out var n)) Console.WriteLine(n);
        if (int.TryParse(await TextAsync(), out var p)) Console.WriteLine(p);
        Console.WriteLine(Join(c: await TextAsync(), a: await TextAsync()));
        Console.WriteLine(new Bag(await TextAsync()) { Count = await NumAsync() }.Name);
        List<int> xs = [await NumAsync(), 2];
        Console.WriteLine(Sum(await NumAsync(), 3));
        string missing = null;
        Console.WriteLine(missing ?? throw new InvalidOperationException(await TextAsync()));
    }
    public static void Main() { }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");

            foreach (var line in result.Javascript!.Split('\n'))
            {
                var at = line.IndexOf("(() => {", System.StringComparison.Ordinal);
                if (at < 0) continue;
                Assert.IsFalse(line[at..].Contains("await", System.StringComparison.Ordinal),
                    "a plain (non-async) arrow IIFE must never wrap an await — that is a JavaScript "
                    + "syntax error that breaks the whole bundle:\n" + line.Trim());
            }
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

        // ---- brace escaping in interpolated strings ---------------------------

        [TestMethod]
        public async Task InterpolatedStringUnescapesDoubledBracesAsync()
        {
            // `{{` / `}}` inside `$"…"` are escapes for a single literal brace. The string target
            // concatenates the text segments directly, so the emitter must collapse them itself —
            // nothing downstream runs a composite-format parser that would. Regression: every case
            // below used to keep the braces doubled (`$"{{x}}"` -> "{{x}}" instead of "{x}").
            await RunTest("""
using System;
public class Program
{
    const string C = $"const {{a}} b";
    enum E { Red }
    public static void Main()
    {
        var n = "W";
        var ch = 'c';
        Console.WriteLine($"{{{n}}}");
        Console.WriteLine($"a {{ b }} c");
        Console.WriteLine($"{{literal}} and {n}");
        Console.WriteLine($@"verbatim {{x}} {n}");
        Console.WriteLine("plain {{ not escaped }}");
        Console.WriteLine($"json: {{ \"k\": {1 + 1} }}");
        Console.WriteLine($"align {{a}} {n,8} {{b}}");
        Console.WriteLine($"fmtclause {{a}} {42:X} {{b}}");
        Console.WriteLine($"char {{a}} {ch} {{b}}");
        Console.WriteLine($"enum {{a}} {E.Red} {{b}}");
        Console.WriteLine($"{{}}");
        Console.WriteLine($"{{{{}}}}");
        Console.WriteLine($"{{{{{n}}}}}");
        Console.WriteLine($"trailing {n}{{");
        Console.WriteLine($"}}leading {n}");
        Console.WriteLine($"nested {$"inner {{x}} {n}"} {{y}}");
        Console.WriteLine(C);
    }
}
""");
        }

        [TestMethod]
        public async Task RawInterpolatedStringKeepsLiteralBracesAsync()
        {
            // The opposite rule: a raw interpolated string has NO brace-doubling escape — the `$` count
            // decides how many braces open an interpolation and shorter runs are literal text. So the
            // text must NOT be unescaped, and when such a string becomes a FormattableString its literal
            // braces have to be doubled to form a valid composite format string (a raw `{0}` would
            // otherwise be misread as a placeholder).
            await RunTest("""""
using System;
public class Program
{
    public static void Main()
    {
        var n = "W";
        Console.WriteLine($$"""raw {a} {{n}} }""");
        Console.WriteLine($$$""""raw3 {a} {{b}} {{{n}}}"""");
        FormattableString f = $$"""fsraw {0} {{n}} {b}""";
        Console.WriteLine(f.Format);
        Console.WriteLine(f.ToString());
    }
}
""""");
        }

        [TestMethod]
        public void InterpolatedStringEmitsSingleBraceForDoubledBrace()
        {
            var code = """
using System;
public class Program
{
    public static void Main() { int a = 1; Console.WriteLine($"{{x}}={a}"); }
}
""";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsTrue(result.Javascript!.Contains("\"{x}=\""),
                "$\"{{x}}={a}\" should emit the literal text \"{x}=\"\n" + result.Javascript);
            Assert.IsFalse(result.Javascript!.Contains("{{x}}"),
                "the doubled braces must not survive into the emitted string\n" + result.Javascript);
        }

        [TestMethod]
        public void FormattableStringKeepsDoubledBracesInCompositeFormat()
        {
            // Inverse guard for the non-raw FormattableString path: there the composite format string
            // is what gets emitted, and it *keeps* the doubling (matching FormattableString.Format).
            var code = """
using System;
public class Program
{
    static void Use(FormattableString fs) { }
    public static void Main() { int a = 1; Use($"lit {{x}} {a}"); }
}
""";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsTrue(result.Javascript!.Contains("FormattableStringFactory.Create(\"lit {{x}} {0}\", ["),
                "the composite format string must keep the doubled braces\n" + result.Javascript);
        }

        // ---- null string concatenation ----------------------------------------

        [TestMethod]
        public async Task NullStringConcatenationTreatsNullAsEmptyAsync()
        {
            // C# string `+` treats a null operand as "" (`null + "x"` is "x"); JS `+` renders null as
            // "null". Every string-typed operand that can be null must be coerced.
            await RunTest(@"
using System;
public class Program
{
    static string S(string x) => x; // opaque, may be null
    public static void Main()
    {
        string a = null, b = ""X"";
        Console.WriteLine(a + ""Hello World"");
        Console.WriteLine(""P"" + a);
        Console.WriteLine(a + b + ""Y"");
        Console.WriteLine(a + a);
        Console.WriteLine(S(null) + ""Z"" + S(null));
        Console.WriteLine((a + b).Length);          // 1: null becomes empty, then concat X
        int n = 5;
        Console.WriteLine(a + n);                    // int operand via toStr, null becomes empty
    }
}");
        }

        [TestMethod]
        public void NullStringOperandGetsCoalesceButLiteralsDoNot()
        {
            var code = @"
using System;
public class Program
{
    public static void Main()
    {
        string a = null;
        Console.WriteLine(a + ""Hello World"");
    }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsTrue(result.Javascript!.Contains("(a ?? \"\") + \"Hello World\""),
                "a nullable string operand should be coerced with `?? \"\"`, the literal left as-is\n" + result.Javascript);
        }

        // ---- guarded async switch referencing an enclosing parameter ----------

        [TestMethod]
        public async Task GuardedAsyncSwitchAccessesEnclosingParameterAsync()
        {
            // A `when`-guarded switch inside an async method (Request.DoRequestAsync's shape) compiles to
            // a labeled block inside the async arrow; a parameter (responseRetriever) and cases that
            // declare locals and await must all resolve against the enclosing scope.
            await RunTest(@"
using System;
using System.Threading.Tasks;
public class Program
{
    static bool auth = true, ready = true;
    static async Task<string> Handle(int status, Func<Task<string>> retriever)
    {
        switch (status)
        {
            case 200:
            case 201:
                var r = await retriever();
                return ""ok:"" + r;
            case 403 when auth:
                return ""auth:"" + await retriever();
            case 503 when ready && status == 503:
                return ""ready:"" + await retriever();
            case 503:
                return ""busy:"" + await retriever();
            default:
                var d = await retriever();
                return ""def:"" + d;
        }
    }
    static async Task RunAll()
    {
        Console.WriteLine(await Handle(200, () => Task.FromResult(""A"")));
        Console.WriteLine(await Handle(403, () => Task.FromResult(""B"")));
        Console.WriteLine(await Handle(503, () => Task.FromResult(""C"")));
        Console.WriteLine(await Handle(999, () => Task.FromResult(""D"")));
        Console.WriteLine(""<<DONE>>"");
    }
    public static void Main() { RunAll(); }
}", waitForOutput: "<<DONE>>");
        }

        // ---- named argument followed by a trailing positional argument --------

        [TestMethod]
        public async Task NamedArgumentFollowedByPositionalRunsAsync()
        {
            // Do(type, accept, retriever: fn, flag): the trailing positional `flag` must land in its own
            // slot, not overwrite the `retriever` slot a named argument already claimed. This is
            // Request.TryDoRequestAsync -> DoRequestAsync (generic, so a type arg is threaded first),
            // where the bug slid `treatHttpResponseAsData` (false) into the responseRetriever slot.
            await RunTest(@"
using System;
public class C
{
    public string Do<T>(string type, string accept, Func<int, T> retriever, bool flag = false)
        => type + ""|"" + accept + ""|"" + retriever(flag ? 1 : 0) + ""|"" + flag;
    public string Try<T>(string type, string accept, Func<int, T> retriever, bool flag)
        => Do<T>(type, accept, retriever: x => retriever(x), flag);
}
public class Program
{
    public static void Main()
    {
        var c = new C();
        Console.WriteLine(c.Try<string>(""GET"", ""json"", n => ""R"" + n, true));
        Console.WriteLine(c.Try<string>(""POST"", ""bin"", n => ""R"" + n, false));
    }
}");
        }

        [TestMethod]
        public void NamedArgThenPositionalKeepsBothArguments()
        {
            var code = @"
using System;
public class C
{
    public T Do<T>(string type, string accept, Func<int,T> retriever, bool flag = false) => retriever(0);
    public T Try<T>(string type, string accept, Func<int,T> retriever, bool flag)
        => Do<T>(type, accept, retriever: x => retriever(x), flag);
}
public class Program { public static void Main() { } }
";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            // Both the named retriever lambda and the trailing positional flag must be present, in order.
            Assert.IsTrue(result.Javascript!.Contains("}, flag)"),
                "the trailing positional argument must follow the named-argument lambda, not replace it\n" + result.Javascript);
        }

        // ---- single element to a params parameter via a NAMED argument --------

        [TestMethod]
        public async Task NamedParamsArgumentWrapsSingleElementAsync()
        {
            // `new Nav(id, commands: singleCmd)` — a single element given to a `params` parameter BY
            // NAME must be wrapped into the array (C# semantics), so a later `foreach` over it works.
            // Without the fix the callee received a bare element and foreach threw "Cannot create
            // Enumerator" (Tesserae's SidebarNav RSS_NAV: `commands: new SidebarCommand(...)`).
            await RunTest(@"
using System;
public class Cmd { public int V; public Cmd(int v){V=v;} }
public class Nav
{
    private Cmd[] _c;
    public Nav(string id, params Cmd[] c) { _c = c; }
    public int Sum() { int n=0; foreach (var x in _c) n+=x.V; return n; }
}
public class Program
{
    public static void Main()
    {
        Console.WriteLine(new Nav(""a"", c: new Cmd(5)).Sum());                  // 5  (named single -> [cmd])
        Console.WriteLine(new Nav(""b"", new Cmd(7)).Sum());                     // 7  (positional single)
        Console.WriteLine(new Nav(""c"", c: new Cmd[] { new Cmd(2), new Cmd(3) }).Sum()); // 5 (named array passthrough)
        Console.WriteLine(new Nav(""d"").Sum());                                  // 0  (omitted -> [])
    }
}");
        }

        [TestMethod]
        public void NamedParamsSingleElementEmitsWrappedArray()
        {
            var code = @"
using System;
public class Cmd { public Cmd(int v){} }
public class Nav { public Nav(string id, params Cmd[] c) { } }
public class Program { public static void Main() { var n = new Nav(""x"", c: new Cmd(5)); } }
";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsTrue(result.Javascript!.Contains("[new Cmd(5)]"),
                "a single element passed to a params parameter by name should be wrapped in an array\n" + result.Javascript);
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

        // ---- interface Keys/Values dispatch on Dictionary ----------------------
        // Accessing a BCL interface property (IReadOnlyDictionary/IDictionary .Keys/.Values) through
        // the interface emits the member's plain camelCase name (`d.values`). On Dictionary that name
        // collided with the private backing field `values` (lazily null), so `.Values` returned null
        // and `.Values.ToArray()` threw "Cannot read properties of null". The colliding field now
        // yields its slot (renamed `values$1`) and the interface getter is aliased onto `values`.

        [TestMethod]
        public async Task ReadOnlyDictionaryValuesAndKeysDispatchToGetterRunsAsync()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;
using System.Linq;
public class Program
{
    static void Dump(IReadOnlyDictionary<string,int> ro, IDictionary<string,int> id)
    {
        Console.WriteLine(""ro.Values: "" + string.Join("","", ro.Values.OrderBy(x => x)));
        Console.WriteLine(""ro.Keys: ""   + string.Join("","", ro.Keys.OrderBy(x => x)));
        Console.WriteLine(""id.Values: "" + string.Join("","", id.Values.OrderBy(x => x)));
        Console.WriteLine(""id.Keys: ""   + string.Join("","", id.Keys.OrderBy(x => x)));
    }
    public static void Main()
    {
        var d = new Dictionary<string,int> { [""a""] = 10, [""b""] = 20 };
        Dump(d, d);
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public void DictionaryBackingFieldYieldsInterfaceSlot()
        {
            var code = @"
using System.Collections.Generic;
using System.Linq;
public class Program
{
    public static int[] Run(IReadOnlyDictionary<string,int> d) => d.Values.ToArray();
    public static void Main() { }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            // The interface access must reach the getter via the plain camelCase slot, not a field.
            Assert.IsTrue(result.Javascript!.Contains("d.values"),
                "IReadOnlyDictionary<,>.Values must be accessed through the plain 'values' slot\n" + result.Javascript);
        }

        // ---- is-pattern variable in an expression-bodied property --------------
        // An expression-bodied property getter/setter (`=> _w is WebSocket ws && ws.readyState...`)
        // must predeclare the is-pattern / out-var it introduces, exactly like an expression-bodied
        // method. Without it the pattern variable was assigned but never declared, so reading it threw
        // "ws is not defined".

        [TestMethod]
        public async Task IsPatternVariableInExpressionBodiedPropertyRunsAsync()
        {
            await RunTest(@"
using System;
public class Program
{
    static object _w = ""hello"";
    static bool Ok => _w is null || (_w is string s && (s.Length == 0 || s.Length == 5));
    public static void Main()
    {
        Console.WriteLine(Ok);             // True  (len 5)
        _w = ""hi""; Console.WriteLine(Ok); // False (len 2)
        _w = null;  Console.WriteLine(Ok); // True  (null)
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public void ExpressionBodiedPropertyPredeclaresPatternVariable()
        {
            var code = @"
public class Program
{
    static object _w = null;
    static bool Ok => _w is string s && s.Length > 0;
    public static void Main() { }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            // Predeclared block-scoped with `let` (matching regular locals), so sibling-scope
            // same-named locals stay distinct and closures capture the right binding.
            Assert.IsTrue(result.Javascript!.Contains("let s;"),
                "an is-pattern variable in an expression-bodied property must be predeclared\n" + result.Javascript);
        }

        // ---- [Transpose.Ready] static method --------------------------------------
        // A [Ready] static method must be scheduled via Transpose.ready so it runs on load. It was
        // dropped entirely, so e.g. the admin package's AdminBridgeInitializer.Initialize never ran
        // and no admin routes were registered ("Admin package not loaded.").

        [TestMethod]
        public async Task ReadyAttributeMethodRunsOnLoadAsync()
        {
            // [Ready] is a Transpose-only concept (a no-op in native .NET), so run JS-only and assert
            // the scheduled method actually executed.
            var output = await RunTest(@"
using System;
static class Init
{
    [Transpose.Ready]
    public static void Setup() { Console.WriteLine(""ready-ran""); }
}
public class Program { public static void Main() { Console.WriteLine(""main-ran""); } }
", skipRoslyn: true);
            Assert.IsTrue(output.Contains("ready-ran"),
                "the [Ready] static method should run on load\n" + output);
            Assert.IsTrue(output.Contains("main-ran"), "the entry point should still run\n" + output);
        }

        [TestMethod]
        public void ReadyAttributeEmitsTransposeReadyCall()
        {
            var code = @"
static class Init
{
    [Transpose.Ready]
    public static void Setup() { }
}
public class Program { public static void Main() { } }
";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsTrue(result.Javascript!.Contains("Transpose.ready(Init.Setup, Init);"),
                "a [Ready] static method must be scheduled with Transpose.ready\n" + result.Javascript);
        }

        // ---- constructor reflection metadata "sn" ------------------------------
        // A constructor's metadata "sn" (the JS slot reflection invokes via type[sn]) must equal the
        // name the class definition emits for that ctor (CtorName: "ctor"/"$ctorN"). It used to be
        // MemberJsName ("ctor$N", different numbering), so reflection-based construction of a
        // non-primary overload (e.g. Newtonsoft picking a [JsonConstructor]) hit an undefined member
        // and failed with "$$initCtor of undefined".

        [TestMethod]
        public async Task ReflectionConstructsNonPrimaryOverloadRunsAsync()
        {
            await RunTest(@"
using System;
public class Multi
{
    public string V;
    public Multi(int a) { V = ""one:"" + a; }
    public Multi(int a, int b) { V = ""two:"" + (a + b); }
    public Multi(int a, int b, int c) { V = ""three:"" + (a + b + c); }
}
public class Program
{
    public static void Main()
    {
        var obj = (Multi)Activator.CreateInstance(typeof(Multi), 1, 2, 3);
        Console.WriteLine(obj.V);
    }
}", waitForOutput: "three:6");
        }

        [TestMethod]
        public void ConstructorMetadataSnMatchesEmittedCtorName()
        {
            var code = @"
public class Multi
{
    public Multi(int a) { }
    public Multi(int a, int b) { }
}
public class Program { public static void Main() { } }
";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            // The class emits the second ctor as "$ctor1"; metadata "sn" must match (not "ctor$1").
            Assert.IsTrue(result.Javascript!.Contains("\"sn\":\"$ctor1\""),
                "constructor metadata sn must use the emitted CtorName ($ctorN)\n" + result.Javascript);
            Assert.IsFalse(result.Javascript!.Contains("\"sn\":\"ctor$1\""),
                "constructor metadata sn must not use the MemberJsName scheme (ctor$N)\n" + result.Javascript);
        }

        // ---- out-var in an `else if` condition captured by a lambda ------------
        // An out-var / pattern var declared in an `else if` condition is scoped to the enclosing
        // block, but the emitter skipped the nested (inline `else if`) statement when predeclaring,
        // so a later capture (e.g. a lambda in that branch) referenced an undeclared variable
        // ("<name> is not defined"). Mirrors NodeRenderer.GetFor.

        [TestMethod]
        public async Task OutVarInElseIfCapturedByLambdaRunsAsync()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;
public class Program
{
    static readonly Dictionary<string,int> A = new Dictionary<string,int>();
    static readonly Dictionary<string,string> B = new Dictionary<string,string> { [""Person""] = ""KEY"" };
    static string GetFor(string nodeType)
    {
        if (A.TryGetValue(nodeType, out var a)) { return ""a:"" + a; }
        else if (B.TryGetValue(nodeType, out var schema))
        {
            Func<string> missing = () => ""renderer:"" + schema;
            return missing();
        }
        else if (nodeType is string s && s.Length > 3)
        {
            Func<string> f = () => ""len:"" + s.Length;
            return f();
        }
        else { return ""none""; }
    }
    public static void Main()
    {
        Console.WriteLine(GetFor(""Person""));  // renderer:KEY
        Console.WriteLine(GetFor(""XYZ""));     // none (len 3, not >3, and not in B)
        Console.WriteLine(GetFor(""LongName"")); // renderer? no -> len:8
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- default value of a non-primitive struct ---------------------------
        // An uninitialized non-primitive struct field (DateTime, Guid, user struct) and a
        // `default(T)` expression must yield the zeroed struct (C# default(T)), not null. They were
        // emitted as null — so an uninitialized DateTime field compared/used threw
        // "Cannot read properties of null (reading 'getTime')". Now assigned via
        // Transpose.getDefaultValue in the ctor (fields) and default expressions.

        [TestMethod]
        public async Task DefaultStructFieldAndExpressionRunsAsync()
        {
            await RunTest(@"
using System;
public struct Point { public int X; public int Y; }
public class Holder { public DateTime When; public Guid Id; public Point P; public string Name = ""x""; }
public class Program
{
    public static void Main()
    {
        var h = new Holder();
        var h2 = new Holder();
        Console.WriteLine(h.When == DateTime.MinValue);   // True
        Console.WriteLine(h.When.Equals(h2.When));         // True (both default)
        Console.WriteLine((int)DateTime.SpecifyKind(h.When, DateTimeKind.Utc).Kind); // 1 (no crash on default)
        Console.WriteLine(h.Id == Guid.Empty);             // True
        Console.WriteLine(h.P.X + "","" + h.P.Y);            // 0,0
        DateTime d = default(DateTime);
        Console.WriteLine(d == DateTime.MinValue);         // True
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- attribute constructed through the correct ctor overload -----------
        // Reflection metadata (`at:[...]`) constructed every attribute with a bare `new T(args)`,
        // which invokes the PRIMARY ctor ("ctor") and silently drops the arguments when the attribute
        // was applied through a non-primary overload ($ctorN). This broke, e.g., [JsonProperty("x")]
        // (PropertyName lost -> wrong JSON wire names). h5 emits `new T.$ctorN(args)`.

        [TestMethod]
        public async Task AttributeNonPrimaryCtorArgsPreservedRunsAsync()
        {
            await RunTest(@"
using System;
using System.Reflection;
[AttributeUsage(AttributeTargets.Class)]
public class TagAttribute : Attribute
{
    public string Name { get; }
    public int Order { get; }
    public TagAttribute() { Name = ""default""; }
    public TagAttribute(string name) { Name = name; }
    public TagAttribute(string name, int order) { Name = name; Order = order; }
}
[Tag(""hello"", 7)]
public class Widget { }
public class Program
{
    public static void Main()
    {
        var a = (TagAttribute)typeof(Widget).GetCustomAttributes(typeof(TagAttribute), false)[0];
        Console.WriteLine(a.Name + ""/"" + a.Order);   // hello/7 (not default/0)
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- operators on long / ulong / decimal, bool.ToString, interpolation alignment ------

        [TestMethod]
        public async Task BoolToStringUsesDotNetCasingRunsAsync()
        {
            await RunTest(@"
using System;
public class Program
{
    public static void Main()
    {
        bool t = true;
        Console.WriteLine(t.ToString());     // True
        Console.WriteLine(false.ToString()); // False
        object o = t;
        Console.WriteLine(o.ToString());     // True
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public async Task IntegerOnLeftOfDecimalOperatorRunsAsync()
        {
            await RunTest(@"
using System;
public class Program
{
    public static void Main()
    {
        int i = 5; decimal m = 2m; short sh = 3; long L = 5L;
        Console.WriteLine(i + m);   // 7
        Console.WriteLine(i / m);   // 2.5
        Console.WriteLine(i - m);   // 3
        Console.WriteLine(i % m);   // 1
        Console.WriteLine(i < m);   // False
        Console.WriteLine(sh + m);  // 5
        Console.WriteLine(L / m);   // 2.5
        Console.WriteLine(m + i);   // 7
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public async Task CompoundAssignLongUlongDecimalRunsAsync()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;
public class Program
{
    public static void Main()
    {
        long p = 10L; p += 3L;  Console.WriteLine(p);   // 13
        long o = 10L; o /= 4L;  Console.WriteLine(o);   // 2
        long q = 1L;  q <<= 40; Console.WriteLine(q);   // 1099511627776
        ulong u = 10UL; u += 3UL; Console.WriteLine(u); // 13
        decimal d = 10m; d += 3m; Console.WriteLine(d); // 13
        decimal e = 10m; e %= 3m; Console.WriteLine(e); // 1
        var dict = new Dictionary<string,long>{{""k"",10L}}; dict[""k""] += 5L; Console.WriteLine(dict[""k""]); // 15
        var lst = new List<decimal>{10m}; lst[0] += 3m; Console.WriteLine(lst[0]); // 13
        long[] arr = { 100L }; arr[0] *= 3L; Console.WriteLine(arr[0]); // 300
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public async Task IncrementDecrementLongDecimalRunsAsync()
        {
            await RunTest(@"
using System;
public class Program
{
    public static void Main()
    {
        long b = 9007199254740993L; b++; Console.WriteLine(b);   // 9007199254740994
        decimal d = 0.1m; d++; Console.WriteLine(d);             // 1.1
        long x = 10L; x++; long y = x * 3L; Console.WriteLine(y);// 33
        long a = 5L; long bb = a++; Console.WriteLine(a + "" "" + bb); // 6 5
        decimal dc = 1m; decimal ec = dc--; Console.WriteLine(dc + "" "" + ec); // 0 1
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public async Task InterpolationAlignmentRunsAsync()
        {
            await RunTest(@"
using System;
public class Program
{
    public static void Main()
    {
        int x = 42;
        Console.WriteLine($""[{x,10}]"");   // [        42]
        Console.WriteLine($""[{x,-10}]"");  // [42        ]
        Console.WriteLine($""[{x,5:N1}]"");// [ 42.0]
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- default value of static struct fields / generic-method type params ----

        [TestMethod]
        public async Task StaticStructFieldDefaultsToZeroedStructRunsAsync()
        {
            await RunTest(@"
using System;
public struct Pt { public int X; public int Y; }
public class St
{
    public static DateTime When;
    public static Guid Id;
    public static Pt P;
    public static (int, string) Tup { get; set; }
}
public class Program
{
    public static void Main()
    {
        Console.WriteLine(St.When == DateTime.MinValue); // True
        Console.WriteLine(St.When.Ticks);                // 0 (no crash)
        Console.WriteLine(St.Id == Guid.Empty);          // True
        Console.WriteLine(St.P.X + "","" + St.P.Y);        // 0,0
        Console.WriteLine(St.Tup.Item1 + "","" + (St.Tup.Item2 ?? ""null"")); // 0,null
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public async Task GenericMethodDefaultOfTypeParamRunsAsync()
        {
            await RunTest(@"
using System;
public class Program
{
    static T Def<T>() { return default(T); }
    public static void Main()
    {
        Console.WriteLine(""int="" + Def<int>());           // 0
        Console.WriteLine(""bool="" + Def<bool>());          // False
        Console.WriteLine(""double="" + Def<double>());      // 0
        Console.WriteLine(""dtTicks="" + Def<DateTime>().Ticks); // 0
        Console.WriteLine(""str="" + (Def<string>() ?? ""null"")); // null
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- for-loop variable is a single shared binding (closure captures final value) ------

        [TestMethod]
        public async Task ForLoopVariableCapturedByClosureRunsAsync()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;
public class Program
{
    public static void Main()
    {
        var acts = new List<Func<int>>();
        for (int i = 0; i < 3; i++) acts.Add(() => i);
        var sb = new System.Text.StringBuilder();
        foreach (var a in acts) sb.Append(a());   // 333 (shared for-loop var), NOT 012
        Console.WriteLine(sb.ToString());
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- foreach disposes the enumerator on early exit (iterator finally runs) ------------

        [TestMethod]
        public async Task ForeachRunsIteratorFinallyOnEarlyExitRunsAsync()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;
public class Program
{
    static IEnumerable<int> Gen()
    {
        try { yield return 1; yield return 2; yield return 3; }
        finally { Console.WriteLine(""FINALLY""); }
    }
    public static void Main()
    {
        foreach (var x in Gen()) { if (x == 2) break; Console.WriteLine(""got "" + x); }
        Console.WriteLine(""after"");
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- params array passing: object[] pass-through and optional-before-params -----------

        [TestMethod]
        public async Task ParamsArrayPassThroughAndOptionalRunsAsync()
        {
            await RunTest(@"
using System;
public class Program
{
    static string JO(string sep, params object[] items) => items.Length + "":"" + string.Join(sep, items);
    static string M(string a, int b = 7, params object[] rest) => a + ""/"" + b + ""/["" + string.Join("","", rest) + ""]"";
    public static void Main()
    {
        var oarr = new object[] { ""x"", ""y"" };
        Console.WriteLine(JO("","", oarr));         // 2:x,y  (array passed through, not double-wrapped)
        Console.WriteLine(JO("","", ""a"", ""b"", ""c""));  // 3:a,b,c
        Console.WriteLine(JO("",""));                // 0:
        Console.WriteLine(M(""x""));                 // x/7/[]  (optional b defaults, params empty)
        Console.WriteLine(M(""x"", 2));              // x/2/[]
        Console.WriteLine(M(""x"", 2, ""p"", ""q""));  // x/2/[p,q]
        Console.WriteLine(M(""x"", rest: oarr));      // x/7/[x,y]
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public async Task ForeachOverNullThrowsNullReferenceRunsAsync()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;
public class Program
{
    public static void Main()
    {
        IEnumerable<int> seq = null;
        try { foreach (var x in seq) Console.WriteLine(x); }
        catch (NullReferenceException) { Console.WriteLine(""caught NRE""); }
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- ValueTuple literals are real ValueTuple instances --------------------------------

        [TestMethod]
        public async Task ValueTupleInstanceBehaviourRunsAsync()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;
public class Program
{
    public static void Main()
    {
        var t = (1, ""x"");
        Console.WriteLine(t.ToString());                 // (1, x)
        Console.WriteLine(t.Item1 + ""/"" + t.Item2);       // 1/x
        var (a, b) = t; Console.WriteLine(a + "","" + b);   // 1,x
        var n = (id: 5, name: ""n""); Console.WriteLine(n.id + "":"" + n.name); // 5:n
        Console.WriteLine((1, ""x"").Equals((1, ""x"")));     // True
        Console.WriteLine((1, ""x"") == (1, ""y""));          // False
        Console.WriteLine((1, 2, 3).ToString());          // (1, 2, 3)
        var set = new HashSet<(int, int)> { (1, 2), (1, 2), (3, 4) };
        Console.WriteLine(set.Count);                      // 2
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- Activator.CreateInstance with an explicit object[] of arguments ------------------

        [TestMethod]
        public async Task ActivatorCreateInstanceWithObjectArrayRunsAsync()
        {
            await RunTest(@"
using System;
public class Multi
{
    public string V;
    public Multi(int a) { V = ""one:"" + a; }
    public Multi(int a, int b, int c) { V = ""three:"" + (a + b + c); }
}
public class Program
{
    public static void Main()
    {
        Console.WriteLine(((Multi)Activator.CreateInstance(typeof(Multi), new object[] { 1, 2, 3 })).V); // three:6
        Console.WriteLine(((Multi)Activator.CreateInstance(typeof(Multi), 1, 2, 3)).V);                   // three:6
        Console.WriteLine(((Multi)Activator.CreateInstance(typeof(Multi), 9)).V);                         // one:9
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- reordered named arguments evaluate in source order -------------------------------

        [TestMethod]
        public async Task ReorderedNamedArgsEvaluateInSourceOrderRunsAsync()
        {
            await RunTest(@"
using System;
public class Program
{
    static int Log(string tag) { Console.WriteLine(""eval "" + tag); return tag.Length; }
    static string M(int a, int b, int c = 99) => ""a="" + a + "" b="" + b + "" c="" + c;
    public static void Main()
    {
        Console.WriteLine(M(b: Log(""BB""), a: Log(""AAA"")));            // eval BB; eval AAA; a=3 b=2 c=99
        Console.WriteLine(M(a: Log(""A""), b: Log(""BB"")));              // eval A; eval BB; a=1 b=2 c=99
        Console.WriteLine(M(c: Log(""CCCC""), b: Log(""BB""), a: Log(""A""))); // eval CCCC; BB; A; a=1 b=2 c=4
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public void AttributeNonPrimaryCtorEmitsCtorOverloadName()
        {
            var code = @"
using System;
[AttributeUsage(AttributeTargets.Class)]
public class TagAttribute : Attribute
{
    public TagAttribute() { }
    public TagAttribute(string name) { }
}
[Tag(""x"")] public class Widget { }
public class Program { public static void Main() { } }
";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            // The (string) overload is the non-primary ctor ($ctor1); the attribute instance must be
            // constructed through it, not the bare `new TagAttribute("x")` (which hits the primary ctor).
            Assert.IsTrue(result.Javascript!.Contains("new TagAttribute.$ctor1(\"x\")"),
                "attribute must be constructed via the applied ctor overload\n" + result.Javascript);
        }

        // ---- property pattern resolves the JS member name ----------------------

        [TestMethod]
        public async Task PropertyPatternUsesResolvedJsMemberName()
        {
            // string.Length has a JS-name override ([Convention(CamelCase)] -> `length`). A property
            // pattern must emit `.length`, not the raw `.Length` (which is undefined on a JS string and
            // would make `undefined > 3` always false, silently skipping the arm).
            await RunTest(@"
using System;
using System.Collections.Generic;
public class Program
{
    static string F(object o) => o switch {
        string { Length: > 3 } s => ""long:"" + s,
        string s => ""short:"" + s,
        Dictionary<string,int> { Count: > 0 } d => ""dict:"" + d.Count,
        _ => ""other""
    };
    public static void Main()
    {
        Console.WriteLine(F(""hello""));
        Console.WriteLine(F(""hi""));
        Console.WriteLine(F(new Dictionary<string,int> { [""a""] = 1 }));
        Console.WriteLine(F(42));
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public void PropertyPatternEmitsCamelCasedMember()
        {
            var code = @"
using System;
public class Program
{
    static bool F(object o) => o is string { Length: > 3 };
    public static void Main() { }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsTrue(result.Javascript!.Contains(".length > 3"),
                "property pattern on string.Length must emit the resolved JS name `.length`\n" + result.Javascript);
            Assert.IsFalse(result.Javascript!.Contains(".Length > 3"),
                "property pattern must not emit the raw C# member name `.Length`\n" + result.Javascript);
        }

        // ---- bare type patterns (parsed as constant patterns) ------------------

        [TestMethod]
        public async Task BareTypePatternPerformsTypeTest()
        {
            // A bare type in a switch arm (`Dog => ...`) is parsed as a ConstantPattern; it must run a
            // type test, not `subject === Dog` (which compares the value to the class constructor and
            // is always false). Enum type patterns must use the runtime type check too (a boxed enum
            // is a Transpose.box object, not a plain number).
            await RunTest(@"
using System;
public class Animal { }
public class Dog : Animal { }
public class Cat : Animal { }
public enum Color { Red, Green, Blue }
public class Program
{
    static string Kind(object o) => o switch {
        Dog => ""dog"",
        Cat => ""cat"",
        Color => ""color"",
        _ => ""other""
    };
    public static void Main()
    {
        Console.WriteLine(Kind(new Dog()));
        Console.WriteLine(Kind(new Cat()));
        Console.WriteLine(Kind(Color.Green));
        Console.WriteLine(Kind(""x""));
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public async Task EnumTypePatternMatchesBoxedEnum()
        {
            // A boxed enum tested/captured via a type pattern (o is Color c) and a switch type pattern.
            await RunTest(@"
using System;
public enum Color { Red, Green, Blue }
public enum Size { S, M, L }
public class Program
{
    public static void Main()
    {
        object o = Color.Green;
        Console.WriteLine(o is Color ? ""is-color"" : ""no"");
        Console.WriteLine(o is Size ? ""is-size"" : ""not-size"");
        Console.WriteLine(o is Color c ? c.ToString() : ""nocap"");
        object o2 = Size.L;
        Console.WriteLine(o2 switch { Color => ""color"", Size => ""size"", _ => ""other"" });
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public async Task ExtendedPropertyPatternResolvesChainedMembers()
        {
            // An extended property pattern names a member chain (`{ Text.Length: > 3 }`); each segment
            // must resolve to its JS name (the whole chain `Text.Length`, not just the leaf `Length`,
            // and Length -> the `length` override).
            await RunTest(@"
using System;
public class Node { public string Text { get; set; } }
public class Program
{
    static string F(Node n) => n switch {
        { Text.Length: > 3 } => ""long"",
        { Text.Length: 0 } => ""empty"",
        _ => ""short""
    };
    public static void Main()
    {
        Console.WriteLine(F(new Node { Text = ""hello"" }));
        Console.WriteLine(F(new Node { Text = ""hi"" }));
        Console.WriteLine(F(new Node { Text = """" }));
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- binary + / - on delegates → combine / remove ----------------------

        [TestMethod]
        public async Task BinaryDelegateOperatorsCombineAndRemove()
        {
            // `d1 + d2` / `d1 - d2` must build/unbuild a multicast delegate (TransposeR.combine/remove),
            // not JS numeric/string `+`/`-` on the underlying functions (which yields a non-callable).
            await RunTest(@"
using System;
using System.Text;
public class Program
{
    static StringBuilder sb = new StringBuilder();
    static void A() => sb.Append(""A"");
    static void B() => sb.Append(""B"");
    static void C() => sb.Append(""C"");
    public static void Main()
    {
        Action a = A;
        Action ab = a + (Action)B;
        ab(); sb.AppendLine("""");
        Action abc = ab + (Action)C;
        abc(); sb.AppendLine("""");
        Action ac = abc - (Action)B;
        ac(); sb.AppendLine("""");
        sb.Append(""<<DONE>>"");
        Console.WriteLine(sb.ToString());
    }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public void BinaryDelegateAddEmitsCombine()
        {
            var code = @"
using System;
public class Program
{
    static void A() { }
    static void B() { }
    public static void Main()
    {
        Action a = A;
        Action ab = a + (Action)B;
        ab();
    }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsTrue(result.Javascript!.Contains("TransposeR.combine("),
                "binary + on delegates must emit TransposeR.combine\n" + result.Javascript);
        }

        // ---- LINQ Chunk / MinBy / MaxBy (BCL EnumerableExtras) ------------------

        [TestMethod]
        public async Task LinqChunkMinByMaxByMatchNative()
        {
            // Chunk / MinBy / MaxBy are implemented in C# in the BCL (EnumerableExtras); verify they
            // match System.Linq including chunk sizing, key selection, empty-source default/throw and
            // the null-key skip rule.
            await RunTest(@"
using System;
using System.Linq;
public class Program
{
    public static void Main()
    {
        Console.WriteLine(string.Join(""|"", new[]{1,2,3,4,5}.Chunk(2).Select(c => string.Join("","", c))));
        Console.WriteLine(new int[0].Chunk(3).Count());
        var people = new[]{ (""Al"",30), (""Bo"",25), (""Cy"",35) };
        Console.WriteLine(people.MinBy(p => p.Item2).Item1);
        Console.WriteLine(people.MaxBy(p => p.Item2).Item1);
        Console.WriteLine(new string[0].MinBy(s => s.Length) ?? ""null"");
        var withNulls = new[]{ (""a"",(string)null), (""b"",""x""), (""c"",(string)null), (""d"",""a"") };
        Console.WriteLine(withNulls.MinBy(t => t.Item2).Item1);
        try { var _ = new int[0].MinBy(v => v); Console.WriteLine(""noio""); }
        catch (InvalidOperationException) { Console.WriteLine(""io-throws""); }
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- Task.Yield + invariant string casing (BCL) ------------------------

        [TestMethod]
        public async Task TaskYieldResumesAsynchronously()
        {
            // await Task.Yield() must resume on a later tick (after the current job), preserving
            // ordering with subsequent yields. Asserted on the JS output only (skipRoslyn): a
            // fire-and-forget async Run() from Main is non-deterministic under native .NET (the
            // process can exit before the Yield continuations run), whereas Node drains its queue.
            var js = await RunTest(@"
using System;
using System.Threading.Tasks;
public class Program
{
    static async Task Run()
    {
        Console.WriteLine(""before"");
        await Task.Yield();
        Console.WriteLine(""after"");
        for (int i = 0; i < 3; i++) { await Task.Yield(); Console.WriteLine(""tick "" + i); }
        Console.WriteLine(""<<DONE>>"");
    }
    public static void Main() { Run(); }
}", waitForOutput: "<<DONE>>", skipRoslyn: true);

            var seq = string.Join(",", js.Replace("\r\n", "\n").Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries));
            Assert.AreEqual("before,after,tick 0,tick 1,tick 2,<<DONE>>", seq,
                "Task.Yield must resume asynchronously, preserving order across successive yields\n" + js);
        }

        [TestMethod]
        public async Task StringInvariantCasingMatchesToLowerToUpper()
        {
            await RunTest(@"
using System;
public class Program
{
    public static void Main()
    {
        Console.WriteLine(""MiXeD"".ToLowerInvariant());
        Console.WriteLine(""MiXeD"".ToUpperInvariant());
        Console.WriteLine(""ABC"".ToLowerInvariant() == ""ABC"".ToLower());
        Console.WriteLine(""abc"".ToUpperInvariant() == ""abc"".ToUpper());
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- [ObjectLiteral] instance-method dispatch --------------------------
        //
        // An [ObjectLiteral] class's instances are plain JS objects (typically JSON-parsed) that carry
        // NO methods of their own, so an instance method call must dispatch through the prototype —
        // Type.prototype.Method.call(receiver, args) — not as `receiver.Method(args)` (which throws
        // "is not a function" at runtime). This is how the Curiosity FrontEnd's Mosaik.Schema.Node /
        // NodeOrEdge helpers (node.TryGetSource(out …), node.GetString(…)) are consumed.

        [TestMethod]
        public void ObjectLiteralInstanceCallDispatchesThroughPrototype()
        {
            var code = @"
using Transpose;
[ObjectLiteral(ObjectCreateMode.Constructor)]
public sealed class Bag
{
    private Bag() { }
    public bool TryGet(string key, out string value) { value = Script.Write<string>(""this[key]""); return value != null; }
    public T GetAs<T>(string key) => Script.Write<T>(""this[key]"");
}
public class Program
{
    public static void Main()
    {
        var b = Script.Write<Bag>(""({ name: 'x' })"");
        b.TryGet(""name"", out var v);
        var s = b.GetAs<string>(""name"");
    }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;
            // out/ref call routes through the prototype with the receiver as the `.call` this-arg.
            Assert.IsTrue(js.Contains("Bag.prototype.TryGet.call("),
                "out/ref instance call on an [ObjectLiteral] type must dispatch through the prototype\n" + js);
            // generic call: receiver precedes the threaded type argument.
            Assert.IsTrue(js.Contains("Bag.prototype.GetAs.call(b, System.String"),
                "generic instance call on an [ObjectLiteral] type must be Type.prototype.M.call(recv, T, args)\n" + js);
        }

        [TestMethod]
        public void ScriptWriteInlinesTemplateWithDynamicArgument()
        {
            // A `dynamic` argument makes Roslyn bind the call as late-bound, so GetSymbolInfo().Symbol
            // is null. The Script.Write special-case must still recover the overload from the
            // candidate symbols and inline the raw-JS template — not emit a bogus `Transpose.Write(...)`.
            var code = @"
using System;
using Transpose;
public class Program
{
    public static void Main()
    {
        dynamic opt = 42;
        object obj = ""x"";
        Script.Write(""console.log({0})"", obj);   // static arg — always inlined
        Script.Write(""console.log({0})"", opt);   // dynamic arg — the regression
        Action<dynamic> onInit = e => Script.Write(""{0}.layout()"", e);
        onInit(7);
    }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;
            Assert.IsFalse(js.Contains("Transpose.Write("),
                "Script.Write with a dynamic argument must inline, not emit Transpose.Write(...)\n" + js);
            Assert.IsTrue(js.Contains("console.log(opt)"),
                "the dynamic-argument template must substitute {0} with the argument\n" + js);
            Assert.IsTrue(js.Contains("e.layout()"),
                "a dynamic lambda-parameter argument must substitute into the template\n" + js);
        }

        [TestMethod]
        public void ScriptWriteWrapsInlineLambdaArgumentInParentheses()
        {
            // The template immediately invokes the substituted argument (`{1}()`). A lambda /
            // delegate-creation argument emits as a bare arrow function, and `() => {…}()` is a
            // syntax error — it must be parenthesized to `(() => {…})()`. A delegate held in a
            // variable is already a primary expression and must NOT be wrapped.
            var code = @"
using System;
using Transpose;
public class Program
{
    public static void Main()
    {
        dynamic m = 1;
        Action a = () => { Console.WriteLine(""v""); };
        Script.Write(""{0}.onChange(function() {{ {1}(); }})"", m, new Action(() => { Console.WriteLine(""x""); }));
        Script.Write(""{0}()"", a);
    }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;
            Assert.IsTrue(js.Contains("(() =>"),
                "an inline-lambda argument in call position must be parenthesized\n" + js);
            Assert.IsFalse(js.Contains("(a)()"),
                "a delegate held in a variable must not be needlessly parenthesized\n" + js);
        }

        [TestMethod]
        public async Task ExpressionBodiedLocalFunctionHoistsOutVar()
        {
            // A local function with an EXPRESSION body whose expression introduces an out-var
            // (`string F() => dict.TryGetValue(k, out var v) ? v : null`) must predeclare `var v;`
            // in the arrow's block — the write-back `v = $ref.v` happens inside a condition IIFE but
            // `v` is read outside it. Without the hoist the name is undeclared and strict-mode bundles
            // throw `ReferenceError: v is not defined` (seen as `u is not defined` in the front-end's
            // InspectChatView.CurrentUidParam). Method/lambda bodies already hoisted; local functions did not.
            await RunTest(@"
using System;
using System.Collections.Generic;
public class Program
{
    public static void Main()
    {
        var state = new Dictionary<string, string> { { ""uid"", ""abc"" } };
        string CurrentUidParam() => state.TryGetValue(""uid"", out var u) ? u : null;
        string Missing() => state.TryGetValue(""nope"", out var u) ? u : ""fallback"";
        Console.WriteLine(CurrentUidParam() ?? ""NULL"");
        Console.WriteLine(Missing());
    }
}");
        }

        [TestMethod]
        public async Task IntPlusUintPromotesToLongResult()
        {
            // C# binary numeric promotion: `int + uint` has no common 32-bit type, so both promote to
            // `long` and the result is `long`. The sum must be a real Int64 at runtime — it flows into a
            // `long`-typed variable whose later `< 0` / indexing use the Int64 helpers (.lt/.add/…). If
            // the addition stays a plain JS number the downstream `visualIndex.lt(...)` throws
            // `visualIndex.lt is not a function` (LogsView.ValidateVisibleRowHeights on #/manage/operate/logs).
            await RunTest(@"
using System;
public class Program
{
    public static void Main()
    {
        int startIndex = 5;
        for (uint i = 0; i < 3; i++)
        {
            var visualIndex = startIndex + i;   // int + uint => long
            Console.WriteLine(visualIndex.GetType().Name);
            Console.WriteLine(visualIndex < 0 || visualIndex >= 100 ? ""oob"" : ""ok "" + visualIndex);
        }
    }
}");
        }

        // ---- null comparisons must never invoke a user-defined ==/!= operator -----------------

        [TestMethod]
        public void NullableStructComparedToNullDoesNotCallUserOperator()
        {
            // `x == null` / `x != null` on a Nullable<struct-with-user-==> is a HasValue check, never a
            // call to the struct's op_Equality/op_Inequality. Emitting the operator passed the null
            // literal straight into it, which then dereferenced it (DateTimeOffset.op_Inequality(x, null)
            // → `null.UtcDateTime`).
            var code = @"
using System;
public class Program
{
    public static void Main()
    {
        DateTimeOffset? a = null;
        DateTimeOffset? b = DateTimeOffset.UtcNow;
        System.Console.WriteLine(a != null);
        System.Console.WriteLine(b == null);
    }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;
            Assert.IsFalse(js.Contains("op_Inequality") || js.Contains("op_Equality"),
                "a comparison to the null literal must not call the struct's ==/!= operator\n" + js);
        }

        [TestMethod]
        public async Task NullableBclStructNullComparisonsMatchNative()
        {
            // Every BCL struct with a user-defined ==/!= (DateTimeOffset, DateTime, TimeSpan, Guid),
            // as a Nullable<T>, compared to the null literal both ways, in a ternary and via ?. — all
            // must reduce to a HasValue check, never an operator call.
            await RunTest(@"
using System;
public class Box { public DateTimeOffset? When { get; set; } }
public class Program
{
    public static void Main()
    {
        DateTimeOffset? dto = null;
        DateTime?       dt  = DateTime.UtcNow;
        TimeSpan?       ts  = null;
        Guid?           g   = Guid.Empty;

        System.Console.WriteLine(dto != null);          // false
        System.Console.WriteLine(dto == null);          // true
        System.Console.WriteLine(null != dto);          // false (null on the left)
        System.Console.WriteLine(dt  != null);          // true
        System.Console.WriteLine(ts  == null);          // true
        System.Console.WriteLine(g   != null);          // true
        System.Console.WriteLine(dto is null);          // true
        System.Console.WriteLine(dt  is not null);      // true

        var box = new Box { When = null };
        System.Console.WriteLine(box?.When != null ? ""has"" : ""none"");  // none
        box.When = DateTime.UtcNow;
        System.Console.WriteLine(box?.When != null ? ""has"" : ""none"");  // has
        Box nullBox = null;
        System.Console.WriteLine(nullBox?.When != null ? ""has"" : ""none""); // none
    }
}");
        }

        [TestMethod]
        public async Task ReferenceAndCustomStructNullComparisonsMatchNative()
        {
            // Reference types and a user struct that overloads ==/!= (dereferencing a field) must all
            // treat a null-literal comparison as a null test.
            await RunTest(@"
using System;
public struct Money
{
    public string Currency;
    public Money(string c) { Currency = c; }
    // An operator that would throw if handed null (like DateTimeOffset.op_* on UtcDateTime).
    public static bool operator ==(Money a, Money b) => a.Currency.Length == b.Currency.Length;
    public static bool operator !=(Money a, Money b) => !(a == b);
    public override bool Equals(object o) => o is Money m && this == m;
    public override int GetHashCode() => Currency?.Length ?? 0;
}
public class Program
{
    public static void Main()
    {
        string s = null;
        object o = ""x"";
        Money? m = null;
        Money? m2 = new Money(""usd"");

        System.Console.WriteLine(s == null);        // true
        System.Console.WriteLine(s != null);        // false
        System.Console.WriteLine(o == null);        // false
        System.Console.WriteLine(o is not null);    // true
        System.Console.WriteLine(m == null);        // true
        System.Console.WriteLine(m != null);        // false
        System.Console.WriteLine(m2 != null);       // true
        System.Console.WriteLine(m2 is null);       // false
    }
}");
        }

        [TestMethod]
        public async Task ConditionalExpressionLiftsNarrowBranchToLong()
        {
            // A ?: whose type is `long` because one branch is long must lift a narrow (int) branch to
            // Int64 — each branch is implicitly converted to the conditional's type. Otherwise the
            // ternary yields a plain number and a downstream Int64 op throws `.sub is not a function`
            // (MigrationsView.RenderTasks: `(intField==0 ? someLong : intField) - (...)`).
            await RunTest(@"
using System;
public class Row { public int Ticks; public bool Flag; }
public class Program
{
    static long Key(Row r)
        => (r.Ticks == 0 ? 5000000000L : r.Ticks) - (r.Flag ? 1000000000L : 0);
    public static void Main()
    {
        System.Console.WriteLine(Key(new Row { Ticks = 0,  Flag = false }));  // 5000000000
        System.Console.WriteLine(Key(new Row { Ticks = 42, Flag = false }));  // 42
        System.Console.WriteLine(Key(new Row { Ticks = 42, Flag = true  }));  // 42 - 1000000000
        long l = 7; int i = 3; bool c = false;
        long m = c ? l : i;                                                   // int branch -> long
        System.Console.WriteLine(m);                                          // 3
    }
}");
        }

        [TestMethod]
        public async Task SiblingBlockSameNameLocalsCapturedIndependently()
        {
            // C# block-scopes a local to its block, so same-named locals in sibling blocks are distinct
            // and each closure captures its own. JS `var` is function-scoped, so emitting all three as
            // `var pending` made every closure see the last value (C,C,C). Block-scoped `let` restores
            // per-block bindings. Mirrors API.Aggregated.ActuallyTriggerDelayedTasksIfRequired (three
            // sibling `if` blocks, each `var pending = ...` captured by a fire-and-forget task).
            await RunTest(@"
using System;
using System.Collections.Generic;
public class Program
{
    public static void Main()
    {
        var actions = new List<Func<string>>();
        if (true) { var pending = ""A""; actions.Add(() => pending); }
        if (true) { var pending = ""B""; actions.Add(() => pending); }
        if (true) { var pending = ""C""; actions.Add(() => pending); }
        foreach (var a in actions) System.Console.WriteLine(a());   // A B C

        // Nested blocks reusing a name at multiple depths, each captured.
        var deep = new List<Func<string>>();
        { var v = ""d1""; { var w = ""d2""; { var v2 = ""d3""; deep.Add(() => v + w + v2); } } }
        { var v = ""e1""; deep.Add(() => v); }
        foreach (var d in deep) System.Console.WriteLine(d());      // d1d2d3  e1

        // Loop body local captured per-iteration (already block-scoped).
        var loop = new List<Func<int>>();
        for (int i = 0; i < 3; i++) { var x = i * 10; loop.Add(() => x); }
        foreach (var f in loop) System.Console.WriteLine(f());      // 0 10 20

        // A classic for-loop variable is a single shared binding (closures see the final value).
        var shared = new List<Func<int>>();
        for (int i = 0; i < 3; i++) shared.Add(() => i);
        foreach (var f in shared) System.Console.WriteLine(f());    // 3 3 3
    }
}");
        }

        [TestMethod]
        public void GenericMethodInTransposeNamedLibraryThreadsTypeArgs()
        {
            // A Transpose.*-named binding library that carries real implementation (NOT [assembly:External])
            // must thread its generic methods' type args as leading JS parameters, so runtime uses of the
            // type parameter in the body (IEnumerable<T>, typeof(T), …) resolve. IsTransposeCompiledSource
            // classifies any Transpose.*-named assembly as runtime purely by name, so ThreadsTypeArgs must
            // still thread a non-external, body-having generic method there.
            // Regression: Transpose.Plotly.Bindings.flatten2DArrayIf1D<T> emitted `function (values)` while
            // the body referenced `T` → `ReferenceError: T is not defined` on #/manage/operate/usage.
            var code = @"
using System.Collections.Generic;
using System.Linq;
public static class Bindings
{
    public static object Flatten<T>(IEnumerable<IEnumerable<T>> values) => values.First().ToArray();
}";
            var result = new RoslynTranslator().Translate(
                new[] { ("App.cs", code) }, "Transpose.Plotly", null, new[] { "DEBUG" });
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;
            Assert.IsTrue(js.Contains("Flatten: function (T,"),
                "a non-external body-having generic method in a Transpose.*-named library must thread its type args\n" + js);
        }

        [TestMethod]
        public async Task ObjectLiteralInstanceMethodRunsOnPlainObject()
        {
            // The receiver is a plain object literal (no prototype), exactly like a JSON.Parse result.
            // Both the external call and an internal implicit-`this` sibling call must still resolve.
            await RunTest(@"
using System;
using Transpose;
[ObjectLiteral(ObjectCreateMode.Constructor)]
public abstract class Base
{
    protected Base() { }
    public bool TryGetName(out string name) => Inner(out name);      // implicit-this sibling call
    private bool Inner(out string name) { name = GetRaw(""name""); return name != null; }
    private string GetRaw(string key) => Script.Write<string>(""this[key]"");
}
[ObjectLiteral(ObjectCreateMode.Constructor)]
public sealed class Thing : Base { private Thing() { } public int Value => Script.Write<int>(""this.Value""); }
public class Program
{
    public static void Main()
    {
        var t = Script.Write<Thing>(""({ name: 'abc', Value: 42 })"");
        if (t.TryGetName(out var n)) Console.WriteLine(""name="" + n + "" value="" + t.Value);
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>", skipRoslyn: true);
        }

        // ---- user-defined implicit conversion at a field/property initializer ---
        //
        // A property/field initialized with a value of a different type must apply the user-defined
        // implicit conversion operator, not store the raw source value. The Curiosity FrontEnd's
        // SearchQuery.ParsedAsLanguage (type LanguageDTO, initialized `= Language.Unknown`) relied on
        // this: without the operator the field held the raw enum number, which then serialized to a
        // number and blew up on JSON round-trip. The operator changes the representation, so it must run.

        [TestMethod]
        public async Task ImplicitConversionOperatorAppliedAtInitializerAndAssignment()
        {
            await RunTest(@"
using System;
public struct Wrapper
{
    public string Code;
    public static implicit operator Wrapper(int value) => new Wrapper { Code = ""N"" + value };
    public static explicit operator int(Wrapper w) => int.Parse(w.Code.Substring(1));
    public override string ToString() => ""W("" + Code + "")"";
}
public class Holder
{
    public Wrapper Prop { get; set; } = 42;   // property initializer + implicit conversion
    public Wrapper Field = 7;                  // field initializer + implicit conversion
}
public class Program
{
    public static void Main()
    {
        var h = new Holder();
        Console.WriteLine(h.Prop);             // W(N42)
        Console.WriteLine(h.Field);            // W(N7)
        Wrapper w = 99; Console.WriteLine(w);  // assignment: W(N99)
        int back = (int)w; Console.WriteLine(back); // explicit op round-trips: 99
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- `this` inside the reordered-named-argument IIFE --------------------
        //
        // When named arguments are supplied out of parameter order, the emitter evaluates them into a
        // temp array (source order) inside an IIFE, then hands them back in parameter order. That IIFE
        // must be an ARROW so a `this`-qualified argument keeps the enclosing instance; a plain
        // `function () { … }` rebinds `this` to undefined and throws "reading X of undefined". Seen in
        // the Curiosity FrontEnd's `new SearchResultsComponent(owner: this, …, wrapResults: this._wrapResults, …)`.

        [TestMethod]
        public async Task ThisInReorderedNamedArgumentsResolvesToInstance()
        {
            await RunTest(@"
using System;
public class C
{
    public int X = 9;
    private string G(int a, int b, int c) => a + "","" + b + "","" + c;
    public string Run() => G(c: X, a: 1, b: 2); // reordered named args + this.X
}
public class Program
{
    public static void Main()
    {
        Console.WriteLine(new C().Run()); // 1,2,9
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- BCL implicit conversion operator (DateTime -> DateTimeOffset) ------
        //
        // Runtime/BCL conversion operators (assembly "Transpose") are emitted against the hand-written
        // runtime primitives, so they must be materialised too — `DateTimeOffset x = someDate` needs
        // op_Implicit or the value stays a DateTime and `x.AddDays(...)` throws "is not a function"
        // (Curiosity FrontEnd MigrationsView: `DateTimeOffset startOfWeek = now.AddDays(-d).Date`).

        [TestMethod]
        public async Task BclImplicitConversionOperatorIsApplied()
        {
            await RunTest(@"
using System;
public class Program
{
    public static void Main()
    {
        DateTimeOffset x = new DateTime(2020, 1, 1); // implicit DateTime -> DateTimeOffset
        Console.WriteLine(x.AddDays(1).Day);          // 2 (operator materialised -> real DateTimeOffset)
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- type [Convention] applies to an interface-implementing method -----
        //
        // A method that implicitly implements an interface member whose own JS name carries no rule
        // (IDisposable.Dispose -> raw "Dispose") must still follow the IMPLEMENTING type's explicit
        // [Convention]. CancellationTokenSource is [Convention(CamelCase)], so `cts.Dispose()` must
        // emit `cts.dispose()` — the slot the hand-written runtime and h5 use; PascalCase "Dispose"
        // threw "is not a function". A ruled interface member (templated GetEnumerator, or
        // IEnumerator.MoveNext's own [Convention]) is still inherited unchanged.

        [TestMethod]
        public void ConventionAppliesToInterfaceImplementingMethod()
        {
            var code = @"
using Transpose;
[External]
[Convention(Member = ConventionMember.Method, Notation = Notation.CamelCase)]
public class Res : System.IDisposable
{
    public extern void Dispose();
    public extern void DoWork();
}
public class Program { public static void Main() { Res r = null; r.Dispose(); r.DoWork(); } }";
            var js = new RoslynTranslator().Translate(code).Javascript!;
            Assert.IsTrue(js.Contains(".dispose()"),
                "IDisposable.Dispose on a CamelCase-convention type must emit .dispose()\n" + js);
            Assert.IsTrue(js.Contains(".doWork()"), "regular convention method still camelCases\n" + js);
            Assert.IsFalse(js.Contains(".Dispose()"), "must not emit PascalCase .Dispose()\n" + js);
        }

        // ---- DateTimeOffset operators call the runtime op_ methods --------------
        //
        // DateTimeOffset is a real (non-external) BCL struct whose operators are transpiled to op_
        // methods in the runtime. `now - date` must call System.DateTimeOffset.op_Subtraction (-> a
        // TimeSpan), not emit raw JS `now - date` on two objects (which yielded NaN and threw
        // ".getTotalDays is not a function" on the Migrations admin view). DateTime keeps its dt*
        // helper path. Comparisons must call the op_ methods too.

        [TestMethod]
        public async Task DateTimeOffsetOperatorsCallRuntimeMethods()
        {
            await RunTest(@"
using System;
public class Program
{
    public static void Main()
    {
        DateTimeOffset a = new DateTime(2020, 1, 10);
        DateTimeOffset b = new DateTime(2020, 1, 1);
        TimeSpan diff = a - b;                     // op_Subtraction -> TimeSpan
        Console.WriteLine(diff.TotalDays);         // 9
        Console.WriteLine(a > b);                  // True
        Console.WriteLine(b >= a);                 // False
        DateTimeOffset c = a - TimeSpan.FromDays(2); // op_Subtraction(DTO, TimeSpan) -> DTO
        Console.WriteLine(c.Day);                  // 8
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- method-level type parameters in reflection metadata --------------
        //
        // A method's generic type parameter has no runtime value in reflection metadata, so it must be
        // emitted as System.Object — even when NESTED in another generic (List<T> in the return type of
        // Convert<T>). Otherwise the metadata references a bare, undefined `T`/`TOutput` and evaluating
        // it throws "ReferenceError: TOutput is not defined" (hit reflecting over List.ConvertAll<TOutput>).

        [TestMethod]
        public void MethodTypeParameterInMetadataBecomesObject()
        {
            var code = @"
using System.Collections.Generic;
public class Foo { public List<T> Convert<T>(T input) { return null; } }
public class Program { public static void Main() { } }";
            var js = new RoslynTranslator().Translate(code).Javascript!;
            Assert.IsFalse(js.Contains("List$1(T)"),
                "a method type parameter nested in a generic must not leak into metadata as bare T\n" + js);
            Assert.IsTrue(js.Contains("List$1(System.Object)"),
                "List<T> in a generic method's metadata should resolve T to System.Object\n" + js);
        }

        // ---- default(struct) zero-initializes nested non-primitive struct fields ----
        //
        // A struct's getDefaultValue factory must set a non-primitive struct field (DateTime, Guid, a
        // nested struct) to the ZEROED struct, not null — otherwise default(DateTimeOffset).m_dateTime
        // is null and .UtcDateTime/.Equals throw "reading getTime of null" (hit comparing a default
        // DateTimeOffset during Newtonsoft serialization on several views).

        [TestMethod]
        public async Task DefaultStructZeroInitializesNestedStructFields()
        {
            await RunTest(@"
using System;
public struct Holder { public DateTime When; public int N; }
public class Program
{
    public static void Main()
    {
        Holder h = default(Holder);
        Console.WriteLine(h.When.Year + "" "" + h.N);      // 1 0
        DateTimeOffset d = default(DateTimeOffset);
        Console.WriteLine(d.UtcDateTime.Year);             // 1 (no null deref)
        Console.WriteLine(d.Equals(default(DateTimeOffset))); // True
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- Nullable<T>.ToString()/GetHashCode() must not pass the type as the converter fn ----
        //
        // The {T:ToString} / {T:GetHashCode} template placeholders (System.Nullable.toString/.getHashCode
        // second arg) resolved to the bound type itself, so the runtime called the *type* as a function
        // (System.Nullable.toString(icon, UIcons) → UIcons(icon)) and crashed with "Cannot read
        // properties of undefined (reading '$initialize')" — hit rendering search results where a
        // UIcons? icon flowed into icon.ToString(). An enum must convert through System.Enum.toString
        // (native toString gives the number, not the name); bool/char need their own converters; every
        // other type (and all GetHashCode) drops the arg so the runtime falls back correctly.

        [TestMethod]
        public async Task NullableToStringAndGetHashCodeMatchNative()
        {
            await RunTest(@"
using System;
[Flags] public enum Perm { None = 0, Read = 1, Write = 2, Exec = 4 }
public enum Big { A = 1, B = 50000000 }   // int-backed: a 64-bit underlying type is unsupported
public class Program
{
    public static void Main()
    {
        Perm? icon = Perm.Read;
        Console.WriteLine(icon.ToString());                     // Read
        Perm? flags = Perm.Read | Perm.Write;
        Console.WriteLine(flags.ToString());                    // Read, Write
        Console.WriteLine(flags.GetHashCode());                 // 3
        Perm? none = null;
        Console.WriteLine(""["" + none.ToString() + ""]"");         // []
        Console.WriteLine(none.GetHashCode());                  // 0
        Big? big = Big.B;
        Console.WriteLine(big.ToString());                      // B
        bool? b = true;
        Console.WriteLine(b.ToString());                        // True
        char? c = 'X';
        Console.WriteLine(c.ToString());                        // X
        int? i = 42;
        Console.WriteLine(i.ToString());                        // 42
        Console.WriteLine(i.GetHashCode());                     // 42
        Console.WriteLine(((int)((Perm?)null).GetValueOrDefault()).ToString()); // 0 ({T:default} still works)
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- [Name] on a property ACCESSOR renames the property's JS slot ----
        //
        // Roslyn attaches an accessor-level attribute to the get/set method, not the property, so a
        // property-only [Name] lookup missed it: Tesserae's ReadOnlyArray<T>.Length has its GETTER
        // marked [Name("length")] to hit the native JS array `.length`. From a referenced (non-source,
        // non-external) assembly the access emitted `.Length` (capital), which is undefined on a plain
        // array — so `.Where(g => g.Values.Length > 0)` dropped every group, GetNodeTypesAndFields
        // returned empty, and the node/field dropdown's `.Single()` threw "No element satisfies the
        // condition". The property's JS name must fall back to its accessor's [Name].

        [TestMethod]
        public void NameOnPropertyAccessorRenamesJsSlot()
        {
            var code = @"
using Transpose;
public class Box
{
    public int Count { [Name(""cnt"")] get; set; }
    public int Total { [Name(""tot"")] set; get; }
}
public class Program
{
    public static int Read(Box b) => b.Count + b.Total;
    public static void Main() { }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;
            Assert.IsTrue(js.Contains("b.cnt") && js.Contains("b.tot"),
                "a property whose accessor carries [Name] must emit that JS slot for access\n" + js);
            Assert.IsFalse(js.Contains("b.Count") || js.Contains("b.Total"),
                "the verbatim C# property name must not leak when an accessor [Name] renames the slot\n" + js);
        }

        // ---- a C# local must not be emitted as `let` when a Script.Write redeclares it as `var` ----
        //
        // Raw JS in a Script.Write(...) often redeclares a local the method also computes into — e.g.
        // Tesserae's Color.FromString has C# `int r, g, b` and a Script.Write("… var r … var g … var b
        // …"). Legacy h5 emitted the locals as `var`, so the two merged into one function-scoped var;
        // the var->let block-scoping change made the local a `let`, and a `let` beside a same-named
        // `var` in one scope is a hard "Identifier 'r' has already been declared" SyntaxError that broke
        // the whole tss.js bundle. A local whose name a Script.Write declares (outside a loop) must fall
        // back to `var`.

        [TestMethod]
        public async Task ScriptWriteVarDoesNotCollideWithLetLocal()
        {
            var js = await RunTest(@"
using System;
using Transpose;
public class Program
{
    static int FromString(string s)
    {
        int r = 0; int g = 0; int b = 0;
        if (s.Length == 0) { return r + g + b; }
        Script.Write(""var bigint = parseInt(s, 16); var r = (bigint >> 16) & 255; var g = (bigint >> 8) & 255; var b = bigint & 255;"");
        return r + g + b;
    }
    public static void Main()
    {
        Console.WriteLine(FromString(""ffffff""));   // 765 (no let/var redeclaration SyntaxError)
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>", skipRoslyn: true);
            Assert.IsTrue(js.Contains("765"),
                "FromString must run to 765 — the JS must not fail with a let/var redeclaration\n" + js);
        }

        // ---- [ObjectLiteral] — every attribute-parameter combination ----
        //
        // The attribute has four ctor overloads: (), (ObjectInitializationMode),
        // (ObjectCreateMode), (ObjectInitializationMode, ObjectCreateMode). Two orthogonal axes:
        //   ObjectInitializationMode  Ignore(0) | Initializer(1) | DefaultValue(2)  — how the {} literal
        //                             is seeded (Plain create only).
        //   ObjectCreateMode          Plain(0) | Constructor(1)                     — {} literal vs a real
        //                             `new T(args)` that RUNS the constructor.
        // ObjectCreateMode was being ignored entirely, so Constructor collapsed to Ignore/{} and dropped
        // the constructor arguments — the Curiosity FileExtensions dictionary came out full of empty {}.
        // The h5 baseline (and native .NET, where [ObjectLiteral] is a no-op) run the constructor.
        //
        // Test class shape: ctor sets X (from the arg) and Y=99; X and Z carry `= 5` / `= 7`
        // initializers; Y has none. Construction is `new T(3)`.
        //   Plain + Ignore        → {}                     (arg dropped, no seeding)
        //   Plain + Initializer   → {X:5, Z:7}             (only initialized properties)
        //   Plain + DefaultValue  → {X:5, Y:0, Z:7}        (all properties)
        //   Constructor (any init)→ {X:3, Z:7, Y:99}       (ctor runs: arg → X, field inits, ctor body → Y)

        private const string ObjectLiteralMatrix = @"
using System;
using Transpose;
[ObjectLiteral]                                                                      public class A { public A(int a){X=a;Y=99;} public int X{get;set;}=5; public int Y{get;set;} public int Z{get;set;}=7; }
[ObjectLiteral(ObjectInitializationMode.Ignore)]                                     public class B { public B(int a){X=a;Y=99;} public int X{get;set;}=5; public int Y{get;set;} public int Z{get;set;}=7; }
[ObjectLiteral(ObjectInitializationMode.Initializer)]                                public class C { public C(int a){X=a;Y=99;} public int X{get;set;}=5; public int Y{get;set;} public int Z{get;set;}=7; }
[ObjectLiteral(ObjectInitializationMode.DefaultValue)]                               public class D { public D(int a){X=a;Y=99;} public int X{get;set;}=5; public int Y{get;set;} public int Z{get;set;}=7; }
[ObjectLiteral(ObjectCreateMode.Plain)]                                              public class E { public E(int a){X=a;Y=99;} public int X{get;set;}=5; public int Y{get;set;} public int Z{get;set;}=7; }
[ObjectLiteral(ObjectCreateMode.Constructor)]                                        public class F { public F(int a){X=a;Y=99;} public int X{get;set;}=5; public int Y{get;set;} public int Z{get;set;}=7; }
[ObjectLiteral(ObjectInitializationMode.Ignore, ObjectCreateMode.Plain)]             public class G { public G(int a){X=a;Y=99;} public int X{get;set;}=5; public int Y{get;set;} public int Z{get;set;}=7; }
[ObjectLiteral(ObjectInitializationMode.Ignore, ObjectCreateMode.Constructor)]       public class H { public H(int a){X=a;Y=99;} public int X{get;set;}=5; public int Y{get;set;} public int Z{get;set;}=7; }
[ObjectLiteral(ObjectInitializationMode.Initializer, ObjectCreateMode.Plain)]        public class I { public I(int a){X=a;Y=99;} public int X{get;set;}=5; public int Y{get;set;} public int Z{get;set;}=7; }
[ObjectLiteral(ObjectInitializationMode.Initializer, ObjectCreateMode.Constructor)]  public class J { public J(int a){X=a;Y=99;} public int X{get;set;}=5; public int Y{get;set;} public int Z{get;set;}=7; }
[ObjectLiteral(ObjectInitializationMode.DefaultValue, ObjectCreateMode.Plain)]       public class K { public K(int a){X=a;Y=99;} public int X{get;set;}=5; public int Y{get;set;} public int Z{get;set;}=7; }
[ObjectLiteral(ObjectInitializationMode.DefaultValue, ObjectCreateMode.Constructor)] public class L { public L(int a){X=a;Y=99;} public int X{get;set;}=5; public int Y{get;set;} public int Z{get;set;}=7; }
public class Program
{
    public static void Main()
    {
        Script.Write(""console.log('A',JSON.stringify({0}))"", new A(3));
        Script.Write(""console.log('B',JSON.stringify({0}))"", new B(3));
        Script.Write(""console.log('C',JSON.stringify({0}))"", new C(3));
        Script.Write(""console.log('D',JSON.stringify({0}))"", new D(3));
        Script.Write(""console.log('E',JSON.stringify({0}))"", new E(3));
        Script.Write(""console.log('F',JSON.stringify({0}))"", new F(3));
        Script.Write(""console.log('G',JSON.stringify({0}))"", new G(3));
        Script.Write(""console.log('H',JSON.stringify({0}))"", new H(3));
        Script.Write(""console.log('I',JSON.stringify({0}))"", new I(3));
        Script.Write(""console.log('J',JSON.stringify({0}))"", new J(3));
        Script.Write(""console.log('K',JSON.stringify({0}))"", new K(3));
        Script.Write(""console.log('L',JSON.stringify({0}))"", new L(3));
        Console.WriteLine(""<<DONE>>"");
    }
}";

        [TestMethod]
        public async Task ObjectLiteralAllModesProduceExpectedRuntimeShape()
        {
            var js = await RunTest(ObjectLiteralMatrix, waitForOutput: "<<DONE>>", skipRoslyn: true);
            void Expect(string tag, string json) =>
                Assert.IsTrue(js.Contains(tag + " " + json),
                    $"[ObjectLiteral] {tag} should produce {json}\n{js}");

            Expect("A", "{}");                       // [ObjectLiteral]              → Ignore + Plain
            Expect("B", "{}");                       // Ignore
            Expect("C", "{\"X\":5,\"Z\":7}");          // Initializer
            Expect("D", "{\"X\":5,\"Y\":0,\"Z\":7}");   // DefaultValue
            Expect("E", "{}");                       // Plain
            Expect("F", "{\"X\":3,\"Z\":7,\"Y\":99}");  // Constructor — ctor runs
            Expect("G", "{}");                       // Ignore + Plain
            Expect("H", "{\"X\":3,\"Z\":7,\"Y\":99}");  // Ignore + Constructor
            Expect("I", "{\"X\":5,\"Z\":7}");          // Initializer + Plain
            Expect("J", "{\"X\":3,\"Z\":7,\"Y\":99}");  // Initializer + Constructor
            Expect("K", "{\"X\":5,\"Y\":0,\"Z\":7}");   // DefaultValue + Plain
            Expect("L", "{\"X\":3,\"Z\":7,\"Y\":99}");  // DefaultValue + Constructor
        }

        [TestMethod]
        public void ObjectLiteralCreateModeControlsConstructionForm()
        {
            var result = new RoslynTranslator().Translate(ObjectLiteralMatrix);
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;

            // Plain create (default) → {} literal (+ initializer seeding); the constructor arg is dropped.
            Assert.IsTrue(js.Contains("JSON.stringify({}))"),
                "Plain/Ignore modes must emit an empty object literal\n" + js);
            Assert.IsTrue(js.Contains("JSON.stringify({X: 5, Z: 7}))"),
                "Initializer mode must seed only the initialized properties\n" + js);
            Assert.IsTrue(js.Contains("JSON.stringify({X: 5, Y: 0, Z: 7}))"),
                "DefaultValue mode must seed every property\n" + js);
            // Constructor create → a real `new T(3)` call so the constructor runs (never {}).
            foreach (var t in new[] { "F", "H", "J", "L" })
                Assert.IsTrue(js.Contains($"JSON.stringify(new {t}(3)))"),
                    $"ObjectCreateMode.Constructor for {t} must emit `new {t}(3)`, not a literal\n" + js);
        }

        // ---- [Enum(Emit.*)] — the NAME-casing modes (the enum member's JS slot name) ----
        //
        // Enum.Emit has value-casing modes (StringName* / Value — how ToString/the emitted value read)
        // AND name-casing modes (Name / NamePreserveCase / NameLowerCase / NameUpperCase) that set the
        // enum member's JS PROPERTY name. The value-casing modes were covered; the name-casing ones were
        // not — the same "an attribute enum parameter changes emission but is untested" gap as
        // ObjectCreateMode. NameLowerCase lowercases the slot, NameUpperCase uppercases it, Name /
        // NamePreserveCase preserve.

        [TestMethod]
        public void EnumEmitNameCasingModesRenameTheMemberSlot()
        {
            var code = @"
using Transpose;
[Enum(Emit.NameLowerCase)]    public enum LowerE { AlphaOne, BetaTwo }
[Enum(Emit.NameUpperCase)]    public enum UpperE { AlphaOne, BetaTwo }
[Enum(Emit.Name)]             public enum NameE  { AlphaOne, BetaTwo }
[Enum(Emit.NamePreserveCase)] public enum PresE  { AlphaOne, BetaTwo }
public class Program
{
    public static object A = LowerE.AlphaOne;
    public static object B = UpperE.BetaTwo;
    public static object C = NameE.AlphaOne;
    public static object D = PresE.BetaTwo;
    public static void Main() { }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;
            Assert.IsTrue(js.Contains("LowerE.alphaone"), "NameLowerCase must lowercase the member slot\n" + js);
            Assert.IsTrue(js.Contains("UpperE.BETATWO"),  "NameUpperCase must uppercase the member slot\n" + js);
            Assert.IsTrue(js.Contains("NameE.AlphaOne"),  "Name must preserve the member slot\n" + js);
            Assert.IsTrue(js.Contains("PresE.BetaTwo"),   "NamePreserveCase must preserve the member slot\n" + js);
        }

        // ---- [Enum(Emit.Value)] ToString/concat/interpolation must not crash ------------------
        //
        // An [Enum(Emit.Value)] enum (e.g. System.Linq.Expressions.ExpressionType,
        // System.MidpointRounding) has no runtime type object — its members inline as bare numbers
        // everywhere, a deliberate bundle-size tradeoff — but converting one to a string (explicit
        // ToString(), string concatenation, or $"{}") unconditionally called
        // System.Enum.toString(TypeRef(type), value), which read a property off `undefined` (there is
        // no such runtime type to look up) and crashed with "Cannot read properties of undefined
        // (reading '<TypeName>')". This reproduced on every Value-mode enum, including ones built
        // through long-working factories like Expression.Assign — nothing to do with any specific
        // factory method. Fixed by falling back to the raw number's own toString() for this mode,
        // in EmitConcatOperand, the explicit ToString() call, string interpolation, and the shared
        // ToStringJs helper (all four previously duplicated the same unconditional call). This
        // necessarily diverges from native, which always has the symbolic name via reflection
        // metadata — that divergence is the accepted cost of choosing Emit.Value, not a new one this
        // introduces, so this test is Transpose-only (skipRoslyn) rather than diffed against native.
        [TestMethod]
        public async Task EnumValueModeToStringDoesNotCrash()
        {
            var js = await RunTest(@"
using System;
using System.Linq.Expressions;
public class Program
{
    public static void Main()
    {
        var c = Expression.Constant(1);
        Console.WriteLine(""concat="" + c.NodeType);
        Console.WriteLine(""explicit="" + c.NodeType.ToString());
        Console.WriteLine($""interp={c.NodeType}"");

        var assign = Expression.Assign(Expression.Parameter(typeof(int)), Expression.Constant(1));
        Console.WriteLine(""assign="" + assign.NodeType);

        Console.WriteLine(""midpoint="" + MidpointRounding.AwayFromZero);
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>", skipRoslyn: true);

            Assert.IsTrue(js.Contains("concat=9"), "ExpressionType.Constant's ordinal (9)\n" + js);
            Assert.IsTrue(js.Contains("explicit=9"), "explicit ToString() must not crash either\n" + js);
            Assert.IsTrue(js.Contains("interp=9"), "string interpolation must not crash either\n" + js);
            Assert.IsTrue(js.Contains("assign=46"), "ExpressionType.Assign's ordinal (46), a different factory\n" + js);
            Assert.IsTrue(js.Contains("midpoint=4"), "MidpointRounding.AwayFromZero's ordinal (4), a different Value-mode enum\n" + js);
        }

        // A default-mode enum (a plain user enum, and a default-mode BCL enum like DayOfWeek) must
        // keep printing its symbolic NAME exactly like native — this fix must not regress that.
        [TestMethod]
        public async Task DefaultModeEnumToStringStillMatchesNative()
        {
            await RunTest(@"
using System;
public enum Color { Red, Green, Blue }
public class Program
{
    public static void Main()
    {
        var color = Color.Green;
        Console.WriteLine(""concat="" + color);
        Console.WriteLine(""explicit="" + color.ToString());
        Console.WriteLine($""interp={color}"");

        var day = DayOfWeek.Wednesday;
        Console.WriteLine(""day="" + day);

        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- [Convention(Notation.*)] — every notation on an [External] type's members ----
        //
        // Convention has a Notation axis (None / LowerCase / UpperCase / CamelCase / PascalCase) plus
        // Member / Target / Accessibility axes. Only CamelCase was tested; the other notations rename
        // members differently and were uncovered — same class of gap as ObjectCreateMode.

        [TestMethod]
        public void ConventionNotationModesRenameMembers()
        {
            var code = @"
using Transpose;
[External][Convention(Notation.CamelCase)]  public class CamC { public extern int SomevalOne { get; } public extern void DoThing(); }
[External][Convention(Notation.PascalCase)] public class PasC { public extern int somevalOne { get; } public extern void doThing(); }
[External][Convention(Notation.LowerCase)]  public class LowC { public extern int SomeValOne { get; } public extern void DoThing(); }
[External][Convention(Notation.UpperCase)]  public class UppC { public extern int SomeValOne { get; } public extern void DoThing(); }
public class Program
{
    public static void Main()
    {
        CamC a = null; PasC b = null; LowC c = null; UppC d = null;
        Script.Write(""x({0})"", a.SomevalOne); a.DoThing();
        Script.Write(""x({0})"", b.somevalOne); b.doThing();
        Script.Write(""x({0})"", c.SomeValOne); c.DoThing();
        Script.Write(""x({0})"", d.SomeValOne); d.DoThing();
    }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;
            Assert.IsTrue(js.Contains("a.somevalOne") && js.Contains("a.doThing("), "CamelCase\n" + js);
            Assert.IsTrue(js.Contains("b.SomevalOne") && js.Contains("b.DoThing("), "PascalCase\n" + js);
            Assert.IsTrue(js.Contains("c.somevalone") && js.Contains("c.dothing("), "LowerCase\n" + js);
            Assert.IsTrue(js.Contains("d.SOMEVALONE") && js.Contains("d.DOTHING("), "UpperCase\n" + js);
        }

        // ---- [Template] Fn — a method group of a templated method uses the Fn (delegate) form ----
        //
        // [Template(Fn = "...")] gives the delegate form to use when the method is referenced as a
        // method group rather than invoked. The emitter ignored Fn, so `Func<string> f = b.ToString`
        // emitted the native `(b).toString.bind(b)` — giving "true"/"false" (bool), the code number
        // (char) or crashing (double.GetHashCode: a JS number has no .getHashCode). It must resolve to
        // the Fn (System.Boolean.toString, String.fromCharCode, System.Nullable.toStringFn(…), …), with
        // the receiver bound as the function's first argument. Test source uses no Transpose-only
        // attributes, so native .NET is the oracle.

        [TestMethod]
        public async Task TemplateFnResolvesMethodGroupOfTemplatedMethod()
        {
            await RunTest(@"
using System;
public enum Color { Red = 1, Green = 5 }
public class Program
{
    public static void Main()
    {
        bool b = true;
        Func<string> fb = b.ToString;
        Console.WriteLine(fb());              // True (not native ""true"")

        char c = 'X';
        Func<string> fc = c.ToString;
        Console.WriteLine(fc());              // X (not the code number ""88"")

        int? n = 42;
        Func<string> fn = n.ToString;
        Console.WriteLine(fn());              // 42

        Color? col = Color.Green;
        Func<string> fe = col.ToString;
        Console.WriteLine(fe());              // Green (enum name via {T:ToString})

        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- char.IsUpper / char.IsLower as a method group ------------------------------------
        //
        // char.IsUpper(char)/IsLower(char) have a plain [Template] (no Fn), so a method group of
        // either fell through EmitMethodGroup's Fn check into the generic static-member-access path,
        // which just name-mangles the method to "isUpper"/"isLower". char.IsLower has no backing
        // runtime function under that name at all (silently always-truthy predicate — every non-'\0'
        // char code is truthy, so password.Any(char.IsLower) always returned true), and char.IsUpper
        // collided with the UNRELATED (string, int) overload's runtime function
        // (System.Char.isUpper(s, index)), which read `index` as the char code and threw
        // ArgumentOutOfRangeException. Fixed by giving both a Fn pointing at the already
        // correctly-shaped (char) -> bool runtime helper (Transpose.isUpper / Transpose.isLower).
        [TestMethod]
        public async Task TemplateFnResolvesCharIsUpperIsLowerMethodGroup()
        {
            await RunTest(@"
using System;
using System.Linq;
public class Program
{
    public static void Main()
    {
        Console.WriteLine(""abc123"".Any(char.IsUpper));   // False
        Console.WriteLine(""abc123"".Any(char.IsLower));   // True
        Console.WriteLine(""ABC123"".Any(char.IsLower));   // False
        Console.WriteLine(""ABC123"".Any(char.IsUpper));   // True

        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- a wider sweep of the same [Template]-without-Fn method-group bug -----------------
        //
        // The IsUpper/IsLower fix above was one instance of a general shape: a BCL method with a
        // per-call-site [Template] (no Fn) shares its bare mangled JS name with a DIFFERENT
        // overload's real backing function (or, for Double/Single/StringBuilder below, with a
        // differently-shaped hand-written runtime helper). A method-group reference to the
        // templated overload falls through to that unrelated bare name and misbehaves — silently
        // wrong results (Double.ToString(string) picked up the (int radix) overload's native
        // `.toString(radix)` and threw a RangeError on a non-numeric radix; StringBuilder.Append(char)
        // pushed the raw char CODE into the generic buffer instead of the character; String.LastIndexOf(char)
        // searched for the wrong thing entirely). Found by auditing every [External] BCL type for
        // overloads whose mangled JS names collide, and fixed the same way: give the templated
        // overload a Fn (an already-shaped real function, or a small inline wrapper) so a method
        // group resolves correctly instead of falling back to the bare name.
        [TestMethod]
        public async Task TemplateFnResolvesWiderMethodGroupCollisionSweep()
        {
            await RunTest(@"
using System;
using System.Text;
public class Program
{
    public static void Main()
    {
        double dd = 3.14159;
        Func<string, string> fd = dd.ToString;
        Console.WriteLine(fd(""F2""));                     // 3.14

        float ff = 3.14159f;
        Func<IFormatProvider, string> ff2 = ff.ToString;
        Console.WriteLine(ff2(null));                     // 3.14159

        string s = ""hello world"";
        Func<char, int> lif = s.LastIndexOf;
        Console.WriteLine(lif('o'));                      // 7

        var sb = new StringBuilder();
        Func<char, StringBuilder> app = sb.Append;
        app('X'); app('Y');
        Console.WriteLine(sb.ToString());                 // XY

        var sb2 = new StringBuilder(""a-b"");
        Func<int, long, StringBuilder> ins = sb2.Insert;
        ins(1, 42L);
        Console.WriteLine(sb2.ToString());                // a42-b

        var sb3 = new StringBuilder(""banana"");
        Func<char, char, StringBuilder> rep = sb3.Replace;
        rep('a', 'o');
        Console.WriteLine(sb3.ToString());                // bonono

        Func<string, int, bool> isLetter = char.IsLetter;
        Console.WriteLine(isLetter(""a1"", 0));            // True

        Func<char, bool> isWs = char.IsWhiteSpace;
        Console.WriteLine(isWs(' '));                     // True

        Func<char, bool> isLd = char.IsLetterOrDigit;
        Console.WriteLine(isLd('!'));                     // False

        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- more of the same method-group-vs-bare-name bug: Array/Decimal/Expression ----------
        //
        // Further confirmed instances of the exact same defect class as the fixes above — a
        // static-member-access fallback for a [Template]-without-Fn method colliding with an
        // unrelated overload's real bare name. Array.Reverse(T[]) and Decimal.Floor/Equals(decimal,...)
        // fell back to a nonexistent bare name and threw "not a function"; Decimal.Round(decimal,int)
        // fell back to the 0-arg instance Round()'s bare name and silently dropped the digit count
        // (returned 4, not 3.14). Fixed the same way: each templated overload now carries a Fn
        // pointing at the already-shaped real runtime function (Array.reverse, Decimal.round/
        // toDecimalPlaces) or a small inline wrapper (Decimal.Floor/Equals, Expression.Add).

        [TestMethod]
        public async Task TemplateFnResolvesArrayReverseMethodGroup()
        {
            await RunTest(@"
using System;
public class Program
{
    public static void Main()
    {
        Action<int[]> f = Array.Reverse;
        var arr = new[] { 1, 2, 3 };
        f(arr);
        Console.WriteLine(string.Join("","", arr));
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public async Task TemplateFnResolvesDecimalFloorMethodGroup()
        {
            await RunTest(@"
using System;
public class Program
{
    public static void Main()
    {
        Func<decimal, decimal> f = decimal.Floor;
        Console.WriteLine(f(3.7m));
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public async Task TemplateFnResolvesDecimalRoundMethodGroup()
        {
            await RunTest(@"
using System;
public class Program
{
    public static void Main()
    {
        Func<decimal, int, decimal> f = decimal.Round;
        Console.WriteLine(f(3.14159m, 2));
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public async Task TemplateFnResolvesDecimalEqualsMethodGroup()
        {
            await RunTest(@"
using System;
public class Program
{
    public static void Main()
    {
        Func<decimal, decimal, bool> f = decimal.Equals;
        Console.WriteLine(f(3.14m, 3.14m));
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // Expression.Add — three overloads, direct call AND method group, compared via == rather than
        // printed (concatenating an Expression's NodeType/Type into a string is a separate,
        // pre-existing crash — "Cannot read properties of undefined (reading 'ExpressionType')" — that
        // reproduces even on long-working factories like Expression.Assign, unrelated to this bug and
        // out of scope here), and uses a source-declared method (Reflectable) rather than a BCL one,
        // since [External] types without [Reflectable] (e.g. Math) surface no methods via
        // GetMethod/GetMethods (also a separate, much larger gap: [External] types are unconditionally
        // excluded from reflection metadata regardless of [Reflectable] — Emitter.Reflection.IsReflectableType
        // checks IsExternalType first — so fixing it would mean changing that policy for every external
        // BCL/DOM type, not just Math).
        //
        // The 2-arg Add(left,right) and the 3-arg Add(left,right,method) with a NULL method were
        // BOTH actually broken before this fix (not just as a method group) — Add(left,right) had no
        // [Template] at all and, since Expression carries [Name("System.Object")] for its runtime VALUE
        // representation (nodes are plain object literals), naming-convention resolution misused that
        // same [Name] for the static-member-ACCESS path too and produced "System.Object.add is not a
        // function"; Add(left,right,method) unconditionally read {method}.rt, crashing on a null method
        // instead of behaving like the 2-arg overload (native treats a null method as "infer the type from
        // the operands"). Both now have a template (and a matching Fn for the method-group path) that
        // infers the type from the right operand when there is no method, matching the existing
        // Expression.MakeBinary/Assign convention in this file.
        [TestMethod]
        public async Task TemplateFnResolvesExpressionAddMethodGroup()
        {
            await RunTest(@"
using System;
using System.Linq.Expressions;
using System.Reflection;
public class Helper
{
    public static int CustomAdd(int a, int b) => a + b + 100;
}
public class Program
{
    public static void Main()
    {
        var e2 = Expression.Add(Expression.Constant(1), Expression.Constant(2));
        Console.WriteLine(e2.NodeType == ExpressionType.Add);
        Console.WriteLine(e2.Type == typeof(int));
        Console.WriteLine(e2.Method == null);

        var e3null = Expression.Add(Expression.Constant(1), Expression.Constant(2), (MethodInfo)null);
        Console.WriteLine(e3null.NodeType == ExpressionType.Add);
        Console.WriteLine(e3null.Type == typeof(int));
        Console.WriteLine(e3null.Method == null);

        MethodInfo m = typeof(Helper).GetMethod(""CustomAdd"");
        var direct = Expression.Add(Expression.Constant(1), Expression.Constant(2), m);
        Func<Expression, Expression, MethodInfo, BinaryExpression> f = Expression.Add;
        var viaGroup = f(Expression.Constant(1), Expression.Constant(2), m);
        Console.WriteLine(direct.NodeType == viaGroup.NodeType);
        Console.WriteLine(direct.Method == viaGroup.Method);
        Console.WriteLine(direct.Type == viaGroup.Type);
        Console.WriteLine(((ConstantExpression)direct.Left).Value + "" "" + ((ConstantExpression)viaGroup.Left).Value);
        Console.WriteLine(((ConstantExpression)direct.Right).Value + "" "" + ((ConstantExpression)viaGroup.Right).Value);

        Func<Expression, Expression, BinaryExpression> f2 = Expression.Add;
        var viaGroup2 = f2(Expression.Constant(1), Expression.Constant(2));
        Console.WriteLine(viaGroup2.NodeType == ExpressionType.Add);
        Console.WriteLine(viaGroup2.Type == typeof(int));

        Func<Expression, Expression, MethodInfo, BinaryExpression> f3null = Expression.Add;
        var viaGroup3null = f3null(Expression.Constant(1), Expression.Constant(2), null);
        Console.WriteLine(viaGroup3null.NodeType == ExpressionType.Add);
        Console.WriteLine(viaGroup3null.Type == typeof(int));
        Console.WriteLine(viaGroup3null.Method == null);

        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- [Template] 2-arg (format, nonExpandedFormat) — the non-expanded params variant ----
        //
        // A 2-arg [Template] gives a second template for when the trailing `params` argument is supplied
        // NON-expanded (a single array passed directly) rather than as individual elements. The emitter
        // only ever used the first template. It now routes a non-expanded params call to the second:
        //   Activator.CreateInstance(type, argsArray) → Transpose.Reflection.applyConstructor(type, …)
        //   MethodInfo.Invoke(obj, argsArray)         → midel(this,obj).apply(null, …)
        // while expanded (individual-element) calls keep the first template. Test source uses only
        // System.Reflection, so native .NET is the oracle.

        private const string NonExpandedParamsProgram = @"
using System;
using System.Reflection;
public class Foo { public int V; public Foo(int a, int b) { V = a + b; } public int Add(int a, int b) => a + b; }
public class Program
{
    public static void Main()
    {
        object[] ctorArgs = new object[] { 2, 3 };
        Console.WriteLine(((Foo)Activator.CreateInstance(typeof(Foo), ctorArgs)).V); // 5  (non-expanded array)
        Console.WriteLine(((Foo)Activator.CreateInstance(typeof(Foo), 4, 5)).V);     // 9  (expanded)

        var m = typeof(Foo).GetMethod(""Add"");
        var f = new Foo(0, 0);
        object[] callArgs = new object[] { 10, 20 };
        Console.WriteLine(m.Invoke(f, callArgs));   // 30 (non-expanded array)
        Console.WriteLine(m.Invoke(f, 3, 4));       // 7  (expanded)
        Console.WriteLine(""<<DONE>>"");
    }
}";

        [TestMethod]
        public async Task TemplateNonExpandedParamsVariantMatchesNative()
        {
            // Native oracle uses only calls that also bind in real .NET: Activator.CreateInstance has a
            // (Type, params object[]) overload (both array and expanded forms compile), and
            // MethodInfo.Invoke(obj, object[]) takes the array form. (The expanded Invoke(obj, a, b) is a
            // Transpose BCL params overload with no .NET counterpart, so it is covered by the
            // translate-only assertion below, not here.)
            await RunTest(@"
using System;
using System.Reflection;
public class Foo { public int V; public Foo(int a, int b) { V = a + b; } public int Add(int a, int b) => a + b; }
public class Program
{
    public static void Main()
    {
        object[] ctorArgs = new object[] { 2, 3 };
        Console.WriteLine(((Foo)Activator.CreateInstance(typeof(Foo), ctorArgs)).V); // 5  (non-expanded array)
        Console.WriteLine(((Foo)Activator.CreateInstance(typeof(Foo), 4, 5)).V);     // 9  (expanded)

        var m = typeof(Foo).GetMethod(""Add"");
        var f = new Foo(0, 0);
        object[] callArgs = new object[] { 10, 20 };
        Console.WriteLine(m.Invoke(f, callArgs));   // 30 (non-expanded array)
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public void TemplateNonExpandedParamsSelectsSecondTemplate()
        {
            var result = new RoslynTranslator().Translate(NonExpandedParamsProgram);
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;
            // Non-expanded (single array) → the nonExpandedFormat variant.
            Assert.IsTrue(js.Contains("Transpose.Reflection.applyConstructor(Foo, [...ctorArgs])"),
                "a non-expanded Activator.CreateInstance must use applyConstructor\n" + js);
            Assert.IsTrue(js.Contains(".apply(null, [...callArgs])"),
                "a non-expanded MethodInfo.Invoke must use .apply\n" + js);
            // Expanded (individual elements) → the primary format.
            Assert.IsTrue(js.Contains("Transpose.createInstance(Foo, [4, 5])"),
                "an expanded Activator.CreateInstance must use createInstance with an array literal\n" + js);
            Assert.IsTrue(js.Contains("midel(f, f)(3, 4)") || js.Contains(")(3, 4)"),
                "an expanded MethodInfo.Invoke must spread the individual args\n" + js);
        }

        // ---- string.Join / string.Concat render members with .NET's ToString() -------------
        //
        // Reported as `string.Join("", name.Split(' ').Select(p => p[0]))` producing "97108107"
        // instead of "alk". The templates delegated to JavaScript's Array.prototype.join, which
        // stringifies with String(v) — but several runtime representations do not match .NET's
        // ToString(): a char is a bare code-point number, a bool gives "true"/"false" rather than
        // "True"/"False", an enum its ordinal rather than its name, and an object without a
        // ToString() override "[object Object]" rather than its type's full name. They now go
        // through System.String.join, which takes the {T:ToString} converter for the member type.

        [TestMethod]
        public async Task JoinAndConcatRenderMembersLikeNative()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;
using System.Linq;
public enum Suit { Hearts, Spades }
public class Plain { }
public class Program
{
    public static void Main()
    {
        var name = ""ada lovelace  king"";
        Console.WriteLine(string.Join("""", name.Split(' ').Where(p => p.Length > 0).Select(p => p[0])).ToUpper());
        Console.WriteLine(string.Join(""-"", new List<char> { 'a', 'b', 'c' }));
        Console.WriteLine(string.Join("""", new char[] { 'x', 'y' }));
        Console.WriteLine(string.Concat(new List<char> { 'q', 'r' }));
        Console.WriteLine(string.Join("","", new List<bool> { true, false }));
        Console.WriteLine(string.Join("","", new List<Suit> { Suit.Hearts, Suit.Spades }));
        Console.WriteLine(string.Join("","", new List<int> { 1, 2 }));
        Console.WriteLine(string.Join("","", new List<string> { ""a"", null, ""b"" }));
        Console.WriteLine(string.Join(""|"", new object[] { 1, 'z', true, null, ""s"", Suit.Spades }));
        Console.WriteLine(string.Join(""|"", new object[] { new Plain() }));
        // A scattered params call: every element needs its conversion, not just the first —
        // `string.Join(""|"", 'p', 'q')` used to emit [TransposeR.boxChar(112), 113] → ""p|113"".
        Console.WriteLine(string.Join(""|"", 'p', 'q'));
        Console.WriteLine(string.Join(""|"", new[] { ""a"", ""b"", ""c"", ""d"" }, 1, 2));
        // T unbound at emit time: the converter is chosen at runtime from the threaded type argument.
        Console.WriteLine(Describe(new[] { 'g', 'h' }));
        Console.WriteLine(Describe(new[] { Suit.Spades, Suit.Hearts }));
        Console.WriteLine(Describe(new[] { 1, 2 }));
        try { Console.WriteLine(string.Join(""|"", (IEnumerable<char>)null)); }
        catch (ArgumentNullException) { Console.WriteLine(""ArgumentNullException""); }
        Console.WriteLine(""<<DONE>>"");
    }

    static string Describe<T>(IEnumerable<T> items) => string.Join(""|"", items);
}", waitForOutput: "<<DONE>>");
        }

        // ---- a T-typed value renders with .NET's ToString() ---------------------------------
        //
        // Same root cause as the Join bug above, on the other three paths that stringify a value:
        // `v.ToString()`, `"" + v` and `$"{v}"` each have an explicit char branch and enum branch,
        // neither of which can fire when the static type is a type parameter — so an enum printed its
        // ordinal and a char its code point inside a generic method, while a concrete call site printed
        // the name and the character. The type argument IS threaded at runtime, so the converter is
        // now chosen from it.

        [TestMethod]
        public async Task GenericToStringRendersEnumAndCharLikeNative()
        {
            await RunTest(@"
using System;
using System.Linq;
public enum Colour { Red, Green }
public class Program
{
    public static void Main()
    {
        var arr = new[] { Colour.Green, Colour.Red };
        Console.WriteLine(arr[0].ToString());                                   // concrete enum
        Console.WriteLine(string.Join("","", arr.Select(x => x.ToString())));
        Console.WriteLine(Show(arr[0]));                                        // T.ToString()
        Console.WriteLine(string.Join("","", arr.Select(Show)));
        Console.WriteLine(Show('x') + Show(true) + Show(3) + Show(""s""));
        Console.WriteLine(Concat(arr[0]) + Concat('x') + Concat(true));         // """" + t
        Console.WriteLine(Interp(arr[0]) + Interp('x') + Interp(true));         // $""{t}""
        Console.WriteLine(""<<DONE>>"");
    }
    static string Show<T>(T v) => v.ToString();
    static string Concat<T>(T v) => """" + v;
    static string Interp<T>(T v) => $""{v}"";
}", waitForOutput: "<<DONE>>");
        }

        // ---- a method group of a generic method binds its type argument ---------------------
        //
        // A generic method that threads its type arguments takes them as LEADING parameters. A method
        // group did not bind them, so the delegate's first VALUE argument landed in the T slot:
        // `new[]{1,2}.Select(Show)` called Show(1, undefined) — typeof(T).Name read "Number" and the
        // real parameter was undefined. An explicitly instantiated group (`Func<int> f = Def<int>;`)
        // did not translate at all ("not supported: GenericName").

        [TestMethod]
        public async Task GenericMethodGroupBindsItsTypeArgument()
        {
            await RunTest(@"
using System;
using System.Linq;
public enum Colour { Red, Green }
public class Box
{
    private readonly int _n;
    public Box(int n) { _n = n; }
    public string Tag<T>(T v) => _n + typeof(T).Name + v;
}
public class Program
{
    public static void Main()
    {
        Func<int, string> f = Show;                                   // implicitly instantiated group
        Console.WriteLine(f(3) + ""/"" + Show(3));
        Console.WriteLine(string.Join("","", new[] { 1, 2 }.Select(Show)));
        Console.WriteLine(string.Join("","", new[] { 1, 2 }.Select(x => Show(x))));
        Func<Colour> g = Def<Colour>;                                 // explicitly instantiated group
        Console.WriteLine(Def<Colour>() + ""/"" + g());
        var inst = new Box(7);
        Func<int, string> h = inst.Tag;                               // instance generic method group
        Console.WriteLine(h(1) + ""/"" + inst.Tag(2));
        Console.WriteLine(string.Join("","", new[] { Colour.Green }.Select(Show)));
        Console.WriteLine(""<<DONE>>"");
    }
    static string Show<T>(T v) => ""<"" + typeof(T).Name + "":"" + v + "">"";
    static T Def<T>() => default(T);
}", waitForOutput: "<<DONE>>");
        }

        // ---- a plain struct has value equality and value hashing ----------------------------
        //
        // .NET gives every value type field-wise Equals/GetHashCode via ValueType, which is what makes
        // a struct usable as a Dictionary/HashSet key. Only records synthesized them, so a plain struct
        // fell back to JS reference identity: `d[new Key { A = 1 }]` threw KeyNotFoundException for a
        // key that had just been added. A null member also has to hash as 0 rather than throwing
        // "HashCode cannot be calculated for empty value".

        [TestMethod]
        public async Task StructsHaveValueEqualityAndHashing()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;
public struct Key2 { public int A; public string B; }
public struct Key1 { public int A; }
public struct Nested { public Key2 K; public double D; }
public struct Own { public int A; public override bool Equals(object o) => true; public override int GetHashCode() => 42; }
public record struct KeyR(int A, string B);
public class RefKey
{
    public string Name;
    public override bool Equals(object o) => o is RefKey r && r.Name == Name;
    public override int GetHashCode() => Name == null ? 0 : Name.GetHashCode();
}
public class Program
{
    public static void Main()
    {
        var a = new Key2 { A = 1, B = ""b"" };
        var b = new Key2 { A = 1, B = ""b"" };
        Console.WriteLine(a.Equals(b) + ""/"" + (a.GetHashCode() == b.GetHashCode()));
        Console.WriteLine(new Dictionary<Key2, string> { { a, ""s"" } }.ContainsKey(b));
        Console.WriteLine(new Dictionary<Key1, string> { { new Key1 { A = 1 }, ""s"" } }.ContainsKey(new Key1 { A = 1 }));
        Console.WriteLine(new Dictionary<KeyR, string> { { new KeyR(1, ""b""), ""s"" } }.ContainsKey(new KeyR(1, ""b"")));
        Console.WriteLine(new Dictionary<RefKey, string> { { new RefKey { Name = ""n"" }, ""r"" } }.ContainsKey(new RefKey { Name = ""n"" }));
        Console.WriteLine(new Dictionary<(int, string), string> { { (1, ""a""), ""t"" } }.ContainsKey((1, ""a"")));
        Console.WriteLine(new HashSet<Key2> { a }.Contains(b));

        // a null member hashes as 0 rather than throwing
        var d = new Dictionary<Key2, string>();
        d[default(Key2)] = ""def"";
        Console.WriteLine(d[new Key2 { A = 0, B = null }]);
        Console.WriteLine(default(Key2).GetHashCode() == new Key2().GetHashCode());
        Console.WriteLine(default(Key2).Equals(new Key2 { A = 0 }) + ""/"" + new Key2 { A = 1 }.Equals(new Key2 { A = 2 }));

        // nested struct members compare by value too
        var dn = new Dictionary<Nested, int> { { new Nested { K = new Key2 { A = 1 }, D = 1.5 }, 9 } };
        Console.WriteLine(dn[new Nested { K = new Key2 { A = 1 }, D = 1.5 }]);

        // a non-struct argument, and a boxed struct
        Console.WriteLine(new Key2 { A = 1 }.Equals(""not a struct""));
        object boxed = new Key2 { A = 3 };
        Console.WriteLine(boxed.Equals(new Key2 { A = 3 }));
        Console.WriteLine(new HashSet<Key2> { default, new Key2 { A = 1 }, default }.Count);

        // a struct that declares its own members keeps them
        Console.WriteLine(new Own { A = 1 }.Equals(new Own { A = 2 }) + ""/"" + new Own { A = 1 }.GetHashCode());
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- extension methods and a dynamic receiver ---------------------------

        /// <summary>
        /// Pins .NET's rule that an extension method is NOT offered on a <c>dynamic</c> receiver, which
        /// is what the Curiosity Monaco providers hit: they take <c>dynamic model</c>, so an invocation
        /// forwarding it is dynamic-typed and <c>task.AsPromise()</c> on the result is a late-bound
        /// member access that never binds to the extension. Both sides must agree: native .NET raises
        /// RuntimeBinderException, and the emitted JavaScript must likewise fail rather than silently
        /// do something else. Pinned because it reads like a compiler bug ("the extension call emitted
        /// as an instance call") and cost real time to diagnose once.
        ///
        /// The static-typed receiver in the same snippet is the control: it must bind normally and emit
        /// the unreduced static call.
        /// </summary>
        [TestMethod]
        public async Task ExtensionMethodIsNotOfferedOnADynamicReceiver()
        {
            await RunTest(@"
using System;
using System.Threading.Tasks;

namespace Ns.One
{
    public static class NsExt
    {
        public static string Tag(this Task t) => ""tagged"";
    }

    public class Caller
    {
        private static async Task<object> WorkAsync(dynamic model) => model;

        // Dynamic-typed invocation: `.Tag()` is late-bound and does not see the extension.
        public static string ViaDynamic(dynamic model) => WorkAsync(model).Tag();

        // Control: the same value pinned to a static type first.
        public static string ViaStaticType(dynamic model)
        {
            Task<object> t = WorkAsync(model);
            return t.Tag();
        }
    }
}

public class Program
{
    public static void Main()
    {
        Console.WriteLine(""static-typed: "" + Ns.One.Caller.ViaStaticType(1));

        try
        {
            Console.WriteLine(""dynamic: "" + Ns.One.Caller.ViaDynamic(1));
        }
        catch (Exception)
        {
            // The exception TYPE differs by platform (RuntimeBinderException vs a JS TypeError), so
            // only the fact that it fails is comparable.
            Console.WriteLine(""dynamic: threw"");
        }

        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }
    }
}
