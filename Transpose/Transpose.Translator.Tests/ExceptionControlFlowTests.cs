using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// The control flow around <c>throw</c> — which handler runs, in what order the
    /// <c>finally</c> blocks unwind, what a rethrow carries with it, and what an exception filter
    /// is allowed to do. Every case here runs natively as well and the two outputs are diffed, so
    /// what is asserted is .NET's behaviour rather than a reading of it.
    ///
    /// These sit alongside <see cref="Ported.ExceptionsTests"/> (the basic shapes, ported from h5)
    /// and <see cref="ExceptionStackTraceTests"/> (what survives the JavaScript → C# boundary): this
    /// file is the deep end of the language semantics — several frames, several handlers, and the
    /// interaction with <c>using</c>, iterators and <c>async</c>.
    /// </summary>
    [TestClass]
    public class ExceptionControlFlowTests : TranslatorTestBase
    {
        /// <summary>
        /// Four frames, each doing something different on the way out: a non-matching catch, a bare
        /// rethrow, a wrap-and-throw, and a finally on every one of them. The order the log comes
        /// out in is the whole assertion — a finally that runs too early or a rethrow that loses the
        /// original type shows up as a diff against native.
        /// </summary>
        [TestMethod]
        public async Task NestedFramesUnwindInOrderAndARethrowKeepsTheOriginalAsync()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;

public class Program
{
    static List<string> log = new List<string>();

    static void Inner()
    {
        try
        {
            log.Add(""inner try"");
            throw new InvalidOperationException(""from inner"");
        }
        catch (FormatException)
        {
            log.Add(""inner WRONG catch"");
        }
        finally
        {
            log.Add(""inner finally"");
        }
    }

    static void Middle()
    {
        try { Inner(); }
        catch (InvalidOperationException e)
        {
            log.Add(""middle catch: "" + e.Message);
            throw;
        }
        finally { log.Add(""middle finally""); }
    }

    static void Wrapper()
    {
        try { Middle(); }
        catch (Exception e)
        {
            log.Add(""wrapper wraps: "" + e.Message);
            throw new ApplicationException(""wrapped"", e);
        }
        finally { log.Add(""wrapper finally""); }
    }

    public static void Main()
    {
        try { Wrapper(); }
        catch (ApplicationException e)
        {
            log.Add(""outer: "" + e.Message);
            log.Add(""outer inner type: "" + e.InnerException.GetType().FullName);
            log.Add(""outer inner msg: "" + e.InnerException.Message);
        }
        finally { log.Add(""outer finally""); }

        log.Add(""returned: "" + WithReturn());

        try { NestedFinallyThrows(); }
        catch (Exception e) { log.Add(""replaced by: "" + e.GetType().Name + "" / "" + e.Message); }

        foreach (var line in log) Console.WriteLine(line);
        Console.WriteLine(""<<DONE>>"");
    }

    // finally runs even when the try returns, and the returned value is the one computed first.
    static int WithReturn()
    {
        try { log.Add(""wr try""); return 1; }
        finally { log.Add(""wr finally""); }
    }

    // A finally that throws while an exception is already in flight REPLACES it: the original is
    // gone, which is why a finally is the wrong place to do anything that can fail.
    static void NestedFinallyThrows()
    {
        try { throw new FormatException(""original""); }
        finally { throw new NotSupportedException(""from finally""); }
    }
}", waitForOutput: "<<DONE>>");
        }

        /// <summary>
        /// A three-level inner-exception chain: every level's message and type is reachable,
        /// <c>GetBaseException()</c> digs to the bottom, and <c>ToString()</c> reports the whole
        /// chain outermost-first with a marker per level. The <c>ToString()</c> half is what makes a
        /// wrapped failure legible at all in a log — before it reported the outer exception and
        /// nothing about the one that actually failed.
        /// </summary>
        [TestMethod]
        public async Task AThreeLevelInnerChainIsFullyReachableAndPrintedAsync()
        {
            await RunTest(@"
using System;

public class Program
{
    public static void Main()
    {
        var l1 = new FormatException(""level one"");
        var l2 = new InvalidOperationException(""level two"", l1);
        var l3 = new ApplicationException(""level three"", l2);

        Console.WriteLine(""outer: "" + l3.Message);
        Console.WriteLine(""inner: "" + l3.InnerException.Message);
        Console.WriteLine(""inner.inner: "" + l3.InnerException.InnerException.Message);
        Console.WriteLine(""bottom of the chain: "" + (l3.InnerException.InnerException.InnerException == null));
        Console.WriteLine(""base: "" + l3.GetBaseException().GetType().FullName + "" / "" + l3.GetBaseException().Message);

        var text = l3.ToString();

        Console.WriteLine(""chain markers: "" + Count(text, "" ---> ""));
        Console.WriteLine(""chain ends: "" + Count(text, ""--- End of inner exception stack trace ---""));
        Console.WriteLine(""names all three: "" + (text.Contains(""level one"") && text.Contains(""level two"") && text.Contains(""level three"")));
        Console.WriteLine(""outermost first: "" + (text.IndexOf(""level three"") < text.IndexOf(""level two"") && text.IndexOf(""level two"") < text.IndexOf(""level one"")));
        Console.WriteLine(""<<DONE>>"");
    }

    static int Count(string s, string needle)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(needle, i)) >= 0) { n++; i += needle.Length; }
        return n;
    }
}", waitForOutput: "<<DONE>>");
        }

        /// <summary>
        /// Exception filters. Three properties are being pinned down, and each was a real
        /// possibility of getting wrong: the filters run in source order and only until one answers
        /// true; a clause whose filter answers false does not handle the exception (so the frame's
        /// finally still runs and the exception keeps going); and a filter that THROWS is swallowed
        /// by the CLR and read as "does not match", leaving the exception already in flight to carry
        /// on. That last one is the one that matters in practice — a null dereference inside a
        /// <c>when</c> clause replacing the error the handler existed to report is a fault nobody
        /// can debug, and it is what evaluating the filter inline used to do.
        /// </summary>
        [TestMethod]
        public async Task ExceptionFiltersRunInOrderAndAThrowingFilterDoesNotMatchAsync()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;

