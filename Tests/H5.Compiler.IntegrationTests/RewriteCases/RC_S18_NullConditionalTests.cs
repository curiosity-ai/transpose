using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Rewrite case S18 (null-conditional chains flattened to conditionals)
    [TestClass]
    public class RC_S18_NullConditionalTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task NullConditional_SingleEvaluationAndVoidCalls()
        {
            var code = """
using System;

public class Counterparty
{
    public static int Fetches;
    public string Name = "n";
    public void Ping() { Console.WriteLine("ping"); }
    public int Add(int x) => x + 1;
}

public class Program
{
    private static Counterparty _cp = new Counterparty();

    private static Counterparty Get(bool exists)
    {
        Counterparty.Fetches++;
        return exists ? _cp : null;
    }

    public static void Main()
    {
        // receiver evaluated exactly once
        Console.WriteLine(Get(true)?.Name);
        Console.WriteLine(Counterparty.Fetches);
        Console.WriteLine(Get(false)?.Name ?? "none");
        Console.WriteLine(Counterparty.Fetches);

        // void method call through ?.
        Get(true)?.Ping();
        Get(false)?.Ping();
        Console.WriteLine(Counterparty.Fetches);

        // method result through ?.
        Console.WriteLine(Get(true)?.Add(1));
        Console.WriteLine(Get(false)?.Add(1) ?? -1);

        // ?. on delegate Invoke
        Action a = null;
        a?.Invoke();
        a = () => Console.WriteLine("invoked");
        a?.Invoke();

        Func<int, int> f = null;
        Console.WriteLine(f?.Invoke(5) ?? -5);
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task NullConditional_DeepChainsAndNullableResults()
        {
            var code = """
using System;
using System.Collections.Generic;

public class L3 { public int Val = 3; public int? MaybeVal = 33; }
public class L2 { public L3 Next; public List<L3> Items; }
public class L1 { public L2 Next; public L3 this[int i] => Next?.Items?[i]; }

public class Program
{
    public static void Main()
    {
        var full = new L1 { Next = new L2 { Next = new L3(), Items = new List<L3> { new L3 { Val = 9 } } } };
        var partial = new L1 { Next = new L2() };
        L1 none = null;

        // deep member chains
        Console.WriteLine(full.Next?.Next?.Val ?? -1);
        Console.WriteLine(partial.Next?.Next?.Val ?? -1);
        Console.WriteLine(none?.Next?.Next?.Val ?? -1);

        // element access in the middle of a chain
        Console.WriteLine(full.Next?.Items?[0]?.Val ?? -1);
        Console.WriteLine(partial.Next?.Items?[0]?.Val ?? -1);

        // custom indexer defined via ?. itself
        Console.WriteLine(full[0]?.Val ?? -1);

        // nullable value member at the end of a chain
        Console.WriteLine(full.Next?.Next?.MaybeVal ?? -2);
        int? maybe = partial.Next?.Next?.MaybeVal;
        Console.WriteLine(maybe.HasValue);

        // ?. under boolean logic and comparisons
        Console.WriteLine(full.Next?.Next?.Val > 2);
        Console.WriteLine(none?.Next?.Next?.Val > 2);
        Console.WriteLine((none?.Next?.Next?.Val).GetValueOrDefault());

        // in string interpolation and concatenation
        Console.WriteLine($"v={full.Next?.Next?.Val}, missing={none?.Next?.Next?.Val}");
    }
}
""";
            await RunTest(code);
        }
    }
}
