using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Positional patterns over a hand-written <c>Deconstruct</c>, and list patterns over every indexable
    /// receiver, each run against native .NET.
    /// <para>
    /// A positional pattern used to read <c>Item1</c>/<c>Item2</c> off anything that was not a tuple or a
    /// record, so <c>o is Foo(1, "a")</c> never matched a class whose <c>Deconstruct</c> was written by
    /// hand. It now calls the method C# bound — instance or extension, generic or not — through
    /// <c>TransposeR.decon</c>. List patterns were array-shaped: over a string they compared
    /// <c>'h'</c> against a character code, and over a span or a list they read members neither has.
    /// </para>
    /// </summary>
    [TestClass]
    public class DeconstructAndListPatternTests : TranslatorTestBase
    {
        /// <summary>Instance and extension <c>Deconstruct</c> methods in <c>is</c>, a switch expression with
        /// guards and combined patterns, a switch statement case, and a positional pattern nested in a
        /// list pattern.</summary>
        [TestMethod]
        public async Task PositionalPatternsCallAHandWrittenDeconstruct()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public class Foo
{
    public int A; public string B;
    public Foo(int a, string b) { A = a; B = b; }
    public void Deconstruct(out int a, out string b) { a = A; b = B; }
}
public struct Pt { public int X, Y; public void Deconstruct(out int x, out int y) { x = X; y = Y; } }
public class Box<T> { public T Value; public Box(T v) { Value = v; } }
public static class Ext
{
    public static void Deconstruct(this Version v, out int major, out int minor) { major = v.Major; minor = v.Minor; }
    public static void Deconstruct<T>(this Box<T> b, out T value, out bool has) { value = b.Value; has = b.Value != null; }
}
public record Rec(int X, int Y) { }
public class Program
{
    static string Describe(object o) => o switch
    {
        Foo(1, "a") => "foo 1 a",
        Foo(var a, var b) when a > 5 => $"big foo {a} {b}",
        Foo(_, null) => "foo null",
        Foo { A: 2 } and (_, var b2) => "foo2 " + b2,
        Pt(0, 0) => "origin",
        Pt(var x, var y) => $"pt {x},{y}",
        Rec(1, var y) => "rec " + y,
        _ => "other",
    };

    public static void Main()
    {
        foreach (var o in new object[] { new Foo(1, "a"), new Foo(7, "z"), new Foo(3, null), new Foo(2, "q"), new Foo(4, "r"), new Pt(), new Pt { X = 2, Y = 3 }, new Rec(1, 9), null })
            Console.WriteLine(Describe(o));
        var d = new Version(2024, 5);
        Console.WriteLine(d is (2024, 5) ? "may" : "no");
        Console.WriteLine(new Box<string>("v") is ("v", true));
        Console.WriteLine(new Box<string>(null) is (null, false));
        var nested = new Foo(1, "a");
        Console.WriteLine(new object[] { nested } is [Foo(1, _)]);
        switch (new Pt { X = 5, Y = 6 }) { case (5, var yy): Console.WriteLine("case " + yy); break; }

    }
}
""");
        }

        /// <summary>List patterns over an array, a span, a <c>ReadOnlySpan&lt;char&gt;</c>, a string and a
        /// list, with slices captured into a variable.</summary>
        [TestMethod]
        public async Task ListPatternsOverEveryIndexableReceiver()
        {
            await RunTest("""
using System;
using System.Collections.Generic;

public class Program
{
    static string Shape(int[] xs) => xs switch { [] => "empty ", [var one] => "one:" + one + " ", [9, ..] => "nine ", [_, .. var tail] => "tail:" + tail.Length + " " };
    public static void Main()
    {
        int[] arr = { 1, 2, 3, 4 };
        Span<int> sp = arr;
        ReadOnlySpan<char> text = "hello";
        var list = new List<int> { 1, 2, 3 };
        Console.WriteLine((sp is [1, .., 4]) + " " + (sp is [1, 2]) + " " + (sp is [_, .. var mid, _] ? mid.Length + ":" + mid[0] : "-"));
        Console.WriteLine((text is ['h', .., 'o']) + " " + ("abc" is ['a', 'b', 'c']) + " " + ("abc" is [.., var last] ? last.ToString() : "-") + " " + ("abcd" is ['a', .. var rest] ? rest : "-"));
        Console.WriteLine((list is [1, _, 3]) + " " + (list is [.., 3]) + " " + (list is [2, ..]));
        Console.WriteLine(arr is [1, .. var r2] ? string.Join(",", r2) : "-");
        Console.WriteLine((arr is [> 0, .., < 5]) + " " + (arr is []) + " " + (new int[0] is []) + " " + (list is [_, _, _, _]));
        Console.WriteLine(Shape(new[] { 1 }) + Shape(new int[0]) + Shape(new[] { 1, 2, 3 }) + Shape(new[] { 9, 9 }));
        ReadOnlySpan<char> word = "level";
        Console.WriteLine(word is [var a, _, 'v', _, var z] && a == z);
    }
}
""");
        }
        /// <summary>A deconstruction assignment in expression position — an expression-bodied method, a
        /// lambda body, a <c>for</c> incrementor, a value that is used. It emitted a tuple constructor
        /// call as an assignment target, a JavaScript syntax error that stopped the whole bundle from
        /// loading; it now runs in an IIFE.</summary>
        [TestMethod]
        public async Task DeconstructionAssignmentInExpressionPosition()
        {
            await RunTest("""
using System;
using System.Collections.Generic;
public class Pt { public int X, Y; public void Deconstruct(out int x, out int y) { x = X; y = Y; } }
public class Program
{
    static void A(out int x, out int y) { (x, y) = (1, 2); }
    static void B(out int x, out int y) => (x, y) = (3, 4);
    static int f, g;
    static void C() => (f, g) = (5, 6);
    static Action D = () => (f, g) = (g, f);
    int h, k;
    void E(Pt p) => (h, k) = p;
    public static void Main()
    {
        A(out var a, out var b); B(out var c, out var d); C();
        int p, q; (p, q) = (7, 8);
        D();
        Console.WriteLine($"{a}{b}{c}{d}{f}{g}{p}{q}");
        var fib = new List<int>();
        for (int x = 0, y = 1; x < 50; (x, y) = (y, x + y)) fib.Add(x);
        Console.WriteLine(string.Join(",", fib));
        var prog = new Program(); prog.E(new Pt { X = 9, Y = 10 }); Console.WriteLine(prog.h + " " + prog.k);
        var t = (p, q) = (1, 2);
        Console.WriteLine(t.Item1 + t.Item2 + p);
    }
}
""");
        }
    }
}
