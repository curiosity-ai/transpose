using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// C# records, end to end: every test transpiles the snippet, runs it on Node and diffs the output
    /// against the same C# executed natively, so each one pins the record's observable .NET behaviour
    /// rather than a particular emitted shape.
    ///
    /// The areas are the ones a record actually synthesizes — construction (positional, bodied, mixed,
    /// optional, extra constructors), value equality and hashing, <c>ToString</c>/<c>PrintMembers</c>,
    /// <c>with</c>, <c>Deconstruct</c>, positional patterns and inheritance — each covered for a record
    /// <b>class</b> and a record <b>struct</b>, and each in both the one-line positional form and the
    /// bodied form. On top of that: what happens when the record declares one of those members itself
    /// (its version has to win), and <c>[ObjectLiteral]</c>, where a record declares the shape of a plain
    /// JavaScript object.
    ///
    /// Behaviours these tests deliberately pin down, each of which was wrong before:
    ///  - <b>ToString prints public FIELDS too</b>, and only <b>public</b> members — C#'s PrintMembers
    ///    walks non-static public fields and public readable properties, base record first;
    ///  - <b>equality compares FIELDS</b>, including private ones and auto-property backing fields, and
    ///    NOT computed properties — so a record with a get-only property that allocates
    ///    (<c>int[] Cache =&gt; new[] { V }</c>) still compares equal;
    ///  - a hand-written <c>ToString</c>, <c>PrintMembers</c>, <c>Equals</c>, <c>GetHashCode</c> or
    ///    <c>Deconstruct</c> replaces the synthesized one instead of colliding with it;
    ///  - a positional parameter is only stored when the record actually synthesized a property for it
    ///    (a body-declared member of the same name suppresses that), and an optional one defaults;
    ///  - a member typed <c>char</c>, an enum, a nullable enum or a type parameter renders in ToString
    ///    the way .NET does (a character / a member name), not as its numeric representation.
    /// </summary>
    [TestClass]
    public class RecordTests : TranslatorTestBase
    {
        // ---- declaration forms -------------------------------------------------

        [TestMethod]
        public async Task RecordClassDeclarationForms()
        {
            await RunTest("""
using System;

public record OneLine(int X);
public record ClassKeyword(int X, string S);
public sealed record Sealed(int X);
public record NoMembers;
public record BodyOnly { public int X { get; init; } }
public record PositionalAndBody(int X) { public int Field = 7; public int Prop { get; init; } = 9; }
public record EmptyParameterList() { public int A { get; init; } }

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new OneLine(1));
        Console.WriteLine(new ClassKeyword(1, "s"));
        Console.WriteLine(new Sealed(2));
        Console.WriteLine(new NoMembers());
        Console.WriteLine(new BodyOnly { X = 3 });
        Console.WriteLine(new PositionalAndBody(4));
        Console.WriteLine(new EmptyParameterList { A = 5 });
    }
}
""");
        }

        [TestMethod]
        public async Task RecordStructDeclarationForms()
        {
            await RunTest("""
using System;

public record struct OneLine(int X);
public readonly record struct ReadOnly(int X, string T);
public record struct NoMembers;
public record struct BodyOnly { public int X { get; set; } }
public record struct PositionalAndBody(int X) { public int Y; }

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new OneLine(1));
        Console.WriteLine(new ReadOnly(1, "t"));
        Console.WriteLine(new NoMembers());
        Console.WriteLine(default(NoMembers));
        Console.WriteLine(new BodyOnly { X = 2 });
        Console.WriteLine(new PositionalAndBody(3) { Y = 4 });
        // A record struct's default value zero-initializes every slot rather than staying undefined.
        Console.WriteLine(default(OneLine));
        Console.WriteLine(default(ReadOnly));
    }
}
""");
        }

        // ---- construction ------------------------------------------------------

        [TestMethod]
        public async Task ExtraConstructorsChainToThePositionalOne()
        {
            await RunTest("""
using System;

public record R(int X, int Y)
{
    public R(int x) : this(x, x * 2) { }
    public R() : this(0) { }
    public int Sum => X + Y;
}

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new R(1, 2));
        Console.WriteLine(new R(3));
        Console.WriteLine(new R());
        Console.WriteLine(new R(3).Sum);
    }
}
""");
        }

        [TestMethod]
        public async Task OptionalPositionalParametersDefault()
        {
            await RunTest("""
using System;

public record Defaults(int X = 5, string S = "d");
public record struct SDefaults(int X = 5, string S = "d");

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new Defaults());
        Console.WriteLine(new Defaults(1));
        Console.WriteLine(new Defaults(1, "x"));
        Console.WriteLine(new Defaults(S: "x"));
        Console.WriteLine(new SDefaults());
        Console.WriteLine(new SDefaults(2));
    }
}
""");
        }

        [TestMethod]
        public async Task RecordStructHasTheImplicitParameterlessConstructor()
        {
            // `new S()` on a `record struct S(int X)` reaches the implicit parameterless struct
            // constructor, not the positional one — so it zeroes the value, and does NOT apply either the
            // positional parameters' defaults or the declared field initializers.
            await RunTest("""
using System;

public record struct Plain(int X);
public record struct WithDefaults(int X = 5, string S = "d");
public record struct WithInitializer(int X) { public int Y = 1; }
public record struct Nested(int X) { public Plain P; }

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new Plain());
        Console.WriteLine(new WithDefaults());
        Console.WriteLine(new WithDefaults(2));
        Console.WriteLine(new WithInitializer().Y);
        Console.WriteLine(new WithInitializer(1).Y);
        Console.WriteLine(default(WithInitializer).Y);
        // A struct-typed slot is still default(T) rather than null, so reading through it works.
        Console.WriteLine(new Nested().P.X);
    }
}
""");
        }

        [TestMethod]
        public async Task PositionalParameterFlowsIntoInitializersAndDeclaredMembers()
        {
            await RunTest("""
using System;

// The body declares X itself, so C# does NOT synthesize a property for the parameter and does not
// store it; the parameter is only what the initializer reads.
public record Shadowed(int X)
{
    public int X { get; init; } = X * 2;
}

public record Initializers(int X)
{
    public int Field = X + 1;
    public int Prop { get; } = X + 2;
}

public record FromString(string S)
{
    public int Len => S.Length;
    public string Upper { get; } = S.ToUpper();
}

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new Shadowed(3).X);
        var i = new Initializers(1);
        Console.WriteLine(i.Field + " " + i.Prop);
        var f = new FromString("ab");
        Console.WriteLine(f.Len + " " + f.Upper);
        Console.WriteLine(f);
    }
}
""");
        }

        [TestMethod]
        public async Task RequiredMembers()
        {
            // A `required` member makes Roslyn stamp [SetsRequiredMembersAttribute] on the constructors
            // that satisfy it — for a record, the synthesized copy constructor `with` uses — so the
            // attribute has to exist in the BCL or the whole compilation fails.
            await RunTest("""
using System;

public record Req
{
    public required int R { get; init; }
    public required string Name { get; init; }
    public int O { get; init; } = 4;
}

public record struct SReq { public required int R { get; init; } }
public record ReqPos(int X) { public required string Name { get; init; } }

public class Program
{
    public static void Main()
    {
        var a = new Req { R = 1, Name = "n" };
        Console.WriteLine(a);
        Console.WriteLine(a with { R = 2 });
        Console.WriteLine(a == new Req { R = 1, Name = "n" });
        Console.WriteLine(new SReq { R = 5 });
        Console.WriteLine(new ReqPos(1) { Name = "p" });
    }
}
""");
        }

        [TestMethod]
        public async Task RecordBodyMembersAreInitializedByThePositionalConstructor()
        {
            await RunTest("""
using System;

public struct Inner { public int V; }
public class Ref { public int V = 3; }

public record R(int X)
{
    public int K = 7;
    public Ref Made = new Ref();
    public Inner I;
    public Ref Unset;
}

public class Program
{
    public static void Main()
    {
        var r = new R(1);
        Console.WriteLine(r.K);
        Console.WriteLine(r.Made.V);
        Console.WriteLine(r.I.V);
        Console.WriteLine(r.Unset == null);
        r.I.V = 9;
        Console.WriteLine(r.I.V);
    }
}
""");
        }

        [TestMethod]
        public async Task DerivedRecordInitializersRunBeforeTheBaseConstructor()
        {
            // C#'s order for any derived type: the derived instance initializers, then the base
            // constructor (which runs the base's own initializers).
            await RunTest("""
using System;

public record B(int A) { public int BX = Program.Log("base-init"); }
public record D(int A, int C) : B(A) { public int DX = Program.Log("derived-init"); }

public class Program
{
    public static int Log(string s) { Console.WriteLine(s); return 1; }
    public static void Main() { Console.WriteLine(new D(1, 2)); }
}
""");
        }

        // ---- equality and hashing ----------------------------------------------

        [TestMethod]
        public async Task RecordClassValueEquality()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public record RC(int X, string S);

public class Program
{
    public static void Main()
    {
        var a = new RC(1, "a");
        var b = new RC(1, "a");
        var c = new RC(2, "a");
        Console.WriteLine(a == b);
        Console.WriteLine(a != c);
        Console.WriteLine(a.Equals(b));
        Console.WriteLine(a.Equals((object)b));
        Console.WriteLine(a.Equals((object)"nope"));
        Console.WriteLine(a.GetHashCode() == b.GetHashCode());
        Console.WriteLine(a.GetHashCode() == c.GetHashCode());
        Console.WriteLine(ReferenceEquals(a, b));

        var set = new HashSet<RC> { a, b, c };
        Console.WriteLine(set.Count);
        var d = new Dictionary<RC, int>();
        d[a] = 1;
        d[b] = 2;
        Console.WriteLine(d.Count + " " + d[a]);

        RC nil = null;
        Console.WriteLine(nil == null);
        Console.WriteLine(a == null);
        Console.WriteLine(a.Equals(null));
    }
}
""");
        }

        [TestMethod]
        public async Task RecordStructValueEquality()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public record struct RS(int X, string S);

public class Program
{
    public static void Main()
    {
        var a = new RS(1, "a");
        var b = new RS(1, "a");
        var c = new RS(3, "a");
        Console.WriteLine(a == b);
        Console.WriteLine(a != c);
        Console.WriteLine(a.Equals(b));
        Console.WriteLine(a.Equals((object)b));
        Console.WriteLine(a.GetHashCode() == b.GetHashCode());

        var set = new HashSet<RS> { a, b, c };
        Console.WriteLine(set.Count);

        // Boxed: a record struct keeps its value semantics through object.
        object boxed = a;
        Console.WriteLine(boxed.Equals(b));
        Console.WriteLine(boxed.GetHashCode() == b.GetHashCode());
        Console.WriteLine(boxed.ToString());
    }
}
""");
        }

        [TestMethod]
        public async Task EqualityAndToStringCoverPublicFieldsOfARecordBody()
        {
            // C# compares a record's FIELDS (so a public field participates in ==/GetHashCode) and
            // PRINTS its public fields and public readable properties. Comparing/printing only the
            // properties dropped `F`/`G` from both.
            await RunTest("""
using System;

public record RC(int X) { public int F = 1; public int G; }
public record struct RS(int X) { public int F; }

public class Program
{
    public static void Main()
    {
        var a = new RC(1);
        var b = new RC(1);
        b.F = 99;
        Console.WriteLine(a);
        Console.WriteLine(b);
        Console.WriteLine(a == b);
        Console.WriteLine(a.GetHashCode() == b.GetHashCode());
        // `with` copies every field, declared ones included.
        var c = a with { X = 1 };
        Console.WriteLine(c.F);
        Console.WriteLine(a == c);

        var s = new RS(1) { F = 5 };
        var s2 = new RS(1) { F = 6 };
        Console.WriteLine(s);
        Console.WriteLine(s == s2);
    }
}
""");
        }

        [TestMethod]
        public async Task OnlyPublicMembersArePrintedAndOnlyFieldsCompared()
        {
            await RunTest("""
using System;

public record RC(int X)
{
    public int Computed => X * 2;          // public + readable: printed, but no storage to compare
    private int _priv = 5;                 // private field: compared, never printed
    public int Priv => _priv;
    public void Bump() { _priv++; }
    public static int Stat = 3;            // static: neither
    public static int StatProp { get; set; }
    public int WriteOnly { set { _priv = value; } }   // no getter: not printed
    internal int Internal { get; set; }     // not public: not printed, but its field IS compared
    private int PrivProp { get; set; }
    public int Manual { get { return _priv; } set { _priv = value; } }
}

public class Program
{
    public static void Main()
    {
        var a = new RC(1);
        var b = new RC(1);
        b.Bump();
        Console.WriteLine(a);
        Console.WriteLine(b);
        Console.WriteLine(a == b);         // the private field differs
        var c = new RC(1);
        c.Internal = 7;
        Console.WriteLine(c);              // Internal is not printed
        Console.WriteLine(a == c);         // but its backing field is compared
    }
}
""");
        }

        [TestMethod]
        public async Task ComputedPropertiesDoNotParticipateInEquality()
        {
            // Equality compares fields, so a get-only property that allocates on every read must not
            // make two otherwise-equal records unequal.
            await RunTest("""
using System;

public record Node(int V)
{
    public int[] Cache => new int[] { V };
    public object Fresh => new object();
}

public class Program
{
    public static void Main()
    {
        var a = new Node(1);
        var b = new Node(1);
        Console.WriteLine(a == b);
        Console.WriteLine(a.Equals(b));
        Console.WriteLine(a.GetHashCode() == b.GetHashCode());
    }
}
""");
        }

        // ---- ToString ----------------------------------------------------------

        [TestMethod]
        public async Task ToStringFormatsMemberValuesLikeDotNet()
        {
            await RunTest("""
using System;

public enum Color { Red, Green }
public struct Plain { public int V; public override string ToString() => "P" + V; }

public record R(char C, Color E, Color? NE, bool B, Plain P, int? NI, double D, float F, string S);
public record G<T>(T V);

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new R('c', Color.Green, Color.Red, true, new Plain { V = 1 }, 2, 1.5, 2.5f, "s"));
        Console.WriteLine(new R('c', Color.Green, null, false, new Plain(), null, 0, 0, null));
        // A member typed as a type parameter renders through the type argument bound at runtime.
        Console.WriteLine(new G<char>('x'));
        Console.WriteLine(new G<Color>(Color.Green));
        Console.WriteLine(new G<string>(null));
        Console.WriteLine(new G<int>(3));
    }
}
""");
        }

        [TestMethod]
        public async Task NestedRecordsPrintRecursively()
        {
            await RunTest("""
using System;

public record Address(string City, string Zip);
public record Person(string Name, Address Addr);
public record struct SAddr(int N);
public record struct SPerson(SAddr A, int Id);

public class Program
{
    public static void Main()
    {
        var p = new Person("a", new Address("c", "z"));
        Console.WriteLine(p);
        Console.WriteLine(p == new Person("a", new Address("c", "z")));
        var r = p with { Addr = p.Addr with { City = "d" } };
        Console.WriteLine(r);
        Console.WriteLine(p == r);

        var s = new SPerson(new SAddr(1), 2);
        Console.WriteLine(s);
        Console.WriteLine(s == new SPerson(new SAddr(1), 2));
    }
}
""");
        }

        // ---- with --------------------------------------------------------------

        [TestMethod]
        public async Task WithExpressions()
        {
            await RunTest("""
using System;

public record RC(int X, string S) { public int Extra { get; init; } }
public record struct RS(int X, string S);

public class Program
{
    public static void Main()
    {
        var a = new RC(1, "a") { Extra = 5 };
        var b = a with { X = 2 };
        Console.WriteLine(b);
        Console.WriteLine(b.Extra);
        var c = a with { };
        Console.WriteLine(c == a);
        Console.WriteLine(ReferenceEquals(c, a));
        var d = a with { X = 9, S = "z", Extra = 8 };
        Console.WriteLine(d);
        Console.WriteLine(d.Extra);

        // `with` on a record struct leaves the source untouched.
        var sa = new RS(1, "a");
        Console.WriteLine(sa with { X = 4 });
        Console.WriteLine(sa);
    }
}
""");
        }

        [TestMethod]
        public async Task WithKeepsTheRuntimeTypeOfADerivedRecord()
        {
            await RunTest("""
using System;

public abstract record Base(int A);
public record Mid(int A, int B) : Base(A);
public record Leaf(int A, int B, int C) : Mid(A, B);

public class Program
{
    public static void Main()
    {
        var l = new Leaf(1, 2, 3);
        var withL = l with { C = 9 };
        Console.WriteLine(withL);
        Console.WriteLine(withL.GetType().Name);
        // Through the base type the copy is still a Leaf, with the untouched members preserved.
        Mid asMid = l;
        var withMid = asMid with { B = 7 };
        Console.WriteLine(withMid);
        Console.WriteLine(withMid.GetType().Name);
    }
}
""");
        }

        // ---- inheritance -------------------------------------------------------

        [TestMethod]
        public async Task RecordInheritanceChain()
        {
            await RunTest("""
using System;

public abstract record Base(int A);
public record Mid(int A, int B) : Base(A);
public record Leaf(int A, int B, int C) : Mid(A, B);
public record Other(int A, int B) : Base(A);

public class Program
{
    public static void Main()
    {
        var l = new Leaf(1, 2, 3);
        Console.WriteLine(l);                       // base members first
        Console.WriteLine(((Base)l).A);
        Console.WriteLine(l == new Leaf(1, 2, 3));
        // Two records of different types are never equal, however alike their members.
        var m = new Mid(1, 2);
        var o = new Other(1, 2);
        Console.WriteLine(m.Equals(o));
        Console.WriteLine(((Base)m).Equals((Base)o));
        Console.WriteLine(typeof(Leaf).BaseType.Name);
        Console.WriteLine(l is Mid);
    }
}
""");
        }

        [TestMethod]
        public async Task DerivedRecordsInheritDeclaredToStringAndPrintMembers()
        {
            await RunTest("""
using System;
using System.Text;

// A hand-written PrintMembers on the base record is what the derived record's synthesized
// PrintMembers chains to, so it shows through the derived ToString as well.
public record Base(int A)
{
    protected virtual bool PrintMembers(StringBuilder b) { b.Append("A!"); return true; }
}
public record Derived(int A, int B) : Base(A);

public record B2(int A);
public record D2(int A, int B) : B2(A) { public override string ToString() => "D2!" + A + B; }

public record B3(int A) { public override string ToString() => "B3!"; }
public record D3(int A, int B) : B3(A);

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new Base(1));
        Console.WriteLine(new Derived(1, 2));
        Console.WriteLine(new D2(1, 2));
        Console.WriteLine(new D3(1, 2));
        Base b = new Derived(1, 2);
        Console.WriteLine(b.ToString());
    }
}
""");
        }

        [TestMethod]
        public async Task DerivedRecordsPrintHiddenAndOverriddenMembersLikeDotNet()
        {
            // An OVERRIDE is printed once — the base record's PrintMembers prints it, and virtual
            // dispatch there reads the override's value. A `new` member is printed twice, once from each
            // record's own set, which is what .NET does.
            await RunTest("""
using System;

public record Base(int A) { public int Shared => 1; public virtual int V => 1; }
public record Derived(int A, int B) : Base(A) { public new int Shared => 2; public override int V => 2; }

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new Base(1));
        Console.WriteLine(new Derived(1, 2));
        Console.WriteLine(new Derived(1, 2) == new Derived(1, 2));
        Console.WriteLine(((Base)new Derived(1, 2)).V);
    }
}
""");
        }

        // ---- declared members win over the synthesized ones --------------------

        [TestMethod]
        public async Task DeclaredToStringAndPrintMembersReplaceTheSynthesizedOnes()
        {
            await RunTest("""
using System;
using System.Text;

public record CustomToString(int X) { public override string ToString() => "custom " + X; }
public record SealedToString(int X) { public sealed override string ToString() => "sealed " + X; }

public record CustomPrint(int X, int Y)
{
    protected virtual bool PrintMembers(StringBuilder b) { b.Append("only X=").Append(X); return true; }
}

// PrintMembers returning false means "nothing printed", so ToString has no inner spacing.
public record PrintNothing(int X)
{
    protected virtual bool PrintMembers(StringBuilder b) => false;
}

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new CustomToString(1));
        Console.WriteLine(new SealedToString(2));
        Console.WriteLine(new CustomPrint(1, 2));
        Console.WriteLine(new PrintNothing(1));
    }
}
""");
        }

        [TestMethod]
        public async Task DeclaredEqualsAndGetHashCodeGovernEveryComparison()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public record R(int X) : IEquatable<R>
{
    // Two records are equal when they share a decade.
    public virtual bool Equals(R other) => other != null && X / 10 == other.X / 10;
    public override int GetHashCode() => X / 10;
}

