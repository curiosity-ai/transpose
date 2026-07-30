using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests.Linq
{
    /// <summary>
    /// LINQ over <b>every kind of element type</b> and the combinations of them: the primitives
    /// (int/long/float/double/decimal/string/char/bool), an enum, a class, a struct, a record class, a
    /// record struct, a nullable value type (including a nullable struct), a ValueTuple, a
    /// <c>KeyValuePair</c>, an anonymous type, <c>dynamic</c>, an interface, a type parameter inside a
    /// generic method — and, where the element type has structure, a nested combination of them.
    ///
    /// The element type is what decides how the operators that need <b>equality</b>
    /// (<c>Distinct</c>/<c>Union</c>/<c>Intersect</c>/<c>Except</c>/<c>Contains</c>/<c>GroupBy</c>/
    /// <c>ToLookup</c>/<c>ToDictionary</c>/<c>SequenceEqual</c>) and <b>ordering</b>
    /// (<c>OrderBy</c>/<c>Min</c>/<c>Max</c>/<c>MinBy</c>/<c>MaxBy</c>) behave, so each type is put
    /// through both families rather than just through <c>Select</c>/<c>Where</c>.
    ///
    /// <para><b>Deliberately not covered — known limitations, not regressions:</b></para>
    /// <list type="bullet">
    /// <item><description><c>OfType&lt;double&gt;()</c> / <c>OfType&lt;float&gt;()</c> over a sequence of
    /// boxed integers. Every JS number is a double, so a boxed <c>int</c> is indistinguishable from a
    /// boxed <c>double</c> and <c>(object)1 is double</c> is true — the same limitation as elsewhere in
    /// the runtime, not something LINQ can fix. <c>OfType</c> over reference types, structs and
    /// <c>long</c> (a real object at runtime) IS covered.</description></item>
    /// <item><description>A <c>dynamic</c> receiver whose operator has numeric overloads
    /// (<c>Enumerable.Sum(dyn)</c>). There is no runtime overload resolver, so the call has no single
    /// binding; a dynamic receiver on an unambiguous operator (<c>Enumerable.Count(dyn)</c>) is
    /// covered.</description></item>
    /// </list>
    /// </summary>
    [TestClass]
    public class LinqElementTypeTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task ClassElements()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Person
{
    public string Name;
    public int Age;
    public override string ToString() => Name + "/" + Age;
}

public class Program
{
    public static void Main()
    {
        var ps = new List<Person>
        {
            new Person { Name = "bob", Age = 30 },
            new Person { Name = "amy", Age = 25 },
            new Person { Name = "cal", Age = 30 },
        };
        Console.WriteLine(string.Join(",", ps.OrderBy(p => p.Age).ThenBy(p => p.Name)));
        Console.WriteLine(ps.Sum(p => p.Age) + "," + ps.Max(p => p.Age) + "," + ps.Average(p => p.Age));
        Console.WriteLine(ps.MinBy(p => p.Age) + "," + ps.MaxBy(p => p.Name));
        Console.WriteLine(string.Join(";", ps.GroupBy(p => p.Age).OrderBy(g => g.Key).Select(g => g.Key + ":" + g.Count())));
        Console.WriteLine(ps.First(p => p.Age == 25));
        Console.WriteLine(string.Join(",", ps.Select(p => p.Name).Distinct()));
        Console.WriteLine(ps.ToDictionary(p => p.Name)["amy"]);
        Console.WriteLine(string.Join(",", ps.Where(p => p.Age > 26).Select(p => p.Name)));
        // A class has reference equality, so Distinct keeps all three and Contains needs the same instance.
        Console.WriteLine(ps.Contains(ps[0]) + "," + ps.Contains(new Person { Name = "bob", Age = 30 }));
        Console.WriteLine(ps.Distinct().Count());
        Console.WriteLine(ps.ToLookup(p => p.Age).Count);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task StructElements()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public struct Pt
{
    public int X;
    public int Y;
    public override string ToString() => "(" + X + "," + Y + ")";
}

public class Program
{
    public static void Main()
    {
        var pts = new List<Pt>
        {
            new Pt { X = 2, Y = 1 },
            new Pt { X = 1, Y = 5 },
            new Pt { X = 2, Y = 1 },
        };
        Console.WriteLine(string.Join(",", pts.OrderBy(p => p.X).ThenBy(p => p.Y)));
        Console.WriteLine(pts.Sum(p => p.X) + "," + pts.Count(p => p.X == 2));
        // A struct has VALUE equality, so the duplicate collapses and Contains matches by value.
        Console.WriteLine(pts.Distinct().Count());
        Console.WriteLine(pts.Contains(new Pt { X = 1, Y = 5 }));
        Console.WriteLine(string.Join(";", pts.GroupBy(p => p.X).OrderBy(g => g.Key).Select(g => g.Key + ":" + g.Count())));
        Console.WriteLine(pts.MaxBy(p => p.Y) + "," + pts.MinBy(p => p.Y));
        var arr = pts.ToArray();
        Console.WriteLine(arr.Length + " " + arr[0]);
        Console.WriteLine(pts.First() + "," + pts.ElementAt(1) + "," + pts.Last());
        Console.WriteLine(string.Join(",", pts.Select(p => p.X + p.Y)));
        Console.WriteLine(pts.SequenceEqual(new List<Pt> { new Pt { X = 2, Y = 1 }, new Pt { X = 1, Y = 5 }, new Pt { X = 2, Y = 1 } }));
        Console.WriteLine(pts.Except(new[] { new Pt { X = 1, Y = 5 } }).Count());
        Console.WriteLine(pts.ToLookup(p => p).Count);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task RecordClassElements()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public record Rec(string Name, int Age);

public class Program
{
    public static void Main()
    {
        var rs = new List<Rec> { new Rec("bob", 30), new Rec("amy", 25), new Rec("bob", 30) };
        Console.WriteLine(string.Join(",", rs.OrderBy(r => r.Name).ThenBy(r => r.Age).Select(r => r.Name + r.Age)));
        // A record has synthesized value equality, so Distinct collapses the duplicate.
        Console.WriteLine(rs.Distinct().Count());
        Console.WriteLine(rs.Contains(new Rec("amy", 25)));
        Console.WriteLine(rs.Sum(r => r.Age) + "," + rs.Average(r => r.Age));
        Console.WriteLine(string.Join(";", rs.GroupBy(r => r.Name).OrderBy(g => g.Key).Select(g => g.Key + "=" + g.Count())));
        Console.WriteLine(rs.MinBy(r => r.Age));
        Console.WriteLine(string.Join(",", rs.Select(r => r with { Age = r.Age + 1 }).Select(r => r.Age)));
        Console.WriteLine(rs.ToLookup(r => r.Name)["bob"].Count());
        Console.WriteLine(string.Join(",", rs.Union(new[] { new Rec("cal", 1) }).Select(r => r.Name)));
        Console.WriteLine(string.Join(",", rs.Except(new[] { new Rec("bob", 30) }).Select(r => r.Name)));
        Console.WriteLine(rs.GroupBy(r => r).Count());
        Console.WriteLine(rs.Distinct().ToDictionary(r => r.Name + r.Age).Count);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task RecordStructElements()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public readonly record struct RS(int A, string B);

public class Program
{
    public static void Main()
    {
        var rs = new List<RS> { new RS(2, "x"), new RS(1, "y"), new RS(2, "x") };
        Console.WriteLine(string.Join(",", rs.OrderBy(r => r.A).ThenBy(r => r.B).Select(r => r.A + r.B)));
        Console.WriteLine(rs.Distinct().Count());
        Console.WriteLine(rs.Contains(new RS(1, "y")));
        Console.WriteLine(rs.Sum(r => r.A));
        Console.WriteLine(string.Join(";", rs.GroupBy(r => r.A).OrderBy(g => g.Key).Select(g => g.Key + "=" + g.Count())));
        Console.WriteLine(rs.MaxBy(r => r.A) + "," + rs.MinBy(r => r.A));
        Console.WriteLine(string.Join(",", rs.Select(r => r with { A = r.A * 10 }).Select(r => r.A)));
        Console.WriteLine(rs.SequenceEqual(new List<RS> { new RS(2, "x"), new RS(1, "y"), new RS(2, "x") }));
        Console.WriteLine(rs.ToLookup(r => r).Count);
        Console.WriteLine(rs.Intersect(new[] { new RS(1, "y") }).Count());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task EnumElements()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public enum Colour { Red = 1, Green = 2, Blue = 3 }

public class Program
{
    public static void Main()
    {
        var cs = new List<Colour> { Colour.Blue, Colour.Red, Colour.Green, Colour.Red };
        Console.WriteLine(string.Join(",", cs.OrderBy(c => c)));
        Console.WriteLine(string.Join(",", cs.OrderByDescending(c => c)));
        Console.WriteLine(string.Join(",", cs.Distinct()));
        Console.WriteLine(cs.Max() + "," + cs.Min() + "," + cs.MaxBy(c => (int)c));
        Console.WriteLine(cs.Count(c => c == Colour.Red) + "," + cs.Sum(c => (int)c));
        Console.WriteLine(string.Join(";", cs.GroupBy(c => c).OrderBy(g => g.Key).Select(g => g.Key + "=" + g.Count())));
        Console.WriteLine(cs.Contains(Colour.Green));
        Console.WriteLine(string.Join(",", cs.Select(c => c.ToString())));
        Console.WriteLine(cs.ToLookup(c => c).Count);
        Console.WriteLine(cs.Distinct().ToDictionary(c => c, c => (int)c).Count);
        Console.WriteLine(string.Join(",", cs.Except(new[] { Colour.Red })));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task NullableElements_IncludingANullableStruct()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public struct Small
{
    public int X;
    public override string ToString() => "S" + X;
}

public class Program
{
    public static void Main()
    {
        var xs = new List<Small?> { new Small { X = 1 }, null, new Small { X = 2 } };
        Console.WriteLine(xs.Count(x => x.HasValue) + "," + xs.Count(x => x == null));
        Console.WriteLine(string.Join(",", xs.Where(x => x.HasValue).Select(x => x.Value.X)));
        Console.WriteLine(string.Join(",", xs.OrderBy(x => x.HasValue ? x.Value.X : -1).Select(x => x == null ? "null" : x.ToString())));
        Console.WriteLine(xs.Distinct().Count());
        Console.WriteLine(xs.First() != null);

        var ns = new List<int?> { 3, null, 1 };
        // A null key sorts FIRST in .NET's default nullable comparison.
        Console.WriteLine(string.Join(",", ns.OrderBy(x => x).Select(x => x == null ? "null" : x.ToString())));
        Console.WriteLine(string.Join(",", ns.OrderByDescending(x => x).Select(x => x == null ? "null" : x.ToString())));
        Console.WriteLine(ns.Distinct().Count() + "," + ns.Contains(null));
        Console.WriteLine(ns.GroupBy(x => x).Count() + "," + ns.ToLookup(x => x).Count);
        Console.WriteLine(ns.Sum() + "," + ns.Min() + "," + ns.Max());
        Console.WriteLine(string.Join(",", ns.Except(new int?[] { null })));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task TupleAndKeyValuePairElements()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var ts = new List<(int A, string B)> { (2, "x"), (1, "y"), (2, "x") };
        Console.WriteLine(string.Join(",", ts.OrderBy(t => t.A).ThenBy(t => t.B).Select(t => t.A + t.B)));
        // A ValueTuple has value equality.
        Console.WriteLine(ts.Distinct().Count() + "," + ts.Contains((1, "y")));
        Console.WriteLine(ts.Sum(t => t.A) + "," + ts.MaxBy(t => t.A).B);
        Console.WriteLine(string.Join(";", ts.GroupBy(t => t.A).OrderBy(g => g.Key).Select(g => g.Key + "=" + g.Count())));
        Console.WriteLine(ts.GroupBy(t => t).Count() + "," + ts.ToLookup(t => t).Count);
        Console.WriteLine(ts.Except(new[] { (2, "x") }).Count() + "," + ts.Union(new[] { (2, "x") }).Count());
        Console.WriteLine(ts.Distinct().ToDictionary(t => t.B, t => t.A).Count);
        Console.WriteLine(new HashSet<(int, string)>(ts).Count);

        var kvs = new Dictionary<string, int> { { "a", 1 }, { "b", 2 } };
        Console.WriteLine(string.Join(",", kvs.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value)));
        Console.WriteLine(kvs.Sum(k => k.Value) + "," + kvs.Max(k => k.Value));
        Console.WriteLine(string.Join(",", kvs.Select(k => Tuple.Create(k.Key, k.Value)).OrderBy(t => t.Item1).Select(t => t.Item1 + t.Item2)));
        Console.WriteLine(new[] { Tuple.Create(1, "a"), Tuple.Create(1, "a") }.Distinct().Count());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task AnonymousTypeElements_HaveValueEqualityAndAMemberwiseToString()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var rows = new[]
        {
            new { A = 1, B = "x", C = 9 },
            new { A = 1, B = "x", C = 8 },
            new { A = 2, B = "y", C = 7 },
        };
        // An anonymous type is a generated class with VALUE equality, so projecting a subset of the
        // columns and calling Distinct is the canonical way to de-duplicate.
        var pairs = rows.Select(r => new { r.A, r.B }).Distinct().ToList();
        Console.WriteLine(pairs.Count);
        Console.WriteLine(string.Join(";", pairs.OrderBy(p => p.A).Select(p => p.A + p.B)));
        Console.WriteLine(rows.Select(r => new { r.A }).Distinct().Count());
        Console.WriteLine(rows.GroupBy(r => new { r.A, r.B }).Count());
        Console.WriteLine(new HashSet<object>(rows.Select(r => (object)new { r.A, r.B })).Count);
        Console.WriteLine(rows.Select(r => new { r.A, r.B }).Contains(new { A = 2, B = "y" }));
        Console.WriteLine(rows.Select(r => new { r.A, r.B }).ToLookup(p => p).Count);
        Console.WriteLine(rows.OrderByDescending(r => r.C).First().C);
        Console.WriteLine(rows.Sum(r => r.C) + "," + rows.MaxBy(r => r.C).B);

        Console.WriteLine(new { A = 1, B = "x" }.ToString());
        Console.WriteLine(new { A = 1, B = "x" }.Equals(new { A = 1, B = "x" }));
        Console.WriteLine(new { A = 1, B = "x" }.Equals(new { A = 2, B = "x" }));
        Console.WriteLine(new { A = 1, B = "x" }.GetHashCode() == new { A = 1, B = "x" }.GetHashCode());
        Console.WriteLine(new { A = 1, B = (string)null }.ToString());
        Console.WriteLine(new { X = 1.5, Y = true, Z = 'c' }.ToString());
        Console.WriteLine(new { E = DayOfWeek.Monday, D = new DateTime(2020, 1, 2) }.ToString());
        Console.WriteLine(new { Nested = new { Q = 1 } }.ToString());
        Console.WriteLine(new { Nested = new { Q = 1 } }.Equals(new { Nested = new { Q = 1 } }));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task DynamicElements()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new List<dynamic> { 3, 1, 2 };
        Console.WriteLine(xs.Count());
        Console.WriteLine(string.Join(",", xs.Select(x => (int)x * 2)));
        Console.WriteLine(string.Join(",", xs.OrderBy(x => (int)x)));
        Console.WriteLine(xs.Sum(x => (int)x) + "," + xs.Any(x => (int)x > 2) + "," + xs.First());
        // A generic operator invoked on a dynamic receiver: the type argument is never inferred, so the
        // emitted runtime type reference has to fall back to System.Object.
        dynamic seq = new List<int> { 5, 6 };
        Console.WriteLine(Enumerable.Count(seq));
        IEnumerable<dynamic> ds = new dynamic[] { "a", "bb" };
        Console.WriteLine(string.Join(",", ds.Select(d => ((string)d).Length)));
        Console.WriteLine(string.Join(",", ds.Where(d => ((string)d).Length > 1)));
        Console.WriteLine(ds.Count() + "," + ds.Distinct().Count());
        Console.WriteLine(string.Join(",", ds.Reverse()));
        dynamic d1 = 5;
        dynamic d2 = 6;
        Console.WriteLine(new[] { d1, d2 }.Max());
        Console.WriteLine(string.Join(",", new[] { d1, d2 }.OrderByDescending(x => x)));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task StringCharAndBoolElements()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        // A string IS an IEnumerable<char>, so every operator applies to it directly.
        var s = "hello world";
        Console.WriteLine(s.Count(c => c == 'l'));
        Console.WriteLine(new string(s.Where(c => c != 'l').ToArray()));
        Console.WriteLine(new string(s.Distinct().ToArray()));
        Console.WriteLine(string.Join("", s.Reverse()));
        Console.WriteLine(s.OrderBy(c => c).First() + "," + s.Max() + "," + s.Min());
        Console.WriteLine(s.GroupBy(c => c).Count());
        Console.WriteLine(s.Select(c => char.ToUpper(c)).Take(3).Aggregate("", (a, c) => a + c));
        Console.WriteLine(string.Join(",", s.Split(' ').OrderByDescending(w => w)));
        Console.WriteLine(s.ToLookup(c => c).Count);

        var bs = new[] { true, false, true };
        Console.WriteLine(bs.Count(b => b) + "," + bs.Distinct().Count());
        Console.WriteLine(bs.All(b => b) + "," + bs.Any(b => !b) + "," + bs.Contains(false));
        Console.WriteLine(string.Join(",", bs.OrderBy(b => b)));
        Console.WriteLine(string.Join(";", bs.GroupBy(b => b).OrderBy(g => g.Key).Select(g => g.Key + ":" + g.Count())));
        Console.WriteLine(bs.ToLookup(b => b).Count);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task DateTimeTimeSpanGuidAndObjectIdentityElements()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var ds = new[] { new DateTime(2021, 3, 1), new DateTime(2020, 1, 1), new DateTime(2021, 3, 1) };
        Console.WriteLine(string.Join(",", ds.OrderBy(d => d).Select(d => d.ToString("yyyy-MM-dd"))));
        Console.WriteLine(ds.Min().ToString("yyyy-MM-dd") + " " + ds.Max().ToString("yyyy-MM-dd"));
        Console.WriteLine(ds.Distinct().Count() + "," + ds.Contains(new DateTime(2020, 1, 1)));
        Console.WriteLine(ds.GroupBy(d => d.Year).Count() + "," + ds.ToLookup(d => d.Year).Count);
        Console.WriteLine(ds.MaxBy(d => d).ToString("yyyy-MM-dd"));

        var ts = new[] { TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(1) };
        Console.WriteLine(ts.Min() + " " + ts.Max());
        Console.WriteLine(string.Join(",", ts.OrderByDescending(t => t)));
        Console.WriteLine(ts.Sum(t => t.TotalMinutes) + "," + ts.Distinct().Count());

        var g1 = new Guid("11111111-1111-1111-1111-111111111111");
        var g2 = new Guid("22222222-2222-2222-2222-222222222222");
        var gs = new[] { g2, g1, g1 };
        Console.WriteLine(gs.Distinct().Count() + "," + gs.Contains(g1));
        Console.WriteLine(string.Join(",", gs.OrderBy(g => g).Select(g => g.ToString().Substring(0, 1))));
        Console.WriteLine(gs.GroupBy(g => g).Count() + "," + gs.ToLookup(g => g).Count);

        var o1 = new object();
        var o2 = new object();
        var os = new[] { o1, o2, o1 };
        Console.WriteLine(os.Distinct().Count() + "," + os.Contains(o2) + "," + os.Count(o => ReferenceEquals(o, o1)));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task InterfaceElementsAndPolymorphicProjections()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public interface IShape { double Area { get; } string Tag { get; } }
public class Square : IShape { public double S; public double Area => S * S; public string Tag => "sq"; }
public class Circle : IShape { public double R; public double Area => 3 * R * R; public string Tag => "ci"; }

public class Base2 { public int V; public override string ToString() => "b" + V; }
public class Derived2 : Base2 { }

public class Program
{
    public static void Main()
    {
        var shapes = new List<IShape> { new Square { S = 2 }, new Circle { R = 1 }, new Square { S = 3 } };
        Console.WriteLine(shapes.Count() + "," + shapes.OfType<Square>().Count() + "," + shapes.OfType<Circle>().Count());
        Console.WriteLine(shapes.Sum(x => x.Area) + "," + shapes.Select(x => x.Area).Max());
        Console.WriteLine(string.Join(",", shapes.OrderByDescending(x => x.Area).Select(x => x.Tag)));
        Console.WriteLine(string.Join(";", shapes.GroupBy(x => x.Tag).OrderBy(g => g.Key).Select(g => g.Key + "=" + g.Count())));
        Console.WriteLine(shapes.MaxBy(x => x.Area).Tag + "," + shapes.Cast<IShape>().Count());

        // Covariance: an IEnumerable<Derived> used as an IEnumerable<Base>.
        IEnumerable<Derived2> derived = new List<Derived2> { new Derived2 { V = 1 }, new Derived2 { V = 2 } };
        IEnumerable<Base2> bases = derived;
        Console.WriteLine(bases.Sum(x => x.V));
        Console.WriteLine(string.Join(",", bases.OrderByDescending(x => x.V)));
        Console.WriteLine(bases.OfType<Derived2>().Count() + "," + bases.Cast<Derived2>().Count());
        Console.WriteLine(string.Join(",", derived.Concat(new[] { new Derived2 { V = 3 } })));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task TypeParameterElements_InsideAGenericMethod()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    static string Describe<T>(IEnumerable<T> src) where T : IComparable<T>
        => src.Count() + "/" + src.Min() + "/" + src.Max() + "/" + string.Join(",", src.OrderBy(x => x));

    static int SumOf<T>(IEnumerable<T> src, Func<T, int> f) => src.Sum(f);

    static int DistinctCount<T>(IEnumerable<T> src) => src.Distinct().Count();

    static string GroupSizes<T, TKey>(IEnumerable<T> src, Func<T, TKey> key)
        => string.Join(";", src.GroupBy(key).OrderBy(g => g.Key.ToString()).Select(g => g.Key + ":" + g.Count()));

    public static void Main()
    {
        Console.WriteLine(Describe(new[] { 3, 1, 2 }));
        Console.WriteLine(Describe(new[] { "b", "a" }));
        Console.WriteLine(Describe(new[] { 2.5, 1.5 }));
        Console.WriteLine(Describe(new[] { 'c', 'a' }));
        Console.WriteLine(Describe(new[] { 3L, 1L }));
        Console.WriteLine(Describe(new[] { 3.5m, 1.5m }));
        Console.WriteLine(SumOf(new[] { "aa", "b" }, s => s.Length));
        Console.WriteLine(DistinctCount(new[] { 1, 1, 2 }) + "," + DistinctCount(new[] { "a", "a" }));
        Console.WriteLine(GroupSizes(new[] { 1, 2, 3, 4 }, x => x % 2));
        Console.WriteLine(GroupSizes(new[] { "aa", "b", "cc" }, s => s.Length));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task EveryCollectionKindAsASource()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var hs = new HashSet<int> { 3, 1, 2 };
        Console.WriteLine(hs.Sum() + "," + string.Join(",", hs.OrderBy(x => x)));
        var d = new Dictionary<string, int> { { "a", 1 }, { "b", 2 } };
        Console.WriteLine(d.Sum(kv => kv.Value) + "," + string.Join(",", d.Keys.OrderBy(k => k)) + "," + string.Join(",", d.Values.OrderBy(v => v)));
        var q = new Queue<int>();
        q.Enqueue(1);
        q.Enqueue(2);
        Console.WriteLine(q.Sum() + "," + q.Count() + "," + string.Join(",", q));
        var st = new Stack<int>();
        st.Push(1);
        st.Push(2);
        Console.WriteLine(st.Sum() + "," + string.Join(",", st));
        var ll = new LinkedList<int>();
        ll.AddLast(1);
        ll.AddLast(2);
        Console.WriteLine(ll.Sum() + "," + ll.Count());
        var sl = new SortedList<int, string> { { 2, "b" }, { 1, "a" } };
        Console.WriteLine(string.Join(",", sl.Select(kv => kv.Key + kv.Value)));
        var ss = new SortedSet<int> { 3, 1 };
        Console.WriteLine(string.Join(",", ss));
        IList<int> il = new List<int> { 5, 6 };
        Console.WriteLine(il.Sum());
        IReadOnlyCollection<int> roc = new List<int> { 7, 8 };
        Console.WriteLine(roc.Sum());
        IReadOnlyList<int> rol = new List<int> { 1, 2 };
        Console.WriteLine(rol.Sum());
        ICollection<int> col = new List<int> { 9 };
        Console.WriteLine(col.Sum());
        var jagged = new[] { new[] { 1, 2 }, new[] { 3 } };
        Console.WriteLine(jagged.SelectMany(a => a).Sum() + "," + jagged.Sum(a => a.Length));
        var empty = new int[3];
        Console.WriteLine(empty.Sum() + "," + empty.Count() + "," + empty.All(x => x == 0));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task IteratorAndDeferredSources()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    static IEnumerable<int> Gen() { yield return 1; yield return 2; yield return 3; }
    static IEnumerable<string> GenS() { for (int i = 0; i < 3; i++) yield return "s" + i; }

    public static void Main()
    {
        Console.WriteLine(Gen().Sum() + "," + Gen().Count() + "," + Gen().Aggregate(0, (a, b) => a + b));
        Console.WriteLine(string.Join(",", Gen().Where(x => x > 1)));
        Console.WriteLine(string.Join(",", Gen().Reverse()));
        Console.WriteLine(string.Join(",", GenS().Select(s => s.ToUpper())));
        Console.WriteLine(string.Join(",", Gen().Zip(GenS(), (a, b) => a + b)));
        Console.WriteLine(string.Join(";", GenS().GroupBy(s => s.Length).Select(g => g.Key + ":" + g.Count())));
        Console.WriteLine(Gen().ToList().Count + "," + Gen().Concat(Gen()).Count());
        Console.WriteLine(Gen().ToLookup(x => x % 2).Count);

        // A query is re-evaluated on every enumeration, and sees changes made to its source afterwards.
        var list = new List<int> { 1, 2, 3 };
        var query = list.Where(x => x > 1);
        Console.WriteLine(query.Count());
        list.Add(4);
        Console.WriteLine(query.Count());

        // Nothing runs until the sequence is enumerated.
        int calls = 0;
        var lazy = list.Select(x => { calls++; return x; });
        Console.WriteLine("before=" + calls);
        lazy.ToList();
        Console.WriteLine("after=" + calls);

        var chained = list.Where(x => x > 2).Select(x => x * 2);
        Console.WriteLine(string.Join(",", chained));
        Console.WriteLine(string.Join(",", chained));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task NestedAndCombinedElementTypes()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public struct Money { public decimal Amount; public string Ccy; public override string ToString() => Amount + Ccy; }
public record Line(string Sku, int Qty, Money Price);
public enum Status { Open = 1, Closed = 2 }

public class Program
{
    public static void Main()
    {
        var lines = new List<Line>
        {
            new Line("a", 2, new Money { Amount = 1.5m, Ccy = "EUR" }),
            new Line("b", 1, new Money { Amount = 3m, Ccy = "USD" }),
            new Line("a", 2, new Money { Amount = 1.5m, Ccy = "EUR" }),
        };
        // A record whose member is a struct: value equality has to recurse into it.
        Console.WriteLine(lines.Distinct().Count());
        Console.WriteLine(lines.Sum(l => l.Qty * l.Price.Amount));
        Console.WriteLine(string.Join(";", lines.GroupBy(l => l.Price.Ccy).OrderBy(g => g.Key).Select(g => g.Key + "=" + g.Sum(l => l.Qty))));
        Console.WriteLine(lines.MaxBy(l => l.Price.Amount).Sku);
        Console.WriteLine(lines.GroupBy(l => l.Price).Count());

        // A tuple of (enum, nullable, struct), grouped and de-duplicated as a composite key.
        var rows = new[]
        {
            (S: Status.Open, N: (int?)1, M: new Money { Amount = 1m, Ccy = "EUR" }),
            (S: Status.Open, N: (int?)null, M: new Money { Amount = 1m, Ccy = "EUR" }),
            (S: Status.Open, N: (int?)1, M: new Money { Amount = 1m, Ccy = "EUR" }),
        };
        Console.WriteLine(rows.Distinct().Count());
        Console.WriteLine(rows.GroupBy(r => r.S).Single().Count());
        Console.WriteLine(rows.Count(r => r.N == null));
        Console.WriteLine(rows.ToLookup(r => r.M.Ccy).Count);

        // An anonymous type over a record, a struct and a grouping.
        var shaped = lines.GroupBy(l => l.Sku)
                          .Select(g => new { Sku = g.Key, Total = g.Sum(l => l.Qty), First = g.First().Price })
                          .OrderBy(x => x.Sku)
                          .ToList();
        foreach (var x in shaped) Console.WriteLine(x.Sku + " " + x.Total + " " + x.First);
        Console.WriteLine(shaped.Select(x => new { x.Sku, x.Total }).Distinct().Count());

        // A dictionary keyed by a struct, built by LINQ.
        var byMoney = lines.Distinct().ToDictionary(l => l.Price, l => l.Sku);
        Console.WriteLine(byMoney.Count + "," + byMoney[new Money { Amount = 3m, Ccy = "USD" }]);
    }
}
""";
            await RunTest(code);
        }
    }
}
