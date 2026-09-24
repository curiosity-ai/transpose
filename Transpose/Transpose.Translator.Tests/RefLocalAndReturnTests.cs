using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// <c>ref</c> locals, <c>ref</c> returns and delegates with <c>ref</c> parameters.
    /// <para>
    /// A <c>ref</c> expression used to collapse to the value of its operand, so a ref local held a copy and
    /// every write through it was lost without a diagnostic: <c>ref int first = ref values[0]; first = 10;</c>
    /// left <c>values[0]</c> at its old value. A reference is now a <i>cell</i> — an object whose <c>v</c>
    /// reads and writes the location itself — the same shape a <c>ref</c>/<c>out</c> parameter's holder
    /// already had (see <c>Emitter.RefCells.cs</c>). Every test here runs natively too and must match it.
    /// </para>
    /// <para>
    /// Separately, invoking a delegate whose signature has a <c>ref</c> parameter passed the bare value, so
    /// the target — compiled to read and write its parameter through a holder — threw "Cannot create
    /// property 'v' on number".
    /// </para>
    /// </summary>
    [TestClass]
    public class RefLocalAndReturnTests : TranslatorTestBase
    {
        /// <summary>The reported case: a ref local over an array element, and over another local.</summary>
        [TestMethod]
        public async Task WriteThroughARefLocalReachesTheLocation()
        {
            await RunTest(@"
using System;

public class Program
{
    public static void Main()
    {
        int[] values = { 1, 2, 3 };
        ref int first = ref values[0];
        first = 10;
        first += 5;
        first++;
        Console.WriteLine(string.Join("","", values) + "" "" + first);

        int local = 1;
        ref int alias = ref local;
        alias = 2;
        local = 3;
        Console.WriteLine(local + "" "" + alias);
    }
}");
        }

        /// <summary>A ref local over a field (instance and static), and ref reassignment, which rebinds
        /// the local without writing through the old reference.</summary>
        [TestMethod]
        public async Task RefLocalOverFieldsAndRefReassignment()
        {
            await RunTest(@"
using System;

public class Counter { public int Count; }

public class Program
{
    static int _total = 100;

    public static void Main()
    {
        var c = new Counter();
        ref int r = ref c.Count;
        r = 5;
        Console.WriteLine(c.Count);

        r = ref _total;
        r += 1;
        Console.WriteLine(c.Count + "" "" + _total);

        int a = 1, b = 2;
        ref int pick = ref a;
        pick = ref b;
        pick = 20;
        Console.WriteLine(a + "" "" + b);
    }
}");
        }

        /// <summary>A ref-returning method: bound to a ref local, assigned to directly, compound-assigned,
        /// incremented, and read as an ordinary value.</summary>
        [TestMethod]
        public async Task RefReturningMethod()
        {
            await RunTest(@"
using System;

public class Store
{
    private readonly int[] _items = { 1, 2, 3 };
    public ref int Find(int value)
    {
        for (int i = 0; i < _items.Length; i++)
            if (_items[i] == value) return ref _items[i];
        throw new InvalidOperationException(""not found"");
    }
    public ref int At(int i) => ref _items[i];
    public string Dump() => string.Join("","", _items);
}

public class Program
{
    public static void Main()
    {
        var s = new Store();
        ref int two = ref s.Find(2);
        two = 20;
        s.Find(3) = 30;
        s.At(0) += 5;
        s.At(0)++;
        int copy = s.At(1);
        copy = 99;
        Console.WriteLine(s.Dump() + "" "" + s.At(0) + "" "" + copy);
    }
}");
        }

        /// <summary>A ref-returning indexer and properties (instance, static, <c>ref readonly</c>) keep
        /// their ordinary read and write syntax — including assigning to an indexer that has no setter,
        /// which C# allows through the reference — and bind to a ref local.</summary>
        [TestMethod]
        public async Task RefReturningIndexerAndProperties()
        {
            await RunTest(@"
using System;

public class Buffer
{
    private readonly int[] _items = { 1, 2, 3 };
    private int _count = 7;
    public static int[] Shared = { 100, 200 };
    public ref int this[int i] => ref _items[i];
    public ref int Count => ref _count;
    public ref readonly int ReadOnlyCount => ref _count;
    public static ref int SharedFirst => ref Shared[0];
    public string Dump() => string.Join("","", _items) + "" count="" + _count;
}

public class Program
{
    public static void Main()
    {
        var buf = new Buffer();
        buf[0] = 11;
        buf[1] += 10;
        buf[2]++;
        ref int slot = ref buf[2];
        slot *= 2;
        Console.WriteLine(buf.Dump() + "" read="" + buf[0]);

        buf.Count = 8;
        ref int c = ref buf.Count;
        c++;
        ref readonly int ro = ref buf.ReadOnlyCount;
        buf.Count = 42;
        Console.WriteLine(buf.Count + "" "" + buf.ReadOnlyCount + "" "" + ro);

        Buffer.SharedFirst = 101;
        ref int sf = ref Buffer.SharedFirst;
        sf += 1;
        Console.WriteLine(string.Join("","", Buffer.Shared));
    }
}");
        }

        /// <summary>A ref-returning method that returns one of its <c>ref</c> parameters (through a ref
        /// conditional): the caller writes through the result after the call has returned, which has to
        /// land in the caller's variable.</summary>
        [TestMethod]
        public async Task RefReturnOfARefParameter()
        {
            await RunTest(@"
using System;

public class Program
{
    static ref int Pick(bool first, ref int a, ref int b) => ref first ? ref a : ref b;
    static void Bump(ref int x) => x += 10;

    public static void Main()
    {
        int a = 1, b = 2;
        Pick(false, ref a, ref b) = 99;
        ref int picked = ref Pick(true, ref a, ref b);
        picked = 50;
        Console.WriteLine(a + "" "" + b);

        ref int r = ref a;
        Bump(ref r);
        Console.WriteLine(a);
    }
}");
        }

        /// <summary>A struct reached by reference is the stored value itself — a field write lands in the
        /// array — while reading a ref-returning call into a plain local still copies it.</summary>
        [TestMethod]
        public async Task StructByReferenceAndByValue()
        {
            await RunTest(@"
using System;

public struct Point { public int X, Y; public override string ToString() => $""({X},{Y})""; }

public class Program
{
    static Point[] _points = { new Point { X = 1, Y = 2 } };
    static ref Point First() => ref _points[0];

    public static void Main()
    {
        ref Point p = ref First();
        p.X = 10;
        Point copy = First();
        copy.Y = 99;
        Point copy2 = p;
        copy2.X = -1;
        Console.WriteLine(_points[0] + "" "" + copy + "" "" + copy2);
    }
}");
        }

        /// <summary>A ref local declared in a loop body refers to that iteration's element; a
        /// ref-returning local function; and an out-of-range reference throws when it is taken, as .NET
        /// bounds-checks <c>ref arr[i]</c> at that point.</summary>
        [TestMethod]
        public async Task LoopsLocalFunctionsAndBoundsChecks()
        {
            await RunTest(@"
using System;

public class Program
{
    public static void Main()
    {
        int[] data = { 1, 2, 3 };
        for (int i = 0; i < data.Length; i++) { ref int e = ref data[i]; e *= e; }
        Console.WriteLine(string.Join("","", data));

        int[] arr = { 5, 6 };
        ref int Last() => ref arr[arr.Length - 1];
        Last() = 60;
        Console.WriteLine(string.Join("","", arr) + "" "" + Last());

        try { ref int bad = ref data[10]; Console.WriteLine(bad); }
        catch (IndexOutOfRangeException) { Console.WriteLine(""out of range""); }

        int[] none = null;
        try { ref int bad = ref none[0]; Console.WriteLine(bad); }
        catch (NullReferenceException) { Console.WriteLine(""null array""); }
    }
}");
        }

        /// <summary>A multi-dimensional array element and a <c>ref readonly</c> local over an <c>in</c>
        /// parameter (the generic cell path).</summary>
        [TestMethod]
        public async Task GenericCellPath()
        {
            await RunTest(@"
using System;

public class Program
{
    static int Read(in int value) { ref readonly int r = ref value; return r * 2; }

    public static void Main()
    {
        var grid = new int[2, 2];
        ref int cell = ref grid[1, 1];
        cell = 7;
        cell++;
        Console.WriteLine(grid[1, 1] + "" "" + Read(21));
    }
}");
        }

        /// <summary>Invoking a delegate with <c>ref</c> and <c>out</c> parameters — through the variable,
        /// through <c>Invoke</c>, and with a ref local as the argument — writes back to the caller.</summary>
        [TestMethod]
        public async Task DelegateWithRefParameters()
        {
            await RunTest(@"
using System;

public delegate void RefAction(ref int x);
public delegate bool TryParser(string s, out int value);

public class Program
{
    static void Increment(ref int x) => x++;
    static bool TryLength(string s, out int value) { value = s.Length; return value > 0; }

    public static void Main()
    {
        RefAction inc = Increment;
        int v = 1;
        inc(ref v);
        inc.Invoke(ref v);
        Console.WriteLine(v);

        int[] arr = { 10 };
        ref int r = ref arr[0];
        inc(ref r);
        Console.WriteLine(arr[0]);

        TryParser parse = TryLength;
        Console.WriteLine(parse(""abcd"", out var n) + "" "" + n);

        RefAction chain = Increment;
        chain += Increment;
        int w = 0;
        chain(ref w);
        Console.WriteLine(w);
    }
}");
        }
    }
}
