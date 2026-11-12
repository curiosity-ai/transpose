using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests.Linq
{
    /// <summary>
    /// The LINQ result types bound to linq.js — <c>IGrouping&lt;,&gt;</c>, <c>Grouping&lt;,&gt;</c>,
    /// <c>ILookup&lt;,&gt;</c>, <c>IOrderedEnumerable&lt;&gt;</c>, … — are <c>[IgnoreGeneric]</c>: at run time
    /// they are plain constructors / <c>Transpose.definei</c> interfaces, not generic definitions. A
    /// reference to one must therefore be the bare arity-suffixed name. Applying it to type arguments
    /// (<c>System.Linq.IGrouping$2(System.String, System.Object)</c>) CALLS it without <c>new</c>: the
    /// interface's default ctor threw <c>this.$initialize is not a function</c> as soon as the
    /// reflection metadata of a method returning <c>IEnumerable&lt;IGrouping&lt;string, object&gt;&gt;</c>
    /// was read, and <c>GroupBy(…).ToArray()</c> passed <c>Grouping$2(…)</c> — which wrote
    /// <c>key</c>/<c>getSource</c> onto <c>System.Linq</c> and returned undefined — as the array's
    /// element type.
    /// </summary>
    [TestClass]
    public class LinqIgnoreGenericTypeRefTests : TranslatorTestBase
    {
        private const string Code = """
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class Item
{
    public string Name;
    public int V;
    public Item(string n, int v) { Name = n; V = v; }
}

public class Program
{
    public static IEnumerable<IGrouping<string, object>> Groups(object[] items)
        => items.GroupBy(i => ((Item)i).Name).ToArray();

    public static IOrderedEnumerable<Item> Ordered(IEnumerable<Item> items) => items.OrderBy(x => x.V);

    public static ILookup<string, Item> Lookup(IEnumerable<Item> items) => items.ToLookup(x => x.Name);

    public static void Main()
    {
        var items = new object[] { new Item("a", 3), new Item("b", 1), new Item("a", 2) };
        foreach (var g in Groups(items)) Console.WriteLine(g.Key + " " + g.Count());

        object first = Groups(items).First();
        Console.WriteLine(first is IGrouping<string, object>);

        var list = items.GroupBy(i => ((Item)i).Name).ToList();
        Console.WriteLine(list.Count + " " + list[1].Key);

        Console.WriteLine(Ordered(items.Cast<Item>()).ThenBy(x => x.Name).First().V);
        Console.WriteLine(Lookup(items.Cast<Item>())["a"].Count());

        // Reading the metadata of the methods above is what threw.
        var names = typeof(Program).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "Groups" || m.Name == "Ordered" || m.Name == "Lookup")
            .Select(m => m.Name + ":" + (m.ReturnType != null))
            .OrderBy(s => s);
        Console.WriteLine(string.Join(",", names));
    }
}
""";

        [TestMethod]
        public async Task IgnoreGenericLinqTypesRunLikeNative() => await RunTest(Code);

        [TestMethod]
        public void IgnoreGenericLinqTypesAreNeverApplied()
        {
            var result = new RoslynTranslator().Translate(Code);
            Assert.IsTrue(result.Success, "translation should succeed");
            var js = result.Javascript!;
            foreach (var t in new[] { "IGrouping$2(", "Grouping$2(", "ILookup$2(", "IOrderedEnumerable$1(" })
                Assert.IsFalse(js.Contains("System.Linq." + t) || js.Contains("Linq." + t),
                    $"an [IgnoreGeneric] type must be referenced by its bare name, never applied: found {t}\n" + js);
            Assert.IsTrue(js.Contains(".ToArray(System.Linq.Grouping$2)"), js);
        }
    }
}
