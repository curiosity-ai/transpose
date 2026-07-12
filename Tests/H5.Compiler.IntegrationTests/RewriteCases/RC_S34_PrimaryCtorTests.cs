using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Rewrite case S34 (primary constructors on classes/structs)
    [TestClass]
    public class RC_S34_PrimaryCtorTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task PrimaryCtor_CaptureSemanticsAndInitializers()
        {
            var code = """
using System;

public class Accumulator(int seed)
{
    private int _total = seed;          // parameter used in initializer
    public int Total => _total + Bias;  // captured parameter? no — field
    public int Bias { get; set; }

    public void Add(int v) { _total += v; }

    // parameter captured in a method body → becomes hidden field
    public int Seed() => seed;
}

public class Program
{
    public static void Main()
    {
        var acc = new Accumulator(10);
        Console.WriteLine(acc.Total);
        acc.Add(5);
        Console.WriteLine(acc.Total);
        Console.WriteLine(acc.Seed());
        acc.Bias = 100;
        Console.WriteLine(acc.Total);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task PrimaryCtor_BaseArgsStructsAndComposition()
        {
            var code = """
using System;

public class Base(string tag)
{
    public string Tag => tag;
}

public class Derived(string tag, int n) : Base(tag.ToUpper())
{
    public int N => n * 2;
}

public struct Pair(int a, int b)
{
    public int Sum => a + b;
    public int A { get; set; } = a;    // struct field initializer from param
}

public class Program
{
    public static void Main()
    {
        var d = new Derived("abc", 21);
        Console.WriteLine(d.Tag);
        Console.WriteLine(d.N);

        var p = new Pair(3, 4);
        Console.WriteLine(p.Sum);
        Console.WriteLine(p.A);
        p.A = 9;
        Console.WriteLine(p.A);

        // primary ctor param used in lambda inside member
        var h = new Hooked(5);
        Console.WriteLine(h.Make()(3));
    }
}

public class Hooked(int factor)
{
    public Func<int, int> Make() => x => x * factor;
}
""";
            await RunTest(code);
        }
    }
}