public record struct SR(int X)
{
    public bool Equals(SR other) => true;
    public override int GetHashCode() => 43;
}

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new R(11) == new R(12));
        Console.WriteLine(new R(11).Equals(new R(12)));
        Console.WriteLine(new R(11).Equals((object)new R(12)));   // must route to the declared Equals
        Console.WriteLine(new R(11).GetHashCode());
        Console.WriteLine(new HashSet<R> { new R(11), new R(12) }.Count);

        Console.WriteLine(new SR(1) == new SR(2));
        Console.WriteLine(new SR(1).Equals((object)new SR(2)));
        Console.WriteLine(new SR(1).GetHashCode());
    }
}
""");
        }

        [TestMethod]
        public async Task ADeclaredDeconstructCoexistsWithTheSynthesizedOne()
        {
            // The record still synthesizes Deconstruct(out int X) for its one positional parameter, so
            // both overloads exist and each call has to reach the right one.
            await RunTest("""
using System;

public record R(int X)
{
    public void Deconstruct(out int a, out int b) { a = X; b = X * 2; }
}

public class Program
{
    public static void Main()
    {
        var (a, b) = new R(5);
        Console.WriteLine(a + " " + b);
        new R(7).Deconstruct(out var x);
        Console.WriteLine(x);
        new R(7).Deconstruct(out var p, out var q);
        Console.WriteLine(p + " " + q);
    }
}
""");
        }

        // ---- deconstruction and patterns ---------------------------------------

        [TestMethod]
        public async Task Deconstruction()
        {
            await RunTest("""
