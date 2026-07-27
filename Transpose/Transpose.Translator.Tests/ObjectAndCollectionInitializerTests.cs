using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// Coverage for C# object and collection initializers, array shapes, and dictionary keys.
    ///
    /// Each test runs the same C# natively and as translated JavaScript and diffs the console output,
    /// so every line below is an assertion that Transpose matches .NET. The areas were previously
    /// covered only by one-line happy paths (`new Person { Name = "Bob" }`, `new List&lt;int&gt; { 1, 2, 3 }`,
    /// an `int[,]` and an `int[][]`), which let these through:
    ///
    ///  - a nested member initializer (`Home = { City = "x" }`, `Tags = { "a" }`, `Scores = { ["k"] = 7 }`)
    ///    emitted the braces as an ARRAY LITERAL, overwriting a getter-only collection, and crashed
    ///    outright ("not supported: ImplicitElementAccess") on the index form;
    ///  - a plain struct had no value equality or hashing, so `d[new Key { A = 1 }]` threw
    ///    KeyNotFoundException for a key that had just been added.
    /// </summary>
    [TestClass]
    public class ObjectAndCollectionInitializerTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task ObjectInitializerVariations()
        {
            await RunTest(""""
using System;
using System.Collections.Generic;

public class Address { public string City { get; set; } public int Zip; }
public class Person
{
    public string Name { get; set; }
    public int Age;
    public Address Home { get; set; } = new Address();
    public List<string> Tags { get; } = new List<string>();
    public Dictionary<string, int> Scores { get; } = new Dictionary<string, int>();
    public Person() { }
    public Person(string name) { Name = name; }
    public override string ToString() => Name + "/" + Age + "/" + (Home == null ? "-" : Home.City + ":" + Home.Zip)
        + "/[" + string.Join(",", Tags) + "]";
}
public struct Point { public int X { get; set; } public int Y; public override string ToString() => "(" + X + "," + Y + ")"; }
public class Counter { public static int Made; public int Id; public Counter() { Id = ++Made; } }
public record Rec(string A, int B);

public class Program
{
    public static void Main()
    {
        // property + new
        var p1 = new Person { Name = "a", Age = 1 };
        Console.WriteLine("1: " + p1);

        // explicit type + new
        Person p2 = new Person { Name = "b", Age = 2 };
        Console.WriteLine("2: " + p2);

        // target-typed new
        Person p3 = new() { Name = "c", Age = 3 };
        Console.WriteLine("3: " + p3);

        // var + ctor, then reassign with new + initializer
        var p4 = new Person("d");
        Console.WriteLine("4a: " + p4);
        p4 = new Person { Name = "d2", Age = 4 };
        Console.WriteLine("4b: " + p4);

        // ctor args + initializer together
        var p5 = new Person("e") { Age = 5 };
        Console.WriteLine("5: " + p5);

        // nested object initializer with new
        var p6 = new Person { Name = "f", Home = new Address { City = "Lis", Zip = 1000 } };
        Console.WriteLine("6: " + p6);

        // nested member initializer WITHOUT new (initializes the existing instance)
        var p7 = new Person { Name = "g", Home = { City = "Por", Zip = 4000 } };
        Console.WriteLine("7: " + p7);

        // collection member initializer without new (Add into a getter-only collection)
        var p8 = new Person { Name = "h", Tags = { "x", "y" } };
        Console.WriteLine("8: " + p8);

        // index member initializer without new
        var p9 = new Person { Name = "i", Scores = { ["k"] = 7 } };
        Console.WriteLine("9: " + p9.Scores["k"]);

        // field-only initializer, and mixing fields and properties
        var a1 = new Address { Zip = 99 };
        Console.WriteLine("10: " + a1.Zip + "/" + (a1.City ?? "null"));

        // struct initializers
        var s1 = new Point { X = 1, Y = 2 };
        Point s2 = new() { X = 3 };
        Console.WriteLine("11: " + s1 + s2 + default(Point));

        // struct copied on assignment, not aliased
        var s3 = s1;
        s3.X = 100;
        Console.WriteLine("12: " + s1 + s3);

        // object initializer inside a collection initializer
        var people = new List<Person> { new Person { Name = "j", Age = 6 }, new() { Name = "k", Age = 7 } };
        Console.WriteLine("13: " + string.Join(" ", people));

        // object initializer inside an array initializer
        var arr = new[] { new Address { City = "A1" }, new Address { City = "A2" } };
        Console.WriteLine("14: " + arr[0].City + arr[1].City);

        // the instance is constructed once, before the members are set
        Counter.Made = 0;
        var c = new Counter { Id = 42 };
        Console.WriteLine("15: " + Counter.Made + "/" + c.Id);

        // initializer expressions evaluate left to right
        var order = new List<string>();
        var p10 = new Person { Name = Tap(order, "N"), Age = TapI(order, 8) };
        Console.WriteLine("16: " + string.Join(",", order) + " " + p10);

        // anonymous type
        var an = new { X = 1, Y = "z" };
        Console.WriteLine("17: " + an.X + an.Y);

        // record: positional ctor + with-expression
        var r1 = new Rec("p", 1);
        var r2 = r1 with { B = 2 };
        Console.WriteLine("18: " + r1 + " " + r2);

        // initializer on a variable typed as a base/interface
        object o = new Person { Name = "m", Age = 9 };
        Console.WriteLine("19: " + o);

        Console.WriteLine("<<DONE>>");
    }

    static string Tap(List<string> log, string v) { log.Add(v); return v; }
    static int TapI(List<string> log, int v) { log.Add(v.ToString()); return v; }
}
"""", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public async Task CollectionInitializerVariations()
        {
            // SortedDictionary, ArrayList and Hashtable are deliberately absent: the Transpose BCL does
            // not define those types at all, which is a BCL gap rather than an initializer one.
            await RunTest(""""
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class Item { public string K; public int V; public override string ToString() => K + "=" + V; }

// A custom collection: collection-initializer support only needs IEnumerable + an accessible Add.
public class Bag : IEnumerable<string>
{
    private readonly List<string> _items = new List<string>();
    public void Add(string s) { _items.Add(s); }
    public void Add(string s, int times) { for (var i = 0; i < times; i++) _items.Add(s); }
    public IEnumerator<string> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public override string ToString() => string.Join(",", _items);
}

// A type with an indexer, initialized with the [key] = value form.
public class Slots
{
    private readonly Dictionary<int, string> _d = new Dictionary<int, string>();
    public string this[int i] { get { return _d.TryGetValue(i, out var v) ? v : "-"; } set { _d[i] = value; } }
    public override string ToString() => string.Join(",", _d.OrderBy(kv => kv.Key).Select(kv => kv.Key + ":" + kv.Value));
}

public class Program
{
    public static void Main()
    {
        Console.WriteLine("1: " + string.Join(",", new List<int> { 1, 2, 3 }));
        Console.WriteLine("2: " + string.Join(",", new List<string> { "a", null, "c" }));
        Console.WriteLine("3: " + string.Join(",", new List<Item> { new Item { K = "x", V = 1 }, new Item { K = "y", V = 2 } }));
        Console.WriteLine("4: " + string.Join(",", new HashSet<int> { 3, 1, 3, 2 }.OrderBy(x => x)));
        Console.WriteLine("5: " + string.Join(",", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A", "a", "b" }.OrderBy(x => x)));
        Console.WriteLine("6: " + string.Join(",", new SortedSet<int> { 5, 1, 4 }));

        var d1 = new Dictionary<string, int> { { "a", 1 }, { "b", 2 } };                 // Add(k, v) form
        var d2 = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };                   // indexer form
        Console.WriteLine("7: " + Dump(d1) + " | " + Dump(d2));

        var sl = new SortedList<int, string> { { 3, "c" }, { 1, "a" } };
        Console.WriteLine("8: " + string.Join(",", sl.Select(kv => kv.Key + "=" + kv.Value)));

        // Nested collections
        var nested = new List<List<int>> { new List<int> { 1, 2 }, new List<int> { 3 } };
        Console.WriteLine("9: " + string.Join(" | ", nested.Select(l => string.Join(",", l))));

        var dictOfList = new Dictionary<string, List<int>> { { "a", new List<int> { 1, 2 } } };
        Console.WriteLine("10: " + string.Join(",", dictOfList["a"]));

        // Custom collection: single-arg and multi-arg Add overloads
        Console.WriteLine("11: " + new Bag { "p", { "q", 3 }, "r" });

        // Custom indexer via the [key] = value initializer form
        Console.WriteLine("12: " + new Slots { [2] = "two", [1] = "one" });

        // Stack/Queue take no collection initializer (no Add) — built from a sequence instead
        Console.WriteLine("13: " + string.Join(",", new Stack<int>(new[] { 1, 2, 3 })));
        Console.WriteLine("14: " + string.Join(",", new Queue<int>(new[] { 1, 2, 3 })));

        // Collection expressions (C# 12) over the same shapes
        List<int> ce1 = [1, 2, 3];
        int[] ce2 = [4, 5];
        List<int> ce3 = [..ce1, ..ce2];
        Console.WriteLine("15: " + string.Join(",", ce3));

        // An initializer over an interface-typed target
        IList<int> il = new List<int> { 9, 8 };
        Console.WriteLine("16: " + string.Join(",", il));

        // Elements are evaluated left to right, once each
        var log = new List<string>();
        var seq = new List<int> { Tap(log, 1), Tap(log, 2), Tap(log, 3) };
        Console.WriteLine("17: " + string.Join(",", log) + " " + string.Join(",", seq));

        // Empty initializers
        Console.WriteLine("18: " + new List<int> { }.Count + "/" + new Dictionary<int, int> { }.Count + "/" + new Bag { });

        Console.WriteLine("<<DONE>>");
    }

    static int Tap(List<string> log, int v) { log.Add("t" + v); return v; }
    static string Dump(Dictionary<string, int> d) => string.Join(",", d.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value));
}
"""", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public async Task SingleMultiDimensionalAndJaggedArrays()
        {
            await RunTest(""""
using System;
using System.Collections.Generic;
using System.Linq;

public enum Colour { Red, Green }
public struct Vec { public int X; public override string ToString() => "V" + X; }
public class Node { public int Id; public override string ToString() => "N" + Id; }

public class Program
{
    public static void Main()
    {
        // ---- single-dimensional -------------------------------------------------
        int[] a1 = new int[3];
        int[] a2 = new int[] { 1, 2, 3 };
        int[] a3 = { 4, 5 };
        int[] a4 = new int[2] { 6, 7 };
        var a5 = new[] { 8, 9 };
        int[] a6 = [10, 11];
        Console.WriteLine("1: " + J(a1) + " " + J(a2) + " " + J(a3) + " " + J(a4) + " " + J(a5) + " " + J(a6));
        Console.WriteLine("2: " + a2.Length + "/" + a2.Rank + "/" + a2.GetLength(0) + "/" + a2.GetLowerBound(0) + "/" + a2.GetUpperBound(0));

        // element types: default values and reference/struct/enum/string/nullable
        Console.WriteLine("3: " + J(new string[2]) + " " + J(new bool[2]) + " " + J(new double[2])
            + " " + J(new Colour[2]) + " " + J(new Vec[2]) + " " + J(new Node[2]) + " " + J(new int?[2]));
        Console.WriteLine("4: " + J(new[] { Colour.Green, Colour.Red }) + " " + J(new[] { new Vec { X = 1 }, new Vec { X = 2 } })
            + " " + J(new[] { new Node { Id = 1 } }) + " " + J(new int?[] { 1, null }));

        // struct elements are values, not shared references
        var vs = new Vec[2];
        vs[0].X = 5;
        Console.WriteLine("5: " + J(vs));

        // ---- multi-dimensional --------------------------------------------------
        int[,] m1 = new int[2, 3];
        int[,] m2 = new int[,] { { 1, 2 }, { 3, 4 } };
        int[,] m3 = { { 5, 6 }, { 7, 8 } };
        int[,] m4 = new int[2, 2] { { 9, 10 }, { 11, 12 } };
        Console.WriteLine("6: " + M2(m1) + " " + M2(m2) + " " + M2(m3) + " " + M2(m4));
        Console.WriteLine("7: " + m2.Length + "/" + m2.Rank + "/" + m2.GetLength(0) + "/" + m2.GetLength(1)
            + "/" + m2.GetUpperBound(0) + "/" + m2.GetUpperBound(1));
        m1[1, 2] = 77;
        Console.WriteLine("8: " + m1[1, 2] + "/" + m1[0, 0]);

        // three-dimensional
        int[,,] m5 = new int[2, 2, 2];
        m5[1, 1, 1] = 9;
        int[,,] m6 = new int[,,] { { { 1, 2 }, { 3, 4 } }, { { 5, 6 }, { 7, 8 } } };
        Console.WriteLine("9: " + m5[1, 1, 1] + "/" + m5.Rank + "/" + m6[1, 0, 1] + "/" + m6.Length);

        // non-int element types in a rectangular array
        string[,] ms = new string[,] { { "a", "b" }, { "c", "d" } };
        Colour[,] mc = new Colour[2, 2];
        Console.WriteLine("10: " + ms[1, 0] + "/" + mc[0, 1]);

        // foreach walks a rectangular array in row-major order
        var flat = new List<int>();
        foreach (var v in m2) flat.Add(v);
        Console.WriteLine("11: " + string.Join(",", flat));

        // ---- jagged -------------------------------------------------------------
        int[][] j1 = new int[2][];
        j1[0] = new int[] { 1, 2 };
        j1[1] = new int[] { 3 };
        int[][] j2 = new int[][] { new int[] { 1, 2 }, new int[] { 3, 4, 5 } };
        int[][] j3 = { new[] { 6 }, new[] { 7, 8 } };
        int[][] j4 = [[9], [10, 11]];
        Console.WriteLine("12: " + Jag(j1) + " " + Jag(j2) + " " + Jag(j3) + " " + Jag(j4));
        Console.WriteLine("13: " + j2.Length + "/" + j2[1].Length + "/" + j2[1][2] + "/" + j2.Rank);

        // a jagged array's rows start null
        int[][] j5 = new int[2][];
        Console.WriteLine("14: " + (j5[0] == null) + "/" + j5.Length);

        // three levels of jaggedness
        int[][][] j6 = new int[][][] { new int[][] { new int[] { 1, 2 } }, new int[][] { new int[] { 3 } } };
        Console.WriteLine("15: " + j6[0][0][1] + "/" + j6.Length + "/" + j6[1][0][0]);

        // ---- mixed: jagged of rectangular, and rectangular of jagged ------------
        int[][,] mixed1 = new int[2][,];
        mixed1[0] = new int[,] { { 1, 2 }, { 3, 4 } };
        Console.WriteLine("16: " + mixed1[0][1, 0] + "/" + (mixed1[1] == null));

        int[,][] mixed2 = new int[2, 2][];
        mixed2[0, 1] = new[] { 7, 8 };
        Console.WriteLine("17: " + mixed2[0, 1][1] + "/" + (mixed2[0, 0] == null));

        // ---- arrays of objects built with initializers --------------------------
        var nodes = new[] { new Node { Id = 1 }, new Node { Id = 2 } };
        var lists = new List<int>[] { new List<int> { 1 }, new List<int> { 2, 3 } };
        Console.WriteLine("18: " + J(nodes) + " " + string.Join("|", lists.Select(l => string.Join(",", l))));

        // ---- array helpers over each shape --------------------------------------
        var copy = (int[])a2.Clone();
        copy[0] = 99;
        Console.WriteLine("19: " + J(a2) + " " + J(copy));
        var sorted = new[] { 3, 1, 2 };
        Array.Sort(sorted);
        Array.Reverse(sorted);
        Console.WriteLine("20: " + J(sorted) + "/" + Array.IndexOf(sorted, 2));
        var resized = new[] { 1, 2 };
        Array.Resize(ref resized, 4);
        Console.WriteLine("21: " + J(resized));

        Console.WriteLine("<<DONE>>");
    }

    static string J<T>(T[] a) => "[" + string.Join(",", a.Select(x => x == null ? "null" : x.ToString())) + "]";
    static string M2(int[,] m)
    {
        var sb = new List<string>();
        for (var i = 0; i < m.GetLength(0); i++)
            for (var k = 0; k < m.GetLength(1); k++)
                sb.Add(m[i, k].ToString());
        return "[" + string.Join(",", sb) + "]";
    }
    static string Jag(int[][] j) => "[" + string.Join("|", j.Select(r => r == null ? "null" : string.Join(",", r))) + "]";
}
"""", waitForOutput: "<<DONE>>");
        }

        [TestMethod]
        public async Task DictionaryKeyTypesAndComparers()
        {
            await RunTest(""""
using System;
using System.Collections.Generic;
using System.Linq;

public enum Level { Low, High }
public struct Key2 { public int A; public string B; public override string ToString() => A + B; }
public class RefKey
{
    public string Name;
    public override bool Equals(object o) => o is RefKey r && r.Name == Name;
    public override int GetHashCode() => Name == null ? 0 : Name.GetHashCode();
    public override string ToString() => "R:" + Name;
}
public class LenComparer : IEqualityComparer<string>
{
    public bool Equals(string a, string b) => (a ?? "").Length == (b ?? "").Length;
    public int GetHashCode(string s) => (s ?? "").Length;
}
public class DescComparer : IComparer<int>
{
    public int Compare(int a, int b) => b.CompareTo(a);
}

public class Program
{
    public static void Main()
    {
        // ---- key types ----------------------------------------------------------
        var kString = new Dictionary<string, int> { { "a", 1 }, { "b", 2 } };
        var kInt = new Dictionary<int, string> { { 2, "two" }, { 1, "one" } };
        var kChar = new Dictionary<char, int> { { 'x', 1 }, { 'y', 2 } };
        var kLong = new Dictionary<long, int> { { 10L, 1 } };
        var kBool = new Dictionary<bool, string> { { true, "t" }, { false, "f" } };
        var kEnum = new Dictionary<Level, string> { { Level.High, "h" }, { Level.Low, "l" } };
        var kDouble = new Dictionary<double, string> { { 1.5, "x" } };
        var kGuid = new Dictionary<Guid, string> { { new Guid("00000000-0000-0000-0000-000000000001"), "g" } };
        var kStruct = new Dictionary<Key2, string> { { new Key2 { A = 1, B = "b" }, "s" } };
        var kRef = new Dictionary<RefKey, string> { { new RefKey { Name = "n" }, "r" } };
        var kTuple = new Dictionary<(int, string), string> { { (1, "a"), "t" } };
        var kNullable = new Dictionary<int?, string> { { 1, "n1" } };

        Console.WriteLine("1: " + Show(kString));
        Console.WriteLine("2: " + string.Join(",", kInt.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value)) + "/" + kInt[2]);
        Console.WriteLine("3: " + kChar['x'] + kChar['y'] + "/" + kChar.Count);
        Console.WriteLine("4: " + kLong[10L] + "/" + kBool[true] + kBool[false]);
        Console.WriteLine("5: " + kEnum[Level.High] + kEnum[Level.Low] + "/" + kDouble[1.5]);
        Console.WriteLine("6: " + kGuid[new Guid("00000000-0000-0000-0000-000000000001")]);
        Console.WriteLine("7: " + kStruct[new Key2 { A = 1, B = "b" }]);       // struct key, by value
        Console.WriteLine("8: " + kRef[new RefKey { Name = "n" }]);            // class key, Equals/GetHashCode
        Console.WriteLine("9: " + kTuple[(1, "a")] + "/" + kNullable[1]);

        // lookup misses and the standard probes
        Console.WriteLine("10: " + kString.ContainsKey("a") + kString.ContainsKey("zz")
            + "/" + kString.TryGetValue("b", out var got) + got
            + "/" + kString.TryGetValue("zz", out var miss) + miss);
        try { var _ = kString["zz"]; Console.WriteLine("11: no throw"); }
        catch (KeyNotFoundException) { Console.WriteLine("11: KeyNotFoundException"); }
        try { kString.Add("a", 3); Console.WriteLine("12: no throw"); }
        catch (ArgumentException) { Console.WriteLine("12: ArgumentException"); }

        // ---- custom comparers in the constructor --------------------------------
        var ci = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "Key", 1 } };
        Console.WriteLine("13: " + ci["KEY"] + ci["key"] + "/" + ci.ContainsKey("kEy") + "/" + ci.Count);

        var ord = new Dictionary<string, int>(StringComparer.Ordinal) { { "Key", 1 } };
        Console.WriteLine("14: " + ord.ContainsKey("Key") + ord.ContainsKey("KEY"));

        var byLen = new Dictionary<string, int>(new LenComparer()) { { "aa", 1 } };
        Console.WriteLine("15: " + byLen["bb"] + "/" + byLen.ContainsKey("c") + "/" + byLen.Count);

        // capacity + comparer overload, and the copy-from-dictionary overload
        var cap = new Dictionary<string, int>(4, StringComparer.OrdinalIgnoreCase) { { "A", 1 } };
        Console.WriteLine("16: " + cap["a"]);
        var copied = new Dictionary<string, int>(ci, StringComparer.OrdinalIgnoreCase);
        Console.WriteLine("17: " + copied["KEY"] + "/" + copied.Count);

        // a comparer-carrying dictionary keeps its comparer as it grows
        var grow = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        grow["One"] = 1;
        grow["ONE"] = 2;
        grow["two"] = 3;
        Console.WriteLine("18: " + grow.Count + "/" + grow["one"]);

        // HashSet with a comparer
        var hs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A", "a", "B" };
        Console.WriteLine("19: " + hs.Count + "/" + hs.Contains("b"));

        // SortedSet / SortedList with an ordering comparer
        var ss = new SortedSet<int>(new DescComparer()) { 1, 3, 2 };
        Console.WriteLine("20: " + string.Join(",", ss));
        var slc = new SortedList<int, string>(new DescComparer()) { { 1, "a" }, { 3, "c" } };
        Console.WriteLine("21: " + string.Join(",", slc.Select(kv => kv.Key + "=" + kv.Value)));

        // ---- enumeration, keys/values, removal ----------------------------------
        Console.WriteLine("22: " + string.Join(",", kString.Keys.OrderBy(k => k))
            + "/" + string.Join(",", kString.Values.OrderBy(v => v)));
        kString.Remove("a");
        Console.WriteLine("23: " + Show(kString) + "/" + kString.Count);
        kString.Clear();
        Console.WriteLine("24: " + kString.Count);

        // ---- dictionaries built by LINQ -----------------------------------------
        var fromLinq = new[] { 1, 2, 3 }.ToDictionary(x => "k" + x, x => x * 10);
        Console.WriteLine("25: " + Show(fromLinq));
        var fromLinqCi = new[] { "A", "b" }.ToDictionary(x => x, x => x.ToLower(), StringComparer.OrdinalIgnoreCase);
        Console.WriteLine("26: " + fromLinqCi["a"] + fromLinqCi["B"]);

        Console.WriteLine("<<DONE>>");
    }

    static string Show(Dictionary<string, int> d)
        => string.Join(",", d.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
}
"""", waitForOutput: "<<DONE>>");
        }
    }
}
