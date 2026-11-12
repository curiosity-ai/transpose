using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// <c>[IgnoreGeneric]</c> in every shape it is used in.
    ///
    /// <para><b>On an [External] binding</b> (the LINQ result types bound to linq.js —
    /// <c>IGrouping</c>, <c>Grouping</c>, <c>ILookup</c>, <c>Lookup</c>, <c>IOrderedEnumerable</c>,
    /// <c>OrderedEnumerable</c>, <c>EnumerableInstance</c> — and any binding library's own) it says the
    /// JavaScript type is NOT generic: one plain constructor, or one <c>Transpose.definei</c> interface,
    /// for every instantiation. A reference to it must therefore be its bare name in every position a
    /// type is written — a generic method's threaded type argument, a generic class's argument, an
    /// array's element tag, <c>is</c>/<c>as</c>/cast, reflection metadata. Applying it
    /// (<c>System.Linq.IGrouping$2(System.String, System.Object)</c>) calls it without <c>new</c>, which
    /// for a definei interface throws "this.$initialize is not a function".</para>
    ///
    /// <para><b>On a type compiled from source</b> the define stays generic, so its reflection
    /// metadata has to take the same type parameters (it named an unbound <c>T</c>).</para>
    ///
    /// <para><b>On a generic method</b> it drops the threaded type arguments.</para>
    /// </summary>
    [TestClass]
    public class IgnoreGenericTests : TranslatorTestBase
    {
        /// <summary>Stand-ins so the source-attribute snippets also compile against the real .NET.</summary>
        private const string NativeAttributeStubs = """

namespace Transpose
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class IgnoreGenericAttribute : System.Attribute { }
}
""";

        private const string BclLinqTypesCode = """
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

public class Holder<T>
{
    public T Value;
    public Holder(T v) { Value = v; }
}

public class Program
{
    public IGrouping<string, int>[] GroupArray;
    public List<IGrouping<string, int>> GroupList { get; set; }
    public Dictionary<string, IOrderedEnumerable<int>> Sorted = new Dictionary<string, IOrderedEnumerable<int>>();
    public ILookup<bool, int> Parity;
    public Func<IGrouping<string, int>, string> Fmt = g => g.Key + "=" + string.Join("+", g);

    static T Pass<T>(T x) => x;
    static List<T> One<T>(T x) => new List<T> { x };
    static async Task<ILookup<bool, int>> LookupAsync(int[] xs) { await Task.Delay(1); return xs.ToLookup(x => x % 2 == 0); }
    static async Task<IGrouping<string, int>[]> GroupsAsync(int[] xs) { await Task.Delay(1); return xs.GroupBy(x => x < 3 ? "lo" : "hi").ToArray(); }

    public static async Task Main()
    {
        var xs = new[] { 5, 1, 4, 2, 3 };
        var p = new Program();
        p.GroupArray = xs.GroupBy(x => x % 2 == 0 ? "even" : "odd").ToArray();
        p.GroupList = xs.GroupBy(x => x > 2 ? "big" : "small").ToList();
        p.Sorted["asc"] = xs.OrderBy(x => x);
        p.Sorted["desc"] = xs.OrderByDescending(x => x);
        p.Parity = xs.ToLookup(x => x % 2 == 0);

        Console.WriteLine(string.Join(" ", p.GroupArray.Select(p.Fmt)));
        Console.WriteLine(string.Join(" ", p.GroupList.Select(g => g.Key + ":" + g.Count())));
        Console.WriteLine(string.Join(",", p.Sorted["asc"].ThenBy(x => x)) + " / " + string.Join(",", p.Sorted["desc"]));
        Console.WriteLine(p.Parity[true].Sum() + " " + p.Parity[false].Sum() + " " + p.Parity.Count + " " + p.Parity.Contains(true));

        var dict = xs.GroupBy(x => x % 3).ToDictionary(g => g.Key);
        Console.WriteLine(string.Join(",", dict.OrderBy(kv => kv.Key).Select(kv => kv.Key + ":" + kv.Value.Count())));
        var arr = new IGrouping<int, int>[] { dict[0], dict[1] };
        Console.WriteLine(arr.Length + " " + arr[1].Key);
        List<IGrouping<bool, int>> fromLookup = p.Parity.ToList();
        Console.WriteLine(fromLookup.Count + " " + string.Join(",", p.Parity.Select(g => g.Key + ":" + g.Count())));

        var objs = new List<object> { p.GroupArray[0], "x", p.Sorted["asc"], 3 };
        Console.WriteLine(objs.OfType<IGrouping<string, int>>().Count() + " " + objs.OfType<IOrderedEnumerable<int>>().Count());
        object o = p.GroupArray[0];
        Console.WriteLine((o is IGrouping<string, int>) + " " + ((o as IGrouping<string, int>)?.Key) + " " + ((IGrouping<string, int>)o).Key);
        Console.WriteLine(o switch { IGrouping<string, int> g => "group " + g.Key, _ => "other" });
        object so = p.Sorted["asc"];
        Console.WriteLine((so is IOrderedEnumerable<int>) + " " + (p.Parity is ILookup<bool, int>));

        Console.WriteLine(Pass(p.GroupArray[1]).Key + " " + One(p.Parity).Count + " " + One(p.Sorted["desc"])[0].First());
        var h = new Holder<IGrouping<string, int>>(p.GroupArray[0]);
        var hs = new List<Holder<IOrderedEnumerable<int>>> { new Holder<IOrderedEnumerable<int>>(p.Sorted["asc"]) };
        Console.WriteLine(h.Value.Key + " " + hs[0].Value.Last());

        var lk = await LookupAsync(xs);
        var ga = await GroupsAsync(xs);
        Console.WriteLine(lk[false].Count() + " " + string.Join(",", ga.Select(g => g.Key)));

        var q = from x in xs group x by x % 2 into g orderby g.Key select g;
        Console.WriteLine(string.Join(";", q.Select(g => g.Key + ":" + g.Count())));

        // Reading the metadata of members typed with these types is what threw.
        var members = typeof(Program).GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m is FieldInfo || m is PropertyInfo || (m is MethodInfo mi && (mi.Name == "LookupAsync" || mi.Name == "GroupsAsync")))
            .Select(m => m.Name + ":" + (m is FieldInfo f ? f.FieldType != null : m is PropertyInfo pi ? pi.PropertyType != null : ((MethodInfo)m).ReturnType != null))
            .Where(s => !s.StartsWith("<"))
            .OrderBy(s => s, StringComparer.Ordinal);
        Console.WriteLine(string.Join(",", members));
    }
}
""";

        /// <summary>The LINQ binding types as fields, properties, async results, generic arguments of
        /// user and BCL generics, array elements, type tests, query results — and through reflection.</summary>
        [TestMethod]
        public async Task BclLinqTypesInEveryTypePositionRunLikeNative() => await RunTest(BclLinqTypesCode);

        /// <summary>No position may apply one of the linq.js binding types to type arguments.</summary>
        [TestMethod]
        public void BclLinqTypesAreNeverApplied()
        {
            var result = new RoslynTranslator().Translate(BclLinqTypesCode);
            Assert.IsTrue(result.Success, "translation should succeed");
            var applied = Regex.Match(result.Javascript!,
                @"\b(IGrouping\$2|Grouping\$2|ILookup\$2|Lookup\$2|IOrderedEnumerable\$1|OrderedEnumerable\$1|EnumerableInstance\$1)\(");
            Assert.IsFalse(applied.Success,
                $"an [IgnoreGeneric] binding must be referenced by its bare name, found `{applied.Value}`\n" + result.Javascript);
            // Spot-check the bare forms in the positions that used to be applications.
            StringAssert.Contains(result.Javascript, "System.Linq.IGrouping$2");          // is/as/cast, metadata
            StringAssert.Contains(result.Javascript, "Program.Pass(System.Linq.IGrouping$2,"); // threaded type argument
            StringAssert.Contains(result.Javascript, "List$1(System.Linq.IGrouping$2)");  // generic class argument
        }

        /// <summary>
        /// <c>GroupBy</c> yields <c>IGrouping</c> and <c>ILookup</c> enumerates <c>IGrouping</c>, as in
        /// .NET. They were declared over the concrete <c>Grouping</c>, so code that is valid C# —
        /// a <c>List&lt;IGrouping&lt;…&gt;&gt;</c> from <c>ToList()</c>, a conditional mixing the two —
        /// failed to compile (List&lt;T&gt; is invariant; only arrays and IEnumerable hid it).
        /// </summary>
        [TestMethod]
        public async Task GroupByAndLookupAreTypedAsIGrouping()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;

public class Program
{
    static List<IGrouping<char, string>> ByInitial(IEnumerable<string> words) => words.GroupBy(w => w[0]).ToList();

    public static void Main()
    {
        var words = new[] { "apple", "avocado", "banana", "cherry", "cranberry", "coconut" };
        List<IGrouping<char, string>> groups = ByInitial(words);
        groups.Add(words.Where(w => w.Length > 6).GroupBy(w => '*').Single());
        Console.WriteLine(string.Join(" ", groups.Select(g => g.Key + "" + g.Count())));

        ILookup<int, string> byLength = words.ToLookup(w => w.Length);
        List<IGrouping<int, string>> lengths = byLength.ToList();
        IEnumerable<IGrouping<int, string>> asSeq = byLength;
        Console.WriteLine(lengths.Count + " " + asSeq.Count() + " " + string.Join(",", byLength.OrderBy(g => g.Key).Select(g => g.Key)));

        bool flag = words.Length > 3;
        IGrouping<char, string> pick = flag ? groups[0] : words.GroupBy(w => w[1]).First();
        Console.WriteLine(pick.Key + " " + string.Join("/", pick));

        var dict = new Dictionary<char, IGrouping<char, string>>();
        foreach (var g in words.GroupBy(w => w[0])) dict[g.Key] = g;
        Console.WriteLine(string.Join(",", dict.Keys) + " " + dict['c'].Last());
    }
}
""";
            await RunTest(code);
        }

        /// <summary>
        /// A null-conditional access to a member whose <c>[Template]</c> is relative to the receiver
        /// (<c>IGrouping.Key</c> → <c>key()</c>, <c>ILookup.Count</c> → <c>count()</c>, the lookup
        /// indexer → <c>get({0})</c>) has to apply it to the receiver, as a plain <c>a.P</c> does. It
        /// emitted a bare <c>key()</c> and a non-existent <c>getItem</c>; and a string's <c>?[i]</c>
        /// yielded a one-character string rather than a char.
        /// </summary>
        [TestMethod]
        public async Task NullConditionalOverTemplatedMembersRunLikeNative()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        var xs = new[] { 1, 2, 3 };
        ILookup<bool, int> lk = xs.ToLookup(x => x > 1);
        ILookup<bool, int> none = null;
        Console.WriteLine(lk?.Contains(true) + "|" + none?.Contains(true) + "|" + lk?.Count + "|" + none?.Count + "|" + lk?[true].Count() + "|" + none?[true].Count());

        var g = xs.GroupBy(x => x % 2).FirstOrDefault();
        IGrouping<int, int> missing = xs.GroupBy(x => x).FirstOrDefault(x => x.Key > 10);
        Console.WriteLine(g?.Key + "|" + missing?.Key + "|" + (missing?.Key ?? -1));

        IOrderedEnumerable<int> o = xs.OrderBy(x => -x);
        IOrderedEnumerable<int> noOrder = null;
        Console.WriteLine(string.Join(",", o?.ThenBy(x => x)) + "|" + (noOrder?.ThenBy(x => x) == null));

        string s = "abc", n = null;
        Console.WriteLine((s?[1] + 1) + "|" + s?[2] + "|" + n?[0] + "|" + s?.Length + "|" + n?.Length);
    }
}
""";
            await RunTest(code);
        }

        /// <summary>
        /// A class compiled from source and marked <c>[IgnoreGeneric]</c>: its define is still generic,
        /// so everything that names it — and its reflection metadata, which named an unbound <c>T</c>
        /// — must agree.
        /// </summary>
        [TestMethod]
        public async Task SourceIgnoreGenericClassRunsAndReflectsLikeNative()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;
