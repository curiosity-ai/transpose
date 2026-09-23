using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// <c>++</c> and <c>--</c> where JavaScript's own operator is wrong or not even valid
    /// (<c>Emitter.IncDec.cs</c>):
    /// <list type="bullet">
    /// <item>on an <b>indexer element</b> — <c>counts[key]++</c> on a <c>Dictionary</c>, <c>list[i]--</c>,
    /// a class's own indexer — the emitter produced <c>coll.getItem(i)++</c>, a JavaScript syntax error
    /// that stopped the whole bundle from loading;</item>
    /// <item>on an <b><c>int</c> or <c>uint</c></b>, which wrap in unchecked C#: <c>int.MaxValue</c> plus
    /// one is <c>int.MinValue</c>, and <c>uint</c> zero minus one is <c>uint.MaxValue</c>, where a bare
    /// JavaScript <c>++</c>/<c>--</c> gave <c>2147483648</c> and <c>-1</c>.</item>
    /// </list>
    /// Every test runs natively too and must match.
    /// </summary>
    [TestClass]
    public class IncrementDecrementTests : TranslatorTestBase
    {
        /// <summary>Statement-form ++/-- on the indexers of the BCL collections and of a source type.</summary>
        [TestMethod]
        public async Task IndexerElementStatements()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;

public class Bag
{
    private readonly int[] _a = { 1, 2 };
    public int this[int i] { get => _a[i]; set => _a[i] = value; }
    public override string ToString() => string.Join("","", _a);
}

public class Program
{
    public static void Main()
    {
        var counts = new Dictionary<string, int> { [""a""] = 1 };
        counts[""a""]++;
        ++counts[""a""];
        counts[""a""]--;

        var list = new List<int> { 10, 20 };
        list[0]++;
        --list[1];

        var bag = new Bag();
        bag[0]++;
        bag[1]--;

        var longs = new List<long> { long.MaxValue - 1 };
        longs[0]++;
        var decimals = new List<decimal> { 1.5m };
        decimals[0]++;
        var bytes = new List<byte> { 255 };
        bytes[0]++;

        Console.WriteLine($""{counts[""a""]} {list[0]} {list[1]} {bag} {longs[0]} {decimals[0]} {bytes[0]}"");
    }
}");
        }

        /// <summary>Used as a value, a postfix form yields the old element and a prefix form the new
        /// one; the receiver and the index are evaluated once.</summary>
        [TestMethod]
        public async Task IndexerElementAsAValue()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;

public class Program
{
    static int _gets;
    static List<int> _list = new List<int> { 5, 6 };
    static List<int> Get() { _gets++; return _list; }
    static int _idx;
    static int Index() { _idx++; return 1; }