using System;

public record RC(int X, string S);
public record struct RS(int X, string S);
public record Three(int A, int B, int C);

public class Program
{
    public static void Main()
    {
        var (x, s) = new RC(1, "a");
        Console.WriteLine(x + " " + s);
        var (y, t) = new RS(2, "b");
        Console.WriteLine(y + " " + t);
        var (p, q, r) = new Three(1, 2, 3);
        Console.WriteLine(p + q + r);

        int a; string b;
        (a, b) = new RC(4, "d");
        Console.WriteLine(a + b);

        new RC(5, "e").Deconstruct(out var m, out var n);
        Console.WriteLine(m + n);

        var (u, (v, w)) = (1, new RC(2, "z"));
        Console.WriteLine(u + " " + v + " " + w);

        foreach (var (k, l) in new[] { new RC(1, "a"), new RC(2, "b") })
            Console.WriteLine(k + l);

        var (_, only) = new RC(6, "f");
        Console.WriteLine(only);
    }
}
""");
        }

        [TestMethod]
        public async Task PositionalAndPropertyPatterns()
        {
            await RunTest("""
using System;

public record RC(int X, string S);
public record struct RS(int X, string S);
public abstract record Shape;
public record Circle(double R) : Shape;
public record Rect(double W, double H) : Shape;

