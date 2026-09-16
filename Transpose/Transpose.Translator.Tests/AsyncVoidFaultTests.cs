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

        private static int CountOf(string haystack, string needle)
        {
            var n = 0;
            for (var i = haystack.IndexOf(needle, System.StringComparison.Ordinal); i >= 0;
                 i = haystack.IndexOf(needle, i + needle.Length, System.StringComparison.Ordinal)) n++;
            return n;
        }
    }
}