    public static void Main()
    {
        var counts = new Dictionary<string, int> { [""k""] = 1 };
        int old = counts[""k""]++;
        int now = ++counts[""k""];
        Console.WriteLine(old + "" "" + now + "" "" + counts[""k""]);

        int a = Get()[Index()]++;
        int b = --Get()[Index()];
        Console.WriteLine(a + "" "" + b + "" "" + _list[1] + "" "" + _gets + "" "" + _idx);
    }
}");
        }

        /// <summary>A null <c>int?</c> (or <c>long?</c>, <c>double?</c>) variable stays null under
        /// ++/--, where JavaScript's <c>null++</c> made it 1; a non-null one steps, with the postfix
        /// value being the old one.</summary>
        [TestMethod]
        public async Task NullableVariable()
        {
            await RunTest(@"
using System;

public class Program
{
    static int? _field;

    public static void Main()
    {
        int? n = null;
        n++;
        --n;
        int? m = 1;
        var old = m++;
        var now = ++m;
        long? l = null;
        l--;
        double? d = 1.5;
        d++;
        _field++;
        byte? b = 255;
        b++;
        Console.WriteLine($""{n == null} {m} {old} {now} {l == null} {d} {_field == null} {b}"");
    }
}");
        }

        /// <summary>A nullable element stays null.</summary>
        [TestMethod]
        public async Task NullableIndexerElement()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;

public class Program
{
    public static void Main()
    {
        var values = new List<int?> { null, 1 };
        values[0]++;
        values[1]++;
        Console.WriteLine((values[0] == null) + "" "" + values[1]);
    }
}");
        }

        /// <summary><c>int</c> and <c>uint</c> wrap at their limits, in a statement, in a <c>for</c>
        /// incrementor and as a value; the old/new value rules hold at the wrap.</summary>
        [TestMethod]
        public async Task ThirtyTwoBitIntegersWrap()
        {
            await RunTest(@"
using System;

public class Program
{
    static int _field = int.MaxValue;

    public static void Main()
    {
        int i = int.MaxValue;
        i++;
        int j = int.MinValue;
        j--;
        uint u = 0;
        u--;
        uint w = uint.MaxValue;
        w++;
        Console.WriteLine($""{i} {j} {u} {w}"");

        int p = int.MaxValue;
        int oldP = p++;
        int q = int.MaxValue;
        int newQ = ++q;
        uint r = 0;
        uint oldR = r--;
        Console.WriteLine($""{oldP} {p} {newQ} {q} {oldR} {r}"");

        _field++;
        Console.WriteLine(_field);

        int count = 0;
        for (int k = int.MaxValue - 2; k != int.MinValue + 1; k++) count++;
        Console.WriteLine(count);

        // Used inside a comparison, where the wrapped form's `| 0` must not bind to the operand.
        int n = 3, loops = 0;
        while (n-- > 1) loops++;
        int top = int.MaxValue;
        Console.WriteLine(loops + "" "" + n + "" "" + (top++ == int.MaxValue) + "" "" + (++top > 0));
    }
}");
        }

        /// <summary>A compound assignment to an element whose receiver or index has a side effect runs
        /// that side effect once, and updates the element it read — for an array and for an accessor
        /// indexer. It used to write <c>arr[k++] = arr[k++] + 10</c>.</summary>
        [TestMethod]
        public async Task CompoundAssignmentEvaluatesTheElementOnce()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;

public class Program
{
    static int _calls;
    static int[] _arr = { 1, 2, 3 };
    static int[] Get() { _calls++; return _arr; }

    public static void Main()
    {
        int[] arr = { 1, 2, 3 };
        int k = 0;
        arr[k++] += 10;
        Console.WriteLine(string.Join("","", arr) + "" "" + k);

        Get()[2] *= 5;
        Get()[0] -= 1;
        Console.WriteLine(string.Join("","", _arr) + "" "" + _calls);

        var list = new List<int> { 1, 2 };
        int j = 0;
        list[j++] += 7;
        Console.WriteLine(string.Join("","", list) + "" "" + j);

        var words = new[] { ""a"", ""x"" };
        int w = 0;
        words[w++] += ""b"";
        Console.WriteLine(words[0] + words[1] + w);

        var bytes = new byte[] { 250 };
        int b = 0;
        bytes[b++] += 10;
        Console.WriteLine(bytes[0] + "" "" + b);

        int m = 0;
        int result = (arr[m++] += 1);
        Console.WriteLine(result + "" "" + m);
    }
}");
        }

        /// <summary>The ordinary shapes are unchanged: loops, array elements, doubles, enums, chars.</summary>
        [TestMethod]
        public async Task OrdinaryIncrementsStillWork()
        {
            await RunTest(@"
using System;

public enum Level { Low, Mid, High }

public class Program
{
    public static void Main()
    {
        int[] arr = { 1, 2, 3 };
        int sum = 0;
        for (int i = 0; i < arr.Length; i++) sum += arr[i]++;
        int k = 0;
        arr[k++] += 10;
        double d = 1.5;
        d++;
        Level level = Level.Low;
        level++;
        char c = 'a';
        c++;
        short s = short.MaxValue;
        s++;
        Console.WriteLine($""{sum} {string.Join("","", arr)} {k} {d} {level} {c} {s}"");
    }
}");
        }
    }
}