public class Program
{
    public static void Main()
    {
        object o = new RC(1, "a");
        Console.WriteLine(o is RC(1, "a"));
        Console.WriteLine(o is RC(2, "a"));
        Console.WriteLine(o is RC { X: 1, S: "a" });
        Console.WriteLine(o is RC(var xx, var ss) ? xx + ss : "no");

        var rs = new RS(3, "c");
        Console.WriteLine(rs is RS(3, _));

        Console.WriteLine(Describe(new Rect(2, 3)));
        Console.WriteLine(Describe(new Circle(1)));
        Console.WriteLine(Describe(null));
    }

    static string Describe(Shape s) => s switch
    {
        Circle(var r) => "circle " + r,
        Rect(var w, var h) => "rect " + (w * h),
        null => "none",
        _ => "?"
    };
}
""");
        }

        // ---- generics, interfaces, nesting -------------------------------------

        [TestMethod]
        public async Task GenericRecords()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public record Box<T>(T Value);
public record Pair<TK, TV>(TK Key, TV Value) { public string Show() => Key + "=" + Value; }
public record struct SBox<T>(T Value);
public record Node<T>(T Value, Node<T> Next) where T : struct;
public record Outer<T>(T V) { public record Inner(T W); }
public class Holder<T> { public record Rec(T V); }

public class Program
{
    public static void Main()
    {
        var b = new Box<int>(1);
        Console.WriteLine(b);
        Console.WriteLine(b == new Box<int>(1));
        Console.WriteLine(new Box<string>("s"));
        var p = new Pair<string, int>("a", 1);
        Console.WriteLine(p.Show());
        Console.WriteLine(p);
        Console.WriteLine(new SBox<int>(2));
        Console.WriteLine(new SBox<int>(2) == new SBox<int>(2));
        Console.WriteLine(new Node<int>(1, new Node<int>(2, null)).Next.Value);
        Console.WriteLine(new Box<int>(1) with { Value = 5 });
        Console.WriteLine(new List<Box<int>> { new(1), new(2) }.Contains(new Box<int>(2)));
        Console.WriteLine(new Outer<int>.Inner(1));
        Console.WriteLine(new Holder<int>.Rec(2));
    }
}
""");
        }

        [TestMethod]
        public async Task RecordsImplementingInterfaces()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public interface IHasName { string Name { get; } }

