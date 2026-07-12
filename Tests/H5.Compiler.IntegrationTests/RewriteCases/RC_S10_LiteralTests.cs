using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace H5.Compiler.IntegrationTests.RewriteCases
{
    // Rewrite case S10 (binary literals / digit separators) — now handled natively
    // by the NRefactory tokenizer; these tests pin runtime values end-to-end.
    [TestClass]
    public class RC_S10_LiteralTests : IntegrationTestBase
    {
        [TestMethod]
        public async Task Literals_BinaryAndDigitSeparators()
        {
            var code = """
using System;

public class Program
{
    public static void Main()
    {
        Console.WriteLine(0b101);
        Console.WriteLine(0b1010_1100);
        Console.WriteLine(0b0);
        Console.WriteLine(1_000_000);
        Console.WriteLine(1_000_000_000_000L);
        Console.WriteLine(1_2.3_4);
        Console.WriteLine(0x_FF_FF);
        Console.WriteLine(0xDE_AD_BE_EF);
        Console.WriteLine(0b1111111111111111111111111111111111111111111111111111111111111111UL);
        Console.WriteLine(1_000.5f);
        Console.WriteLine(123_456m);
        Console.WriteLine(5.ToString());
        Console.WriteLine(1_000.ToString());
        Console.WriteLine(0b11 + 0b100);
        const int K = 0b1_0000_0000;
        Console.WriteLine(K);
    }
}
""";
            await RunTest(code);
        }
    }
}
