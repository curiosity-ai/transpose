using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Rewrite case S4 (exception filters lowered to a single catch + if/else chain)
    [TestClass]
    public class RC_S4_ExceptionFilterTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task Filters_SelectionOrderAndFallthrough()
        {
            var code = """
using System;

public class Program
{
    public static void Main()
    {
        // first matching filter wins; false filters fall through in order
        Run(1); Run(2); Run(3); Run(4);

        // unmatched filtered exception propagates to outer catch
        try
        {
            try { throw new Exception("outer-bound"); }
            catch (Exception e) when (e.Message == "nope") { Console.WriteLine("wrong"); }
        }
        catch (Exception e) { Console.WriteLine("outer:" + e.Message); }
    }

    private static void Run(int i)
    {
        try
        {
            throw new Exception("m" + i);
        }
        catch (Exception e) when (e.Message == "m1") { Console.WriteLine("one"); }
        catch (Exception e) when (e.Message == "m2" || e.Message == "m3") { Console.WriteLine("two-or-three:" + e.Message); }
        catch (Exception e) { Console.WriteLine("rest:" + e.Message); }
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Filters_TypedCatchesWithFiltersAndSideEffects()
        {
            var code = """
using System;

public class Program
{
    private static int _calls;

    private static bool Check(string tag, bool result)
    {
        _calls++;
        Console.WriteLine("filter:" + tag);
        return result;
    }

    public static void Main()
    {
        // filters evaluate in order until one is true; side effects observable
        try { throw new ArgumentException("bad-arg"); }
        catch (InvalidOperationException e) when (Check("iop", true)) { Console.WriteLine("wrong:" + e.Message); }
        catch (ArgumentException e) when (Check("arg-false", false)) { Console.WriteLine("wrong2:" + e.Message); }
        catch (ArgumentException e) when (Check("arg-true", e.Message == "bad-arg")) { Console.WriteLine("caught:" + e.Message); }
        Console.WriteLine("calls=" + _calls);

        // filter using members of a derived exception type
        try { throw new ArgumentNullException("p1"); }
        catch (ArgumentNullException e) when (e.ParamName == "p1") { Console.WriteLine("paramName ok"); }

        // filter that itself throws is treated as false
        try
        {
            try { throw new Exception("x"); }
            catch (Exception e) when (Boom()) { Console.WriteLine("never"); }
            catch (Exception e) when (e.Message == "x") { Console.WriteLine("after-throwing-filter"); }
        }
        catch { Console.WriteLine("should not reach outer"); }
    }

    private static bool Boom() { throw new InvalidOperationException("filter-boom"); }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Filters_NestedTryAndRethrow()
        {
            var code = """
using System;

public class Program
{
    public static void Main()
    {
        // rethrow inside a filtered catch
        try
        {
            try { throw new Exception("keep"); }
            catch (Exception e) when (e.Message == "keep")
            {
                Console.WriteLine("inner");
                throw;
            }
        }
        catch (Exception e) { Console.WriteLine("rethrown:" + e.Message); }

        // filters with finally interleaving
        try
        {
            try { throw new Exception("f"); }
            finally { Console.WriteLine("inner-finally"); }
        }
        catch (Exception e) when (e.Message == "f") { Console.WriteLine("outer-caught"); }

        // filter referencing captured local
        string want = "z";
        try { throw new Exception("z"); }
        catch (Exception e) when (e.Message == want) { Console.WriteLine("captured-ok"); }
    }
}
""";
            await RunTest(code);
        }

        [TestMethod]
        public async Task Filters_InAsyncMethods()
        {
            var code = """
using System;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main()
    {
        try
        {
            await FailAsync("a1");
        }
        catch (Exception e) when (e.Message == "a1")
        {
            Console.WriteLine("async-caught:" + e.Message);
        }

        // await inside the catch of a filtered catch
        try { await FailAsync("a2"); }
        catch (Exception e) when (e.Message == "a2")
        {
            await Task.Delay(1);
            Console.WriteLine("async-body:" + e.Message);
        }

        Console.WriteLine("<<DONE>>");
    }

    private static async Task FailAsync(string m)
    {
        await Task.Delay(1);
        throw new Exception(m);
    }
}
""";
            await RunTest(code, waitForOutput: "<<DONE>>");
        }
    }
}
