using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Rewrite case S9/R2 (LocalFunctionReplacer): hoisted delegate locals + lambdas
    [TestClass]
    public class RC_S9_LocalFunctionTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task LocalFunctions_SignatureVariations()
        {
            var code = """
using System;

public class Program
{
    public static void Main()
    {
        // default parameter values
        int WithDefault(int a, int b = 10) => a + b;
        Console.WriteLine(WithDefault(1));
        Console.WriteLine(WithDefault(1, 2));

        // params array (zero-arg expanded call is a known failure, split out below)
        int SumAll(params int[] xs)
        {
            int t = 0;
            foreach (var x in xs) t += x;
            return t;
        }
        Console.WriteLine(SumAll(1, 2, 3));

        // out and ref parameters
        bool TrySplit(int v, out int half, ref int count)
        {
            half = v / 2;
            count++;
            return v % 2 == 0;
        }
        int c = 0;
        Console.WriteLine(TrySplit(8, out var h, ref c) + "," + h + "," + c);

        // void local function
        void Log(string m) => Console.WriteLine("log:" + m);
        Log("done");
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        [Ignore("Known bug: zero-argument expanded call of a params local function (lowered to a delegate) does not wrap into an empty array in JS. See FINDINGS.md.")]
        public async Task LocalFunctions_ParamsZeroArgs_MinimalFailing()
        {
            var code = """
using System;

public class Program
{
    public static void Main()
    {
        int SumAll(params int[] xs)
        {
            int t = 0;
            foreach (var x in xs) t += x;
            return t;
        }
        Console.WriteLine(SumAll());
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        [Ignore("Known limitation: generic local functions are lowered to delegate-typed locals, but no delegate can be open-generic; call sites fail to resolve. See FINDINGS.md.")]
        public async Task LocalFunctions_Generic_MinimalFailing()
        {
            var code = """
using System;

public class Program
{
    public static void Main()
    {
        T Identity<T>(T v) => v;
        Console.WriteLine(Identity(5));
        Console.WriteLine(Identity("s"));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task LocalFunctions_RecursionAndForwardReferences()
        {
            var code = """
using System;

public class Program
{
    public static void Main()
    {
        // forward reference: call before declaration in source order
        Console.WriteLine(Even(10));

        bool Even(int n) => n == 0 ? true : Odd(n - 1);
        bool Odd(int n) => n == 0 ? false : Even(n - 1);   // mutual recursion

        Console.WriteLine(Odd(7));

        int Fact(int n) => n <= 1 ? 1 : n * Fact(n - 1);
        Console.WriteLine(Fact(6));
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task LocalFunctions_ClosuresAndMutation()
        {
            var code = """
using System;
using System.Collections.Generic;

public class Program
{
    public static void Main()
    {
        int counter = 0;
        void Bump() { counter++; }
        Bump(); Bump(); Bump();
        Console.WriteLine(counter);

        // capturing loop variable in local function used via delegate
        var fs = new List<Func<int>>();
        for (int i = 0; i < 3; i++)
        {
            int captured = i;
            int Get() => captured * 10;
            fs.Add(Get);
        }
        foreach (var f in fs) Console.WriteLine(f());

        // local function passed as delegate argument
        int Twice(int x) => x * 2;
        Console.WriteLine(Apply(Twice, 21));
    }

    private static int Apply(Func<int, int> f, int v) => f(v);
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task LocalFunctions_InsideOtherConstructs()
        {
            var code = """
using System;

public class Program
{
    public static void Main()
    {
        // inside a switch section
        switch (2)
        {
            case 2:
                int Double(int x) => x * 2;
                Console.WriteLine(Double(4));
                break;
        }

        // inside if/else blocks with same name in sibling scopes
        if (true)
        {
            int F() => 1;
            Console.WriteLine(F());
        }
        else { }
        {
            int F() => 2;
            Console.WriteLine(F());
        }

        // local function inside property accessor
        Console.WriteLine(Computed);

        // local function inside lambda
        Func<int, int> outer = x =>
        {
            int Add3(int y) => y + 3;
            return Add3(x);
        };
        Console.WriteLine(outer(10));
    }

    private static int Computed
    {
        get
        {
            int Sq(int v) => v * v;
            return Sq(6);
        }
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task LocalFunctions_AsyncVariations()
        {
            var code = """
using System;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        async Task<int> GetAsync()
        {
            await Task.Delay(1);
            return 5;
        }
        Console.WriteLine(await GetAsync());

        // async local function capturing state
        int baseVal = 100;
        async Task<int> AddAsync(int x)
        {
            await Task.Delay(1);
            return baseVal + x;
        }
        Console.WriteLine(await AddAsync(7));

        // sync local function used inside async local function
        int Mult(int a, int b) => a * b;
        async Task<int> MultAsync()
        {
            await Task.Delay(1);
            return Mult(6, 7);
        }
        Console.WriteLine(await MultAsync());

        Console.WriteLine("<<DONE>>");
    }
}
""";
            await RunTest(code, waitForOutput: "<<DONE>>");
        }
    }
}