using Transpose;

[IgnoreGeneric]
public class Bag<T>
{
    public T Last;
    public List<T> Items { get; } = new List<T>();
    public void Add(T x) { Items.Add(x); Last = x; }
    public string Describe() => "Bag<" + typeof(T).Name + ">:" + Items.Count;
}

public class Program
{
    public static List<Bag<int>> Bags = new List<Bag<int>>();
    static Bag<string> Echo(Bag<string> b) => b;

    public static void Main()
    {
        var b = new Bag<int>(); b.Add(1); b.Add(2);
        Bags.Add(b);
        Console.WriteLine(b.Describe() + " " + b.Last + " " + Bags.Count);
        object o = b;
        Console.WriteLine((o is Bag<int>) + " " + Echo(new Bag<string>()).Describe());
        Console.WriteLine(new[] { b }.Length + " " + new List<Bag<int>> { b }.ToArray()[0].Items.Sum());
        Console.WriteLine(typeof(Bag<int>) == typeof(Bag<string>));

        Console.WriteLine(string.Join(",", typeof(Bag<int>).GetMethods()
            .Where(m => m.Name == "Add").Select(m => m.GetParameters()[0].ParameterType.Name)));
        Console.WriteLine(typeof(Bag<string>).GetField("Last").FieldType.Name + " "
            + typeof(Bag<string>).GetProperty("Items").PropertyType.Name);
    }
}
""";
            await RunTest(code, overrideRoslynCode: code + NativeAttributeStubs);
        }

        /// <summary>A generic method marked <c>[IgnoreGeneric]</c> is called with no threaded type
        /// arguments; an unmarked one still threads them.</summary>
        [TestMethod]
        public async Task IgnoreGenericMethodDropsItsTypeArguments()
        {
            var code = """
