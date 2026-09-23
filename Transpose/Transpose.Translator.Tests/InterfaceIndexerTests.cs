using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Transpose.Translator.Tests
{
    /// <summary>
    /// An indexer used through a user-defined interface. A call through a source interface goes to the
    /// interface-qualified accessor (<c>b.IBag$getItem(i)</c>, <c>b.IBag$setItem(i, v)</c>), which the
    /// implementer has to alias onto its own <c>getItem</c>/<c>setItem</c>. The alias table named the
    /// indexer <c>this[]</c> instead — not a JavaScript member — so every such read or write threw
    /// "IBag$setItem is not a function". Every test runs natively too and must match.
    /// </summary>
    [TestClass]
    public class InterfaceIndexerTests : TranslatorTestBase
    {
        /// <summary>Read, write, compound assignment and ++ through the interface, on an implicit
        /// implementation, a get-only one and an explicit one.</summary>
        [TestMethod]
        public async Task IndexerThroughASourceInterface()
        {
            await RunTest(@"
using System;
using System.Collections.Generic;

public interface IBag { int this[int i] { get; set; } }
public class Bag : IBag
{
    private readonly int[] _a = { 1, 2 };
    public int this[int i] { get => _a[i]; set => _a[i] = value; }
}

public interface ILookup { string this[string key] { get; } }
public class Lookup : ILookup { public string this[string key] => key + ""!""; }

public interface IExplicit { int this[int i] { get; set; } }
public class Explicit : IExplicit
{
    private int _v;
    int IExplicit.this[int i] { get => _v + i; set => _v = value; }
}

public interface IGrid<T> { T this[int row, int col] { get; set; } }
public class Grid<T> : IGrid<T>
{
    private readonly Dictionary<(int, int), T> _cells = new();
    public T this[int row, int col]
    {
        get => _cells.TryGetValue((row, col), out var v) ? v : default;
        set => _cells[(row, col)] = value;
    }
}

public class Program
{
    public static void Main()
    {
        IBag bag = new Bag();
        bag[0] = 5;
        bag[1] += 10;
        bag[1]++;
        Console.WriteLine(bag[0] + "" "" + bag[1]);

        ILookup lookup = new Lookup();
        Console.WriteLine(lookup[""x""]);

        IExplicit ex = new Explicit();
        ex[0] = 10;
        Console.WriteLine(ex[5]);

        IGrid<string> grid = new Grid<string>();
        grid[1, 2] = ""cell"";
        Console.WriteLine(grid[1, 2] + "" "" + (grid[0, 0] == null));
    }
}");
        }

        /// <summary>A ref-returning indexer and property through an interface: the value view and the
        /// cell accessor are both reachable, so reads, writes and a ref local work.</summary>
        [TestMethod]
        public async Task RefReturningMembersThroughAnInterface()
        {
            await RunTest(@"
using System;

public interface ISlots
{
    ref int this[int i] { get; }
    ref int Count { get; }
    ref int Slot(int i);
}

public class Slots : ISlots
{
    private readonly int[] _a = { 1, 2 };
    private int _count = 3;
    public ref int this[int i] => ref _a[i];
    public ref int Count => ref _count;
    public ref int Slot(int i) => ref _a[i];
    public override string ToString() => string.Join("","", _a) + "" count="" + _count;
}

public class Program
{
    public static void Main()
    {
        ISlots s = new Slots();
        s.Slot(0) = 10;
        s[1] = 20;
        ref int q = ref s[1];
        q++;
        s.Count = 4;
        ref int c = ref s.Count;
        c += 10;
        Console.WriteLine(s + "" "" + s[0] + "" "" + s.Count);
    }
}");
        }
    }
}
