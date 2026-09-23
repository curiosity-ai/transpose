using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// <c>Span&lt;T&gt;</c> and <c>ReadOnlySpan&lt;T&gt;</c>, each program run against native .NET.
    /// <para>
    /// A span is a real runtime object — an array, an offset and a length (<c>BCL/Transpose.BCL/System/Span.cs</c>)
    /// — and every place C# makes one goes through <c>TransposeR.toSpan</c> (<c>Emitter.Spans.cs</c>). Before
    /// that, C# 14's first-class span conversions emitted nothing: <c>Span&lt;int&gt; s = new int[3]</c> left
    /// the bare array in <c>s</c>, <c>s[0] = 1</c> threw "setItem is not a function", and <c>stackalloc</c>,
    /// <c>"x"u8</c> and a string constant pattern over a span were rejected outright.
    /// </para>
    /// </summary>
    [TestClass]
    public class SpanTests : TranslatorTestBase
    {
        /// <summary>A <c>params ReadOnlySpan&lt;T&gt;</c> argument list, a collection expression and an array all become a span, and writes through a <c>Span&lt;T&gt;</c> parameter land in the array.</summary>
        [TestMethod]
        public async Task ParamsSpanAndArrayToSpanConversions()
        {
            await RunTest("""
using System;

public struct P { public int X; public override string ToString() => "P" + X; }

public class Program
{
    static int Sum(params ReadOnlySpan<int> xs) { int t = 0; foreach (var x in xs) t += x; return t; }
    static void Double(Span<int> s) { for (int i = 0; i < s.Length; i++) s[i] *= 2; }
    public static void Main()
    {
        Console.WriteLine(Sum(1, 2, 3) + " " + Sum() + " " + Sum([4, 5]));
        int[] arr = { 1, 2, 3, 4 };
        Double(arr);
        Double(arr.AsSpan(2));
        Double(arr.AsSpan(0, 1));
        Console.WriteLine(string.Join(",", arr));
        Span<int> s = arr;
        s[1] = 0;
        ReadOnlySpan<int> ro = s;
        Console.WriteLine(arr[1] + " " + ro[1] + " " + ro.Length + " " + new Span<int>(arr, 1, 2).Length);
    }
}
""");
        }

        /// <summary><c>foreach (ref var x in span)</c> and <c>ref int r = ref span[i]</c> alias the array's elements.</summary>
        [TestMethod]
        public async Task ForeachRefAndRefLocalsWriteThroughTheSpan()
        {
            await RunTest("""
using System;

public struct P { public int X; public override string ToString() => "P" + X; }

public class Program
{

    public static void Main()
    {
        int[] arr = { 1, 2, 3, 4 };
        Span<int> s = arr;
        foreach (ref var x in s) x += 1;
        int total = 0;
        foreach (var x in s) total += x;
        Console.WriteLine(string.Join(",", arr) + " " + total);
        ref int first = ref s[0];
        first = 100;
        Console.WriteLine(arr[0]);
        Span<P> ps = new[] { new P { X = 1 }, new P { X = 2 } };
        ps[0].X = 10;
        foreach (ref var p in ps) p.X++;
        Console.WriteLine(ps[0] + " " + ps[1]);
        ReadOnlySpan<int> ro = arr;
        foreach (ref readonly var x in ro) total += x;
        Console.WriteLine(total);
    }
}
""");
        }

        /// <summary><c>stackalloc</c> into a span, sized, initialized and implicitly typed.</summary>
        [TestMethod]
        public async Task StackAllocIntoASpan()
        {
            await RunTest("""
using System;

public struct P { public int X; public override string ToString() => "P" + X; }

public class Program
{

    public static void Main()
    {
        Span<int> buf = stackalloc int[4];
        Console.WriteLine(string.Join(",", buf.ToArray()));
        buf.Fill(7);
        buf[1] = 1;
        Span<int> init = stackalloc int[] { 9, 8, 7 };
        Span<int> implicitly = stackalloc[] { 5, 6 };
        ReadOnlySpan<char> chars = stackalloc char[] { 'h', 'i' };
        var n = 3;
        Span<bool> flags = stackalloc bool[n];
        Console.WriteLine(string.Join(",", buf.ToArray()) + " " + init[2] + " " + implicitly.Length + " " + chars.ToString() + " " + flags[2]);
    }
}
""");
        }

        /// <summary><c>"text"u8</c> is a <c>ReadOnlySpan&lt;byte&gt;</c> over the UTF-8 encoding, computed at compile time.</summary>
        [TestMethod]
        public async Task Utf8StringLiteral()
        {
            await RunTest("""
using System;

public struct P { public int X; public override string ToString() => "P" + X; }

public class Program
{

    public static void Main()
    {
        ReadOnlySpan<byte> u8 = "héllo €"u8;
        Console.WriteLine(u8.Length + " " + u8[1] + " " + u8[2] + " " + string.Join(",", u8.ToArray()));
        Console.WriteLine(""u8.Length + " " + "a\n"u8[1]);
    }
}
""");
        }

        /// <summary>A <c>ReadOnlySpan&lt;char&gt;</c>: trimming and searching, a string constant in <c>is</c>, a switch expression and a switch statement, <c>new string(span)</c> and a range.</summary>
        [TestMethod]
        public async Task CharSpanMethodsPatternsAndSwitch()
        {
            await RunTest("""
using System;

public struct P { public int X; public override string ToString() => "P" + X; }

public class Program
{
    static string Kind(ReadOnlySpan<char> s) => s switch { "yes" => "Y", "no" => "N", _ => "?" };
    static string Kind2(ReadOnlySpan<char> s) { switch (s) { case "a": return "A"; case "b": return "B"; default: return "-"; } }
    public static void Main()
    {
        ReadOnlySpan<char> text = "  hello world  ";
        var trimmed = text.Trim();
        Console.WriteLine("[" + trimmed.ToString() + "] " + trimmed.Length + " " + trimmed.IndexOf('o') + " " + trimmed.LastIndexOf('o')
            + " " + trimmed.StartsWith("hello") + " " + trimmed.EndsWith("world") + " " + trimmed.Contains('w') + " " + trimmed.IndexOf("wor"));
        Console.WriteLine("[" + text.TrimStart().ToString() + "][" + text.TrimEnd().ToString() + "] " + "   ".AsSpan().IsWhiteSpace()
            + " " + trimmed.Equals("HELLO WORLD", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine(Kind("yes") + Kind("no") + Kind("maybe") + Kind2("a") + Kind2("b") + Kind2("c") + ("abc".AsSpan() is "abc") + ("abc".AsSpan() is not "abd"));
        Console.WriteLine(new string(trimmed.Slice(6)) + " " + trimmed[..5].ToString() + " " + trimmed[^5..].ToString() + " " + trimmed[^1]);
    }
}
""");
        }

        /// <summary>Copying, slicing, clearing, <c>==</c> (the same window onto the same array) and the <c>MemoryExtensions</c> searches.</summary>
        [TestMethod]
        public async Task CopySliceClearAndEquality()
        {
            await RunTest("""
using System;

public struct P { public int X; public override string ToString() => "P" + X; }

public class Program
{

    public static void Main()
    {
        int[] arr = { 100, 5, 7, 9 };
        Span<int> s = arr;
        var dest = new int[6];
        s.CopyTo(dest);
        Console.WriteLine(string.Join(",", dest) + " " + s.TryCopyTo(new int[2]) + " " + s.Slice(1, 2).ToString() + " " + s.Slice(1, 2)[1]);
        s.Slice(2).Clear();
        Console.WriteLine(string.Join(",", arr) + " " + Span<int>.Empty.IsEmpty + " " + s.IsEmpty + " " + (s == s) + " " + (s == s.Slice(0)) + " " + (s == arr.AsSpan(1)) + " " + (s != arr.AsSpan(1)));
        Console.WriteLine(arr.AsSpan().SequenceEqual(new[] { 100, 5, 0, 0 }) + " " + arr.AsSpan().IndexOf(5) + " " + ((ReadOnlySpan<int>)arr).Contains(9)
            + " " + arr.AsSpan().LastIndexOf(0) + " " + arr.AsSpan().StartsWith(new[] { 100, 5 }) + " " + arr.AsSpan().EndsWith(new[] { 0 }));
        var copy = s.ToArray();
        copy[0] = 1;
        Console.WriteLine(arr[0] + " " + copy[0]);
    }
}
""");
        }

        /// <summary>A span spread into a collection expression, and the exceptions a bad index, slice or copy throws.</summary>
        [TestMethod]
        public async Task SpreadAndBoundsErrors()
        {
            await RunTest("""
using System;

public struct P { public int X; public override string ToString() => "P" + X; }

public class Program
{

    public static void Main()
    {
        int[] arr = { 1, 2, 3 };
        Span<int> s = arr;
        int[] spread = [.. s, 42];
        Console.WriteLine(string.Join(",", spread));
        try { var bad = s[10]; } catch (IndexOutOfRangeException) { Console.WriteLine("ioor"); }
        try { s[-1] = 0; } catch (IndexOutOfRangeException) { Console.WriteLine("ioor write"); }
        try { s.Slice(5); } catch (ArgumentOutOfRangeException) { Console.WriteLine("aoor"); }
        try { var r = s[1..5]; } catch (ArgumentOutOfRangeException) { Console.WriteLine("aoor range"); }
        try { new Span<int>(arr, 2, 2); } catch (ArgumentOutOfRangeException) { Console.WriteLine("aoor ctor"); }
        try { s.CopyTo(new int[1]); } catch (ArgumentException) { Console.WriteLine("short"); }
    }
}
""");
        }

        /// <summary>C#'s implicit <c>Index</c> support — <c>x[^n]</c> on a type with an <c>int</c> indexer and a <c>Length</c>/<c>Count</c> — on a list, a string, a hand-written indexer and a span, for reads, writes, compound assignments and <c>++</c>.</summary>
        [TestMethod]
        public async Task ImplicitIndexSupportOnEveryIndexer()
        {
            await RunTest("""
using System;
using System.Collections.Generic;
public class Grid { int[] _a = { 1, 2, 3 }; public int Length => _a.Length; public int this[int i] { get => _a[i]; set => _a[i] = value; } }
public struct P { public int X; public override string ToString() => "P" + X; }

public class Program
{

    public static void Main()
    {
        var l = new List<int> { 1, 2, 3 };
        Console.WriteLine(l[^1]);
        l[^1] = 9;
        Console.WriteLine(l[2]);
        l[^2] += 10;
        l[^3]++;
        Console.WriteLine(string.Join(",", l));
        var s = "hello";
        Console.WriteLine(s[^1]);
        Index i = ^2;
        Console.WriteLine(s[i] + " " + l[i] + " " + l[new Index(0)]);
        var g = new Grid();
        g[^1] = 30;
        Console.WriteLine(g[^1] + " " + g[0]);
        Span<int> sp = new[] { 4, 5, 6 };
        Console.WriteLine(sp[^1] + " " + sp[1..][0] + " " + sp[..^1].Length);
        sp[^1] = 60;
        Console.WriteLine(sp[2]);
        int[] arr = { 7, 8 };
        Console.WriteLine(arr[^1]);
    }
}
""");
        }

        /// <summary><c>stackalloc</c> into a pointer is unsafe code, and still refused — with a message
        /// that says how to write it instead.</summary>
        [TestMethod]
        public async Task StackAllocIntoAPointerIsRejected()
        {
            await RunTestExpectingError("""
using System;

public class Program
{
    public static unsafe void Main()
    {
        int* p = stackalloc int[4];
        Console.WriteLine(p[0]);
    }
}
""", "stackalloc");
        }
    }
}