using System;
using System.Collections.Generic;
using Transpose;

public class Box
{
    public object Held;
    [IgnoreGeneric] public Box Put<T>(T item) { Held = item; return this; }
}

public class Program
{
    [IgnoreGeneric]
    public static string Show<T>(T x) => "show:" + x;

    [IgnoreGeneric]
    public static List<object> Wrap<T1, T2>(T1 a, T2 b) => new List<object> { a, b };

    public static string Named<T>(T x) => typeof(T).Name + ":" + x;

    public static void Main()
    {
        Console.WriteLine(Show<int>(3) + " " + Show("s") + " " + Named(4));
        Console.WriteLine(Wrap(1, "two").Count + " " + new Box().Put(5).Put("x").Held);
        Func<int, string> f = Show;
        Console.WriteLine(f(7));
    }
}
""";
            await RunTest(code, overrideRoslynCode: code + NativeAttributeStubs);

            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            StringAssert.Contains(result.Javascript, "Program.Show(3)");
            StringAssert.Contains(result.Javascript, "Program.Wrap(1, \"two\")");
            StringAssert.Contains(result.Javascript, ".Put(5)");
            StringAssert.Contains(result.Javascript, "Program.Named(System.Int32, 4)");
        }

        /// <summary>
        /// A binding library's own <c>[External, IgnoreGeneric]</c> type over a hand-written, non-generic
        /// JavaScript constructor — with a <c>[Name]</c> and without one (then the arity-suffixed name is
        /// the runtime name, as linq.js's <c>Grouping$2</c> is). Nothing native to compare against.
        /// </summary>
        [TestMethod]
        public async Task ExternalIgnoreGenericBindingIsReferencedByItsBareName()
        {
            var code = """
