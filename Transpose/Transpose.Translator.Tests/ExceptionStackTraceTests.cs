using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// A value caught by <c>catch (Exception)</c> is either a real <c>System.Exception</c> — which
    /// captured an <c>Error</c> into <c>errorStack</c> when it was constructed — or a RAW JavaScript
    /// error thrown by interop or a rejected promise, which has a native <c>stack</c> and no
    /// <c>errorStack</c>. C# matches both, but <c>StackTrace</c> read <c>errorStack.stack</c>
    /// unconditionally, so it came back null for every raw error while a C#-thrown exception worked.
    ///
    /// The read now goes through <c>TransposeR.stackTrace</c>, which takes whichever shape arrived.
    /// The alternative — normalising every caught value into a wrapper the way h5 does — was rejected:
    /// it allocates on entry to every catch clause and makes <c>throw;</c> rethrow the wrapper rather
    /// than the original error, losing its identity and native stack for any outer JS handler.
    /// </summary>
    [TestClass]
    public class ExceptionStackTraceTests : TranslatorTestBase
    {
        [TestMethod]
        public void StackTraceReadsGoThroughTheHelper()
        {
            var code = @"
using System;

public class Program
{
    public static string Read()
    {
        try { throw new InvalidOperationException(""x""); }
        catch (Exception e) { return e.StackTrace; }
    }
    public static void Main() { }
}";
            var result = new RoslynTranslator().Translate(code);

            Assert.IsTrue(result.Success, "translation should succeed");
            Assert.IsTrue(result.Javascript!.Contains("TransposeR.stackTrace("),
                "Exception.StackTrace must read through the helper so a raw JS error also resolves\n"
                + result.Javascript);
        }

        /// <summary>A C#-thrown exception keeps its captured stack, and ToString() (which reads
        /// StackTrace internally) still works.</summary>
        [TestMethod]
        public async Task CSharpThrownExceptionStillHasAStackTraceAsync()
        {
            await RunTest(@"
using System;

public class Program
{
    static void Deep() => throw new InvalidOperationException(""deep"");

    public static void Main()
    {
        try { Deep(); }
        catch (Exception e)
        {
            Console.WriteLine(""has stack: "" + (e.StackTrace != null));
            Console.WriteLine(""tostring: "" + e.ToString().Contains(""deep""));
        }
    }
}");
        }

        /// <summary>A raw JS error crossing into C# resolves its native stack; a thrown non-Error
        /// (a bare string, an object literal) has no stack and correctly yields null.</summary>
        [TestMethod]
        public async Task RawJavaScriptErrorExposesItsNativeStackAsync()
        {
            var js = await RunTest(@"
using System;
using Transpose;

public class Native
{
    [Script(""throw new TypeError('js boom');"")] public static extern void ThrowError();
    [Script(""throw 'bare string';"")]            public static extern void ThrowString();
}

public class Program
{
    public static void Main()
    {
        try { Native.ThrowError(); }
        catch (Exception e) { Console.WriteLine(""error stack: "" + (e.StackTrace ?? """").Split('\n')[0]); }

        try { Native.ThrowString(); }
        catch (Exception e) { Console.WriteLine(""string stack null: "" + (e.StackTrace == null)); }
    }
}", skipRoslyn: true);

            StringAssert.Contains(js, "error stack: TypeError: js boom",
                "a raw JS error must expose its native stack through Exception.StackTrace");
            StringAssert.Contains(js, "string stack null: True",
                "a thrown non-Error has no stack, so StackTrace must be null rather than undefined");
        }

        /// <summary>
        /// A browser error that crosses into C# through a Task - a rejected promise, a body that
        /// throws inside Task.Run - is normalised by System.Exception.create. That normalisation
        /// used to lose both halves of the diagnostic: it passed the Error OBJECT where a string
        /// message was expected (so Message was not a string at all), and it returned before
        /// keeping the error, leaving the exception with the stack its own constructor had just
        /// captured - every frame of it inside tps.js, none of them the throw site.
        /// </summary>
        [TestMethod]
        public async Task ANormalisedBrowserErrorKeepsItsMessageAndItsOwnStackAsync()
        {
            var js = await RunTest(@"
using System;
using System.Threading.Tasks;
using Transpose;

public class Native
{
    [Script(""throw new Error('inside the task');"")] public static extern void Boom();
}

public class Program
{
    public static async Task Run()
    {
        try { await Task.Run(() => Native.Boom()); }
        catch (Exception e)
        {
            Console.WriteLine(""message: "" + e.Message);
            Console.WriteLine(""message is a string: "" + (e.Message.Length == ""inside the task"".Length));
            Console.WriteLine(""stack names the thrower: "" + e.StackTrace.Contains(""Boom""));
            Console.WriteLine(""stack is not the wrapper's: "" + !e.StackTrace.Split('\n')[1].Contains(""ctor""));
        }
        Console.WriteLine(""<<DONE>>"");
    }

    public static void Main() { Run(); }
}", skipRoslyn: true);

            StringAssert.Contains(js, "message: inside the task",
                "Message must be the error's message as a string, not the Error object");
            StringAssert.Contains(js, "message is a string: True", "Message must reach C# as a string");
            StringAssert.Contains(js, "stack names the thrower: True",
                "the browser's own frames must survive normalisation");
            StringAssert.Contains(js, "stack is not the wrapper's: True",
                "the stack must not be the one captured by the wrapping exception's constructor");
            StringAssert.Contains(js, "<<DONE>>");
        }

        /// <summary>
        /// The engine's own error types map onto their .NET counterparts, and the message they came
        /// with has to survive that mapping verbatim - a RangeError's message was passed as the
        /// *paramName* of ArgumentOutOfRangeException, which buried it inside "Specified argument was
        /// out of the range of valid values.".
        /// </summary>
        [TestMethod]
        public async Task NormalisingAnEngineErrorTypeKeepsItsMessageVerbatimAsync()
        {
            var js = await RunTest(@"
using System;
using System.Threading.Tasks;
using Transpose;

public class Native
{
    [Script(""throw new RangeError('Invalid array length');"")] public static extern void BoomRange();
    [Script(""throw new TypeError('x is not a function');"")]   public static extern void BoomType();
}

public class Program
{
    public static async Task Run()
    {
        try { await Task.Run(() => Native.BoomRange()); }
        catch (Exception e) { Console.WriteLine(""range: "" + e.GetType().FullName + "" / "" + e.Message); }

        try { await Task.Run(() => Native.BoomType()); }
        catch (Exception e) { Console.WriteLine(""type: "" + e.GetType().FullName + "" / "" + e.Message); }

        Console.WriteLine(""<<DONE>>"");
    }

    public static void Main() { Run(); }
}", skipRoslyn: true);

            StringAssert.Contains(js, "range: System.ArgumentOutOfRangeException / Invalid array length",
                "a RangeError's message must be the message, not a parameter name");
            StringAssert.Contains(js, "type: System.NullReferenceException / x is not a function");
            StringAssert.Contains(js, "<<DONE>>");
        }

        /// <summary>
        /// A thrown non-Error carries no frames, so the Error the wrapping exception's constructor
        /// captures - taken in the handler, close to the throw - is the only stack there is.
        /// Assigning the thrown value over it left the exception with no stack at all.
        /// </summary>
        [TestMethod]
        public async Task NormalisingAThrownNonErrorKeepsTheCapturedStackAsync()
        {
            var js = await RunTest(@"
using System;
using System.Threading.Tasks;
using Transpose;

public class Native
{
    [Script(""throw 'a bare string';"")]                          public static extern void BoomString();
    [Script(""throw { type: 'error', target: null };"")]           public static extern void BoomEvent();
}

public class Program
{
    public static async Task Run()
    {
        try { await Task.Run(() => Native.BoomString()); }
        catch (Exception e)
        {
            Console.WriteLine(""string message: "" + e.Message);
            Console.WriteLine(""string has a stack: "" + (e.StackTrace != null));
        }

        try { await Task.Run(() => Native.BoomEvent()); }
        catch (Exception e)
        {
            Console.WriteLine(""event message: "" + e.Message);
        }

        Console.WriteLine(""<<DONE>>"");
    }

    public static void Main() { Run(); }
}", skipRoslyn: true);

            StringAssert.Contains(js, "string message: a bare string");
            StringAssert.Contains(js, "string has a stack: True",
                "the constructor's captured stack must not be replaced by a value that has none");
            StringAssert.Contains(js, "event message: A JavaScript 'error' event was raised.",
                "a DOM event carries no message and stringifies to \"[object Event]\": name the event instead");
            StringAssert.Contains(js, "<<DONE>>");
        }

        /// <summary>
        /// An ErrorEvent (window.onerror, worker.onerror) or a PromiseRejectionEvent wraps the value
        /// that was actually thrown. The wrapped error is what carries the stack, so it is the one to
        /// report: the event's own `stack` is absent, and reporting the inner error's stack TEXT as
        /// the message (what this used to do) put the two in each other's place.
        /// </summary>
        [TestMethod]
        public async Task NormalisingAnErrorEventReportsTheWrappedErrorAsync()
        {
            var js = await RunTest(@"
using System;
using System.Threading.Tasks;
using Transpose;

public class Native
{
    [Script(""throw { type: 'error', target: null, message: 'Uncaught Error', filename: 'app.js', error: new Error('the real cause') };"")]
    public static extern void BoomEvent();
}

public class Program
{
    public static async Task Run()
    {
        try { await Task.Run(() => Native.BoomEvent()); }
        catch (Exception e)
        {
            Console.WriteLine(""message: "" + e.Message);
            Console.WriteLine(""stack starts with the cause: "" + e.StackTrace.Split('\n')[0]);
        }
        Console.WriteLine(""<<DONE>>"");
    }

    public static void Main() { Run(); }
}", skipRoslyn: true);

            StringAssert.Contains(js, "message: the real cause",
                "the wrapped error's message must be the message, not its stack text");
            StringAssert.Contains(js, "stack starts with the cause: Error: the real cause",
                "the stack must come from the wrapped error, which is the value that has one");
            StringAssert.Contains(js, "<<DONE>>");
        }

        /// <summary>
        /// `catch (Exception)` also binds a value the engine threw that is not an Error at all - a
        /// string, an object - and those have no `message` field, so reading the member directly
        /// reported no message for them. Read through TransposeR.message, as StackTrace already is.
        /// </summary>
        [TestMethod]
        public async Task MessageOfARawThrownValueIsNotLostAsync()
        {
            var js = await RunTest(@"
using System;
using Transpose;

public class Native
{
    [Script(""throw 'a bare string';"")]                                public static extern void ThrowString();
    [Script(""throw new Error('a real error');"")]                      public static extern void ThrowError();
    [Script(""throw { type: 'error', target: null };"")]                public static extern void ThrowEvent();
}

public class Program
{
    public static void Main()
    {
        try { Native.ThrowString(); } catch (Exception e) { Console.WriteLine(""string: "" + e.Message); }
        try { Native.ThrowError(); }  catch (Exception e) { Console.WriteLine(""error: "" + e.Message); }
        try { Native.ThrowEvent(); }  catch (Exception e) { Console.WriteLine(""event: "" + e.Message); }

        try { throw new InvalidOperationException(""from C#""); }
        catch (Exception e) { Console.WriteLine(""csharp: "" + e.Message); }
    }
}", skipRoslyn: true);

            StringAssert.Contains(js, "string: a bare string", "a thrown string is its own message");
            StringAssert.Contains(js, "error: a real error");
            StringAssert.Contains(js, "event: A JavaScript 'error' event was raised.");
            StringAssert.Contains(js, "csharp: from C#", "a real exception's message is unchanged");
        }

        /// <summary>
        /// TaskCompletionSource.TrySetException is handed whatever `catch (Exception)` bound, which
        /// may be a raw browser error (BrowserHttpHandler does exactly this). Building the
        /// AggregateException out of that value threw "Cannot create Enumerator." from inside the
        /// setter, which left the task un-completed forever - every awaiter hung - and discarded the
        /// error on the way.
        /// </summary>
        [TestMethod]
        public async Task TrySetExceptionAcceptsARawBrowserErrorAsync()
        {
            var js = await RunTest(@"
using System;
using System.Threading.Tasks;
using Transpose;

public class Native
{
    [Script(""throw new Error('handler failed');"")] public static extern void Boom();
}

public class Program
{
    public static async Task Run()
    {
        var tcs = new TaskCompletionSource<int>();

        try { Native.Boom(); }
        catch (Exception e) { tcs.TrySetException(e); }

        Console.WriteLine(""task completed: "" + tcs.Task.IsCompleted);

        try { await tcs.Task; }
        catch (Exception e) { Console.WriteLine(""awaited: "" + e.Message); }

        Console.WriteLine(""<<DONE>>"");
    }

    public static void Main() { Run(); }
}", skipRoslyn: true);

            StringAssert.Contains(js, "task completed: True",
                "a raw error must fault the task rather than throw out of the setter");
            StringAssert.Contains(js, "awaited: handler failed", "the error itself must survive");
            StringAssert.Contains(js, "<<DONE>>");
        }

        /// <summary>
        /// ToString() reported the type, the message and the stack, and nothing at all about the
        /// inner exception - which, for an exception wrapping the error that actually failed, is the
        /// part the reader needs. .NET reports the whole chain.
        /// </summary>
        [TestMethod]
        public async Task ToStringReportsTheInnerExceptionChainAsync()
        {
            var js = await RunTest(@"
using System;

public class Program
{
    public static void Main()
    {
        var inner = new FormatException(""the inner cause"");
        var text = new InvalidOperationException(""the outer failure"", inner).ToString();

        Console.WriteLine(""names the outer: "" + text.Contains(""System.InvalidOperationException: the outer failure""));
        Console.WriteLine(""names the inner: "" + text.Contains(""System.FormatException: the inner cause""));
        Console.WriteLine(""marks the chain: "" + text.Contains("" ---> ""));
        Console.WriteLine(""ends the inner trace: "" + text.Contains(""--- End of inner exception stack trace ---""));
    }
}", skipRoslyn: true);

            StringAssert.Contains(js, "names the outer: True");
            StringAssert.Contains(js, "names the inner: True", "an exception's chain must not be dropped");
            StringAssert.Contains(js, "marks the chain: True");
            StringAssert.Contains(js, "ends the inner trace: True");
        }

        /// <summary>
        /// A task faulted with a bare exception rather than an AggregateException (Task.FromException
        /// does that) had its fault read as `exception.innerExceptions.Count`, raising a TypeError
        /// over the real exception; an AggregateException carrying none at all rethrew `null`.
        /// </summary>
        [TestMethod]
        public async Task AwaitingATaskFaultedWithABareExceptionRethrowsItAsync()
        {
            var js = await RunTest(@"
using System;
using System.Threading.Tasks;

public class Program
{
    public static async Task Run()
    {
        try { await Task.Run(() => Task.FromException(new InvalidOperationException(""bare fault""))); }
        catch (Exception e) { Console.WriteLine(""nested: "" + e.GetType().FullName + "" / "" + e.Message); }

        try { Task.FromException(new FormatException(""direct fault"")).GetAwaiter().GetResult(); }
        catch (Exception e) { Console.WriteLine(""awaiter: "" + e.GetType().FullName + "" / "" + e.Message); }

        Console.WriteLine(""<<DONE>>"");
    }

    public static void Main() { Run(); }
}", skipRoslyn: true);

            StringAssert.Contains(js, "nested: System.InvalidOperationException / bare fault");
            StringAssert.Contains(js, "awaiter: System.FormatException / direct fault",
                "reading the fault must not raise a TypeError over it");
            StringAssert.Contains(js, "<<DONE>>");
        }
    }
}
