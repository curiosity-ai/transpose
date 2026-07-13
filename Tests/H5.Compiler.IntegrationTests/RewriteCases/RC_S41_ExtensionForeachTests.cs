using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Rewrite case S41 (extension-method foreach): the collection's GetEnumerator
    // is a public extension method. Now resolved by the frontend and emitted as a
    // static call, so the manual-loop lowering was removed. The Roslyn scripting
    // host cannot host extension methods in a wrapper class, so assert H5 output.
    [TestClass]
    public class RC_S41_ExtensionForeachTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task ExtensionForeach_SyncPath()
        {
            var code = """
using System;
using System.Collections.Generic;

public class Range
{
    public int From { get; }
    public int To { get; }
    public Range(int from, int to) { From = from; To = to; }
}

public static class Extensions
{
    public static IEnumerator<int> GetEnumerator(this Range range)
    {
        for (int i = range.From; i < range.To; i++)
            yield return i;
    }
}

public class Program
{
    public static void Main()
    {
        int sum = 0;
        foreach (var i in new Range(0, 5))
        {
            Console.WriteLine(i);
            sum += i;
        }
        Console.WriteLine("sum=" + sum);

        // nested + single-statement body (no block)
        foreach (var a in new Range(0, 2))
            foreach (var b in new Range(0, 2))
                Console.WriteLine(a + "," + b);
    }
}
""";
            var output = await RunTest(code, skipRoslyn: true);
            Assert.AreEqual("0\n1\n2\n3\n4\nsum=10\n0,0\n0,1\n1,0\n1,1", output);
        }

        [TestMethod]
        public async Task ExtensionForeach_AsyncPath()
        {
            // foreach with an await in the body forces the async emit path.
            var code = """
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class Range
{
    public int From { get; }
    public int To { get; }
    public Range(int from, int to) { From = from; To = to; }
}

public static class Extensions
{
    public static IEnumerator<int> GetEnumerator(this Range range)
    {
        for (int i = range.From; i < range.To; i++)
            yield return i;
    }
}

public class Program
{
    public static async Task Main()
    {
        foreach (var i in new Range(0, 3))
        {
            await Task.Delay(1);
            Console.WriteLine("got " + i);
        }
        Console.WriteLine("<<DONE>>");
    }
}
""";
            var output = await RunTest(code, waitForOutput: "<<DONE>>", skipRoslyn: true);
            StringAssert.Contains(output, "got 0");
            StringAssert.Contains(output, "got 1");
            StringAssert.Contains(output, "got 2");
        }
    }
}
