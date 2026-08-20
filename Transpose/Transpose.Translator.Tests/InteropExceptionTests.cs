using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Exceptions crossing the JavaScript boundary — a <c>[Script]</c> body that throws, a rejected
    /// promise, a C# handler invoked from JS — which is where a diagnostic is easiest to lose,
    /// because the value the engine hands back is not a <c>System.Exception</c> and nothing about it
    /// is guaranteed: it may be an <c>Error</c>, a subclass of one from another realm, a string, or a
    /// DOM event with no message at all.
    ///
    /// There is no native .NET counterpart for any of this, so these assert on the emitted
    /// program's output directly (<c>skipRoslyn</c>) rather than diffing against Roslyn. Where a
    /// case has a pure-C# equivalent it lives in <see cref="AggregateExceptionTests"/> or
    /// <see cref="ExceptionControlFlowTests"/> instead, and is diffed there.
    /// <see cref="ExceptionStackTraceTests"/> covers the single-value normalisation itself; this
    /// file is about a JS failure travelling through the constructs a real application puts around
    /// it — several frames, a filter, a Task, a WhenAll.
    /// </summary>
    [TestClass]
    public class InteropExceptionTests : TranslatorTestBase
    {
        /// <summary>
        /// A throw at the bottom of C# → JS → C# → JS, and the handler at the top. The interesting
        /// half is the middle: a C# exception thrown by a callback the JS frame invoked has to travel
        /// out THROUGH that frame and arrive at the C# handler as itself — same type, same message —
        /// and a JS frame that catches it instead sees a System.Exception object - not a JS `Error`,
        /// so `instanceof Error` is false - which nonetheless carries `message`, the field every
        /// JavaScript handler reads.
        /// A bare rethrow of a JS error hands the ORIGINAL value back to JavaScript rather than a
        /// wrapper, which is what keeps its identity and its native frames usable by an outer JS
        /// handler.
        /// </summary>
        [TestMethod]
        public async Task AThrowSurvivesEveryHopBetweenCSharpAndJavaScriptAsync()
        {
            var js = await RunTest(@"
using System;
using Transpose;

public class Native
{
    [Script(""throw new Error('deep js failure');"")]
    public static extern void Boom();

    [Script(""return callback();"")]
    public static extern int Call(Func<int> callback);

    // What a JavaScript handler sees when the C# callback it invoked throws.
    [Script(""try { callback(); return 'no throw'; } catch (e) { return (e instanceof Error) + '/' + (e.message || e); }"")]
    public static extern string CallAndCatch(Action callback);

    // The JS frame does not catch: the C# exception has to pass through it untouched.
    [Script(""callback();"")]
    public static extern void CallAndPropagate(Action callback);
}

public class Program
{
    static int Middle() => Native.Call(() => { Native.Boom(); return 1; });

    public static void Main()
    {
        try { Middle(); }
        catch (Exception e) { Console.WriteLine(""js error through both frames: "" + e.Message); }

        Console.WriteLine(""js caught a C# exception: "" + Native.CallAndCatch(() => throw new InvalidOperationException(""from the C# callback"")));

        try { Native.CallAndPropagate(() => throw new FormatException(""through the JS frame"")); }
        catch (FormatException e) { Console.WriteLine(""round trip kept the type: "" + e.Message); }

        // `throw;` rethrows what was actually thrown, so the outer JS frame gets its own Error back.
        Console.WriteLine(""rethrown to JS: "" + Native.CallAndCatch(() =>
        {
            try { Native.Boom(); }
            catch (Exception) { throw; }
        }));
    }
}", skipRoslyn: true);

            StringAssert.Contains(js, "js error through both frames: deep js failure");
            StringAssert.Contains(js, "js caught a C# exception: false/from the C# callback",
                "a C# exception is not a JS Error - it is a System.Exception object - but it does carry "
                + "`message`, which is what a JavaScript handler reads");
            StringAssert.Contains(js, "round trip kept the type: through the JS frame",
                "a C# exception passing through a JavaScript frame must arrive as itself");
            StringAssert.Contains(js, "rethrown to JS: true/deep js failure",
                "a bare rethrow must hand JavaScript back the value that was thrown, not a wrapper");
        }

        /// <summary>
        /// A rejected promise. Awaiting one binds whatever JavaScript rejected with, and that value
        /// is normalised into a real <c>System.Exception</c> on the way in — the same normalisation
        /// <c>Task.Run</c> and a faulted task already applied, and the reason the handler can use
        /// <c>GetType()</c>, <c>ToString()</c> and the inner chain at all instead of holding a bare
        /// <c>Error</c> that only answers to <c>Message</c>. All four reason shapes a browser
        /// actually produces are covered: an <c>Error</c>, an engine error type, a string, and an
        /// event with no message.
        /// </summary>
        [TestMethod]
        public async Task AwaitingARejectedPromiseYieldsARealExceptionAsync()
        {
            var js = await RunTest(@"
using System;
using System.Threading.Tasks;
using Transpose;

public class Native
{
    [Script(""return Promise.reject(new Error('the promise rejected'));"")]
    public static extern Task Rejected();

    [Script(""return Promise.reject(new RangeError('out of range'));"")]
    public static extern Task RejectedWithEngineError();

    [Script(""return Promise.reject('a bare string reason');"")]
    public static extern Task RejectedWithAString();

    [Script(""return Promise.reject({ type: 'error', target: null });"")]
    public static extern Task RejectedWithAnEvent();

    [Script(""return Promise.reject(new Error('typed rejection'));"")]
    public static extern Task<int> RejectedTyped();
}

public class Program
{
    public static async Task Run()
    {
        try { await Native.Rejected(); }
        catch (Exception e)
        {
            Console.WriteLine(""error: "" + e.GetType().FullName + "" / "" + e.Message);
            Console.WriteLine(""error is an Exception: "" + (e is Exception) + "" stack: "" + (e.StackTrace != null));
            Console.WriteLine(""error tostring: "" + e.ToString().Contains(""the promise rejected""));
        }

        try { await Native.RejectedWithEngineError(); }
        catch (Exception e) { Console.WriteLine(""range: "" + e.GetType().FullName + "" / "" + e.Message); }

        try { await Native.RejectedWithAString(); }
        catch (Exception e) { Console.WriteLine(""string: "" + e.GetType().FullName + "" / "" + e.Message); }

        try { await Native.RejectedWithAnEvent(); }
        catch (Exception e) { Console.WriteLine(""event: "" + e.GetType().FullName + "" / "" + e.Message); }

        try { var v = await Native.RejectedTyped(); Console.WriteLine(""NOT REACHED "" + v); }
        catch (Exception e) { Console.WriteLine(""typed: "" + e.GetType().FullName + "" / "" + e.Message); }

        Console.WriteLine(""<<DONE>>"");
    }

    public static void Main() { Run(); }
}", skipRoslyn: true);

            StringAssert.Contains(js, "error: System.SystemException / the promise rejected",
                "a rejected promise's reason must arrive as a System.Exception, not as the raw Error");
            StringAssert.Contains(js, "error is an Exception: True stack: True");
            StringAssert.Contains(js, "error tostring: True");
            StringAssert.Contains(js, "range: System.ArgumentOutOfRangeException / out of range");
            StringAssert.Contains(js, "string: System.Exception / a bare string reason",
                "a rejection reason that is not an Error at all still has to become one");
            StringAssert.Contains(js, "event: System.Exception / A JavaScript 'error' event was raised.");
            StringAssert.Contains(js, "typed: System.SystemException / typed rejection");
            StringAssert.Contains(js, "<<DONE>>");
        }

        /// <summary>
        /// <c>Task.WhenAll</c> over promises. A <c>[Script]</c> binding that returns a native promise
        /// where a <c>Task</c> is declared is the natural way to bind a JS async function, and it
        /// behaves like a working Task right up until it is handed to <c>WhenAll</c>/<c>WhenAny</c>,
        /// which drive their arguments through <c>continueWith</c>: the call died on a promise having
        /// no such method and, being inside a promise itself, took the rejection out of reach with
        /// it — nothing threw, nothing was logged, and every awaiter hung.
        /// </summary>
        [TestMethod]
        public async Task WhenAllAcceptsPromisesAndCollectsTheirRejectionsAsync()
        {
            var js = await RunTest(@"
using System;
using System.Linq;
using System.Threading.Tasks;
using Transpose;

public class Native
{
    [Script(""return Promise.reject(new Error('rejected ' + tag));"")]
    public static extern Task Reject(string tag);

    [Script(""return Promise.resolve(41);"")]
    public static extern Task<int> Resolve();
}

public class Program
{
    public static async Task Run()
    {
        var all = Task.WhenAll(Native.Reject(""one""), Native.Reject(""two""));

        try { await all; Console.WriteLine(""NOT REACHED""); }
        catch (Exception e) { Console.WriteLine(""threw: "" + e.GetType().FullName); }

        Console.WriteLine(""count: "" + all.Exception.InnerExceptions.Count);
        Console.WriteLine(""messages: "" + string.Join("","", all.Exception.InnerExceptions.Select(x => x.Message).OrderBy(x => x)));
        Console.WriteLine(""aggregate message: "" + all.Exception.Message);
        Console.WriteLine(""every inner is an Exception: "" + all.Exception.InnerExceptions.All(x => x is Exception));

        var any = await Task.WhenAny(Native.Reject(""via WhenAny""));
        Console.WriteLine(""WhenAny: faulted="" + any.IsFaulted + "" / "" + any.Exception.InnerException.Message);

        Console.WriteLine(""a resolving promise still resolves: "" + await Native.Resolve());
        Console.WriteLine(""<<DONE>>"");
    }

    public static void Main() { Run(); }
}", skipRoslyn: true);

            StringAssert.Contains(js, "threw: System.SystemException");
            StringAssert.Contains(js, "count: 2", "both rejections must reach the aggregate");
            StringAssert.Contains(js, "messages: rejected one,rejected two");
            StringAssert.Contains(js, "aggregate message: One or more errors occurred. (rejected one) (rejected two)",
                "the aggregate's message is the one a logger prints, so it has to name what failed");
            StringAssert.Contains(js, "every inner is an Exception: True");
            StringAssert.Contains(js, "WhenAny: faulted=True / rejected via WhenAny");
            StringAssert.Contains(js, "a resolving promise still resolves: 41");
            StringAssert.Contains(js, "<<DONE>>");
        }

        /// <summary>
        /// A JS failure inside the C# constructs that surround it: a <c>WhenAll</c> mixing a JS
        /// error with a C# exception (both have to land in the aggregate as exceptions, and the
        /// aggregate's message has to name both), an exception filter reading a JS error's message,
        /// and wrapping one as an <c>InnerException</c> so that <c>ToString()</c> reports the chain.
        /// </summary>
        [TestMethod]
        public async Task AJsErrorBehavesLikeAnExceptionInsideTasksFiltersAndChainsAsync()
        {
            var js = await RunTest(@"
using System;
using System.Linq;
using System.Threading.Tasks;
using Transpose;

public class Native
{
    [Script(""throw new Error('js failure ' + tag);"")]
    public static extern void Boom(string tag);
}

public class Program
{
    static async Task<int> JsFails(string tag) { await Task.Yield(); Native.Boom(tag); return 0; }
    static async Task<int> CSharpFails(string tag) { await Task.Yield(); throw new FormatException(""c# failure "" + tag); }

    public static async Task Run()
    {
        var all = Task.WhenAll(JsFails(""A""), CSharpFails(""B""), Task.FromResult(1));

        try { await all; Console.WriteLine(""NOT REACHED""); }
        catch (Exception e) { Console.WriteLine(""threw: "" + e.GetType().FullName + "" / "" + e.Message); }

        Console.WriteLine(""count: "" + all.Exception.InnerExceptions.Count);
        Console.WriteLine(""kinds: "" + string.Join("","", all.Exception.InnerExceptions.Select(x => x.GetType().FullName)));
        Console.WriteLine(""aggregate message: "" + all.Exception.Message);
        Console.WriteLine(""all are exceptions: "" + all.Exception.InnerExceptions.All(x => x is Exception));

        // A filter reading a JS error's message, and one that does not match letting it pass on.
        try { Native.Boom(""F""); }
        catch (Exception e) when (e.Message.Contains(""js failure F"")) { Console.WriteLine(""filter matched: "" + e.Message); }

        try
        {
            try { Native.Boom(""G""); }
            catch (Exception) when (false) { Console.WriteLine(""NOT REACHED""); }
        }
        catch (Exception e) { Console.WriteLine(""unmatched filter passed it on: "" + e.Message); }

        // Wrapped as an inner exception, the JS error is still what ToString() reports at the bottom.
        try
        {
            try { Native.Boom(""W""); }
            catch (Exception inner) { throw new InvalidOperationException(""wrapped a JS error"", inner); }
        }
        catch (Exception e)
        {
            Console.WriteLine(""wrapper: "" + e.Message + "" / inner: "" + e.InnerException.Message);
            Console.WriteLine(""tostring names both: "" + (e.ToString().Contains(""wrapped a JS error"") && e.ToString().Contains(""js failure W"")));
        }

        Console.WriteLine(""<<DONE>>"");
    }

    public static void Main() { Run(); }
}", skipRoslyn: true);

            StringAssert.Contains(js, "count: 2");
            StringAssert.Contains(js, "kinds: System.SystemException,System.FormatException",
                "a JS error and a C# exception must both reach the aggregate as exceptions");
            StringAssert.Contains(js, "aggregate message: One or more errors occurred. (js failure A) (c# failure B)");
            StringAssert.Contains(js, "all are exceptions: True");
            StringAssert.Contains(js, "filter matched: js failure F");
            StringAssert.Contains(js, "unmatched filter passed it on: js failure G");
            StringAssert.Contains(js, "wrapper: wrapped a JS error / inner: js failure W");
            StringAssert.Contains(js, "tostring names both: True");
            StringAssert.Contains(js, "<<DONE>>");
        }

        /// <summary>
        /// Normalising a browser value must not discard what it carried. The engine's own errors
        /// arrive with fields no .NET exception has a home for — an <c>ErrorEvent</c>'s
        /// filename/lineno, a <c>CloseEvent</c>'s code/reason, a custom <c>code</c> on an
        /// application error — so the value exactly as it arrived stays on the exception as
        /// <c>errorSource</c>, reachable from JavaScript. An <c>Error</c> subclass (which is what a
        /// library's own error type is) keeps its message and its frames, and a value from another
        /// realm is recognised by carrying a stack rather than by <c>instanceof</c>, which is
        /// per-realm and answers false for an iframe's or a worker's error.
        /// </summary>
        [TestMethod]
        public async Task NormalisationKeepsTheOriginalValueAndItsFieldsReachableAsync()
        {
            var js = await RunTest(@"
using System;
using System.Threading.Tasks;
using Transpose;

public class Native
{
    [Script(""var e = new Error('carried'); e.code = 'E_CUSTOM'; e.detail = { line: 12 }; throw e;"")]
    public static extern void ThrowWithFields();

    [Script(""class AppError extends Error { constructor(m) { super(m); this.name = 'AppError'; } } throw new AppError('a subclassed Error');"")]
    public static extern void ThrowSubclass();

    // An error from another realm: a real Error that fails `instanceof Error` here.
    [Script(""var alien = { message: 'from another realm', stack: 'Error: from another realm\\n    at alien (other.js:1:1)' }; throw alien;"")]
    public static extern void ThrowForeignError();

    [Script(""var s = ex.errorSource; return s ? (s.code + '/' + (s.detail ? s.detail.line : '-')) : 'no source';"")]
    public static extern string SourceOf(Exception ex);
}

public class Program
{
    public static async Task Run()
    {
        try { await Task.Run(() => Native.ThrowWithFields()); }
        catch (Exception e)
        {
            Console.WriteLine(""message: "" + e.Message);
            Console.WriteLine(""fields still reachable: "" + Native.SourceOf(e));
            Console.WriteLine(""frames are the throw site: "" + e.StackTrace.Contains(""ThrowWithFields""));
        }

        try { await Task.Run(() => Native.ThrowSubclass()); }
        catch (Exception e) { Console.WriteLine(""subclass: "" + e.Message + "" / stack: "" + (e.StackTrace != null)); }

        try { await Task.Run(() => Native.ThrowForeignError()); }
        catch (Exception e)
        {
            Console.WriteLine(""foreign: "" + e.GetType().FullName + "" / "" + e.Message);
            Console.WriteLine(""foreign frames kept: "" + e.StackTrace.Contains(""other.js""));
        }

        Console.WriteLine(""<<DONE>>"");
    }

    public static void Main() { Run(); }
}", skipRoslyn: true);

            StringAssert.Contains(js, "message: carried");
            StringAssert.Contains(js, "fields still reachable: E_CUSTOM/12",
                "the value as it arrived must stay reachable, or everything it carried is lost");
            StringAssert.Contains(js, "frames are the throw site: True");
            StringAssert.Contains(js, "subclass: a subclassed Error / stack: True");
            StringAssert.Contains(js, "foreign: System.SystemException / from another realm",
                "an error from another realm carries a stack but fails instanceof, so duck-type it");
            StringAssert.Contains(js, "foreign frames kept: True");
            StringAssert.Contains(js, "<<DONE>>");
        }
    }
}
