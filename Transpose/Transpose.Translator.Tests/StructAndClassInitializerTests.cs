using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Exhaustive coverage of how a class or struct gets its initial state, and of what a struct
    /// copy has to carry — the two halves of the same problem, since C# initializes a value type by
    /// copying a zeroed value around.
    ///
    /// <para>The direct ancestor of this file is <see cref="UninitializedStructLocalTests"/>, which
    /// covered the one shape that took down HashSet's slow path (`ElementCount result;` filled in
    /// field by field). Working outwards from it — nesting a struct in a struct, a struct in a class,
    /// a class in a struct, arrays, collections, generics, records, boxing — turned up six more
    /// defects, each guarded below and named in the test that found it:</para>
    /// <list type="bullet">
    /// <item>a <c>ref struct</c> local was excluded from the zeroed-value rule, so it stayed
    /// <c>undefined</c>;</item>
    /// <item><c>$clone</c> copied a struct-typed slot by reference, so EVERY value copy (assignment,
    /// argument, return, array fill, boxing, collection insert, <c>with</c>) aliased the nested
    /// struct;</item>
    /// <item>a record's primary constructor ran no instance field initializers at all;</item>
    /// <item>a struct whose every declared constructor takes arguments emitted no parameterless
    /// <c>ctor</c>, so <c>new S()</c> / <c>: this()</c> left its struct slots null;</item>
    /// <item>a record's positional members had no field slot (<c>default(R).X</c> was
    /// <c>undefined</c>) and no synthesized <c>Deconstruct</c>;</item>
    /// <item>an unboxing cast handed back the boxed object itself instead of a copy.</item>
    /// </list>
    /// Every test is differential: the same C# runs natively and as translated JS and the console
    /// output must match, so each one states the C# semantics rather than the emitted shape.
    /// </summary>
    [TestClass]
    public class StructAndClassInitializerTests : TranslatorTestBase
    {
        // ---- initializer-less locals -------------------------------------------------------

        /// <summary>A user-declared <c>ref struct</c> is emitted as an ordinary struct define, so an
        /// initializer-less local of that type needs the same zeroed instance as any other struct.
        /// It used to be excluded, leaving `let r;` undefined — the very failure the zeroed-struct
        /// rule exists to prevent ("Cannot set properties of undefined").</summary>
        [TestMethod]
        public async Task TestUninitializedRefStructLocalIsZeroedAsync()
        {
            await RunTest("""
using System;

public ref struct RS { public int X; public int Y; }

public class Program
{
    public static void Main()
    {
        RS r;
        r.X = 1;
        r.Y = 2;
        Console.WriteLine(r.X + "," + r.Y);
    }
}
""");
        }

        /// <summary>An initializer-less struct local must be zeroed wherever it is declared, not just
        /// in a method body: a switch section, try/catch/finally, a loop body, a foreach body and a
        /// using block each open their own scope, and each declaration must produce a fresh value.</summary>
        [TestMethod]
        public async Task TestUninitializedStructLocalInEveryBlockScopeAsync()
        {
            await RunTest("""
using System;

public struct S { public int X; }

public class Program
{
    public static void Main()
    {
        int k = 2;
        switch (k)
        {
            case 2: { S s; s.X = 22; Console.WriteLine("switch:" + s.X); break; }
            default: break;
        }
        try { S s; s.X = 33; Console.WriteLine("try:" + s.X); }
        catch { }
        finally { S s; s.X = 44; Console.WriteLine("finally:" + s.X); }
        while (k-- > 1) { S s; s.X = k; Console.WriteLine("while:" + s.X); }
        foreach (var i in new[] { 1, 2 }) { S s; s.X = i; Console.WriteLine("foreach:" + s.X); }
        using (var d = new System.IO.MemoryStream()) { S s; s.X = 55; Console.WriteLine("using:" + s.X); }
    }
}
""");
        }

        /// <summary>The zeroed value of a CONSTRUCTED generic struct has to be built per instantiation —
        /// `Pair&lt;int&gt;` zeroes to two 0s, `Pair&lt;string&gt;` to two nulls.</summary>
        [TestMethod]
        public async Task TestUninitializedGenericStructLocalAsync()
        {
            await RunTest("""
using System;

public struct Pair<T> { public T A; public T B; }

public class Program
{
    public static void Main()
    {
        Pair<int> p; p.A = 1; p.B = 2;
        Pair<string> q; q.A = "x"; q.B = "y";
        Console.WriteLine(p.A + "," + p.B + "," + q.A + "," + q.B);
        Pair<int> d = default;
        Console.WriteLine(d.A + "," + d.B);
    }
}
""");
        }

        /// <summary>Zero-initialization recurses: three levels of struct nesting must all be real objects
        /// so `a.B.C.V = 5` has somewhere to write, and `default` of the outermost reads 0 all the way down.</summary>
        [TestMethod]
        public async Task TestThreeLevelNestedStructDefaultsAsync()
        {
            await RunTest("""
using System;

public struct L3 { public int V; }
public struct L2 { public L3 C; public int N; }
public struct L1 { public L2 B; public int M; }

public class Program
{
    public static void Main()
    {
        L1 a;
        a.B.C.V = 5; a.B.N = 4; a.M = 3;
        Console.WriteLine(a.B.C.V + "," + a.B.N + "," + a.M);

        L1 z = default;
        Console.WriteLine(z.B.C.V + "," + z.B.N + "," + z.M);
    }
}
""");
        }

        /// <summary>A local function body, a lambda body and an iterator body are each emitted as their
        /// own function; the zeroed-struct rule has to reach all three.</summary>
        [TestMethod]
        public async Task TestUninitializedStructLocalInNestedFunctionBodiesAsync()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public struct S { public int X; public int Y; }

public class Program
{
    static IEnumerable<int> Iter() { S s; s.X = 1; s.Y = 2; yield return s.X; yield return s.Y; }

    public static void Main()
    {
        int Local() { S s; s.X = 7; s.Y = 8; return s.X + s.Y; }
        Func<int> f = () => { S s; s.X = 1; s.Y = 2; return s.X * 10 + s.Y; };
        Console.WriteLine(Local() + "," + f());
        foreach (var v in Iter()) Console.WriteLine("iter:" + v);
    }
}
""");
        }

        /// <summary>An async body is rewritten into a state machine; a struct local declared in one
        /// still has to start out zeroed.</summary>
        [TestMethod]
        public async Task TestUninitializedStructLocalInAsyncMethodAsync()
        {
            await RunTest("""
using System;
using System.Threading.Tasks;

public struct S { public int X; public int Y; }

public class Program
{
    public static async Task Main()
    {
        Console.WriteLine(await Fill(10));
        Console.WriteLine("<<DONE>>");
    }

    static async Task<int> Fill(int v)
    {
        await Task.Yield();
        S s;
        s.X = v;
        s.Y = v * 2;
        return s.X + s.Y;
    }
}
""", "<<DONE>>");
        }

        /// <summary>Several declarators in one statement each get their own zeroed value — `S a, b;`
        /// must not have a and b share one object.</summary>
        [TestMethod]
        public async Task TestMultipleStructDeclaratorsAreIndependentAsync()
        {
            await RunTest("""
using System;

public struct S { public int X; }

public class Program
{
    public static void Main()
    {
        S a, b;
        a.X = 1;
        b.X = 2;
        Console.WriteLine(a.X + "," + b.X);
    }
}
""");
        }

        /// <summary>A BCL struct local declared without an initializer takes the same path as a user
        /// struct, and its `default` must be the .NET zero value (DateTime year 1, an all-zeros Guid,
        /// a zero TimeSpan) rather than null.</summary>
        [TestMethod]
        public async Task TestBclStructLocalsWithoutInitializerAsync()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public class Program
{
    public static void Main()
    {
        TimeSpan t; t = TimeSpan.FromSeconds(90);
        KeyValuePair<int, string> kv; kv = new KeyValuePair<int, string>(1, "a");
        Guid g; g = Guid.Empty;
        DateTime d; d = new DateTime(2020, 1, 2);
        Console.WriteLine(t.TotalSeconds + "," + kv.Key + kv.Value + "," + g + "," + d.Year);

        DateTime d2 = default;
        Console.WriteLine(d2.Year + "," + default(TimeSpan).Ticks + "," + default(Guid));
    }
}
""");
        }

        /// <summary>An `out` parameter of struct type may be definitely assigned field by field in the
        /// callee, so the holder the caller passes has to arrive already zeroed.</summary>
        [TestMethod]
        public async Task TestOutParameterOfStructTypeAssignedFieldWiseAsync()
        {
            await RunTest("""
using System;

public struct S { public int X; public int Y; }

public class Program
{
    static void Fill(out S s, int v) { s.X = v; s.Y = v * 2; }

    public static void Main()
    {
        Fill(out S s, 3);
        Console.WriteLine(s.X + "," + s.Y);
        S t;
        Fill(out t, 5);
        Console.WriteLine(t.X + "," + t.Y);
    }
}
""");
        }

        // ---- constructors -----------------------------------------------------------------

        /// <summary>Inside a struct's own constructor `this` behaves like an out parameter: it may be
        /// filled in one nested field at a time, which only works if the instance (and its nested
        /// struct slots) exist before the body runs.</summary>
        [TestMethod]
        public async Task TestStructConstructorAssignsNestedFieldWiseAsync()
        {
            await RunTest("""
using System;

public struct Inner { public int V; }

public struct S
{
    public int X;
    public Inner I;
    public S(int x) { X = x; I.V = x * 2; }
}

public class Program
{
    public static void Main()
    {
        var s = new S(4);
        Console.WriteLine(s.X + "," + s.I.V);
    }
}
""");
        }

        /// <summary>A struct always has a parameterless constructor even when every declared one takes
        /// arguments, and both `new S()` and `: this()` reach it. It has no syntax of its own, so it
        /// used to be skipped entirely: `new S().I` was null and reading through it threw.</summary>
        [TestMethod]
        public async Task TestStructWithOnlyParameterizedConstructorsAsync()
        {
            await RunTest("""
using System;

public struct Inner { public int V; public int W; }

public struct S
{
    public Inner I;
    public int X;
    public S(int x) : this() { X = x; }
    public S(int x, int v) { X = x; I = new Inner { V = v }; }
}

public class Program
{
    public static void Main()
    {
        var chained = new S(1);
        Console.WriteLine(chained.X + "," + chained.I.V + "," + chained.I.W);

        var full = new S(2, 3);
        Console.WriteLine(full.X + "," + full.I.V);

        var implicitDefault = new S();
        Console.WriteLine(implicitDefault.X + "," + implicitDefault.I.V);

        var zero = default(S);
        Console.WriteLine(zero.X + "," + zero.I.V);
    }
}
""");
        }

        /// <summary>C# 10 lets a struct declare its own parameterless constructor, and `default(S)`
        /// must NOT call it — `new S()` runs the body, `default(S)` stays all-zeros. A field
        /// initializer in a struct is likewise the constructor's business, not `default`'s.</summary>
        [TestMethod]
        public async Task TestStructParameterlessConstructorVersusDefaultAsync()
        {
            await RunTest("""
using System;

public struct S
{
    public int X;
    public int Y;
    public S() { X = 42; Y = 7; }
}

public struct T
{
    public int A = 3;
    public S Inner;
    public T() { A = 3; }
}

public class Program
{
    public static void Main()
    {
        var a = new S();
        var b = default(S);
        Console.WriteLine(a.X + "," + a.Y + " | " + b.X + "," + b.Y);

        var t = new T();
        Console.WriteLine(t.A + "," + t.Inner.X);

        var t2 = default(T);
        Console.WriteLine(t2.A + "," + t2.Inner.X);
    }
}
""");
        }

        /// <summary>A non-record primary constructor on a class and on a struct: the captured parameter,
        /// the field initializers and the zeroed struct slot all have to happen in the one constructor
        /// the declaration produces.</summary>
        [TestMethod]
        public async Task TestPrimaryConstructorOnClassAndStructAsync()
        {
            await RunTest("""
using System;

public class Nested { public int Q = 4; }
public struct Inner { public int V; }

public class PC(int a) { public int A = a; public Nested N = new Nested(); public Inner I; }
public struct PS(int a) { public int A = a; public Inner I; }

public class Program
{
    public static void Main()
    {
        var c = new PC(1);
        Console.WriteLine(c.A + "," + (c.N == null ? "NULL" : c.N.Q.ToString()) + "," + c.I.V);

        var s = new PS(2);
        Console.WriteLine(s.A + "," + s.I.V);

        var d = default(PS);
        Console.WriteLine(d.A + "," + d.I.V);
    }
}
""");
        }

        /// <summary>Field initializers run before the base constructor, so a base constructor body
        /// observes its own fields initialized and the derived ones not yet — and a struct-typed field
        /// with no initializer is already the zeroed struct by then, not null.</summary>
        [TestMethod]
        public async Task TestFieldInitializerOrderAcrossInheritanceAsync()
        {
            await RunTest("""
using System;

public struct Inner { public int V; }

public class Base
{
    public Inner BI;
    public int B = 1;
    public Base() { Console.WriteLine("Base ctor BI.V=" + BI.V + " B=" + B); }
}

public class Derived : Base
{
    public Inner DI;
    public int D = 2;
    public Derived() { Console.WriteLine("Derived ctor DI.V=" + DI.V + " D=" + D); }
}

public class Program
{
    public static void Main()
    {
        var d = new Derived();
        Console.WriteLine(d.BI.V + "," + d.DI.V + "," + d.B + "," + d.D);
    }
}
""");
        }

        // ---- nesting: struct on struct, struct on class, class on struct ------------------

        /// <summary>A struct on a class: every struct-typed field and auto-property of a reference type
        /// starts out as the zeroed struct (never null), each instance gets its own, and a nested field
        /// is writable straight through the owner (`h.O.I.V = 1`).</summary>
        [TestMethod]
        public async Task TestStructOnClassDefaultsAndNestedWritesAsync()
        {
            await RunTest("""
using System;

public struct Inner { public int V; }
public struct Outer { public Inner I; public int N; }

public class Holder { public Outer O; public Inner I2; public int Plain; public Inner Prop { get; set; } }

public class Program
{
    public static void Main()
    {
        var h = new Holder();
        Console.WriteLine(h.O.I.V + "," + h.O.N + "," + h.I2.V + "," + h.Plain + "," + h.Prop.V);

        h.O.I.V = 1; h.O.N = 2; h.I2.V = 3;
        Console.WriteLine(h.O.I.V + "," + h.O.N + "," + h.I2.V);

        var fresh = new Holder();
        Console.WriteLine("fresh:" + fresh.O.I.V + "," + fresh.O.N);
    }
}
""");
        }

        /// <summary>A class on a struct is the mirror image: the slot defaults to null, and once set it
        /// is genuinely SHARED by every copy of the struct — a struct copy is shallow with respect to
        /// its reference-typed fields, and that is the C# semantics we must reproduce.</summary>
        [TestMethod]
        public async Task TestClassOnStructIsSharedByEveryCopyAsync()
        {
            await RunTest("""
using System;

public class Leaf { public int V; public Deep D = new Deep(); }
public class Deep { public int Q; }
public struct S { public Leaf L; public int X; }

public class Program
{
    public static void Main()
    {
        var s = new S { X = 1, L = new Leaf { V = 2, D = { Q = 3 } } };
        Console.WriteLine(s.X + "," + s.L.V + "," + s.L.D.Q);

        var t = s;
        t.L.V = 99;
        Console.WriteLine(s.L.V + "," + t.L.V + "," + object.ReferenceEquals(s.L, t.L));

        S u = default;
        Console.WriteLine((u.L == null) + "," + u.X);
    }
}
""");
        }

        /// <summary>The core value-copy rule: copying a struct copies its struct-typed fields too, all
        /// the way down. Sharing the one nested object made `var b = a; b.I.V = 6;` write through to
        /// `a.I.V`, and passing a struct by value let the callee mutate the caller's nested state.</summary>
        [TestMethod]
        public async Task TestNestedStructOnStructIsCopiedNotSharedAsync()
        {
            await RunTest("""
using System;

public struct Inner { public int V; }
public struct S { public int X; public Inner I; }

public class Program
{
    static void Mutate(S s) { s.X = 999; s.I.V = 999; }
    static S Make() { S s; s.X = 1; s.I.V = 2; return s; }

    public static void Main()
    {
        S a; a.X = 1; a.I.V = 2;

        var b = a;
        b.X = 5; b.I.V = 6;
        Console.WriteLine(a.X + "," + a.I.V + " | " + b.X + "," + b.I.V);

        Mutate(a);
        Console.WriteLine("after mutate:" + a.X + "," + a.I.V);

        var c = Make(); c.X = 7;
        var d = Make();
        Console.WriteLine(c.X + "," + d.X);
    }
}
""");
        }

        /// <summary>Deep nesting through a member initializer, then copied: the copy must own all three
        /// levels, so `copy.B.C.V = 99` leaves the original's `B.C.V` alone.</summary>
        [TestMethod]
        public async Task TestDeepNestedMemberInitializerAndCopyAsync()
        {
            await RunTest("""
using System;

public struct L3 { public int V; public int W; }
public struct L2 { public L3 C; public int N; }
public struct L1 { public L2 B; public int M; }
public class Holder { public L1 A; public int Z; }

public class Program
{
    public static void Main()
    {
        var h = new Holder { Z = 1, A = { M = 2, B = { N = 3, C = { V = 4, W = 5 } } } };
        Console.WriteLine(h.Z + "," + h.A.M + "," + h.A.B.N + "," + h.A.B.C.V + "," + h.A.B.C.W);

        var l = new L1 { M = 6, B = { N = 7, C = { V = 8 } } };
        Console.WriteLine(l.M + "," + l.B.N + "," + l.B.C.V);

        var copy = l;
        copy.B.C.V = 99;
        Console.WriteLine(l.B.C.V + "," + copy.B.C.V);
    }
}
""");
        }

        /// <summary>A struct-typed slot whose declared type is a TYPE PARAMETER can only be judged at
        /// run time: `Wrap&lt;Inner&gt;` must copy Value, `Wrap&lt;SomeClass&gt;` must share it. Nesting
        /// the generic struct in itself exercises the recursion.</summary>
        [TestMethod]
        public async Task TestGenericStructSlotIsCopiedWhenItHoldsAStructAsync()
        {
            await RunTest("""
using System;

public struct Inner { public int V; }
public class Ref { public int V; }
public struct Wrap<T> { public T Value; public int N; }

public class Program
{
    public static void Main()
    {
        Wrap<Inner> w = default;
        Console.WriteLine(w.Value.V + "," + w.N);

        w.Value.V = 5;
        var w2 = w;
        w2.Value.V = 9;
        Console.WriteLine(w.Value.V + "," + w2.Value.V);

        Wrap<Wrap<Inner>> deep = default;
        Console.WriteLine(deep.Value.Value.V);
        deep.Value.Value.V = 7;
        var deep2 = deep;
        deep2.Value.Value.V = 8;
        Console.WriteLine(deep.Value.Value.V + "," + deep2.Value.Value.V);

        var r = new Wrap<Ref> { Value = new Ref { V = 1 } };
        var r2 = r;
        r2.Value.V = 2;
        Console.WriteLine(r.Value.V + "," + object.ReferenceEquals(r.Value, r2.Value));
    }
}
""");
        }

        /// <summary>A <c>Nullable&lt;T&gt;</c> of struct type defaults to null (not a zeroed T), and
        /// `.Value` hands back a copy — mutating that copy must not reach back into the nullable.</summary>
        [TestMethod]
        public async Task TestNullableStructFieldDefaultAndCopyAsync()
        {
            await RunTest("""
using System;

public struct Inner { public int V; }
public struct S { public Inner? MaybeInner; public Inner I; public int? MaybeInt; }

public class Program
{
    public static void Main()
    {
        S a = default;
        Console.WriteLine((a.MaybeInner == null) + "," + a.I.V + "," + (a.MaybeInt == null));

        a.MaybeInner = new Inner { V = 3 };
        var b = a;
        Console.WriteLine(b.MaybeInner.Value.V);

        var c = a.MaybeInner.Value;
        c.V = 9;
        Console.WriteLine(a.MaybeInner.Value.V + "," + c.V);
    }
}
""");
        }

        /// <summary>A struct declared inside a class, a class declared inside a class, and a struct field
        /// referencing a type declared elsewhere — nesting the TYPE declarations must not change how
        /// their initializers and defaults work.</summary>
        [TestMethod]
        public async Task TestNestedTypeDeclarationInitializersAsync()
        {
            await RunTest("""
using System;

public struct DeepStruct { public int Q; }

public class Outer
{
    public class InnerClass { public int V; public DeepStruct D; }
    public struct InnerStruct { public int W; public InnerClass C; }

    public InnerClass IC = new InnerClass();
    public InnerStruct IS;
}

public class Program
{
    public static void Main()
    {
        var o = new Outer { IC = { V = 1, D = { Q = 2 } }, IS = { W = 3 } };
        Console.WriteLine(o.IC.V + "," + o.IC.D.Q + "," + o.IS.W + "," + (o.IS.C == null));

        var fresh = new Outer();
        Console.WriteLine(fresh.IC.V + "," + fresh.IC.D.Q + "," + fresh.IS.W);
    }
}
""");
        }

        /// <summary>A readonly struct's slots are zeroed the same way; `default` of one that nests
        /// another readonly struct must read 0 through both levels.</summary>
        [TestMethod]
        public async Task TestReadonlyStructDefaultsAsync()
        {
            await RunTest("""
using System;

public readonly struct RO { public readonly int X; public RO(int x) { X = x; } }

public readonly struct ROOuter
{
    public readonly RO Inner;
    public readonly int N;
    public ROOuter(int n) { Inner = new RO(n); N = n; }
}

public class Program
{
    public static void Main()
    {
        ROOuter o = default;
        Console.WriteLine(o.Inner.X + "," + o.N);

        var p = new ROOuter(4);
        Console.WriteLine(p.Inner.X + "," + p.N);
    }
}
""");
        }

        // ---- arrays, collections, boxing: the implicit copies -----------------------------

        /// <summary>Every element of a struct array is an independent value — including its nested
        /// struct field. One shared nested object made `arr[0].I.V = 10` show up in `arr[1].I.V`.
        /// Jagged and multi-dimensional arrays take different fill paths, so all three are checked.</summary>
        [TestMethod]
        public async Task TestStructArrayElementsAreIndependentAsync()
        {
            await RunTest("""
using System;

public struct Inner { public int V; }
public struct S { public int X; public Inner I; }

public class Program
{
    public static void Main()
    {
        var arr = new S[3];
        arr[0].X = 1; arr[0].I.V = 10;
        arr[2].X = 3;
        Console.WriteLine(arr[0].X + "," + arr[0].I.V + " | " + arr[1].X + "," + arr[1].I.V + " | " + arr[2].X);

        var jag = new S[2][];
        jag[0] = new S[2];
        jag[0][1].I.V = 8;
        Console.WriteLine(jag[0][0].I.V + "," + jag[0][1].I.V);

        var two = new S[2, 2];
        two[1, 1].I.V = 9;
        Console.WriteLine(two[0, 0].I.V + "," + two[1, 1].I.V);
    }
}
""");
        }

        /// <summary>The same independence, three struct levels deep and via an array initializer, so the
        /// zeroed element is built recursively rather than copied from one prototype.</summary>
        [TestMethod]
        public async Task TestArrayOfDeeplyNestedStructsAsync()
        {
            await RunTest("""
using System;

public struct L3 { public int V; }
public struct L2 { public L3 C; }
public struct L1 { public L2 B; }

public class Program
{
    public static void Main()
    {
        var arr = new L1[3];
        arr[0].B.C.V = 1;
        Console.WriteLine(arr[0].B.C.V + "," + arr[1].B.C.V + "," + arr[2].B.C.V);

        var init = new L1[] { new L1 { B = { C = { V = 5 } } }, default };
        init[1].B.C.V = 6;
        Console.WriteLine(init[0].B.C.V + "," + init[1].B.C.V);

        var many = new L1[2, 2];
        many[0, 0].B.C.V = 7;
        Console.WriteLine(many[0, 0].B.C.V + "," + many[0, 1].B.C.V + "," + many[1, 1].B.C.V);
    }
}
""");
        }

        /// <summary>Storing a struct in a collection stores a COPY, and reading one back yields a copy:
        /// neither later mutations of the source nor mutations of the retrieved value may reach the
        /// stored element. `foreach` hands out copies for the same reason.</summary>
        [TestMethod]
        public async Task TestStructCopiesThroughCollectionsAsync()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public struct Inner { public int V; }
