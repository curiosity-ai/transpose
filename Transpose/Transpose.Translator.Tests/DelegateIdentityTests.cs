using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Converting an instance method group to a delegate, and the identity of the delegate that comes
    /// out of it.
    ///
    /// <para>
    /// The emitter used to write <c>(recv).M.bind(recv)</c>, and
    /// <c>Function.prototype.bind</c> mints a fresh function on every conversion. The reported shape
    /// is an event handler: <c>el.RemoveEventListener("click", OnClick)</c> hands the DOM a different
    /// function than <c>el.AddEventListener("click", OnClick)</c> did, so the handler is never
    /// removed and stays attached for the life of the page. The same cause made
    /// <c>Action a = OnClick, b = OnClick; a == b</c> answer false where .NET answers true — a bound
    /// function carries none of the target/method identity <c>Delegate.Equals</c> compares — and made
    /// the receiver expression evaluate TWICE, so <c>Get().OnClick</c> called <c>Get()</c> twice and
    /// bound the second object to a method read off the first.
    /// </para>
    ///
    /// <para>
    /// All three are one fix: <c>Transpose.fn.cacheBindMember</c>, which takes the receiver once and
    /// returns the cached delegate for that (receiver, method, bound type arguments) triple — the
    /// mechanism h5 used, which was already present in the runtime as <c>fn.cacheBind</c> and simply
    /// never called.
    /// </para>
    /// </summary>
    [TestClass]
    public class DelegateIdentityTests : TranslatorTestBase
    {
        /// <summary>
        /// The report: add and remove have to hand the listener the same function, or the handler
        /// never comes off. Modelled on a real <c>addEventListener</c>/<c>removeEventListener</c>
        /// pair, with the registry kept in JS so the test sees exactly what the DOM would.
        /// </summary>
        [TestMethod]
        public async Task AddAndRemoveEventListenerSeeTheSameFunction()
        {
            var output = await RunTest("""
using System;
using Transpose;

public class Emitter
{
    private readonly object _target;
    public Emitter(object target) { _target = target; }
    public void Add(string name, Action<object> h)    => Script.Write("{0}.add({1}, {2})", _target, name, h);
    public void Remove(string name, Action<object> h) => Script.Write("{0}.remove({1}, {2})", _target, name, h);
}

public class Widget
{
    public int Clicks;
    public void OnClick(object e) { Clicks++; }

    public void Run()
    {
        var target = Script.Write<object>(@"({
            handlers: [],
            add: function (n, h) { this.handlers.push([n, h]); },
            remove: function (n, h) { var i = this.handlers.findIndex(function (x) { return x[0] === n && x[1] === h; }); if (i >= 0) this.handlers.splice(i, 1); },
            count: function () { return this.handlers.length; }
        })");

        var em = new Emitter(target);
        em.Add("click", OnClick);
        Console.WriteLine("after add    : " + Script.Write<int>("{0}.count()", target));
        em.Remove("click", OnClick);
        Console.WriteLine("after remove : " + Script.Write<int>("{0}.count()", target));
    }
}

public class Program
{
    public static void Main() { new Widget().Run(); Console.WriteLine("<<DONE>>"); }
}
""", skipRoslyn: true);

            StringAssert.Contains(output, "after add    : 1", output);
            StringAssert.Contains(output, "after remove : 0",
                "the delegate handed to remove must be the one add registered\n" + output);
        }

        /// <summary>
        /// The same thing without the DOM in the way: two conversions of one instance method group on
        /// one receiver are the same JS function, so anything that compares by reference works.
        /// </summary>
        [TestMethod]
        public async Task TwoConversionsOfOneMethodGroupAreOneFunction()
        {
            var output = await RunTest("""
using System;
using Transpose;

public class Widget
{
    public void OnClick(object e) { }
    public void OnOther(object e) { }

    public void Run()
    {
        Action<object> a = OnClick;
        Action<object> b = OnClick;
        Action<object> c = OnOther;
        Console.WriteLine("same method  : " + Script.Write<bool>("{0} === {1}", a, b));
        Console.WriteLine("other method : " + Script.Write<bool>("{0} === {1}", a, c));
    }
}

public class Program
{
    public static void Main()
    {
        var w1 = new Widget();
        var w2 = new Widget();
        w1.Run();
        Action<object> x = w1.OnClick;
        Action<object> y = w2.OnClick;
        Console.WriteLine("other target : " + Script.Write<bool>("{0} === {1}", x, y));
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

            StringAssert.Contains(output, "same method  : True", output);
            StringAssert.Contains(output, "other method : False", "a different method is a different delegate\n" + output);
            StringAssert.Contains(output, "other target : False", "a different target is a different delegate\n" + output);
        }

        /// <summary>
        /// What the caching buys in C# terms, and here .NET is the oracle: two delegates over the same
        /// target and method are equal, over a different target or method they are not.
        /// </summary>
        [TestMethod]
        public async Task DelegateEqualityMatchesDotNet()
        {
            await RunTest("""
using System;

public class Widget
{
    public void OnClick(object e) { }
    public void OnOther(object e) { }
}

public class Program
{
    public static void Main()
    {
        var w1 = new Widget();
        var w2 = new Widget();

        Action<object> a = w1.OnClick;
        Action<object> b = w1.OnClick;
        Action<object> c = w1.OnOther;
        Action<object> d = w2.OnClick;

        Console.WriteLine("same target+method : " + (a == b));
        Console.WriteLine("same target, other : " + (a == c));
        Console.WriteLine("other target       : " + (a == d));
        Console.WriteLine("Equals             : " + a.Equals(b));
        Console.WriteLine("<<DONE>>");
    }
}
""");
        }

        /// <summary>
        /// The receiver of a method group is evaluated exactly once, where it is written — it used to
        /// be emitted twice, which called a side-effecting receiver twice AND bound the second object
        /// to a method read off the first. .NET is the oracle for both the count and which object the
        /// delegate ends up on.
        /// </summary>
        [TestMethod]
        public async Task TheReceiverOfAMethodGroupIsEvaluatedOnce()
        {
            await RunTest("""
using System;

public class Widget
{
    public string Name;
    public void OnClick(object e) { Console.WriteLine("  handled by " + Name); }
}

public class Program
{
    static int calls;
    static Widget Get() { calls++; return new Widget { Name = "w" + calls }; }

    public static void Main()
    {
        Action<object> h = Get().OnClick;
        Console.WriteLine("receiver evaluations : " + calls);
        h(null);
        Console.WriteLine("<<DONE>>");
    }
}
""");
        }

        /// <summary>
        /// A generic method group threads its type arguments as leading bound parameters, so the cache
        /// has to be keyed by those too — keyed on the method alone, <c>Show&lt;int&gt;</c> and
        /// <c>Show&lt;string&gt;</c> collided and the second call got the first one's binding, which
        /// would have silently handed it the wrong T.
        /// </summary>
        [TestMethod]
        public async Task AGenericMethodGroupIsCachedPerTypeArgument()
        {
            await RunTest("""
using System;

public class Printer
{
    public string Show<T>(T value) => typeof(T).Name + ":" + value;
}

public class Program
{
    public static void Main()
    {
        var p = new Printer();
        Func<int, string> asInt = p.Show<int>;
        Func<string, string> asStr = p.Show<string>;
        Console.WriteLine(asInt(7));
        Console.WriteLine(asStr("x"));
        Func<int, string> asIntAgain = p.Show<int>;
        Console.WriteLine(asIntAgain(9));
        Console.WriteLine("same binding reused : " + (asInt == asIntAgain));
        Console.WriteLine("<<DONE>>");
    }
}
""");
        }

        /// <summary>
        /// The delegate still dispatches virtually: it is bound to the receiver, not to the method the
        /// static type would have picked, so an override runs.
        /// </summary>
        [TestMethod]
        public async Task ABoundDelegateStillDispatchesVirtually()
        {
            await RunTest("""
using System;

public class Base    { public virtual string Who() => "base"; }
public class Derived : Base { public override string Who() => "derived"; }

public class Program
{
    public static void Main()
    {
        Base b = new Derived();
        Func<string> f = b.Who;
        Console.WriteLine(f());
        Console.WriteLine("<<DONE>>");
    }
}
""");
        }

        /// <summary>
        /// A static method group was already a stable reference and must stay one — it binds only to
        /// thread type arguments, and nothing about it goes through the target cache (there is no
        /// target).
        /// </summary>
        [TestMethod]
        public async Task AStaticMethodGroupIsStillAStableReference()
        {
            var output = await RunTest("""
using System;
using Transpose;

public class Helper
{
    public static void OnClick(object e) { }
}

public class Program
{
    public static void Main()
    {
        Action<object> a = Helper.OnClick;
        Action<object> b = Helper.OnClick;
        Console.WriteLine("static same : " + Script.Write<bool>("{0} === {1}", a, b));
        Console.WriteLine("static ==   : " + (a == b));
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

            StringAssert.Contains(output, "static same : True", output);
            StringAssert.Contains(output, "static ==   : True", output);
        }

        /// <summary>
        /// A lambda is NOT a method group and gets no caching — each one is its own delegate, exactly
        /// as in .NET, so an application that wants to remove a handler has to keep the reference.
        /// This is the boundary of the fix, and worth pinning so nobody widens it by accident.
        /// </summary>
        [TestMethod]
        public async Task LambdasAreStillDistinctDelegates()
        {
            await RunTest("""
using System;

public class Program
{
    public static void Main()
    {
        Action<object> a = e => { };
        Action<object> b = e => { };
        Console.WriteLine("two lambdas equal : " + (a == b));
        Action<object> c = a;
        Console.WriteLine("same reference    : " + (a == c));
        Console.WriteLine("<<DONE>>");
    }
}
""");
        }

        /// <summary>
        /// The cache hangs off the target object, so it must not become visible to the JavaScript that
        /// object is handed to — an own enumerable array property would show up in Object.keys and
        /// for-in and make structuredClone refuse it.
        /// </summary>
        [TestMethod]
        public async Task TheDelegateCacheIsInvisibleToJavaScript()
        {
            var output = await RunTest("""
using System;
using Transpose;

[ObjectLiteral]
public class Options
{
    public string Name { get; set; }
}

public class Widget
{
    public void OnClick(object e) { }
}

public class Program
{
    public static void Main()
    {
        // A delegate over a plain JS object as the target: the cache must not disturb it. The method
        // lives on the prototype so the object itself stays structured-cloneable — an own enumerable
        // function property would make structuredClone refuse it on its own account, which would say
        // nothing about $$bind.
        object plain = Script.Write<object>("(function(){ var o = Object.create({ m: function () { return 1; } }); o.a = 1; o.b = 2; return o; })()");
        Console.WriteLine("keys before : " + Script.Write<string>("JSON.stringify(Object.keys({0}))", plain));
        Console.WriteLine("clone before: " + Script.Write<bool>("(function(v){try{structuredClone(v);return true;}catch(e){return false;}})({0})", plain));
        Script.Write("globalThis.__d = Transpose.fn.cacheBindMember({0}, 'm')", plain);
        Console.WriteLine("keys after  : " + Script.Write<string>("JSON.stringify(Object.keys({0}))", plain));
        Console.WriteLine("json after  : " + Script.Write<string>("JSON.stringify({0})", plain));
        Console.WriteLine("clone after : " + Script.Write<bool>("(function(v){try{structuredClone(v);return true;}catch(e){return false;}})({0})", plain));
        Console.WriteLine("cached      : " + Script.Write<bool>("globalThis.__d === Transpose.fn.cacheBindMember({0}, 'm')", plain));
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

            StringAssert.Contains(output, """keys before : ["a","b"]""", output);
            StringAssert.Contains(output, "clone before: True", "premise: the fixture is cloneable to begin with\n" + output);
            StringAssert.Contains(output, """keys after  : ["a","b"]""", "the cache must not add an enumerable key\n" + output);
            StringAssert.Contains(output, """json after  : {"a":1,"b":2}""", output);
            StringAssert.Contains(output, "clone after : True", "the cache must not make the object un-cloneable\n" + output);
            StringAssert.Contains(output, "cached      : True", output);
        }

        /// <summary>
        /// The event-handler round trip the fix exists for, over several handlers and two targets at
        /// once: each (target, method) pair has to remove independently.
        /// </summary>
        [TestMethod]
        public async Task SeveralHandlersAndTargetsRemoveIndependently()
        {
            var output = await RunTest("""
using System;
using Transpose;

public class Widget
{
    public string Id;
    public void A(object e) { }
    public void B(object e) { }
}

public class Program
{
    static object Bus() => Script.Write<object>(@"({
        hs: [],
        add: function (h) { this.hs.push(h); },
        remove: function (h) { var i = this.hs.indexOf(h); if (i >= 0) this.hs.splice(i, 1); },
        count: function () { return this.hs.length; }
    })");

    public static void Main()
    {
        var bus = Bus();
        var w1 = new Widget { Id = "1" };
        var w2 = new Widget { Id = "2" };

        Script.Write("{0}.add({1})", bus, (Action<object>)w1.A);
        Script.Write("{0}.add({1})", bus, (Action<object>)w1.B);
        Script.Write("{0}.add({1})", bus, (Action<object>)w2.A);
        Console.WriteLine("added   : " + Script.Write<int>("{0}.count()", bus));

        Script.Write("{0}.remove({1})", bus, (Action<object>)w1.B);
        Console.WriteLine("-w1.B   : " + Script.Write<int>("{0}.count()", bus));
        Script.Write("{0}.remove({1})", bus, (Action<object>)w2.A);
        Console.WriteLine("-w2.A   : " + Script.Write<int>("{0}.count()", bus));
        Script.Write("{0}.remove({1})", bus, (Action<object>)w1.A);
        Console.WriteLine("-w1.A   : " + Script.Write<int>("{0}.count()", bus));
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

            StringAssert.Contains(output, "added   : 3", output);
            StringAssert.Contains(output, "-w1.B   : 2", output);
            StringAssert.Contains(output, "-w2.A   : 1", output);
            StringAssert.Contains(output, "-w1.A   : 0", output);
        }

        /// <summary>
        /// Everything in the BCL that finds a delegate again needs this identity, and each of these
        /// quietly did nothing before: a fresh function per conversion means the list never contains
        /// the handler you just added and <c>-=</c> never takes one off. .NET is the oracle.
        /// </summary>
        [TestMethod]
        public async Task FindingADelegateAgainWorksAcrossTheBcl()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public class Widget
{
    public void Handle(object e) { }
    public void Other(object e) { }
}

public class Program
{
    public static void Main()
    {
        var w = new Widget();

        var list = new List<Action<object>> { w.Handle };
        Console.WriteLine("Contains     : " + list.Contains(w.Handle));
        Console.WriteLine("IndexOf      : " + list.IndexOf(w.Handle));
        Console.WriteLine("Remove       : " + list.Remove(w.Handle) + " count=" + list.Count);

        Action<object> m = w.Handle;
        m += w.Other;
        m += w.Handle;
        Console.WriteLine("multicast    : " + m.GetInvocationList().Length);
        m -= w.Handle;
        Console.WriteLine("after -=     : " + m.GetInvocationList().Length);
        m -= w.Other;
        Console.WriteLine("after -= 2   : " + (m == null ? "null" : m.GetInvocationList().Length.ToString()));

        var set = new HashSet<Action<object>> { w.Handle };
        Console.WriteLine("set re-add   : " + set.Add(w.Handle) + " count=" + set.Count);
        Console.WriteLine("<<DONE>>");
    }
}
""");
        }

        /// <summary>
        /// A delegate taken off <c>this</c> inside the type — the form an event handler is actually
        /// written in — caches the same way as one taken off a named receiver.
        /// </summary>
        [TestMethod]
        public async Task AMethodGroupOnThisIsCachedToo()
        {
            var output = await RunTest("""
using System;
using Transpose;

public class Widget
{
    public void OnClick(object e) { }

    public bool SameTwice()
    {
        Action<object> a = OnClick;
        Action<object> b = this.OnClick;
        return Script.Write<bool>("{0} === {1}", a, b);
    }
}

public class Program
{
    public static void Main()
    {
        Console.WriteLine("this-bound cached : " + new Widget().SameTwice());
        Console.WriteLine("<<DONE>>");
    }
}
""", skipRoslyn: true);

            StringAssert.Contains(output, "this-bound cached : True", output);
        }
    }
}
