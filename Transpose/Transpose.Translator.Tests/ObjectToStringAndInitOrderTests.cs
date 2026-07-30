using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// The general-purpose behaviours that a record's synthesized ToString and constructor exposed, each
    /// of which reproduces without records and so is fixed for every type:
    ///
    ///  - <b>ToString of a type-shaped value.</b> <c>object.ToString()</c> is <c>GetType().ToString()</c>,
    ///    which names generic arguments plainly (<c>List`1[System.Int32]</c>) rather than
    ///    assembly-qualifying them as <c>FullName</c> does; an <b>array</b>'s ToString is its type name,
    ///    not its elements (JS's <c>Array.prototype.toString</c> joins them); a <b>DateTime</b>'s is the
    ///    culture's general date/time pattern, not the JS <c>Date</c> string; and a <b>Type</b>'s is its
    ///    display name, not the constructor's source text.
    ///  - <b>Instance field initializers run before the base constructor</b>, which is C#'s order.
    ///  - <b>A virtual auto-property has storage per declaration</b>, so an override does not share the
    ///    base's, and a read dispatches to the most-derived accessor.
    ///  - <b>A local binding never shadows a type reference</b>, however it is named.
    /// </summary>
    [TestClass]
    public class ObjectToStringAndInitOrderTests : TranslatorTestBase
    {
        // ---- ToString -----------------------------------------------------------

        [TestMethod]
        public async Task ToStringOfArraysCollectionsDatesAndTypes()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public class Plain { }

public class Program
{
    public static void Main()
    {
        // An array's ToString is its TYPE name; JS would join the elements ("1,2").
        Console.WriteLine(new int[] { 1, 2 }.ToString());
        Console.WriteLine(new string[0].ToString());
        Console.WriteLine("A = " + new int[] { 1, 2 });
        object boxedArray = new int[] { 1, 2 };
        Console.WriteLine(boxedArray.ToString());

        // object.ToString() falls back to GetType().ToString(), which names generic arguments
        // plainly rather than assembly-qualifying them the way FullName does.
        Console.WriteLine(new List<int> { 1 }.ToString());
        Console.WriteLine(new Dictionary<string, List<int>>().ToString());
        Console.WriteLine(new Plain().ToString());

        // A DateTime's ToString is the general date/time pattern, not the JS Date string.
        Console.WriteLine(new DateTime(2020, 1, 2).ToString());
        Console.WriteLine(new DateTime(2020, 1, 2, 3, 4, 5).ToString());
        Console.WriteLine("D = " + new DateTime(2020, 1, 2));

        // A Type's ToString is its display name, not the JS constructor's source text.
        Console.WriteLine(typeof(List<int>).ToString());
        Console.WriteLine(typeof(int[]).ToString());
        Console.WriteLine(typeof(int).ToString());
        Console.WriteLine(typeof(Dictionary<string, List<int>>).ToString());
        Console.WriteLine("T = " + typeof(List<int>));
        Console.WriteLine(typeof(List<int>).Name);
        Console.WriteLine(typeof(int[]).Name);
    }
}
""");
        }

        // ---- initializer / base-constructor order -------------------------------

        [TestMethod]
        public async Task FieldInitializersRunBeforeTheBaseConstructor()
        {
            await RunTest("""
using System;

public class B { public B() { Console.WriteLine("B ctor"); } }
public class D : B { public int X = Program.Log("D init"); }

public class WithArgs { public WithArgs(int a) { Console.WriteLine("base " + a); } }
public class Primary(int a) : WithArgs(a) { public int Y = Program.Log("primary init"); }

public class Program
{
    public static int Log(string s) { Console.WriteLine(s); return 1; }

    public static void Main()
    {
        new D();
        Console.WriteLine("--");
        Console.WriteLine(new Primary(2).Y);
    }
}
""");
        }

        // ---- virtual auto-properties -------------------------------------------

        [TestMethod]
        public async Task AVirtualAutoPropertyHasStoragePerDeclaration()
        {
            await RunTest("""
using System;

public class B
{
    public virtual int P { get; set; } = 1;
    public B() { Console.WriteLine("in base ctor: " + P); }
}

public class D : B
{
    public override int P { get; set; } = 2;
    // The base's own storage stays reachable through base.P, and only through it.
    public int BaseP => base.P;
}

public abstract class AbstractBase { public abstract int Q { get; set; } }
public class Concrete : AbstractBase { public override int Q { get; set; } = 5; }

public class Program
{
    public static void Main()
    {
        var d = new D();
        Console.WriteLine(d.P);            // the override's storage
        Console.WriteLine(((B)d).P);       // virtual dispatch reaches it too
        Console.WriteLine(d.BaseP);        // the base's own, untouched by the override
        d.P = 7;
        Console.WriteLine(d.P);
        Console.WriteLine(new B().P);      // the base still has its own
        Console.WriteLine(new Concrete().Q);
        AbstractBase a = new Concrete();
        a.Q = 9;
        Console.WriteLine(a.Q);
    }
}
""");
        }

        [TestMethod]
        public async Task AVirtualAutoPropertyOnARecord()
        {
            await RunTest("""
using System;

public record AutoBase(int A) { public virtual int P { get; init; } = 1; }
public record AutoDerived(int A) : AutoBase(A) { public override int P { get; init; } = 2; }

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new AutoBase(1));
        Console.WriteLine(new AutoDerived(1));
        Console.WriteLine(new AutoDerived(1).P);
        Console.WriteLine(((AutoBase)new AutoDerived(1)).P);
        Console.WriteLine(new AutoDerived(1) == new AutoDerived(1));
        Console.WriteLine(new AutoDerived(1) with { P = 8 });
    }
}
""");
        }

        [TestMethod]
        public async Task BaseAccessOnAnOverriddenPropertyReachesTheBaseAccessor()
        {
            // `base` emits as `this`, so a property read/write through it has to be routed to the base
            // type's own accessor — a plain `this.P` runs the most-derived override, which is exactly
            // what `base` exists to bypass.
            await RunTest("""