public struct S { public Inner I; public int X; }

public class Program
{
    public static void Main()
    {
        var list = new List<S>();
        S a; a.X = 1; a.I.V = 2;
        list.Add(a);

        a.X = 50; a.I.V = 60;
        Console.WriteLine(list[0].X + "," + list[0].I.V);

        var got = list[0];
        got.X = 70; got.I.V = 80;
        Console.WriteLine(list[0].X + "," + list[0].I.V);

        var dict = new Dictionary<int, S>();
        dict[1] = a;
        a.I.V = 111;
        Console.WriteLine(dict[1].I.V);

        foreach (var e in list) { var m = e; m.I.V = 999; }
        Console.WriteLine(list[0].I.V);
    }
}
""");
        }

        /// <summary>`foreach` over a struct array iterates copies: mutating the loop value — even its
        /// nested struct field — leaves the array untouched.</summary>
        [TestMethod]
        public async Task TestForeachOverStructArrayIteratesCopiesAsync()
        {
            await RunTest("""
using System;

public struct Inner { public int V; }
public struct S { public Inner I; public int X; }

public class Program
{
    public static void Main()
    {
        var arr = new S[2];
        arr[0].X = 1; arr[0].I.V = 2; arr[1].X = 3; arr[1].I.V = 4;

        foreach (var e in arr) { var m = e; m.X = 100; m.I.V = 200; }
        Console.WriteLine(arr[0].X + "," + arr[0].I.V + "," + arr[1].X + "," + arr[1].I.V);

        int sum = 0;
        foreach (var e in arr) sum += e.X + e.I.V;
        Console.WriteLine(sum);
    }
}
""");
        }

        /// <summary>Boxing copies, and so does unboxing. Mutating the struct after `object o = s` must
        /// not change what o holds, and `(S)o` must hand back a copy — returning the boxed object
        /// itself let `back.I.V = 77` rewrite the box.</summary>
        [TestMethod]
        public async Task TestBoxingAndUnboxingBothCopyTheStructAsync()
        {
            await RunTest("""