using System;
using System.Collections.Generic;
using System.Linq;
using Transpose;

[External, IgnoreGeneric, Name("JsBox")]
public class JsBox<T>
{
    public extern JsBox(T v);
    [Name("v")] public extern T V { get; }
}

[External, IgnoreGeneric]
public class Plain<T>
{
    public extern Plain(T v);
    [Name("v")] public extern T V { get; }
}

public class Program
{
    static T Pass<T>(T x) => x;
    public static List<Plain<int>> Plains;

    public static void Main()
    {
        Script.Write("globalThis.JsBox = function (v) { this.v = v; }; globalThis.Plain$1 = function (v) { this.v = v; };");
        var jb = new JsBox<int>(5);
        var boxes = new List<JsBox<int>> { jb, new JsBox<int>(6) }.ToArray();
        Console.WriteLine(jb.V + " " + boxes.Length + " " + boxes.Sum(b => b.V) + " " + Pass(jb).V);

        // Assigned here rather than by a static initializer, which would run before the Script.Write
        // above has defined the constructors.
        Plains = new List<Plain<int>>();
        Plains.Add(new Plain<int>(1));
        var pls = new[] { new Plain<string>("p") }.Select(x => x).ToList();
        object o = pls[0];
        Console.WriteLine(Plains[0].V + " " + pls[0].V + " " + (o is Plain<string>) + " " + (o is JsBox<int>));
        Console.WriteLine(typeof(Plain<int>) == typeof(Plain<string>));
        Console.WriteLine(Plains.GetType().Name);
    }
}
""";
            var output = await RunTest(code, skipRoslyn: true);
            Assert.AreEqual("5 2 11 5\n1 p True False\nTrue\nList`1", output.Replace("\r\n", "\n").Trim());

            var result = new RoslynTranslator().Translate(code);
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;
            StringAssert.Contains(js, "new JsBox(5)");
            StringAssert.Contains(js, "List$1(JsBox)");
            StringAssert.Contains(js, "Program.Pass(JsBox, jb)");
            StringAssert.Contains(js, "new Plain$1(1)");
            StringAssert.Contains(js, "List$1(Plain$1)");
            // Every instantiation is the one JS constructor, so typeof(Plain<int>) == typeof(Plain<string>)
            // above is true — unlike .NET, and the reason a native comparison is not possible here.
            Assert.IsFalse(Regex.IsMatch(js, @"\b(JsBox|Plain\$1)\(System\."),
                "an [External, IgnoreGeneric] binding must never be applied to type arguments\n" + js);
        }
    }
}