public class MyException : Exception
{
    public int Code { get; }
    public MyException(string message, int code) : base(message) { Code = code; }
    public MyException(string message, int code, Exception inner) : base(message, inner) { Code = code; }
}

public class Program
{
    static List<string> log = new List<string>();

    static bool Probe(string name, bool answer) { log.Add(""filter "" + name + "" -> "" + answer); return answer; }

    public static void Main()
    {
        try
        {
            try { throw new MyException(""code 42"", 42); }
            catch (MyException e) when (Probe(""code==7"", e.Code == 7)) { log.Add(""WRONG""); }
            catch (MyException e) when (Probe(""code==42"", e.Code == 42)) { log.Add(""right: "" + e.Message); }
            catch (MyException) when (Probe(""never evaluated"", true)) { log.Add(""WRONG""); }
            finally { log.Add(""inner finally""); }
        }
        catch (Exception) { log.Add(""NOT REACHED""); }

        // No filter matches: the exception propagates, and this frame's finally still runs.
        try
        {
            try { throw new MyException(""code 1"", 1); }
            catch (MyException e) when (Probe(""code==2"", e.Code == 2)) { log.Add(""WRONG""); }
            finally { log.Add(""unmatched finally""); }
        }
        catch (MyException e) { log.Add(""propagated: "" + e.Message); }

        // A filter can inspect the whole chain, and a pattern variable declared in it is in scope
        // in the handler body.
        try { throw new MyException(""outer"", 9, new FormatException(""the cause"")); }
        catch (MyException e) when (e.InnerException is FormatException f && f.Message == ""the cause"")
        {
            log.Add(""filter matched on the chain: "" + f.Message);
        }

        // A throwing filter is swallowed and counts as false: the ORIGINAL exception is the one
        // that reaches the outer handler, not the filter's.
        try
        {
            try { throw new FormatException(""under a throwing filter""); }
            catch (Exception) when (Throws()) { log.Add(""WRONG""); }
        }
        catch (Exception e) { log.Add(""after a throwing filter: "" + e.GetType().Name + "" / "" + e.Message); }

        foreach (var line in log) Console.WriteLine(line);
        Console.WriteLine(""<<DONE>>"");
    }