using System;

public struct Inner { public int V; }

public struct S
{
    public Inner I;
    public int X;
    public override string ToString() { return X + "/" + I.V; }
}

public class Program
{
    public static void Main()
    {
        S s = default; s.X = 1; s.I.V = 2;

        object o = s;
        s.X = 9; s.I.V = 8;
        Console.WriteLine(o.ToString() + " | " + s.ToString());

        S back = (S)o;
        back.I.V = 77;
        Console.WriteLine(o.ToString() + " | " + back.ToString());
    }
}
""");
        }

        /// <summary>Assigning a struct to an interface boxes it, so a mutating call through the
        /// interface acts on the box and leaves the original alone.</summary>
        [TestMethod]
        public async Task TestStructThroughInterfaceIsABoxedCopyAsync()
        {
            await RunTest("""
using System;

public interface IHas { int Get(); void Bump(); }

public struct Inner { public int V; }

public struct S : IHas
{
    public Inner I;
    public int X;
    public int Get() { return X + I.V; }
    public void Bump() { X++; I.V++; }
}

public class Program
{
    public static void Main()
    {
        S s = default; s.X = 1; s.I.V = 2;
        IHas h = s;
        h.Bump();
        Console.WriteLine(s.X + "," + s.I.V + " | " + h.Get());
    }
}
""");
        }

        /// <summary>`ref` and `in` are the exceptions: they pass the storage, not a copy, so the callee
        /// writes through to the caller's value (including its nested struct).</summary>
        [TestMethod]
        public async Task TestRefAndInParametersActOnTheCallersValueAsync()
        {
            await RunTest("""