public record Person(string Name, int Age) : IHasName, IComparable<Person>
{
    public int CompareTo(Person other) => Age.CompareTo(other.Age);
}

public record struct SPerson(string Name) : IHasName;

public class Program
{
    public static void Main()
    {
        IHasName n = new Person("a", 1);
        Console.WriteLine(n.Name);
        var list = new List<Person> { new("c", 3), new("a", 1), new("b", 2) };
        list.Sort();
        foreach (var p in list) Console.WriteLine(p);
        IHasName sn = new SPerson("s");
        Console.WriteLine(sn.Name);
        Console.WriteLine(((IHasName)new SPerson("q")).Name);
    }
}
""");
        }

        [TestMethod]
        public async Task NestedPartialAndStaticRecordMembers()
        {
            await RunTest("""
using System;

public partial record P(int X);
public partial record P { public int Y => X + 1; }

public record Nested
{
    public record Inner(int V);
    public Inner I { get; init; }
}

public class Outer { public record InClass(int V); }

public record WithStatics(int X)
{
    public static int Count;
    static WithStatics() { Count = 10; }
    public static WithStatics Create(int x) => new WithStatics(x);
    public const int K = 3;
    public event Action Ev;
    public void Fire() => Ev?.Invoke();
    public int Method() => X + K;
    public int this[int i] => X + i;
}

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new P(1).Y);
        Console.WriteLine(new Nested { I = new Nested.Inner(7) }.I);
        Console.WriteLine(new Outer.InClass(8));

        Console.WriteLine(WithStatics.Count);
        Console.WriteLine(WithStatics.Create(1));
        Console.WriteLine(WithStatics.K);
        var r = new WithStatics(1);
        r.Ev += () => Console.WriteLine("fired");
        r.Fire();
        Console.WriteLine(r.Method());
        Console.WriteLine(r[2]);
        Console.WriteLine(r);       // an indexer/event/const is not a printed member
    }
}
""");
        }

        [TestMethod]
        public async Task RecordsCaptureTheirPositionalParametersInLambdas()
        {
            await RunTest("""
