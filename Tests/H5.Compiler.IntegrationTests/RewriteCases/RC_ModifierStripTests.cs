using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Rewrite cases S24 (init accessors), S31 (required members),
    // S20 (static lambdas/local functions), S19 (readonly struct/members),
    // S42 (private protected)
    [TestClass]
    public class RC_ModifierStripTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task InitAccessors_ObjectInitializersAndCtors()
        {
            var code = """
using System;

public class Config
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; }
    public Config() { }
    public Config(int port) { Port = port; }     // init assignable in ctor
}

public class Program
{
    public static void Main()
    {
        var a = new Config { Host = "example.com", Port = 8080 };
        Console.WriteLine(a.Host + ":" + a.Port);

        var b = new Config();
        Console.WriteLine(b.Host + ":" + b.Port);

        var c = new Config(99);
        Console.WriteLine(c.Host + ":" + c.Port);

        // nested object initializers with init props
        var pair = new Pair { First = new Config { Port = 1 }, Second = new Config { Port = 2 } };
        Console.WriteLine(pair.First.Port + "," + pair.Second.Port);
    }
}

public class Pair
{
    public Config First { get; init; }
    public Config Second { get; init; }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task RequiredMembers_SetViaInitializer()
        {
            var code = """
using System;

public class User
{
    public required string Name { get; set; }
    public required int Age { get; init; }
    public string Note { get; set; } = "none";
}

public class Program
{
    public static void Main()
    {
        var u = new User { Name = "Ann", Age = 30 };
        Console.WriteLine(u.Name + "/" + u.Age + "/" + u.Note);
        var v = new User { Name = "Bob", Age = 41, Note = "vip" };
        Console.WriteLine(v.Name + "/" + v.Age + "/" + v.Note);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task StaticLambdasAndLocalFunctions()
        {
            var code = """
using System;
using System.Linq;

public class Program
{
    public static void Main()
    {
        Func<int, int> f = static x => x * 2;
        Console.WriteLine(f(21));

        Func<int, int, int> g = static (a, b) => a + b;
        Console.WriteLine(g(1, 2));

        static int Triple(int x) => x * 3;
        Console.WriteLine(Triple(5));

        static int Fib(int n) => n < 2 ? n : Fib(n - 1) + Fib(n - 2);   // recursive static local fn
        Console.WriteLine(Fib(10));

        var nums = new[] { 1, 2, 3 }.Select(static n => n * n);
        Console.WriteLine(string.Join(",", nums));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task ReadonlyStructAndMembers()
        {
            var code = """
using System;

public readonly struct Frac
{
    public readonly int Num;
    public readonly int Den;
    public Frac(int n, int d) { Num = n; Den = d; }
    public readonly double Value => (double)Num / Den;
    public readonly string Show() => Num + "/" + Den;
    public override readonly string ToString() => Show();
}

public struct Counter
{
    private int _n;
    public readonly int Peek() => _n;      // readonly member in mutable struct
    public void Inc() { _n++; }
}

public class Program
{
    public static void Main()
    {
        var f = new Frac(3, 4);
        Console.WriteLine(f.Value);
        Console.WriteLine(f.Show());
        Console.WriteLine(f.ToString());

        var c = new Counter();
        c.Inc();
        c.Inc();
        Console.WriteLine(c.Peek());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task PrivateProtected_AccessibleInDerived()
        {
            var code = """
using System;

public class Base
{
    private protected int Secret = 7;
    private protected virtual string Who() => "base";
}

public class Derived : Base
{
    public void Reveal()
    {
        Console.WriteLine(Secret);
        Console.WriteLine(Who());
    }
    private protected override string Who() => "derived";
}

public class Program
{
    public static void Main()
    {
        new Derived().Reveal();
    }
}
""";
            await RunTest(code);
        }
    }
}