using System;

public struct Inner { public int V; }
public struct S { public Inner I; public int X; }

public class Program
{
    static void ByRef(ref S s) { s.X = 10; s.I.V = 20; }
    static int ByIn(in S s) { return s.X + s.I.V; }

    public static void Main()
    {
        S a = default;
        ByRef(ref a);
        Console.WriteLine(a.X + "," + a.I.V + "," + ByIn(in a));
    }
}
""");
        }

        /// <summary>Reading a struct-typed PROPERTY yields a copy (which is why C# rejects
        /// `h.Prop.V = 1`), while a struct-typed FIELD is storage and is written in place.</summary>
        [TestMethod]
        public async Task TestStructPropertyReturnsACopyFieldIsStorageAsync()
        {
            await RunTest("""
using System;

public struct Inner { public int V; }
public class Holder { public Inner Prop { get; set; } public Inner Field; }

public class Program
{
    public static void Main()
    {
        var h = new Holder();
        Console.WriteLine(h.Prop.V + "," + h.Field.V);

        h.Prop = new Inner { V = 1 };
        var got = h.Prop;
        got.V = 2;
        Console.WriteLine(h.Prop.V + "," + got.V);

        h.Field.V = 3;
        Console.WriteLine(h.Field.V);
    }
}
""");
        }

        /// <summary>Static state takes the same two paths: a static struct field with no initializer is
        /// zeroed (and writable field-wise from a static constructor), one with an object initializer
        /// runs it, and copying either out still yields an independent value.</summary>
        [TestMethod]
        public async Task TestStaticStructFieldsAndTheirCopiesAsync()
        {
            await RunTest("""