    static bool Throws() { log.Add(""filter throws""); throw new NotSupportedException(""inside the filter""); }
}", waitForOutput: "<<DONE>>");
        }

        /// <summary>
        /// The emit-shape half of the case above. A filter has to be evaluated somewhere a throw can
        /// be caught, so it goes through <c>TransposeR.filter</c>; inline in the guard there is
        /// nowhere for the CLR's "swallow it and read the filter as false" rule to live. The arrow
        /// is plain rather than <c>async</c> on purpose — C# forbids <c>await</c> in a filter
        /// expression, so no <c>await</c> can ever appear inside it.
        /// </summary>
        [TestMethod]
        public void AFilterIsEvaluatedThroughTheHelperThatSwallowsItsOwnThrow()
        {
            var code = @"
using System;

public class Program
{
    public static string Read()
    {
        try { throw new InvalidOperationException(""x""); }
        catch (Exception e) when (e.Message == ""x"") { return ""filtered""; }
        catch (Exception) { return ""not filtered""; }
    }
    public static void Main() { }
}";
            var result = new RoslynTranslator().Translate(code);

            Assert.IsTrue(result.Success, "translation should succeed");
            StringAssert.Contains(result.Javascript!, "TransposeR.filter(() => (",
                "a catch filter must be evaluated through the helper, so a throw inside it reads as "
                + "\"does not match\" instead of replacing the exception being handled");
        }

        /// <summary>
        /// <c>using</c> and iterators, which are both just <c>try</c>/<c>finally</c> underneath:
        /// nested resources dispose innermost-first while an exception is in flight, a Dispose that
        /// throws replaces the body's exception (same rule as any other finally), an iterator's
        /// finally runs when its body throws mid-enumeration, and it runs again when the consumer
        /// breaks out early — i.e. the enumerator really is disposed.
        /// </summary>
        [TestMethod]
        public async Task UsingAndIteratorFinallyBlocksRunWhileAnExceptionIsInFlightAsync()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;

class Res : IDisposable
{
    readonly string name; readonly bool failOnDispose;
    public Res(string name, bool failOnDispose = false) { this.name = name; this.failOnDispose = failOnDispose; Console.WriteLine(""open "" + name); }
    public void Dispose()
    {
        Console.WriteLine(""dispose "" + name);
        if (failOnDispose) throw new NotSupportedException(""dispose of "" + name + "" failed"");
    }
}

public class Program
{
    static IEnumerable<int> Numbers()
    {
        try
        {
            yield return 1;
            yield return 2;
            throw new InvalidOperationException(""iterator blew up at 3"");
        }
        finally
        {
            Console.WriteLine(""iterator finally"");
        }
    }

