using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// An <c>async void</c> body — a method, a local function, or an async lambda converted to a
    /// void-returning delegate (<c>window.setTimeout(async _ =&gt; …)</c>, a DOM event handler) — produces
    /// a Task nobody can reach: the delegate's caller has no return value to await. Emitting it like any
    /// other async body did not merely lose a failure, it hid one. <c>TransposeR.fromPromise</c> attaches
    /// a rejection handler, so the engine's own unhandled-rejection report never fired either, and
    /// nothing in the runtime reports an unobserved faulted Task — an exception thrown out of such a
    /// body vanished with no console output at all, quieter than the same code written in JavaScript.
    /// <para>
    /// Those bodies now emit through <c>TransposeR.fireAndForget</c>, which reports the fault (by default
    /// to <c>console.error</c>, and to <c>Transpose.Script.SetUnhandledExceptionHandler</c>'s handler when
    /// one is installed) — the browser's analogue of .NET rethrowing an async void fault on the
    /// SynchronizationContext. Everything whose Task IS observable keeps returning it.
    /// </para>
    /// <para>
    /// The cases that install a handler compile against <c>Transpose.Script</c>, so they need a
    /// <c>TRANSPOSE_DLL_PATH</c> pointing at a locally built <c>Transpose.dll</c> if the NuGet-cached base
    /// library predates that API. They also run Transpose-only (<c>skipRoslyn</c>): natively, an async void
    /// fault is rethrown on the thread pool and tears the process down.
    /// </para>
    /// </summary>
    [TestClass]
    public class AsyncVoidFaultTests : TranslatorTestBase
    {
        // ---- the fault is reported, not swallowed ------------------------------

        [TestMethod]
        public async Task AsyncVoidLambdaFaultReachesTheHandler()
        {
            var output = await RunTest(@"
using System;
using System.Threading.Tasks;
using Transpose;
public class Program
{
    public static void Main()
    {
        Script.SetUnhandledExceptionHandler(ex => Console.WriteLine(""handled: "" + ex.Message));
        // The shape this exists for: an async lambda handed to a void-returning delegate.
        Action fireAndForget = async () => { await Task.Delay(1); throw new InvalidOperationException(""after await""); };
        Action throwsFirst = async () => { throw new InvalidOperationException(""before await""); };
        fireAndForget();
        throwsFirst();
        Console.WriteLine(""main done"");
    }
}", skipRoslyn: true);

            StringAssert.Contains(output, "main done");
            StringAssert.Contains(output, "handled: after await");
            // A body that throws before its first await still faults the promise, not the caller.
            StringAssert.Contains(output, "handled: before await");
        }

        [TestMethod]
        public async Task AsyncVoidMethodAndLocalFunctionFaultsReachTheHandler()
        {
            var output = await RunTest(@"
using System;
using System.Threading.Tasks;
using Transpose;
public class Program
{
    static async void Method() { await Task.Delay(1); throw new InvalidOperationException(""from method""); }

    public static void Main()
    {
        Script.SetUnhandledExceptionHandler(ex => Console.WriteLine(""handled: "" + ex.Message));
        async void Local() { await Task.Delay(1); throw new InvalidOperationException(""from local""); }
        Method();
        Local();
    }
}", skipRoslyn: true);

            StringAssert.Contains(output, "handled: from method");
            StringAssert.Contains(output, "handled: from local");
        }

        [TestMethod]
        public async Task AsyncVoidStillRunsItsBodyToCompletion()
        {
            // Reporting the fault must not change the happy path: the body still runs to its end — so
            // this one is diffed against native .NET rather than asserted on. The two are awaited one
            // at a time because .NET resumes an async void continuation on the thread pool, so two in
            // flight together finish in either order and the diff would be flaky.
            await RunTest(@"
using System;
using System.Threading.Tasks;
public class Program
{
    static async void Method(string tag) { await Task.Delay(1); Console.WriteLine(""ran "" + tag); }

    public static async Task Main()
    {
        Method(""method"");
        await Task.Delay(100);
        Action lambda = async () => { await Task.Delay(1); Console.WriteLine(""ran lambda""); };
        lambda();
        await Task.Delay(100);
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        // ---- which bodies are async void --------------------------------------

        [TestMethod]
        public void EveryAsyncVoidBodyEmitsFireAndForget()
        {
            var code = @"
using System;
using System.Threading.Tasks;
public class Program
{
    static async void Method() { await Task.Delay(1); }

    public static void Main()
    {
        Action lambda = async () => { await Task.Delay(1); };
        Action<int> anon = async delegate (int x) { await Task.Delay(1); };
        Action expr = async () => await Task.Delay(1);
        async void Local() { await Task.Delay(1); }
        Method(); lambda(); anon(1); expr(); Local();
    }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;
            Assert.AreEqual(5, CountOf(js, "TransposeR.fireAndForget("),
                "the async void method, lambda, anonymous method, expression-bodied lambda and local function\n" + js);
            Assert.IsFalse(js.Contains("TransposeR.fromPromise("),
                "none of these has a Task anyone can observe\n" + js);
        }

        [TestMethod]
        public void AnObservableTaskIsStillReturned()
        {
            var code = @"
using System;
using System.Threading.Tasks;
public class Program
{
    static async Task Method() { await Task.Delay(1); }

    public static void Main()
    {
        Func<Task> lambda = async () => { await Task.Delay(1); };
        var natural = async () => { await Task.Delay(1); };
        async Task Local() { await Task.Delay(1); }
        // Task.Run binds an async lambda to Run<TResult>(Func<TResult>), not Run(Action) — a lambda
        // whose inferred return type is non-void beats the void-returning candidate — so the common
        // `Task.Run(async () => …)` fire-and-forget helper keeps handing its Task back.
        Task.Run(async () => { await Task.Delay(1); });
        Method(); lambda(); natural(); Local();
    }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;
            Assert.AreEqual(5, CountOf(js, "return TransposeR.fromPromise("),
                "the async Task method, the two Task-returning lambdas, the local function and Task.Run's lambda\n" + js);
            Assert.IsFalse(js.Contains("TransposeR.fireAndForget("),
                "every one of these produces a Task the C# side holds\n" + js);
        }


        // ---- every shape that is async void -----------------------------------

        [TestMethod]
        public void EveryVoidDelegateConversionIsAsyncVoid()
        {
            // Which bodies are async void is decided by the CONVERTED delegate's return type, so the
            // interesting cases are the ones where that target is not written next to the lambda: an
            // event subscription, a cast, an argument, a type argument C# infers, a ternary or switch
            // arm whose branches are converted one by one, a field initializer. Each of these is a real
            // application's fire-and-forget callback, and each has to reach TransposeR.fireAndForget.
            var code = @"
using System;
using System.Threading.Tasks;
using Transpose;

[ExpandParams]
public delegate void VariadicHandler(string name, params object[] args);
public delegate void CustomVoid(int x);

public class Program
{
    public static event EventHandler Ticked;
    static readonly Action Field = async () => { await Task.Delay(1); };

    static void Takes(Action a) { }
    static void Infers<T>(Action<T> a) { }

    public static void Main()
    {
        Ticked += async (s, e) => { await Task.Delay(1); };
        CustomVoid custom = async x => { await Task.Delay(1); };
        var cast = (Action)(async () => { await Task.Delay(1); });
        Takes(async () => { await Task.Delay(1); });
        Infers(async (int x) => { await Task.Delay(1); });
        Action ternary = true ? (async () => { await Task.Delay(1); }) : (Action)null;
        Action arm = 1 switch { 1 => async () => { await Task.Delay(1); }, _ => (Action)null };
        VariadicHandler variadic = async (name, args) => { await Task.Delay(1); };
        Action<int> paramless = async delegate { await Task.Delay(1); };
        custom(1); cast(); ternary(); arm(); variadic(""e""); paramless(1);
    }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;
            Assert.AreEqual(10, CountOf(js, "TransposeR.fireAndForget("),
                "the field initializer, the event handler, the custom void delegate, the cast, the argument,\n"
                + "the inferred Action<T>, the ternary branch, the switch arm, the [ExpandParams] delegate\n"
                + "and the parameterless anonymous method\n" + js);
            Assert.IsFalse(js.Contains("TransposeR.fromPromise("),
                "none of these has a Task anyone can observe\n" + js);
            // [ExpandParams] and async void are decided independently and both apply: the variadic tail
            // still arrives as a JS rest parameter inside a body that reports rather than returns.
            StringAssert.Contains(js.Replace("\r\n", "\n"), "(name, ...args) => {\n",
                "the [ExpandParams] tail is still a rest parameter\n" + js);
        }

        [TestMethod]
        public void EveryAsyncVoidDeclarationShapeIsAsyncVoid()
        {
            // The declaration side: a method, an expression-bodied method, an override, an explicit
            // interface implementation, a generic method, a member of a generic type, and an
            // expression-bodied local function each take a different path through the emitter.
            var code = @"
using System;
using System.Threading.Tasks;

public interface ITicker { void Tick(); }
public abstract class Base { public virtual void OnTick() { } }

public class Derived : Base, ITicker
{
    public override async void OnTick() => await Task.Delay(1);
    async void ITicker.Tick() { await Task.Delay(1); }
}

public class Holder<T>
{
    public async void Show() { await Task.Delay(1); }
}

public struct Counter
{
    public async void Bump() { await Task.Delay(1); }
}

public class Program
{
    static async void Generic<T>(T v) { await Task.Delay(1); }
    static async void ExprBodied() => await Task.Delay(1);

    public static void Main()
    {
        async void ExprLocal() => await Task.Delay(1);
        Generic(1); ExprBodied(); ExprLocal();
    }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;
            Assert.AreEqual(7, CountOf(js, "TransposeR.fireAndForget("),
                "the override, the explicit interface implementation, the generic type's member, the struct's\n"
                + "member, the generic method, the expression-bodied method and the expression-bodied local function\n" + js);
            Assert.IsFalse(js.Contains("TransposeR.fromPromise("),
                "none of these has a Task anyone can observe\n" + js);
        }

        [TestMethod]
        public void ATaskReturningOverloadStillWinsAndStillHandsItsTaskBack()
        {
            // The narrowing rule in reverse: wherever the target delegate returns a Task the body keeps
            // returning one, including the case that decides it for the common fire-and-forget helper —
            // a method overloaded on Action AND Func<Task> binds the Func<Task> candidate for an async
            // lambda, because the lambda's inferred return type is non-void.
            var code = @"
using System;
using System.Threading.Tasks;

public delegate Task CustomTask();

public class Program
{
    public static event Func<Task> Asked;

    static void TakesTask(Func<Task> f) { }
    static void Both(Action a) { }
    static void Both(Func<Task> f) { }

    public static void Main()
    {
        Asked += async () => { await Task.Delay(1); };
        CustomTask customTask = async () => { await Task.Delay(1); };
        TakesTask(async () => { await Task.Delay(1); });
        Both(async () => { await Task.Delay(1); });
        customTask();
    }
}";
            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;
            Assert.AreEqual(4, CountOf(js, "return TransposeR.fromPromise("),
                "the Func<Task> event handler, the custom Task delegate, the Func<Task> argument and the\n"
                + "Func<Task> overload the async lambda binds\n" + js);
            Assert.IsFalse(js.Contains("TransposeR.fireAndForget("),
                "every one of these produces a Task the C# side holds\n" + js);
            StringAssert.Contains(js, "Program.Both$1(",
                "the async lambda binds Both(Func<Task>), not Both(Action)\n" + js);
        }

        // ---- the fault reaches the handler from every kind of body -------------

        [TestMethod]
        public async Task TheHandlerReceivesTheExceptionItselfExactlyOnce()
        {
            var output = await RunTest(@"
using System;
using System.Threading.Tasks;
using Transpose;

public class AppException : Exception { public AppException(string m) : base(m) { } }

public class Program
{
    public static void Main()
    {
        Script.SetUnhandledExceptionHandler(ex => Console.WriteLine(""H: "" + ex.GetType().Name + "" / "" + ex.Message));
        Action typed = async () => { await Task.Delay(1); throw new AppException(""custom type""); };
        typed();
    }
}", skipRoslyn: true);

            // Not a stand-in: the exception the body threw, with its own type and message.
            StringAssert.Contains(output, "H: AppException / custom type");
            Assert.AreEqual(1, CountOf(output, "H: "), "reported once, not once per continuation\n" + output);
        }

        [TestMethod]
        public async Task AFaultReachesTheHandlerFromEveryPlaceAnAsyncVoidBodyCanLive()
        {
            // The shapes an application actually writes: a callback handed to foreign JavaScript (the
            // `window.setTimeout(async _ => …)` case this exists for), an instance method that captures
            // `this`, a struct's method, an explicit interface implementation, a member of a generic
            // type, and a lambda whose body needs statements to emit (an object initializer awaiting a
            // value, which emits as an async IIFE inside the async void body).
            var output = await RunTest(@"
using System;
using System.Threading.Tasks;
using Transpose;

[External]
[Name(""globalThis"")]
public static class Js
{
    [Template(""setTimeout({callback}, {ms})"")]
    public static extern void SetTimeout(Action callback, int ms);
}

public interface ITicker { void Tick(); }

public class Worker : ITicker
{
    private readonly int _n;
    public Worker(int n) { _n = n; }
    public async void Start() { await Task.Delay(1); throw new Exception(""worker "" + _n); }
    async void ITicker.Tick() { await Task.Delay(1); throw new Exception(""explicit impl""); }
}

public struct Counter
{
    public int N;
    public async void Bump() { await Task.Delay(1); throw new Exception(""struct "" + N); }
}

public class Holder<T>
{
    public T Value;
    public async void Show() { await Task.Delay(1); throw new Exception(""holder "" + Value); }
}

public class Box { public int X; public int Y; }

public class Program
{
    static Task<int> GetAsync(int v) => Task.FromResult(v);

    public static void Main()
    {
        Script.SetUnhandledExceptionHandler(ex => Console.WriteLine(""H: "" + ex.Message));

        Js.SetTimeout(async () => { await Task.Delay(1); throw new Exception(""foreign callback""); }, 1);
        new Worker(7).Start();
        ((ITicker)new Worker(0)).Tick();
        new Counter { N = 5 }.Bump();
        new Holder<int> { Value = 4 }.Show();

        Action initializer = async () =>
        {
            var b = new Box { X = await GetAsync(1), Y = await GetAsync(2) };
            throw new Exception(""initializer "" + b.X + b.Y);
        };
        initializer();
    }
}", skipRoslyn: true);

            StringAssert.Contains(output, "H: foreign callback");
            StringAssert.Contains(output, "H: worker 7");
            StringAssert.Contains(output, "H: explicit impl");
            StringAssert.Contains(output, "H: struct 5");
            StringAssert.Contains(output, "H: holder 4");
            StringAssert.Contains(output, "H: initializer 12");
        }

        [TestMethod]
        public async Task CapturedStateIsWhatTheHandlerSees()
        {
            // A fire-and-forget callback is nearly always a closure over a loop variable. The body
            // resumes long after its caller returned and after the loop it was created in has finished,
            // so what it reports has to be the value that iteration captured — three distinct faults,
            // not three copies of the last one.
            var output = await RunTest(@"
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Transpose;

public class Program
{
    public static void Main()
    {
        Script.SetUnhandledExceptionHandler(ex => Console.WriteLine(""H: "" + ex.Message));
        var actions = new List<Action>();
        for (int i = 0; i < 3; i++)
        {
            int captured = i;
            actions.Add(async () => { await Task.Delay(1); throw new Exception(""captured "" + captured); });
        }
        foreach (var a in actions) a();
    }
}", skipRoslyn: true);

            StringAssert.Contains(output, "H: captured 0");
            StringAssert.Contains(output, "H: captured 1");
            StringAssert.Contains(output, "H: captured 2");
        }

        [TestMethod]
        public async Task ACancellationIsReportedLikeAnyOtherFault()
        {
            // .NET rethrows an async void fault on the SynchronizationContext whatever it is, so a
            // cancelled operation is reported too. Silence is asked for by clearing the handler, not by
            // the runtime deciding some exceptions do not count.
            var output = await RunTest(@"
using System;
using System.Threading.Tasks;
using Transpose;

public class Program
{
    public static void Main()
    {
        Script.SetUnhandledExceptionHandler(ex => Console.WriteLine(""H: "" + ex.GetType().Name));
        Action cancelled = async () => { await Task.Delay(1); throw new OperationCanceledException(""stopped""); };
        cancelled();
    }
}", skipRoslyn: true);

            StringAssert.Contains(output, "H: OperationCanceledException");
        }

        [TestMethod]
        public async Task AFaultHandledInsideTheBodyIsNeverReported()
        {
            // Reporting is for the exception that leaves the body. One the body catches — including one
            // thrown past a finally that runs first — is not a fault at all.
            var output = await RunTest(@"
using System;
using System.Threading.Tasks;
using Transpose;

public class Program
{
    public static void Main()
    {
        Script.SetUnhandledExceptionHandler(ex => Console.WriteLine(""H: "" + ex.Message));

        Action caught = async () =>
        {
            try { await Task.Delay(1); throw new Exception(""swallowed""); }
            catch (Exception e) { Console.WriteLine(""caught "" + e.Message); }
            finally { Console.WriteLine(""finally ran""); }
        };
        caught();

        Action never = async () => { await Task.Delay(1); Console.WriteLine(""no throw at all""); };
        never();
    }
}", skipRoslyn: true);

            StringAssert.Contains(output, "caught swallowed");
            StringAssert.Contains(output, "finally ran");
            StringAssert.Contains(output, "no throw at all");
            Assert.AreEqual(0, CountOf(output, "H: "), "nothing left either body\n" + output);
        }

        [TestMethod]
        public async Task WithNoHandlerInstalledTheFaultStillReachesTheConsole()
        {
            // What an application that never heard of the hook gets, which is the whole point: before
            // this, a throw out of an async void body produced no output at all — not even the engine's
            // unhandled-rejection report, because fromPromise had already attached a rejection handler.
            // console.error goes to stderr and the harness reads stdout, so the report is captured by
            // swapping console.error for something that writes to console.log.
            var output = await RunTest(@"
using System;
using System.Threading.Tasks;
using Transpose;

public class Program
{
    public static async Task Main()
    {
        Script.Write(""globalThis.console.error = function (x) { if (typeof x === 'string') { globalThis.console.log('ERR ' + x); } }"");
        Action noHandlerInstalled = async () => { await Task.Delay(1); throw new InvalidOperationException(""nobody is listening""); };
        noHandlerInstalled();
        await Task.Delay(50);
        Console.WriteLine(""still running"");
    }
}", skipRoslyn: true);

            StringAssert.Contains(output, "ERR Unhandled exception in an async void method:");
            StringAssert.Contains(output, "System.InvalidOperationException: nobody is listening");
            // Reporting it is all that happens — the program carries on, as .NET's process does not.
            StringAssert.Contains(output, "still running");
        }

        // ---- the handler itself ------------------------------------------------

        [TestMethod]
        public async Task ClearingTheHandlerSilencesTheReport()
        {
            // Passing null is how an application asks for the old behaviour back. It has to be the
            // whole of it: nothing is written, and nothing else in the program is disturbed.
            var output = await RunTest(@"
using System;
using System.Threading.Tasks;
using Transpose;

public class Program
{
    public static async Task Main()
    {
        Script.SetUnhandledExceptionHandler(null);
        Action silent = async () => { await Task.Delay(1); throw new Exception(""should be silent""); };
        silent();
        await Task.Delay(50);
        Console.WriteLine(""still running"");
    }
}", skipRoslyn: true);

            Assert.IsFalse(output.Contains("should be silent"), "the fault was reported anyway\n" + output);
            StringAssert.Contains(output, "still running");
        }

        [TestMethod]
        public async Task AHandlerThatThrowsDoesNotTakeTheNextFaultWithIt()
        {
            // A throwing handler must not become a second unhandled rejection — which nothing would
            // report, putting the failure back exactly where it was — and must not leave the hook in a
            // state where the NEXT fault goes unreported.
            var output = await RunTest(@"
using System;
using System.Threading.Tasks;
using Transpose;

public class Program
{
    public static async Task Main()
    {
        Script.SetUnhandledExceptionHandler(ex =>
        {
            Console.WriteLine(""H: "" + ex.Message);
            throw new Exception(""the handler itself failed"");
        });

        Action first = async () => { await Task.Delay(1); throw new Exception(""first""); };
        first();
        await Task.Delay(50);

        Action second = async () => { await Task.Delay(1); throw new Exception(""second""); };
        second();
        await Task.Delay(50);
        Console.WriteLine(""still running"");
    }
}", skipRoslyn: true);

            StringAssert.Contains(output, "H: first");
            StringAssert.Contains(output, "H: second");
            StringAssert.Contains(output, "still running");
        }

        [TestMethod]
        public async Task TheHandlerCanBeReplacedWhileFaultsAreInFlight()
        {
            // The hook is a single global read at report time, so an application that installs its own
            // handler after start-up (once its telemetry is up) sees everything reported from then on —
            // including a fault reported from inside another handler.
            var output = await RunTest(@"
using System;
using System.Threading.Tasks;
using Transpose;

public class Program
{
    public static async Task Main()
    {
        Script.SetUnhandledExceptionHandler(ex =>
        {
            Console.WriteLine(""first handler: "" + ex.Message);
            if (ex.Message == ""a"") { Action nested = async () => { await Task.Delay(1); throw new Exception(""from the handler""); }; nested(); }
        });

        Action a = async () => { await Task.Delay(1); throw new Exception(""a""); };
        a();
        await Task.Delay(60);

        Script.SetUnhandledExceptionHandler(ex => Console.WriteLine(""second handler: "" + ex.Message));
        Action b = async () => { await Task.Delay(1); throw new Exception(""b""); };
        b();
    }
}", skipRoslyn: true);

            StringAssert.Contains(output, "first handler: a");
            StringAssert.Contains(output, "first handler: from the handler");
            StringAssert.Contains(output, "second handler: b");
        }

        // ---- reporting a fault changes nothing else ----------------------------

        [TestMethod]
        public async Task AnAsyncVoidBodyRunsItsSynchronousPrefixBeforeTheCallerContinues()
        {
            // An async void call returns at its body's first await, not at its end — the JS caller of a
            // void delegate expects exactly that, and so does .NET. `fireAndForget` returns undefined
            // instead of a Task, which must not change when the body's statements run. Diffed against
            // native .NET rather than asserted on.
            await RunTest(@"
using System;
using System.Threading.Tasks;
public class Program
{
    static async void Tick(string tag)
    {
        Console.WriteLine(tag + "": before await"");
        await Task.Delay(1);
        Console.WriteLine(tag + "": after await"");
    }

    public static async Task Main()
    {
        Tick(""method"");
        Console.WriteLine(""caller continues"");
        await Task.Delay(100);
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public async Task AnAsyncVoidBodyKeepsItsControlFlow()
        {
            // The body is emitted inside an async arrow, so an early `return`, a loop around an await
            // and a caught exception have to behave as they did. Diffed against native .NET.
            await RunTest(@"
using System;
using System.Threading.Tasks;
public class Program
{
    static async void EarlyOut(int n)
    {
        await Task.Delay(1);
        if (n == 0) { Console.WriteLine(""returned early""); return; }
        for (var i = 0; i < n; i++) { await Task.Delay(1); Console.WriteLine(""step "" + i); }
        try { await Task.Delay(1); throw new Exception(""handled inside""); }
        catch (Exception e) { Console.WriteLine(""caught "" + e.Message); }
        finally { Console.WriteLine(""finally""); }
    }

    public static async Task Main()
    {
        EarlyOut(0);
        await Task.Delay(100);
        EarlyOut(2);
        await Task.Delay(100);
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public async Task AFaultedEventHandlerLeavesTheOtherSubscribersAlone()
        {
            // The case an application meets first: several handlers on one event, one of them an async
            // void that fails. A multicast invocation runs every subscriber and returns synchronously —
            // the failure surfaces later, through the handler, and takes nothing with it.
            var output = await RunTest(@"
using System;
using System.Threading.Tasks;
using Transpose;

public class Button
{
    public event EventHandler<string> Clicked;
    public void Click(string arg) { Clicked?.Invoke(this, arg); }
}

public class Program
{
    public static void Main()
    {
        Script.SetUnhandledExceptionHandler(ex => Console.WriteLine(""H: "" + ex.Message));
        var b = new Button();
        b.Clicked += async (s, e) => { await Task.Delay(1); throw new Exception(""subscriber one "" + e); };
        b.Clicked += (s, e) => Console.WriteLine(""subscriber two ran"");
        b.Clicked += async (s, e) => { await Task.Delay(1); Console.WriteLine(""subscriber three ran""); };
        b.Click(""go"");
        Console.WriteLine(""Click returned"");
    }
}", skipRoslyn: true);

            // The synchronous subscriber and the raise site are untouched by the one that will fail.
            StringAssert.Contains(output, "subscriber two ran");
            StringAssert.Contains(output, "Click returned");
            StringAssert.Contains(output, "subscriber three ran");
            StringAssert.Contains(output, "H: subscriber one go");
            // Click() returns before any of the async bodies resume, as it does in .NET.
            Assert.IsTrue(output.IndexOf("Click returned", StringComparison.Ordinal)
                        < output.IndexOf("H: subscriber one go", StringComparison.Ordinal),
                "the raise site returned before the fault was reported\n" + output);
        }

        private static int CountOf(string haystack, string needle)
        {
            var n = 0;
            for (var i = haystack.IndexOf(needle, System.StringComparison.Ordinal); i >= 0;
                 i = haystack.IndexOf(needle, i + needle.Length, System.StringComparison.Ordinal)) n++;
            return n;
        }
    }
}