using System;

public struct Inner { public int V; }
public struct Cfg { public Inner I; public int N; }

public class Program
{
    static Cfg Shared;
    static Cfg WithInit = new Cfg { N = 5, I = { V = 6 } };

    static Program() { Shared.N = 1; Shared.I.V = 2; }

    public static void Main()
    {
        Console.WriteLine(Shared.N + "," + Shared.I.V + " | " + WithInit.N + "," + WithInit.I.V);

        var copy = WithInit;
        copy.I.V = 99;
        Console.WriteLine(WithInit.I.V + "," + copy.I.V);
    }
}
""");
        }

        // ---- object and collection initializers -------------------------------------------

        /// <summary>Nested object initializers on reference types, including a nested COLLECTION
        /// initializer (`Items = { 1, 2, 3 }`), which adds to the existing instance rather than
        /// assigning a new one.</summary>
        [TestMethod]
        public async Task TestNestedObjectAndCollectionInitializersOnClassesAsync()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public class Leaf { public int V; public string Name = "leaf"; }
public class Mid { public Leaf L = new Leaf(); public int N; public List<int> Items = new List<int>(); }
public class Top { public Mid M = new Mid(); public string Tag; }

public class Program
{
    public static void Main()
    {
        var t = new Top { Tag = "t", M = { N = 2, L = { V = 9, Name = "x" }, Items = { 1, 2, 3 } } };
        Console.WriteLine(t.Tag + "," + t.M.N + "," + t.M.L.V + "," + t.M.L.Name + "," + string.Join("|", t.M.Items));
    }
}
""");
        }

        /// <summary>A nested member initializer is legal on a struct-typed FIELD (it writes through to
        /// the existing value), on a struct nested in a struct, and on a struct field of a class.</summary>
        [TestMethod]
        public async Task TestNestedMemberInitializerOnStructFieldsAsync()
        {
            await RunTest("""
using System;

public struct Inner { public int V; public int W; }
public struct Outer { public Inner Nested; public int N; }
public class Holder { public Outer O; public int Z; }

public class Program
{
    public static void Main()
    {
        var o = new Outer { N = 1, Nested = { V = 2, W = 3 } };
        Console.WriteLine(o.N + "," + o.Nested.V + "," + o.Nested.W);

        var h = new Holder { Z = 4, O = { N = 5, Nested = { V = 6 } } };
        Console.WriteLine(h.Z + "," + h.O.N + "," + h.O.Nested.V);
    }
}
""");
        }

        /// <summary>An object initializer on a struct, driving auto-properties rather than fields, and a
        /// struct-typed auto-property assigned a whole new value (the only form C# allows for a
        /// property). `default` of the owner must still zero the property's slot.</summary>
        [TestMethod]
        public async Task TestStructObjectInitializerWithPropertiesAsync()
        {
            await RunTest("""
using System;

public struct P { public int X { get; set; } public int Y { get; set; } }
public struct Q { public int A; public P Prop { get; set; } }

public class Program
{
    public static void Main()
    {
        var p = new P { X = 1, Y = 2 };
        Console.WriteLine(p.X + "," + p.Y);

        var q = new Q { A = 3, Prop = new P { X = 4, Y = 5 } };
        Console.WriteLine(q.A + "," + q.Prop.X + "," + q.Prop.Y);

        var zero = default(Q);
        Console.WriteLine(zero.A + "," + zero.Prop.X);
    }
}
""");
        }

        /// <summary>Collection initializers in every shape: `Add`-style, indexer-style (`["k"] = v`),
        /// a struct as the key (so the value-wise hash/equality has to agree), and a nested indexer
        /// initializer inside an object initializer.</summary>
        [TestMethod]
        public async Task TestDictionaryAndIndexerInitializersAsync()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public struct K { public int A; public int B; }