using System;

public record Cap(int X)
{
    public Func<int> Field = () => 7;
    public Func<int> Make() => () => X * 2;
}

public class Program
{
    public static void Main()
    {
        var c = new Cap(3);
        Console.WriteLine(c.Field());
        Console.WriteLine(c.Make()());
    }
}
""");
        }

        // ---- record structs are value types ------------------------------------

        [TestMethod]
        public async Task RecordStructsCopyByValue()
        {
            await RunTest("""
using System;

public record struct Inner(int V);
public record struct Outer(Inner I, int N);

public class Program
{
    public static void Main()
    {
        var a = new Outer(new Inner(1), 2);
        var b = a;
        b.I = new Inner(9);
        Console.WriteLine(a.I.V + " " + b.I.V);          // the assignment copied the value

        var c = a with { N = 5 };
        Console.WriteLine(a.N + " " + c.N);
        Console.WriteLine(c.I.V);

        Mutate(a);
        Console.WriteLine(a.N);                           // by-value argument

        var arr = new Outer[2];
        Console.WriteLine(arr[0].N + " " + arr[0].I.V);   // default-initialized elements

        object boxed = a;
        var d = (Outer)boxed;
        d.N = 77;
        Console.WriteLine(a.N + " " + d.N);
    }

    static void Mutate(Outer o) { o.N = 100; }
}
""");
        }

        [TestMethod]
        public async Task RecordStructMembersAndMutation()
        {
            await RunTest("""
using System;

public readonly record struct RS(int X, string S)
{
    public int Double => X * 2;                 // a get-only computed member on a readonly struct
    public RS Grow() => this with { X = X + 1 };
}

public record struct MS(int X)
{
    public void Bump() => X++;
}

public record struct SExplicit(int X)
{
    public SExplicit() : this(42) { }
}

public class Program
{
    public static void Main()
    {
        var a = new RS(1, "a");
        Console.WriteLine(a.Double);
        Console.WriteLine(a.Grow());
        Console.WriteLine(a);

        var m = new MS(1);
        m.Bump();
        Console.WriteLine(m);
        var m2 = m;
        m2.Bump();
        Console.WriteLine(m + " " + m2);

        Console.WriteLine(new SExplicit());
        Console.WriteLine(new SExplicit(1));
    }
}
""");
        }

        // ---- reflection and collections ----------------------------------------

        [TestMethod]
        public async Task RecordsInLinqAndCollections()
        {
            await RunTest("""
using System;
using System.Collections.Generic;
using System.Linq;

public record Item(string Name, int Qty);

