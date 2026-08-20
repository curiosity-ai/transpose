using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// <c>AggregateException</c> and the task faults that produce one. This is where a failure gets
    /// lost most easily, because a task's exception is reported through three different shapes
    /// depending on how it is observed — <c>await</c> throws the first inner exception unwrapped,
    /// <c>.Result</c> and <c>.Wait()</c> throw the aggregate, and <c>task.Exception</c> hands it to
    /// you without throwing — and every one of them has to agree about what actually failed.
    ///
    /// Every case runs natively as well and the two outputs are diffed, which is what pins the
    /// message text and the shapes down rather than an opinion about them.
    /// </summary>
    [TestClass]
    public class AggregateExceptionTests : TranslatorTestBase
    {
        /// <summary>
        /// The aggregate's own surface: its inner list, that <c>InnerException</c> is the first of
        /// them, and — the part that is easy to leave out and expensive to leave out — that its
        /// <c>Message</c> NAMES every inner message. "One or more errors occurred." on its own is
        /// what a logger prints, and it says nothing whatsoever about what failed.
        /// </summary>
        [TestMethod]
        public async Task AnAggregatesMessageNamesEveryInnerExceptionAsync()
        {
            await RunTest(@"
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var a = new AggregateException(""two failed"",
            new FormatException(""first""),
            new InvalidOperationException(""second""));

        Console.WriteLine(""count: "" + a.InnerExceptions.Count);
        Console.WriteLine(""[0]: "" + a.InnerExceptions[0].GetType().FullName + "" / "" + a.InnerExceptions[0].Message);
        Console.WriteLine(""[1]: "" + a.InnerExceptions[1].GetType().FullName + "" / "" + a.InnerExceptions[1].Message);
        Console.WriteLine(""InnerException is the first: "" + ReferenceEquals(a.InnerException, a.InnerExceptions[0]));
        Console.WriteLine(""message: |"" + a.Message + ""|"");

        Console.WriteLine(""default message: |"" + new AggregateException(new FormatException(""only"")).Message + ""|"");
        Console.WriteLine(""no inner: |"" + new AggregateException(""nothing inside"").Message + ""|"");
        Console.WriteLine(""nothing at all: |"" + new AggregateException().Message + ""|"");

        // ToString lists the inner exceptions base.ToString() cannot reach - i.e. all but the first.
        var text = a.ToString();
        Console.WriteLine(""prints the composed message: "" + text.Contains(""System.AggregateException: two failed (first) (second)""));
        Console.WriteLine(""prints the first as the chain: "" + text.Contains("" ---> System.FormatException: first""));
        Console.WriteLine(""prints the second by index: "" + text.Contains(""(Inner Exception #1) System.InvalidOperationException: second""));
        Console.WriteLine(""does not index the first: "" + !text.Contains(""(Inner Exception #0)""));
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        /// <summary>
        /// <c>Flatten()</c> collapses an arbitrarily deep tree of aggregates into one level, and
        /// carries over the text the aggregate was CONSTRUCTED with rather than its composed
        /// <c>Message</c> — composing again over the flattened list would repeat every message the
        /// composed text already names. <c>GetBaseException()</c> digs through single-inner
        /// aggregates to the one real exception and gives up as soon as there is more than one.
        /// </summary>
        [TestMethod]
        public async Task FlattenCollapsesNestedAggregatesAndKeepsTheBaseMessageAsync()
        {
            await RunTest(@"
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var nested = new AggregateException(""outer"",
            new AggregateException(""middle"",
                new FormatException(""leaf one""),
                new AggregateException(""deep"", new NotSupportedException(""leaf two""))),
            new ArgumentException(""leaf three""));

        Console.WriteLine(""nested count: "" + nested.InnerExceptions.Count);
        Console.WriteLine(""nested message: |"" + nested.Message + ""|"");

        var flat = nested.Flatten();

        Console.WriteLine(""flat count: "" + flat.InnerExceptions.Count);
        Console.WriteLine(""no aggregate left: "" + !flat.InnerExceptions.Any(e => e is AggregateException));
        Console.WriteLine(""flat messages: "" + string.Join("","", flat.InnerExceptions.Select(e => e.Message).OrderBy(m => m)));
        Console.WriteLine(""flat message: |"" + flat.Message + ""|"");

        var single = new AggregateException(new AggregateException(new FormatException(""only one"")));
        Console.WriteLine(""base of nested singles: "" + single.GetBaseException().GetType().FullName + "" / "" + single.GetBaseException().Message);

        var many = new AggregateException(new FormatException(""x""), new FormatException(""y""));
        Console.WriteLine(""base of many: "" + many.GetBaseException().GetType().FullName);
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        /// <summary>
        /// <c>Handle</c>: the predicate consumes what it answers true for, and anything left is
        /// rethrown as a NEW aggregate carrying only those. Note the message of that new aggregate —
        /// .NET builds it from the composed <c>Message</c>, so an already-named inner message is
        /// named twice; that is .NET's behaviour and the native diff is what says so.
        /// </summary>
        [TestMethod]
        public async Task HandleConsumesWhatThePredicateAcceptsAndRethrowsTheRestAsync()
        {
            await RunTest(@"
using System;

public class Program
{
    public static void Main()
    {
        var partly = new AggregateException(new FormatException(""handled""), new NotSupportedException(""unhandled""));

        try
        {
            partly.Handle(e => e is FormatException);
            Console.WriteLine(""NOT REACHED"");
        }
        catch (AggregateException rest)
        {
            Console.WriteLine(""rethrown count: "" + rest.InnerExceptions.Count);
            Console.WriteLine(""rethrown: "" + rest.InnerExceptions[0].GetType().Name + "" / "" + rest.InnerExceptions[0].Message);
            Console.WriteLine(""rethrown message: |"" + rest.Message + ""|"");
        }

        new AggregateException(new FormatException(""a""), new FormatException(""b"")).Handle(e => true);
        Console.WriteLine(""everything handled, nothing thrown"");

        var seen = 0;
        try { new AggregateException(new FormatException(""x""), new FormatException(""y"")).Handle(e => { seen++; return true; }); }
        catch (Exception) { Console.WriteLine(""NOT REACHED""); }
        Console.WriteLine(""predicate saw: "" + seen);
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        /// <summary>
        /// <c>Task.WhenAll</c> with more than one task failing. Three separate things have to hold:
        /// awaiting throws ONE exception unwrapped (which of them is not specified — the aggregate
        /// is built in completion order, so this asserts the set rather than the pick), the WhenAll
        /// task's own <c>Exception</c> carries every failure, and the tasks that succeeded still
        /// have their results.
        /// </summary>
        [TestMethod]
        public async Task WhenAllCollectsEveryFailureWhileKeepingTheSuccessesAsync()
        {
            await RunTest(@"
using System;
using System.Linq;
using System.Threading.Tasks;

public class Program
{
    static async Task<int> Fail(string message, int delay)
    {
        await Task.Delay(delay);
        throw new InvalidOperationException(message);
    }

    static async Task<int> Succeed(int value, int delay)
    {
        await Task.Delay(delay);
        return value;
    }

    public static async Task Main()
    {
        var a = Fail(""first failure"", 10);
        var b = Fail(""second failure"", 20);
        var c = Succeed(3, 5);
        var all = Task.WhenAll(a, b, c);

        try { await all; Console.WriteLine(""NOT REACHED""); }
        catch (Exception e) { Console.WriteLine(""await threw one, unwrapped: "" + e.GetType().FullName); }

        Console.WriteLine(""faulted: "" + all.IsFaulted);
        Console.WriteLine(""aggregate count: "" + all.Exception.InnerExceptions.Count);
        Console.WriteLine(""aggregate messages: "" + string.Join("","", all.Exception.InnerExceptions.Select(e => e.Message).OrderBy(m => m)));
        Console.WriteLine(""aggregate message names both: ""
            + (all.Exception.Message.Contains(""first failure"") && all.Exception.Message.Contains(""second failure"")));
        Console.WriteLine(""per task: "" + a.IsFaulted + "" "" + b.IsFaulted + "" "" + c.IsFaulted);
        Console.WriteLine(""the success kept its result: "" + c.Result);
        Console.WriteLine(""each task's own aggregate: "" + a.Exception.InnerExceptions.Count + "" / "" + a.Exception.InnerException.Message);

        // WhenAll<T> throws before it can return the array, but the tasks that finished are intact.
        var ok = Task.FromResult(7);
        var bad = Fail(""in WhenAll<T>"", 1);
        try { var r = await Task.WhenAll(ok, bad); Console.WriteLine(""NOT REACHED "" + r.Length); }
        catch (InvalidOperationException e) { Console.WriteLine(""WhenAll<T> threw: "" + e.Message); }
        Console.WriteLine(""ok still has its result: "" + ok.Result);
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        /// <summary>
        /// A <c>WhenAll</c> of <c>WhenAll</c>s: the outer aggregate holds the leaves rather than a
        /// tree of aggregates (WhenAll ranges over its antecedents' inner exceptions), and
        /// <c>Flatten()</c> is a no-op on it. A <c>TaskCompletionSource</c> faulted with several
        /// exceptions at once behaves the same way, and refuses to be completed a second time.
        /// </summary>
        [TestMethod]
        public async Task NestedWhenAllAndAMultiExceptionCompletionSourceReportEveryLeafAsync()
        {
            await RunTest(@"
using System;
using System.Linq;
using System.Threading.Tasks;

public class Program
{
    static async Task Fail(string m) { await Task.Yield(); throw new FormatException(m); }

    public static async Task Main()
    {
        var outer = Task.WhenAll(Task.WhenAll(Fail(""a1""), Fail(""a2"")), Task.WhenAll(Fail(""b1"")));

        try { await outer; Console.WriteLine(""NOT REACHED""); }
        catch (FormatException) { Console.WriteLine(""await threw a leaf, unwrapped""); }

        var agg = outer.Exception;
        Console.WriteLine(""leaves, not a tree: "" + agg.InnerExceptions.Count
            + "" / "" + string.Join("","", agg.InnerExceptions.Select(x => x.GetType().Name).Distinct()));
        Console.WriteLine(""messages: "" + string.Join("","", agg.InnerExceptions.Select(x => x.Message).OrderBy(x => x)));
        Console.WriteLine(""flatten changes nothing: "" + agg.Flatten().InnerExceptions.Count);

        var tcs = new TaskCompletionSource<int>();
        Console.WriteLine(""set two at once: "" + tcs.TrySetException(new Exception[] { new FormatException(""tcs one""), new NotSupportedException(""tcs two"") }));

        try { await tcs.Task; Console.WriteLine(""NOT REACHED""); }
        catch (Exception e) { Console.WriteLine(""awaited: "" + e.GetType().Name + "" / "" + e.Message); }

        Console.WriteLine(""tcs count: "" + tcs.Task.Exception.InnerExceptions.Count);
        Console.WriteLine(""tcs message names both: ""
            + (tcs.Task.Exception.Message.Contains(""tcs one"") && tcs.Task.Exception.Message.Contains(""tcs two"")));
        Console.WriteLine(""a completed task refuses a result: "" + tcs.TrySetResult(1));
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        /// <summary>
        /// The three ways of observing a faulted task disagree on purpose, and all three have to be
        /// right: <c>await</c> and <c>GetAwaiter().GetResult()</c> throw the inner exception
        /// unwrapped, while <c>.Result</c> and <c>.Wait()</c> throw the <c>AggregateException</c>.
        /// <c>Wait()</c> is the one that used to report nothing at all — it was emitted as a call
        /// whose returned task nobody looked at, so a faulted task's exception was discarded by the
        /// single method that exists to observe it.
        /// </summary>
        [TestMethod]
        public async Task ResultAndWaitThrowTheAggregateWhereAwaitUnwrapsItAsync()
        {
            await RunTest(@"
using System;
using System.Threading.Tasks;

public class Program
{
    static async Task<int> Fail(string m) { await Task.Yield(); throw new FormatException(m); }

    public static async Task Main()
    {
        var viaResult = Fail(""via Result"");
        try { await viaResult; } catch (Exception) { }
        try { var _ = viaResult.Result; Console.WriteLine(""NOT REACHED""); }
        catch (AggregateException e) { Console.WriteLine(""Result threw the aggregate: "" + e.InnerExceptions.Count + "" / "" + e.InnerException.Message); }

        var viaGetResult = Fail(""via GetResult"");
        try { await viaGetResult; } catch (Exception) { }
        try { viaGetResult.GetAwaiter().GetResult(); Console.WriteLine(""NOT REACHED""); }
        catch (AggregateException) { Console.WriteLine(""GetResult threw an aggregate - it should not""); }
        catch (FormatException e) { Console.WriteLine(""GetResult threw it unwrapped: "" + e.Message); }

        var viaWait = Fail(""via Wait"");
        try { await viaWait; } catch (Exception) { }
        try { viaWait.Wait(); Console.WriteLine(""NOT REACHED""); }
        catch (AggregateException e) { Console.WriteLine(""Wait threw the aggregate: "" + e.InnerExceptions.Count + "" / "" + e.InnerException.Message); }

        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        /// <summary>
        /// <c>Task.FromException</c> and the readers of what it produces. <c>Task.Exception</c> is an
        /// <c>AggregateException</c> in .NET always — it wraps whatever it is given, an aggregate
        /// included — and everything downstream leans on that: awaiting unwraps one level, and
        /// <c>WhenAll</c>/<c>WhenAny</c> range over the inner list. A bare exception stored there
        /// instead left the fault unreachable from the task and took WhenAll's continuation down with
        /// it, which completed nothing and hung every awaiter in silence.
        /// </summary>
        [TestMethod]
        public async Task FromExceptionAlwaysReportsAnAggregateThatWhenAllCanReadAsync()
        {
            await RunTest(@"
using System;
using System.Linq;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        var bare = Task.FromException(new FormatException(""bare fault""));
        Console.WriteLine(""Exception is an aggregate: "" + (bare.Exception is AggregateException));
        Console.WriteLine(""count: "" + bare.Exception.InnerExceptions.Count + "" / "" + bare.Exception.InnerException.Message);
        try { await bare; Console.WriteLine(""NOT REACHED""); }
        catch (FormatException e) { Console.WriteLine(""awaited: "" + e.Message); }

        // Even an AggregateException is wrapped rather than adopted.
        var inner = new AggregateException(""agg"", new FormatException(""leaf""));
        var wrapped = Task.FromException(inner);
        Console.WriteLine(""wraps an aggregate too: "" + wrapped.Exception.InnerException.GetType().FullName
            + "" / same instance: "" + ReferenceEquals(wrapped.Exception.InnerException, inner));

        // WhenAll and WhenAny over it: both used to be fed a task with no inner list to read.
        var all = Task.WhenAll(Task.FromException(new FormatException(""bare in WhenAll"")), Task.CompletedTask);
        try { await all; Console.WriteLine(""NOT REACHED""); }
        catch (FormatException e) { Console.WriteLine(""WhenAll: "" + e.Message); }
        Console.WriteLine(""WhenAll count: "" + all.Exception.InnerExceptions.Count);

        var any = await Task.WhenAny(Task.FromException(new NotSupportedException(""bare in WhenAny"")));
        Console.WriteLine(""WhenAny: "" + any.IsFaulted + "" / "" + any.Exception.InnerException.Message);

        // The typed form, and a task faulted through a continuation that threw.
        var typed = Task.FromException<int>(new FormatException(""typed""));
        Console.WriteLine(""typed: "" + typed.IsFaulted + "" / "" + typed.Exception.InnerException.Message);

        var chained = Task.FromResult(1).ContinueWith<int>(t => throw new NotSupportedException(""from the continuation""));
        try { await chained; Console.WriteLine(""NOT REACHED""); }
        catch (NotSupportedException e) { Console.WriteLine(""continuation faulted the continuation: "" + e.Message); }

        // A continuation over a faulted antecedent OBSERVES it rather than throwing.
        var observed = Task.FromException(new FormatException(""seen by ContinueWith""))
            .ContinueWith(t => t.IsFaulted + "" / "" + t.Exception.InnerException.Message);
        Console.WriteLine(""continuation saw: "" + await observed);
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        /// <summary>
        /// A cancelled task reports <c>TaskCanceledException</c> (an
        /// <c>OperationCanceledException</c>) and <c>IsCanceled</c>, and a <c>WhenAll</c> over a
        /// cancelled task beside a faulted one is FAULTED, not cancelled — a fault outranks a
        /// cancellation, so the failure is not quietly reclassified as "the user changed their mind".
        /// </summary>
        [TestMethod]
        public async Task CancellationIsReportedAsCancelledAndAFaultOutranksItAsync()
        {
            await RunTest(@"
using System;
using System.Threading.Tasks;

public class Program
{
    static async Task Fail(string m) { await Task.Yield(); throw new FormatException(m); }

    public static async Task Main()
    {
        var tcs = new TaskCompletionSource<int>();
        tcs.TrySetCanceled();

        try { await tcs.Task; Console.WriteLine(""NOT REACHED""); }
        catch (OperationCanceledException e)
        {
            Console.WriteLine(""cancelled: is TaskCanceledException="" + (e is TaskCanceledException)
                + "" IsCanceled="" + tcs.Task.IsCanceled + "" IsFaulted="" + tcs.Task.IsFaulted);
        }

        var mixed = Task.WhenAll(Fail(""a fault beside a cancel""), tcs.Task);
        try { await mixed; Console.WriteLine(""NOT REACHED""); }
        catch (Exception e) { Console.WriteLine(""mixed threw: "" + e.GetType().Name); }
        Console.WriteLine(""mixed faulted: "" + mixed.IsFaulted + "" cancelled: "" + mixed.IsCanceled);

        var onlyCancelled = Task.WhenAll(tcs.Task, Task.CompletedTask);
        try { await onlyCancelled; Console.WriteLine(""NOT REACHED""); }
        catch (OperationCanceledException) { Console.WriteLine(""all-cancelled threw a cancellation""); }
        Console.WriteLine(""all-cancelled: faulted="" + onlyCancelled.IsFaulted + "" cancelled="" + onlyCancelled.IsCanceled);
        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }
    }
}