public class Bag { public Dictionary<string, int> Map = new Dictionary<string, int>(); public List<K> Ks = new List<K>(); }

public class Program
{
    public static void Main()
    {
        var d = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var byStruct = new Dictionary<K, string> { { new K { A = 1, B = 2 }, "one" } };
        var b = new Bag { Map = { ["z"] = 9 }, Ks = { new K { A = 7 }, default } };

        Console.WriteLine(d["a"] + "," + d["b"] + "," + byStruct[new K { A = 1, B = 2 }]
            + "," + b.Map["z"] + "," + b.Ks[0].A + "," + b.Ks[1].A);
    }
}
""");
        }

        /// <summary>Structs built by an initializer and then placed in a list or array stay independent,
        /// and a `default` entry sits alongside them as a zeroed value.</summary>
        [TestMethod]
        public async Task TestStructsInsideCollectionAndArrayInitializersAsync()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public struct Inner { public int V; }
public struct S { public Inner I; public int X; }

public class Program
{
    public static void Main()
    {
        var list = new List<S> { new S { X = 1, I = { V = 10 } }, new S { X = 2 }, default };
        Console.WriteLine(list[0].X + "," + list[0].I.V + " | " + list[1].X + "," + list[1].I.V + " | " + list[2].X);

        var arr = new[] { new S { X = 1, I = { V = 5 } }, new S { X = 2 } };
        arr[0].I.V = 77;
        Console.WriteLine(arr[0].I.V + "," + arr[1].I.V);
    }
}
""");
        }

        /// <summary>Collections of collections built entirely from initializers — a dictionary of lists,
        /// an array of lists, a dictionary of dictionaries — each holding structs.</summary>
        [TestMethod]
        public async Task TestNestedCollectionInitializersOfStructsAsync()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public struct Inner { public int V; }