    public static void Main()
    {
        try
        {
            using (var a = new Res(""a""))
            using (var b = new Res(""b""))
            {
                throw new FormatException(""inside the usings"");
            }
        }
        catch (Exception e) { Console.WriteLine(""caught "" + e.GetType().Name + "": "" + e.Message); }

        try
        {
            using (var c = new Res(""c"", failOnDispose: true))
            {
                throw new FormatException(""body failed first"");
            }
        }
        catch (Exception e) { Console.WriteLine(""after a throwing dispose: "" + e.GetType().Name + "" / "" + e.Message); }

        try
        {
            foreach (var n in Numbers()) Console.WriteLine(""got "" + n);
        }
        catch (Exception e) { Console.WriteLine(""iterator threw: "" + e.Message); }

        foreach (var n in Numbers()) { Console.WriteLine(""partial "" + n); break; }

        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        /// <summary>
        /// The same unwinding through <c>async</c> frames, where every one of them is a separate
        /// state machine: a three-deep chain wraps and keeps the chain, a <c>finally</c> at the top
        /// of it runs, and both a <c>catch</c> and a <c>finally</c> can themselves <c>await</c> —
        /// including a catch that throws something new after awaiting, which has to reach the
        /// handler outside the finally rather than being lost in it.
        /// </summary>
        [TestMethod]
        public async Task ExceptionsUnwindThroughAChainOfAsyncFramesAsync()
        {
            await RunTest(@"
using System;
using System.Threading.Tasks;

public class Program
{
    static async Task<int> Layer3() { await Task.Yield(); throw new FormatException(""deepest""); }

    static async Task<int> Layer2()
    {
        try { return await Layer3(); }
        catch (FormatException e) { throw new InvalidOperationException(""layer 2"", e); }
    }

    static async Task<int> Layer1()
    {
        try { return await Layer2(); }
        finally { Console.WriteLine(""layer 1 finally""); }
    }

    public static async Task Main()
    {
        try { await Layer1(); }
        catch (Exception e)
        {
            Console.WriteLine(""outer: "" + e.GetType().Name + "" / "" + e.Message);
            Console.WriteLine(""inner: "" + e.InnerException.GetType().Name + "" / "" + e.InnerException.Message);
        }

        try
        {
            try { await Layer3(); }
            catch (Exception e)
            {
                await Task.Yield();
                Console.WriteLine(""awaited in catch: "" + e.Message);
                throw new NotSupportedException(""from the catch"");
            }
            finally
            {
                await Task.Yield();
                Console.WriteLine(""awaited in finally"");
            }
        }
        catch (Exception e) { Console.WriteLine(""escaped: "" + e.Message); }

        Console.WriteLine(""<<DONE>>"");
    }
}", waitForOutput: "<<DONE>>");
        }

        /// <summary>
        /// Which handler a type-matched catch picks out of a hierarchy (most derived first, and the
        /// first match wins — not the best match), and that the exceptions the runtime itself raises
        /// for a failed operation are the .NET types a handler names: an out-of-range index, a bad
        /// parse, a missing dictionary key, a list index. A lambda deep inside a LINQ pipeline
        /// throwing surfaces where the sequence is enumerated, not where it was declared.
        /// </summary>
        [TestMethod]
        public async Task RuntimeFailuresRaiseTheirDotNetTypesAndMatchTheNearestHandlerAsync()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class BaseError : Exception { public BaseError(string m) : base(m) { } }
public class MidError : BaseError { public MidError(string m) : base(m) { } }
public class LeafError : MidError { public LeafError(string m) : base(m) { } }

public class Program
{
    static void Throw(Exception e) => throw e;

    static string Classify(Exception e)
    {
        try { Throw(e); }
        catch (LeafError x) { return ""leaf: "" + x.Message; }
        catch (MidError x) { return ""mid: "" + x.Message; }
        catch (BaseError x) { return ""base: "" + x.Message; }
        catch (Exception x) { return ""other: "" + x.GetType().Name; }
        return ""unreachable"";
    }

    public static async Task Main()
    {
        Console.WriteLine(Classify(new LeafError(""L"")));
        Console.WriteLine(Classify(new MidError(""M"")));
        Console.WriteLine(Classify(new BaseError(""B"")));
        Console.WriteLine(Classify(new FormatException(""F"")));

        Exception any = new LeafError(""cast"");
        Console.WriteLine(""is MidError: "" + (any is MidError) + "" / is BaseError: "" + (any is BaseError));

        int[] arr = new int[2];
        try { arr[5] = 1; } catch (IndexOutOfRangeException e) { Console.WriteLine(""index: "" + e.GetType().FullName); }

        try { var _ = int.Parse(""nope""); } catch (FormatException e) { Console.WriteLine(""parse: "" + e.GetType().FullName); }

        var dict = new Dictionary<string, int>();
        try { var _ = dict[""absent""]; } catch (KeyNotFoundException e) { Console.WriteLine(""key: "" + e.GetType().FullName); }

        var list = new List<int>();
        try { var _ = list[0]; } catch (ArgumentOutOfRangeException e) { Console.WriteLine(""list: "" + e.GetType().FullName); }

        // A projection that throws surfaces at enumeration, after the elements before it.
        var q = new[] { 1, 2, 0, 4 }.Select(x => 10 / x);
        try { foreach (var v in q) Console.WriteLine(""value "" + v); }
        catch (DivideByZeroException e) { Console.WriteLine(""linq threw: "" + e.GetType().FullName); }

        // A throwing property getter, reached through a Task.Run body.
        try { await Task.Run(() => { var _ = Bad; }); }
        catch (Exception e) { Console.WriteLine(""in Task.Run: "" + e.GetType().FullName + "" / "" + e.Message); }

        // A closure returned by a closure.
        Func<Func<int>> nested = () => () => throw new FormatException(""from a nested closure"");
        try { nested()(); }
        catch (Exception e) { Console.WriteLine(""closure: "" + e.Message); }

        Console.WriteLine(""<<DONE>>"");
    }

    static int Bad => throw new InvalidOperationException(""from a property getter"");
}", waitForOutput: "<<DONE>>");
        }
    }
}
