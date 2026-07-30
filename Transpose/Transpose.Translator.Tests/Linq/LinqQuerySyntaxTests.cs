using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests.Linq
{
    /// <summary>
    /// LINQ <b>query expression</b> syntax — every clause the language defines, and the combinations that
    /// force C#'s "transparent identifier" lowering: <c>from</c> (including a second and third one),
    /// <c>let</c>, <c>join</c> and <c>join … into</c>, <c>where</c>, <c>orderby</c> (with several keys and
    /// mixed directions), <c>select</c>, <c>group … by</c>, and an <c>into</c> continuation.
    ///
    /// The interesting cases are the ones where more than one range variable is live at once: a clause is
    /// a single-parameter lambda, so the compiler has to carry the variables in a frame object and every
    /// later clause reads them out of it. These tests pin the resulting *behaviour* against native .NET
    /// rather than the emitted shape, so the frame layout is free to change.
    /// </summary>
    [TestClass]
    public class LinqQuerySyntaxTests : TranslatorTestBase
    {
        [TestMethod]
        public async Task SingleFrom_WhereOrderBySelect()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { 5, 3, 8, 1 };
        Console.WriteLine(string.Join(",", from x in xs where x > 2 orderby x select x * 2));
        Console.WriteLine(string.Join(",", from x in xs orderby x descending select x));
        Console.WriteLine(string.Join(",", from x in xs select x));
        Console.WriteLine(string.Join(",", from x in xs where x > 100 select x));
        Console.WriteLine(string.Join(",", from x in xs orderby x % 3, x descending select x));
        Console.WriteLine(string.Join(",", from x in xs orderby x % 3 descending, x select x));
        Console.WriteLine(string.Join(",", from x in xs where x > 2 where x < 8 select x));
        Console.WriteLine(string.Join(",", from x in xs select new { X = x, Sq = x * x } into p select p.X + "^" + p.Sq));
        Console.WriteLine((from x in xs select x).Count());
        Console.WriteLine((from x in xs where x > 3 select x).Sum());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task LetClause()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { 1, 2, 3, 4 };
        Console.WriteLine(string.Join(",", from x in xs let y = x * 10 where y > 20 select x + "/" + y));
        Console.WriteLine(string.Join(",", from x in xs let a = x + 1 let b = a * 2 select x + ":" + a + ":" + b));
        Console.WriteLine(string.Join(",", from x in xs let s = x.ToString() orderby s descending select s));
        var words = new[] { "hello", "hi" };
        Console.WriteLine(string.Join(",", from w in words let n = w.Length where n > 2 select w + n));
        Console.WriteLine(string.Join(",", from x in xs let y = x * 2 group y by x % 2 into g select g.Key + "=" + g.Sum()));
        // A let whose expression is itself a query over the outer range variable.
        Console.WriteLine(string.Join(",", from x in xs let y = xs.Where(z => z > x).Count() select x + "->" + y));
        Console.WriteLine(string.Join(",", from x in xs let y = x let z = y select x + y + z));
        Console.WriteLine(string.Join(",", from x in xs let y = x % 2 == 0 ? "even" : "odd" orderby y, x select y + x));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task MultipleFromClauses()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { 1, 2, 3 };
        var ys = new[] { "a", "b" };
        Console.WriteLine(string.Join(",", from x in xs from y in ys select x + y));
        Console.WriteLine(string.Join(",", from x in xs where x > 1 from y in ys where y == "a" select x + y));
        Console.WriteLine(string.Join(",", from x in xs from y in ys from z in xs where z == x select x + y + z));
        // The second source may depend on the first range variable.
        Console.WriteLine(string.Join(",", from x in xs from y in Enumerable.Range(1, x) select x + "^" + y));
        Console.WriteLine(string.Join(",", from x in xs from y in ys orderby y, x descending select x + y));
        Console.WriteLine(string.Join(",", from x in xs from y in ys let k = x + y select k.ToUpper()));
        Console.WriteLine(string.Join(",", from x in xs from y in ys group x by y into g select g.Key + ":" + g.Sum()));
        Console.WriteLine((from x in xs from y in ys select x).Count());
        Console.WriteLine(string.Join(",", from s in ys from c in s select s + c));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task JoinAndJoinInto()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var outer = new[] { 1, 2, 3 };
        var inner = new[] { "1a", "1b", "3c" };
        Console.WriteLine(string.Join(",", from o in outer
                                          join i in inner on o.ToString() equals i.Substring(0, 1)
                                          select o + "/" + i));
        Console.WriteLine(string.Join(",", from o in outer
                                          join i in inner on o.ToString() equals i.Substring(0, 1) into g
                                          select o + "=" + g.Count()));
        // join-into followed by a from over the group: the "left outer join" shape.
        Console.WriteLine(string.Join(",", from o in outer
                                          join i in inner on o.ToString() equals i.Substring(0, 1) into g
                                          from i2 in g
                                          select o + "-" + i2));
        Console.WriteLine(string.Join(",", from o in outer
                                          join i in inner on o.ToString() equals i.Substring(0, 1)
                                          where i.EndsWith("b")
                                          select o + "!" + i));
        Console.WriteLine(string.Join(",", from o in outer
                                          join i in inner on o.ToString() equals i.Substring(0, 1)
                                          orderby i descending
                                          select i));
        Console.WriteLine(string.Join(",", from o in outer
                                          let t = o * 10
                                          join i in inner on o.ToString() equals i.Substring(0, 1)
                                          select t + ":" + i));
        Console.WriteLine(string.Join(";", from o in outer
                                          join i in inner on o.ToString() equals i.Substring(0, 1) into g
                                          select o + "[" + string.Join("|", g) + "]"));
        Console.WriteLine((from o in outer join i in new string[0] on o.ToString() equals i select o).Count());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task GroupByAndIntoContinuation()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { 1, 2, 3, 4, 5 };
        Console.WriteLine(string.Join(",", from x in xs group x by x % 2 into g orderby g.Key select g.Key + ":" + g.Count()));
        Console.WriteLine(string.Join(",", from g in (from x in xs group x by x % 2) select g.Key + "/" + g.Sum()));
        Console.WriteLine(string.Join(",", from x in xs group x * 10 by x % 2 into g orderby g.Key select g.Key + "=" + string.Join("+", g)));
        Console.WriteLine(string.Join(",", from x in xs where x > 1 group x by x % 3 into g where g.Count() > 1 select g.Key.ToString()));
        Console.WriteLine((from x in xs group x by x % 2).Count());
        var words = new[] { "apple", "avocado", "banana" };
        Console.WriteLine(string.Join(";", from w in words group w by w[0] into g select g.Key + ":" + g.Count()));
        Console.WriteLine(string.Join(";", from w in words
                                          group w by w[0] into g
                                          let longest = g.OrderByDescending(s => s.Length).First()
                                          select g.Key + "->" + longest));
        // An into continuation that itself introduces further range variables.
        Console.WriteLine(string.Join(";", from w in words
                                          select w into s
                                          let n = s.Length
                                          from c in s.Substring(0, 1)
                                          orderby n descending
                                          select c + n));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task ExplicitlyTypedRangeVariable_IsACast()
        {
            var code = """
using System;
using System.Collections;
using System.Linq;

public class Program
{
    public static void Main()
    {
        // `from T x in src` is defined as Cast<T>(src), so a non-generic IEnumerable becomes queryable.
        IEnumerable objs = new object[] { 1, 2, 3 };
        Console.WriteLine(string.Join(",", from int i in objs select i * 2));
        Console.WriteLine((from int i in objs where i > 1 select i).Sum());
        IEnumerable strs = new object[] { "a", "bb" };
        Console.WriteLine(string.Join(",", from string s in strs select s.Length));
        Console.WriteLine(string.Join(",", from int i in objs from string s in strs select i + s));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task QuerySyntax_OverRealObjectGraphs()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    class Order { public int Id; public string Cust; public decimal Total; }

    public static void Main()
    {
        var orders = new List<Order>
        {
            new Order { Id = 1, Cust = "amy", Total = 10m },
            new Order { Id = 2, Cust = "bob", Total = 25m },
            new Order { Id = 3, Cust = "amy", Total = 5m },
        };
        var custs = new[] { "amy", "cal" };

        var q = from o in orders
                join c in custs on o.Cust equals c
                let big = o.Total > 6m
                where big
                orderby o.Total descending, o.Id
                select new { o.Id, o.Cust, o.Total, big };
        foreach (var r in q) Console.WriteLine(r.Id + " " + r.Cust + " " + r.Total + " " + r.big);

        var g = from o in orders
                group o by o.Cust into byCust
                let total = byCust.Sum(x => x.Total)
                where total > 6m
                orderby byCust.Key
                select byCust.Key + "=" + total;
        Console.WriteLine(string.Join(",", g));

        var nested = from o in orders
                     from ch in o.Cust
                     where ch != 'a'
                     select o.Id + ch.ToString();
        Console.WriteLine(string.Join(",", nested));

        // A query inside a clause of another query.
        var correlated = from o in orders
                         let peers = (from p in orders where p.Cust == o.Cust && p.Id != o.Id select p.Id).ToList()
                         orderby o.Id
                         select o.Id + "~[" + string.Join(",", peers) + "]";
        Console.WriteLine(string.Join(";", correlated));

        // Query syntax reading an instance field through `this` is why a clause must be an arrow function.
        Console.WriteLine(new Program().Filtered(orders));
    }

    private readonly string _target = "amy";
    string Filtered(List<Order> orders) =>
        string.Join(",", from o in orders where o.Cust == _target orderby o.Id select o.Id);
}
""";
            await RunTest(code);
        }
    }
}