public class Program
{
    public static void Main()
    {
        var d = new Dictionary<string, List<Inner>>
        {
            ["a"] = new List<Inner> { new Inner { V = 1 }, default },
            ["b"] = new List<Inner>()
        };
        d["b"].Add(new Inner { V = 2 });
        Console.WriteLine(d["a"][0].V + "," + d["a"][1].V + "," + d["b"][0].V);

        var jag = new List<Inner>[] { new List<Inner> { new Inner { V = 3 } } };
        Console.WriteLine(jag[0][0].V);

        var nested = new Dictionary<int, Dictionary<int, Inner>> { [1] = new Dictionary<int, Inner> { [2] = new Inner { V = 4 } } };
        Console.WriteLine(nested[1][2].V);
    }
}
""");
        }

        /// <summary>An object initializer runs after the constructor and evaluates its assignments in
        /// source order, interleaved with a nested `new` — so the side effects have to come out in
        /// exactly that sequence.</summary>
        [TestMethod]
        public async Task TestObjectInitializerEvaluationOrderAsync()
        {
            await RunTest("""
using System;

public class Node { public int A; public int B; public Node Child; public Node() { Console.WriteLine("ctor"); } }

public class Program
{
    static int Log(string s, int v) { Console.WriteLine("eval " + s); return v; }

    public static void Main()
    {
        var n = new Node { A = Log("A", 1), Child = new Node { A = Log("childA", 2) }, B = Log("B", 3) };
        Console.WriteLine(n.A + "," + n.B + "," + n.Child.A);
    }
}
""");
        }

        /// <summary>A struct holding a delegate and a collection: both default to null (not an empty
        /// instance), and both are shared by a copy — same as any other reference-typed slot.</summary>
        [TestMethod]
        public async Task TestStructWithDelegateAndCollectionSlotsAsync()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public struct Bag { public List<int> Items; public Action Cb; public int N; }

public class Program
{
    public static void Main()
    {
        Bag d = default;
        Console.WriteLine((d.Items == null) + "," + (d.Cb == null) + "," + d.N);

        var b = new Bag { N = 1, Items = new List<int> { 1, 2 }, Cb = () => Console.WriteLine("called") };
        Console.WriteLine(b.N + "," + string.Join("|", b.Items));

        var c = b;
        c.Items.Add(3);
        c.Cb();
        Console.WriteLine(string.Join("|", b.Items) + " / " + object.ReferenceEquals(b.Cb, c.Cb));
    }
}
""");
        }

        /// <summary>A struct with a nested struct field used as a dictionary key and a set element: the
        /// synthesized value-wise Equals/GetHashCode must recurse into the nested value, so two
        /// separately built keys with equal contents are one key.</summary>
        [TestMethod]
        public async Task TestStructWithNestedStructAsDictionaryKeyAsync()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public struct Inner { public int V; }
public struct Key { public Inner I; public int N; }

public class Program
{
    public static void Main()
    {
        var d = new Dictionary<Key, string>();
        d[new Key { N = 1, I = { V = 2 } }] = "a";
        Console.WriteLine(d[new Key { N = 1, I = { V = 2 } }]);
        Console.WriteLine(d.ContainsKey(new Key { N = 1, I = { V = 3 } }));

        var set = new HashSet<Key> { new Key { N = 1, I = { V = 2 } }, new Key { N = 1, I = { V = 2 } } };
        Console.WriteLine(set.Count);

        Console.WriteLine(default(Key).Equals(new Key()));
        Console.WriteLine(new Key { N = 1 }.Equals(new Key { N = 1 }));
    }
}
""");
        }

        /// <summary>`default(T)` for an unconstrained T resolves at run time, so `default` of a struct
        /// that nests another struct has to be built recursively there too — via a generic field, a
        /// generic method and a generic class.</summary>
        [TestMethod]
        public async Task TestGenericDefaultOfNestedStructAsync()
        {
            await RunTest("""
using System;

public struct Inner { public int V; }
public struct S { public Inner I; public int X; }

public class Box<T> { public T Value; public T Get() { return default(T); } }

public class Program
{
    static T Def<T>() { return default(T); }

    public static void Main()
    {
        var b = new Box<S>();
        Console.WriteLine(b.Value.I.V + "," + b.Value.X);
        Console.WriteLine(b.Get().I.V + "," + Def<S>().I.V + "," + Def<int>() + "," + (Def<string>() == null));

        var b2 = new Box<Inner>();
        Console.WriteLine(b2.Value.V);
    }
}
""");
        }

        // ---- records ----------------------------------------------------------------------

        /// <summary>A record's positional constructor is the only one that runs, so it has to do
        /// everything a constructor does. It ran NO instance field initializers: `public int K = 7`
        /// stayed 0, `public Nested N = new Nested()` stayed null, and a struct-typed field was null
        /// instead of the zeroed struct.</summary>
        [TestMethod]
        public async Task TestRecordPrimaryConstructorRunsFieldInitializersAsync()
        {
            await RunTest("""
