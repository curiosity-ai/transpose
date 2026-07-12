using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Rewrite case S3 (getter-only auto-properties + property initializers)
    [TestClass]
    public class RC_S3_AutoPropertyTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task AutoProps_InitializersAndOrdering()
        {
            var code = """
using System;

public class Thing
{
    public static int Sequence;

    public int A { get; } = Next("A");          // getter-only with initializer
    public int B { get; set; } = Next("B");     // settable with initializer
    public static int S { get; } = Next("S");   // static getter-only
    public int FromCtor { get; }

    public Thing()
    {
        FromCtor = Next("ctor");                 // assign getter-only in ctor
    }

    private static int Next(string tag)
    {
        Sequence++;
        Console.WriteLine("init:" + tag);
        return Sequence;
    }
}

public class Program
{
    public static void Main()
    {
        var t = new Thing();
        Console.WriteLine(t.A);
        Console.WriteLine(t.B);
        Console.WriteLine(t.FromCtor);
        Console.WriteLine(Thing.S);
        t.B = 100;
        Console.WriteLine(t.B);

        var t2 = new Thing();
        Console.WriteLine(t2.A);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task AutoProps_InheritanceAndComplexInitializers()
        {
            var code = """
using System;
using System.Collections.Generic;

public class Base
{
    public string Tag { get; } = "base";
    public virtual int V { get; set; } = 1;
    public List<int> Items { get; } = new List<int> { 1, 2, 3 };
}

public class Derived : Base
{
    public string OwnTag { get; } = "derived";
    public override int V { get; set; } = 2;
    public Dictionary<string, int> Map { get; } = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
    public int[] Arr { get; } = { 10, 20 };
    public Func<int, int> F { get; } = x => x * 3;
}

public class Program
{
    public static void Main()
    {
        var d = new Derived();
        Console.WriteLine(d.Tag);
        Console.WriteLine(d.OwnTag);
        Console.WriteLine(d.V);
        d.Items.Add(4);
        Console.WriteLine(string.Join(",", d.Items));
        Console.WriteLine(d.Map["a"] + "," + d.Map["b"]);
        Console.WriteLine(d.Arr[0] + "," + d.Arr[1]);
        Console.WriteLine(d.F(7));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task AutoProps_InStructsAndGenerics()
        {
            var code = """
using System;

public struct Pt
{
    public int X { get; }
    public int Y { get; set; }
    public Pt(int x, int y) { X = x; Y = y; }
}

public class Holder<T>
{
    public T Value { get; set; }
    public string Kind { get; } = "holder";
}

public class Program
{
    public static void Main()
    {
        var p = new Pt(1, 2);
        Console.WriteLine(p.X + "," + p.Y);
        p.Y = 5;
        Console.WriteLine(p.Y);

        var h = new Holder<string> { Value = "v" };
        Console.WriteLine(h.Value + "," + h.Kind);

        var hi = new Holder<int>();
        Console.WriteLine(hi.Value + "," + hi.Kind);
    }
}
""";
            await RunTest(code);
        }
    }
}