public class Program
{
    public static void Main()
    {
        var items = new List<Item> { new("b", 2), new("a", 1), new("a", 1) };
        Console.WriteLine(items.Distinct().Count());
        Console.WriteLine(string.Join(";", items.OrderBy(i => i.Name).Select(i => i.Name)));
        Console.WriteLine(items.GroupBy(i => i).Count());
        Console.WriteLine(items.Contains(new Item("a", 1)));
        Console.WriteLine(items.Distinct().ToDictionary(i => i, i => i.Qty)[new Item("a", 1)]);
        Console.WriteLine(items.FirstOrDefault(i => i.Name == "z") is null);
        Console.WriteLine(items.Any(i => i is Item("a", 1)));
    }
}
""");
        }

        [TestMethod]
        public async Task RecordRuntimeTypes()
        {
            await RunTest("""
using System;

public record R(int X);
public record struct S(int X);
public abstract record BaseR(int A);
public record DerivedR(int A, int B) : BaseR(A);

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new R(1).GetType().Name);
        Console.WriteLine(new S(1).GetType().Name);
        Console.WriteLine(typeof(R).Name);
        BaseR b = new DerivedR(1, 2);
        Console.WriteLine(b.GetType().Name);
        Console.WriteLine(b is DerivedR);
        Console.WriteLine(typeof(DerivedR).BaseType.Name);

        object o = new S(3);
        Console.WriteLine(o is S);
        Console.WriteLine(((S)o).X);
        Console.WriteLine(o.ToString());
        Console.WriteLine(o.Equals(new S(3)));
    }
}
""");
        }

        [TestMethod]
        public async Task RecordShapesWithoutAPositionalConstructor()
        {
            await RunTest("""
using System;

// Nothing public: ToString prints an empty body, but the private field still separates the values.
public record Hidden { private int _x = 1; internal int I { get; init; } }
public record OnlyField { public int F = 2; }

public abstract record AbstractBase { public int A { get; init; } }
public record Concrete : AbstractBase { public int B { get; init; } }

public interface IWithDefault { int N { get; } }
public record WithInterface(int X) : IWithDefault { public int N => 5; }

public record struct SOuter(int X) { public record SInner(int Y); }
public record Tupled((int A, string B) T, WithInterface Maybe);

public class Program
{
    public static void Main()
    {
        Console.WriteLine(new Hidden { I = 3 });
        Console.WriteLine(new Hidden { I = 3 } == new Hidden { I = 4 });
        Console.WriteLine(new OnlyField());
        Console.WriteLine(new Concrete { A = 1, B = 2 });
        Console.WriteLine(new Concrete { A = 1, B = 2 } == new Concrete { A = 1, B = 2 });
        Console.WriteLine(((IWithDefault)new WithInterface(1)).N);
        Console.WriteLine(new SOuter.SInner(1));
        Console.WriteLine(new Tupled((1, "b"), null));
        Console.WriteLine(new Tupled((1, "b"), new WithInterface(2)));
        Console.WriteLine(new Tupled((1, "b"), null) == new Tupled((1, "b"), null));
    }
}
""");
        }

        [TestMethod]
        public async Task RecordsWithUserDefinedOperatorsAndConversions()
        {
            // Declaring operators does not displace the record's synthesized `==`, which stays value-wise.
            await RunTest("""
using System;

public record Money(decimal Amount, string Currency)
{
    public static Money operator +(Money a, Money b) => new Money(a.Amount + b.Amount, a.Currency);
    public static implicit operator decimal(Money m) => m.Amount;
    public override string ToString() => Amount + " " + Currency;
}

public record struct Vec(double X, double Y)
{
    public static Vec operator +(Vec a, Vec b) => new Vec(a.X + b.X, a.Y + b.Y);
    public static bool operator >(Vec a, Vec b) => a.X > b.X;
    public static bool operator <(Vec a, Vec b) => a.X < b.X;
}

public record Wrapper<T>(T Value) where T : notnull
{
    public Wrapper<TOut> Map<TOut>(Func<T, TOut> f) where TOut : notnull => new Wrapper<TOut>(f(Value));
}

public class Program
{
    public static void Main()
    {
        var m = new Money(1.5m, "EUR") + new Money(2.5m, "EUR");
        Console.WriteLine(m);
        decimal d = m;
        Console.WriteLine(d);
        Console.WriteLine(new Vec(1, 2) + new Vec(3, 4));
        Console.WriteLine(new Vec(3, 0) > new Vec(1, 0));
        Console.WriteLine(new Wrapper<int>(2).Map(x => "v" + x));
        Console.WriteLine(new Vec(1, 2) == new Vec(1, 2));
        Console.WriteLine(new Money(1m, "E") == new Money(1m, "E"));
    }
}
""");
        }

        [TestMethod]
        public async Task RecordsAcrossAsyncBoundariesAndSwitchGuards()
        {
            await RunTest("""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public record Job(int Id, string Name);

public class Program
{
    static async Task<Job> Fetch(int id) => await Task.FromResult(new Job(id, "j" + id));

    public static void Main() { Run().Wait(); }