using System;

public class Nested { public int Q = 4; }
public struct Inner { public int V; }

public record class RC(int A) { public Nested N = new Nested(); public int K = 7; public Inner I; public string S = "s"; }

public class Program
{
    public static void Main()
    {
        var c = new RC(1);
        Console.WriteLine(c.A + "," + c.K + "," + c.S + "," + (c.N == null ? "NULL" : c.N.Q.ToString()) + "," + c.I.V);
        c.I.V = 5;
        Console.WriteLine(c.I.V);
    }
}
""");
        }

        /// <summary>The same for a record struct, plus its `default`: the positional members had no
        /// field slot at all, so `default(RS).X` read back `undefined` instead of 0.</summary>
        [TestMethod]
        public async Task TestRecordStructFieldsAndDefaultAsync()
        {
            await RunTest("""
using System;

public struct Inner { public int V; }
public record struct RS(int X) { public Inner I; public int K; }

public class Program
{
    public static void Main()
    {
        var a = new RS(1);
        Console.WriteLine(a.X + "," + a.K + "," + a.I.V);
        a.I.V = 2;
        Console.WriteLine(a.I.V);

        var b = default(RS);
        Console.WriteLine(b.X + "," + b.K + "," + b.I.V);
    }
}
""");
        }

        /// <summary>A derived record's field initializers run before it forwards to the base record's
        /// positional constructor — the same order every other constructor uses.</summary>
        [TestMethod]
        public async Task TestRecordFieldInitializerOrderWithBaseRecordAsync()
        {
            await RunTest("""
using System;

public record class BaseR(int B)
{
    public int BK = Log("base-field-init", 1);
    public static int Log(string s, int v) { Console.WriteLine(s); return v; }
}

public record class DerR(int B, int D) : BaseR(B) { public int DK = Log("derived-field-init", 2); }

public class Program
{
    public static void Main()
    {
        var d = new DerR(1, 2);
        Console.WriteLine(d.B + "," + d.D + "," + d.BK + "," + d.DK);
    }
}
""");
        }

        /// <summary>A record's positional members are real state: they need a field slot (so `default`
        /// zeroes them), a synthesized `Deconstruct` (so `var (x, y) = r` works — it threw
        /// "Deconstruct is not a function"), and they are what a positional pattern matches against
        /// (which read tuple `Item1`/`Item2` slots off the record and so never matched).</summary>
        [TestMethod]
        public async Task TestRecordPositionalMembersDefaultDeconstructAndPatternAsync()
        {
            await RunTest("""
using System;

public record struct RS(int X, int Y);
public record class RC(int A, string B);

public class Program
{
    public static void Main()
    {
        var rs = new RS(1, 2);
        var (x, y) = rs;
        Console.WriteLine(x + "," + y);

        var rc = new RC(3, "b");
        var (a, b) = rc;
        Console.WriteLine(a + "," + b);

        if (rc is RC(3, "b")) Console.WriteLine("positional pattern ok");
        if (rs is RS(1, 2)) Console.WriteLine("struct positional pattern ok");

        Console.WriteLine(default(RS).X + "|" + default(RS).Y + "|" + (default(RC) == null));
    }
}
""");
        }

        /// <summary>A `with` expression copies the value and overwrites the named members — so for a
        /// record struct the copy must carry an independent nested struct, and for a record class the
        /// copy shares its reference-typed members (a shallow copy, per C#).</summary>
        [TestMethod]
        public async Task TestWithExpressionCopySemanticsAsync()
        {
            await RunTest("""
using System;

public struct Inner { public int V; }
public record struct RS(int X, int Y) { public Inner I; }
public class Nested { public int Q; }
public record class RC(int A) { public Inner I; public Nested N = new Nested(); }

public class Program
{
    public static void Main()
    {
        var a = new RS(1, 2);
        var b = a with { Y = 9 };
        a.I.V = 5;
        Console.WriteLine(a.X + "," + a.Y + "," + a.I.V + " | " + b.X + "," + b.Y + "," + b.I.V);

        var c = new RC(1) { N = { Q = 3 } };
        var d = c with { A = 2 };
        Console.WriteLine(c.A + "," + c.N.Q + " | " + d.A + "," + d.N.Q + "," + object.ReferenceEquals(c.N, d.N));
    }
}
""");
        }

        /// <summary>A record with a nested record and a struct member, initialized through a mix of
        /// positional arguments, an object initializer and defaults — the combination a real DTO uses.</summary>
        [TestMethod]
        public async Task TestNestedRecordsWithStructMembersAsync()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public struct Point { public int X; public int Y; }
public record class Leaf(string Name) { public Point At; public int Weight = 3; }
public record class Branch(string Id)
{
    public Leaf Child = new Leaf("default-leaf");
    public Point Origin;
    public List<int> Tags = new List<int>();
}

public class Program
{
    public static void Main()
    {
        var fresh = new Branch("b0");
        Console.WriteLine(fresh.Id + "," + fresh.Child.Name + "," + fresh.Child.Weight + ","
            + fresh.Child.At.X + "," + fresh.Origin.X + "," + fresh.Tags.Count);

        var built = new Branch("b1")
        {
            Child = new Leaf("leaf") { At = { X = 1, Y = 2 }, Weight = 4 },
            Origin = { X = 5, Y = 6 },
            Tags = { 7, 8 }
        };
        Console.WriteLine(built.Id + "," + built.Child.Name + "," + built.Child.At.X + ","
            + built.Child.At.Y + "," + built.Child.Weight + "," + built.Origin.X + ","
            + built.Origin.Y + "," + string.Join("|", built.Tags));

        var moved = built.Origin;
        moved.X = 99;
        Console.WriteLine(built.Origin.X + "," + moved.X);
    }
}
""");
        }
    }
}
