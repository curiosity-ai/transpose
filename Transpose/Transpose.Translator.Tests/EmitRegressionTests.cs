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
    }
}