    static async Task Run()
    {
        var j = await Fetch(1);
        Console.WriteLine(j);
        var all = await Task.WhenAll(Enumerable.Range(1, 3).Select(Fetch));
        Console.WriteLine(string.Join(";", all.Select(x => x.Name)));
        Console.WriteLine(Describe(j));
        Console.WriteLine(Describe(new Job(99, "big")));
        // A record built after an await is still the same dictionary key.
        var d = new Dictionary<Job, int> { [j] = 1 };
        Console.WriteLine(d[await Fetch(1)]);
        Console.WriteLine("<<DONE>>");
    }

    static string Describe(Job j) => j switch
    {
        Job(var id, _) when id > 50 => "big " + id,
        Job(1, var n) => "first " + n,
        _ => "other"
    };
}
""", waitForOutput: "<<DONE>>");
        }

        // ---- renamed members ---------------------------------------------------

        [TestMethod]
        public async Task RenamedRecordMembersKeepTheirCSharpNameInToString()
        {
            // [Name] moves the member's JS slot; ToString still prints the C# name (that is what .NET
            // writes), and the positional store, equality and Deconstruct all have to follow the slot.
            const string code = """
using System;
using Transpose;

public record R([property: Name("jsX")] int X)
{
    [Name("jsF")] public int F = 3;
}

public class Program
{
    public static void Main()
    {
        var r = new R(1);
        Console.WriteLine(r);
        Console.WriteLine(r.X + " " + r.F);
        Console.WriteLine(r == new R(1));
        r.Deconstruct(out var x);
        Console.WriteLine(x);
    }
}
""";
            await RunTest(code, overrideRoslynCode: code
                .Replace("using Transpose;", "")
                .Replace("""[property: Name("jsX")] """, "")
                .Replace("""[Name("jsF")] """, ""));
        }

        // ---- [ObjectLiteral] ---------------------------------------------------

        [TestMethod]
        public async Task ObjectLiteralRecordCarriesItsPositionalArguments()
        {
            // A record is the natural way to declare the shape of a plain JavaScript object, so an
            // [ObjectLiteral] record's positional arguments have to become the literal's members.
            // Natively the attribute does nothing (it is [NonScriptable]), so the same program run as
            // real records must observe the same values — which is what the comparison asserts.
            const string code = """
using System;
using Transpose;

[ObjectLiteral]
public record Point(int X, int Y);

[ObjectLiteral]
public record struct SPoint(int X, int Y);

[ObjectLiteral]
public record Defaults(int X = 7, string S = "d");

[ObjectLiteral]
public record Named(int A, int B);

public class Program
{
    public static void Main()
    {
        var p = new Point(1, 2);
        Console.WriteLine(p.X + "," + p.Y);
        var s = new SPoint(3, 4);
        Console.WriteLine(s.X + "," + s.Y);
        var d = new Defaults();
        Console.WriteLine(d.X + "," + d.S);
        var n = new Named(B: 2, A: 1);
        Console.WriteLine(n.A + "," + n.B);
        // `with` on a literal copies the plain object.
        var w = p with { X = 9 };
        Console.WriteLine(w.X + "," + w.Y);
    }
}
""";
            await RunTest(code, overrideRoslynCode: code.Replace("using Transpose;", "")
                                                        .Replace("[ObjectLiteral]", "")
                                                        .Replace("[ObjectLiteral(ObjectInitializationMode.DefaultValue)]", ""));
        }

        [TestMethod]
        public async Task ObjectLiteralRecordInitializationModes()
        {
            const string code = """
using System;
using Transpose;

[ObjectLiteral(ObjectInitializationMode.DefaultValue)]
public record All(string Name) { public int Size { get; init; } = 3; public string Extra { get; init; } }

[ObjectLiteral(ObjectInitializationMode.Initializer)]
public record Only(string Name) { public int Size { get; init; } = 3; public string Extra { get; init; } }

public class Program
{
    public static void Main()
    {
        var a = new All("n");
        Console.WriteLine(a.Name + "/" + a.Size + "/" + (a.Extra ?? "<null>"));
        var b = new Only("i") { Extra = "e" };
        Console.WriteLine(b.Name + "/" + b.Size + "/" + b.Extra);
    }
}
""";
            await RunTest(code, overrideRoslynCode: code
                .Replace("using Transpose;", "")
                .Replace("[ObjectLiteral(ObjectInitializationMode.DefaultValue)]", "")
                .Replace("[ObjectLiteral(ObjectInitializationMode.Initializer)]", ""));
        }

        [TestMethod]
        public void ObjectLiteralRecordEmitsAPlainJsObjectWithoutCompilerMembers()
        {
            var result = new RoslynTranslator().Translate("""
using System;
using Transpose;

[ObjectLiteral(ObjectInitializationMode.DefaultValue)]
public record Point(int X, int Y) { public string Tag { get; init; } = "t"; }

public class Program
{
    public static void Main() { Console.WriteLine(new Point(1, 2).X); }
}
""");
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;
            Assert.IsTrue(js.Contains("""{X: 1, Y: 2, Tag: "t"}"""),
                "an [ObjectLiteral] record should build a plain JS object from its positional arguments\n" + js);
            // EqualityContract is compiler bookkeeping a record synthesizes; it is not part of the shape
            // of the JavaScript object the literal describes.
            Assert.IsFalse(js.Contains("EqualityContract:"),
                "a record's synthesized EqualityContract must not leak into the literal\n" + js);
        }
    }
}
