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
            // Predeclared outside a loop → `var` (matching regular locals, so it coexists with any
            // same-named function-scoped local).
            Assert.IsTrue(result.Javascript!.Contains("var s;"),
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
    }
}
