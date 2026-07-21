using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Constructor emission and initialization ordering. C#'s rules are subtle and the emitter must
    /// reproduce them exactly:
    ///  - a derived class's instance field initializers run BEFORE the base constructor body, so a
    ///    virtual method called from the base ctor sees the derived fields already set;
    ///  - a `: this(...)` delegation must NOT re-run field initializers (they run in the delegated
    ///    ctor) and must bind to a SIBLING ctor of the same declaring type, not dynamically to the
    ///    most-derived override (which would recurse forever in a subclass);
    ///  - `: base(args)` / primary-constructor `: Base(args)` forward their arguments;
    ///  - records forward positional args to a base record and chain secondary ctors to the primary.
    /// Every test diffs Transpose's JS output against native .NET.
    /// </summary>
    [TestClass]
    public class ConstructorOrderingTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task MultiLevelInheritanceBaseAndThisChainsAsync()
        {
            // Puppy -> Dog -> Animal with `: base(4)`, `: this()`, `: base(n)` mixed. The middle
            // class's `: this()` must call Dog's own ctor, not Puppy's (a dynamic `this.ctor()`
            // recursed into the subclass forever → stack overflow).
            await RunTest(@"
using System;
public abstract class Animal
{
    public string Kind = ""animal"";
    protected int legs;
    public Animal() { Console.WriteLine(""Animal()""); }
    public Animal(int l) { legs = l; Console.WriteLine(""Animal(int) legs="" + legs); }
    public abstract string Speak();
}
public class Dog : Animal
{
    public string Name = ""Rex"";
    private int extra = 99;
    public Dog() : base(4) { Console.WriteLine(""Dog() Name="" + Name + "" extra="" + extra); }
    public Dog(string n) : this() { Name = n; Console.WriteLine(""Dog(string) Name="" + Name); }
    public override string Speak() => ""Woof from "" + Name;
}
public class Puppy : Dog
{
    public int Age = 1;
    public Puppy(string n) : base(n) { Console.WriteLine(""Puppy(string) Age="" + Age); }
    public override string Speak() => base.Speak() + "" (puppy age "" + Age + "")"";
}
public class Program
{
    public static void Main()
    {
        var d = new Dog();          Console.WriteLine(d.Speak());
        var d2 = new Dog(""Fido"");   Console.WriteLine(d2.Speak());
        var p = new Puppy(""Bella""); Console.WriteLine(p.Speak());
        Console.WriteLine(""Kind="" + p.Kind);
    }
}");
        }

        [TestMethod]
        public async Task DerivedFieldInitRunsBeforeBaseCtorBodyAsync()
        {
            // The classic C# gotcha: a virtual method invoked from the base constructor dispatches
            // to the override, which must already see the derived field initializers.
            await RunTest(@"
using System;
public class Base
{
    public Base() { Console.WriteLine(""Base.ctor sees: "" + Describe()); }
    public virtual string Describe() => ""base"";
}
public class Derived : Base
{
    private string tag = ""TAG-SET"";
    public int Count = 10;
    public Derived() { Console.WriteLine(""Derived.ctor tag="" + tag); }
    public override string Describe() => ""derived tag="" + (tag ?? ""null"") + "" count="" + Count;
}
public class Program
{
    public static void Main()
    {
        var d = new Derived();
        Console.WriteLine(d.Describe());
    }
}");
        }

        [TestMethod]
        public async Task ThisChainDoesNotRerunFieldInitializersAsync()
        {
            // A `: this(...)` delegation must not re-run the instance field initializers after the
            // delegated ctor already set the object up (the Random/Guid constant-value bug).
            await RunTest(@"
using System;
public class Box
{
    public int[] Data = new int[3];
    public Box() : this(7) { }
    public Box(int seed) { Data[0] = seed; Data[1] = seed + 1; Data[2] = seed + 2; }
}
public class Program
{
    public static void Main()
    {
        var b = new Box();
        Console.WriteLine(b.Data[0] + "","" + b.Data[1] + "","" + b.Data[2]);   // 7,8,9
    }
}");
        }

        [TestMethod]
        public async Task DeepThisAndBaseChainWithStaticCtorAsync()
        {
            await RunTest(@"
using System;
public class A
{
    public static int Created = 0;
    public string trace;
    public A() : this(""default"") { trace += ""|A()""; }
    public A(string s) { trace = ""A("" + s + "")""; Created++; }
}
public class B : A
{
    public int id = Next();
    static int counter = 100;
    static int Next() => ++counter;
    public B() : base(""from-B"") { trace += ""|B() id="" + id; }
    public B(int x) : this() { trace += ""|B(int)="" + x; }
}
public class Program
{
    public static void Main()
    {
        var b = new B(5);  Console.WriteLine(b.trace);
        Console.WriteLine(""Created="" + A.Created);
        var b2 = new B();  Console.WriteLine(b2.trace);
    }
}");
        }

        [TestMethod]
        public async Task GenericBaseVirtualDispatchAndThisChainAsync()
        {
            await RunTest(@"
using System;
public class Container<T>
{
    protected T value;
    public Container(T v) { value = v; Setup(); }
    protected virtual void Setup() { Console.WriteLine(""Container.Setup "" + value); }
    public T Get() => value;
}
public class Named<T> : Container<T>
{
    public string name = ""unnamed"";
    public Named(T v) : base(v) { Console.WriteLine(""Named ctor name="" + name); }
    public Named(T v, string n) : this(v) { name = n; }
    protected override void Setup() { Console.WriteLine(""Named.Setup value="" + value + "" name="" + (name ?? ""null"")); }
}
public class Program
{
    public static void Main()
    {
        var n = new Named<int>(42, ""answer"");
        Console.WriteLine(n.Get() + "" / "" + n.name);
        var n2 = new Named<string>(""hi"");
        Console.WriteLine(n2.Get() + "" / "" + n2.name);
    }
}");
        }

        [TestMethod]
        public async Task StructConstructorThisChainAsync()
        {
            await RunTest(@"
using System;
public struct Point
{
    public int X, Y;
    public Point(int x, int y) { X = x; Y = y; }
    public Point(int v) : this(v, v) { }
    public override string ToString() => $""({X},{Y})"";
}
public class Program
{
    public static void Main()
    {
        Console.WriteLine(new Point(5));
        Console.WriteLine(new Point(3, 7));
    }
}");
        }

        [TestMethod]
        public async Task PrimaryConstructorWithBaseAndSecondaryAsync()
        {
            await RunTest(@"
using System;
public class Widget(int w, int h)
{
    public int W = w, H = h;
    public Widget(int size) : this(size, size) { Console.WriteLine(""Widget(size)""); }
}
public class Button(string label) : Widget(10, 5)
{
    public string Label = label;
    public Button() : this(""OK"") { Console.WriteLine(""Button()""); }
}
public class Program
{
    public static void Main()
    {
        var w = new Widget(7);
        Console.WriteLine($""{w.W}x{w.H}"");
        var b = new Button();
        Console.WriteLine($""{b.Label} {b.W}x{b.H}"");
    }
}");
        }

        [TestMethod]
        public async Task RecordSecondaryConstructorAndInheritanceAsync()
        {
            // Records: a secondary ctor chains to the positional primary; a derived record forwards
            // its `: Base(...)` args; ToString/Equals/GetHashCode include inherited members.
            await RunTest(@"
using System;
public record Shape(string Color);
public record Circle(string Color, double Radius) : Shape(Color);
public record Person(string Name, int Age)
{
    public Person(string name) : this(name, 0) { }
}
public class Program
{
    public static void Main()
    {
        var a = new Circle(""red"", 2.0);
        var b = new Circle(""red"", 2.0);
        var c = new Circle(""blue"", 2.0);
        Console.WriteLine(a);                              // Circle { Color = red, Radius = 2 }
        Console.WriteLine(a == b);                         // True
        Console.WriteLine(a.Equals(b));                    // True (IEquatable<Circle>)
        Console.WriteLine(a.Equals(c));                    // False
        Console.WriteLine(a.Equals((object)b));            // True (object override)
        Console.WriteLine(a.GetHashCode() == b.GetHashCode()); // True
        var p = new Person(""Alice"");
        Console.WriteLine(p);                              // Person { Name = Alice, Age = 0 }
        var p2 = new Person(""Bob"", 30);
        Console.WriteLine(p2.Equals(new Person(""Bob"", 30)));   // True
    }
}");
        }

        [TestMethod]
        public async Task DefaultCtorWithFieldInitAndInheritanceAsync()
        {
            // No explicit derived ctor: the synthesized default must still run field initializers
            // and chain to the base's parameterless ctor.
            await RunTest(@"
using System;
public class Base
{
    public string b = ""base-field"";
    public Base() { Console.WriteLine(""Base ctor b="" + b); }
}
public class Derived : Base
{
    public int n = 42;
    public string tag = ""derived-field"";
}
public class Program
{
    public static void Main()
    {
        var d = new Derived();
        Console.WriteLine(d.b + "" "" + d.n + "" "" + d.tag);
    }
}");
        }
    }
}
