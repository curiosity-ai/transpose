using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// A value thrown by JavaScript rather than by C# — the <c>TypeError</c> a null dereference raises, a
    /// <c>RangeError</c>, an <c>Error</c> from a library, a rejected promise, a bare string — reaching a C#
    /// <c>catch</c>.
    /// <para>
    /// The emitter used to hand the raw value straight to the clauses, so no typed clause could match it:
    /// <c>catch (NullReferenceException)</c> never caught a null dereference, and <c>catch (Exception e)</c>
    /// gave the body a JS error whose <c>GetType().Name</c> was "TypeError". Every <c>catch</c> now passes
    /// the value through the runtime's <c>System.Exception.create</c>, which maps it onto the .NET exception
    /// it corresponds to and hands a real <c>System.Exception</c> back unchanged:
    /// </para>
    /// <list type="table">
    /// <item><term><c>TypeError</c></term><description><c>NullReferenceException</c></description></item>
    /// <item><term><c>RangeError</c></term><description><c>ArgumentOutOfRangeException</c></description></item>
    /// <item><term>any other <c>Error</c></term><description><c>SystemException</c></description></item>
    /// <item><term>anything else</term><description><c>Exception</c>, with the value's message or text</description></item>
    /// </list>
    /// <para>
    /// The first group runs against native .NET: the constructs raise the same exception there, so the
    /// output has to match line for line (only type names are printed, since .NET and V8 word their
    /// messages differently). The second group throws from hand-written JavaScript through <c>[Script]</c>,
    /// which has no native counterpart, and asserts on the translated output alone.
    /// </para>
    /// </summary>
    [TestClass]
    public class JavaScriptErrorCatchTests : TranslatorTestBase
    {
        // ---- the same program on .NET and in JavaScript --------------------------------------------

        /// <summary>Each shape of null dereference is caught by a typed <c>NullReferenceException</c>
        /// clause — a property, a method call, a field, an element of a null array, a string member.</summary>
        [TestMethod]
        public async Task NullDereferenceIsCaughtAsNullReferenceException()
        {
            await RunTest(@"
using System;

public class Node { public Node Next; public int Value; public int Twice() => Value * 2; }

public class Program
{
    static void Try(string label, Action action)
    {
        try { action(); Console.WriteLine(label + "": no exception""); }
        catch (NullReferenceException) { Console.WriteLine(label + "": NullReferenceException""); }
    }

    public static void Main()
    {
        Node node = null;
        string text = null;
        int[] numbers = null;

        Try(""field"", () => Console.WriteLine(node.Value));
        Try(""method"", () => Console.WriteLine(node.Twice()));
        Try(""chain"", () => Console.WriteLine(new Node().Next.Value));
        Try(""string"", () => Console.WriteLine(text.Length));
        Try(""string method"", () => Console.WriteLine(text.ToUpper()));
        Try(""array element"", () => Console.WriteLine(numbers[0]));
    }
}");
        }

        /// <summary>The mapped exception has the right type everywhere a type is asked about: its
        /// name, <c>is</c>, a base-class clause, an exception filter, and its <c>ToString</c>.</summary>
        [TestMethod]
        public async Task MappedExceptionHasTheDotNetTypeEverywhere()
        {
            await RunTest(@"
using System;

public class Program
{
    public static void Main()
    {
        string text = null;

        try { Console.WriteLine(text.Length); }
        catch (Exception e)
        {
            Console.WriteLine(e.GetType().Name);
            Console.WriteLine(e.GetType().FullName);
            Console.WriteLine(e is NullReferenceException);
            Console.WriteLine(e is SystemException);
            Console.WriteLine(e.InnerException == null);
            Console.WriteLine(e.Message.Length > 0);
            Console.WriteLine(e.ToString().StartsWith(""System.NullReferenceException""));
        }

        try { Console.WriteLine(text.Length); }
        catch (SystemException) { Console.WriteLine(""caught as SystemException""); }

        try { Console.WriteLine(text.Length); }
        catch (Exception e) when (e is NullReferenceException) { Console.WriteLine(""filter saw NullReferenceException""); }

        try { Console.WriteLine(text.Length); }
        catch (Exception e) when (e is ArgumentException) { Console.WriteLine(""wrong filter""); }
        catch (Exception e) { Console.WriteLine(""second clause: "" + e.GetType().Name); }
    }
}");
        }

        /// <summary>The first matching clause wins, in declaration order, exactly as for a C# throw.</summary>
        [TestMethod]
        public async Task ClauseOrderIsRespected()
        {
            await RunTest(@"
using System;

public class Program
{
    static string Classify(Action action)
    {
        try { action(); return ""none""; }
        catch (ArgumentException) { return ""ArgumentException""; }
        catch (NullReferenceException) { return ""NullReferenceException""; }
        catch (IndexOutOfRangeException) { return ""IndexOutOfRangeException""; }
        catch (Exception e) { return ""Exception: "" + e.GetType().Name; }
    }

    public static void Main()
    {
        string text = null;
        int[] numbers = new int[1];
        int zero = 0;
        Console.WriteLine(Classify(() => Console.WriteLine(text.Length)));
        Console.WriteLine(Classify(() => Console.WriteLine(numbers[3])));
        Console.WriteLine(Classify(() => Console.WriteLine(1 / zero)));
        Console.WriteLine(Classify(() => throw new ArgumentNullException(""x"")));
        Console.WriteLine(Classify(() => { }));
    }
}");
        }

        /// <summary>A clause that does not match rethrows, and the next enclosing <c>try</c> sees the
        /// mapped exception — not the raw JavaScript value a second time.</summary>
        [TestMethod]
        public async Task UnmatchedClauseRethrowsToTheEnclosingTry()
        {
            await RunTest(@"
using System;

public class Program
{
    static void Inner()
    {
        string text = null;
        try { Console.WriteLine(text.Length); }
        catch (ArgumentException) { Console.WriteLine(""inner: wrong clause""); }
        finally { Console.WriteLine(""inner finally""); }
    }

    public static void Main()
    {
        try { Inner(); }
        catch (NullReferenceException e) { Console.WriteLine(""outer: "" + e.GetType().Name); }
    }
}");
        }

        /// <summary><c>throw;</c> and <c>throw e;</c> hand the enclosing <c>try</c> the same exception
        /// object the inner clause caught.</summary>
        [TestMethod]
        public async Task RethrowKeepsTheSameExceptionObject()
        {
            await RunTest(@"
using System;

public class Program
{
    public static void Main()
    {
        string text = null;
        Exception first = null;

        try
        {
            try { Console.WriteLine(text.Length); }
            catch (NullReferenceException e) { first = e; throw; }
        }
        catch (Exception e) { Console.WriteLine(""throw; same object: "" + ReferenceEquals(first, e)); }

        try
        {
            try { Console.WriteLine(text.Length); }
            catch (NullReferenceException e) { first = e; throw e; }
        }
        catch (Exception e) { Console.WriteLine(""throw e; same object: "" + ReferenceEquals(first, e)); }
    }
}");
        }

        /// <summary>An exception thrown by C# is untouched: the clause receives the very object that was
        /// thrown, with its own type, message and data.</summary>
        [TestMethod]
        public async Task CSharpExceptionsAreNotRewrapped()
        {
            await RunTest(@"
using System;

public class MyError : Exception { public int Code; public MyError(string m, int code) : base(m) { Code = code; } }

public class Program
{
    public static void Main()
    {
        var thrown = new MyError(""custom"", 7);
        thrown.Data[""k""] = ""v"";
        try { throw thrown; }
        catch (Exception e)
        {
            Console.WriteLine(ReferenceEquals(thrown, e));
            Console.WriteLine(e.GetType().Name + "" "" + e.Message + "" "" + ((MyError)e).Code + "" "" + e.Data[""k""]);
        }

        try { throw new InvalidOperationException(""plain""); }
        catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
    }
}");
        }

        /// <summary>A catch-all without a declaration, and a typed clause without a variable, both still
        /// work — the mapping does not depend on a variable being bound.</summary>
        [TestMethod]
        public async Task ClausesWithoutAVariable()
        {
            await RunTest(@"
using System;

public class Program
{
    public static void Main()
    {
        string text = null;
        try { Console.WriteLine(text.Length); }
        catch (NullReferenceException) { Console.WriteLine(""typed, no variable""); }

        try { Console.WriteLine(text.Length); }
        catch { Console.WriteLine(""bare catch""); }
    }
}");
        }

        /// <summary>A null dereference inside a <c>catch</c> body is its own exception, caught by a
        /// <c>try</c> nested in that body; the outer variable is unaffected. An exception thrown from a
        /// catch keeps the mapped one as its <c>InnerException</c>.</summary>
        [TestMethod]
        public async Task NestedTryInsideCatchAndInnerException()
        {
            await RunTest(@"
using System;

public class Program
{
    public static void Main()
    {
        string text = null;
        int[] numbers = null;

        try { Console.WriteLine(text.Length); }
        catch (Exception outer)
        {
            try { Console.WriteLine(numbers[0]); }
            catch (Exception inner) { Console.WriteLine(""inner: "" + inner.GetType().Name + "", same: "" + ReferenceEquals(inner, outer)); }
            Console.WriteLine(""outer: "" + outer.GetType().Name);
        }

        try
        {
            try { Console.WriteLine(text.Length); }
            catch (NullReferenceException e) { throw new InvalidOperationException(""wrapped"", e); }
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine(e.Message + "" <- "" + e.InnerException.GetType().Name);
        }
    }
}");
        }

        /// <summary><c>finally</c> and <c>using</c> still run on the way out of a JavaScript error, and
        /// the error still reaches the outer clause as the mapped exception.</summary>
        [TestMethod]
        public async Task FinallyAndDisposeRunForAJavaScriptError()
        {
            await RunTest(@"
using System;

public class Resource : IDisposable { public void Dispose() => Console.WriteLine(""disposed""); }

public class Program
{
    public static void Main()
    {
        string text = null;
        try
        {
            using (new Resource())
            {
                try { Console.WriteLine(text.Length); }
                finally { Console.WriteLine(""finally""); }
            }
        }
        catch (NullReferenceException) { Console.WriteLine(""caught""); }
    }
}");
        }

        /// <summary>The error is raised somewhere a C# frame does not directly contain it: inside a lambda
        /// run by LINQ, inside an iterator being enumerated, and in a static constructor.</summary>
        [TestMethod]
        public async Task ErrorRaisedInLambdasIteratorsAndDeferredCode()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;
using System.Linq;

public class Item { public string Name; }

public class Program
{
    static IEnumerable<int> Lengths(IEnumerable<Item> items)
    {
        foreach (var item in items) yield return item.Name.Length;
    }

    public static void Main()
    {
        var items = new[] { new Item { Name = ""a"" }, new Item(), new Item { Name = ""ccc"" } };

        try { Console.WriteLine(items.Select(i => i.Name.Length).Sum()); }
        catch (NullReferenceException) { Console.WriteLine(""linq lambda: NullReferenceException""); }

        var seen = new List<int>();
        try { foreach (var n in Lengths(items)) seen.Add(n); }
        catch (NullReferenceException) { Console.WriteLine(""iterator: NullReferenceException after "" + seen.Count); }

        try { Console.WriteLine(items.OrderBy(i => i.Name.Length).First().Name); }
        catch (NullReferenceException) { Console.WriteLine(""key selector: NullReferenceException""); }
    }
}");
        }

        /// <summary>A null dereference in an async method, before and after an <c>await</c>, reaches the
        /// awaiting caller's typed clause; one inside the awaiting method's own <c>try</c> is caught
        /// there.</summary>
        [TestMethod]
        public async Task ErrorAcrossAwaitIsCaughtAsNullReferenceException()
        {
            await RunTest(@"
using System;
using System.Threading.Tasks;

public class Program
{
    static async Task<int> BeforeAwait(string s) { var n = s.Length; await Task.Delay(1); return n; }
    static async Task<int> AfterAwait(string s) { await Task.Delay(1); return s.Length; }

    public static async Task Main()
    {
        try { await BeforeAwait(null); }
        catch (NullReferenceException) { Console.WriteLine(""before await: NullReferenceException""); }

        try { await AfterAwait(null); }
        catch (NullReferenceException) { Console.WriteLine(""after await: NullReferenceException""); }

        try
        {
            await Task.Delay(1);
            string s = null;
            Console.WriteLine(s.Length);
        }
        catch (NullReferenceException) { Console.WriteLine(""own try: NullReferenceException""); }

        try { await Task.Run(() => AfterAwait(null)); }
        catch (NullReferenceException) { Console.WriteLine(""Task.Run: NullReferenceException""); }
    }
}");
        }

        // ---- values thrown by hand-written JavaScript --------------------------------------------

        private const string Thrower = @"
using System;
using System.Threading.Tasks;
using Transpose;

public class Js
{
    [Script(""throw new TypeError('type boom');"")]              public static extern void TypeError();
    [Script(""throw new RangeError('range boom');"")]            public static extern void RangeError();
    [Script(""throw new Error('plain boom');"")]                 public static extern void Error();
    [Script(""throw new SyntaxError('syntax boom');"")]          public static extern void SyntaxError();
    [Script(""return notDefinedAnywhere + 1;"")]                 public static extern int ReferenceError();
    [Script(""throw 'string boom';"")]                           public static extern void String();
    [Script(""throw { message: 'object boom', code: 3 };"")]     public static extern void ObjectWithMessage();
    [Script(""throw 42;"")]                                      public static extern void Number();
    [Script(""throw 0;"")]                                       public static extern void Zero();
    [Script(""throw null;"")]                                    public static extern void Null();
    [Script(""throw undefined;"")]                               public static extern void Undefined();
    [Script(""return (1).toFixed(500);"")]                       public static extern string NativeRangeError();
    [Script(""return Promise.reject(new TypeError('rejected'));"")] public static extern Task Rejected();
    [Script(""return Promise.reject('rejected string');"")]      public static extern Task RejectedString();
    [Script(""fn();"")]                                          public static extern void Call(Action fn);
}
";

        private static string Program(string main) => Thrower + @"
public class Program
{
    static void Show(string label, Action action)
    {
        try { action(); Console.WriteLine(label + ': no exception'); }
        catch (Exception e) { Console.WriteLine(label + ': ' + e.GetType().FullName + ' | ' + e.Message); }
    }
".Replace('\'', '"') + main + "\n}";

        /// <summary>Each kind of JavaScript value maps to the documented exception type, and keeps its
        /// message.</summary>
        [TestMethod]
        public async Task EachThrownValueMapsToItsExceptionType()
        {
            var js = await RunTest(Program(@"
    public static void Main()
    {
        Show(""TypeError"", Js.TypeError);
        Show(""RangeError"", Js.RangeError);
        Show(""native RangeError"", () => Js.NativeRangeError());
        Show(""Error"", Js.Error);
        Show(""SyntaxError"", Js.SyntaxError);
        Show(""ReferenceError"", () => Js.ReferenceError());
        Show(""string"", Js.String);
        Show(""object"", Js.ObjectWithMessage);
        Show(""number"", Js.Number);
        Show(""zero"", Js.Zero);
    }"), skipRoslyn: true);

            StringAssert.Contains(js, "TypeError: System.NullReferenceException | type boom");
            StringAssert.Contains(js, "RangeError: System.ArgumentOutOfRangeException | range boom");
            StringAssert.Contains(js, "native RangeError: System.ArgumentOutOfRangeException |");
            StringAssert.Contains(js, "Error: System.SystemException | plain boom");
            StringAssert.Contains(js, "SyntaxError: System.SystemException | syntax boom");
            StringAssert.Contains(js, "ReferenceError: System.SystemException | notDefinedAnywhere is not defined");
            StringAssert.Contains(js, "string: System.Exception | string boom");
            StringAssert.Contains(js, "object: System.Exception | object boom");
            StringAssert.Contains(js, "number: System.Exception | 42");
            StringAssert.Contains(js, "zero: System.Exception | 0");
        }

        /// <summary>A thrown null or undefined carries nothing: it becomes an <c>Exception</c> with the
        /// default message, and reading its <c>StackTrace</c> or <c>ToString()</c> does not throw.</summary>
        [TestMethod]
        public async Task ThrownNullAndUndefinedBecomeAPlainException()
        {
            var js = await RunTest(Program(@"
    public static void Main()
    {
        foreach (var (label, action) in new (string, Action)[] { (""null"", Js.Null), (""undefined"", Js.Undefined) })
        {
            try { action(); }
            catch (Exception e)
            {
                Console.WriteLine(label + "": "" + e.GetType().FullName + "" | "" + e.Message);
                Console.WriteLine(label + "" tostring: "" + e.ToString().StartsWith(""System.Exception""));
            }
        }
    }"), skipRoslyn: true);

            StringAssert.Contains(js, "null: System.Exception | Exception of type 'System.Exception' was thrown.");
            StringAssert.Contains(js, "undefined: System.Exception | Exception of type 'System.Exception' was thrown.");
            StringAssert.Contains(js, "null tostring: True");
            StringAssert.Contains(js, "undefined tostring: True");
        }

        /// <summary>Typed clauses, base-class clauses and filters all see the mapped type of a value
        /// thrown by JavaScript.</summary>
        [TestMethod]
        public async Task TypedClausesAndFiltersMatchJavaScriptErrors()
        {
            var js = await RunTest(Program(@"
    public static void Main()
    {
        try { Js.TypeError(); }
        catch (ArgumentException) { Console.WriteLine(""wrong""); }
        catch (NullReferenceException e) { Console.WriteLine(""typed: "" + e.Message); }

        try { Js.RangeError(); }
        catch (ArgumentException e) { Console.WriteLine(""base class: "" + e.GetType().Name); }

        try { Js.Error(); }
        catch (Exception e) when (e.Message.Contains(""plain"")) { Console.WriteLine(""filter: "" + e.GetType().Name); }

        try { Js.String(); }
        catch (SystemException) { Console.WriteLine(""wrong: a string is not a SystemException""); }
        catch (Exception e) { Console.WriteLine(""string falls through to Exception: "" + e.Message); }
    }"), skipRoslyn: true);

            StringAssert.Contains(js, "typed: type boom");
            StringAssert.Contains(js, "base class: ArgumentOutOfRangeException");
            StringAssert.Contains(js, "filter: SystemException");
            StringAssert.Contains(js, "string falls through to Exception: string boom");
            Assert.IsFalse(js.Contains("wrong"), js);
        }

        /// <summary>The wrapper keeps the original error as its stack source, so <c>StackTrace</c> is the
        /// native JavaScript stack.</summary>
        [TestMethod]
        public async Task MappedExceptionKeepsTheNativeStack()
        {
            var js = await RunTest(Program(@"
    public static void Main()
    {
        try { Js.TypeError(); }
        catch (NullReferenceException e) { Console.WriteLine(""stack: "" + e.StackTrace.Split('\n')[0]); }
    }"), skipRoslyn: true);

            StringAssert.Contains(js, "stack: TypeError: type boom");
        }

        /// <summary>A rejected promise that is awaited is caught like a thrown value: a rejected
        /// <c>TypeError</c> as <c>NullReferenceException</c>, a rejected string as <c>Exception</c>.</summary>
        [TestMethod]
        public async Task AwaitedRejectionIsMapped()
        {
            var js = await RunTest(Program(@"
    public static async Task Main()
    {
        try { await Js.Rejected(); }
        catch (NullReferenceException e) { Console.WriteLine(""rejected TypeError: "" + e.Message); }

        try { await Js.RejectedString(); }
        catch (Exception e) { Console.WriteLine(""rejected string: "" + e.GetType().FullName + "" | "" + e.Message); }
    }"), skipRoslyn: true);

            StringAssert.Contains(js, "rejected TypeError: rejected");
            StringAssert.Contains(js, "rejected string: System.Exception | rejected string");
        }

        /// <summary>A C# exception that crosses a JavaScript frame — thrown by a callback that
        /// hand-written JavaScript invoked — arrives as the same object, not a re-wrapped copy.</summary>
        [TestMethod]
        public async Task CSharpExceptionThroughAJavaScriptFrameKeepsItsIdentity()
        {
            var js = await RunTest(Program(@"
    public static void Main()
    {
        var thrown = new InvalidOperationException(""from callback"");
        try { Js.Call(() => throw thrown); }
        catch (InvalidOperationException e) { Console.WriteLine(""same object: "" + ReferenceEquals(thrown, e)); }
    }"), skipRoslyn: true);

            StringAssert.Contains(js, "same object: True");
        }

        /// <summary>A JavaScript error that no clause matches leaves the C# code as the mapped exception,
        /// so an outer C# <c>try</c> in a caller two frames up still catches it by type.</summary>
        [TestMethod]
        public async Task UnmatchedJavaScriptErrorPropagatesAsTheMappedException()
        {
            var js = await RunTest(Program(@"
    static void Inner()
    {
        try { Js.TypeError(); }
        catch (ArgumentException) { Console.WriteLine(""wrong""); }
    }

    static void Middle() { try { Inner(); } finally { Console.WriteLine(""middle finally""); } }

    public static void Main()
    {
        try { Middle(); }
        catch (NullReferenceException e) { Console.WriteLine(""outer: "" + e.Message); }
    }"), skipRoslyn: true);

            StringAssert.Contains(js, "middle finally");
            StringAssert.Contains(js, "outer: type boom");
            Assert.IsFalse(js.Contains("wrong"), js);
        }

        /// <summary>The emitted catch maps the value before any clause is tested — the shape the rest of
        /// this suite relies on.</summary>
        [TestMethod]
        public void CatchMapsTheValueBeforeTheClauses()
        {
            var result = new RoslynTranslator().Translate(@"
using System;
public class Program
{
    public static void Main()
    {
        try { Console.WriteLine(1); }
        catch (NullReferenceException e) { Console.WriteLine(e.Message); }
    }
}");
            Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
            var js = result.Javascript!;
            var map = js.IndexOf("$ex = System.Exception.create($ex);", System.StringComparison.Ordinal);
            var test = js.IndexOf("TransposeR.is($ex, System.NullReferenceException)", System.StringComparison.Ordinal);
            Assert.IsTrue(map >= 0 && test > map, js);
        }
    }
}