using System;

public class B
{
    private int _v = 1;
    public virtual int P { get { return _v; } set { _v = value; } }
    public virtual int A { get; set; } = 10;
}

public class D : B
{
    private int _d = 2;
    public override int P { get { return _d; } set { _d = value; } }
    public override int A { get; set; } = 20;

    public int BaseP => base.P;
    public int BaseA => base.A;
    public void SetBaseP(int v) { base.P = v; }
    public void SetBaseA(int v) { base.A = v; }
}

public class Program
{
    public static void Main()
    {
        var d = new D();
        Console.WriteLine(d.P + " " + d.BaseP);
        Console.WriteLine(d.A + " " + d.BaseA);
        d.SetBaseP(7);
        d.SetBaseA(70);
        Console.WriteLine(d.P + " " + d.BaseP);
        Console.WriteLine(d.A + " " + d.BaseA);
        d.P = 8;
        d.A = 80;
        Console.WriteLine(d.P + " " + d.BaseP);
        Console.WriteLine(d.A + " " + d.BaseA);
    }
}
""");
        }

        // ---- type references are unshadowable ----------------------------------

        [TestMethod]
        public async Task ALocalBindingNeverShadowsATypeReference()
        {
            await RunTest("""
using System;
using System.Collections.Generic;
using System.Linq;

public class B { public int A; public B(int a) { A = a; } }
public class D : B { public int Bb; public D(int A, int B) : base(A) { Bb = B; } }

public record RB(int A);
public record RD(int A, int B) : RB(A);

public class Helper { public static int Twice(int x) => x * 2; }

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new D(1, 2).A);
        Console.WriteLine(new RD(1, 2));

        // A local, a foreach variable, an out var and a lambda parameter all named after a type
        // that the same method references.
        var Helper = 3;
        Console.WriteLine(Helper + Program.Twice(Helper));
        foreach (var B in new[] { 1, 2 }) Console.WriteLine(new B(B).A);
        if (int.TryParse("4", out var Helper2)) Console.WriteLine(Helper2);
        Func<int, int> f = B => new B(B).A;
        Console.WriteLine(f(5));
        Console.WriteLine(new List<int> { 1, 2 }.Select(B => new B(B).A).Sum());
    }

    static int Twice(int x) => Helper.Twice(x);
}
""");
        }
    }
}
